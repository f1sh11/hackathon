import SwiftUI
import MapKit
import Combine

struct JourneyCoordinate: Codable {let lat:Double;let lng:Double;var coordinate:CLLocationCoordinate2D{.init(latitude:lat,longitude:lng)}}
struct WalkingDirection:Codable,Identifiable {var id:Int;var text:String;var meters:Double}
struct JourneyStep: Codable, Identifiable {
    var id:String {"\(mode)|\(from)|\(departureTime)"}
    let mode:String;let line:String?;let headsign:String?;let from:String;let to:String
    let departureTime:String;let arrivalTime:String;let realtime:Bool;let stopCount:Int?
    let meters:Double;let seconds:Double;let waitSeconds:Double;let platform:String?;let agency:String?
    let geometry:[JourneyCoordinate]
    var walkingDirections:[WalkingDirection]? = nil
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
func transportColor(_ mode:String)->Color {switch mode {case "walk":return Color(red:25.0/255,green:118.0/255,blue:210.0/255);case "drive":return Palette.green;default:return .blue}}
func journeyDate(_ raw:String)->Date? {
    let parser=ISO8601DateFormatter();parser.formatOptions=[.withInternetDateTime,.withFractionalSeconds]
    return parser.date(from:raw) ?? ISO8601DateFormatter().date(from:raw)
}
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
    init(session:URLSession = BackendConnection.session,endpoint:URL = BackendConnection.url("journey/plan")){self.session=session;self.endpoint=endpoint}
    func invalidate(){generation=UUID();task?.cancel();task=nil;busy=false;plan=nil;error=nil}
    func load(day:DayPlan,modes:[String],departure:Date,routingProvider:String = "open",mapProvider:String = "osm",contentSource:String = "open"){
        invalidate();busy=true;let token=generation
        let points:[[String:Any]] = [["name":day.start.name,"lat":day.start.lat,"lng":day.start.lng,"stayMinutes":0]] + day.stops.map{["name":$0.name,"lat":$0.lat,"lng":$0.lng,"stayMinutes":$0.visitMinutes]} + [["name":day.end.name,"lat":day.end.lat,"lng":day.end.lng,"stayMinutes":0]]
        task=Task {
            do {
                if routingProvider == "apple" {
                    let parsed=try await AppleJourneyProvider.plan(day:day,modes:modes,departure:departure,useSavedTransit:false)
                    try Task.checkCancellation();guard token==generation else{return}
                    plan=parsed;busy=false;return
                }
                var request=URLRequest(url:endpoint);request.httpMethod="POST";request.timeoutInterval=95
                request.setValue("application/json",forHTTPHeaderField:"Content-Type")
                request.httpBody=try JSONSerialization.data(withJSONObject:["points":points,"modes":modes,"departureTime":ISO8601DateFormatter().string(from:departure),"contentSource":contentSource,"routingProvider":routingProvider,"mapProvider":mapProvider])
                let (data,response)=try await session.data(for:BackendConnection.authorized(request))
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
    @Environment(\.locale) private var interfaceLocale

    let day:DayPlan
    let tripID:String
    let provider:MapProvider
    let contentSource:String
    private let onPlan:((JourneyPlan)->Void)?
    @State private var routingProvider:String
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model:JourneyModel
    @State private var modes:[String]
    @State private var departure:Date
    @State private var details=true
    @State private var navigation=false
    private let fixture:JourneyPlan?
    init(day:DayPlan,tripID:String,provider:MapProvider,departure:Date = Date(),fixture:JourneyPlan? = nil,contentSource:String = "open",onPlan:((JourneyPlan)->Void)? = nil){
        self.day=day;self.tripID=tripID;self.provider=provider;self.fixture=fixture;self.contentSource=contentSource;self.onPlan=onPlan
        _routingProvider=State(initialValue:provider == .google ? "google" : provider == .apple ? "apple" : "open")
        _model=StateObject(wrappedValue:JourneyModel());_modes=State(initialValue:fixture?.legs.map(\.mode) ?? (provider == .apple ? day.transportModes:Array(repeating:"walk",count:day.stops.count+1)));_departure=State(initialValue:departure)
    }
    private var result:JourneyPlan? {fixture ?? model.plan}
    private var points:[(String,String)] {[("起",day.start.name)]+day.stops.enumerated().map{(String($0.offset+1),$0.element.name)}+[("终",day.end.name)]}
    var body:some View {
        let _=interfaceLocale
        ZStack(alignment:.top) {
            map.ignoresSafeArea()
            VStack(spacing:10) {
                HStack {Text(uiText("交通路线")).font(.headline);Spacer();Button(uiText("关闭")){dismiss()}}.padding().background(.white,in:RoundedRectangle(cornerRadius:20))
                if fixture != nil {Text(uiText("测试资料 · TEST 公车非真实班次")).font(.caption.bold()).padding(8).background(.yellow,in:Capsule())}
                if !details {
                    VStack(alignment:.leading,spacing:8){Text(uiText("起 · \(day.start.name)")).bold();Text(uiText("↓ \(day.stops.count) 个停留点")).font(.caption);Text(uiText("终 · \(day.end.name)")).bold()}.padding().frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:20))
                }
                Spacer(minLength:0)
                panel
            }.padding(.horizontal,16).padding(.top,8).padding(.bottom,12)
        }.tint(Palette.green)
        .onChange(of:provider){model.invalidate();routingProvider=provider == .google ? "google" : provider == .apple ? "apple" : "open"}.onChange(of:routingProvider){model.invalidate()}.onChange(of:modes){model.invalidate()}.onChange(of:departure){model.invalidate()}
        .onReceive(model.$plan){newPlan in if let newPlan {onPlan?(newPlan)}}
        .onDisappear{model.invalidate()}
        .fullScreenCover(isPresented:$navigation){WalkingNavigationView(day:day,tripID:tripID)}
    }
    @ViewBuilder private var map:some View {
        let overview=result?.overview
        switch provider {
        case .osm,.apple: NativeRouteMap(day:day,overview:overview,expanded:true,provider:provider,viewport:nil,onViewport:{_ in},sections:result?.sections ?? []).id(result?.arrivalTime ?? "unplanned")
        case .google:
            if SydneyBuddyApp.mapReady {GoogleRouteMap(day:day,overview:overview,expanded:true,viewport:nil,onViewport:{_ in},sections:result?.sections ?? []).id(result?.arrivalTime ?? "unplanned")}
            else {Palette.mint.overlay(Text(uiText("Google 地图未配置密钥，可切换 OpenStreetMap")))}
        case .schematic:SchematicMap(day:day)
        }
    }
    private var panel:some View {
        VStack(alignment:.leading,spacing:10) {
            HStack {
                Text(uiText(result.map{"\(Int(ceil($0.travelSeconds/60))) 分钟 · \(Int($0.meters)) 米"} ?? "选择每段交通方式")).font(.headline)
                Spacer();Button(uiText(details ? "收起" : "路线详情")){details.toggle()}.font(.caption.bold())
            }
            if let result {Text(uiText(result.source)).font(.caption2).foregroundStyle(Palette.muted)}
            if details {
                ScrollView {
                    VStack(alignment:.leading,spacing:12) {
                        if result == nil {
                        Picker(uiText("路线资料"),selection:$routingProvider) {
                            if contentSource != "google" {Text(uiText("开源 · Valhalla / OTP")).tag("open");Text(uiText("NSW 交通 + Valhalla")).tag("nsw")}
                            if provider == .google {Text(uiText("Google Routes")).tag("google")}
                            if provider == .apple && contentSource != "google" {Text(uiText("Apple Maps")).tag("apple")}
                        }.font(.caption)
                        if routingProvider == "apple" && modes.contains("transit") {
                            Text(uiText("步行使用 Apple，公共交通按所选出发时间查询 NSW 班次，路线仍在 App 内显示。")).font(.caption).foregroundStyle(Palette.muted)
                        }
                        DatePicker(uiText("出发时间（悉尼）"),selection:$departure).environment(\.timeZone,TimeZone(identifier:"Australia/Sydney")!).font(.caption)
                        HStack {ForEach(["walk","drive","transit"],id:\.self){mode in Button {modes=Array(repeating:mode,count:modes.count)} label:{Label(uiText(transportLabel(mode)),systemImage:transportSymbol(mode)).font(.caption.bold()).padding(8).background(modes.allSatisfy{$0==mode} ? Palette.mint : Color.gray.opacity(0.08),in:Capsule())}.accessibilityValue(uiText(modes.allSatisfy{$0==mode} ? "已选择" : "未选择"))}}
                        } else {HStack {Text(uiText("出发 \(journeyTime(result!.departureTime)) · 悉尼")).font(.caption);Spacer();if fixture == nil {Button(uiText("调整交通")){model.invalidate()}.font(.caption)}}}
                        ForEach(points.indices,id:\.self){i in
                            HStack(alignment:.top,spacing:12){Text(uiText(points[i].0)).font(.caption.bold()).frame(width:26,height:26).background(Palette.mint,in:Circle());Text(uiText(points[i].1)).font(.subheadline.bold());Spacer()}
                            if i<modes.count {
                                if result == nil {Picker(uiText("第 \(i+1) 段交通"),selection:$modes[i]) {ForEach(["walk","drive","transit"],id:\.self){Text(uiText(transportLabel($0))).tag($0)}}.pickerStyle(.segmented)}
                                if let leg=result?.legs.first(where:{$0.index==i}) {
                                    Text(uiText("\(journeyTime(leg.departureTime)) → \(journeyTime(leg.arrivalTime))"+(leg.staySeconds>0 ? " · 到站停留 \(Int(leg.staySeconds/60)) 分" : ""))).font(.caption).foregroundStyle(Palette.muted)
                                    ForEach(Array(leg.steps.enumerated()),id:\.offset){_,step in stepRow(step)}
                                }
                            }
                        }
                        if let result {ForEach(result.warnings,id:\.self){Text(uiText($0)).font(.caption2).foregroundStyle(Palette.muted)};Text(uiText("\(result.source) · 时间为悉尼当地时间")).font(.caption2)}
                        else {Text(uiText("步行：蓝色密集小圆点 · 开车：绿色细实线 · 公共交通：蓝色细实线。尚未查询时仅显示站点，不画直线；路线沿服务回传的道路线形。")).font(.caption2).foregroundStyle(Palette.muted)}
                    }.padding(.vertical,4)
                }.frame(maxHeight:360)
            }
            if let error=model.error {Text(uiText(error)).font(.caption).foregroundStyle(.red).accessibilityIdentifier("journey-error").fixedSize(horizontal:false,vertical:true)}
            HStack {
                Button(uiText(model.busy ? "正在查询交通…" : result == nil ? "规划交通路线" : "重新查询")){model.load(day:day,modes:modes,departure:departure,routingProvider:routingProvider,mapProvider:provider.rawValue,contentSource:contentSource)}.disabled(model.busy || fixture != nil).buttonStyle(.borderedProminent)
                if model.busy {Button(uiText("取消")){model.invalidate()}}
                if fixture == nil,routingProvider == "open",result != nil,modes.allSatisfy({$0=="walk"}) {Button(uiText("开始步行导航")){navigation=true}}
                Spacer()
            }
            if provider == .osm {Link(uiText("© OpenStreetMap contributors"),destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2)}
        }.padding(16).background(.white,in:RoundedRectangle(cornerRadius:24)).shadow(color:.black.opacity(0.1),radius:10)
    }
    private func stepRow(_ step:JourneyStep)->some View {
        VStack(alignment:.leading,spacing:5) {
            HStack {Label(uiText(step.title),systemImage:transportSymbol(step.mode)).font(.subheadline.bold()).foregroundStyle(transportColor(step.mode));Spacer();Text(uiText("\(Int(ceil(step.seconds/60))) 分")).font(.caption.bold())}
            if step.waitSeconds>0 {Text(uiText("候车／衔接约 \(Int(ceil(step.waitSeconds/60))) 分")).font(.caption)}
            Text(uiText("\(step.from) → \(step.to)")).font(.caption)
            if let direction=step.headsign,!direction.isEmpty {Text(uiText("往 \(direction)")).font(.caption)}
            if step.mode != "walk" && step.mode != "drive" {
                Text(uiText("\(journeyTime(step.departureTime)) 上车 · \(journeyTime(step.arrivalTime)) 下车"+(step.stopCount.map{" · \($0) 站"} ?? "")+(step.platform?.isEmpty == false ? " · 站台 \(step.platform!)" : ""))).font(.caption)
                Text(uiText(step.realtime ? "实时更新" : "时刻表班次 · 非实时")).font(.caption2).foregroundStyle(Palette.muted)
            }
            if step.geometry.count<2 {Text(uiText("此段未提供路线线形，请按站名确认。")).font(.caption2).foregroundStyle(Palette.muted)}
        }.padding(10).background(transportColor(step.mode).opacity(0.08),in:RoundedRectangle(cornerRadius:12))
    }
}

/// Apple supplies road geometry; NSW supplies in-app transit. Saved plans and fresh queries stay separate.
@MainActor enum AppleJourneyProvider {
    static func transitSteps(from:Coordinate,to:Coordinate,departure:Date) async throws -> [JourneyStep] {
        var request=URLRequest(url:BackendConnection.url("journey/plan"));request.httpMethod="POST";request.timeoutInterval=95;request.setValue("application/json",forHTTPHeaderField:"Content-Type")
        request.httpBody=try JSONSerialization.data(withJSONObject:["points":[["name":from.name,"lat":from.lat,"lng":from.lng,"stayMinutes":0],["name":to.name,"lat":to.lat,"lng":to.lng,"stayMinutes":0]],"modes":["transit"],"departureTime":ISO8601DateFormatter().string(from:departure),"routingProvider":"nsw","mapProvider":"apple","contentSource":"open"])
        let (data,response)=try await BackendConnection.data(for:request)
        guard (response as? HTTPURLResponse)?.statusCode==200 else{let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any];throw NSError(domain:"Journey",code:5,userInfo:[NSLocalizedDescriptionKey:raw?["error"] as? String ?? "NSW 班次暂时无法核实，已保留原安排。"])}
        let result=try JSONDecoder().decode(JourneyPlan.self,from:data)
        guard result.legs.count==1,!result.legs[0].steps.isEmpty else{throw URLError(.cannotParseResponse)}
        return result.legs[0].steps
    }
    static func plan(day:DayPlan,modes:[String],departure:Date,useSavedTransit:Bool=true,transitLookup:(Coordinate,Coordinate,Date) async throws -> [JourneyStep] = transitSteps) async throws -> JourneyPlan {
        guard modes.count == day.stops.count+1, modes.allSatisfy({$0 == "walk" || $0 == "drive" || $0 == "transit"}) else {
            throw NSError(domain:"Journey",code:2,userInfo:[NSLocalizedDescriptionKey:"Apple App 内路线仅支持步行／开车；公交请选择 NSW 资料或在 Apple Maps 查询。"])
        }
        let coordinates=[day.start.location]+day.stops.map(\.coordinate)+[day.end.location]
        let names=[day.start.name]+day.stops.map(\.name)+[day.end.name]
        let formatter=ISO8601DateFormatter()
        var cursor=departure
        let verified=useSavedTransit && day.legs.count==modes.count && day.legs.allSatisfy{$0.source.contains("Apple") || $0.source.contains("Transport for NSW")}
        let calendar=Calendar(identifier:.gregorian)
        func scheduleTime(_ time:String)->Date? {
            let f=DateFormatter();f.timeZone=TimeZone(identifier:"Australia/Sydney");f.dateFormat="yyyy-MM-dd HH:mm";return f.date(from:day.date+" "+time)
        }
        func afterStop(_ arrival:Date,_ index:Int,_ stay:Double)->Date {
            guard verified else{return arrival.addingTimeInterval(stay)}
            let planned=index<day.stops.count ? scheduleTime(day.stops[index].departure):nil
            return max(arrival.addingTimeInterval(stay),planned ?? arrival).addingTimeInterval(index<day.stops.count ? 900:0)
        }
        _ = calendar
        var legs:[JourneyLeg]=[]
        for i in modes.indices {
            try Task.checkCancellation()
            if modes[i]=="transit" {
                let steps:[JourneyStep];let start:String;let end:String
                if useSavedTransit {
                    guard day.legs.indices.contains(i),let saved=day.legs[i].steps,!saved.isEmpty,let begins=day.legs[i].departureTime,let ends=day.legs[i].arrivalTime,let at=journeyDate(begins),at.timeIntervalSince(cursor)>=(-60) else{throw NSError(domain:"Journey",code:3,userInfo:[NSLocalizedDescriptionKey:"这段保存的 NSW 班次已无法衔接，请按新的出发时间刷新交通。原行程仍保留。"])}
                    steps=saved;start=begins;end=ends
                }else {
                    steps=try await transitLookup(Coordinate(name:names[i],lat:coordinates[i].latitude,lng:coordinates[i].longitude),Coordinate(name:names[i+1],lat:coordinates[i+1].latitude,lng:coordinates[i+1].longitude),cursor)
                    guard let first=steps.first,let last=steps.last,let at=journeyDate(first.departureTime),at.timeIntervalSince(cursor)>=(-60) else{throw NSError(domain:"Journey",code:6,userInfo:[NSLocalizedDescriptionKey:"最新班次无法衔接，未使用旧班次替代。请调整出发时间。"])}
                    start=formatter.string(from:cursor);end=last.arrivalTime
                }
                guard let arrival=journeyDate(end),arrival>=cursor else{throw URLError(.cannotParseResponse)}
                let stay=i<day.stops.count ? Double(day.stops[i].visitMinutes*60):0
                legs.append(JourneyLeg(index:i,mode:"transit",from:names[i],to:names[i+1],departureTime:start,arrivalTime:end,staySeconds:stay,steps:steps))
                cursor=afterStop(arrival,i,stay);continue
            }
            if verified{cursor=cursor.addingTimeInterval(600)}
            if CLLocation(latitude:coordinates[i].latitude,longitude:coordinates[i].longitude).distance(from:CLLocation(latitude:coordinates[i+1].latitude,longitude:coordinates[i+1].longitude))<1 {
                let stay=i<day.stops.count ? Double(day.stops[i].visitMinutes*60):0
                let geometry=[coordinates[i],coordinates[i+1]].map{JourneyCoordinate(lat:$0.latitude,lng:$0.longitude)}
                let step=JourneyStep(mode:modes[i],line:nil,headsign:nil,from:names[i],to:names[i+1],departureTime:formatter.string(from:cursor),arrivalTime:formatter.string(from:cursor),realtime:false,stopCount:nil,meters:0,seconds:0,waitSeconds:0,platform:nil,agency:nil,geometry:geometry,walkingDirections:[WalkingDirection(id:0,text:"同一位置，无需步行",meters:0)])
                legs.append(JourneyLeg(index:i,mode:modes[i],from:names[i],to:names[i+1],departureTime:formatter.string(from:cursor),arrivalTime:formatter.string(from:cursor),staySeconds:stay,steps:[step]));cursor=afterStop(cursor,i,stay);continue
            }
            let route:MKRoute
            if modes[i] == "walk" {
                route=try await AppleWalkingRoads.shared.route(Coordinate(name:names[i],lat:coordinates[i].latitude,lng:coordinates[i].longitude),Coordinate(name:names[i+1],lat:coordinates[i+1].latitude,lng:coordinates[i+1].longitude))
            }else{
            let request=MKDirections.Request()
            request.source=MKMapItem(placemark:MKPlacemark(coordinate:coordinates[i]))
            request.destination=MKMapItem(placemark:MKPlacemark(coordinate:coordinates[i+1]))
            request.transportType=modes[i] == "walk" ? .walking : .automobile
            request.departureDate=cursor
            let directions=MKDirections(request:request)
            let timeout=Task{@MainActor in try await Task.sleep(nanoseconds:20_000_000_000);directions.cancel()}
            let response:MKDirections.Response
            do {response=try await withTaskCancellationHandler(operation:{try await directions.calculate()},onCancel:{directions.cancel()});timeout.cancel()}
            catch {timeout.cancel();throw error}
            try Task.checkCancellation()
            guard let found=response.routes.first, found.polyline.pointCount>1 else {throw URLError(.cannotParseResponse)}
            route=found
            }
            let points=route.polyline.points()
            let geometry=(0..<route.polyline.pointCount).map {index -> JourneyCoordinate in
                let c=points[index].coordinate;return JourneyCoordinate(lat:c.latitude,lng:c.longitude)
            }
            let arrival=cursor.addingTimeInterval(route.expectedTravelTime)
            let stay=i<day.stops.count ? Double(day.stops[i].visitMinutes*60) : 0
            let instructions=route.steps.enumerated().filter{!$0.element.instructions.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty}.map{WalkingDirection(id:$0.offset,text:$0.element.instructions,meters:$0.element.distance)}
            let step=JourneyStep(mode:modes[i],line:nil,headsign:nil,from:names[i],to:names[i+1],departureTime:formatter.string(from:cursor),arrivalTime:formatter.string(from:arrival),realtime:false,stopCount:nil,meters:route.distance,seconds:route.expectedTravelTime,waitSeconds:0,platform:nil,agency:nil,geometry:geometry,walkingDirections:instructions)
            legs.append(JourneyLeg(index:i,mode:modes[i],from:names[i],to:names[i+1],departureTime:formatter.string(from:cursor),arrivalTime:formatter.string(from:arrival),staySeconds:stay,steps:[step]))
            cursor=afterStop(verified ? cursor.addingTimeInterval(ceil(route.expectedTravelTime/60)*60):arrival,i,stay)
        }
        return JourneyPlan(departureTime:formatter.string(from:departure),arrivalTime:formatter.string(from:cursor),travelSeconds:legs.flatMap(\.steps).reduce(0){$0+$1.seconds+$1.waitSeconds},elapsedSeconds:cursor.timeIntervalSince(departure),meters:legs.flatMap(\.steps).reduce(0){$0+$1.meters},legs:legs,warnings:["Apple 预估路线；重新查询才会更新。不修改原行程。"],source:modes.contains("transit") ? "Apple Maps / Transport for NSW":"Apple Maps / MapKit")
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


/// Read-only Apple walking preview: no external-app handoff and no live-navigation claim.
struct AppleTripWalkingView:View {
    @Environment(\.locale) private var interfaceLocale

    let trip:Trip
    private let initialDay:Int
    private let initialPlan:JourneyPlan?
    private let onPlan:((JourneyPlan,DayPlan)->Void)?
    @EnvironmentObject private var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var overview=ItineraryOverview()
    @State private var dayIndex:Int
    @State private var navigating=false
    @State private var choosingTransport=false
    @StateObject private var refreshedJourney=JourneyModel()
    @State private var refreshed=false
    @State private var updated=false
    @State private var updateError:String?
    init(trip:Trip,initialDay:Int=0,initialPlan:JourneyPlan?=nil,onPlan:((JourneyPlan,DayPlan)->Void)?=nil) {
        self.trip=trip;self.onPlan=onPlan;let index=trip.days.indices.contains(initialDay) ? initialDay:0
        self.initialDay=index;self.initialPlan=initialPlan?.source=="Apple Maps / MapKit" && initialPlan?.legs.flatMap(\.steps).allSatisfy({$0.mode=="walk"})==true ? initialPlan:nil;_dayIndex=State(initialValue:index)
    }
    private var day:DayPlan? {trip.days.indices.contains(dayIndex) ? trip.days[dayIndex]:nil}
    var body:some View {
        let _=interfaceLocale
        NavigationStack{ScrollView{VStack(alignment:.leading,spacing:16){
            if trip.days.count>1 {
                Picker(uiText("查看哪一天"),selection:$dayIndex){ForEach(trip.days.indices,id:\.self){Text(uiText("第 \($0+1) 天")).tag($0)}}.pickerStyle(.segmented).accessibilityIdentifier("apple-walking-day")
            }
            if let day {
                Text(uiText(day.date+(day.hasTransit ? " · 步行与公共交通":" · 步行路线预览"))).font(.subheadline.bold())
                NativeRouteMap(day:day,overview:overview.route,expanded:false,bottomInset:28,provider:.apple,viewport:nil,onViewport:{_ in},sections:overview.plan?.sections ?? []).frame(height:300).clipShape(RoundedRectangle(cornerRadius:20)).accessibilityIdentifier("apple-walking-map")
                if !day.hasWalkingJourney{Text(uiText("当天为自由安排，先在行程中添加地点。")).foregroundStyle(Palette.muted)}
                else if overview.busy {ProgressView(day.hasTransit ? "正在读取当天步行与公共交通路线…":"Apple 正在查询当天步行路线…").accessibilityIdentifier("apple-walking-loading")}
                else if let plan=overview.plan {
                    Label(uiText(day.hasTransit ? "步行＋公共交通 · 约 \(Int(ceil(plan.travelSeconds/60))) 分钟":"约 \(Int(plan.meters.rounded())) 米 · 步行 \(Int(ceil(plan.travelSeconds/60))) 分钟"),systemImage:day.hasTransit ? "tram.fill":"figure.walk").font(.headline).accessibilityIdentifier("apple-walking-summary")
                    Text(uiText(day.hasTransit ? "交通总计包含接驳和等车，不含游览停留；各段班次见下方。":"步行总计不含停留和等待；开始导航后跟随当前位置，偏离路线会重新查询。")).font(.caption).foregroundStyle(Palette.muted)
                    if !day.hasTransit {Button(uiText("开始步行导航"),systemImage:"location.fill"){navigating=true}.buttonStyle(.borderedProminent)}
                    if day.hasTransit {Text(uiText(refreshed ? "这是按新出发时间查询的路线预览；尚未修改已保存日程，票价未知。":"已保存的计划班次，不能保证现在仍能赶上。出发前请刷新交通；票价未知。")).font(.caption)}
                    else if day.legs.count==plan.legs.count && zip(day.legs,plan.legs).allSatisfy({ old,new in old.source.contains("Apple") && old.minutes==Int(ceil(new.steps.reduce(0){$0+$1.seconds}/60)) }) {Label(uiText("当天安排已按 Apple 步行时间校准"),systemImage:"checkmark.circle").font(.caption).foregroundStyle(Palette.green)}
                    else {
                        Text(uiText("行程里的到达与返回时间尚未按这条道路路线校准。")).font(.caption).foregroundStyle(Palette.muted)
                        if trip.id != nil {
                            Button(uiText("按真实步行时间更新当天安排")){Task{if await store.updateWalkingTimes(plan,original:trip,dayIndex:dayIndex){updated=true;dismiss()}else{updateError=store.adjustmentError}}}.buttonStyle(.borderedProminent).disabled(store.busy).accessibilityIdentifier("apply-walking-times")
                            Text(uiText("保留原景点与停留，计入衔接和休息；超出步数或时间上限会提示，原安排保留。")).font(.caption2).foregroundStyle(Palette.muted)
                        }else{Text(uiText("保存行程后可按最新步行时间校验安排。")).font(.caption)}
                        if let updateError{Text(uiText(updateError)).font(.caption).foregroundStyle(.red)}
                    }
                    ForEach(plan.legs){leg in
                        DisclosureGroup {
                            let directions=leg.steps.flatMap{$0.walkingDirections ?? []}
                            if leg.mode=="transit" {ForEach(leg.steps){step in VStack(alignment:.leading,spacing:4){Text(uiText(step.title)).bold();Text(uiText(step.from+" → "+step.to));Text(uiText(journeyTime(step.departureTime)+"–"+journeyTime(step.arrivalTime)+(step.realtime ? " · 实时预估":" · 时刻表")))}.font(.caption).padding(.vertical,6)}}
                            if directions.isEmpty && leg.mode != "transit" {Text(uiText("此段暂未提供分步文字，请参考地图路线。")).font(.caption)}
                            ForEach(Array(directions.enumerated()),id:\.offset){_,instruction in
                                HStack(alignment:.top){Text(uiText(instruction.text)).font(.subheadline);Spacer();if instruction.meters>0{Text(uiText("\(Int(instruction.meters.rounded())) 米")).font(.caption).foregroundStyle(Palette.muted)}}.padding(.vertical,5)
                            }
                        } label:{VStack(alignment:.leading,spacing:4){Text(uiText("\(leg.index+1). "+leg.from+" → "+leg.to)).font(.subheadline.bold());Text(uiText(leg.walkingSummary)).font(.caption).foregroundStyle(Palette.muted)}}.padding(14).background(.white,in:RoundedRectangle(cornerRadius:16))
                    }
                } else if let error=overview.message {
                    Label(uiText(store.language.display(error)),systemImage:"exclamationmark.circle").font(.subheadline).foregroundStyle(Palette.muted).accessibilityIdentifier("apple-walking-error")
                    Button(uiText("重试步行路线")){overview.load(day,provider:.apple)}.buttonStyle(.borderedProminent).accessibilityIdentifier("apple-walking-retry")
                }
                if day.hasTransit {
                    Button(uiText(refreshedJourney.busy ? "正在查询最新班次…":"刷新交通路线")) {
                        let f=DateFormatter();f.timeZone=TimeZone(identifier:"Australia/Sydney");f.dateFormat="yyyy-MM-dd HH:mm"
                        let minute=day.planningStartMinute ?? trip.profile.startMinute
                        let scheduled=f.date(from:day.date+String(format:" %02d:%02d",minute/60,minute%60)) ?? Date()
                        refreshedJourney.load(day:day,modes:day.transportModes,departure:max(Date(),scheduled),routingProvider:"apple",mapProvider:"apple")
                    }.buttonStyle(.borderedProminent).disabled(refreshedJourney.busy).accessibilityIdentifier("refresh-transit-route")
                    if let error=refreshedJourney.error{Text(uiText(error)).font(.caption).foregroundStyle(.red)}
                }
                if overview.plan != nil {
                    if day.hasTransit {Button(uiText("按最新班次重新安排")){store.trip=trip;store.dayIndex=dayIndex;dismiss();Task{await store.generate("重新核对第 \(dayIndex+1) 天剩余行程的公共交通和时间，保留已完成活动与固定安排。",profileOverride:trip.profile)}}.font(.subheadline)}
                    else{Button(uiText("重新查询路线")){overview.clear();overview.load(day,provider:.apple)}.font(.subheadline)}
                }
                Button(uiText("选择出发时间与交通方式"),systemImage:"slider.horizontal.3"){choosingTransport=true}.font(.subheadline).accessibilityIdentifier("route-transport-options")
                Text(uiText(day.hasTransit ? "路线来源：Apple Maps＋Transport for NSW · 原行程保持不变":"路线来源：Apple Maps / MapKit · 原行程安排保持不变")).font(.caption2).foregroundStyle(Palette.muted)
            }
        }.padding(18)}.fullScreenCover(isPresented:$navigating){if let day {WalkingNavigationView(day:day,tripID:trip.id ?? "draft",apple:true)}}.background(Palette.cream).navigationTitle(uiText(day?.hasTransit == true ? "步行与公共交通":"App 内步行路线")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("关闭")){dismiss()}}.tint(Palette.green)}
        .sheet(isPresented:$choosingTransport){if let day{JourneyPlannerView(day:day,tripID:trip.id ?? "draft",provider:.apple,departure:transportDeparture(day))}}
        .onReceive(refreshedJourney.$plan){value in if let value,let day{refreshed=true;overview.accept(value,day:day)}}
        .onDisappear{refreshedJourney.invalidate()}
        .task(id:day.map{mapSceneKey($0)} ?? "empty"){
            overview.clear();updateError=nil;refreshedJourney.invalidate();refreshed=false
            guard let day,day.hasWalkingJourney else{return}
            if dayIndex==initialDay,let initialPlan{overview.accept(initialPlan,day:day)}else{overview.load(day,provider:.apple)}
        }
        .onDisappear{
            store.dayIndex=dayIndex;store.selectedStopID=nil
            if !updated && !refreshed,let day,let plan=overview.plan{onPlan?(plan,day)}
            overview.cancel()
        }
    }
    private func transportDeparture(_ day:DayPlan)->Date {
        let format=DateFormatter();format.timeZone=TimeZone(identifier:"Australia/Sydney");format.dateFormat="yyyy-MM-dd HH:mm"
        let minute=day.planningStartMinute ?? trip.profile.startMinute
        return max(Date(),format.date(from:day.date+String(format:" %02d:%02d",minute/60,minute%60)) ?? Date())
    }
}

extension JourneyLeg {
    var walkingSummary:String {
        let meters=steps.reduce(0){$0+$1.meters};let seconds=steps.reduce(0){$0+$1.seconds+$1.waitSeconds}
        if mode=="transit" {return steps.filter{$0.mode != "walk"}.map(\.title).joined(separator:" → ")+" · 含接驳与等车约 \(Int(ceil(seconds/60))) 分钟"}
        return meters<30 ? "同一区域内 · 约 \(Int(meters.rounded())) 米，按现场入口步行":"\(Int(meters.rounded())) 米 · 约 \(Int(ceil(seconds/60))) 分钟"
    }
}

// Shared directional road observations used by both planning and editing.
struct WalkingObservation:Codable {
    var from:Coordinate;var to:Coordinate;var meters:Double;var seconds:Double;var fetchedAt:String;var unreachable:Bool? = nil
}
@MainActor final class AppleWalkingRoads {
    static let shared=AppleWalkingRoads()
    private var cache:[String:WalkingObservation]=[:]
    private var paths:[String:MKRoute]=[:]
    // Reserve network slots across reentrant planning, preview and navigation requests.
    private var nextQueryAt=Date.distantPast
    static func querySlot(now:Date,next:Date)->(start:Date,next:Date) {
        let start=max(now,next);return (start,start.addingTimeInterval(1.6))
    }
    private func waitForQuerySlot() async throws {
        let slot=Self.querySlot(now:Date(),next:nextQueryAt);nextQueryAt=slot.next
        let delay=slot.start.timeIntervalSinceNow
        if delay>0 {try await Task.sleep(for:.seconds(delay))}
        try Task.checkCancellation()
    }
    init(){if let data=UserDefaults.standard.data(forKey:"apple.walking.observations"),let values=try? JSONDecoder().decode([String:WalkingObservation].self,from:data){cache=values.filter{_,v in ISO8601DateFormatter().date(from:v.fetchedAt).map{Date().timeIntervalSince($0)<1800} ?? false}}}
    private func persist(){if let data=try? JSONEncoder().encode(cache){UserDefaults.standard.set(data,forKey:"apple.walking.observations")}}
    var query:((Coordinate,Coordinate) async throws -> MKRoute)?
    static func key(_ a:Coordinate,_ b:Coordinate)->String {String(format:"%.6f,%.6f,%.6f,%.6f",locale:Locale(identifier:"en_US_POSIX"),a.lat,a.lng,b.lat,b.lng)}
    func observation(_ a:Coordinate,_ b:Coordinate,force:Bool=false) async throws -> WalkingObservation {
        let key=Self.key(a,b)
        if !force,let hit=cache[key],let date=ISO8601DateFormatter().date(from:hit.fetchedAt),Date().timeIntervalSince(date)<1800{return hit}
        if CLLocation(latitude:a.lat,longitude:a.lng).distance(from:CLLocation(latitude:b.lat,longitude:b.lng))<1 {
            return WalkingObservation(from:a,to:b,meters:0,seconds:0,fetchedAt:ISO8601DateFormatter().string(from:Date()))
        }
        do {
            let route=try await route(a,b,force:force)
            let value=WalkingObservation(from:a,to:b,meters:route.distance,seconds:route.expectedTravelTime,fetchedAt:ISO8601DateFormatter().string(from:Date()))
            cache[key]=value;persist();return value
        } catch let error as MKError where error.code == .directionsNotFound {
            let value=WalkingObservation(from:a,to:b,meters:0,seconds:0,fetchedAt:ISO8601DateFormatter().string(from:Date()),unreachable:true);cache[key]=value;persist();return value
        }
    }
    func route(_ a:Coordinate,_ b:Coordinate,force:Bool=false) async throws -> MKRoute {
        let key=Self.key(a,b)
        if !force,let route=paths[key],let hit=cache[key],let at=ISO8601DateFormatter().date(from:hit.fetchedAt),Date().timeIntervalSince(at)<1800{return route}
        var last:Error=URLError(.cannotLoadFromNetwork)
        for attempt in 0..<3 {
            try Task.checkCancellation()
            do {
                let result:MKRoute
                if let query {result=try await query(a,b)}
                else {
                    try await waitForQuerySlot()
                    let request=MKDirections.Request();request.source=MKMapItem(placemark:MKPlacemark(coordinate:a.location));request.destination=MKMapItem(placemark:MKPlacemark(coordinate:b.location));request.transportType = .walking
                    let directions=MKDirections(request:request)
                    let timeout=Task{@MainActor in try await Task.sleep(for:.seconds(20));directions.cancel()}
                    defer{timeout.cancel()}
                    let response=try await withTaskCancellationHandler(operation:{try await directions.calculate()},onCancel:{directions.cancel()})
                    guard let found=response.routes.first else{throw MKError(.directionsNotFound)};result=found
                }
                try Task.checkCancellation();paths[key]=result
                cache[key]=WalkingObservation(from:a,to:b,meters:result.distance,seconds:result.expectedTravelTime,fetchedAt:ISO8601DateFormatter().string(from:Date()))
                persist();return result
            }catch {if Task.isCancelled{throw CancellationError()};last=error;if (error as? MKError)?.code == .directionsNotFound{throw error};if attempt<2{let delay=Self.isThrottled(error) ? Self.retryDelay(error):Double(attempt == 0 ? 3:10);if Self.isThrottled(error){nextQueryAt=max(nextQueryAt,Date().addingTimeInterval(delay))};try await Task.sleep(for:.seconds(delay))}}
        }
        throw NSError(domain:"Roads",code:1,userInfo:[NSLocalizedDescriptionKey:"Apple 步行查询暂不可用，尚未生成或保存估算路线。请稍后重试。",NSUnderlyingErrorKey:last])
    }
    static func isThrottled(_ error:Error)->Bool {
        let value=error as NSError
        if (error as? MKError)?.code == .loadingThrottled || (value.domain == "GEOErrorDomain" && value.code == -3) {return true}
        if value.userInfo["timeUntilReset"] != nil {return true}
        return value.userInfo.values.contains { item in
            if let nested=item as? NSError{return isThrottled(nested)}
            if let map=item as? [String:Any]{return map["timeUntilReset"] != nil}
            return false
        }
    }
    static func retryDelay(_ error:Error)->Double {
        func find(_ value:Any,depth:Int=0)->Double? {
            guard depth<6 else{return nil}
            if let error=value as? NSError {return find(error.userInfo,depth:depth+1)}
            if let map=value as? [String:Any] {if let n=map["timeUntilReset"] as? NSNumber{return n.doubleValue};for item in map.values {if let n=find(item,depth:depth+1){return n}}}
            if let list=value as? [Any] {for item in list{if let n=find(item,depth:depth+1){return n}}};return nil
        }
        return min(65,max(2,(find(error) ?? 60)+1))
    }
    func matrix(_ points:[Coordinate],progress:((Int,Int)->Void)?=nil) async throws -> [WalkingObservation] {
        var seen=Set<String>();let unique=points.filter{seen.insert(Self.key($0,$0)).inserted}
        guard unique.count<=32 else{throw NSError(domain:"Roads",code:2,userInfo:[NSLocalizedDescriptionKey:"候选地点过多，请缩小范围。"])}
        var result:[WalkingObservation]=[];let total=unique.count*max(0,unique.count-1)
        for a in unique {for b in unique where Self.key(a,a) != Self.key(b,b) {
            try Task.checkCancellation();result.append(try await observation(a,b));progress?(result.count,total)
        }}
        return result
    }
    func legs(_ days:[DayPlan]) async throws -> [WalkingObservation] {
        var result:[WalkingObservation]=[];var keys=Set<String>()
        for day in days {let points=[day.start]+day.stops.map{Coordinate(name:$0.name,lat:$0.lat,lng:$0.lng)}+[day.end]
            for i in 0..<(points.count-1) where keys.insert(Self.key(points[i],points[i+1])).inserted {result.append(try await observation(points[i],points[i+1]))}
        };return result
    }
}
