import SwiftUI
import GoogleMaps
import MapKit

struct GoogleRouteMap: UIViewRepresentable {
    var day: DayPlan?
    var overview: OverviewRoute? = nil
    var expanded = false
    var viewport: MapViewport?
    var onViewport: (MapViewport) -> Void
    var sections:[JourneyMapSection] = []
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    final class Coordinator: NSObject, GMSMapViewDelegate {
        var parent: GoogleRouteMap
        var sceneKey: String?
        var cameraSceneKey: String?
        init(_ parent: GoogleRouteMap) { self.parent=parent }
        func mapView(_ mapView: GMSMapView, idleAt position: GMSCameraPosition) {
            guard sceneKey != nil else {return}
            parent.onViewport(MapViewport(sceneKey:mapSceneKey(parent.day),lat:position.target.latitude,lng:position.target.longitude,zoom:Double(position.zoom)))
        }
    }
    func makeUIView(context: Context) -> GMSMapView {
        let options=GMSMapViewOptions();options.camera=GMSCameraPosition(latitude:-33.865,longitude:151.212,zoom:14)
        let map=GMSMapView(options:options);map.settings.compassButton=true;map.settings.rotateGestures=false;map.delegate=context.coordinator
        map.padding=expanded ? UIEdgeInsets(top:90,left:25,bottom:160,right:25) : UIEdgeInsets(top:260,left:25,bottom:380,right:25)
        return map
    }
    func updateUIView(_ map:GMSMapView,context:Context) {
        context.coordinator.parent=self
        let key=mapSceneKey(day)+"|"+String(overview?.coordinates.count ?? 0)
        guard context.coordinator.sceneKey != key else {return}
        context.coordinator.sceneKey=key
        map.clear()
        guard let day else {
            if let saved=viewport, saved.sceneKey==mapSceneKey(day) {map.camera=GMSCameraPosition(latitude:saved.lat,longitude:saved.lng,zoom:Float(saved.zoom))}
            return
        }
        var bounds=GMSCoordinateBounds(coordinate:day.start.location,coordinate:day.end.location)
        let paths = day.legs.compactMap { GMSPath(fromEncodedPath: $0.polyline) }.filter { $0.count() > 1 }
        let actual = overview != nil || (paths.count == day.stops.count+1)
        let path=GMSMutablePath()
        if let overview { overview.coordinates.forEach { path.add($0) } }
        else if actual { paths.forEach { leg in for i in 0..<leg.count() { path.add(leg.coordinate(at:i)) } } }
        else { path.add(day.start.location); day.stops.forEach {path.add($0.coordinate)}; path.add(day.end.location) }
        let casing=GMSPolyline(path:path);casing.strokeColor = .white;casing.strokeWidth=9;casing.map=map
        let line=GMSPolyline(path:path);line.strokeColor=UIColor(Palette.green);line.strokeWidth=5
        if !actual {line.spans=GMSStyleSpans(path,[GMSStrokeStyle.solidColor(UIColor(Palette.green)),GMSStrokeStyle.solidColor(.clear)],[25,15],.rhumb)}
        line.map=map
        if !sections.isEmpty {
            casing.map=nil;line.map=nil
            for section in sections where section.coordinates.count>1 {
                let sectionPath=GMSMutablePath();section.coordinates.forEach{sectionPath.add($0)}
                let border=GMSPolyline(path:sectionPath);border.strokeColor = .white;border.strokeWidth=9;border.map=map
                let stroke=GMSPolyline(path:sectionPath);stroke.strokeColor=UIColor(transportColor(section.mode));stroke.strokeWidth=5
                if section.mode == "walk" {stroke.spans=GMSStyleSpans(sectionPath,[GMSStrokeStyle.solidColor(.systemBlue),GMSStrokeStyle.solidColor(.clear)],[8,10],.rhumb)}
                stroke.map=map
            }
        }
        for item in overviewPins(day) {
            let pin=GMSMarker(position:item.coordinate);pin.title=item.title;pin.snippet=item.subtitle
            pin.icon=overviewBadge(item.badge);pin.groundAnchor=CGPoint(x:0.5,y:0.5);pin.zIndex=2;pin.map=map
            bounds=bounds.includingCoordinate(item.coordinate)
        }
        let coordinates=(0..<path.count()).map {path.coordinate(at:$0)}
        for item in overviewDirections(coordinates) {
            let pin=GMSMarker(position:item.coordinate);pin.icon=overviewBadge(item.badge,direction:true)
            pin.groundAnchor=CGPoint(x:0.5,y:0.5);pin.zIndex=1;pin.map=map
        }
        coordinates.forEach {bounds=bounds.includingCoordinate($0)}
        let cameraKey=key
        guard context.coordinator.cameraSceneKey != cameraKey else {return}
        context.coordinator.cameraSceneKey=cameraKey
        if overview == nil,let saved=viewport, saved.sceneKey==mapSceneKey(day) {map.camera=GMSCameraPosition(latitude:saved.lat,longitude:saved.lng,zoom:Float(saved.zoom))}
        else {map.moveCamera(GMSCameraUpdate.fit(bounds,withPadding:55))}
    }
}
enum MapProvider: String, CaseIterable, Identifiable {
    case osm, apple, google, schematic
    var id:String {rawValue}
    var label:String {switch self {
        case .osm:return "OpenStreetMap"
        case .apple:return "Apple Maps"
        case .google:return "Google Maps"
        case .schematic:return "离线示意图"
    }}
}
struct MapViewport { var sceneKey:String; var lat:Double; var lng:Double; var zoom:Double; var latitudeDelta:Double? = nil; var longitudeDelta:Double? = nil }
func mapSceneKey(_ day:DayPlan?) -> String {
    guard let day else {return "overview"}
    let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys]
    return (try? encoder.encode(day).base64EncodedString()) ?? day.date
}

/// OSM replaces MapKit's base tiles; requests identify this app and honour HTTP caching.
final class OsmTiles: MKTileOverlay {
    private static let session:URLSession = {
        let config=URLSessionConfiguration.default
        config.urlCache=URLCache(memoryCapacity:16*1024*1024,diskCapacity:64*1024*1024,diskPath:"sydney-osm")
        config.requestCachePolicy = .useProtocolCachePolicy
        config.httpAdditionalHeaders=["User-Agent":"SydneyBuddy/1.0 (iOS; https://github.com/f1sh11/hackathon)"]
        config.timeoutIntervalForRequest=15
        return URLSession(configuration:config)
    }()
    init() {
        super.init(urlTemplate:"https://tile.openstreetmap.org/{z}/{x}/{y}.png")
        canReplaceMapContent=true;minimumZ=0;maximumZ=19
        tileSize=CGSize(width:256,height:256)
    }
    override func loadTile(at path:MKTileOverlayPath,result:@escaping (Data?,Error?)->Void) {
        let request=URLRequest(url:url(forTilePath:path),cachePolicy:.useProtocolCachePolicy)
        Self.session.dataTask(with:request) { data,response,error in
            if let error {result(nil,error);return}
            guard (response as? HTTPURLResponse)?.statusCode==200,let data else {
                result(nil,NSError(domain:"OsmTiles",code:1,userInfo:[NSLocalizedDescriptionKey:"图砖暂不可用"]));return
            }
            result(data,nil)
        }.resume()
    }
}

struct NativeRouteMap:UIViewRepresentable {
    var day:DayPlan?
    var overview:OverviewRoute? = nil
    var expanded = false
    var provider:MapProvider
    var viewport:MapViewport?
    var onViewport:(MapViewport)->Void
    var sections:[JourneyMapSection] = []
    func makeCoordinator()->Coordinator {Coordinator(self)}
    func makeUIView(context:Context)->MKMapView {
        let map=MKMapView();map.delegate=context.coordinator
        map.showsUserLocation=false;map.isPitchEnabled=false;map.isRotateEnabled=false
        map.pointOfInterestFilter = .excludingAll
        map.layoutMargins = .zero
        if provider == .osm {map.addOverlay(OsmTiles(),level:.aboveRoads)}
        return map
    }
    func updateUIView(_ map:MKMapView,context:Context) {
        context.coordinator.parent=self
        let key=mapSceneKey(day)+"|"+String(overview?.coordinates.count ?? 0)
        guard context.coordinator.sceneKey != key else {return}
        context.coordinator.sceneKey=key
        map.removeAnnotations(map.annotations)
        map.removeOverlays(map.overlays.filter {!($0 is MKTileOverlay)})
        let start=day?.start ?? .quay,end=day?.end ?? .central
        let coordinates=[start.location]+(day?.stops.map(\.coordinate) ?? [])+[end.location]
        if let day {
            map.addAnnotations(overviewPins(day))
            let geometry = overview?.coordinates ?? coordinates
            let casing=MKPolyline(coordinates:geometry,count:geometry.count);casing.title="casing"
            let line=MKPolyline(coordinates:geometry,count:geometry.count);line.title=overview == nil ? "schematic" : "walking"
            if sections.isEmpty {map.addOverlay(casing,level:.aboveLabels);map.addOverlay(line,level:.aboveLabels)}
            else {for section in sections where section.coordinates.count>1 {
                let border=MKPolyline(coordinates:section.coordinates,count:section.coordinates.count);border.title="casing"
                let stroke=MKPolyline(coordinates:section.coordinates,count:section.coordinates.count);stroke.title=section.mode
                map.addOverlay(border,level:.aboveLabels);map.addOverlay(stroke,level:.aboveLabels)
            }}
            if sections.isEmpty {map.addAnnotations(overviewDirections(geometry))}
        }
        let cameraKey=key
        guard context.coordinator.cameraSceneKey != cameraKey else {return}
        context.coordinator.cameraSceneKey=cameraKey
        if overview == nil,let saved=viewport,saved.sceneKey==mapSceneKey(day) {
            let longitudeDelta=saved.longitudeDelta ?? 360/pow(2,saved.zoom)
            let latitudeDelta=saved.latitudeDelta ?? longitudeDelta*Double(max(1,map.bounds.height)/max(1,map.bounds.width))*cos(saved.lat*Double.pi/180)
            map.setRegion(MKCoordinateRegion(center:CLLocationCoordinate2D(latitude:saved.lat,longitude:saved.lng),span:MKCoordinateSpan(latitudeDelta:latitudeDelta,longitudeDelta:longitudeDelta)),animated:false)
        } else {
            let bounds=(coordinates+(overview?.coordinates ?? [])).reduce(MKMapRect.null) {rect,coordinate in
                let point=MKMapPoint(coordinate)
                return rect.union(MKMapRect(x:point.x,y:point.y,width:1,height:1))
            }
            let padding=expanded ? UIEdgeInsets(top:110,left:45,bottom:180,right:45) : UIEdgeInsets(top:275,left:45,bottom:400,right:45)
            DispatchQueue.main.async {map.setVisibleMapRect(bounds,edgePadding:padding,animated:false)}
        }
    }
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:NativeRouteMap
        var sceneKey:String?
        var cameraSceneKey:String?
        init(_ parent:NativeRouteMap) {self.parent=parent}
        func mapView(_ mapView:MKMapView,regionDidChangeAnimated animated:Bool) {
            guard sceneKey != nil else {return}
            let center=mapView.region.center
            parent.onViewport(MapViewport(sceneKey:mapSceneKey(parent.day),lat:center.latitude,lng:center.longitude,zoom:log2(360/max(0.00001,mapView.region.span.longitudeDelta)),latitudeDelta:mapView.region.span.latitudeDelta,longitudeDelta:mapView.region.span.longitudeDelta))
        }
        func mapView(_ mapView:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            guard let pin=annotation as? ItineraryPin else {return nil}
            let id=pin.direction ? "direction" : "itinerary"
            let view=mapView.dequeueReusableAnnotationView(withIdentifier:id) ?? MKAnnotationView(annotation:pin,reuseIdentifier:id)
            view.annotation=pin;view.image=overviewBadge(pin.badge,direction:pin.direction)
            view.canShowCallout = !pin.direction;view.displayPriority = pin.direction ? .defaultLow : .required
            view.collisionMode = .circle;view.layer.zPosition = pin.direction ? 1 : 2
            return view
        }
        func mapView(_ mapView:MKMapView,rendererFor overlay:MKOverlay)->MKOverlayRenderer {
            if let tile=overlay as? MKTileOverlay {return MKTileOverlayRenderer(tileOverlay:tile)}
            if let line=overlay as? MKPolyline {
                let renderer=MKPolylineRenderer(polyline:line)
                renderer.strokeColor = line.title == "casing" ? .white : line.title == "walking" || line.title == "schematic" ? UIColor(Palette.green) : UIColor(transportColor(line.title ?? "walk"))
                renderer.lineWidth = line.title == "casing" ? 9 : 5
                renderer.lineCap = .round;renderer.lineJoin = .round
                if line.title == "schematic" {renderer.lineDashPattern=[9,6]}
                if line.title == "walk" {renderer.lineDashPattern=[1,2]}
                return renderer
            }
            return MKOverlayRenderer(overlay:overlay)
        }
    }
}

struct ProviderRouteMap:View {
    var overview: OverviewRoute? = nil
    var expanded = false
    @EnvironmentObject var store:AppStore
    var body:some View {
        // A live trip contains Google Places coordinates as well as Google route paths.
        // Keep that entire overlay on Google; changing the base never triggers provider calls.
        let visibleDay=store.trip?.mode=="live" && store.mapProvider != .google ? nil : store.currentDay
        let safeOverview=store.trip?.mode=="live" ? nil : overview
        switch store.mapProvider {
        case .osm,.apple:
            NativeRouteMap(day:visibleDay,overview:safeOverview,expanded:expanded,provider:store.mapProvider,viewport:expanded ? nil : store.mapViewport,onViewport:{if !expanded {store.mapViewport=$0}}).id(store.mapProvider)
        case .google:
            if SydneyBuddyApp.mapReady {GoogleRouteMap(day:visibleDay,overview:safeOverview,expanded:expanded,viewport:expanded ? nil : store.mapViewport,onViewport:{if !expanded {store.mapViewport=$0}})}
            else {Palette.mint.overlay(Text("Google 地图未配置密钥\n请在“我的”选择其他底图").multilineTextAlignment(.center))}
        case .schematic:SchematicMap(day:visibleDay)
        }
    }
}

struct SchematicMap: View {
    var day: DayPlan?
    var body:some View {
        GeometryReader { geo in
            ZStack {
                Color(red:0.88,green:0.93,blue:0.86)
                Path { p in p.move(to:CGPoint(x:geo.size.width*0.63,y:0));p.addCurve(to:CGPoint(x:geo.size.width,y:geo.size.height*0.62),control1:CGPoint(x:geo.size.width*0.30,y:geo.size.height*0.25),control2:CGPoint(x:geo.size.width*0.9,y:geo.size.height*0.28));p.addLine(to:CGPoint(x:geo.size.width,y:0));p.closeSubpath() }.fill(Color(red:0.65,green:0.84,blue:0.90))
                Canvas { context,size in
                    for i in 0..<10 {
                        var path=Path();let x=CGFloat(i)*62-100
                        path.move(to:CGPoint(x:x,y:0));path.addLine(to:CGPoint(x:x+190,y:size.height))
                        context.stroke(path,with:.color(.white.opacity(0.65)),lineWidth:13)
                    }
                    for i in 0..<9 {
                        var path=Path();let y=CGFloat(i)*85
                        path.move(to:CGPoint(x:0,y:y));path.addLine(to:CGPoint(x:size.width,y:y-90))
                        context.stroke(path,with:.color(.white.opacity(0.60)),lineWidth:9)
                    }
                    let items=day?.stops ?? []
                    if items.count>1 {
                        var path=Path();path.move(to:point(items[0],size))
                        for s in items.dropFirst() {path.addLine(to:point(s,size))}
                        context.stroke(path,with:.color(Palette.green),style:StrokeStyle(lineWidth:4,lineCap:.round,dash:[7,8]))
                    }
                }
                Text("SYDNEY\nHARBOUR").font(.system(size:14,weight:.semibold,design:.rounded)).tracking(3).foregroundStyle(Color(red:0.22,green:0.48,blue:0.56)).position(x:geo.size.width*0.79,y:geo.size.height*0.18)
                Image(systemName:"sailboat.fill").font(.system(size:36)).foregroundStyle(.white).position(x:geo.size.width*0.80,y:geo.size.height*0.30)
                if let stops=day?.stops {
                    ForEach(Array(stops.enumerated()),id:\.element.id) { i,stop in
                        VStack(spacing:4) { Text("\(i+1)").font(.headline.bold()).foregroundStyle(.white).frame(width:34,height:34).background(Palette.green,in:Circle()).overlay(Circle().stroke(.white,lineWidth:3)); Text(stop.name).font(.system(size:10,weight:.bold)).padding(6).background(.white,in:Capsule()) }.position(point(stop,geo.size))
                    }
                } else { VStack(spacing:15) { Robot(size:110); Text("你的悉尼小冒险\n从这里开始").font(.title2.bold()).multilineTextAlignment(.center).foregroundStyle(Palette.ink) }.offset(y:-40) }
            }
        }
    }
    func point(_ stop:Stop,_ size:CGSize) -> CGPoint {
        let stops=day?.stops ?? [stop]
        let minLat=(stops.map(\.lat).min() ?? -33.88)-0.006,maxLat=(stops.map(\.lat).max() ?? -33.85)+0.006
        let minLng=(stops.map(\.lng).min() ?? 151.20)-0.006,maxLng=(stops.map(\.lng).max() ?? 151.22)+0.006
        return CGPoint(x:45+(stop.lng-minLng)/(maxLng-minLng)*max(1,size.width-110),y:140+(maxLat-stop.lat)/(maxLat-minLat)*max(1,size.height-420))
    }
}
struct HomeView: View {
    @EnvironmentObject var store:AppStore
    @State var chat=false
    @State var details=false
    @State var expandedMap=false
    @State var journey=false
    @State var navigation=false
    @StateObject private var overview=ItineraryOverview()
    var body:some View {
        ZStack(alignment:.top) {
            ProviderRouteMap(overview:overview.key == mapSceneKey(store.currentDay) ? overview.route : nil).ignoresSafeArea(edges:.top)
            VStack(spacing:10) {
                HStack { HStack(spacing:8) { Image(systemName:"mappin.circle.fill").foregroundStyle(Palette.green); VStack(alignment:.leading,spacing:3) { Text("悉尼 · 小冒险").font(.headline); Text(store.currentDay?.date ?? "准备出发").font(.caption).foregroundStyle(Palette.muted) } }.padding(.horizontal,18).padding(.vertical,12).background(.white,in:Capsule()); Spacer(); Button {store.tab=3} label:{Image(systemName:"person.crop.circle.fill").font(.system(size:42)).foregroundStyle(Palette.green).background(.white,in:Circle())} }.padding(.horizontal,20)
                HStack { Text(store.mapProvider.label).font(.caption2.bold()).padding(.horizontal,12).padding(.vertical,6).background(.white.opacity(0.9),in:Capsule()); if store.mapProvider == .osm {Link("© OpenStreetMap contributors",destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2).padding(6).background(.white.opacity(0.95),in:Capsule())} }
                if let t=store.trip,t.days.count>1 { Picker("天数",selection:$store.dayIndex) { ForEach(t.days.indices,id:\.self) { Text("第 \($0+1) 天").tag($0) } }.pickerStyle(.segmented).padding(.horizontal,24) }
                if let t=store.trip { WeatherCard(weather:store.refreshedWeather ?? t.weather,compact:true).padding(.horizontal,20) }
                if store.trip != nil {
                    HStack {Spacer();Button {expandedMap=true} label:{Label("展开地图",systemImage:"arrow.up.left.and.arrow.down.right").font(.caption.bold()).padding(10).background(.white,in:Capsule())}}.padding(.horizontal,20)
                }
                Spacer()
                if store.trip != nil {PreparationTrail {details=true}.padding(.horizontal,20)}
                HStack(alignment:.bottom,spacing:12) {
                    VStack(alignment:.leading,spacing:12) {
                        Text(store.trip?.title ?? "悉尼一日游").font(.title3.bold()).foregroundStyle(Palette.ink)
                        if let day=store.currentDay {
                            Text("\(day.stops.count) 个地点 · 约 \(day.estimatedSteps) 步").font(.caption).foregroundStyle(Palette.muted)
                            HStack(spacing:8) { Label("A$\(Int(day.estimatedCost))",systemImage:"wallet.bifold"); Text("·");Text("\(day.finishTime) 返回") }.font(.caption.bold()).foregroundStyle(Palette.green)
                            HStack(spacing:12) {
                                Button("交通路线规划"){journey=true}
                                Button("逐段步行导航"){navigation=true}
                            }.font(.caption.bold()).tint(Palette.green).lineLimit(1).minimumScaleFactor(0.8)
                            ScrollView(.horizontal,showsIndicators:false) {
                                HStack(spacing:8) {
                                    Text("起 · \(day.start.name)")
                                    ForEach(Array(day.stops.enumerated()),id:\.offset) { i, stop in Text("→ \(i+1) · \(stop.name) \(stop.arrival)") }
                                    Text("→ 终 · \(day.end.name)")
                                }.font(.caption).foregroundStyle(Palette.green)
                            }.accessibilityLabel("行程站点顺序")
                            if store.trip?.mode != "live", store.mapProvider != .schematic {
                                Button(overview.busy ? "正在取得步行路线…" : overview.route == nil ? "显示步行路线" : "步行约 \(Int((overview.route?.meters ?? 0).rounded())) 米 · \(Int(ceil((overview.route?.seconds ?? 0)/60))) 分钟") {overview.load(day)}
                                    .font(.caption.bold()).tint(Palette.green).disabled(overview.busy || overview.route != nil)
                                if let route=overview.route {ScrollView(.horizontal,showsIndicators:false){Text(route.segments).font(.caption2).foregroundStyle(Palette.muted)}}
                                if let message=overview.message {Text(message).font(.caption2).foregroundStyle(Palette.muted)}
                            }
                        } else { Text("让 Buddy 帮你选出刚刚好的路线").font(.caption).foregroundStyle(Palette.muted) }
                        Button { if store.trip==nil {store.newTrip()} else {details=true} } label:{ HStack {Text(store.trip==nil ? "开始规划" : "查看路线").fontWeight(.bold);Spacer();Image(systemName:"arrow.up.right")}.padding(13).foregroundStyle(.white).background(Palette.green,in:RoundedRectangle(cornerRadius:14)) }
                    }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:25)).shadow(color:Palette.ink.opacity(0.08),radius:12,y:4)
                    Button {chat=true} label:{ VStack(spacing:0) {Text("问 AI").font(.headline.bold()).padding(.horizontal,18).padding(.vertical,12).foregroundStyle(.white).background(Palette.green,in:Capsule());Robot(size:80)} }.buttonStyle(.plain).accessibilityLabel("问 AI")
                }.padding(.horizontal,20)
                Text(store.trip?.mode=="live" ? (store.mapProvider == .google ? "Google 路线 · 费用与步数为估算" : "Google 行程图层已隐藏，请切换 Google 底图查看") : (overview.route == nil || store.mapProvider == .schematic ? "虚线 / 箭头为行程顺序示意 · 非实际步行路径" : "沿道路步行路线 · 不含景点停留时间")).font(.system(size:10)).foregroundStyle(Palette.ink.opacity(0.7)).padding(7).background(.white.opacity(0.85),in:Capsule())
            }.padding(.top,12).padding(.bottom,12)
        }.task(id:mapSceneKey(store.currentDay)+"|"+(store.trip?.mode ?? "")) {overview.reset(mapSceneKey(store.currentDay))}
        .sheet(isPresented:$chat){AgentView()}.sheet(isPresented:$details){TripDetailView()}
        .fullScreenCover(isPresented:$expandedMap) {
            ZStack(alignment:.top) {
                ProviderRouteMap(overview:overview.key == mapSceneKey(store.currentDay) ? overview.route : nil,expanded:true).ignoresSafeArea()
                VStack {
                    HStack {Text("全天行程顺序").font(.headline);Spacer();Button("关闭地图"){expandedMap=false}}.padding().background(.white,in:RoundedRectangle(cornerRadius:18))
                    Spacer()
                    VStack(alignment:.leading,spacing:10) {
                        Text(overview.route == nil ? "连线为顺序示意 · 非实际步行路径" : "沿道路步行路线 · 不含景点停留时间").font(.caption.bold())
                        if let day=store.currentDay {
                            ScrollView(.horizontal,showsIndicators:false) {Text("起 · \(day.start.name) → " + day.stops.enumerated().map {"\($0.offset+1) · \($0.element.name)"}.joined(separator:" → ")+" → 终 · \(day.end.name)").font(.caption)}
                            if store.trip?.mode != "live",store.mapProvider != .schematic {
                                Button(overview.busy ? "正在取得步行路线…" : overview.route == nil ? "显示步行路线" : "步行约 \(Int(overview.route!.meters)) 米 · \(Int(ceil(overview.route!.seconds/60))) 分钟") {overview.load(day)}.disabled(overview.busy || overview.route != nil)
                                if let route=overview.route {ScrollView(.horizontal,showsIndicators:false){Text(route.segments).font(.caption2)}}
                                if let message=overview.message {Text(message).font(.caption)}
                            }
                        }
                        if store.mapProvider == .osm {Link("© OpenStreetMap contributors",destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2)}
                    }.padding().frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:18))
                }.padding().tint(Palette.green)
            }
        }
        .fullScreenCover(isPresented:$journey) {
            if let day=store.currentDay,let trip=store.trip {
                if trip.mode == "live" {VStack(spacing:20){Text("Google live 行程暂不发送至开源交通服务，请选择示例行程。");Button("关闭"){journey=false}}.padding()}
                else {JourneyPlannerView(day:day,tripID:(trip.id ?? "draft")+":\(trip.revision ?? 0)",provider:store.mapProvider)}
            }
        }
        .fullScreenCover(isPresented:$navigation) {
            if let day=store.currentDay, let trip=store.trip {
                if trip.mode == "live" { NavigationStack { VStack(spacing:20) { Text("此行程含 Google 地点资料，请使用原地图查看。开源导航测试请先建立示例行程。");Button("关闭"){navigation=false} }.padding() } }
                else {WalkingNavigationView(day:day,tripID:(trip.id ?? "draft")+":\(trip.revision ?? 0)")}
            }
        }
    }
}
