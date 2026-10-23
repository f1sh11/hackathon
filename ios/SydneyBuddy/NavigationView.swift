import SwiftUI
import MapKit
import Combine
import CryptoKit
import FerrostarCore
import FerrostarCoreFFI
import GoogleMaps

struct WalkingPoint: Codable, Equatable {
    var name: String
    var lat: Double
    var lng: Double
    var stay: Int = 0
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
    var target: WalkingPoint { points[min(progress.leg + 1, points.count - 1)] }
    var count: Int { points.count - 1 }

    init(day: DayPlan, tripID: String, endpoint: String) {
        points = [WalkingPoint(name: day.start.name, lat: day.start.lat, lng: day.start.lng)]
            + day.stops.map { WalkingPoint(name: $0.name, lat: $0.lat, lng: $0.lng, stay: $0.visitMinutes) }
            + [WalkingPoint(name: day.end.name + "（返回）", lat: day.end.lat, lng: day.end.lng)]
        let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
        let identity = tripID + ":" + ((try? encoder.encode(day).base64EncodedString()) ?? day.date)
        storageKey = "walking:" + SHA256.hash(data: Data(identity.utf8)).map { String(format: "%02x", $0) }.joined()
        let saved = UserDefaults.standard.data(forKey: storageKey)
            .flatMap { try? JSONDecoder().decode(WalkingProgress.self, from: $0) } ?? WalkingProgress()
        let restored = saved.leg >= 0 && saved.leg < points.count - 1 ? saved : WalkingProgress()
        progress = restored
        realProgress = restored
        self.endpoint = endpoint
    }
    func setSimulation(_ value: Bool) {
        pause(); simulation = value
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
        UIApplication.shared.isIdleTimerDisabled = false
    }
    func arrived() {
        pause(); progress.arrive(); instruction = "已到达 " + target.name; remaining = ""; save()
    }
    func next() {
        guard progress.arrived else { return }
        progress.advance(legCount: count); geometry = []; position = nil; save()
        if !progress.finished { start() }
    }
    func reset() {
        pause(); progress = WalkingProgress(); geometry = []; position = nil; message = nil; save()
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
        pause(); busy = true; message = nil
        let token = generation
        task = Task {
            do {
                let source = SimulatedLocationProvider(location: CLLocation(coordinate: points[0].coordinate, altitude: 0, horizontalAccuracy: 5, verticalAccuracy: 5, timestamp: Date()))
                let engine = try FerrostarCore(wellKnownRouteProvider: .valhalla(endpointUrl: endpoint, profile: "pedestrian"), locationProvider: source, navigationControllerConfig: Self.config())
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
    @EnvironmentObject var store: AppStore
    @Environment(\.dismiss) var dismiss
    @Environment(\.scenePhase) var scenePhase
    @StateObject private var navigator: WalkingNavigator
    @State private var reset = false
    init(day: DayPlan, tripID: String) {
        _navigator = StateObject(wrappedValue: WalkingNavigator(day: day, tripID: tripID, endpoint: "http://127.0.0.1:8787/navigation/route"))
    }
    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text("第 \(navigator.progress.leg + 1) / \(navigator.count) 段 · 步行").font(.headline)
                    Text(navigator.progress.finished ? "今天的行程已完成" : "前往 " + navigator.target.name).font(.title2.bold())
                    Picker("底图", selection: $store.mapProvider) {
                        ForEach(MapProvider.allCases) { Text($0.label).tag($0) }
                    }.pickerStyle(.menu)
                    if store.mapProvider == .google {
                        if SydneyBuddyApp.mapReady { WalkingGoogleMap(points: navigator.points, geometry: navigator.geometry, position: navigator.position).frame(height: 300).clipShape(RoundedRectangle(cornerRadius: 20)) }
                        else { Text("Google 地图未配置密钥，请选择 OpenStreetMap 或 Apple Maps。") }
                    } else if store.mapProvider == .schematic {
                        Text("离线示意图不显示导航路线，请选择联网底图。")
                    } else {
                        WalkingNativeMap(points: navigator.points, geometry: navigator.geometry, position: navigator.position, osm: store.mapProvider == .osm)
                            .id(store.mapProvider).frame(height: 300).clipShape(RoundedRectangle(cornerRadius: 20))
                        if store.mapProvider == .osm { Link("© OpenStreetMap contributors", destination: URL(string: "https://www.openstreetmap.org/copyright")!).font(.caption) }
                    }
                    Toggle("模拟导航（不使用设备定位）", isOn: Binding(get: { navigator.simulation }, set: { navigator.setSimulation($0) }))
                        .disabled(navigator.busy || navigator.active)
                    if !navigator.simulation { Text("实际位置会经本机后端发送至已配置的步行路线服务，用于计算路线。").font(.caption) }
                    if navigator.simulation { Text("模拟位置 · 10 倍速度 · 不记录实际行程进度").font(.caption).foregroundStyle(.orange) }
                    if !navigator.active {
                        Button("预览整天步行路线") { navigator.preview() }.disabled(navigator.busy || store.health?.navigationConfigured != true)
                        if !navigator.remaining.isEmpty { Text(navigator.remaining).font(.caption) }
                    }
                    Text("步行路线 · 到站后可停留，再前往下一站").font(.caption).foregroundStyle(Palette.muted)
                    Link("路线数据 © OpenStreetMap contributors", destination: URL(string: "https://www.openstreetmap.org/copyright")!).font(.caption)
                    if navigator.busy { ProgressView("正在获取位置与步行路线…") }
                    if let message = navigator.message { Text(message).foregroundStyle(.red) }
                    if navigator.active {
                        Text(navigator.instruction).font(.headline)
                        Text(navigator.remaining)
                        HStack {
                            Button(navigator.muted ? "开启语音" : "静音") { navigator.toggleMute() }
                            Spacer(); Button("暂停导航") { navigator.pause() }
                        }
                        Button("我已到达") { navigator.arrived() }
                    } else if navigator.progress.finished {
                        Label("全部路段已完成", systemImage: "checkmark.circle.fill").foregroundStyle(Palette.green)
                    } else if navigator.progress.arrived {
                        Text("已到达。\(navigator.target.stay > 0 ? "建议停留 \(navigator.target.stay) 分钟。" : "")准备好再继续。")
                        PrimaryButton(title: navigator.progress.leg + 1 == navigator.count ? "完成今天行程" : "前往下一站", symbol: "arrow.right") { navigator.next() }.disabled(navigator.busy)
                    } else {
                        PrimaryButton(title: navigator.simulation ? "开始模拟导航" : "开始本段导航", symbol: "location.fill") { navigator.start() }
                            .disabled(navigator.busy || store.health?.navigationConfigured != true)
                    }
                    if store.health?.navigationConfigured != true {
                        Text("步行路线服务尚未开启，暂时无法导航。请联系开发团队。") .font(.caption)
                    }
                    DisclosureGroup("整天路线与完成进度") {
                        ForEach(Array(navigator.points.enumerated()), id: \.offset) { index, point in
                            HStack {
                                Image(systemName: index <= navigator.progress.leg || navigator.progress.finished || index == navigator.progress.leg + 1 && navigator.progress.arrived ? "checkmark.circle.fill" : "circle")
                                Text("\(index). \(point.name)"); Spacer()
                                if point.stay > 0 { Text("\(point.stay) 分钟").font(.caption) }
                            }.padding(.vertical, 8)
                        }
                    }
                    Text("保持本页开启进行导航；离开 App 会暂停，回来后可从当前位置继续。路线与语音为英文，停留时间不计入步行 ETA。") .font(.caption).foregroundStyle(Palette.muted)
                    Button("重置今天导航进度", role: .destructive) { reset = true }
                }.padding(20)
            }.background(Palette.cream).navigationTitle("逐段导航").navigationBarTitleDisplayMode(.inline)
                .toolbar { ToolbarItem(placement: .topBarTrailing) { Button("关闭") { dismiss() } } }
                .confirmationDialog("重置今天的导航进度？", isPresented: $reset) { Button("重置", role: .destructive) { navigator.reset() } }
        }
        .onDisappear { navigator.pause() }
        .onChange(of: scenePhase) { _, phase in if phase != .active { navigator.pause() } }
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
