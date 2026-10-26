import SwiftUI
import MapKit
import GoogleMaps
import CryptoKit

@MainActor final class PoiToolbox:ObservableObject {
    static let shared=PoiToolbox()
    @Published var places:[ScheduleCandidate]
    private init(){places=(UserDefaults.standard.data(forKey:"poi-toolbox").flatMap{try? JSONDecoder().decode([ScheduleCandidate].self,from:$0)}) ?? []}
    func replace(_ point:ScheduleCandidate){guard let index=places.firstIndex(where:{$0.id==point.id}) else{return};places[index]=point;if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
    func remove(_ id:String){places.removeAll{$0.id==id};if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
    func add(_ p:ScheduleCandidate){guard !places.contains(where:{$0.id==p.id}) else{return};places.append(p);if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
}
enum PoiDiscoveryCategory {
    static let groups=[("all","全部"),("sights","景點"),("gems","私房景點"),("food","餐飲"),("lodging","住宿"),("shops","店家"),("facilities","生活設施")]
    static let details=[("restaurants","餐廳","food"),("cafes","咖啡廳","food"),("bakeries","甜點烘焙","food"),("bars","酒吧夜生活","food"),("museums","博物館","sights"),("parks","公園綠地","sights"),("beaches","海灘","sights"),("hotels","飯店旅館","lodging"),("hostels","青年旅館","lodging"),("supermarkets","超市雜貨","shops"),("toilets","廁所","facilities"),("drinking_water","飲水點","facilities")]
    static func parent(_ id:String)->String{details.first{$0.0==id}?.2 ?? id}
    static func filters(_ id:String)->[(String,String,String)]{let group=parent(id);return details.filter{group=="all" || $0.2==group}}
}
@MainActor enum ApplePoiProvider {
    static func search(_ center:CLLocationCoordinate2D,_ category:String,_ query:String,_ radiusOverride:Double?=nil) async throws -> [ScheduleCandidate] {
        if category=="drinking_water"{return []} // MapKit has no dedicated drinking-water category.
        var categories:[MKPointOfInterestCategory]
        switch category {
        case "restaurants":categories=[.restaurant]
        case "cafes":categories=[.cafe]
        case "bakeries":categories=[.bakery]
        case "bars":categories=[.nightlife]
        case "museums":categories=[.museum]
        case "parks":categories=[.park,.nationalPark]
        case "beaches":categories=[.beach]
        case "supermarkets":categories=[.foodMarket]
        case "toilets":categories=[.restroom]
        case "hotels","hostels","lodging":categories=[.hotel]
        case "shops":categories=[.store,.foodMarket]
        case "all":categories=[.hotel,.store,.restaurant,.cafe,.museum,.park,.nationalPark,.library,.bakery,.foodMarket,.beach,.nightlife,.restroom]
        case "food":categories=[.restaurant,.cafe,.bakery,.foodMarket,.nightlife]
        case "facilities":categories=[.restroom] // Access and opening hours still need verification.
        default:categories=[.museum,.park,.nationalPark,.amusementPark,.aquarium,.zoo,.theater,.library]
        }
        if #available(iOS 18.0,*),(category=="sights" || category=="all") {categories += [.landmark,.nationalMonument,.musicVenue]}
        let radius:Double=radiusOverride ?? (category == "all" ? 5000:1500)
        let search:MKLocalSearch
        if query.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty && category != "hotels" && category != "hostels" {
            let request=MKLocalPointsOfInterestRequest(center:center,radius:radius)
            request.pointOfInterestFilter=MKPointOfInterestFilter(including:categories)
            search=MKLocalSearch(request:request)
        }else {
            let request=MKLocalSearch.Request()
            request.naturalLanguageQuery=category=="hostels" ? "hostel "+query:category=="hotels" ? "hotel "+query:query
            request.resultTypes = .pointOfInterest
            request.region=MKCoordinateRegion(center:center,latitudinalMeters:radius*2,longitudinalMeters:radius*2)
            if !categories.isEmpty && category != "all" {request.pointOfInterestFilter=MKPointOfInterestFilter(including:categories)}
            search=MKLocalSearch(request:request)
        }
        let response=try await withTaskCancellationHandler {try await search.start()} onCancel:{Task{@MainActor in search.cancel()}}
        try Task.checkCancellation()
        let origin=CLLocation(latitude:center.latitude,longitude:center.longitude)
        var seen=Set<String>()
        return response.mapItems.compactMap{item in
            let point=item.placemark.coordinate
            guard CLLocationCoordinate2DIsValid(point),origin.distance(from:CLLocation(latitude:point.latitude,longitude:point.longitude))<=radius,let name=item.name,!name.isEmpty else{return nil}
            guard let candidate=candidate(item,category:category),seen.insert(candidate.id).inserted else{return nil}
            return candidate
        }
    }
    static func candidate(_ item:MKMapItem,category:String="all")->ScheduleCandidate? {
        let point=item.placemark.coordinate
        guard CLLocationCoordinate2DIsValid(point),let name=item.name,!name.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty else{return nil}
            var identity=name+String(format:"|%.6f|%.6f",point.latitude,point.longitude)
            if #available(iOS 18.0,*),let identifier=item.identifier{identity=identifier.rawValue}
            let id="apple:"+SHA256.hash(data:Data(identity.utf8)).map{String(format:"%02x",$0)}.joined()
            let address=[item.placemark.thoroughfare,item.placemark.locality,item.placemark.postalCode].compactMap{$0}.joined(separator:", ")
            // A facility keyword match is not proof of a verified public restroom.
            let kind=category == "all" ? (item.pointOfInterestCategory == .hotel ? "lodging":item.pointOfInterestCategory == .store ? "shops":[MKPointOfInterestCategory.restaurant,.cafe,.bakery,.foodMarket].contains(where:{$0==item.pointOfInterestCategory}) ? "food":item.pointOfInterestCategory == .restroom ? "facilities":"sights"):PoiDiscoveryCategory.parent(category)
            let actual=item.pointOfInterestCategory
            let interest=kind=="food" ? "美食":kind=="facilities" ? "設施":kind=="lodging" ? "住宿":kind=="shops" ? "购物":actual == .park || actual == .nationalPark || actual == .beach ? "自然":actual == .museum || actual == .library ? "文化":actual == .theater ? "艺术":"风景"
            return ScheduleCandidate(id:id,name:name,category:interest,indoor:false,visitMinutes:kind=="food" ? 45:kind=="facilities" ? 10:kind=="lodging" ? 30:60,cost:0,lat:point.latitude,lng:point.longitude,reason:"Apple Maps 地點搜尋",description:address.isEmpty ? "Apple Maps 未提供詳細介紹。":address,note:"Apple Maps 地點；費用、排隊與營業時間未知。設施用途及開放情況請另行確認。",source:"Apple Maps",poiKind:actual == .publicTransport ? "transport":category=="toilets" || item.pointOfInterestCategory == .restroom ? "toilets":category=="cafes" || actual == .cafe ? "cafe":category=="bakeries" || actual == .bakery ? "bakery":actual == .foodMarket ? "foodMarket":category=="hostels" ? "hostel":kind=="lodging" ? "hotel":kind=="food" ? "restaurant":kind=="facilities" ? "facility":"attraction",openingHoursText:"",website:item.url?.absoluteString,phone:item.phoneNumber,address:address)
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
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await BackendConnection.data(for:$0)}
    var appleSearch:(CLLocationCoordinate2D,String,String,Double?) async throws -> [ScheduleCandidate] = ApplePoiProvider.search
    private var cameraJob:Task<Void,Never>?
    private var lastCamera:(CLLocationCoordinate2D,Double)?
    var cameraDelay:UInt64=800_000_000
    private(set) var viewportRadius:Double?
    func cameraChanged(_ point:CLLocationCoordinate2D,zoom:Double) {
        guard CLLocationCoordinate2DIsValid(point),zoom.isFinite else{return}
        if let last=lastCamera {
            let distance=CLLocation(latitude:point.latitude,longitude:point.longitude).distance(from:CLLocation(latitude:last.0.latitude,longitude:last.0.longitude))
            guard distance>60 || abs(zoom-last.1)>0.35 else{return}
        }
        lastCamera=(point,zoom);center=point
        viewportRadius=min(5000,max(500,156543.03392*cos(point.latitude * .pi/180)/pow(2,zoom)*420))
        stop();places=[];selected=nil;error=nil
        guard provider != .schematic else{return}
        cameraJob=Task{@MainActor in
            do{try await Task.sleep(nanoseconds:cameraDelay);try Task.checkCancellation()}catch{return}
            cameraJob=nil;search()
        }
    }
    private var job:Task<Void,Never>?
    private var generation=UUID()
    var source:String {if dataSource.warehouse{return "Open travel data / Zetaris"};if category == "gems" {return "Open travel data / local"};switch provider{case .apple:return "Apple Maps / MapKit";case .google:return "Google Places";case .osm:return openSource;case .schematic:return "離線示意 · 不搜尋地點"}}
    func configure(_ value:MapProvider,googleMapConfigured:Bool=true,dataSource:TravelDataSource = .direct) {
        stop();lastCamera=nil;viewportRadius=nil;provider=value;self.dataSource=dataSource;self.googleMapConfigured=googleMapConfigured;places=[];selected=nil;error=nil;hasSearched=false
        attribution=dataSource.warehouse ? "Overture Maps Foundation":value == .apple ? "Apple Maps":value == .google ? "Google Maps":value == .osm ? "Overture Maps Foundation":""
        dataVersion=nil
        if category == "gems" || value == .osm || value == .apple || dataSource.warehouse{search()}
    }
    func search(){
        stop();error=nil;searchSummary=nil;hasSearched=true;places=[];selected=nil
        guard provider != .schematic else{error="離線示意圖不提供地點搜尋，請切換其他底圖。";return}
        guard category == "gems" || dataSource.warehouse || provider != .google || googleMapConfigured else{error="Google 地圖尚未設定金鑰，請選擇其他底圖。";return}
        busy=true
        let point=center,text=query.trimmingCharacters(in:.whitespacesAndNewlines),kind=category,current=provider,route=dataSource,token=generation,radius=viewportRadius
        job=Task {@MainActor in
            do {
                let result:[ScheduleCandidate]
                if current == .apple && route == .direct && kind != "gems" {
                    result=try await appleSearch(point,kind,text,radius)
                    if kind=="drinking_water",token==generation{searchSummary="Apple Maps 未提供獨立飲水點分類；請切換開源資料搜尋。"}
                }
                else {
                    var request=URLRequest(url:BackendConnection.url("poi/search"));request.httpMethod="POST";route.apply(to:&request);request.setValue("application/json",forHTTPHeaderField:"Content-Type");request.timeoutInterval=30
                    request.httpBody=try JSONSerialization.data(withJSONObject:["lat":point.latitude,"lng":point.longitude,"radius":Int(radius ?? (["gems","all"].contains(kind) ? 5000:1500)),"category":kind,"query":text,"provider":current == .google && route == .direct && kind != "gems" ? "google":"osm","language":language])
                    let (data,response)=try await loader(request);try Task.checkCancellation()
                    guard (response as? HTTPURLResponse)?.statusCode==200 else {let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any];throw NSError(domain:"POI",code:1,userInfo:[NSLocalizedDescriptionKey:raw?["error"] as? String ?? "地點搜尋失敗，請重試。"])}
                    struct Response:Decodable{struct Metadata:Decodable{var release:String?};var places:[ScheduleCandidate];var attribution:String?;var source:String?;var metadata:Metadata?;var message:String?;var truncated:Bool?}
                    let parsed=try JSONDecoder().decode(Response.self,from:data)
                    guard kind == "gems" || route.warehouse || current != .google || parsed.places.allSatisfy({$0.source=="Google Places"}) else{throw NSError(domain:"POI",code:2,userInfo:[NSLocalizedDescriptionKey:"搜尋來源不一致，請更新後端後重試。"])}
                    result=parsed.places
                    guard token==generation else{return};if let credit=parsed.attribution{attribution=credit};searchSummary=parsed.truncated == true ? "已顯示部分結果，請輸入名稱或縮小分類。":parsed.message
                    if kind == "gems" || current == .osm || route.warehouse{openSource=parsed.source ?? "開放地點資料";dataVersion=parsed.metadata?.release}
                }
                try Task.checkCancellation();guard token==generation else{return}
                places=result;busy=false
            }catch {if !Task.isCancelled && token==generation{self.error="地點資料暫時無法載入："+error.localizedDescription;busy=false}}
        }
    }
    func stop(){cameraJob?.cancel();cameraJob=nil;generation=UUID();job?.cancel();job=nil;busy=false}
}
@MainActor struct PoiDiscoveryView:View {
    var embedded=false
    var initialQuery=""
    var addingToDay=false
    var replacingPlace=false
    var onAdd:(ScheduleCandidate)->Void = {PoiToolbox.shared.add($0)}
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var model=PoiSearchModel()
    @State private var detail:ScheduleCandidate?
    @State private var focus:PoiMapFocus?
    @State private var ticketSummaries:[String:TicketPriceSummary]=[:]
    @State private var featureLoading=false
    @State private var featureError:String?
    @State private var localProvider:MapProvider = .apple
    private var searchProvider:MapProvider {addingToDay ? localProvider:store.mapProvider}
    @ObservedObject private var toolbox=PoiToolbox.shared
    @State private var showInsights=false
    @State private var zoom:Double=14
    @State private var expanded=false
    @State private var areaChanged=false
    @FocusState private var searching:Bool
    private var visiblePlaces:[ScheduleCandidate] {model.provider==searchProvider && model.dataSource==store.travelDataSource ? model.places:[]}
    private var orderedPlaces:[ScheduleCandidate] {visiblePlaces.sorted{a,b in a.id==model.selected && b.id != model.selected}}
    private var insightPoints:[[String:Any]] {[["id":"center","name":"搜尋中心","lat":model.center.latitude,"lng":model.center.longitude]]+visiblePlaces.prefix(20).map{["id":$0.id,"name":$0.name,"lat":$0.lat,"lng":$0.lng]}}
    private var insightMarkers:[InsightMarker] {(store.insights?.traffic.items ?? [])+(store.insights?.pricePlaces ?? [])}
    var body: some View {
    NavigationStack {
        GeometryReader { geo in
            ZStack(alignment: .top) {
                map.ignoresSafeArea(edges: .bottom)
                VStack(spacing: 10) {
                    HStack(spacing: 10) {
                        HStack {
                            Image(systemName: "magnifyingglass").foregroundStyle(Palette.muted)
                            TextField("想去哪里？", text: $model.query)
                                .focused($searching)
                                .submitLabel(.search)
                                .onSubmit { search() }
                            if !model.query.isEmpty {
                                Button {
                                    model.query = ""
                                    search()
                                } label: {
                                    Image(systemName: "xmark.circle.fill")
                                }
                                .accessibilityLabel("清空搜索")
                            }
                            Button("搜索") { search() }
                                .font(.subheadline.bold())
                                .disabled(model.busy)
                        }
                        .padding(13)
                        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18))

                        Menu {
                            Picker("地图来源", selection: Binding(get:{searchProvider},set:{if addingToDay{localProvider=$0}else{store.mapProvider=$0}})) {
                                ForEach(store.availableMapProviders) { Text($0.label).tag($0) }
                            }
                        } label: {
                            Image(systemName: "square.3.layers.3d")
                                .frame(width: 46, height: 46)
                                .background(.regularMaterial, in: Circle())
                        }
                        .accessibilityLabel("切换地图来源")
                    }

                    ScrollView(.horizontal,showsIndicators:false){HStack(spacing:8){
                        ForEach(PoiDiscoveryCategory.groups,id:\.0){id,label in
                            Button{model.category=id}label:{Text(LocalizedStringKey(label)).font(.subheadline).padding(.horizontal,12).padding(.vertical,8).background(PoiDiscoveryCategory.parent(model.category)==id ? Palette.green:Palette.mint,in:Capsule()).foregroundStyle(PoiDiscoveryCategory.parent(model.category)==id ? .white:Palette.ink)}.accessibilityIdentifier("poi-category-"+id)
                        }
                    }}.padding(6).background(.regularMaterial,in:RoundedRectangle(cornerRadius:16))
                    if !PoiDiscoveryCategory.filters(model.category).isEmpty {
                        HStack(spacing:6){
                            Text("細分類").font(.caption).foregroundStyle(Palette.muted)
                            ScrollView(.horizontal,showsIndicators:false){HStack(spacing:6){
                                if PoiDiscoveryCategory.parent(model.category) != "all"{categoryFilter(PoiDiscoveryCategory.parent(model.category),"全部")}
                                ForEach(PoiDiscoveryCategory.filters(model.category),id:\.0){id,title,_ in categoryFilter(id,title)}
                            }}
                        }.padding(6).background(.regularMaterial,in:RoundedRectangle(cornerRadius:14))
                    }
                    Button{showInsights=true;Task{await store.refreshInsights(places:insightPoints)}}label:{Label("交通・人潮・費用",systemImage:"info.circle").font(.caption).padding(10).background(.regularMaterial,in:Capsule())}.accessibilityIdentifier("travel-insights-open")

                    if areaChanged {
                        Button {
                            search()
                        } label: {
                            Label("搜索此区域", systemImage: "arrow.clockwise")
                                .font(.subheadline.bold())
                                .padding(.horizontal, 16)
                                .padding(.vertical, 10)
                                .background(.regularMaterial, in: Capsule())
                        }
                        .disabled(model.busy)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.top, 10)
            }
            .safeAreaInset(edge: .bottom, spacing: 0) {
                if !searching { results(height: panelHeight(geo.size.height)) }
            }
        }
        .background(Palette.cream)
        .navigationTitle(replacingPlace ? "寻找替代地点":addingToDay ? "寻找餐厅，加入当天":"探索目的地")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if !embedded {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("完成") { dismiss() }
                }
            }
        }
    }
    .tint(Palette.green)
    .overlay {if featureLoading{ProgressView("正在載入地點…").padding(16).background(.regularMaterial,in:Capsule()).accessibilityIdentifier("map-feature-loading")}}
    .alert("地點資料暫時無法載入",isPresented:Binding(get:{featureError != nil},set:{if !$0{featureError=nil}})){Button("完成"){featureError=nil}}message:{Text(featureError ?? "")}
    .task(id:visiblePlaces.map(\.id).joined()+store.travelDataSource.rawValue+searchProvider.rawValue){await loadTicketSummaries()}
    .sheet(item: $detail) { SchedulePlaceDetail(candidate: $0) }
    .sheet(isPresented:$showInsights){TravelInsightsSheet(places:insightPoints)}
    .task(id:visiblePlaces.map(\.id).joined()+store.travelDataSource.rawValue+store.mapProvider.rawValue){if !visiblePlaces.isEmpty{await store.refreshInsights(places:insightPoints)}}
    .onAppear {
        model.language = store.language == .english ? "en" : store.language == .simplified ? "zh-CN" : "zh-TW"
        if let viewport = store.mapViewport {
            model.center = CLLocationCoordinate2D(latitude: viewport.lat, longitude: viewport.lng)
        }
        model.query=initialQuery
        if addingToDay{model.category=replacingPlace ? "all":"food"}
        model.configure(searchProvider, googleMapConfigured: SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)
    }
    .onChange(of:localProvider){_,provider in if addingToDay{detail=nil;areaChanged=false;model.configure(provider,googleMapConfigured:SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)}}
    .onChange(of: store.mapProvider) { _, provider in
        guard !addingToDay else{return}
        detail = nil
        areaChanged = false
        model.configure(provider, googleMapConfigured: SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)
    }
    .onChange(of:store.travelDataSource){_,_ in detail=nil;areaChanged=false;model.configure(searchProvider,googleMapConfigured:SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)}
    .onChange(of: model.category) { _, _ in
        searching=false;areaChanged=false;model.search()
    }
    .onChange(of: model.selected) { _, id in
        if id == nil {focus=nil}
        if id != nil {
            searching = false
            expanded = false
        }
    }
    .onDisappear { model.stop() }
    }
    private func panelHeight(_ height:CGFloat)->CGFloat {max(180,height*(expanded ? 0.65:0.36))}
    @ViewBuilder private var map:some View {
        switch searchProvider {
        case .apple,.osm:PoiNativeMap(places:visiblePlaces,osm:searchProvider == .osm,center:model.center,zoom:zoom,onCamera:{zoom=$1;areaChanged=false;model.cameraChanged($0,zoom:$1)},onSelect:{id in if let point=visiblePlaces.first(where:{$0.id==id}){focusPlace(point)}},insightMarkers:insightMarkers,focus:focus,onFeature:{point in model.stop();if !model.places.contains(where:{$0.id==point.id}){model.places.append(point)};focusPlace(point);detail=point},onFeatureStatus:{loading,error in featureLoading=loading;featureError=error}).accessibilityIdentifier("poi-map-"+searchProvider.rawValue)
        case .google:
            if SydneyBuddyApp.mapReady{PoiGoogleMap(places:visiblePlaces,center:model.center,zoom:zoom,onCamera:{zoom=$1;areaChanged=false;model.cameraChanged($0,zoom:$1)},onSelect:{id in if let point=visiblePlaces.first(where:{$0.id==id}){focusPlace(point)}},insightMarkers:insightMarkers,focus:focus)}
            else{ContentUnavailableView("地图尚未配置",systemImage:"map",description:Text("请切换到 Apple Maps 或 OpenStreetMap。"))}
        case .schematic:ContentUnavailableView("请选择在线底图",systemImage:"map",description:Text("示意图不支持地点搜索。"))
        }
    }
    private func results(height:CGFloat)->some View {
        VStack(spacing:0){
            Button{togglePanel()}label:{VStack(spacing:10){Capsule().fill(Palette.muted.opacity(0.35)).frame(width:36,height:4);HStack{Text(LocalizedStringKey(model.busy ? "正在寻找好去处…":"附近地点")).font(.headline);Text("\(visiblePlaces.count)").font(.caption).foregroundStyle(Palette.muted);Spacer();Image(systemName:expanded ? "chevron.down":"chevron.up").font(.caption.bold())}}.padding(.horizontal,20).padding(.top,10).padding(.bottom,12)}.buttonStyle(.plain).accessibilityLabel(expanded ? "收起地点列表":"展开地点列表")
                .gesture(DragGesture(minimumDistance:15).onEnded{value in if abs(value.translation.height)>20{expanded=value.translation.height<0}})
            if model.busy{ProgressView().padding(12)}
            if let summary=model.searchSummary{Text(LocalizedStringKey(summary)).font(.caption).foregroundStyle(Palette.muted).padding(.horizontal,16).padding(.bottom,6).accessibilityIdentifier("poi-search-summary")}
            if let error=model.error{ScrollView{VStack(spacing:10){Text("暂时没找到地点资料").font(.headline);Text(error).font(.caption).foregroundStyle(Palette.muted);Button("重新搜索"){search()}}.padding(20)}}
            else if !model.busy && visiblePlaces.isEmpty {ContentUnavailableView("换个地方看看",systemImage:"magnifyingglass",description:Text("移动地图后自动搜索，也可点“搜索此区域”，或换一个关键词。"))}
            else{ScrollViewReader{proxy in ScrollView{LazyVStack(spacing:0){ForEach(orderedPlaces){p in placeRow(p).id(p.id);Divider().padding(.leading,68)}}}.onChange(of:model.selected){_,id in if let id{proxy.scrollTo(id,anchor:.top)}}}}
            Text(store.language.text("地點資料：{0}",model.source)).font(.caption2).foregroundStyle(Palette.muted)
            attribution.padding(.horizontal,16).padding(.vertical,6)
        }.frame(height:height).frame(maxWidth:.infinity).background(Palette.cream,in:UnevenRoundedRectangle(topLeadingRadius:24,topTrailingRadius:24)).shadow(color:.black.opacity(0.08),radius:12,y:-3)
    }
    private func placeRow(_ p:ScheduleCandidate)->some View {
        let priceText:String=ticketSummaries[p.id]?.label(store.language) ?? p.mappedFoodPriceLabel ?? store.insights?.places.first{$0.id==p.id}?.price.summary ?? "費用未知"
        let metaLine:String="\(p.mappedRatingLabel) · \(priceText) · 人潮未知"
        return HStack(alignment:.top,spacing:12){
            Image(systemName:p.discoverySymbol).font(.title3).foregroundStyle(Palette.green).frame(width:38,height:44).background(Palette.mint,in:RoundedRectangle(cornerRadius:12))
            Button{focusPlace(p)}label:{VStack(alignment:.leading,spacing:5){Text(p.name).font(.subheadline.bold()).multilineTextAlignment(.leading);Text(metaLine).font(.caption2).foregroundStyle(Palette.muted);Text(p.description ?? "点击查看地点详情").font(.caption).foregroundStyle(Palette.muted).lineLimit(2)}.frame(maxWidth:.infinity,alignment:.leading)}.buttonStyle(.plain).accessibilityLabel("查看地点："+p.name).accessibilityIdentifier("poi-focus-"+p.id)
            Button{focusPlace(p);detail=p}label:{Image(systemName:"info.circle").frame(width:36,height:44)}.accessibilityLabel("介紹："+p.name).accessibilityIdentifier("poi-detail-"+p.id)
            if p.source == "Google Places" {
                Text("Google 地點暫不匯入本機示例時間軸").font(.caption2).foregroundStyle(Palette.muted)
            } else {
                Button {
                    if addingToDay {if !replacingPlace{toolbox.add(p)};onAdd(p)}
                    else if isAdded(p) { toolbox.remove(p.id) }
                    else { toolbox.add(p); onAdd(p) }
                } label: {
                    Image(systemName:addingToDay ? "plus":isAdded(p) ? "checkmark":"plus").font(.headline).frame(width:44,height:44).background(.white,in:Circle())
                }
                .accessibilityLabel((replacingPlace ? "用这个替换":addingToDay ? "加入当天":store.language.text(isAdded(p) ? "移出工具箱":"＋工具箱")) + "：" + p.name)
                .accessibilityIdentifier("poi-toggle-"+p.id)
            }
        }.padding(.horizontal,16).padding(.vertical,12).background(model.selected==p.id ? Palette.mint.opacity(0.5):.clear)
    }
    @ViewBuilder private var attribution:some View {
        if searchProvider == .osm {
            VStack(spacing:3) {
                HStack{Link("地圖 © OpenStreetMap contributors · ODbL",destination:URL(string:"https://www.openstreetmap.org/copyright")!);Spacer();Link(model.attribution,destination:URL(string:model.attribution.contains("OpenStreetMap") ? "https://www.openstreetmap.org/copyright":"https://docs.overturemaps.org/attribution/")!).lineLimit(1)}
                if let version=model.dataVersion { Text(store.language.text("本機地點資料快照：{0}",version)) }
            }.font(.caption2).foregroundStyle(Palette.muted)
        } else{Text(model.attribution).font(.caption2).foregroundStyle(Palette.muted)}
    }
    private func categoryFilter(_ id:String,_ title:String)->some View {
        Button{model.category=id}label:{Text(LocalizedStringKey(title)).font(.caption.bold()).padding(.horizontal,10).padding(.vertical,8).background(model.category==id ? Palette.green:Palette.mint,in:Capsule()).foregroundStyle(model.category==id ? .white:Palette.ink)}.accessibilityIdentifier("poi-category-"+id)
    }
    private func isAdded(_ p:ScheduleCandidate)->Bool {toolbox.places.contains{$0.id==p.id}}
    private func loadTicketSummaries() async {
        ticketSummaries=[:];let points=Array(visiblePlaces.prefix(80));let source=store.travelDataSource;let provider=searchProvider
        guard !points.isEmpty else{return}
        do{
            let raw=try await SocialImportModel().call("activities/summaries",body:["places":points.map{["id":$0.id,"name":$0.name,"lat":$0.lat,"lng":$0.lng] as [String:Any]}],store:store)
            try Task.checkCancellation()
            guard source==store.travelDataSource,provider==searchProvider,raw["dataSource"] as? String==source.rawValue else{return}
            ticketSummaries=try JSONDecoder().decode([String:TicketPriceSummary].self,from:JSONSerialization.data(withJSONObject:raw["summaries"] ?? [:]))
        }catch{ /* Reference prices are optional; unavailable data keeps the unknown label. */ }
    }
    private func focusPlace(_ point:ScheduleCandidate){
        searching=false;model.stop();model.center = .init(latitude:point.lat,longitude:point.lng);model.selected=point.id;expanded=false;focus=PoiMapFocus(point:point)
    }
    private func search(){searching=false;areaChanged=false;model.search()}
    private func togglePanel(){withAnimation(reduceMotion ? nil:.easeInOut(duration:0.2)){expanded.toggle()}}
}
@MainActor final class AppleFeatureLookup {
    private var job:Task<Void,Never>?
    private var generation=UUID()
    func cancel(){generation=UUID();job?.cancel();job=nil}
    func load(operation:@escaping @MainActor () async throws -> ScheduleCandidate,onResult:@escaping (ScheduleCandidate)->Void,onStatus:@escaping (Bool,String?)->Void){
        cancel();let token=generation;onStatus(true,nil)
        job=Task{@MainActor in
            do{
                let point=try await operation();try Task.checkCancellation()
                guard generation==token else{return};job=nil;onStatus(false,nil);onResult(point)
            }catch{
                guard generation==token,!Task.isCancelled else{return};job=nil;onStatus(false,"請再點一次地圖上的地點重試。");
            }
        }
    }
}

struct PoiMapFocus {let token=UUID();let point:ScheduleCandidate}

final class PoiPin:NSObject,MKAnnotation {
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
    var focus:PoiMapFocus?=nil
    var onFeature:(ScheduleCandidate)->Void={_ in}
    var onFeatureStatus:(Bool,String?)->Void={_,_ in}
    func makeCoordinator()->Coordinator{Coordinator(self)}
    static func dismantleUIView(_ map:MKMapView,coordinator:Coordinator){coordinator.featureLookup.cancel();map.delegate=nil}
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:PoiNativeMap;var key="";var focusToken:UUID?;var focusing=false;let featureLookup=AppleFeatureLookup()
        init(_ p:PoiNativeMap){parent=p}
        func mapView(_ map:MKMapView,rendererFor overlay:MKOverlay)->MKOverlayRenderer{if let tiles=overlay as? MKTileOverlay{return MKTileOverlayRenderer(tileOverlay:tiles)};return MKOverlayRenderer(overlay:overlay)}
        func mapView(_ map:MKMapView,regionDidChangeAnimated animated:Bool){if focusing{focusing=false;return};parent.onCamera(map.centerCoordinate,log2(360/max(0.00001,map.region.span.longitudeDelta)))}
        func mapView(_ map:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            if let pin=annotation as? InsightMapPin {let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);view.markerTintColor=pin.kind == "price" ? .systemGreen:.systemOrange;view.glyphText=pin.kind == "price" ? "$":"!";view.canShowCallout=true;return view}
            guard let pin=annotation as? PoiPin else{return nil}
            let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);let selected=pin.point.id==parent.focus?.point.id;view.markerTintColor=selected ? .systemBlue:pin.point.category=="美食" ? .systemOrange:.systemTeal;view.displayPriority=selected ? .required:.defaultHigh;view.zPriority=selected ? .max:.defaultUnselected;view.transform=selected ? CGAffineTransform(scaleX:1.2,y:1.2):.identity;view.glyphImage=UIImage(systemName:pin.point.discoverySymbol);view.canShowCallout=true;return view
        }
        func mapView(_ map:MKMapView,didSelect view:MKAnnotationView){if let pin=view.annotation as? PoiPin,pin.point.id != parent.focus?.point.id{featureLookup.cancel();parent.onFeatureStatus(false,nil);parent.onSelect(pin.point.id)}}
        func mapView(_ map:MKMapView,didSelect annotation:MKAnnotation){
            guard !parent.osm,let feature=annotation as? MKMapFeatureAnnotation,feature.featureType == .pointOfInterest else{return}
            let request=MKMapItemRequest(mapFeatureAnnotation:feature)
            featureLookup.load(operation:{
                let item=try await withTaskCancellationHandler{try await request.mapItem}onCancel:{Task{@MainActor in request.cancel()}}
                guard let point=ApplePoiProvider.candidate(item)else{throw NSError(domain:"MapFeature",code:1)}
                return point
            },onResult:{[weak self] point in guard let self,!self.parent.osm else{return};self.parent.onFeature(point)},onStatus:{[weak self,weak map] loading,error in
                self?.parent.onFeatureStatus(loading,error)
                if error != nil{map?.deselectAnnotation(feature,animated:false)}
            })
        }
    }
    func makeUIView(context:Context)->MKMapView{let map=MKMapView();map.delegate=context.coordinator;map.selectableMapFeatures=osm ? []:[.pointsOfInterest];if osm{map.addOverlay(OsmTiles(),level:.aboveLabels)};let delta=360/pow(2,zoom);map.setRegion(MKCoordinateRegion(center:center,span:MKCoordinateSpan(latitudeDelta:delta,longitudeDelta:delta)),animated:false);return map}
    func updateUIView(_ map:MKMapView,context:Context){
        if context.coordinator.parent.osm != osm{context.coordinator.featureLookup.cancel();onFeatureStatus(false,nil)}
        context.coordinator.parent=self;map.selectableMapFeatures=osm ? []:[.pointsOfInterest]
        let tiles=map.overlays.filter{$0 is OsmTiles}
        if osm && tiles.isEmpty{map.addOverlay(OsmTiles(),level:.aboveLabels)}
        if !osm{map.removeOverlays(tiles)}
        let key=places.map(\.id).joined(separator:",")+insightMarkers.map{$0.id+$0.title}.joined()+(focus?.point.id ?? "")
        if context.coordinator.key != key {
            context.coordinator.key=key;map.removeAnnotations(map.annotations);map.addAnnotations(places.map(PoiPin.init))
            for item in insightMarkers{let pin=InsightMapPin();pin.kind=item.kind;pin.coordinate = .init(latitude:item.lat,longitude:item.lng);pin.title=item.title;pin.subtitle=item.summary;map.addAnnotation(pin)}
        }
        if let focus,context.coordinator.focusToken != focus.token,let pin=map.annotations.compactMap({$0 as? PoiPin}).first(where:{$0.point.id==focus.point.id}) {
            context.coordinator.focusToken=focus.token;context.coordinator.focusing=true
            let point=MKMapPoint(pin.coordinate);let width=900/MKMetersPerMapPointAtLatitude(pin.coordinate.latitude)
            let rect=MKMapRect(x:point.x-width/2,y:point.y-width/2,width:width,height:width)
            map.setVisibleMapRect(rect,edgePadding:UIEdgeInsets(top:map.bounds.height*0.30,left:32,bottom:map.bounds.height*0.36,right:32),animated:!UIAccessibility.isReduceMotionEnabled)
            map.selectAnnotation(pin,animated:true)
        }
    }
}

private struct PoiGoogleMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var center:CLLocationCoordinate2D;var zoom:Double
    var onCamera:(CLLocationCoordinate2D,Double)->Void;var onSelect:(String)->Void
    var insightMarkers:[InsightMarker]=[]
    var focus:PoiMapFocus?=nil
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,GMSMapViewDelegate {
        var parent:PoiGoogleMap;var key="";var focusToken:UUID?;var focusing=false;var markers:[String:GMSMarker]=[:]
        init(_ parent:PoiGoogleMap){self.parent=parent}
        func mapView(_ map:GMSMapView,willMove gesture:Bool){if gesture{focusing=false}}
        func mapView(_ map:GMSMapView,idleAt camera:GMSCameraPosition){if focusing{focusing=false;return};parent.onCamera(camera.target,Double(camera.zoom))}
        func mapView(_ map:GMSMapView,didTap marker:GMSMarker)->Bool{if let id=marker.userData as? String{parent.onSelect(id)};return true}
    }
    func makeUIView(context:Context)->GMSMapView {
        let options=GMSMapViewOptions();options.camera=GMSCameraPosition(target:center,zoom:Float(zoom))
        let map=GMSMapView(options:options);map.delegate=context.coordinator;return map
    }
    func updateUIView(_ map:GMSMapView,context:Context){
        context.coordinator.parent=self;let key=places.map(\.id).joined(separator:",")+insightMarkers.map{$0.id+$0.title}.joined()+(focus?.point.id ?? "")
        if key != context.coordinator.key {context.coordinator.key=key;map.clear();context.coordinator.markers=[:]
        for item in insightMarkers{let pin=GMSMarker(position:.init(latitude:item.lat,longitude:item.lng));pin.title=item.title;pin.snippet=item.summary;pin.icon=GMSMarker.markerImage(with:item.kind == "price" ? .systemGreen:.systemOrange);pin.map=map}
        for point in places{let marker=GMSMarker(position:CLLocationCoordinate2D(latitude:point.lat,longitude:point.lng));marker.title=point.name;marker.userData=point.id;marker.icon=GMSMarker.markerImage(with:point.id==focus?.point.id ? .systemBlue:point.category=="美食" ? .systemOrange:.systemTeal);marker.zIndex=point.id==focus?.point.id ? 1000:0;marker.map=map;context.coordinator.markers[point.id]=marker}
        }
        if let focus,context.coordinator.focusToken != focus.token {
            context.coordinator.focusToken=focus.token;context.coordinator.focusing=true
            map.padding=UIEdgeInsets(top:map.bounds.height*0.30,left:24,bottom:map.bounds.height*0.36,right:24)
            if let selected=context.coordinator.markers[focus.point.id]{map.selectedMarker=selected;map.animate(to:GMSCameraPosition(target:selected.position,zoom:17))}
        }
    }
}

struct PlacePhoto:Codable,Identifiable {
    var url:String;var pageUrl:String;var title:String;var artist:String;var license:String;var licenseUrl:String?;var matchKind:String?;var source:String?
    var id:String {pageUrl}
}
@MainActor final class PlaceMediaModel:ObservableObject {
    @Published private(set) var photos:[PlacePhoto]=[]
    @Published private(set) var loading=false
    @Published private(set) var message:String?
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await BackendConnection.data(for:$0)}
    private var generation=UUID()
    func prepare(_ candidate:ScheduleCandidate,dataSource:TravelDataSource) async {
        generation=UUID();photos=candidate.photos ?? [];message=nil;loading=false
        if photos.isEmpty{await load(candidate,dataSource:dataSource)}
    }
    func load(_ candidate:ScheduleCandidate,dataSource:TravelDataSource) async {
        guard !loading else{return}
        let token=generation
        loading=true;message=nil
        defer{if token==generation{loading=false}}
        do{
            var request=URLRequest(url:BackendConnection.url("poi/media"))
            request.httpMethod="POST";dataSource.apply(to:&request);request.timeoutInterval=22;request.setValue("application/json",forHTTPHeaderField:"Content-Type")
            request.httpBody=try JSONSerialization.data(withJSONObject:["lat":candidate.lat,"lng":candidate.lng,"id":candidate.id,"name":candidate.name,"wikidata":candidate.wikidata ?? "","poiKind":candidate.poiKind ?? "","category":candidate.category])
            let (data,response)=try await loader(request)
            try Task.checkCancellation();guard token==generation else{return}
            guard (response as? HTTPURLResponse)?.statusCode==200 else{throw URLError(.badServerResponse)}
            struct Media:Decodable{var photos:[PlacePhoto]}
            let found=try JSONDecoder().decode(Media.self,from:data).photos
            if !found.isEmpty{photos=found}
            if photos.isEmpty{message="附近尚無可用的開放照片。"}
        }catch{
            guard !Task.isCancelled,token==generation else{return}
            message="照片暫時無法載入，請稍後重試。"
        }
    }
}
struct PlaceMediaView:View {
    var candidate:ScheduleCandidate
    @StateObject private var media=PlaceMediaModel()
    @State private var scene:MKLookAroundScene?
    @State private var panoramaLoading=false
    @State private var panoramaMessage:String?
    @EnvironmentObject var store:AppStore
    var body:some View {
        VStack(alignment:.leading,spacing:12){
            Text("照片與街景").font(.headline)
            Text(LocalizedStringKey(candidate.hiddenGem == true ? "社群照片保留作者與授權；附近照片不代表已確認拍攝此景點。":"附近 100 公尺的實景照片，並非已核實的商家或店內照片。")).font(.caption).foregroundStyle(Palette.muted)
            Button(media.photos.isEmpty ? "載入附近照片":"重新載入照片"){Task{await media.load(candidate,dataSource:store.travelDataSource)}}.disabled(media.loading).accessibilityIdentifier("place-media-retry")
            if media.loading{ProgressView("正在載入照片…").accessibilityIdentifier("place-media-loading")}
            if let message=media.message{Text(LocalizedStringKey(message)).font(.caption).accessibilityIdentifier("place-media-status")}
            ScrollView(.horizontal){HStack(alignment:.top,spacing:12){ForEach(media.photos,content:photoCard)}}
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
        }.task(id:candidate.id+"|"+store.travelDataSource.rawValue){
            scene=nil;panoramaMessage=nil;media.loader=store.loader
            await media.prepare(candidate,dataSource:store.travelDataSource)
        }
    }
    @ViewBuilder private func photoCard(_ photo:PlacePhoto)->some View {
        let credit="\(photo.artist) · \(photo.license)"
        VStack(alignment:.leading,spacing:5){
            AsyncImage(url:URL(string:photo.url)){phase in
                switch phase{case .success(let image):image.resizable().scaledToFill();case .failure:Text("照片無法載入");default:ProgressView()}
            }.frame(width:260,height:170).clipped().clipShape(RoundedRectangle(cornerRadius:12))
            Text(photo.title).font(.caption).lineLimit(2)
            Text(credit).font(.caption2).lineLimit(3)
            if photo.matchKind == "exact-place" || photo.matchKind == "guide-file" {Text("✓ 已配對景點實景照片").font(.caption2).foregroundStyle(Palette.green)}
            else if photo.matchKind == "nearby-unverified" {Text("附近照片，景點配對未確認").font(.caption2)}
            if let url=URL(string:photo.pageUrl){Link("照片來源與授權",destination:url).font(.caption)}
        }.frame(width:260,alignment:.leading)
    }
}

struct TicketPriceSummary:Decodable {
    let amount:Double;let currency:String;let basis:String
    func label(_ language:AppLanguage)->String {
        if amount==0{return language.text(basis=="general_admission" ? "一般入場免費":"活動免費")}
        let money=(currency=="AUD" ? "A$":currency+" ")+String(format:amount.rounded()==amount ? "%.0f":"%.2f",amount)
        return language.text(basis=="guided_tour" ? "成人導覽 {0} 起":basis=="combo" ? "成人套裝 {0} 起":"成人票 {0} 起",money)
    }
}

@MainActor enum ApplePlanningDiscovery {
    /// MapKit may label station groceries and snack counters as restaurants.
    static func primaryMealName(_ name:String)->Bool {
        name.range(of:"bakery|bakehouse|patisserie|juice|fruit|food market|mini.?mart|grocery|supermarket|catering|coffee|café|cafe|面包|烘焙|果汁|咖啡",options:[.regularExpression,.caseInsensitive]) == nil
    }
    static func isTransportAccess(_ p:ScheduleCandidate)->Bool {
        guard p.poiKind != "restaurant",p.category != "美食" else{return false}
        return p.poiKind=="transport" || p.name.range(of:#"\b(station|ferry terminal|bus stop|bus interchange|pontoon|wharf)\b|车站|車站|公交站|巴士站|登船点|登船點"#,options:[.regularExpression,.caseInsensitive]) != nil
    }
    static func planningCandidate(_ value:ScheduleCandidate)->ScheduleCandidate {
        var p=value
        let meal=p.poiKind=="restaurant" && primaryMealName(p.name)
        p.activityType=meal ? "meal":"attraction";p.cost=meal ? 30:0
        if meal{p.category="美食";p.visitMinutes=45}
        return p
    }
    static func search(_ profile:TripProfile,preserveDetails:Bool=false,lookup:(CLLocationCoordinate2D,String,String) async throws -> [ScheduleCandidate] = {point,category,query in try await ApplePoiProvider.search(point,category,query)}) async throws -> [ScheduleCandidate] {
        var groups:[(String,String)]=[]
        let interests=profile.interests
        if interests.contains("美食"){groups.append(("food","restaurants"))}
        if interests.contains("购物"){groups.append(("shops",""))}
        for interest in interests where interest != "美食" && interest != "购物" {
            let query=interest=="自然" ? "parks":interest=="艺术" ? "art galleries":interest=="文化" ? "museums":interest=="城市" ? "Sydney landmarks":"tourist attractions"
            groups.append(("sights",query))
        }
        if !groups.contains(where:{$0.0=="sights"}){groups.append(("sights","tourist attractions"))}
        let must=(profile.mustDo ?? "").trimmingCharacters(in:.whitespacesAndNewlines)
        if !["","无","没有","未确定","暂不确定","无特别要求"].contains(must){groups.insert(("all",must),at:0)}
        var found:[ScheduleCandidate]=[];var seen=Set<String>()
        for (category,query) in groups.prefix(5) {
            try Task.checkCancellation()
            var results=try await lookup(profile.start.location,category,query)
            // Keyword search can miss nearby museums/parks. Enrich from actual local POIs,
            // preserving their real categories instead of filling gaps with catalogue examples.
            if category=="sights",!query.isEmpty {results += try await lookup(profile.start.location,category,"")}
            var added=0
            for var p in results.filter({category != "food" || primaryMealName($0.name)}) where seen.insert(p.id).inserted {
                if preserveDetails {
                    let kind=p.poiKind ?? ""
                    if (kind=="restaurant" || kind.hasSuffix("_restaurant") || ["fast_food","food_court","casual_eatery"].contains(kind)),primaryMealName(p.name) {
                        p.activityType="meal";p.category="美食"
                    }
                }else{p=planningCandidate(p)}
                if category != "all" && isTransportAccess(p){continue}
                if category=="shops"{p.category="购物"}
                found.append(p);added+=1
                if added>=12{break}
            }
        }
        return Array(found.prefix(60))
    }
}
