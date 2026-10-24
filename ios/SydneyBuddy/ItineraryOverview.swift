import SwiftUI
import MapKit
import Combine

struct OverviewRoute {
    let coordinates: [CLLocationCoordinate2D]
    let meters: Double
    let seconds: Double
    let legSeconds: [Double]
    var segments: String {legSeconds.enumerated().map {i,seconds in "\(i == 0 ? "起" : String(i))→\(i == legSeconds.count-1 ? "终" : String(i+1)) 步行 \(Int(ceil(seconds/60))) 分"}.joined(separator:" · ")}

    static func parse(_ data: Data) throws -> OverviewRoute {
        let object = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        guard object?["code"] as? String == "Ok",
              let route = (object?["routes"] as? [[String: Any]])?.first,
              let encoded = route["geometry"] as? String,
              let meters = route["distance"] as? Double,
              let seconds = route["duration"] as? Double,
              meters.isFinite, seconds.isFinite, meters >= 0, seconds >= 0 else { throw URLError(.cannotParseResponse) }
        let coordinates = try decode(encoded)
        guard coordinates.count > 1 else { throw URLError(.cannotParseResponse) }
        let legs = (route["legs"] as? [[String:Any]])?.compactMap {$0["duration"] as? Double} ?? []
        guard !legs.isEmpty, legs.allSatisfy({$0.isFinite && $0 >= 0}) else {throw URLError(.cannotParseResponse)}
        return OverviewRoute(coordinates: coordinates, meters: meters, seconds: seconds, legSeconds:legs)
    }
    // The navigation endpoint returns polyline6; Google itinerary legs remain polyline5.
    static func decode(_ encoded: String) throws -> [CLLocationCoordinate2D] {
        let bytes = Array(encoded.utf8)
        var index = 0, lat = 0, lng = 0
        func next() throws -> Int {
            var value = 0, shift = 0
            while true {
                guard index < bytes.count, shift <= 30, bytes[index] >= 63, bytes[index] <= 126 else { throw URLError(.cannotParseResponse) }
                let b = Int(bytes[index]) - 63; index += 1
                value |= (b & 31) << shift; shift += 5
                if b < 32 { break }
            }
            return value & 1 == 0 ? value >> 1 : ~(value >> 1)
        }
        var result: [CLLocationCoordinate2D] = []
        while index < bytes.count {
            lat += try next(); lng += try next()
            let point = CLLocationCoordinate2D(latitude: Double(lat) / 1e6, longitude: Double(lng) / 1e6)
            guard CLLocationCoordinate2DIsValid(point) else { throw URLError(.cannotParseResponse) }
            result.append(point)
        }
        return result
    }
}

@MainActor final class ItineraryOverview: ObservableObject {
    @Published var key = ""
    @Published var route: OverviewRoute?
    @Published var plan: JourneyPlan?
    @Published var busy = false
    @Published var message: String?
    private var task: Task<Void, Never>?
    private var generation = UUID()
    private var homeIdentity=""
    private var routeProvider:MapProvider?
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
    private let applePlan:(DayPlan,Date) async throws -> JourneyPlan
    init(applePlan:@escaping (DayPlan,Date) async throws -> JourneyPlan = {day,departure in
        try await AppleJourneyProvider.plan(day:day,modes:Array(repeating:"walk",count:day.stops.count+1),departure:departure)
    }) {self.applePlan=applePlan}
    var summary:String {route.map{"路线约 \(Int($0.meters.rounded())) 米 · \(Int(ceil($0.seconds/60))) 分钟"} ?? "显示步行路线"}
    func loadHome(_ day:DayPlan,provider:MapProvider,contentSource:String) {
        let identity=mapSceneKey(day)+"|"+provider.rawValue+"|"+contentSource
        guard homeIdentity != identity else{return};homeIdentity=identity
        clear();key=mapSceneKey(day)
        if !day.stops.isEmpty,contentSource != "google" && (provider == .osm || provider == .apple){load(day,provider:provider)}
    }
    func clear(){cancel();route=nil;plan=nil;message=nil}
    func accept(_ result:JourneyPlan,day:DayPlan) {
        cancel();key=mapSceneKey(day);plan=result;route=result.overview;message="路线来源："+result.source
        if result.source=="Apple Maps / MapKit",result.legs.flatMap(\.steps).allSatisfy({$0.mode=="walk"}) {
            routeProvider = .apple;homeIdentity=mapSceneKey(day)+"|apple|open"
        }
    }
    func cancel() { generation = UUID(); task?.cancel(); task = nil; busy = false }
    func reset(_ newKey: String) {
        guard key != newKey else { return }
        clear(); key = newKey
    }
    func load(_ day: DayPlan,provider:MapProvider = .osm) {
        if routeProvider != provider {clear();routeProvider=provider}
        reset(mapSceneKey(day)); guard !busy, route == nil else { return }
        guard provider == .osm || provider == .apple else {message="请从交通路线规划选择对应的路线服务。";return}
        busy = true; message = nil
        let token = generation
        let points = [day.start.location] + day.stops.map(\.coordinate) + [day.end.location]
        task = Task {
            do {
                if provider == .apple {
                    let result=try await applePlan(day,Date())
                    try Task.checkCancellation();guard token==generation else{return}
                    plan=result;route=result.overview;busy=false;message="路线来源："+result.source
                    return
                }
                // Co-located meal/facility placeholders remain separate itinerary stops, but
                // must not become zero-length break points in the upstream road query.
                var compact:[CLLocationCoordinate2D]=[points[0]]
                var edgeMap:[Int?]=[]
                for point in points.dropFirst() {
                    let previous=compact.last!
                    if point.latitude == previous.latitude && point.longitude == previous.longitude {edgeMap.append(nil)}
                    else {edgeMap.append(compact.count-1);compact.append(point)}
                }
                if compact.count == 1 {
                    try Task.checkCancellation();guard token == generation else{return}
                    route=OverviewRoute(coordinates:[points[0],points[0]],meters:0,seconds:0,legSeconds:edgeMap.map{_ in 0});busy=false;return
                }
                var request = URLRequest(url: URL(string: "http://127.0.0.1:8787/navigation/route")!)
                request.httpMethod = "POST"; request.timeoutInterval = 25
                request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                request.httpBody = try JSONSerialization.data(withJSONObject: ["locations": compact.map { ["lat": $0.latitude, "lon": $0.longitude] }])
                let (data, response) = try await loader(request)
                guard (response as? HTTPURLResponse)?.statusCode == 200 else {let object=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any];throw NSError(domain:"Overview",code:1,userInfo:[NSLocalizedDescriptionKey:object?["error"] as? String ?? "道路路線服務暫時無法使用"])}
                let parsed = try OverviewRoute.parse(data)
                guard parsed.legSeconds.count == compact.count-1 else {throw URLError(.cannotParseResponse)}
                let expanded=OverviewRoute(coordinates:parsed.coordinates,meters:parsed.meters,seconds:parsed.seconds,legSeconds:edgeMap.map{index in index.map{parsed.legSeconds[$0]} ?? 0})
                try Task.checkCancellation()
                guard token == generation else { return }
                plan=nil;route = expanded; busy = false
            } catch {
                guard token == generation, !Task.isCancelled else { return }
                busy = false
                message = provider == .apple ? "Apple 步行路线暂时无法载入，请检查网络后重试。当前仅显示站点顺序，未取得真实道路路线。":"道路路线无法载入："+error.localizedDescription.trimmingCharacters(in:CharacterSet(charactersIn:"。.!"))+"。当前仅显示站点顺序。"
            }
        }
    }
}

/// Pins stay numbered without opening a callout. Start and return share one badge on loop trips.
final class ItineraryPin: MKPointAnnotation {
    let badge: String
    let direction: Bool
    init(_ coordinate: CLLocationCoordinate2D, badge: String, title: String, detail: String? = nil, direction: Bool = false) {
        self.badge = badge; self.direction = direction
        super.init(); self.coordinate = coordinate; self.title = title; self.subtitle = detail
    }
}
func overviewBadge(_ text: String, direction: Bool = false) -> UIImage {
    let size = CGSize(width: direction ? 24 : text == "●" ? 24 : 42, height: direction ? 24 : text == "●" ? 24 : 42)
    return UIGraphicsImageRenderer(size:size).image {_ in
        let rect=CGRect(origin:.zero,size:size).insetBy(dx:3,dy:3)
        let start=text.contains("起"),end=text == "终"
        if end {
            let image=UIImage(systemName:"mappin.circle.fill")?.withTintColor(.systemRed,renderingMode:.alwaysOriginal)
            image?.draw(in:rect);return
        }
        UIColor.white.setFill();UIBezierPath(ovalIn:CGRect(origin:.zero,size:size)).fill()
        (start ? UIColor.systemBlue : direction ? UIColor(Palette.green) : UIColor.white).setFill()
        let circle=UIBezierPath(ovalIn:rect);circle.fill()
        if !start && !direction {UIColor.darkGray.setStroke();circle.lineWidth=2;circle.stroke()}
        let attrs:[NSAttributedString.Key:Any]=[.font:UIFont.boldSystemFont(ofSize:direction ? 16 : text.count>2 ? 10 : 15),.foregroundColor:start || direction ? UIColor.white : UIColor.darkGray]
        let label=text == "●" ? "" : text
        let measured=(label as NSString).size(withAttributes:attrs)
        (label as NSString).draw(at:CGPoint(x:(size.width-measured.width)/2,y:(size.height-measured.height)/2),withAttributes:attrs)
    }
}
func overviewPins(_ day: DayPlan) -> [ItineraryPin] {
    let loop = CLLocation(latitude: day.start.lat, longitude: day.start.lng).distance(from: CLLocation(latitude: day.end.lat, longitude: day.end.lng)) < 3
    var pins = [ItineraryPin(day.start.location, badge: loop ? "起/终" : "起", title: day.start.name + (loop ? " · 起点 / 返回点" : " · 起点"))]
    var consumed=Set<Int>()
    for (i,stop) in day.stops.enumerated() where !consumed.contains(i) {
        let origin=CLLocation(latitude:stop.lat,longitude:stop.lng)
        let group=day.stops.indices.filter {j in !consumed.contains(j) && origin.distance(from:CLLocation(latitude:day.stops[j].lat,longitude:day.stops[j].lng)) < 20}
        consumed.formUnion(group)
        pins.append(ItineraryPin(stop.coordinate,badge:group.map{String($0+1)}.joined(separator:"/"),title:group.map{"\($0+1). \(day.stops[$0].name)"}.joined(separator:" → "),detail:group.map{"\(day.stops[$0].arrival)–\(day.stops[$0].departure)"}.joined(separator:" · ")))
    }
    if !loop { pins.append(ItineraryPin(day.end.location, badge: "终", title: day.end.name + " · 返回点")) }
    return pins
}
func overviewDirections(_ coordinates: [CLLocationCoordinate2D]) -> [ItineraryPin] {
    // One arrow halfway along each non-zero segment; do not clutter the map with an arrow per road vertex.
    zip(coordinates, coordinates.dropFirst()).enumerated().compactMap { index, pair in
        let (a,b) = pair
        if coordinates.count > 30 && index % max(1,coordinates.count/8) != 0 {return nil}
        guard abs(a.latitude-b.latitude)+abs(a.longitude-b.longitude) > 0.00001 else { return nil }
        let angle = atan2((b.longitude-a.longitude)*cos(a.latitude * .pi/180), b.latitude-a.latitude)
        let symbols = ["↑", "↗", "→", "↘", "↓", "↙", "←", "↖"]
        let index = (Int((angle / (.pi/4)).rounded()) + 8) % 8
        let position = coordinates.count > 30 ? a : CLLocationCoordinate2D(latitude: (a.latitude+b.latitude)/2, longitude: (a.longitude+b.longitude)/2)
        return ItineraryPin(position, badge: symbols[index], title: "行程方向", direction: true)
    }
}

struct OverviewTransportLeg:Identifiable {
    var id:Int;var label:String;var from:String;var to:String;var mode:String;var transport:String;var minutes:Int
}
func overviewTransportLegs(_ day:DayPlan,route:OverviewRoute?,plan:JourneyPlan?)->[OverviewTransportLeg] {
    if let plan{return plan.legs.map{leg in
        OverviewTransportLeg(id:leg.index,label:"\(leg.index==0 ? "起":String(leg.index))→\(leg.index==day.stops.count ? "終":String(leg.index+1))",from:leg.from,to:leg.to,mode:leg.steps.first?.mode ?? leg.mode,transport:leg.steps.map(\.title).joined(separator:" · "),minutes:Int(ceil(leg.steps.reduce(0){$0+$1.seconds+$1.waitSeconds}/60)))
    }}
    guard let route,route.legSeconds.count==day.stops.count+1 else{return []}
    let names=[day.start.name]+day.stops.map(\.name)+[day.end.name]
    return route.legSeconds.enumerated().map{i,seconds in OverviewTransportLeg(id:i,label:"\(i==0 ? "起":String(i))→\(i==day.stops.count ? "終":String(i+1))",from:names[i],to:names[i+1],mode:"walk",transport:seconds == 0 ? "同一位置" : "步行",minutes:Int(ceil(seconds/60)))}
}
struct HomeTransportStrip:View {
    var day:DayPlan;@ObservedObject var overview:ItineraryOverview
    @EnvironmentObject var store:AppStore
    var onPlan:()->Void;var onRetry:()->Void
    var body:some View {
        VStack(alignment:.leading,spacing:6){
            HStack{Button(action:onPlan){Label("交通方式",systemImage:"arrow.triangle.swap").font(.caption.bold())};Spacer();if overview.route != nil{Text(store.language.text("{0} 分 · 道路查詢",String(Int(ceil((overview.route?.seconds ?? 0)/60))))).font(.caption2).foregroundStyle(Palette.muted)}}
            Text("行程排程交通 \(day.legs.reduce(0){$0+$1.minutes}) 分；道路查詢為另一來源，尚未套用至時間軸。").font(.caption2).foregroundStyle(Palette.muted)
            if overview.busy{ProgressView("正在載入道路路線…").font(.caption)}
            else if overview.route != nil{
                ScrollView(.horizontal,showsIndicators:false){HStack(spacing:6){ForEach(overviewTransportLegs(day,route:overview.route,plan:overview.plan)){leg in
                    VStack(alignment:.leading,spacing:4){Label(store.language.text("{0} · {1} {2} 分",leg.label,store.language.text(leg.transport),String(leg.minutes)),systemImage:transportSymbol(leg.mode)).font(.caption.bold()).lineLimit(1);Text(leg.from+" → "+leg.to).font(.system(size:10)).lineLimit(1)}.padding(8).frame(maxWidth:220,alignment:.leading).background(Palette.mint.opacity(0.65),in:RoundedRectangle(cornerRadius:10))
                }}}.accessibilityIdentifier("home-transport-legs")
            }else if let message=overview.message{
                HStack(alignment:.top){Text(message).font(.caption2).foregroundStyle(.red);Spacer();Button("重試",action:onRetry).font(.caption.bold())}.accessibilityIdentifier("home-route-error")
            }else{Text("選擇交通方式以規劃路線").font(.caption2).foregroundStyle(Palette.muted)}
        }
    }
}
