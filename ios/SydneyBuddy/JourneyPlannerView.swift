import SwiftUI
import MapKit
import Combine

struct JourneyCoordinate: Codable {let lat:Double;let lng:Double;var coordinate:CLLocationCoordinate2D{.init(latitude:lat,longitude:lng)}}
struct JourneyStep: Codable, Identifiable {
    var id:String {"\(mode)|\(from)|\(departureTime)"}
    let mode:String;let line:String?;let headsign:String?;let from:String;let to:String
    let departureTime:String;let arrivalTime:String;let realtime:Bool;let stopCount:Int?
    let meters:Double;let seconds:Double;let waitSeconds:Double;let platform:String?;let agency:String?
    let geometry:[JourneyCoordinate]
    var title:String {transportLabel(mode)+(mode == "walk" || mode == "drive" ? "" : " · "+(line?.isEmpty == false ? line! : "路线号码未提供"))}
}
struct JourneyLeg: Codable, Identifiable {
    var id:Int {index};let index:Int;let mode:String;let from:String;let to:String
    let departureTime:String;let arrivalTime:String;let staySeconds:Double;let steps:[JourneyStep]
}
struct JourneyPlan: Codable {
    let departureTime:String;let arrivalTime:String;let travelSeconds:Double;let elapsedSeconds:Double
    let meters:Double;let legs:[JourneyLeg];let warnings:[String];let source:String
    var sections:[JourneyMapSection] {legs.flatMap(\.steps).map {JourneyMapSection(mode:$0.mode,coordinates:$0.geometry.map(\.coordinate),boarding:$0.from,alighting:$0.to,line:$0.line)}}
    var overview:OverviewRoute {OverviewRoute(coordinates:sections.flatMap(\.coordinates),meters:meters,seconds:travelSeconds,legSeconds:legs.map{$0.steps.reduce(0){$0+$1.seconds+$1.waitSeconds}})}
}
struct JourneyMapSection {
    let mode:String;let coordinates:[CLLocationCoordinate2D]
    var boarding:String="";var alighting:String="";var line:String?=nil
    var stopPins:[ItineraryPin] {
        guard mode != "walk",mode != "drive",coordinates.count>1 else{return []}
        let title=transportLabel(mode)+(line.map{" · "+$0} ?? "")
        return [ItineraryPin(coordinates.first!,badge:"●",title:"上车 · "+boarding,detail:title),ItineraryPin(coordinates.last!,badge:"●",title:"下车 · "+alighting,detail:title)]
    }
}
func transportLabel(_ mode:String)->String {switch mode {case "walk":return "步行";case "drive":return "开车";case "transit":return "大众运输";case "bus":return "公车";case "rail":return "火车";case "metro":return "地铁";case "tram":return "轻轨";case "ferry":return "渡轮";default:return "交通"}}
func transportSymbol(_ mode:String)->String {switch mode {case "walk":return "figure.walk";case "drive":return "car.fill";case "bus":return "bus.fill";case "ferry":return "ferry.fill";default:return "tram.fill"}}
func transportColor(_ mode:String)->Color {switch mode {case "walk":return Color(uiColor:.darkGray);case "drive":return Palette.green;default:return .blue}}
func journeyTime(_ raw:String)->String {
    let parser=ISO8601DateFormatter();parser.formatOptions=[.withInternetDateTime,.withFractionalSeconds]
    let date=parser.date(from:raw) ?? ISO8601DateFormatter().date(from:raw)
    guard let date else {return "时间未提供"}
    let formatter=DateFormatter();formatter.timeZone=TimeZone(identifier:"Australia/Sydney");formatter.dateFormat="HH:mm"
    return formatter.string(from:date)
}
@MainActor final class JourneyModel:ObservableObject {
    @Published var plan:JourneyPlan?
    @Published var busy=false
    @Published var error:String?
    private var generation=UUID()
    private var task:Task<Void,Never>?
    let session:URLSession
    let endpoint:URL
    init(session:URLSession = .shared,endpoint:URL = URL(string:"http://127.0.0.1:8787/journey/plan")!){self.session=session;self.endpoint=endpoint}
    func invalidate(){generation=UUID();task?.cancel();task=nil;busy=false;plan=nil;error=nil}
    func load(day:DayPlan,modes:[String],departure:Date,routingProvider:String = "open",mapProvider:String = "osm",contentSource:String = "open"){
        invalidate();busy=true;let token=generation
        let points:[[String:Any]] = [["name":day.start.name,"lat":day.start.lat,"lng":day.start.lng,"stayMinutes":0]] + day.stops.map{["name":$0.name,"lat":$0.lat,"lng":$0.lng,"stayMinutes":$0.visitMinutes]} + [["name":day.end.name,"lat":day.end.lat,"lng":day.end.lng,"stayMinutes":0]]
        task=Task {
            do {
                if routingProvider == "apple" {
                    let parsed=try await AppleJourneyProvider.plan(day:day,modes:modes,departure:departure)
                    try Task.checkCancellation();guard token==generation else{return}
                    plan=parsed;busy=false;return
                }
                var request=URLRequest(url:endpoint);request.httpMethod="POST";request.timeoutInterval=95
                request.setValue("application/json",forHTTPHeaderField:"Content-Type")
                request.httpBody=try JSONSerialization.data(withJSONObject:["points":points,"modes":modes,"departureTime":ISO8601DateFormatter().string(from:departure),"contentSource":contentSource,"routingProvider":routingProvider,"mapProvider":mapProvider])
                let (data,response)=try await session.data(for:request)
                guard (response as? HTTPURLResponse)?.statusCode==200 else {
                    let object=try? JSONSerialization.jsonObject(with:data) as? [String:Any]
                    throw NSError(domain:"Journey",code:1,userInfo:[NSLocalizedDescriptionKey:object?["error"] as? String ?? "交通规划服务暂不可用。"])
                }
                let parsed=try JSONDecoder().decode(JourneyPlan.self,from:data)
                try Task.checkCancellation();guard token==generation else{return}
                plan=parsed;busy=false
            } catch {guard token==generation,!Task.isCancelled else{return};busy=false;self.error=error.localizedDescription}
        }
    }
}

struct JourneyPlannerView:View {
    let day:DayPlan
    let tripID:String
    let provider:MapProvider
    let contentSource:String
    @State private var routingProvider:String
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model:JourneyModel
    @State private var modes:[String]
    @State private var departure:Date
    @State private var details=true
    @State private var navigation=false
    private let fixture:JourneyPlan?
    init(day:DayPlan,tripID:String,provider:MapProvider,departure:Date = Date(),fixture:JourneyPlan? = nil,contentSource:String = "open"){
        self.day=day;self.tripID=tripID;self.provider=provider;self.fixture=fixture;self.contentSource=contentSource
        _routingProvider=State(initialValue:provider == .google ? "google" : provider == .apple ? "apple" : "open")
        _model=StateObject(wrappedValue:JourneyModel());_modes=State(initialValue:fixture?.legs.map(\.mode) ?? Array(repeating:"walk",count:day.stops.count+1));_departure=State(initialValue:departure)
    }
    private var result:JourneyPlan? {fixture ?? model.plan}
    private var points:[(String,String)] {[("起",day.start.name)]+day.stops.enumerated().map{(String($0.offset+1),$0.element.name)}+[("终",day.end.name)]}
    var body:some View {
        ZStack(alignment:.top) {
            map.ignoresSafeArea()
            VStack(spacing:10) {
                HStack {Text("交通路线").font(.headline);Spacer();Button("关闭"){dismiss()}}.padding().background(.white,in:RoundedRectangle(cornerRadius:20))
                if fixture != nil {Text("测试资料 · TEST 公车非真实班次").font(.caption.bold()).padding(8).background(.yellow,in:Capsule())}
                if !details {
                    VStack(alignment:.leading,spacing:8){Text("起 · \(day.start.name)").bold();Text("↓ \(day.stops.count) 个停留点").font(.caption);Text("终 · \(day.end.name)").bold()}.padding().frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:20))
                }
                Spacer(minLength:0)
                panel
            }.padding(.horizontal,16).padding(.top,8).padding(.bottom,12)
        }.tint(Palette.green)
        .onChange(of:provider){model.invalidate();routingProvider=provider == .google ? "google" : provider == .apple ? "apple" : "open"}.onChange(of:routingProvider){model.invalidate()}.onChange(of:modes){model.invalidate()}.onChange(of:departure){model.invalidate()}
        .onDisappear{model.invalidate()}
        .fullScreenCover(isPresented:$navigation){WalkingNavigationView(day:day,tripID:tripID)}
    }
    @ViewBuilder private var map:some View {
        let overview=result?.overview
        switch provider {
        case .osm,.apple: NativeRouteMap(day:day,overview:overview,expanded:true,provider:provider,viewport:nil,onViewport:{_ in},sections:result?.sections ?? []).id(result?.arrivalTime ?? "unplanned")
        case .google:
            if SydneyBuddyApp.mapReady {GoogleRouteMap(day:day,overview:overview,expanded:true,viewport:nil,onViewport:{_ in},sections:result?.sections ?? []).id(result?.arrivalTime ?? "unplanned")}
            else {Palette.mint.overlay(Text("Google 地图未配置密钥，可切换 OpenStreetMap"))}
        case .schematic:SchematicMap(day:day)
        }
    }
    private var panel:some View {
        VStack(alignment:.leading,spacing:10) {
            HStack {
                Text(result.map{"\(Int(ceil($0.travelSeconds/60))) 分钟 · \(Int($0.meters)) 米"} ?? "选择每段交通方式").font(.headline)
                Spacer();Button(details ? "收起" : "路线详情"){details.toggle()}.font(.caption.bold())
            }
            if let result {Text(result.source).font(.caption2).foregroundStyle(Palette.muted)}
            if details {
                ScrollView {
                    VStack(alignment:.leading,spacing:12) {
                        if result == nil {
                        Picker("路线资料",selection:$routingProvider) {
                            if contentSource != "google" {Text("开源 · Valhalla / OTP").tag("open");Text("NSW 交通 + Valhalla").tag("nsw")}
                            if provider == .google {Text("Google Routes").tag("google")}
                            if provider == .apple && contentSource != "google" {Text("Apple Maps").tag("apple")}
                        }.font(.caption)
                        if routingProvider == "apple" && modes.contains("transit") {
                            Text("Apple 仅提供 App 内步行／开车路线。公交请选 NSW 资料，或在 Apple Maps 查询；不会自动改用其他来源。").font(.caption).foregroundStyle(Palette.muted)
                            Button("在 Apple Maps 查询首个公交区间") {AppleJourneyProvider.openTransit(day:day,modes:modes)}.font(.caption)
                        }
                        DatePicker("出发时间（悉尼）",selection:$departure).environment(\.timeZone,TimeZone(identifier:"Australia/Sydney")!).font(.caption)
                        HStack {ForEach(["walk","drive","transit"],id:\.self){mode in Button {modes=Array(repeating:mode,count:modes.count)} label:{Label(transportLabel(mode),systemImage:transportSymbol(mode)).font(.caption.bold()).padding(8).background(modes.allSatisfy{$0==mode} ? Palette.mint : Color.gray.opacity(0.08),in:Capsule())}.accessibilityValue(modes.allSatisfy{$0==mode} ? "已选择" : "未选择")}}
                        } else {HStack {Text("出发 \(journeyTime(result!.departureTime)) · 悉尼").font(.caption);Spacer();if fixture == nil {Button("调整交通"){model.invalidate()}.font(.caption)}}}
                        ForEach(points.indices,id:\.self){i in
                            HStack(alignment:.top,spacing:12){Text(points[i].0).font(.caption.bold()).frame(width:26,height:26).background(Palette.mint,in:Circle());Text(points[i].1).font(.subheadline.bold());Spacer()}
                            if i<modes.count {
                                if result == nil {Picker("第 \(i+1) 段交通",selection:$modes[i]) {ForEach(["walk","drive","transit"],id:\.self){Text(transportLabel($0)).tag($0)}}.pickerStyle(.segmented)}
                                if let leg=result?.legs.first(where:{$0.index==i}) {
                                    Text("\(journeyTime(leg.departureTime)) → \(journeyTime(leg.arrivalTime))"+(leg.staySeconds>0 ? " · 到站停留 \(Int(leg.staySeconds/60)) 分" : "")).font(.caption).foregroundStyle(Palette.muted)
                                    ForEach(Array(leg.steps.enumerated()),id:\.offset){_,step in stepRow(step)}
                                }
                            }
                        }
                        if let result {ForEach(result.warnings,id:\.self){Text($0).font(.caption2).foregroundStyle(Palette.muted)};Text("\(result.source) · 时间为悉尼当地时间").font(.caption2)}
                        else {Text("步行：灰色小圆点 · 开车：绿色细实线 · 公共交通：蓝色细实线。尚未查询时仅显示站点，不画直线；路线沿服务回传的道路线形。").font(.caption2).foregroundStyle(Palette.muted)}
                    }.padding(.vertical,4)
                }.frame(maxHeight:360)
            }
            if let error=model.error {Text(error).font(.caption).foregroundStyle(.red).accessibilityIdentifier("journey-error").fixedSize(horizontal:false,vertical:true)}
            HStack {
                Button(model.busy ? "正在查询交通…" : result == nil ? "规划交通路线" : "重新查询"){model.load(day:day,modes:modes,departure:departure,routingProvider:routingProvider,mapProvider:provider.rawValue,contentSource:contentSource)}.disabled(model.busy || fixture != nil || (routingProvider == "apple" && modes.contains("transit"))).buttonStyle(.borderedProminent)
                if model.busy {Button("取消"){model.invalidate()}}
                if fixture == nil,routingProvider == "open",result != nil,modes.allSatisfy({$0=="walk"}) {Button("开始步行导航"){navigation=true}}
                Spacer()
            }
            if provider == .osm {Link("© OpenStreetMap contributors",destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2)}
        }.padding(16).background(.white,in:RoundedRectangle(cornerRadius:24)).shadow(color:.black.opacity(0.1),radius:10)
    }
    private func stepRow(_ step:JourneyStep)->some View {
        VStack(alignment:.leading,spacing:5) {
            HStack {Label(step.title,systemImage:transportSymbol(step.mode)).font(.subheadline.bold()).foregroundStyle(transportColor(step.mode));Spacer();Text("\(Int(ceil(step.seconds/60))) 分").font(.caption.bold())}
            if step.waitSeconds>0 {Text("候车／衔接约 \(Int(ceil(step.waitSeconds/60))) 分").font(.caption)}
            Text("\(step.from) → \(step.to)").font(.caption)
            if let direction=step.headsign,!direction.isEmpty {Text("往 \(direction)").font(.caption)}
            if step.mode != "walk" && step.mode != "drive" {
                Text("\(journeyTime(step.departureTime)) 上车 · \(journeyTime(step.arrivalTime)) 下车"+(step.stopCount.map{" · \($0) 站"} ?? "")+(step.platform?.isEmpty == false ? " · 站台 \(step.platform!)" : "")).font(.caption)
                Text(step.realtime ? "实时更新" : "时刻表班次 · 非实时").font(.caption2).foregroundStyle(Palette.muted)
            }
            if step.geometry.count<2 {Text("此段未提供路线线形，请按站名确认。").font(.caption2).foregroundStyle(Palette.muted)}
        }.padding(10).background(transportColor(step.mode).opacity(0.08),in:RoundedRectangle(cornerRadius:12))
    }
}

/// MapKit exposes complete walking/driving geometry. Transit directions are handed to Apple Maps.
@MainActor enum AppleJourneyProvider {
    static func plan(day:DayPlan,modes:[String],departure:Date) async throws -> JourneyPlan {
        guard modes.count == day.stops.count+1, modes.allSatisfy({$0 == "walk" || $0 == "drive"}) else {
            throw NSError(domain:"Journey",code:2,userInfo:[NSLocalizedDescriptionKey:"Apple App 内路线仅支持步行／开车；公交请选择 NSW 资料或在 Apple Maps 查询。"])
        }
        let coordinates=[day.start.location]+day.stops.map(\.coordinate)+[day.end.location]
        let names=[day.start.name]+day.stops.map(\.name)+[day.end.name]
        let formatter=ISO8601DateFormatter()
        var cursor=departure
        var legs:[JourneyLeg]=[]
        for i in modes.indices {
            try Task.checkCancellation()
            let request=MKDirections.Request()
            request.source=MKMapItem(placemark:MKPlacemark(coordinate:coordinates[i]))
            request.destination=MKMapItem(placemark:MKPlacemark(coordinate:coordinates[i+1]))
            request.transportType=modes[i] == "walk" ? .walking : .automobile
            request.departureDate=cursor
            let directions=MKDirections(request:request)
            let response=try await withTaskCancellationHandler(operation:{try await directions.calculate()},onCancel:{directions.cancel()})
            try Task.checkCancellation()
            guard let route=response.routes.first, route.polyline.pointCount>1 else {throw URLError(.cannotParseResponse)}
            let points=route.polyline.points()
            let geometry=(0..<route.polyline.pointCount).map {index -> JourneyCoordinate in
                let c=points[index].coordinate;return JourneyCoordinate(lat:c.latitude,lng:c.longitude)
            }
            let arrival=cursor.addingTimeInterval(route.expectedTravelTime)
            let stay=i<day.stops.count ? Double(day.stops[i].visitMinutes*60) : 0
            let step=JourneyStep(mode:modes[i],line:nil,headsign:nil,from:names[i],to:names[i+1],departureTime:formatter.string(from:cursor),arrivalTime:formatter.string(from:arrival),realtime:false,stopCount:nil,meters:route.distance,seconds:route.expectedTravelTime,waitSeconds:0,platform:nil,agency:nil,geometry:geometry)
            legs.append(JourneyLeg(index:i,mode:modes[i],from:names[i],to:names[i+1],departureTime:formatter.string(from:cursor),arrivalTime:formatter.string(from:arrival),staySeconds:stay,steps:[step]))
            cursor=arrival.addingTimeInterval(stay)
        }
        return JourneyPlan(departureTime:formatter.string(from:departure),arrivalTime:formatter.string(from:cursor),travelSeconds:legs.flatMap(\.steps).reduce(0){$0+$1.seconds},elapsedSeconds:cursor.timeIntervalSince(departure),meters:legs.flatMap(\.steps).reduce(0){$0+$1.meters},legs:legs,warnings:["Apple 预估路线；重新查询才会更新。不修改原行程。"],source:"Apple Maps / MapKit")
    }
    static func openTransit(day:DayPlan,modes:[String]) {
        // Multi-stop transit is not exposed by MapKit; hand off the first selected itinerary leg honestly.
        let i=modes.firstIndex(of:"transit") ?? 0
        let points=[day.start.location]+day.stops.map(\.coordinate)+[day.end.location]
        let names=[day.start.name]+day.stops.map(\.name)+[day.end.name]
        let start=MKMapItem(placemark:MKPlacemark(coordinate:points[i]));start.name=names[i]
        let end=MKMapItem(placemark:MKPlacemark(coordinate:points[i+1]));end.name=names[i+1]
        MKMapItem.openMaps(with:[start,end],launchOptions:[MKLaunchOptionsDirectionsModeKey:MKLaunchOptionsDirectionsModeTransit])
    }
}
