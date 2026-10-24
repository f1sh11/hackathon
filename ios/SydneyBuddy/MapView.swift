import SwiftUI
import GoogleMaps
import MapKit

// Screen points, independent of Retina scale and geographic zoom.
private func walkingDotMetrics(_ size:CGSize)->(width:CGFloat,halo:CGFloat,spacing:CGFloat) {
    let shortSide=min(size.width,size.height)
    let scale=max(0.9,min(1.15,(shortSide>0 ? shortSide : 390)/390))
    return (4.4*scale,0.65*scale,10*scale)
}

struct GoogleRouteMap: UIViewRepresentable {
    var day: DayPlan?
    var overview: OverviewRoute? = nil
    var expanded = false
    var viewport: MapViewport?
    var onViewport: (MapViewport) -> Void
    var sections:[JourneyMapSection] = []
    var selectedStopID:String?=nil
    var onSelect:(String)->Void={_ in}
    var insightMarkers:[InsightMarker] = []
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    final class Coordinator: NSObject, GMSMapViewDelegate {
        var parent: GoogleRouteMap
        var sceneKey: String?
        var cameraSceneKey: String?
        var lastSelection:String?
        var markers:[GMSMarker]=[]
        var walkingLines:[GMSPolyline]=[]
        func restyleWalking(_ map:GMSMapView) {
            let metrics=walkingDotMetrics(map.bounds.size)
            let metersPerPoint=156543.03392*cos(map.camera.target.latitude * .pi/180)/pow(2,Double(map.camera.zoom))
            for line in walkingLines {
                line.strokeWidth=metrics.width
                if let path=line.path {line.spans=GMSStyleSpans(path,[GMSStrokeStyle.solidColor(UIColor(transportColor("walk"))),GMSStrokeStyle.solidColor(.clear)],[NSNumber(value:0.1*metersPerPoint),NSNumber(value:Double(metrics.spacing)*metersPerPoint)],.rhumb)}
            }
        }
        func mapView(_ mapView:GMSMapView,didTap marker:GMSMarker)->Bool {
            if let name=marker.title {
                let matches=parent.day?.stops.filter{name.contains($0.name)} ?? []
                if let stop=matches.first(where:{$0.id==parent.selectedStopID}) ?? matches.first{parent.onSelect(stop.id)}
            }
            return false
        }
        init(_ parent: GoogleRouteMap) { self.parent=parent }
        func mapView(_ mapView: GMSMapView, idleAt position: GMSCameraPosition) {
            guard sceneKey != nil else {return}
            restyleWalking(mapView)
            parent.onViewport(MapViewport(sceneKey:mapSceneKey(parent.day),lat:position.target.latitude,lng:position.target.longitude,zoom:Double(position.zoom)))
        }
    }
    func makeUIView(context: Context) -> GMSMapView {
        let options=GMSMapViewOptions();options.camera=GMSCameraPosition(latitude:-33.865,longitude:151.212,zoom:14)
        let map=GMSMapView(options:options);map.settings.compassButton=true;map.settings.rotateGestures=false;map.delegate=context.coordinator
        map.padding=expanded ? UIEdgeInsets(top:90,left:25,bottom:160,right:25) : UIEdgeInsets(top:140,left:25,bottom:240,right:25)
        return map
    }
    func updateUIView(_ map:GMSMapView,context:Context) {
        context.coordinator.parent=self
        if context.coordinator.lastSelection != selectedStopID {
            context.coordinator.lastSelection=selectedStopID
            if let stop=day?.stops.first(where:{$0.id==selectedStopID}),let marker=context.coordinator.markers.first(where:{$0.title?.contains(stop.name)==true}){map.selectedMarker=marker;map.animate(toLocation:stop.coordinate)}
        }
        let key=mapSceneKey(day)+"|"+String(overview?.coordinates.count ?? 0)+insightMarkers.map{$0.id+$0.title}.joined()
        guard context.coordinator.sceneKey != key else {return}
        context.coordinator.sceneKey=key
        map.clear();context.coordinator.markers=[]
        for item in insightMarkers {let pin=GMSMarker(position:.init(latitude:item.lat,longitude:item.lng));pin.title=(item.kind == "price" ? "💰 ":"⚠️ ")+item.title;pin.snippet=item.summary+" · "+item.source;pin.icon=GMSMarker.markerImage(with:item.kind == "price" ? .systemGreen:.systemOrange);pin.map=map}
        context.coordinator.walkingLines=[]
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

        let line=GMSPolyline(path:path);line.strokeColor=UIColor(transportColor("walk"));line.strokeWidth=2
        if path.count()>1 {line.spans=GMSStyleSpans(path,[GMSStrokeStyle.solidColor(UIColor(transportColor("walk"))),GMSStrokeStyle.solidColor(.clear)],[1,7],.rhumb)}
        line.map=map
        context.coordinator.walkingLines.append(line)
        if !sections.isEmpty {
            line.map=nil
            context.coordinator.walkingLines=[]
            for section in sections where section.coordinates.count>1 {
                let sectionPath=GMSMutablePath();section.coordinates.forEach{sectionPath.add($0)}
                if section.mode != "walk" {let border=GMSPolyline(path:sectionPath);border.strokeColor = .white;border.strokeWidth=5;border.map=map}
                let stroke=GMSPolyline(path:sectionPath);stroke.strokeColor=UIColor(transportColor(section.mode));stroke.strokeWidth=section.mode == "walk" ? 2 : 3
                if section.mode == "walk" {stroke.spans=GMSStyleSpans(sectionPath,[GMSStrokeStyle.solidColor(UIColor(transportColor("walk"))),GMSStrokeStyle.solidColor(.clear)],[1,7],.rhumb)}
                stroke.map=map
                if section.mode == "walk" {context.coordinator.walkingLines.append(stroke)}
            }
        }
        context.coordinator.restyleWalking(map)
        for item in sections.flatMap(\.stopPins)+overviewPins(day) {
            let pin=GMSMarker(position:item.coordinate);pin.title=item.title;pin.snippet=item.subtitle
            pin.icon=overviewBadge(item.badge);pin.groundAnchor=CGPoint(x:0.5,y:0.5);pin.zIndex=2;pin.map=map;context.coordinator.markers.append(pin)
            bounds=bounds.includingCoordinate(item.coordinate)
        }
        let coordinates=(0..<path.count()).map {path.coordinate(at:$0)}
        for item in (sections.isEmpty ? overviewDirections(coordinates) : []) {
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

/// Project the supplied roads into the actual viewport, then sample in screen points.
/// This avoids MapKit's tiled overlay zoom scaling and merges coincident return paths.
private final class WalkingDotsView:UIView {
    weak var map:MKMapView?
    var paths:[[CLLocationCoordinate2D]]=[]
    init(map:MKMapView) {
        self.map=map;super.init(frame:map.bounds)
        isOpaque=false;backgroundColor = .clear;isUserInteractionEnabled=false
        autoresizingMask=[.flexibleWidth,.flexibleHeight]
    }
    required init?(coder:NSCoder) {fatalError("init(coder:) has not been implemented")}
    override func draw(_ rect:CGRect) {
        guard let map,let context=UIGraphicsGetCurrentContext() else {return}
        let dots=walkingDotMetrics(bounds.size),radius=dots.width/2
        let exclusion=map.annotations.compactMap{$0 as? ItineraryPin}.map{(map.convert($0.coordinate,toPointTo:self),$0.direction ? CGFloat(15) : CGFloat(24))}
        // Deduplicate nearby samples from outward/return legs using screen-space buckets.
        let separation=dots.spacing*0.65
        var buckets:[String:[CGPoint]]=[:]
        func key(_ x:Int,_ y:Int)->String {"\(x),\(y)"}
        func paint(_ point:CGPoint) {
            guard bounds.insetBy(dx:-radius,dy:-radius).contains(point),
                  !exclusion.contains(where:{hypot($0.0.x-point.x,$0.0.y-point.y)<$0.1}) else {return}
            let x=Int(floor(point.x/separation)),y=Int(floor(point.y/separation))
            for dx in -1...1 {for dy in -1...1 {
                if buckets[key(x+dx,y+dy),default:[]].contains(where:{hypot($0.x-point.x,$0.y-point.y)<separation}) {return}
            }}
            buckets[key(x,y),default:[]].append(point)
            context.setFillColor(UIColor.white.cgColor)
            context.fillEllipse(in:CGRect(x:point.x-radius-dots.halo,y:point.y-radius-dots.halo,width:2*(radius+dots.halo),height:2*(radius+dots.halo)))
            context.setFillColor(UIColor(transportColor("walk")).cgColor)
            context.fillEllipse(in:CGRect(x:point.x-radius,y:point.y-radius,width:2*radius,height:2*radius))
        }
        for path in paths where path.count>1 {
            let points=path.map{map.convert($0,toPointTo:self)}
            var remaining:CGFloat=0
            for (a,b) in zip(points,points.dropFirst()) {
                let length=hypot(b.x-a.x,b.y-a.y)
                guard length.isFinite,length>0 else {continue}
                // Ignore distant segments without losing sampling phase.
                let limit=min(length,remaining+dots.spacing*8000)
                while remaining<=limit {
                    let t=remaining/length
                    paint(CGPoint(x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t))
                    remaining+=dots.spacing
                }
                remaining=(remaining-length).truncatingRemainder(dividingBy:dots.spacing)
                if remaining<0 {remaining+=dots.spacing}
            }
        }
    }
}

final class InsightMapPin:MKPointAnnotation {var kind:String?}

struct NativeRouteMap:UIViewRepresentable {
    var day:DayPlan?
    var overview:OverviewRoute? = nil
    var expanded = false
    var provider:MapProvider
    var viewport:MapViewport?
    var onViewport:(MapViewport)->Void
    var sections:[JourneyMapSection] = []
    var selectedStopID:String?=nil
    var onSelect:(String)->Void={_ in}
    var insightMarkers:[InsightMarker] = []
    func makeCoordinator()->Coordinator {Coordinator(self)}
    func makeUIView(context:Context)->MKMapView {
        let map=MKMapView();map.delegate=context.coordinator
        map.showsUserLocation=false;map.isPitchEnabled=false;map.isRotateEnabled=false
        map.pointOfInterestFilter = .excludingAll
        map.layoutMargins = .zero
        if provider == .osm {map.addOverlay(OsmTiles(),level:.aboveRoads)}
        let dots=WalkingDotsView(map:map);context.coordinator.walkingDots=dots;map.addSubview(dots)
        return map
    }
    func updateUIView(_ map:MKMapView,context:Context) {
        context.coordinator.parent=self
        if context.coordinator.lastSelection != selectedStopID {
            context.coordinator.lastSelection=selectedStopID
            if let stop=day?.stops.first(where:{$0.id==selectedStopID}),let pin=map.annotations.first(where:{($0.title ?? nil)?.contains(stop.name)==true}){
                map.selectAnnotation(pin,animated:true);map.setCenter(stop.coordinate,animated:true)
            }
        }
        let key=mapSceneKey(day)+"|"+String(overview?.coordinates.count ?? 0)+insightMarkers.map{$0.id+$0.title}.joined()
        guard context.coordinator.sceneKey != key else {return}
        context.coordinator.sceneKey=key
        context.coordinator.walkingDots?.paths=sections.isEmpty ? [overview?.coordinates ?? []] : sections.filter{$0.mode == "walk"}.map(\.coordinates)
        context.coordinator.walkingDots?.setNeedsDisplay()
        map.removeAnnotations(map.annotations)
        for item in insightMarkers {let pin=InsightMapPin();pin.kind=item.kind;pin.coordinate = .init(latitude:item.lat,longitude:item.lng);pin.title=(item.kind == "price" ? "💰 ":"⚠️ ")+item.title;pin.subtitle=item.summary+" · "+item.source;map.addAnnotation(pin)}
        map.removeOverlays(map.overlays.filter {!($0 is MKTileOverlay)})
        let start=day?.start ?? .quay,end=day?.end ?? .central
        let coordinates=[start.location]+(day?.stops.map(\.coordinate) ?? [])+[end.location]
        if let day {
            map.addAnnotations(sections.flatMap(\.stopPins)+overviewPins(day))
            let geometry = overview?.coordinates ?? []
            let line=MKPolyline(coordinates:geometry,count:geometry.count);line.title="walk"
            if sections.isEmpty && geometry.count>1 {
                let border=MKPolyline(coordinates:geometry,count:geometry.count);border.title="walk-casing"
                map.addOverlay(border,level:.aboveLabels);map.addOverlay(line,level:.aboveLabels)
            }
            else {for section in sections where section.coordinates.count>1 {
                let border=MKPolyline(coordinates:section.coordinates,count:section.coordinates.count);border.title=section.mode == "walk" ? "walk-casing" : "casing"
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
            let padding=expanded ? UIEdgeInsets(top:110,left:45,bottom:180,right:45) : UIEdgeInsets(top:150,left:35,bottom:240,right:35)
            DispatchQueue.main.async {map.setVisibleMapRect(bounds,edgePadding:padding,animated:false)}
        }
    }
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:NativeRouteMap
        fileprivate var walkingDots:WalkingDotsView?
        var sceneKey:String?
        var cameraSceneKey:String?
        var lastSelection:String?
        func mapView(_ mapView:MKMapView,didSelect view:MKAnnotationView){
            guard let pin=view.annotation as? ItineraryPin,!pin.direction,let title=pin.title else{return}
            let matches=parent.day?.stops.filter{title.contains($0.name)} ?? []
            guard let stop=matches.first(where:{$0.id==parent.selectedStopID}) ?? matches.first else{return}
            view.transform=CGAffineTransform(scaleX:1.15,y:1.15);parent.onSelect(stop.id)
        }
        func mapView(_ mapView:MKMapView,didDeselect view:MKAnnotationView){view.transform = .identity}
        init(_ parent:NativeRouteMap) {self.parent=parent}
        func mapView(_ mapView:MKMapView,regionDidChangeAnimated animated:Bool) {
            guard sceneKey != nil else {return}
            walkingDots?.setNeedsDisplay()
            let center=mapView.region.center
            parent.onViewport(MapViewport(sceneKey:mapSceneKey(parent.day),lat:center.latitude,lng:center.longitude,zoom:log2(360/max(0.00001,mapView.region.span.longitudeDelta)),latitudeDelta:mapView.region.span.latitudeDelta,longitudeDelta:mapView.region.span.longitudeDelta))
        }
        func mapViewDidChangeVisibleRegion(_ mapView:MKMapView) {walkingDots?.setNeedsDisplay()}
        func mapView(_ mapView:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            if let pin=annotation as? InsightMapPin {
                let view=(mapView.dequeueReusableAnnotationView(withIdentifier:"insight") as? MKMarkerAnnotationView) ?? MKMarkerAnnotationView(annotation:pin,reuseIdentifier:"insight")
                view.annotation=pin;view.markerTintColor=pin.kind == "price" ? .systemGreen:.systemOrange;view.glyphText=pin.kind == "price" ? "$":"!";view.canShowCallout=true;return view
            }
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
                if line.title == "walk" || line.title == "walk-casing" {
                    return MKOverlayRenderer(overlay:line)
                }
                let renderer=MKPolylineRenderer(polyline:line)
                renderer.strokeColor = line.title == "casing" || line.title == "walk-casing" ? .white : line.title == "walking" || line.title == "schematic" ? UIColor(Palette.green) : UIColor(transportColor(line.title ?? "walk"))
                let dots=walkingDotMetrics(mapView.bounds.size)
                renderer.lineWidth = line.title == "casing" ? 5 : line.title == "walk-casing" ? dots.width+2*dots.halo : line.title == "walk" ? dots.width : 3
                renderer.lineCap = .round;renderer.lineJoin = .round
                if line.title == "schematic" {renderer.lineDashPattern=[9,6]}
                if line.title == "walk" || line.title == "walk-casing" {renderer.lineDashPattern=[0.1,NSNumber(value:Double(dots.spacing))]}
                return renderer
            }
            return MKOverlayRenderer(overlay:overlay)
        }
    }
}

struct ProviderRouteMap:View {
    var overview: OverviewRoute? = nil
    var sections:[JourneyMapSection] = []
    var overviewIsGoogle=false
    var expanded = false
    @EnvironmentObject var store:AppStore
    var body:some View {
        // A live trip contains Google Places coordinates as well as Google route paths.
        // Keep that entire overlay on Google; changing the base never triggers provider calls.
        let visibleDay=store.trip?.mode=="live" && store.mapProvider != .google ? nil : store.currentDay
        let canShowOverview = (!overviewIsGoogle || store.mapProvider == .google) && (store.trip?.mode != "live" || overviewIsGoogle)
        let safeOverview = canShowOverview ? overview : nil
        let safeSections = canShowOverview ? sections : []
        switch store.mapProvider {
        case .osm,.apple:
            NativeRouteMap(day:visibleDay,overview:safeOverview,expanded:expanded,provider:store.mapProvider,viewport:expanded ? nil : store.mapViewport,onViewport:{if !expanded {store.mapViewport=$0}},sections:safeSections,selectedStopID:store.selectedStopID,onSelect:{if store.selectedStopID != $0{store.selectedStopID=$0}},insightMarkers:(store.insights?.traffic.items ?? [])+(store.insights?.pricePlaces ?? [])).id(store.mapProvider)
        case .google:
            if SydneyBuddyApp.mapReady {GoogleRouteMap(day:visibleDay,overview:safeOverview,expanded:expanded,viewport:expanded ? nil : store.mapViewport,onViewport:{if !expanded {store.mapViewport=$0}},sections:safeSections,selectedStopID:store.selectedStopID,onSelect:{if store.selectedStopID != $0{store.selectedStopID=$0}},insightMarkers:(store.insights?.traffic.items ?? [])+(store.insights?.pricePlaces ?? []))}
            else {Palette.mint.overlay(Text("Google 地图未配置密钥\n请在“我的”选择其他底图").multilineTextAlignment(.center))}
        case .schematic:SchematicMap(day:visibleDay)
        }
    }
}

struct SchematicMap: View {
    var day: DayPlan?
    @EnvironmentObject private var store:AppStore
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
                        VStack(spacing:4) { Text("\(i+1)").font(.headline.bold()).foregroundStyle(.white).frame(width:34,height:34).background(Palette.green,in:Circle()).overlay(Circle().stroke(.white,lineWidth:3)); Text(stop.name).font(.system(size:10,weight:.bold)).padding(6).background(.white,in:Capsule()) }.position(point(stop,geo.size)).onTapGesture{store.selectedStopID=stop.id}.scaleEffect(store.selectedStopID==stop.id ? 1.1:1)
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
    @State var poiSearch=false
    @State private var showInsights=false
    @State private var homeInfo=false
    @State var chat=false
    @State var details=false
    @State private var adjusting=false
    @State private var undoMessage:String?
    @State var expandedMap=false
    @State var journey=false
    @State var navigation=false
    @StateObject private var overview=ItineraryOverview()
    var body:some View {
        ZStack(alignment:.top) {
            ProviderRouteMap(overview:overview.key == mapSceneKey(store.currentDay) ? overview.route : nil,sections:overview.key == mapSceneKey(store.currentDay) ? (overview.plan?.sections ?? []) : [],overviewIsGoogle:overview.plan?.source == "Google Routes").ignoresSafeArea(edges:.top)
            VStack(spacing:10) {
                HStack(spacing:8){
                    Button{poiSearch=true}label:{Label("搜尋景點、餐廳與設施",systemImage:"magnifyingglass").font(.subheadline).padding(14).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:Capsule())}
                    Menu{Picker("底圖",selection:$store.mapProvider){ForEach(MapProvider.allCases){Text($0.label).tag($0)}}}label:{Image(systemName:"square.3.layers.3d").frame(width:46,height:46).background(.white,in:Circle())}.accessibilityLabel("切換底圖")
                }
                HStack(spacing:10){
                    Text(store.language.text("悉尼 · {0}",store.currentDay?.date.suffix(5).description ?? store.language.text("準備出發"))).font(.caption.bold()).padding(.horizontal,12).padding(.vertical,9).background(.white.opacity(0.95),in:Capsule())
                    if let t=store.trip,t.days.count>1{Picker("天数",selection:$store.dayIndex){ForEach(t.days.indices,id:\.self){Text(store.language.text("第 {0} 天",String($0+1))).tag($0)}}.pickerStyle(.segmented)}
                    else{Spacer()}
                }
                Spacer(minLength:20)
                HStack{Spacer();if store.trip != nil{Button{expandedMap=true}label:{Image(systemName:"arrow.up.left.and.arrow.down.right").frame(width:42,height:42).background(.white,in:Circle())}.accessibilityLabel("展开地图")};Button{chat=true}label:{Text("🐨 聊一聊").font(.caption.bold()).padding(12).background(.white,in:Capsule())}.accessibilityLabel("问 AI")}
                VStack(alignment:.leading,spacing:10){
                    if store.trip != nil {
                        Button{withAnimation(.spring(response:0.3)){homeInfo.toggle()}}label:{
                            VStack(spacing:5){Capsule().fill(Palette.muted.opacity(0.35)).frame(width:34,height:4);Text(homeInfo ? "收起当天行程":"查看当天行程").font(.caption.weight(.medium))}.frame(maxWidth:.infinity).padding(.vertical,4)
                        }.accessibilityIdentifier("home-info-toggle").gesture(DragGesture(minimumDistance:12).onEnded{value in withAnimation{if value.translation.height < -12{homeInfo=true}else if value.translation.height > 12{homeInfo=false}}})
                    }
                    Button{showInsights=true}label:{Label(store.insightsBusy ? "正在更新旅遊資訊…" : store.insights?.traffic.summary ?? "交通・人潮・費用",systemImage:"info.circle").font(.caption).lineLimit(1)}.accessibilityIdentifier("travel-insights-open")
                    if let day=store.currentDay {
                        Text("🐨 今天安排 \(day.stops.count) 站，预计约 \(day.estimatedSteps.formatted()) 步。点活动就能在地图上找到它。").font(.caption).foregroundStyle(Palette.ink)
                        ScrollViewReader{proxy in ScrollView(.horizontal,showsIndicators:false){HStack{ForEach(Array(day.stops.enumerated()),id:\.element.id){i,stop in
                            Button{store.selectedStopID=stop.id}label:{Text("\(i+1). "+stop.name).font(.caption.bold()).padding(8).background(store.selectedStopID==stop.id ? Palette.green:Palette.mint,in:Capsule()).foregroundStyle(store.selectedStopID==stop.id ? .white:Palette.ink)}.id(stop.id).accessibilityIdentifier("trip-stop-"+stop.id)
                        }}}.onChange(of:store.selectedStopID){_,id in if let id{withAnimation{proxy.scrollTo(id,anchor:.center)}}}}
                        Text(store.trip?.mode == "live" ? "在线路线 · 出发前核实开放时间":"示例行程 · 费用和步数为估算").font(.caption2).foregroundStyle(Palette.muted)
                        Button("调整这一天",systemImage:"slider.horizontal.3"){adjusting=true}.font(.subheadline.bold())
                        if let previous=store.lastAdjustment,previous.id==store.trip?.id {Button("撤销上次调整"){Task{if await store.undoLastAdjustment(){undoMessage="已恢复上次安排"}}}.font(.caption).disabled(store.busy)}
                        if let undoMessage{Text(undoMessage).font(.caption).foregroundStyle(Palette.green)}
                    }
                    HStack(alignment:.top){
                        VStack(alignment:.leading,spacing:5){Text(store.trip == nil ? "开始你的悉尼旅行":"第 \(store.dayIndex+1) 天 · 当天概况").font(.headline).foregroundStyle(Palette.ink);if let day=store.currentDay{Text(store.language.text("{0} 站 · {1} 步 · A${2} · {3} 返回",String(day.stops.count),day.estimatedSteps.formatted(),String(Int(day.estimatedCost)),day.finishTime)).font(.caption).foregroundStyle(Palette.muted)}}
                        Spacer(minLength:4)

                    }
                    if homeInfo,let day=store.currentDay{HomeTransportStrip(day:day,overview:overview,onPlan:{journey=true},onRetry:{overview.load(day,provider:store.mapProvider)})}
                    if homeInfo,let trip=store.trip {
                        ScrollView{VStack(alignment:.leading,spacing:12){
                            WeatherCard(weather:store.currentWeather,compact:true)
                            PreparationTrail{details=true}
                            Button("交通路线规划"){journey=true}.font(.subheadline.bold())
                            if let day=store.currentDay{
                                ScrollView(.horizontal,showsIndicators:false){Text("起 · \(day.start.name) → "+day.stops.enumerated().map{"\($0.offset+1) · \($0.element.name) \($0.element.arrival)"}.joined(separator:" → ")+" → 终 · \(day.end.name)").font(.caption)}.accessibilityLabel("行程站点顺序")
                                if trip.mode != "live",store.mapProvider == .osm || store.mapProvider == .apple{Button(overview.busy ? "正在取得步行路线…":overview.summary){overview.load(day,provider:store.mapProvider)}.font(.caption.bold()).disabled(overview.busy || overview.route != nil);if let message=overview.message{Text(message).font(.caption).foregroundStyle(Palette.muted)}}
                            }
                        }}.frame(maxHeight:220).accessibilityIdentifier("home-extra-info")
                    }
                    HStack(spacing:10){
                        if store.trip != nil && homeInfo{Button{details=true}label:{Text("行程说明").font(.subheadline.bold()).frame(maxWidth:.infinity).padding(.vertical,12).background(Palette.mint,in:RoundedRectangle(cornerRadius:13))}}
                        Button{if store.trip==nil{store.newTrip()}else{navigation=true}}label:{Label(store.trip==nil ? "開始規劃":"開始導航",systemImage:"location.fill").font(.subheadline.bold()).frame(maxWidth:.infinity).padding(.vertical,12).foregroundStyle(.white).background(Palette.green,in:RoundedRectangle(cornerRadius:13))}
                    }
                    HStack(spacing:4){Text(LocalizedStringKey(store.mapProvider.label)).fontWeight(.medium);if store.mapProvider == .osm{Link("© OpenStreetMap contributors",destination:URL(string:"https://www.openstreetmap.org/copyright")!)};Spacer(minLength:0);Text(LocalizedStringKey(store.trip?.mode=="live" ? (store.mapProvider == .google ? "Google 路線":"需 Google 底圖"):store.mapProvider == .schematic ? "離線示意":overview.route==nil ? "僅顯示站點":"已規劃路線"))}.font(.system(size:9)).foregroundStyle(Palette.muted).lineLimit(1).minimumScaleFactor(0.8)
                }.padding(16).background(.white,in:RoundedRectangle(cornerRadius:24)).shadow(color:Palette.ink.opacity(0.08),radius:12,y:4)
            }.padding(.horizontal,18).padding(.top,10).padding(.bottom,10).tint(Palette.green)
        }.task(id:mapSceneKey(store.currentDay)+"|"+store.mapProvider.rawValue+"|"+(store.trip?.mode ?? "")) {if let day=store.currentDay{overview.loadHome(day,provider:store.mapProvider,contentSource:store.trip?.mode == "live" ? "google":"open")}}
        .sheet(isPresented:$adjusting){if let trip=store.trip{TripAdjustmentView(original:trip,dayIndex:store.dayIndex)}}
        .onChange(of:store.dayIndex){_,_ in store.selectedStopID=nil}
        .onChange(of:store.selectedStopID){_,value in if value != nil{withAnimation{homeInfo=false}}}
        .task(id:(store.trip?.id ?? "")+store.travelDataSource.rawValue){if store.trip != nil{await store.refreshWeather()}}
        .task(id:mapSceneKey(store.currentDay)+"|"+store.travelDataSource.rawValue+"|"+store.mapProvider.rawValue){await store.refreshInsights()}
        .sheet(isPresented:$showInsights){TravelInsightsSheet()}
        .sheet(isPresented:$poiSearch){PoiDiscoveryView()}
        .sheet(isPresented:$chat){AgentView()}.sheet(isPresented:$details){TripDetailView()}
        .fullScreenCover(isPresented:$expandedMap) {
            ZStack(alignment:.top) {
                ProviderRouteMap(overview:overview.key == mapSceneKey(store.currentDay) ? overview.route : nil,sections:overview.key == mapSceneKey(store.currentDay) ? (overview.plan?.sections ?? []) : [],overviewIsGoogle:overview.plan?.source == "Google Routes",expanded:true).ignoresSafeArea()
                VStack {
                    HStack {Text("全天行程顺序").font(.headline);Spacer();Button("关闭地图"){expandedMap=false}}.padding().background(.white,in:RoundedRectangle(cornerRadius:18))
                    Spacer()
                    VStack(alignment:.leading,spacing:10) {
                        Text(overview.route == nil ? "尚未查询道路路线 · 仅显示站点" : "已规划道路路线 · 不含景点停留时间").font(.caption.bold())
                        if let day=store.currentDay {
                            ScrollView(.horizontal,showsIndicators:false) {Text("起 · \(day.start.name) → " + day.stops.enumerated().map {"\($0.offset+1) · \($0.element.name)"}.joined(separator:" → ")+" → 终 · \(day.end.name)").font(.caption)}
                            if store.trip?.mode != "live",store.mapProvider == .osm || store.mapProvider == .apple {
                                Button(overview.busy ? "正在取得步行路线…" : overview.summary) {overview.load(day,provider:store.mapProvider)}.disabled(overview.busy || overview.route != nil)
                                if overview.plan == nil,let route=overview.route {ScrollView(.horizontal,showsIndicators:false){Text(route.segments).font(.caption2)}}
                                if let message=overview.message {Text(message).font(.caption)}
                            }
                        }
                        if store.mapProvider == .osm {Link("© OpenStreetMap contributors",destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2)}
                    }.padding().frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:18))
                }.padding().tint(Palette.green)
            }.task(id:mapSceneKey(store.currentDay)+"|"+store.mapProvider.rawValue) {
                if let day=store.currentDay,store.trip?.mode != "live",store.mapProvider == .apple || store.mapProvider == .osm {
                    overview.load(day,provider:store.mapProvider)
                }
            }
        }

        .fullScreenCover(isPresented:$journey) {
            if let day=store.currentDay,let trip=store.trip {
                if trip.mode == "live" && store.mapProvider != .google {VStack(spacing:20){Text("此行程的交通规划需搭配 Google 底图与 Google Routes，请切换底图。");Button("关闭"){journey=false}}.padding()}
                else {JourneyPlannerView(day:day,tripID:(trip.id ?? "draft")+":\(trip.revision ?? 0)",provider:store.mapProvider,contentSource:trip.mode == "live" ? "google" : "open",onPlan:{overview.accept($0,day:day)})}
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

struct TravelInsightsSheet:View {
    var places:[[String:Any]]?=nil
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) var dismiss
    var body:some View {
        NavigationStack {ScrollView {VStack(alignment:.leading,spacing:16){
            Button{Task{await store.refreshInsights(places:places)}}label:{Label(store.insightsBusy ? "更新中…":"重新查詢",systemImage:"arrow.clockwise")}.disabled(store.insightsBusy)
            if let error=store.insightsError {Text(error).foregroundStyle(.red)}
            if let report=store.insights {
                Text(report.traffic.summary).font(.headline)
                Text(report.scope).font(.caption).foregroundStyle(.secondary)
                Text("來源：\(report.dataSource) · 查詢於 \(report.fetchedAt)").font(.caption)
                ForEach(report.traffic.items){item in VStack(alignment:.leading){Text("⚠️ "+item.title).bold();Text(item.summary);Text(item.source).font(.caption)}.padding().frame(maxWidth:.infinity,alignment:.leading).background(Color.orange.opacity(0.12),in:RoundedRectangle(cornerRadius:12))}
                if let fees=report.pricePlaces,!fees.isEmpty {Text("附近參考收費").font(.headline);ForEach(fees){place in VStack(alignment:.leading){Text(place.title).bold();Text(place.summary);Text(place.source).font(.caption)}.padding().frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:12))}}
                ForEach(Array(report.places.enumerated()),id:\.offset){_,place in VStack(alignment:.leading,spacing:6){
                    Text(place.name).font(.headline)
                    Text(place.crowd.summary).foregroundStyle(.secondary)
                    Text(place.price.summary)
                    if let range=place.price.range {Text((range.startPrice?.label ?? "")+" – "+(range.endPrice?.label ?? "無上限"))}
                    Text(place.price.source).font(.caption).foregroundStyle(.secondary)
                    if let note=place.price.scope {Text(note).font(.caption)}
                    if let url=place.price.sourceUrl.flatMap(URL.init(string:)){Link("查看收費來源",destination:url)}
                }.padding().frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint.opacity(0.6),in:RoundedRectangle(cornerRadius:14))}
                Text(report.attribution).font(.caption).foregroundStyle(.secondary)
            }else if store.insightsBusy {ProgressView()}
        }.padding()}.navigationTitle("交通・人潮・費用").toolbar{ToolbarItem(placement:.confirmationAction){Button("完成"){dismiss()}}}}
        .accessibilityIdentifier("travel-insights-sheet")
    }
}
