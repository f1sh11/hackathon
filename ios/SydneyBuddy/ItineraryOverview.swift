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
    @Published var busy = false
    @Published var message: String?
    private var task: Task<Void, Never>?
    private var generation = UUID()
    func cancel() { generation = UUID(); task?.cancel(); task = nil; busy = false }
    func reset(_ newKey: String) {
        guard key != newKey else { return }
        cancel(); key = newKey; route = nil; message = nil
    }
    func load(_ day: DayPlan) {
        reset(mapSceneKey(day)); guard !busy, route == nil else { return }
        busy = true; message = nil
        let token = generation
        let points = [day.start.location] + day.stops.map(\.coordinate) + [day.end.location]
        task = Task {
            do {
                var request = URLRequest(url: URL(string: "http://127.0.0.1:8787/navigation/route")!)
                request.httpMethod = "POST"; request.timeoutInterval = 25
                request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                request.httpBody = try JSONSerialization.data(withJSONObject: ["locations": points.map { ["lat": $0.latitude, "lon": $0.longitude] }])
                let (data, response) = try await URLSession.shared.data(for: request)
                guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }
                let parsed = try OverviewRoute.parse(data)
                try Task.checkCancellation()
                guard token == generation else { return }
                route = parsed; busy = false
            } catch {
                guard token == generation, !Task.isCancelled else { return }
                busy = false; message = "暂时无法取得步行路线，仅显示站点；请重试。"
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
