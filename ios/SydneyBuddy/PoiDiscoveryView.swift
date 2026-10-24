import SwiftUI
import MapKit
import GoogleMaps
import CryptoKit

@MainActor final class PoiToolbox:ObservableObject {
    static let shared=PoiToolbox()
    @Published var places:[ScheduleCandidate]
    private init(){places=(UserDefaults.standard.data(forKey:"poi-toolbox").flatMap{try? JSONDecoder().decode([ScheduleCandidate].self,from:$0)}) ?? []}
    func add(_ p:ScheduleCandidate){guard !places.contains(where:{$0.id==p.id}) else{return};places.append(p);if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
}
@MainActor enum ApplePoiProvider {
    static func search(_ center:CLLocationCoordinate2D,_ category:String,_ query:String) async throws -> [ScheduleCandidate] {
        var categories:[MKPointOfInterestCategory]
        switch category {
        case "food":categories=[.restaurant,.cafe,.bakery,.foodMarket]
        case "facilities":categories=[] // MapKit has no public-toilet/drinking-water POI category on iOS 17.
        default:categories=[.museum,.park,.nationalPark,.amusementPark,.aquarium,.zoo,.theater,.library]
        }
        if #available(iOS 18.0,*),category=="sights" {categories += [.landmark,.nationalMonument,.musicVenue]}
        let search:MKLocalSearch
        if query.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty && category != "facilities" {
            let request=MKLocalPointsOfInterestRequest(center:center,radius:1500)
            request.pointOfInterestFilter=MKPointOfInterestFilter(including:categories)
            search=MKLocalSearch(request:request)
        }else {
            let request=MKLocalSearch.Request()
            request.naturalLanguageQuery=query.isEmpty ? "public toilets drinking water" : query
            request.resultTypes = .pointOfInterest
            request.region=MKCoordinateRegion(center:center,latitudinalMeters:3000,longitudinalMeters:3000)
            if !categories.isEmpty{request.pointOfInterestFilter=MKPointOfInterestFilter(including:categories)}
            search=MKLocalSearch(request:request)
        }
        let response=try await withTaskCancellationHandler {try await search.start()} onCancel:{Task{@MainActor in search.cancel()}}
        try Task.checkCancellation()
        let origin=CLLocation(latitude:center.latitude,longitude:center.longitude)
        var seen=Set<String>()
        return response.mapItems.compactMap{item in
            let point=item.placemark.coordinate
            guard CLLocationCoordinate2DIsValid(point),origin.distance(from:CLLocation(latitude:point.latitude,longitude:point.longitude))<=1500,let name=item.name,!name.isEmpty else{return nil}
            var identity=name+String(format:"|%.6f|%.6f",point.latitude,point.longitude)
            if #available(iOS 18.0,*),let identifier=item.identifier{identity=identifier.rawValue}
            let id="apple:"+SHA256.hash(data:Data(identity.utf8)).map{String(format:"%02x",$0)}.joined()
            guard seen.insert(id).inserted else{return nil}
            let address=[item.placemark.thoroughfare,item.placemark.locality,item.placemark.postalCode].compactMap{$0}.joined(separator:", ")
            // A facility keyword match is not proof of a verified public restroom.
            return ScheduleCandidate(id:id,name:name,category:category=="food" ? "美食":category=="facilities" ? "設施":"風景",indoor:false,visitMinutes:category=="food" ? 45:category=="facilities" ? 10:60,cost:0,lat:point.latitude,lng:point.longitude,reason:"Apple Maps 地點搜尋",description:address.isEmpty ? "Apple Maps 未提供詳細介紹。":address,note:"Apple Maps 地點；費用、排隊與營業時間未知。設施用途及開放情況請另行確認。",source:"Apple Maps",poiKind:category=="food" ? "restaurant":category=="facilities" ? "facility":"attraction",openingHoursText:"")
        }
    }
}
@MainActor final class PoiSearchModel:ObservableObject {
    @Published var query=""
    @Published var category="sights"
    @Published var places:[ScheduleCandidate]=[]
    @Published var selected:String?
    @Published var busy=false
    @Published var error:String?
    @Published var hasSearched=false
    @Published private(set) var provider:MapProvider = .osm
    @Published private(set) var attribution="© OpenStreetMap contributors · ODbL"
    var language="zh-TW"
    var googleMapConfigured=true
    var center=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
    var appleSearch:(CLLocationCoordinate2D,String,String) async throws -> [ScheduleCandidate] = ApplePoiProvider.search
    private var job:Task<Void,Never>?
    private var generation=UUID()
    var source:String {switch provider{case .apple:return "Apple Maps / MapKit";case .google:return "Google Places";case .osm:return "OpenStreetMap / Overpass";case .schematic:return "離線示意 · 不搜尋地點"}}
    func configure(_ value:MapProvider,googleMapConfigured:Bool=true) {
        stop();provider=value;self.googleMapConfigured=googleMapConfigured;places=[];selected=nil;error=nil;hasSearched=false
        attribution=value == .apple ? "Apple Maps":value == .google ? "Google Maps":value == .osm ? "© OpenStreetMap contributors · ODbL":""
        if value == .osm || value == .apple{search()}
    }
    func search(){
        stop();error=nil;hasSearched=true;places=[];selected=nil
        guard provider != .schematic else{error="離線示意圖不提供地點搜尋，請切換其他底圖。";return}
        guard provider != .google || googleMapConfigured else{error="Google 地圖尚未設定金鑰，請選擇其他底圖。";return}
        busy=true
        let point=center,text=query.trimmingCharacters(in:.whitespacesAndNewlines),kind=category,current=provider,token=generation
        job=Task {@MainActor in
            do {
                let result:[ScheduleCandidate]
                if current == .apple {result=try await appleSearch(point,kind,text)}
                else {
                    var request=URLRequest(url:URL(string:"http://127.0.0.1:8787/poi/search")!);request.httpMethod="POST";request.setValue("application/json",forHTTPHeaderField:"Content-Type");request.timeoutInterval=30
                    request.httpBody=try JSONSerialization.data(withJSONObject:["lat":point.latitude,"lng":point.longitude,"radius":1500,"category":kind,"query":text,"provider":current == .google ? "google":"osm","language":language])
                    let (data,response)=try await loader(request);try Task.checkCancellation()
                    guard (response as? HTTPURLResponse)?.statusCode==200 else {let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any];throw NSError(domain:"POI",code:1,userInfo:[NSLocalizedDescriptionKey:raw?["error"] as? String ?? "地點搜尋失敗，請重試。"])}
                    struct Response:Decodable{var places:[ScheduleCandidate];var attribution:String?}
                    let parsed=try JSONDecoder().decode(Response.self,from:data)
                    guard current != .google || parsed.places.allSatisfy({$0.source=="Google Places"}) else{throw NSError(domain:"POI",code:2,userInfo:[NSLocalizedDescriptionKey:"搜尋來源不一致，請更新後端後重試。"])}
                    result=parsed.places
                    guard token==generation else{return};if let credit=parsed.attribution{attribution=credit}
                }
                try Task.checkCancellation();guard token==generation else{return}
                places=result;busy=false
            }catch {if !Task.isCancelled && token==generation{self.error="地點資料暫時無法載入："+error.localizedDescription;busy=false}}
        }
    }
    func stop(){generation=UUID();job?.cancel();job=nil;busy=false}
}
@MainActor struct PoiDiscoveryView:View {
    var onAdd:(ScheduleCandidate)->Void = {PoiToolbox.shared.add($0)}
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) var dismiss
    @StateObject private var model=PoiSearchModel()
    @State private var detail:ScheduleCandidate?
    @State private var added=Set<String>()
    @State private var zoom:Double=14
    private var visiblePlaces:[ScheduleCandidate] {model.provider==store.mapProvider ? model.places:[]}
    var body:some View {
        NavigationStack {
            VStack(spacing:10) {
                HStack {TextField("搜尋附近地點名稱（可留空）",text:$model.query).textFieldStyle(.roundedBorder).onSubmit{model.search()};Button("搜尋"){model.search()}.disabled(model.busy)}.padding(.horizontal)
                Picker("地點分類",selection:$model.category){Text("景點").tag("sights");Text("餐飲").tag("food");Text("生活設施").tag("facilities")}.pickerStyle(.segmented).padding(.horizontal)
                HStack{Picker("底圖",selection:$store.mapProvider){ForEach(MapProvider.allCases){Text($0.label).tag($0)}}.pickerStyle(.menu);Spacer();Text(store.language.text("地點資料：{0}",store.language.text(model.source))).font(.caption2).foregroundStyle(Palette.muted)}.padding(.horizontal)
                Group {
                    switch store.mapProvider {
                    case .apple,.osm:
                        PoiNativeMap(places:visiblePlaces,osm:store.mapProvider == .osm,center:model.center,zoom:zoom,onCamera:{model.center=$0;zoom=$1},onSelect:{model.selected=$0})
                    case .google:
                        if SydneyBuddyApp.mapReady{PoiGoogleMap(places:visiblePlaces,center:model.center,zoom:zoom,onCamera:{model.center=$0;zoom=$1},onSelect:{model.selected=$0})}
                        else{Text("Google 地圖尚未設定金鑰，請選擇其他底圖。").font(.caption).padding()}
                    case .schematic:
                        Text("離線示意圖沒有街道底圖，請切換聯網底圖來移動搜尋範圍。").font(.caption).padding()
                    }
                }.frame(height:250).accessibilityIdentifier("poi-map-"+store.mapProvider.rawValue)
                Text(LocalizedStringKey(store.mapProvider == .google ? "Google Places · 按搜尋才查詢 · 附近 1.5 km":"切換分類自動搜尋 · 移動地圖後按搜尋 · 附近 1.5 km")).font(.caption2).foregroundStyle(Palette.muted)
                if model.busy {ProgressView("正在搜尋附近地點…")}
                if let error=model.error {
                    VStack(spacing:6){Text(error).font(.caption).foregroundStyle(.red);Text("地圖底圖仍可顯示；地點清單尚未取得，並非附近沒有地點。").font(.caption2).foregroundStyle(Palette.muted);Button("重新搜尋"){model.search()}}
                        .padding(.horizontal).accessibilityIdentifier("poi-search-error")
                }
                List {
                    if visiblePlaces.isEmpty && !model.busy && model.error==nil && model.hasSearched {Text("此範圍沒有符合的地點。可清空名稱、切換分類，或移動地圖後重新搜尋。").font(.caption)}
                    ForEach(visiblePlaces.sorted{a,b in a.id==model.selected && b.id != model.selected}) {p in
                        VStack(alignment:.leading,spacing:7) {
                            Label(p.name,systemImage:p.category=="美食" ? "fork.knife":p.category=="設施" ? "figure.stand":"mappin.and.ellipse").font(.subheadline.bold()).foregroundStyle(p.category=="美食" ? .orange:Palette.green)
                            Text(p.description ?? "介紹未提供").font(.caption).lineLimit(2)
                            HStack {Button("詳細介紹"){detail=p};Spacer();if p.source == "Google Places" {Text("Google 地點暫不匯入本機示例時間軸").font(.caption2)}
                                else {Button(added.contains(p.id)||PoiToolbox.shared.places.contains(where:{$0.id==p.id}) ? "已加入":"＋工具箱"){onAdd(p);added.insert(p.id)}.disabled(added.contains(p.id)||PoiToolbox.shared.places.contains(where:{$0.id==p.id}))}}.font(.caption)
                        }.listRowBackground(p.id==model.selected ? Palette.mint:Color.white)
                    }
                }.listStyle(.plain)
                if store.mapProvider == .osm {Link("地點資料 © OpenStreetMap contributors · ODbL",destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2)}
                else if !model.attribution.isEmpty {Text(model.attribution).font(.caption2).foregroundStyle(Palette.muted)}
            }.navigationTitle("搜尋地圖地點").navigationBarTitleDisplayMode(.inline).toolbar{Button("完成"){dismiss()}}
        }.sheet(item:$detail){SchedulePlaceDetail(candidate:$0)}
            .onAppear{model.language=store.language == .english ? "en":store.language == .simplified ? "zh-CN":"zh-TW";if let viewport=store.mapViewport{model.center=CLLocationCoordinate2D(latitude:viewport.lat,longitude:viewport.lng)};model.configure(store.mapProvider,googleMapConfigured:SydneyBuddyApp.mapConfigured)}
            .onChange(of:store.mapProvider){_,provider in detail=nil;model.configure(provider,googleMapConfigured:SydneyBuddyApp.mapConfigured)}
            .onChange(of:model.category){_,_ in if store.mapProvider == .google{model.configure(.google,googleMapConfigured:SydneyBuddyApp.mapConfigured)}else{model.search()}}
            .onDisappear{model.stop()}
    }
}
private final class PoiPin:NSObject,MKAnnotation {
    let point:ScheduleCandidate
    var coordinate:CLLocationCoordinate2D {CLLocationCoordinate2D(latitude:point.lat,longitude:point.lng)}
    var title:String? {point.name}
    init(_ p:ScheduleCandidate){point=p}
}
struct PoiNativeMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var osm:Bool
    var center:CLLocationCoordinate2D;var zoom:Double
    var onCamera:(CLLocationCoordinate2D,Double)->Void;var onSelect:(String)->Void
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:PoiNativeMap;var key=""
        init(_ p:PoiNativeMap){parent=p}
        func mapView(_ map:MKMapView,rendererFor overlay:MKOverlay)->MKOverlayRenderer{if let tiles=overlay as? MKTileOverlay{return MKTileOverlayRenderer(tileOverlay:tiles)};return MKOverlayRenderer(overlay:overlay)}
        func mapView(_ map:MKMapView,regionDidChangeAnimated animated:Bool){parent.onCamera(map.centerCoordinate,log2(360/max(0.00001,map.region.span.longitudeDelta)))}
        func mapView(_ map:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            guard let pin=annotation as? PoiPin else{return nil}
            let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);view.markerTintColor=pin.point.category=="美食" ? .systemOrange:.systemTeal;view.glyphImage=UIImage(systemName:pin.point.category=="美食" ? "fork.knife":pin.point.category=="設施" ? "figure.stand":"mappin");view.canShowCallout=true;return view
        }
        func mapView(_ map:MKMapView,didSelect view:MKAnnotationView){if let pin=view.annotation as? PoiPin{parent.onSelect(pin.point.id)}}
    }
    func makeUIView(context:Context)->MKMapView{let map=MKMapView();map.delegate=context.coordinator;if osm{map.addOverlay(OsmTiles(),level:.aboveLabels)};let delta=360/pow(2,zoom);map.setRegion(MKCoordinateRegion(center:center,span:MKCoordinateSpan(latitudeDelta:delta,longitudeDelta:delta)),animated:false);return map}
    func updateUIView(_ map:MKMapView,context:Context){context.coordinator.parent=self
        let tiles=map.overlays.filter{$0 is OsmTiles}
        if osm && tiles.isEmpty{map.addOverlay(OsmTiles(),level:.aboveLabels)}
        if !osm{map.removeOverlays(tiles)}
        let key=places.map(\.id).joined(separator:",");guard context.coordinator.key != key else{return};context.coordinator.key=key;map.removeAnnotations(map.annotations);map.addAnnotations(places.map(PoiPin.init))}
}

private struct PoiGoogleMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var center:CLLocationCoordinate2D;var zoom:Double
    var onCamera:(CLLocationCoordinate2D,Double)->Void;var onSelect:(String)->Void
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,GMSMapViewDelegate {
        var parent:PoiGoogleMap;var key=""
        init(_ parent:PoiGoogleMap){self.parent=parent}
        func mapView(_ map:GMSMapView,idleAt camera:GMSCameraPosition){parent.onCamera(camera.target,Double(camera.zoom))}
        func mapView(_ map:GMSMapView,didTap marker:GMSMarker)->Bool{if let id=marker.userData as? String{parent.onSelect(id)};return false}
    }
    func makeUIView(context:Context)->GMSMapView {
        let options=GMSMapViewOptions();options.camera=GMSCameraPosition(target:center,zoom:Float(zoom))
        let map=GMSMapView(options:options);map.delegate=context.coordinator;return map
    }
    func updateUIView(_ map:GMSMapView,context:Context){
        context.coordinator.parent=self;let key=places.map(\.id).joined(separator:",")
        guard key != context.coordinator.key else{return};context.coordinator.key=key;map.clear()
        for point in places{let marker=GMSMarker(position:CLLocationCoordinate2D(latitude:point.lat,longitude:point.lng));marker.title=point.name;marker.userData=point.id;marker.icon=GMSMarker.markerImage(with:point.category=="美食" ? .systemOrange:.systemTeal);marker.map=map}
    }
}
