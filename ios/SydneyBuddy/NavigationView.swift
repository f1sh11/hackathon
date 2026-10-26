import SwiftUI
import MapKit
import Combine
import CryptoKit
import FerrostarCore
import FerrostarCoreFFI
import GoogleMaps
import AVFoundation

private struct BackendRouteSession:URLRequestLoading {
    func loadData(with request:URLRequest) async throws -> (Data,URLResponse){try await BackendConnection.data(for:request)}
}

struct WalkingPoint: Codable, Equatable {
    var name: String
    var lat: Double
    var lng: Double
    var stay: Int = 0
    var activityID:String? = nil
    var coordinate: CLLocationCoordinate2D { .init(latitude: lat, longitude: lng) }
}
struct WalkingProgress: Codable, Equatable {
    var leg = 0
    var arrived = false
    var finished = false
    mutating func arrive() { if !finished { arrived = true } }
    mutating func advance(legCount: Int) {
        guard arrived, !finished else { return }
        if leg + 1 >= legCount { finished = true } else { leg += 1; arrived = false }
    }
}

/// The itinerary owns stop progress; Ferrostar owns only the currently active walking leg.
@MainActor final class WalkingNavigator: ObservableObject {
    let points: [WalkingPoint]
    let storageKey: String
    @Published var progress: WalkingProgress
    @Published var simulation = false
    @Published var busy = false
    @Published var active = false
    @Published var message: String?
    @Published var instruction = "准备出发"
    @Published var remaining: String = ""
    @Published var geometry: [CLLocationCoordinate2D] = []
    @Published var position: CLLocationCoordinate2D?
    @Published var muted = false
    private var core: FerrostarCore?
    private var provider: LocationProviding?
    private var observer: AnyCancellable?
    private var task: Task<Void, Never>?
    private var generation = UUID()
    private var realProgress: WalkingProgress
    private let endpoint: String
    let apple:Bool
    private let executionKey:String
    private var completed:Set<String>
    private var skipped:Set<String>
    private var speech=AVSpeechSynthesizer()
    private var lastSpoken=""
    var target: WalkingPoint { points[min(progress.leg + 1, points.count - 1)] }
    var count: Int { points.count - 1 }

    init(day: DayPlan, tripID: String, endpoint: String,apple:Bool=false) {
        self.apple=apple
        executionKey=JourneyExecution.key(tripID:tripID,date:day.date)
        completed=Set(UserDefaults.standard.stringArray(forKey:executionKey) ?? [])
        skipped=JourneyExecution.skipped(tripID:tripID,date:day.date)
        points = [WalkingPoint(name: day.start.name, lat: day.start.lat, lng: day.start.lng)]
            + day.stops.map { WalkingPoint(name: $0.name, lat: $0.lat, lng: $0.lng, stay: $0.visitMinutes,activityID:$0.id) }
            + [WalkingPoint(name: day.end.name + "（返回）", lat: day.end.lat, lng: day.end.lng)]
        let identity = tripID + ":" + day.date + ":" + points.map{$0.activityID ?? "\($0.lat),\($0.lng)"}.joined(separator:"|")
        storageKey = "walking:" + SHA256.hash(data: Data(identity.utf8)).map { String(format: "%02x", $0) }.joined()
        let saved = UserDefaults.standard.data(forKey: storageKey)
            .flatMap { try? JSONDecoder().decode(WalkingProgress.self, from: $0) } ?? WalkingProgress()
        var restored = saved.leg >= 0 && saved.leg < points.count - 1 ? saved : WalkingProgress()
        let consumed=completed.union(skipped)
        if apple {restored=WalkingProgress();while restored.leg<points.count-2,let id=points[restored.leg+1].activityID,(completed.contains(id)||skipped.contains(id)){restored.leg+=1};if completed.contains("return") && points.dropFirst().dropLast().allSatisfy({$0.activityID.map{consumed.contains($0)} ?? false}){restored.finished=true}}
        progress = restored
        realProgress = restored
        self.endpoint = endpoint
    }
    func setSimulation(_ value: Bool) {
        pause(); simulation = value
        skipped=value ? []:Set(UserDefaults.standard.stringArray(forKey:executionKey.replacingOccurrences(of:"journey.completed.",with:"journey.skipped.")) ?? [])
        progress = value ? WalkingProgress() : realProgress
        geometry = []; position = nil; message = nil
    }
    private func save() {
        guard !simulation else { return }
        realProgress = progress
        if let data = try? JSONEncoder().encode(progress) { UserDefaults.standard.set(data, forKey: storageKey) }
    }
    func pause() {
        generation = UUID(); task?.cancel(); task = nil
        observer?.cancel(); observer = nil; core?.stopNavigation(); provider?.stopUpdating()
        core = nil; provider = nil; busy = false; active = false
        speech.stopSpeaking(at:.immediate)
        UIApplication.shared.isIdleTimerDisabled = false
    }
    func arrived() {
        pause(); progress.arrive(); instruction = "已到达 " + target.name; remaining = "";if !simulation {completed.insert(target.activityID ?? "return");UserDefaults.standard.set(Array(completed),forKey:executionKey)};save()
    }
    func next() {
        guard progress.arrived else { return }
        progress.advance(legCount: count);
        if apple && !simulation {while !progress.finished,let id=points[min(progress.leg+1,points.count-1)].activityID,(completed.contains(id)||skipped.contains(id)){progress.arrive();progress.advance(legCount:count)}}
        geometry = []; position = nil; save()
        if !progress.finished { start() }
    }
    func reset() {
        pause();completed=[];skipped=[];UserDefaults.standard.removeObject(forKey:executionKey);UserDefaults.standard.removeObject(forKey:executionKey.replacingOccurrences(of:"journey.completed.",with:"journey.skipped.")); progress = WalkingProgress(); geometry = []; position = nil; message = nil; save()
    }
    func skip() {
        guard let id=target.activityID else{return}
        pause();skipped.insert(id)
        if !simulation{UserDefaults.standard.set(Array(skipped),forKey:executionKey.replacingOccurrences(of:"journey.completed.",with:"journey.skipped."))}
        progress.arrive();next()
    }
    func toggleMute() {
        muted.toggle(); core?.spokenInstructionObserver.toggleMute()
    }
    static func config() -> SwiftNavigationControllerConfig {
        return SwiftNavigationControllerConfig(
                    waypointAdvance: .waypointWithinRange(15),
                    stepAdvanceCondition: stepAdvanceDistanceEntryAndExit(distanceToEndOfStep: 15, distanceAfterEndOfStep: 5, minimumHorizontalAccuracy: 50),
                    arrivalStepAdvanceCondition: stepAdvanceDistanceToEndOfStep(distance: 15, minimumHorizontalAccuracy: 50),
                    routeDeviationTracking: .staticThreshold(minimumHorizontalAccuracy: 50, maxAcceptableDeviation: 40),
                    snappedLocationCourseFiltering: .raw)
    }
    func preview() {
        guard !busy, !active else { return }
        if apple {previewApple();return}
        pause(); busy = true; message = nil
        let token = generation
        task = Task {
            do {
                let source = SimulatedLocationProvider(location: CLLocation(coordinate: points[0].coordinate, altitude: 0, horizontalAccuracy: 5, verticalAccuracy: 5, timestamp: Date()))
                let engine = try FerrostarCore(wellKnownRouteProvider: .valhalla(endpointUrl: endpoint, profile: "pedestrian"), locationProvider: source, navigationControllerConfig: Self.config(),networkSession:BackendRouteSession())
                let routes = try await engine.getRoutes(initialLocation: source.lastLocation!, waypoints: points.dropFirst().map { Waypoint(coordinate: GeographicCoordinate(lat: $0.lat, lng: $0.lng), kind: .break) })
                try Task.checkCancellation()
                guard token == generation, let route = routes.first else { return }
                geometry = route.geometry.map(\.clLocationCoordinate2D); position = nil
                remaining = "全天步行约 \(Int(route.distance)) 米（不含停留）"; busy = false
            } catch is CancellationError { }
            catch { if token == generation { pause(); message = "暂时无法预览步行路线，请稍后重试。" } }
        }
    }
    func start() {
        guard !busy, !active, !progress.finished, !progress.arrived else { return }
        if apple {startApple();return}
        pause(); busy = true; message = nil
        let token = generation
        task = Task {
            do {
                let origin = points[progress.leg]
                let source: LocationProviding
                if simulation {
                    let fake = SimulatedLocationProvider(location: CLLocation(coordinate: origin.coordinate, altitude: 0, horizontalAccuracy: 5, verticalAccuracy: 5, timestamp: Date()))
                    fake.warpFactor = 10; source = fake
                } else {
                    guard CLLocationManager.locationServicesEnabled() else { throw navigationError("请先开启设备定位服务。") }
                    let live = CoreLocationProvider(activityType: .fitness, allowBackgroundLocationUpdates: false)
                    source = live
                }
                provider = source
                source.startUpdating()
                var location: FerrostarCoreFFI.UserLocation?
                for _ in 0..<150 {
                    try Task.checkCancellation()
                    if !simulation && (source.authorizationStatus == .denied || source.authorizationStatus == .restricted) {
                        throw navigationError("定位权限未开启，请在系统设置允许精确定位，或选择模拟导航。")
                    }
                    if let fix = source.lastLocation, fix.horizontalAccuracy >= 0, fix.horizontalAccuracy <= 50,
                       abs(fix.timestamp.timeIntervalSinceNow) < 30 { location = fix; break }
                    try await Task.sleep(nanoseconds: 200_000_000)
                }
                guard let location else { throw navigationError("尚未取得准确位置，请到开阔处重试，或选择模拟导航。") }
                try Task.checkCancellation()
                guard token == generation else { return }
                let actual = CLLocation(latitude: location.coordinates.lat, longitude: location.coordinates.lng)
                let destination = CLLocation(latitude: target.lat, longitude: target.lng)
                if location.horizontalAccuracy <= 20 && actual.distance(from: destination) <= 10 {
                    position = location.coordinates.clLocationCoordinate2D; geometry = []; arrived(); return
                }
                let config = Self.config()
                let engine = try FerrostarCore(wellKnownRouteProvider: .valhalla(endpointUrl: endpoint, profile: "pedestrian"), locationProvider: source, navigationControllerConfig: config)
                if muted { engine.spokenInstructionObserver.toggleMute() }
                core = engine
                let routes = try await engine.getRoutes(initialLocation: location, waypoints: [Waypoint(coordinate: GeographicCoordinate(lat: target.lat, lng: target.lng), kind: .break)])
                try Task.checkCancellation()
                guard token == generation, let route = routes.first else { throw navigationError("找不到可步行的路线，请重试。") }
                geometry = route.geometry.map(\.clLocationCoordinate2D)
                if let fake = source as? SimulatedLocationProvider { try fake.setSimulatedRoute(route) }
                observer = engine.$state.receive(on: DispatchQueue.main).sink { [weak self] state in
                    guard let self, token == self.generation, let state else { return }
                    self.geometry = state.routeGeometry.map(\.clLocationCoordinate2D)
                    self.position = state.preferredUserLocation?.coordinates.clLocationCoordinate2D
                    self.instruction = state.isCalculatingNewRoute ? "偏离路线，正在重新规划…" : (state.currentVisualInstruction?.primaryContent.text ?? state.currentStep?.roadName ?? "沿路线步行")
                    if let p = state.currentProgress { self.remaining = "剩余 \(Int(p.distanceRemaining)) 米 · 约 \(max(1, Int(ceil(p.durationRemaining / 60)))) 分钟" }
                    if case .complete = state.tripState { self.arrived() }
                }
                active = true; busy = false
                UIApplication.shared.isIdleTimerDisabled = true
                try engine.startNavigation(route: route)
            } catch is CancellationError { }
            catch {
                guard token == generation else { return }
                pause(); message = (error as NSError).domain == "WalkingNavigation" ? error.localizedDescription : "步行路线暂不可用，请稍后重试。到站进度已保留。"
            }
        }
    }
}
private func navigationError(_ text: String) -> NSError { NSError(domain: "WalkingNavigation", code: 1, userInfo: [NSLocalizedDescriptionKey: text]) }

struct WalkingNavigationView: View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store: AppStore
    @Environment(\.dismiss) var dismiss
    @Environment(\.scenePhase) var scenePhase
    @StateObject private var navigator: WalkingNavigator
    @State private var reset = false
    @State private var skipping=false
    @State private var autoStarted = false
    @State private var journeyCue:KoalaCue?
    @State private var announcedJourney=false
    var autoStart:Bool
    var dayDate:String
    init(day: DayPlan, tripID: String,autoStart:Bool=false,apple:Bool?=nil) {
        self.autoStart=autoStart;self.dayDate=day.date
        _navigator = StateObject(wrappedValue: WalkingNavigator(day: day, tripID: tripID, endpoint: BackendConnection.url("navigation/route").absoluteString,apple:apple ?? (MapProvider.preferred() == .apple)))
    }
    var body: some View {
        let _=interfaceLocale
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if let cue=journeyCue {KoalaFeedbackBanner(cue:cue){journeyCue=nil}}
                    Text(uiText("導航日期："+dayDate)).font(.caption).foregroundStyle(Palette.muted)
                    Text(uiText("第 \(navigator.progress.leg + 1) / \(navigator.count) 段 · 步行")).font(.headline)
                    Text(uiText(navigator.progress.finished ? "今天的行程已完成" : "前往 " + navigator.target.name)).font(.title2.bold())
                    Picker(uiText("底图"), selection: $store.mapProvider) {
                        ForEach(MapProvider.allCases) { Text(uiText($0.label)).tag($0) }
                    }.pickerStyle(.menu)
                    if store.mapProvider == .google {
                        if SydneyBuddyApp.mapReady { WalkingGoogleMap(points: navigator.points, geometry: navigator.geometry, position: navigator.position).frame(height: 300).clipShape(RoundedRectangle(cornerRadius: 20)) }
                        else { Text(uiText("Google 地图未配置密钥，请选择 OpenStreetMap 或 Apple Maps。")) }
                    } else if store.mapProvider == .schematic {
                        Text(uiText("离线示意图不显示导航路线，请选择联网底图。"))
                    } else {
                        WalkingNativeMap(points: navigator.points, geometry: navigator.geometry, position: navigator.position, osm: store.mapProvider == .osm)
                            .id(store.mapProvider).frame(height: 300).clipShape(RoundedRectangle(cornerRadius: 20))
                        if store.mapProvider == .osm { Link(uiText("© OpenStreetMap contributors"), destination: URL(string: "https://www.openstreetmap.org/copyright")!).font(.caption) }
                    }
                    Toggle(uiText("模拟导航（不使用设备定位）"), isOn: Binding(get: { navigator.simulation }, set: { navigator.setSimulation($0) }))
                        .disabled(navigator.busy || navigator.active)
                    if !navigator.simulation { Text(uiText(navigator.apple ? "使用 Apple 步行路线；当前位置用于查路和偏航重算。":"实际位置会经本机后端发送至已配置的步行路线服务，用于计算路线。")).font(.caption) }
                    if navigator.simulation { Text(uiText("模拟位置 · 快速演示 · 不记录实际行程进度")).font(.caption).foregroundStyle(.orange) }
                    if !navigator.active {
                        Button(uiText("预览整天步行路线")) { navigator.preview() }.disabled(navigator.busy || (!navigator.apple && store.health?.navigationConfigured != true))
                        if !navigator.remaining.isEmpty { Text(uiText(navigator.remaining)).font(.caption) }
                    }
                    Text(uiText("步行路线 · 到站后可停留，再前往下一站")).font(.caption).foregroundStyle(Palette.muted)
                    if navigator.apple {Text(uiText("路线数据：Apple Maps / MapKit")).font(.caption)} else {Link(uiText("路线数据 © OpenStreetMap contributors"), destination: URL(string: "https://www.openstreetmap.org/copyright")!).font(.caption)}
                    if navigator.busy { ProgressView("正在获取位置与步行路线…") }
                    if let message = navigator.message { Text(uiText(message)).foregroundStyle(.red) }
                    if navigator.active {
                        Text(uiText(navigator.instruction)).font(.headline)
                        Text(uiText(navigator.remaining))
                        HStack {
                            Button(uiText(navigator.muted ? "开启语音" : "静音")) { navigator.toggleMute() }
                            Spacer(); Button(uiText("暂停导航")) { navigator.pause() }
                        }
                        Button(uiText("我已到达")) { navigator.arrived() }
                    } else if navigator.progress.finished {
                        Label(uiText("全部路段已完成"), systemImage: "checkmark.circle.fill").foregroundStyle(Palette.green)
                    } else if navigator.progress.arrived {
                        Text(uiText("已到达。\(navigator.target.stay > 0 ? "建议停留 \(navigator.target.stay) 分钟。" : "")准备好再继续。"))
                        PrimaryButton(title: navigator.progress.leg + 1 == navigator.count ? "完成今天行程" : "前往下一站", symbol: "arrow.right") { navigator.next() }.disabled(navigator.busy)
                    } else {
                        PrimaryButton(title: navigator.simulation ? "开始模拟导航" : "开始本段导航", symbol: "location.fill") { navigator.start() }
                            .disabled(navigator.busy || (!navigator.apple && store.health?.navigationConfigured != true))
                    }
                    if (!navigator.apple && store.health?.navigationConfigured != true) {
                        Text(uiText("步行路线服务尚未开启，暂时无法导航。请联系开发团队。")) .font(.caption)
                    }
                    if navigator.apple,navigator.target.activityID != nil,!navigator.progress.finished {Button(uiText("跳过这一站")){skipping=true}.disabled(navigator.busy)}
                    DisclosureGroup(uiText("整天路线与完成进度")) {
                        ForEach(Array(navigator.points.enumerated()), id: \.offset) { index, point in
                            HStack {
                                Image(systemName: index <= navigator.progress.leg || navigator.progress.finished || index == navigator.progress.leg + 1 && navigator.progress.arrived ? "checkmark.circle.fill" : "circle")
                                Text(uiText("\(index). \(point.name)")); Spacer()
                                if point.stay > 0 { Text(uiText("\(point.stay) 分钟")).font(.caption) }
                            }.padding(.vertical, 8)
                        }
                    }
                    Text(uiText("保持本页开启进行导航；离开 App 会暂停，回来后可从当前位置继续。停留时间不计入步行 ETA。")) .font(.caption).foregroundStyle(Palette.muted)
                    Button(uiText("重置今天导航进度"), role: .destructive) { reset = true }
                }.padding(20)
            }.background(Palette.cream).navigationTitle(uiText("逐段导航")).navigationBarTitleDisplayMode(.inline)
                .task(id:store.health?.navigationConfigured){if autoStart && !autoStarted && (navigator.apple || store.health?.navigationConfigured==true) {autoStarted=true;navigator.start()}}
                .toolbar { ToolbarItem(placement: .topBarTrailing) { Button(uiText("关闭")) { dismiss() } } }
                .confirmationDialog(uiText("跳过当前地点，直接前往下一站？"),isPresented:$skipping){Button(uiText("跳过这一站"),role:.destructive){navigator.skip()}}
                .confirmationDialog(uiText("重置今天的导航进度？"), isPresented: $reset) { Button(uiText("重置"), role: .destructive) { navigator.reset() } }
        }
        .onChange(of:navigator.active){_,active in
            if active && !navigator.simulation && !announcedJourney {announcedJourney=true;journeyCue=KoalaCue(event:.journeyStarted)}
        }
        .onDisappear { navigator.pause() }
        // A system location prompt makes the scene inactive; keep the pending start alive until authorization returns.
        .onChange(of: scenePhase) { _, phase in if phase == .background { navigator.pause() } }
    }
}

/// Map renderers consume neutral coordinates, not Ferrostar or provider-specific route objects.
struct WalkingNativeMap: UIViewRepresentable {
    var points: [WalkingPoint]
    var geometry: [CLLocationCoordinate2D]
    var position: CLLocationCoordinate2D?
    var osm: Bool
    func makeCoordinator() -> Coordinator { Coordinator() }
    func makeUIView(context: Context) -> MKMapView {
        let map = MKMapView(); map.delegate = context.coordinator
        map.pointOfInterestFilter = .excludingAll
        if osm { map.addOverlay(OsmTiles(), level: .aboveLabels) }
        return map
    }
    func updateUIView(_ map: MKMapView, context: Context) {
        let key = geometry.map { "\($0.latitude),\($0.longitude)" }.joined(separator: ";")
        if context.coordinator.key != key {
            context.coordinator.key = key
            map.removeOverlays(map.overlays.filter { !($0 is MKTileOverlay) })
            if !geometry.isEmpty { var coordinates = geometry; map.addOverlay(MKPolyline(coordinates: &coordinates, count: coordinates.count)) }
        }
        map.removeAnnotations(map.annotations)
        for (index, point) in points.enumerated() {
            let pin = MKPointAnnotation(); pin.coordinate = point.coordinate; pin.title = "\(index). \(point.name)"; map.addAnnotation(pin)
        }
        if let position {
            let pin = MKPointAnnotation(); pin.coordinate = position; pin.title = "当前位置"; map.addAnnotation(pin)
            map.setRegion(MKCoordinateRegion(center: position, latitudinalMeters: 700, longitudinalMeters: 700), animated: false)
        } else if !context.coordinator.fitted || context.coordinator.fittedKey != key {
            context.coordinator.fitted = true; context.coordinator.fittedKey = key
            let coords = geometry.isEmpty ? points.map(\.coordinate) : geometry
            let rect = coords.reduce(MKMapRect.null) { $0.union(MKMapRect(origin: MKMapPoint($1), size: MKMapSize(width: 1, height: 1))) }
            map.setVisibleMapRect(rect, edgePadding: UIEdgeInsets(top: 40, left: 40, bottom: 40, right: 40), animated: false)
        }
    }
    final class Coordinator: NSObject, MKMapViewDelegate {
        var key: String?; var fittedKey: String?; var fitted = false
        func mapView(_ mapView: MKMapView, rendererFor overlay: MKOverlay) -> MKOverlayRenderer {
            if let tile = overlay as? MKTileOverlay { return MKTileOverlayRenderer(tileOverlay: tile) }
            if let line = overlay as? MKPolyline { let renderer = MKPolylineRenderer(polyline: line); renderer.strokeColor = UIColor(Palette.green); renderer.lineWidth = 5; return renderer }
            return MKOverlayRenderer(overlay: overlay)
        }
    }
}
struct WalkingGoogleMap: UIViewRepresentable {
    var points: [WalkingPoint]; var geometry: [CLLocationCoordinate2D]; var position: CLLocationCoordinate2D?
    func makeUIView(context: Context) -> GMSMapView { GMSMapView(options: GMSMapViewOptions()) }
    func updateUIView(_ map: GMSMapView, context: Context) {
        map.clear()
        for (index, point) in points.enumerated() { let pin = GMSMarker(position: point.coordinate); pin.title = "\(index). \(point.name)"; pin.map = map }
        let path = GMSMutablePath(); geometry.forEach { path.add($0) }
        let line = GMSPolyline(path: path); line.strokeColor = UIColor(Palette.green); line.strokeWidth = 5; line.map = map
        if let position { let pin = GMSMarker(position: position); pin.title = "当前位置"; pin.map = map; map.camera = GMSCameraPosition(target: position, zoom: 16) }
        else if let first = points.first { var bounds = GMSCoordinateBounds(coordinate: first.coordinate, coordinate: first.coordinate); points.forEach { bounds = bounds.includingCoordinate($0.coordinate) }; map.moveCamera(GMSCameraUpdate.fit(bounds, withPadding: 40)) }
    }
}

/// Stable activity completion is independent of route revisions and stop durations.
enum JourneyExecution {
    static func key(tripID:String,date:String)->String {"journey.completed."+tripID+"."+date}
    static func completed(tripID:String,date:String)->Set<String>{Set(UserDefaults.standard.stringArray(forKey:key(tripID:tripID,date:date)) ?? [])}
    static func skipped(tripID:String,date:String)->Set<String>{Set(UserDefaults.standard.stringArray(forKey:"journey.skipped."+tripID+"."+date) ?? [])}
    static func positionKey(tripID:String,date:String)->String {"journey.position."+tripID+"."+date}
}
struct RouteProjection {
    var distance:Double;var remaining:Double;var fraction:Double
    static func acceptsArrival(distanceToRoadEnd:Double,remaining:Double,accuracy:Double)->Bool {distanceToRoadEnd<15 && remaining<40 && accuracy>=0 && accuracy<=20}
    static func locate(_ coordinate:CLLocationCoordinate2D,on points:[CLLocationCoordinate2D])->Self {
        guard points.count>1 else{return .init(distance:.infinity,remaining:0,fraction:0)}
        let p=MKMapPoint(coordinate);var total=0.0;var best=Double.infinity;var along=0.0;var travelled=0.0
        for i in 0..<(points.count-1) {
            let a=MKMapPoint(points[i]),b=MKMapPoint(points[i+1]);let dx=b.x-a.x,dy=b.y-a.y
            let t=max(0,min(1,((p.x-a.x)*dx+(p.y-a.y)*dy)/max(0.000001,dx*dx+dy*dy)))
            let q=MKMapPoint(x:a.x+t*dx,y:a.y+t*dy);let length=a.distance(to:b);let distance=p.distance(to:q)
            if distance<best {best=distance;along=travelled+t*length};travelled+=length;total+=length
        }
        return .init(distance:best,remaining:max(0,total-along),fraction:total>0 ? along/total:1)
    }
}
extension WalkingNavigator {
    private func previewApple() {
        pause();busy=true;message=nil;let token=generation
        task=Task {do {
            var all:[CLLocationCoordinate2D]=[];var meters=0.0
            for i in 0..<(points.count-1) {
                let a=Coordinate(name:points[i].name,lat:points[i].lat,lng:points[i].lng),b=Coordinate(name:points[i+1].name,lat:points[i+1].lat,lng:points[i+1].lng)
                let observation=try await AppleWalkingRoads.shared.observation(a,b)
                if observation.unreachable==true{throw navigationError("有路段无法步行，请修改安排。")}
                meters+=observation.meters
                if observation.meters>0 {let route=try await AppleWalkingRoads.shared.route(a,b);all+=Self.coordinates(route)}
            }
            guard generation==token else{return};geometry=all;remaining="全天步行约 \(Int(meters)) 米（不含停留）";busy=false
        }catch {if generation==token{pause();message=error.localizedDescription}}}
    }
    private static func coordinates(_ route:MKRoute)->[CLLocationCoordinate2D] {
        let p=route.polyline.points();return (0..<route.polyline.pointCount).map{p[$0].coordinate}
    }
    private func say(_ text:String) {guard !muted,text != lastSpoken else{return};lastSpoken=text;let line=AVSpeechUtterance(string:uiText(text));let language=AppLanguage.current;line.voice=AVSpeechSynthesisVoice(language:language == .english ? "en-AU":language == .traditional ? "zh-TW":"zh-CN");speech.stopSpeaking(at:.immediate);speech.speak(line)}
    private func startApple() {
        pause();busy=true;message=nil;lastSpoken="";let token=generation
        task=Task {do {
            try Task.checkCancellation()
            let origin=points[progress.leg];var actual=origin.coordinate;var initialAccuracy=5.0
            var live:CoreLocationProvider?
            if !simulation {
                guard CLLocationManager.locationServicesEnabled() else{throw navigationError("请开启定位服务，或使用模拟导航。")}
                let source=CoreLocationProvider(activityType:.fitness,allowBackgroundLocationUpdates:false);live=source;provider=source;source.startUpdating()
                var fix:FerrostarCoreFFI.UserLocation?
                for _ in 0..<150 {
                    try Task.checkCancellation()
                    if source.authorizationStatus == .denied || source.authorizationStatus == .restricted {throw navigationError("请在设置允许精确定位，或使用模拟导航。")}
                    if let value=source.lastLocation,value.horizontalAccuracy>=0,value.horizontalAccuracy<=50,abs(value.timestamp.timeIntervalSinceNow)<30{fix=value;break}
                    try await Task.sleep(for:.milliseconds(200))
                }
                guard let fix else{throw navigationError("尚未取得准确位置，请到开阔处重试。")};actual=fix.coordinates.clLocationCoordinate2D;initialAccuracy=fix.horizontalAccuracy
            }
            if !simulation {UserDefaults.standard.set(["lat":actual.latitude,"lng":actual.longitude,"at":Date().timeIntervalSince1970],forKey:executionKey.replacingOccurrences(of:"journey.completed.",with:"journey.position."))}
            let destination=Coordinate(name:target.name,lat:target.lat,lng:target.lng)
            if initialAccuracy<=20 && CLLocation(latitude:actual.latitude,longitude:actual.longitude).distance(from:CLLocation(latitude:destination.lat,longitude:destination.lng))<10 {position=actual;arrived();return}
            var route=try await AppleWalkingRoads.shared.route(Coordinate(name:"当前位置",lat:actual.latitude,lng:actual.longitude),destination,force:!simulation)
            guard generation==token else{return}
            geometry=Self.coordinates(route);active=true;busy=false;UIApplication.shared.isIdleTimerDisabled=true
            var simulatedIndex=0;var offRouteCount=0;var lastReroute=Date.distantPast;var arrivalFixes=0
            while generation==token && !Task.isCancelled {
                let accuracy:Double
                if simulation {guard !geometry.isEmpty else{throw navigationError("路线没有可用道路形状。")};actual=geometry[min(simulatedIndex,geometry.count-1)];simulatedIndex=min(geometry.count-1,simulatedIndex+1);accuracy=5}
                else {guard let fix=live?.lastLocation,abs(fix.timestamp.timeIntervalSinceNow)<30,fix.horizontalAccuracy>=0,fix.horizontalAccuracy<=50 else{message="定位暂不准确，正在等待恢复…";try await Task.sleep(for:.seconds(1));continue};actual=fix.coordinates.clLocationCoordinate2D;accuracy=fix.horizontalAccuracy}
                position=actual;message=nil
                if !simulation {let key=executionKey.replacingOccurrences(of:"journey.completed.",with:"journey.position.");UserDefaults.standard.set(["lat":actual.latitude,"lng":actual.longitude,"at":Date().timeIntervalSince1970],forKey:key)}
                let projection=RouteProjection.locate(actual,on:geometry)
                let endpoint=geometry.last ?? destination.location
                let distance=CLLocation(latitude:actual.latitude,longitude:actual.longitude).distance(from:CLLocation(latitude:endpoint.latitude,longitude:endpoint.longitude))
                if RouteProjection.acceptsArrival(distanceToRoadEnd:distance,remaining:projection.remaining,accuracy:accuracy) {arrivalFixes+=1}else{arrivalFixes=0}
                if arrivalFixes>=2 {arrived();return}
                let steps=route.steps.filter{!$0.instructions.isEmpty}
                let next=steps.first{step in guard step.polyline.pointCount>0 else{return false};return RouteProjection.locate(step.polyline.points()[step.polyline.pointCount-1].coordinate,on:geometry).remaining<projection.remaining-5} ?? steps.last
                instruction=NavigationInstruction.display(next?.instructions ?? "沿路线步行前往"+target.name,remaining:projection.remaining,target:target.name)
                remaining="剩余 \(Int(projection.remaining)) 米 · 约 \(max(1,Int(ceil(route.expectedTravelTime*(1-projection.fraction)/60)))) 分钟";say(instruction)
                if !simulation && projection.distance>max(40,accuracy*1.5){offRouteCount+=1}else{offRouteCount=0}
                if offRouteCount>=3 && Date().timeIntervalSince(lastReroute)>20 {
                    instruction="偏离路线，正在重新查询…";lastReroute=Date()
                    do {route=try await AppleWalkingRoads.shared.route(Coordinate(name:"当前位置",lat:actual.latitude,lng:actual.longitude),destination,force:true);geometry=Self.coordinates(route);offRouteCount=0;lastSpoken=""}
                    catch {message="重算失败，保留当前路线；稍后自动重试，也可暂停后重试。"}
                }
                try await Task.sleep(for:simulation ? .milliseconds(150):.seconds(1))
            }
        }catch {guard generation==token else{return};pause();message=Task.isCancelled ? nil:error.localizedDescription}}
    }
}

 enum NavigationInstruction {
    static func display(_ raw:String,remaining:Double,target:String)->String {
        let arrival=raw.contains("到达") || raw.contains("目的地在") || raw.lowercased().contains("destination") || raw.lowercased().contains("arrive")
        return arrival && remaining>25 ? "沿路线继续前往"+target+"，剩余约 \(Int(remaining)) 米":raw
    }
}
