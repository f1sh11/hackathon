import SwiftUI
import MapKit
import GoogleMaps
import CryptoKit

@MainActor final class PoiToolbox:ObservableObject {
    static let shared=PoiToolbox()
    @Published var places:[ScheduleCandidate]
    private init(){places=(UserDefaults.standard.data(forKey:"poi-toolbox").flatMap{try? JSONDecoder().decode([ScheduleCandidate].self,from:$0)}) ?? []}
    func remove(_ id:String){places.removeAll{$0.id==id};if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
    func add(_ p:ScheduleCandidate){guard !places.contains(where:{$0.id==p.id}) else{return};places.append(p);if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
}
@MainActor enum ApplePoiProvider {
    static func search(_ center:CLLocationCoordinate2D,_ category:String,_ query:String) async throws -> [ScheduleCandidate] {
        var categories:[MKPointOfInterestCategory]
        switch category {
        case "lodging":categories=[.hotel]
        case "shops":categories=[.store,.foodMarket]
        case "all":categories=[.hotel,.store,.restaurant,.cafe,.museum,.park,.library,.bakery,.foodMarket]
        case "food":categories=[.restaurant,.cafe,.bakery,.foodMarket]
        case "facilities":categories=[] // MapKit has no public-toilet/drinking-water POI category on iOS 17.
        default:categories=[.museum,.park,.nationalPark,.amusementPark,.aquarium,.zoo,.theater,.library]
        }
        if #available(iOS 18.0,*),category=="sights" {categories += [.landmark,.nationalMonument,.musicVenue]}
        let radius:Double=category == "all" ? 5000:1500
        let search:MKLocalSearch
        if query.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty && category != "facilities" {
            let request=MKLocalPointsOfInterestRequest(center:center,radius:radius)
            request.pointOfInterestFilter=MKPointOfInterestFilter(including:categories)
            search=MKLocalSearch(request:request)
        }else {
            let request=MKLocalSearch.Request()
            request.naturalLanguageQuery=query.isEmpty ? "public toilets drinking water" : query
            request.resultTypes = .pointOfInterest
            request.region=MKCoordinateRegion(center:center,latitudinalMeters:radius*2,longitudinalMeters:radius*2)
            if !categories.isEmpty{request.pointOfInterestFilter=MKPointOfInterestFilter(including:categories)}
            search=MKLocalSearch(request:request)
        }
        let response=try await withTaskCancellationHandler {try await search.start()} onCancel:{Task{@MainActor in search.cancel()}}
        try Task.checkCancellation()
        let origin=CLLocation(latitude:center.latitude,longitude:center.longitude)
        var seen=Set<String>()
        return response.mapItems.compactMap{item in
            let point=item.placemark.coordinate
            guard CLLocationCoordinate2DIsValid(point),origin.distance(from:CLLocation(latitude:point.latitude,longitude:point.longitude))<=radius,let name=item.name,!name.isEmpty else{return nil}
            var identity=name+String(format:"|%.6f|%.6f",point.latitude,point.longitude)
            if #available(iOS 18.0,*),let identifier=item.identifier{identity=identifier.rawValue}
            let id="apple:"+SHA256.hash(data:Data(identity.utf8)).map{String(format:"%02x",$0)}.joined()
            guard seen.insert(id).inserted else{return nil}
            let address=[item.placemark.thoroughfare,item.placemark.locality,item.placemark.postalCode].compactMap{$0}.joined(separator:", ")
            // A facility keyword match is not proof of a verified public restroom.
            let kind=category == "all" ? (item.pointOfInterestCategory == .hotel ? "lodging":item.pointOfInterestCategory == .store ? "shops":[MKPointOfInterestCategory.restaurant,.cafe,.bakery,.foodMarket].contains(where:{$0==item.pointOfInterestCategory}) ? "food":"sights"):category
            return ScheduleCandidate(id:id,name:name,category:kind=="food" ? "美食":kind=="facilities" ? "設施":kind=="lodging" ? "住宿":kind=="shops" ? "店家":"風景",indoor:false,visitMinutes:category=="food" ? 45:category=="facilities" ? 10:60,cost:0,lat:point.latitude,lng:point.longitude,reason:"Apple Maps 地點搜尋",description:address.isEmpty ? "Apple Maps 未提供詳細介紹。":address,note:"Apple Maps 地點；費用、排隊與營業時間未知。設施用途及開放情況請另行確認。",source:"Apple Maps",poiKind:category=="food" ? "restaurant":category=="facilities" ? "facility":"attraction",openingHoursText:"",website:item.url?.absoluteString,phone:item.phoneNumber,address:address)
        }
    }
}
@MainActor final class PoiSearchModel:ObservableObject {
    @Published var query=""
    @Published var category="all"
    @Published var places:[ScheduleCandidate]=[]
    @Published var selected:String?
    @Published var busy=false
    @Published var error:String?
    @Published var hasSearched=false
    @Published var searchSummary:String?
    @Published private(set) var provider:MapProvider = .osm
    @Published private(set) var attribution="Overture Maps Foundation"
    @Published private(set) var openSource="Overture Maps / local snapshot"
    @Published private(set) var dataVersion:String?
    var dataSource:TravelDataSource = .direct
    var language="zh-TW"
    var googleMapConfigured=true
    var center=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
    var appleSearch:(CLLocationCoordinate2D,String,String) async throws -> [ScheduleCandidate] = ApplePoiProvider.search
    private var job:Task<Void,Never>?
    private var generation=UUID()
    var source:String {if dataSource == .zetaris{return "Open travel data / Zetaris"};if category == "gems" {return "Open travel data / local"};switch provider{case .apple:return "Apple Maps / MapKit";case .google:return "Google Places";case .osm:return openSource;case .schematic:return "離線示意 · 不搜尋地點"}}
    func configure(_ value:MapProvider,googleMapConfigured:Bool=true,dataSource:TravelDataSource = .direct) {
        stop();provider=value;self.dataSource=dataSource;self.googleMapConfigured=googleMapConfigured;places=[];selected=nil;error=nil;hasSearched=false
        attribution=dataSource == .zetaris ? "Overture Maps Foundation":value == .apple ? "Apple Maps":value == .google ? "Google Maps":value == .osm ? "Overture Maps Foundation":""
        dataVersion=nil
        if category == "gems" || value == .osm || value == .apple || dataSource == .zetaris{search()}
    }
    func search(){
        stop();error=nil;searchSummary=nil;hasSearched=true;places=[];selected=nil
        guard provider != .schematic else{error="離線示意圖不提供地點搜尋，請切換其他底圖。";return}
        guard category == "gems" || dataSource == .zetaris || provider != .google || googleMapConfigured else{error="Google 地圖尚未設定金鑰，請選擇其他底圖。";return}
        busy=true
        let point=center,text=query.trimmingCharacters(in:.whitespacesAndNewlines),kind=category,current=provider,route=dataSource,token=generation
        job=Task {@MainActor in
            do {
                let result:[ScheduleCandidate]
                if current == .apple && route == .direct && kind != "gems" {result=try await appleSearch(point,kind,text)}
                else {
                    var request=URLRequest(url:URL(string:"http://127.0.0.1:8787/poi/search")!);request.httpMethod="POST";route.apply(to:&request);request.setValue("application/json",forHTTPHeaderField:"Content-Type");request.timeoutInterval=30
                    request.httpBody=try JSONSerialization.data(withJSONObject:["lat":point.latitude,"lng":point.longitude,"radius":["gems","all"].contains(kind) ? 5000:1500,"category":kind,"query":text,"provider":current == .google && route == .direct && kind != "gems" ? "google":"osm","language":language])
                    let (data,response)=try await loader(request);try Task.checkCancellation()
                    guard (response as? HTTPURLResponse)?.statusCode==200 else {let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any];throw NSError(domain:"POI",code:1,userInfo:[NSLocalizedDescriptionKey:raw?["error"] as? String ?? "地點搜尋失敗，請重試。"])}
                    struct Response:Decodable{struct Metadata:Decodable{var release:String?};var places:[ScheduleCandidate];var attribution:String?;var source:String?;var metadata:Metadata?;var message:String?;var truncated:Bool?}
                    let parsed=try JSONDecoder().decode(Response.self,from:data)
                    guard kind == "gems" || route == .zetaris || current != .google || parsed.places.allSatisfy({$0.source=="Google Places"}) else{throw NSError(domain:"POI",code:2,userInfo:[NSLocalizedDescriptionKey:"搜尋來源不一致，請更新後端後重試。"])}
                    result=parsed.places
                    guard token==generation else{return};if let credit=parsed.attribution{attribution=credit};searchSummary=parsed.truncated == true ? "已顯示部分結果，請輸入名稱或縮小分類。":parsed.message
                    if kind == "gems" || current == .osm || route == .zetaris{openSource=parsed.source ?? "開放地點資料";dataVersion=parsed.metadata?.release}
                }
                try Task.checkCancellation();guard token==generation else{return}
                places=result;busy=false
            }catch {if !Task.isCancelled && token==generation{self.error="地點資料暫時無法載入："+error.localizedDescription;busy=false}}
        }
    }
    func stop(){generation=UUID();job?.cancel();job=nil;busy=false}
}
@MainActor struct PoiDiscoveryView:View {
    var embedded=false
    var onAdd:(ScheduleCandidate)->Void = {PoiToolbox.shared.add($0)}
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) var dismiss
    @StateObject private var model=PoiSearchModel()
    @State private var detail:ScheduleCandidate?
    @ObservedObject private var toolbox=PoiToolbox.shared
    @State private var showInsights=false
    @State private var zoom:Double=14
    private var visiblePlaces:[ScheduleCandidate] {model.provider==store.mapProvider && model.dataSource==store.travelDataSource ? model.places:[]}
    private var insightPoints:[[String:Any]] {[["id":"center","name":"搜尋中心","lat":model.center.latitude,"lng":model.center.longitude]]+visiblePlaces.prefix(20).map{["id":$0.id,"name":$0.name,"lat":$0.lat,"lng":$0.lng]}}
    private var insightMarkers:[InsightMarker] {(store.insights?.traffic.items ?? [])+(store.insights?.pricePlaces ?? [])}
    var body:some View {
        NavigationStack {
            VStack(spacing:10) {
                HStack {TextField("搜尋附近地點名稱（可留空）",text:$model.query).textFieldStyle(.roundedBorder).onSubmit{model.search()};Button("搜尋"){model.search()}.disabled(model.busy)}.padding(.horizontal)
                ScrollView(.horizontal,showsIndicators:false){HStack(spacing:8){ForEach([("all","全部"),("sights","景點"),("gems","私房景點"),("food","餐飲"),("lodging","住宿"),("shops","店家"),("facilities","生活設施")],id:\.0){id,label in Button{model.category=id}label:{Text(LocalizedStringKey(label)).font(.subheadline).padding(.horizontal,14).padding(.vertical,9).background(model.category==id ? Palette.green:Palette.mint,in:Capsule()).foregroundStyle(model.category==id ? .white:Palette.ink)}.accessibilityIdentifier("poi-category-"+id)}}}.padding(.horizontal)
                HStack{Picker("底圖",selection:$store.mapProvider){ForEach(store.availableMapProviders){Text($0.label).tag($0)}}.pickerStyle(.menu);Spacer();Text(store.language.text("地點資料：{0}",store.language.text(model.source))).font(.caption2).foregroundStyle(Palette.muted)}.padding(.horizontal)
                Button{showInsights=true;Task{await store.refreshInsights(places:insightPoints)}}label:{Label("交通・人潮・費用",systemImage:"info.circle")}.font(.caption).accessibilityIdentifier("travel-insights-open")
                Group {
                    switch store.mapProvider {
                    case .apple,.osm:
                        PoiNativeMap(places:visiblePlaces,osm:store.mapProvider == .osm,center:model.center,zoom:zoom,onCamera:{model.center=$0;zoom=$1},onSelect:{model.selected=$0},insightMarkers:insightMarkers)
                    case .google:
                        if SydneyBuddyApp.mapReady{PoiGoogleMap(places:visiblePlaces,center:model.center,zoom:zoom,onCamera:{model.center=$0;zoom=$1},onSelect:{model.selected=$0},insightMarkers:insightMarkers)}
                        else{Text("Google 地圖尚未設定金鑰，請選擇其他底圖。").font(.caption).padding()}
                    case .schematic:
                        Text("離線示意圖沒有街道底圖，請切換聯網底圖來移動搜尋範圍。").font(.caption).padding()
                    }
                }.frame(height:embedded ? 350 : 250).accessibilityIdentifier("poi-map-"+store.mapProvider.rawValue)
                Text(LocalizedStringKey(model.category == "all" ? "全部分類 · 附近 5 km · 資料限已匯入區域":model.category == "gems" ? "社群探索候選 · 附近 5 km · 非熱門度評分":store.mapProvider == .google && store.travelDataSource == .direct ? "Google Places · 按搜尋才查詢 · 附近 1.5 km":"切換分類自動搜尋 · 移動地圖後按搜尋 · 附近 1.5 km")).font(.caption2).foregroundStyle(Palette.muted)
                if let summary=model.searchSummary{Text(LocalizedStringKey(summary)).font(.caption2).foregroundStyle(Palette.muted).padding(.horizontal)}
                if model.busy {ProgressView("正在搜尋附近地點…")}
                if let error=model.error {
                    VStack(spacing:6){Text(error).font(.caption).foregroundStyle(.red);Text("地圖底圖仍可顯示；地點清單尚未取得，並非附近沒有地點。").font(.caption2).foregroundStyle(Palette.muted);Button("重新搜尋"){model.search()}}
                        .padding(.horizontal).accessibilityIdentifier("poi-search-error")
                }
                List {
                    if visiblePlaces.isEmpty && !model.busy && model.error==nil && model.hasSearched {Text("此範圍沒有符合的地點。可清空名稱、切換分類，或移動地圖後重新搜尋。").font(.caption)}
                    ForEach(visiblePlaces.sorted{a,b in a.id==model.selected && b.id != model.selected}) {p in
                        VStack(alignment:.leading,spacing:7) {
                            Button{detail=p}label:{VStack(alignment:.leading,spacing:7){
                                Label(p.name,systemImage:p.discoverySymbol).font(.subheadline.bold()).foregroundStyle(p.category=="美食" ? .orange:Palette.green)
                                Text(store.language.text(p.category)+" · "+(p.sources ?? [p.source ?? ""]).joined(separator:" / ")).font(.caption2).foregroundStyle(Palette.muted)
                                Text((store.insights?.places.first{$0.id==p.id}?.price.summary ?? "費用未知")+" · 人潮未知").font(.caption2).foregroundStyle(Palette.muted)
                                Text(p.description ?? "介紹未提供").font(.caption).lineLimit(2).foregroundStyle(.primary)
                            }.frame(maxWidth:.infinity,alignment:.leading).contentShape(Rectangle())}.buttonStyle(.borderless).accessibilityIdentifier("poi-detail-"+p.id)
                            HStack {Button("詳細介紹"){detail=p};Spacer();if p.source == "Google Places" {Text("Google 地點暫不匯入本機示例時間軸").font(.caption2)}
                                else {Button(toolbox.places.contains(where:{$0.id==p.id}) ? "移出工具箱":"＋工具箱"){
                                    if toolbox.places.contains(where:{$0.id==p.id}){toolbox.remove(p.id)}else{toolbox.add(p);onAdd(p)}
                                }.accessibilityIdentifier("poi-toggle-"+p.id)}}.font(.caption).buttonStyle(.borderless)
                        }.listRowBackground(p.id==model.selected ? Palette.mint:Color.white)
                    }
                }.listStyle(.plain)
                if store.mapProvider == .osm {
                    VStack(spacing:3){
                        Link("地圖 © OpenStreetMap contributors · ODbL",destination:URL(string:"https://www.openstreetmap.org/copyright")!)
                        Link(model.attribution,destination:URL(string:model.category == "gems" ? "https://en.wikivoyage.org/wiki/Wikivoyage:Copyleft":model.attribution.contains("OpenStreetMap") ? "https://www.openstreetmap.org/copyright":"https://docs.overturemaps.org/attribution/")!)
                        if let version=model.dataVersion{Text(store.language.text("本機地點資料快照：{0}",version))}
                    }.font(.caption2)
                }
                else if !model.attribution.isEmpty {Text(model.attribution).font(.caption2).foregroundStyle(Palette.muted)}
            }.navigationTitle(embedded ? "探索目的地" : "搜尋地圖地點").navigationBarTitleDisplayMode(.inline).toolbar{if !embedded{Button("完成"){dismiss()}}}
        }.sheet(item:$detail){SchedulePlaceDetail(candidate:$0)}
            .sheet(isPresented:$showInsights){TravelInsightsSheet(places:insightPoints)}
            .task(id:visiblePlaces.map(\.id).joined()+store.travelDataSource.rawValue+store.mapProvider.rawValue){if !visiblePlaces.isEmpty{await store.refreshInsights(places:insightPoints)}}
            .onAppear{model.language=store.language == .english ? "en":store.language == .simplified ? "zh-CN":"zh-TW";if let viewport=store.mapViewport{model.center=CLLocationCoordinate2D(latitude:viewport.lat,longitude:viewport.lng)};model.configure(store.mapProvider,googleMapConfigured:SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)}
            .onChange(of:store.mapProvider){_,provider in detail=nil;model.configure(provider,googleMapConfigured:SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)}
            .onChange(of:store.travelDataSource){_,_ in detail=nil;model.configure(store.mapProvider,googleMapConfigured:SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)}
            .onChange(of:model.category){_,_ in if model.category != "gems" && store.mapProvider == .google && store.travelDataSource == .direct{model.configure(.google,googleMapConfigured:SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)}else{model.search()}}
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
    var insightMarkers:[InsightMarker]=[]
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:PoiNativeMap;var key=""
        init(_ p:PoiNativeMap){parent=p}
        func mapView(_ map:MKMapView,rendererFor overlay:MKOverlay)->MKOverlayRenderer{if let tiles=overlay as? MKTileOverlay{return MKTileOverlayRenderer(tileOverlay:tiles)};return MKOverlayRenderer(overlay:overlay)}
        func mapView(_ map:MKMapView,regionDidChangeAnimated animated:Bool){parent.onCamera(map.centerCoordinate,log2(360/max(0.00001,map.region.span.longitudeDelta)))}
        func mapView(_ map:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            if let pin=annotation as? InsightMapPin {let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);view.markerTintColor=pin.kind == "price" ? .systemGreen:.systemOrange;view.glyphText=pin.kind == "price" ? "$":"!";view.canShowCallout=true;return view}
            guard let pin=annotation as? PoiPin else{return nil}
            let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);view.markerTintColor=pin.point.category=="美食" ? .systemOrange:.systemTeal;view.glyphImage=UIImage(systemName:pin.point.discoverySymbol);view.canShowCallout=true;return view
        }
        func mapView(_ map:MKMapView,didSelect view:MKAnnotationView){if let pin=view.annotation as? PoiPin{parent.onSelect(pin.point.id)}}
    }
    func makeUIView(context:Context)->MKMapView{let map=MKMapView();map.delegate=context.coordinator;if osm{map.addOverlay(OsmTiles(),level:.aboveLabels)};let delta=360/pow(2,zoom);map.setRegion(MKCoordinateRegion(center:center,span:MKCoordinateSpan(latitudeDelta:delta,longitudeDelta:delta)),animated:false);return map}
    func updateUIView(_ map:MKMapView,context:Context){context.coordinator.parent=self
        let tiles=map.overlays.filter{$0 is OsmTiles}
        if osm && tiles.isEmpty{map.addOverlay(OsmTiles(),level:.aboveLabels)}
        if !osm{map.removeOverlays(tiles)}
        let key=places.map(\.id).joined(separator:",")+insightMarkers.map{$0.id+$0.title}.joined();guard context.coordinator.key != key else{return};context.coordinator.key=key;map.removeAnnotations(map.annotations);map.addAnnotations(places.map(PoiPin.init));for item in insightMarkers{let pin=InsightMapPin();pin.kind=item.kind;pin.coordinate = .init(latitude:item.lat,longitude:item.lng);pin.title=item.title;pin.subtitle=item.summary;map.addAnnotation(pin)}}
}

private struct PoiGoogleMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var center:CLLocationCoordinate2D;var zoom:Double
    var onCamera:(CLLocationCoordinate2D,Double)->Void;var onSelect:(String)->Void
    var insightMarkers:[InsightMarker]=[]
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
        context.coordinator.parent=self;let key=places.map(\.id).joined(separator:",")+insightMarkers.map{$0.id+$0.title}.joined()
        guard key != context.coordinator.key else{return};context.coordinator.key=key;map.clear()
        for item in insightMarkers{let pin=GMSMarker(position:.init(latitude:item.lat,longitude:item.lng));pin.title=item.title;pin.snippet=item.summary;pin.icon=GMSMarker.markerImage(with:item.kind == "price" ? .systemGreen:.systemOrange);pin.map=map}
        for point in places{let marker=GMSMarker(position:CLLocationCoordinate2D(latitude:point.lat,longitude:point.lng));marker.title=point.name;marker.userData=point.id;marker.icon=GMSMarker.markerImage(with:point.category=="美食" ? .systemOrange:.systemTeal);marker.map=map}
    }
}

struct PlacePhoto:Codable,Identifiable {
    var url:String;var pageUrl:String;var title:String;var artist:String;var license:String;var licenseUrl:String?;var matchKind:String?;var source:String?
    var id:String {pageUrl}
}
struct PlaceMediaView:View {
    var candidate:ScheduleCandidate
    @State private var photos:[PlacePhoto]=[]
    @State private var loading=false
    @State private var message:String?
    @State private var scene:MKLookAroundScene?
    @State private var panoramaLoading=false
    @State private var panoramaMessage:String?
    @EnvironmentObject var store:AppStore
    var body:some View {
        VStack(alignment:.leading,spacing:12){
            Text("照片與街景").font(.headline)
            Text(LocalizedStringKey(candidate.hiddenGem == true ? "社群照片保留作者與授權；附近照片不代表已確認拍攝此景點。":"附近 100 公尺的實景照片，並非已核實的商家或店內照片。")).font(.caption).foregroundStyle(Palette.muted)
            Button(photos.isEmpty ? "載入附近照片":"重新載入照片"){Task{await loadPhotos()}}.disabled(loading)
            if loading{ProgressView()}
            if let message{Text(LocalizedStringKey(message)).font(.caption)}
            ScrollView(.horizontal){HStack(alignment:.top,spacing:12){ForEach(photos){photo in
                VStack(alignment:.leading,spacing:5){
                    AsyncImage(url:URL(string:photo.url)){phase in
                        switch phase{case .success(let image):image.resizable().scaledToFill();case .failure:Text("照片無法載入");default:ProgressView()}
                    }.frame(width:260,height:170).clipped().clipShape(RoundedRectangle(cornerRadius:12))
                    Text(photo.title).font(.caption).lineLimit(2)
                    Text(photo.artist+" · "+photo.license).font(.caption2).lineLimit(3)
                    if photo.matchKind == "nearby-unverified" {Text("附近照片，景點配對未確認").font(.caption2)}
                    if let url=URL(string:photo.pageUrl){Link("照片來源與授權",destination:url).font(.caption)}
                }.frame(width:260,alignment:.leading)
            }}}
            if store.mapProvider != .google {
                Button("預覽街景（Apple Look Around）"){Task{
                    panoramaLoading=true;panoramaMessage=nil
                    do{scene=try await MKLookAroundSceneRequest(coordinate:CLLocationCoordinate2D(latitude:candidate.lat,longitude:candidate.lng)).scene
                        if scene==nil{panoramaMessage="此位置尚無可用街景。"}
                    }catch{panoramaMessage="街景暫時無法載入，請稍後重試。"}
                    panoramaLoading=false
                }}.disabled(panoramaLoading)
                if panoramaLoading{ProgressView()}
                if let scene{LookAroundPreview(initialScene:scene).frame(height:210).clipShape(RoundedRectangle(cornerRadius:12))}
                if let panoramaMessage{Text(LocalizedStringKey(panoramaMessage)).font(.caption)}
            }
            Link("開啟附近街景（外部地圖）",destination:URL(string:"https://www.mapillary.com/app/?lat=\(candidate.lat)&lng=\(candidate.lng)&z=17")!).font(.caption)
            Text("街景覆蓋由服務提供者決定；未必有此商家的全景。").font(.caption).foregroundStyle(Palette.muted)
        }.task(id:candidate.id){photos=candidate.photos ?? [];message=nil;scene=nil;panoramaMessage=nil}
    }
    @MainActor private func loadPhotos() async {
        loading=true;message=nil
        defer{loading=false}
        do{
            var request=URLRequest(url:URL(string:"http://127.0.0.1:8787/poi/media")!)
            request.httpMethod="POST";store.travelDataSource.apply(to:&request);request.timeoutInterval=22;request.setValue("application/json",forHTTPHeaderField:"Content-Type")
            request.httpBody=try JSONSerialization.data(withJSONObject:["lat":candidate.lat,"lng":candidate.lng,"id":candidate.id])
            let (data,response)=try await URLSession.shared.data(for:request)
            guard (response as? HTTPURLResponse)?.statusCode==200 else{throw URLError(.badServerResponse)}
            struct Media:Decodable{var photos:[PlacePhoto]}
            photos=try JSONDecoder().decode(Media.self,from:data).photos
            if photos.isEmpty{message="附近尚無可用的開放照片。"}
        }catch{message="照片暫時無法載入，請稍後重試。"}
    }
}
