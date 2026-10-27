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
    static let groups=[("all","全部"),("sights","景點"),("food","餐飲"),("gems","私房景點"),("lodging","住宿"),("shops","店家"),("facilities","生活設施")]
    static let details=[("restaurants","餐廳","food"),("cafes","咖啡廳","food"),("bakeries","甜點烘焙","food"),("bars","酒吧夜生活","food"),("museums","博物館","sights"),("parks","公園綠地","sights"),("beaches","海灘","sights"),("hotels","飯店旅館","lodging"),("hostels","青年旅館","lodging"),("supermarkets","超市雜貨","shops"),("toilets","廁所","facilities"),("drinking_water","飲水點","facilities")]
    static func parent(_ id:String)->String{details.first{$0.0==id}?.2 ?? id}
    static func filters(_ id:String)->[(String,String,String)]{let group=parent(id);return group=="all" ? []:details.filter{$0.2==group}}
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
    @Published private(set) var searchCenter=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
    @Published var selected:String?
    @Published var busy=false
    @Published var error:String?
    @Published var hasSearched=false
    @Published var searchSummary:String?
    @Published private(set) var truncated=false
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
    func cameraChanged(_ point:CLLocationCoordinate2D,zoom:Double,automaticallySearch:Bool=true) {
        guard CLLocationCoordinate2DIsValid(point),zoom.isFinite else{return}
        if let last=lastCamera {
            let distance=CLLocation(latitude:point.latitude,longitude:point.longitude).distance(from:CLLocation(latitude:last.0.latitude,longitude:last.0.longitude))
            guard distance>60 || abs(zoom-last.1)>0.35 else{return}
        }
        lastCamera=(point,zoom);center=point
        viewportRadius=min(5000,max(500,156543.03392*cos(point.latitude * .pi/180)/pow(2,zoom)*420))
        stop();selected=nil;error=nil
        guard automaticallySearch else{return}
        places=[]
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
        stop();error=nil;searchSummary=nil;truncated=false;hasSearched=true;places=[];selected=nil;searchCenter=center
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
                    guard token==generation else{return};if let credit=parsed.attribution{attribution=credit};truncated=parsed.truncated == true;searchSummary=parsed.truncated == true ? "已顯示部分結果，請輸入名稱或縮小分類。":parsed.message
                    if kind == "gems" || current == .osm || route.warehouse{openSource=parsed.source ?? "開放地點資料";dataVersion=parsed.metadata?.release}
                }
                try Task.checkCancellation();guard token==generation else{return}
                places=result;busy=false
            }catch {if !Task.isCancelled && token==generation{self.error="地點資料暫時無法載入："+error.localizedDescription;busy=false}}
        }
    }
    func stop(){cameraJob?.cancel();cameraJob=nil;generation=UUID();job?.cancel();job=nil;busy=false}
}
private struct ExploreHeaderHeight:PreferenceKey {
    static var defaultValue:CGFloat=126
    static func reduce(value:inout CGFloat,nextValue:()->CGFloat){value=max(value,nextValue())}
}
enum ExploreDrawer:Int {
    case collapsed,preview,expanded
    func height(in available:CGFloat,header:CGFloat=126)->CGFloat {
        switch self {case .collapsed:return 108;case .preview:return min(330,max(230,available*0.40));case .expanded:return max(160,available-header-12)}
    }
    var next:Self {self == .collapsed ? .preview:self == .preview ? .expanded:.collapsed}
    func toggled(resultCount:Int)->Self {resultCount==1 && self == .preview ? .collapsed:next}
    func dragged(up:Bool)->Self {up ? (self == .collapsed ? .preview:.expanded):(self == .expanded ? .preview:.collapsed)}
}
struct ExploreCheck:Identifiable {let id:String;let text:String;let blocking:Bool}
enum ExplorePresentation {
    static func money(_ amount:Double)->String {String(format:amount.rounded()==amount ? "A$%.0f":"A$%.2f",amount)}
    static func checks(_ state:ScheduleState,day:Int)->[ExploreCheck] {
        guard state.days.indices.contains(day) else{return []}
        let current=state.days[day]
        var seen=Set<String>()
        return state.issues.filter{$0.day==day || $0.day == -1}.sorted{$0.blocking && !$1.blocking}.compactMap {issue in
            guard seen.insert(issue.code).inserted else{return nil}
            let text:String
            switch issue.code {
            case "budget":text="Estimated day cost "+money(current.estimatedCost)+" exceeds the day budget of "+money(current.dailyBudget ?? state.settings.dailyBudget)+"."
            case "total-budget":text="Estimated trip cost "+money(state.totalCost)+" exceeds the trip budget of "+money(state.settings.totalBudget)+"."
            case "steps":text="About \(current.estimatedSteps) steps exceeds this day’s limit of \(current.maxSteps ?? state.settings.maxSteps)."
            case "time-budget":text="Travel and queues exceed this day’s time allowance."
            case "end-time":text="This change would finish around "+current.finishTime+", after the planned end of the day."
            case "restroom-gap":text="Leave room for a bathroom break."
            case "poi-unknown":text="Some place prices and opening hours still need checking."
            case "unknown-queue":text="Queue times are unknown. Leave a little spare time."
            case "weather":text="Some stops have rain warnings. Check the forecast before heading out."
            case "unconfirmed":text="Some stays or facilities still need confirming."
            case "appointment":text="A stop would miss a fixed time slot."
            case "hours","window":text="A stop conflicts with its planning time window."
            case "locked":text="This would change a locked stop."
            case "luggage":text="Luggage drop-off and collection need checking."
            case "restaurant-price-expired":text="A meal estimate has expired. Refresh it before relying on it."
            default:text=issue.blocking ? "This change needs another check before it can be saved.":"There is another planning note to review."
            }
            return ExploreCheck(id:issue.code,text:text,blocking:issue.blocking)
        }
    }
    static func insert(_ candidate:ScheduleCandidate,into state:inout ScheduleState,day:Int) {
        guard state.days.indices.contains(day),candidate.category != "住宿",!state.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id}) else{return}
        var point=candidate
        if point.category=="美食" {
            point.activityType="meal"
            let estimate=point.restaurantData?.budgetEstimate
            let amount=estimate?.currency=="AUD" ? estimate?.perPerson:nil
            point.cost=amount ?? point.mappedFoodCost ?? state.settings.mealCost
            point.note=(point.note ?? "")+" Planning meal cost is an estimate, not a verified price."
        }
        if !state.candidates.contains(where:{$0.id==point.id}){state.candidates.append(point)}
        let activity=ScheduleActivity(id:UUID().uuidString,placeId:point.id,kind:point.activityKind,name:point.name,lat:point.lat,lng:point.lng,duration:point.visitMinutes,cost:point.cost,arrival:"",departure:"",scheduledStart:state.days[day].startMinute,travelMinutes:0,indoor:point.indoor)
        let position=state.days[day].activities.firstIndex(where:{$0.kind=="stay" || $0.kind=="bag-pickup"}) ?? state.days[day].activities.count
        state.days[day].activities.insert(activity,at:position)
    }
    static func ranked(_ places:[ScheduleCandidate],interests:[String],center:CLLocationCoordinate2D,selected:String?)->[ScheduleCandidate] {
        let origin=CLLocation(latitude:center.latitude,longitude:center.longitude)
        func distance(_ p:ScheduleCandidate)->Double {origin.distance(from:CLLocation(latitude:p.lat,longitude:p.lng))}
        return places.sorted {a,b in
            if (a.id==selected) != (b.id==selected){return a.id==selected}
            let am=interests.contains(a.category),bm=interests.contains(b.category)
            if am != bm{return am}
            let ad=distance(a),bd=distance(b)
            return abs(ad-bd)>1 ? ad<bd:a.id<b.id
        }
    }
    static func distance(_ p:ScheduleCandidate,from center:CLLocationCoordinate2D)->String {
        let meters=CLLocation(latitude:center.latitude,longitude:center.longitude).distance(from:CLLocation(latitude:p.lat,longitude:p.lng))
        return meters<1000 ? "\(max(10,Int((meters/10).rounded())*10)) m from search centre":String(format:"%.1f km from search centre",meters/1000)
    }
    static func price(_ p:ScheduleCandidate)->String? {
        if let estimate=p.restaurantData?.budgetEstimate,estimate.currency=="AUD",let value=estimate.perPerson,value.isFinite,value>0{return String(format:"Est. A$%.0f/person",value)}
        if let value=p.estimatedSpendAud,value.isFinite,value>0{return String(format:"Est. A$%.0f/person",value)}
        if p.category=="住宿",let value=p.mappedLodgingCost,value.isFinite,value>0{return String(format:"Est. A$%.0f/night",value)}
        if p.category=="美食",let value=p.mappedFoodCost,value.isFinite,value>0{return String(format:"Est. A$%.0f/person",value)}
        if !p.importedPoi,p.cost.isFinite,p.cost>0{return String(format:"Est. A$%.0f/person",p.cost)}
        return nil
    }
    static func mapPlaces(_ places:[ScheduleCandidate],selected:String?,limit:Int=40)->[ScheduleCandidate] {
        guard limit>0 else{return []}
        var seen=Set<String>()
        let spread=places.filter{point in seen.insert("\(Int(floor(point.lat/0.002))):\(Int(floor(point.lng/0.002)))").inserted}
        let spreadIDs=Set(spread.map(\.id))
        var result=Array((spread+places.filter{!spreadIDs.contains($0.id)}).prefix(limit))
        if let point=places.first(where:{$0.id==selected}),!result.contains(where:{$0.id==point.id}){if !result.isEmpty{result.removeLast()};result.append(point)}
        return result
    }
    static func clusters(_ places:[ScheduleCandidate],zoom:Double,selected:String?)->[[ScheduleCandidate]] {
        let size=360/pow(2,max(3,min(20,zoom)))*0.065
        let groups=Dictionary(grouping:places){p in p.id==selected ? "selected:"+p.id:"\(Int(floor(p.lat/size))):\(Int(floor(p.lng/size)))"}
        return groups.keys.sorted().compactMap{groups[$0]}
    }
}
@MainActor struct PoiDiscoveryView:View {
    @Environment(\.locale) private var interfaceLocale
    var embedded=false
    var initialQuery=""
    var initialCategory:String?=nil
    var addingToDay=false
    var replacingPlace=false
    var onAdd:(ScheduleCandidate)->Void = {PoiToolbox.shared.add($0)}
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var model=PoiSearchModel()
    @ObservedObject private var toolbox=PoiToolbox.shared
    @State private var detail:ScheduleCandidate?
    @State private var savingPlace:ScheduleCandidate?
    @State private var focus:PoiMapFocus?
    @State private var ticketSummaries:[String:TicketPriceSummary]=[:]
    @State private var featureLoading=false
    @State private var featureError:String?
    @State private var localProvider:MapProvider = .apple
    @State private var filtersOpen=false
    @State private var creditsOpen=false
    @State private var savedOpen=false
    @State private var savedFocus:ScheduleCandidate?
    @State private var showInsights=false
    @State private var requestInsights=false
    @State private var requestCredits=false
    @State private var showMapInsights=false
    @State private var receipt:String?
    @State private var zoom:Double=14
    @State private var cameraAction:RouteMapCameraAction?
    @State private var drawer:ExploreDrawer = .preview
    @State private var headerHeight:CGFloat=126
    @State private var areaChanged=false
    @State private var initialized=false
    @State private var resumeSearch=false
    @FocusState private var searching:Bool
    private let blue=Palette.primary
    private let ink=Palette.ink
    private var searchProvider:MapProvider {addingToDay ? localProvider:store.mapProvider}
    private var visiblePlaces:[ScheduleCandidate] {model.provider==searchProvider && model.dataSource==store.travelDataSource ? model.places:[]}
    private var orderedPlaces:[ScheduleCandidate] {ExplorePresentation.ranked(visiblePlaces,interests:model.category=="all" ? store.personalProfile.interests ?? []:[],center:model.center,selected:model.selected)}
    private var mapPlaces:[ScheduleCandidate] {ExplorePresentation.mapPlaces(orderedPlaces,selected:model.selected)}
    private var insightPoints:[[String:Any]] {[["id":"center","name":"搜尋中心","lat":model.center.latitude,"lng":model.center.longitude]]+visiblePlaces.prefix(20).map{["id":$0.id,"name":$0.name,"lat":$0.lat,"lng":$0.lng]}}
    private var insightMarkers:[InsightMarker] {showMapInsights ? (store.insights?.traffic.items ?? [])+(store.insights?.pricePlaces ?? []):[]}
    var body:some View {
        let _=interfaceLocale
        NavigationStack {
            GeometryReader {geo in
                ZStack(alignment:.top){
                    map(bottomInset:drawerHeight(geo.size.height)).ignoresSafeArea(edges:embedded ? [.top,.bottom]:.bottom)
                    searchControls.padding(.horizontal,16).padding(.top,8)
                        .background(GeometryReader{header in Color.clear.preference(key:ExploreHeaderHeight.self,value:header.size.height)})
                        .onPreferenceChange(ExploreHeaderHeight.self){headerHeight=$0}
                }
                .overlay(alignment:.bottomTrailing){
                    if !searching && drawer != .expanded && searchProvider != .schematic {
                        mapControls.padding(.trailing,16).padding(.bottom,drawerHeight(geo.size.height)+12)
                    }
                }
                .overlay(alignment:.bottom){if !searching{results(height:drawerHeight(geo.size.height))}}
                .overlay(alignment:.bottom){
                    if let receipt,!searching {
                        HStack(spacing:10){PetAvatar(cue:KoalaCue(event:.preferenceConfirmed,id:receipt),size:42).frame(width:42,height:48);Text(receipt).font(.subheadline.weight(.semibold)).lineLimit(2)}
                            .padding(12).background(.white,in:RoundedRectangle(cornerRadius:18)).shadow(color:.black.opacity(0.10),radius:10,y:3)
                            .padding(.horizontal,24).padding(.bottom,drawerHeight(geo.size.height)+12).allowsHitTesting(false)
                    }
                }
            }
            .background(.white).foregroundStyle(ink)
            .navigationTitle(uiText(replacingPlace ? "寻找替代地点":addingToDay ? "寻找餐厅，加入当天":"Explore"))
            .navigationBarTitleDisplayMode(.inline)
            .toolbar(embedded ? .hidden:.visible,for:.navigationBar)
            .toolbar{if !embedded{ToolbarItem(placement:.topBarTrailing){Button(uiText("完成")){dismiss()}}}}
        }.buddySurface()
        .overlay{if featureLoading{ProgressView(uiText("Loading place…")).padding(16).background(.regularMaterial,in:Capsule()).accessibilityIdentifier("map-feature-loading")}}
        .alert(uiText("地點資料暫時無法載入"),isPresented:Binding(get:{featureError != nil},set:{if !$0{featureError=nil}})){Button(uiText("完成")){featureError=nil}}message:{Text(uiText(featureError ?? ""))}
        .sheet(item:$detail){SchedulePlaceDetail(candidate:$0)}
        .sheet(item:$savingPlace){point in ExploreSavePlaceSheet(candidate:point){message in savingPlace=nil;acknowledge(message)}.environmentObject(store)}
        .sheet(isPresented:$filtersOpen,onDismiss:{if requestInsights{requestInsights=false;showInsights=true;Task{await store.refreshInsights(places:insightPoints)}}else if requestCredits{requestCredits=false;creditsOpen=true}}){filterSheet}
        .sheet(isPresented:$creditsOpen){creditsSheet}
        .sheet(isPresented:$savedOpen,onDismiss:{if let point=savedFocus{savedFocus=nil;if !model.places.contains(where:{$0.id==point.id}){model.places.append(point)};focusPlace(point)}}){savedSheet}
        .sheet(isPresented:$showInsights){TravelInsightsSheet(places:insightPoints)}
        .task(id:visiblePlaces.map(\.id).joined()+store.travelDataSource.rawValue+searchProvider.rawValue){await loadTicketSummaries()}
        .task(id:showMapInsights.description+visiblePlaces.map(\.id).joined()+store.travelDataSource.rawValue){if showMapInsights && !visiblePlaces.isEmpty{await store.refreshInsights(places:insightPoints)}}
        .task(id:receipt){guard let current=receipt else{return};do{try await Task.sleep(nanoseconds:4_500_000_000);if receipt==current{receipt=nil}}catch{}}
        .onAppear {
            updateSearchLanguage(store.language)
            if initialized{if model.provider != searchProvider || model.dataSource != store.travelDataSource{configure()}else if resumeSearch{model.search()};resumeSearch=false;return}
            initialized=true
            if let viewport=store.mapViewport{model.center = .init(latitude:viewport.lat,longitude:viewport.lng)}
            model.query=initialQuery
            if let initialCategory{model.category=initialCategory}else if addingToDay{model.category=replacingPlace ? "all":"food"}
            configure()
        }
        .onChange(of:store.language){_,language in updateSearchLanguage(language)}
        .onChange(of:localProvider){_,_ in if addingToDay{configure()}}
        .onChange(of:store.mapProvider){_,_ in if !addingToDay{configure()}}
        .onChange(of:store.travelDataSource){_,_ in configure()}
        .onChange(of:model.category){_,_ in search()}
        .onChange(of:model.selected){_,id in if id==nil{focus=nil}}
        .onDisappear{resumeSearch=model.busy;model.stop()}
    }
    private var searchControls:some View {
        VStack(spacing:10){
            HStack(spacing:8){
                HStack(spacing:8){
                    Image(systemName:"magnifyingglass").foregroundStyle(Palette.muted)
                    TextField(uiText("Find a place"),text:$model.query).focused($searching).submitLabel(.search).onSubmit{search()}.accessibilityIdentifier("poi-search")
                    if !model.query.isEmpty{Button{model.query="";search()}label:{Image(systemName:"xmark.circle.fill").foregroundStyle(Palette.muted)}.accessibilityLabel(uiText("清空搜索"))}
                }.padding(14).background(.white,in:RoundedRectangle(cornerRadius:18))
                Button{searching=false;savedOpen=true}label:{Image(systemName:"bookmark").frame(width:44,height:48).background(.white,in:RoundedRectangle(cornerRadius:16))}.accessibilityLabel(uiText("Saved places")).accessibilityIdentifier("poi-saved-places")
                Button{searching=false;filtersOpen=true}label:{Image(systemName:"slider.horizontal.3").frame(width:44,height:48).background(.white,in:RoundedRectangle(cornerRadius:16))}.accessibilityLabel(uiText("Filters and map options")).accessibilityIdentifier("poi-filters")
            }.shadow(color:.black.opacity(0.07),radius:8,y:3)
            if !searching {
                categoryBar
                if areaChanged{Button{search()}label:{Label(uiText("搜索此区域"),systemImage:"arrow.clockwise").font(.subheadline.bold()).padding(.horizontal,16).padding(.vertical,11).background(.white,in:Capsule())}.accessibilityIdentifier("poi-search-area")}
            }
        }
    }
    private var categoryMenuGroups:[(String,String)] {PoiDiscoveryCategory.groups.filter{group in PoiDiscoveryCategory.details.contains{$0.2==group.0}}}
    private var selectedCategoryDetail:String? {PoiDiscoveryCategory.details.first{$0.0==model.category}?.1}
    private var categoryBar:some View {
        HStack(spacing:8){
            ScrollViewReader{proxy in
                ScrollView(.horizontal,showsIndicators:false){HStack(spacing:8){ForEach(PoiDiscoveryCategory.groups,id:\.0){id,label in
                    categoryChip(id,id=="all" && !(store.personalProfile.interests ?? []).isEmpty ? "For you":uiText(label)).id(id)
                }}.padding(.horizontal,2)}
                .onChange(of:model.category){_,category in withAnimation(reduceMotion ? nil:.easeInOut(duration:0.2)){proxy.scrollTo(PoiDiscoveryCategory.parent(category),anchor:.center)}}
            }
            Menu{
                categoryOption(PoiDiscoveryCategory.parent(model.category),"全部")
                if PoiDiscoveryCategory.parent(model.category)=="all"{
                    ForEach(categoryMenuGroups,id:\.0){group in
                        Section(uiText(group.1)){ForEach(PoiDiscoveryCategory.details.filter{$0.2==group.0},id:\.0){option in categoryOption(option.0,option.1)}}
                    }
                }else{ForEach(PoiDiscoveryCategory.filters(model.category),id:\.0){id,title,_ in categoryOption(id,title)}}
            }label:{Label(uiText(selectedCategoryDetail ?? "Type"),systemImage:"line.3.horizontal.decrease").font(.caption.weight(.semibold)).fixedSize(horizontal:true,vertical:false).padding(.horizontal,12).frame(minHeight:40).background(selectedCategoryDetail==nil ? .white:blue,in:Capsule()).foregroundStyle(selectedCategoryDetail==nil ? ink:.white)}
                .accessibilityIdentifier("poi-category-filter").accessibilityLabel(uiText("細分類")+": "+uiText(selectedCategoryDetail ?? "全部"))
        }
    }
    private func categoryOption(_ id:String,_ title:String)->some View {
        Button{model.category=id}label:{if model.category==id{Label(uiText(title),systemImage:"checkmark")}else{Text(uiText(title))}}.accessibilityIdentifier("poi-category-"+id)
    }
    private func categoryChip(_ id:String,_ label:String,exact:Bool=false)->some View {
        let selected=exact ? model.category==id:PoiDiscoveryCategory.parent(model.category)==id
        return Button{model.category=id}label:{Text(uiText(label)).font(exact ? .caption.weight(.semibold):.subheadline.weight(.semibold)).padding(.horizontal,14).frame(minHeight:exact ? 34:40).background(selected ? blue:Color.white,in:Capsule()).foregroundStyle(selected ? .white:ink)}.buttonStyle(BuddyPressStyle()).accessibilityAddTraits(selected ? [.isSelected]:[]).accessibilityIdentifier("poi-category-"+id)
    }
    private var mapControls:some View {
        VStack(spacing:10){
            VStack(spacing:0){
                Button{changeCamera(.zoomIn)}label:{Image(systemName:"plus").frame(width:44,height:44)}.accessibilityLabel(store.language.text("放大地圖")).accessibilityIdentifier("poi-zoom-in")
                Divider().frame(width:22)
                Button{changeCamera(.zoomOut)}label:{Image(systemName:"minus").frame(width:44,height:44)}.accessibilityLabel(store.language.text("縮小地圖")).accessibilityIdentifier("poi-zoom-out")
            }.background(.white,in:RoundedRectangle(cornerRadius:16))
            Button{changeCamera(.fit)}label:{Image(systemName:"arrow.up.left.and.arrow.down.right").frame(width:44,height:44).background(.white,in:Circle())}.accessibilityLabel(store.language.text("顯示搜尋範圍")).accessibilityIdentifier("poi-fit-results")
        }.font(.subheadline.bold()).shadow(color:.black.opacity(0.08),radius:6,y:2)
    }
    private func results(height:CGFloat)->some View {
        VStack(spacing:0){
            Button{setDrawer(drawer.toggled(resultCount:visiblePlaces.count))}label:{VStack(spacing:9){Capsule().fill(Color.secondary.opacity(0.3)).frame(width:34,height:4);HStack{Text(uiText(model.busy ? "Finding places…":"Explore nearby")).font(.system(.headline,design:.rounded));Text(String(visiblePlaces.count)).font(.caption).foregroundStyle(Palette.muted);Spacer();Image(systemName:drawer == .expanded || (visiblePlaces.count==1 && drawer == .preview) ? "chevron.down":"chevron.up").font(.caption.bold())}}.padding(.horizontal,20).padding(.top,10).padding(.bottom,12)}
                .buttonStyle(.plain).accessibilityIdentifier("poi-drawer-toggle").accessibilityLabel(uiText(drawer == .expanded || (visiblePlaces.count==1 && drawer == .preview) ? "收起地点列表":"展开地点列表"))
                .gesture(DragGesture(minimumDistance:15).onEnded{value in if abs(value.translation.height)>20{setDrawer(visiblePlaces.count==1 && value.translation.height<0 ? .preview:drawer.dragged(up:value.translation.height<0))}})
            if drawer != .collapsed {
                if model.busy{ProgressView().padding(12)}
                if let error=model.error{ScrollView{VStack(spacing:10){Text(uiText("暂时没找到地点资料")).font(.headline);Text(uiText(error)).font(.caption).foregroundStyle(Palette.muted);Button(uiText("重新搜索")){search()}}.padding(20)}}
                else if !model.busy && visiblePlaces.isEmpty{VStack(spacing:10){Image(systemName:"magnifyingglass").font(.title2);Text(uiText("No places here yet")).font(.headline);Text(uiText(model.category=="drinking_water" && model.provider == .apple && model.dataSource == .direct ? "Apple Maps doesn’t list drinking-water points. Switch to OpenStreetMap in Filters.":"Try a name, another category, or search a different area.")).font(.caption).foregroundStyle(Palette.muted)}.frame(maxWidth:.infinity,maxHeight:.infinity).padding(20)}
                else {
                    ScrollViewReader{proxy in ScrollView{LazyVStack(spacing:0){
                        if model.truncated{Text(uiText("Showing a selection. Search a name or narrow the category.")).font(.caption).foregroundStyle(Palette.muted).padding(.horizontal,20).padding(.bottom,6).accessibilityIdentifier("poi-search-summary")}
                        ForEach(orderedPlaces){point in placeRow(point).id(point.id);Divider().padding(.leading,68)}
                    }}.scrollDismissesKeyboard(.interactively).onChange(of:model.selected){_,id in if let id{proxy.scrollTo(id,anchor:.top)}}}
                }
            }
            Spacer(minLength:0)
            Button{creditsOpen=true}label:{HStack(spacing:8){Label(uiText("Data credits"),systemImage:"info.circle").font(.caption);Spacer(minLength:8);Text(verbatim:compactCredit).font(.caption2).multilineTextAlignment(.trailing)}.foregroundStyle(Palette.muted).frame(maxWidth:.infinity,minHeight:44).padding(.horizontal,18)}.buttonStyle(.plain).accessibilityIdentifier("poi-data-credits")
        }.frame(height:height).frame(maxWidth:.infinity).background(Palette.cream,in:UnevenRoundedRectangle(topLeadingRadius:24,topTrailingRadius:24)).shadow(color:.black.opacity(0.08),radius:12,y:-3)
    }
    private func placeRow(_ point:ScheduleCandidate)->some View {
        HStack(spacing:12){
            Button{focusPlace(point);detail=point}label:{HStack(spacing:12){
                Image(systemName:point.discoverySymbol).font(.title3).foregroundStyle(blue).frame(width:40,height:44).background(blue.opacity(0.07),in:RoundedRectangle(cornerRadius:12))
                VStack(alignment:.leading,spacing:4){
                    Text(point.displayName).font(.subheadline.weight(.semibold)).foregroundStyle(ink).lineLimit(2)
                    Text(uiText(point.category)+" · "+uiText(ExplorePresentation.distance(point,from:model.searchCenter))).font(.caption2).foregroundStyle(Palette.muted).lineLimit(1)
                    if let ticket=ticketSummaries[point.id]{Text(uiText("Reference price")+" · "+ticket.label(store.language)).font(.caption2).foregroundStyle(Palette.muted).lineLimit(1)}
                    else if let price=ExplorePresentation.price(point){Text(uiText(price)).font(.caption2).foregroundStyle(Palette.muted).lineLimit(1)}
                    if point.hasSourcedRating{Text(uiText(point.mappedRatingLabel)).font(.caption2).foregroundStyle(Palette.muted)}
                }.frame(maxWidth:.infinity,alignment:.leading)
            }}.buttonStyle(.plain).accessibilityLabel(uiText("查看地点："+point.displayName)).accessibilityIdentifier("poi-detail-"+point.id)
            Button{save(point)}label:{Label(uiText(point.source=="Google Places" ? "Details":addingToDay ? (replacingPlace ? "Use":"Add"):isAdded(point) ? "Saved":"Save"),systemImage:addingToDay ? "plus":isAdded(point) ? "checkmark":"plus").font(.caption.weight(.semibold)).padding(.horizontal,10).frame(minHeight:44).background(blue.opacity(0.08),in:Capsule())}.buttonStyle(BuddyPressStyle()).accessibilityLabel(uiText("Save ")+point.displayName).accessibilityIdentifier("poi-toggle-"+point.id)
        }.padding(.horizontal,18).padding(.vertical,12).background(model.selected==point.id ? blue.opacity(0.055):.clear)
    }
    private var filterSheet:some View {
        NavigationStack{Form{
            Section(uiText("Map")){Picker(uiText("地图来源"),selection:Binding(get:{searchProvider},set:{if addingToDay{localProvider=$0}else{store.mapProvider=$0}})){ForEach(store.availableMapProviders){Text(uiText($0.label)).tag($0)}}.accessibilityIdentifier("poi-map-provider")}
            Section(uiText("Explore")){Picker(uiText("Category"),selection:Binding(get:{PoiDiscoveryCategory.parent(model.category)},set:{model.category=$0})){ForEach(PoiDiscoveryCategory.groups,id:\.0){id,label in Text(uiText(label)).tag(id)}}
                if model.category=="all",!(store.personalProfile.interests ?? []).isEmpty{Text(uiText("For you puts places matching your saved interests first, then sorts by distance from the map centre.")).font(.caption)}
            }
            Section(uiText("Travel context")){
                Toggle(uiText("Show traffic and cost markers"),isOn:$showMapInsights).accessibilityIdentifier("poi-insights-toggle")
                Button{requestInsights=true;filtersOpen=false}label:{Label(uiText("交通・人潮・費用"),systemImage:"info.circle")}.accessibilityIdentifier("travel-insights-open")
            }
            Section{Button(uiText("Places & data sources")){requestCredits=true;filtersOpen=false}.accessibilityIdentifier("poi-filter-credits")}
        }.navigationTitle(uiText("Filters")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("Done")){filtersOpen=false}}}.buddySurface().presentationDetents([.medium,.large])
    }
    private var compactCredit:String {
        if model.attribution.contains("OpenStreetMap") || searchProvider == .osm{return model.attribution.contains("Overture") ? "© OpenStreetMap · Overture":"© OpenStreetMap"}
        return searchProvider == .google ? "Google Maps":searchProvider == .apple ? "Apple Maps":"Overture"
    }
    private var creditsSheet:some View {
        NavigationStack{ScrollView{VStack(alignment:.leading,spacing:16){
            Text(uiText("Place data: ")+model.source).font(.headline)
            Text(model.attribution).font(.caption)
            if let version=model.dataVersion{Text(uiText("Data version: ")+version).font(.caption)}
            if model.attribution.contains("OpenStreetMap") || searchProvider == .osm{Link("© OpenStreetMap contributors · ODbL",destination:URL(string:"https://www.openstreetmap.org/copyright")!)}
            if model.attribution.contains("Overture"){Link("Overture Maps attribution",destination:URL(string:"https://docs.overturemaps.org/attribution/")!)}
            if model.attribution.contains("Wikivoyage"){Link("Wikivoyage · CC BY-SA 4.0",destination:URL(string:"https://creativecommons.org/licenses/by-sa/4.0/")!)}
            Text(uiText("Prices marked Est. are planning estimates. Ratings appear only when a review source and review count are available. Distance is measured from the last search centre, not a verified walking route.")).font(.subheadline)
            if let summary=model.searchSummary{DisclosureGroup(uiText("Original provider note")){Text(verbatim:summary).font(.caption)}}
        }.padding(24)}.navigationTitle(uiText("Data credits")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("Done")){creditsOpen=false}}}.buddySurface()
    }
    private var savedSheet:some View {
        NavigationStack{List{
            if toolbox.places.isEmpty{Text(uiText("Save a place here, then add it to a trip whenever you’re ready.")).foregroundStyle(Palette.muted)}
            ForEach(toolbox.places){point in Button{savedFocus=point;savedOpen=false}label:{VStack(alignment:.leading,spacing:5){Text(point.displayName).font(.headline);Text(uiText(point.category)).font(.caption).foregroundStyle(Palette.muted)}}.swipeActions{Button(uiText("Remove"),role:.destructive){toolbox.remove(point.id)}}}
        }.navigationTitle(uiText("Saved places")).toolbar{Button(uiText("Done")){savedOpen=false}}}.buddySurface()
    }
    @ViewBuilder private func map(bottomInset:CGFloat)->some View {
        switch searchProvider {
        case .apple,.osm:PoiNativeMap(places:mapPlaces,osm:searchProvider == .osm,center:model.center,zoom:zoom,onCamera:cameraChanged,onSelect:{id in if let point=visiblePlaces.first(where:{$0.id==id}){focusPlace(point)}},insightMarkers:insightMarkers,focus:focus,cameraAction:cameraAction,bottomInset:bottomInset,onFeature:{point in model.stop();if !model.places.contains(where:{$0.id==point.id}){model.places.append(point)};focusPlace(point);detail=point},onFeatureStatus:{loading,error in featureLoading=loading;featureError=error}).accessibilityIdentifier("poi-map-"+searchProvider.rawValue)
        case .google:
            if SydneyBuddyApp.mapReady{PoiGoogleMap(places:mapPlaces,center:model.center,zoom:zoom,onCamera:cameraChanged,onSelect:{id in if let point=visiblePlaces.first(where:{$0.id==id}){focusPlace(point)}},insightMarkers:insightMarkers,focus:focus,cameraAction:cameraAction,bottomInset:bottomInset)}
            else{ContentUnavailableView(uiText("地图尚未配置"),systemImage:"map",description:Text(uiText("请切换到 Apple Maps 或 OpenStreetMap。")))}
        case .schematic:ContentUnavailableView(uiText("请选择在线底图"),systemImage:"map",description:Text(uiText("示意图不支持地点搜索。")))
        }
    }
    private func updateSearchLanguage(_ language:AppLanguage){
        switch language{case .english:model.language="en";case .simplified:model.language="zh-CN";case .traditional:model.language="zh-TW"}
    }
    private func configure(){detail=nil;focus=nil;areaChanged=false;cameraAction=nil;model.configure(searchProvider,googleMapConfigured:SydneyBuddyApp.mapConfigured,dataSource:store.travelDataSource)}
    private func cameraChanged(_ point:CLLocationCoordinate2D,_ newZoom:Double){
        let moved=CLLocation(latitude:model.center.latitude,longitude:model.center.longitude).distance(from:CLLocation(latitude:point.latitude,longitude:point.longitude))>60 || abs(zoom-newZoom)>0.35
        zoom=newZoom
        guard moved else{return}
        model.cameraChanged(point,zoom:newZoom,automaticallySearch:false);areaChanged=true;searching=false;setDrawer(.collapsed)
    }
    private func changeCamera(_ kind:RouteMapCameraAction.Kind){searching=false;focus=nil;model.selected=nil;cameraAction=RouteMapCameraAction(kind:kind);setDrawer(.collapsed)}
    private func focusPlace(_ point:ScheduleCandidate){searching=false;model.stop();model.center = .init(latitude:point.lat,longitude:point.lng);model.selected=point.id;setDrawer(.preview);focus=PoiMapFocus(point:point)}
    private func search(){searching=false;areaChanged=false;focus=nil;model.search();setDrawer(.preview)}
    private func drawerHeight(_ available:CGFloat)->CGFloat {
        if drawer == .preview,!model.busy,model.error==nil,visiblePlaces.count==1{return min(180,available*0.35)}
        return drawer.height(in:available,header:headerHeight)
    }
    private func setDrawer(_ value:ExploreDrawer){withAnimation(reduceMotion ? nil:.easeInOut(duration:0.22)){drawer=value}}
    private func isAdded(_ point:ScheduleCandidate)->Bool {toolbox.places.contains{$0.id==point.id}}
    private func save(_ point:ScheduleCandidate){
        if point.source=="Google Places"{detail=point;return}
        if addingToDay{if !replacingPlace{toolbox.add(point)};onAdd(point)}else{savingPlace=point}
    }
    private func acknowledge(_ message:String){receipt=uiText(message);UIImpactFeedbackGenerator(style:.soft).impactOccurred();UIAccessibility.post(notification:.announcement,argument:receipt)}
    private func loadTicketSummaries() async {
        ticketSummaries=[:];let points=Array(visiblePlaces.prefix(80));let source=store.travelDataSource;let provider=searchProvider
        guard !points.isEmpty else{return}
        do{
            let raw=try await SocialImportModel().call("activities/summaries",body:["places":points.map{["id":$0.id,"name":$0.name,"lat":$0.lat,"lng":$0.lng] as [String:Any]}],store:store)
            try Task.checkCancellation();guard source==store.travelDataSource,provider==searchProvider,raw["dataSource"] as? String==source.rawValue else{return}
            ticketSummaries=try JSONDecoder().decode([String:TicketPriceSummary].self,from:JSONSerialization.data(withJSONObject:raw["summaries"] ?? [:]))
        }catch{}
    }
}

@MainActor private struct ExploreSavePlaceSheet:View {
    let candidate:ScheduleCandidate
    var onSaved:(String)->Void
    @EnvironmentObject var store:AppStore
    @ObservedObject private var toolbox=PoiToolbox.shared
    @Environment(\.dismiss) private var dismiss
    var body:some View {
        NavigationStack{List{
            Section{Text(candidate.displayName).font(.title3.bold());Button{toolbox.add(candidate);onSaved("Saved for later. We can make room for it when you’re ready.")}label:{Label(uiText(toolbox.places.contains(where:{$0.id==candidate.id}) ? "Keep in saved places":"Save for later"),systemImage:"bookmark")}.accessibilityIdentifier("poi-save-later")}
            Section(uiText("Add to a trip")){
                if candidate.category=="住宿"{Text(uiText("Save this stay for later, then choose it in your trip’s Preferences & stays. It won’t be added as a sightseeing stop.")).font(.subheadline).foregroundStyle(Palette.muted)}
                else if store.saved.isEmpty{Text(uiText("No trips yet. Save it for later, then plan a trip from Trips.")).foregroundStyle(Palette.muted)}
                ForEach(TripHomePresentation.orderedTrips(store.saved,today:TripProfile.dateString(Date())).filter{!$0.days.isEmpty && candidate.category != "住宿"}){trip in
                    NavigationLink{ExploreAddToTripView(candidate:candidate,original:trip,onSaved:onSaved).environmentObject(store)}label:{VStack(alignment:.leading,spacing:4){Text(TripHomePresentation.title(trip,language:store.language));Text(trip.profile.date).font(.caption).foregroundStyle(Palette.muted)}}.accessibilityIdentifier("poi-save-trip-"+(trip.id ?? ""))
                }
            }
            if toolbox.places.contains(where:{$0.id==candidate.id}){Section{Button(uiText("Remove from saved places"),role:.destructive){toolbox.remove(candidate.id);dismiss()}}}
        }.navigationTitle(uiText("Save this place")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("Cancel")){dismiss()}}}.tint(Palette.primary)
    }
}

@MainActor private struct ExploreAddToTripView:View {
    let candidate:ScheduleCandidate
    let original:Trip
    var onSaved:(String)->Void
    @EnvironmentObject var store:AppStore
    @StateObject private var editor=ScheduleEditorModel()
    @State private var selectedDay=0
    @State private var checked=false
    @State private var saving=false
    @State private var saveError:String?
    private var alreadyAdded:Bool {(original.schedule ?? ScheduleState.preserving(original)).days.flatMap(\.activities).contains{$0.placeId==candidate.id}}
    private var canSave:Bool {checked && !editor.busy && !editor.dirty && !saving && editor.error==nil && editor.state?.issues.contains(where:{$0.blocking})==false && editor.state?.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id})==true}
    var body:some View {
        ScrollView{VStack(alignment:.leading,spacing:18){
            Text(candidate.displayName).font(.title2.bold())
            Text(TripHomePresentation.title(original,language:store.language)).font(.subheadline).foregroundStyle(Palette.muted)
            if alreadyAdded{Label(uiText("This place is already in this trip."),systemImage:"checkmark.circle")}
            else{
                Picker(uiText("Choose a day"),selection:$selectedDay){ForEach(original.days.indices,id:\.self){index in Text("Day \(index+1) · \(original.days[index].date)").tag(index)}}.pickerStyle(.menu).accessibilityIdentifier("poi-save-day")
                Text(uiText("We’ll check travel time and how it fits before changing your trip.")).font(.subheadline).foregroundStyle(Palette.muted)
                if editor.busy{ProgressView(uiText("Checking the route and timing…"))}
                if let state=editor.state,checked,!editor.busy,state.days.indices.contains(selectedDay){
                    let day=state.days[selectedDay]
                    Text("\(day.activities.count) stops · About \(day.estimatedSteps) steps").font(.headline)
                    ForEach(day.activities){activity in HStack{Text(activity.arrival).font(.caption.monospacedDigit());Text(state.candidates.first(where:{$0.id==activity.placeId})?.displayName ?? uiText(activity.name)).font(.subheadline)}.foregroundStyle(activity.placeId==candidate.id ? Color.blue:Color.primary)}
                    let checks=ExplorePresentation.checks(state,day:selectedDay)
                    if checks.contains(where:{$0.blocking}){Text(uiText("This doesn’t fit yet. Try another day, or keep it saved for later. Your original trip is unchanged.")).font(.subheadline.weight(.semibold))}
                    ForEach(checks.filter(\.blocking)){check in Label(uiText(check.text),systemImage:"exclamationmark.triangle").font(.caption).foregroundStyle(.red)}
                    if checks.contains(where:{!$0.blocking}){DisclosureGroup(uiText("Things to keep in mind")){ForEach(checks.filter{!$0.blocking}){check in Text(uiText(check.text)).font(.caption).padding(.vertical,3)}}}
                    DisclosureGroup(uiText("Original calculation notes")){ForEach(state.issues.filter{$0.day==selectedDay || $0.day == -1}){issue in Text(verbatim:issue.message).font(.caption)}}
                    Text(uiText("Prices and opening hours may still need checking.")).font(.caption).foregroundStyle(Palette.muted)
                }
                if let error=editor.error ?? saveError{Text(uiText("Couldn’t complete this change. Your original trip is unchanged. Try again, or refresh Trips before retrying a save.")).font(.caption).foregroundStyle(.red);DisclosureGroup(uiText("Technical details")){Text(verbatim:error).font(.caption)}}
                Button(uiText(checked ? "Check again":"Check fit")){check()}.buttonStyle(BuddySecondaryStyle()).disabled(editor.busy || saving).accessibilityIdentifier("poi-check-fit")
                Button{save()}label:{Text(uiText(saving ? "Saving…":"Confirm and add")).font(.headline).frame(maxWidth:.infinity,minHeight:50)}.buttonStyle(KokoPrimaryStyle()).disabled(!canSave).accessibilityIdentifier("poi-confirm-add")
                if checked,!editor.busy,!saving,!canSave{Button(uiText("Keep saved for later")){PoiToolbox.shared.add(candidate);onSaved("Saved for later. We can find a better fit together.")}.accessibilityIdentifier("poi-save-instead")}
            }
        }.padding(24)}.disabled(saving).navigationTitle(uiText("Add to trip")).navigationBarTitleDisplayMode(.inline)
        .interactiveDismissDisabled(saving).navigationBarBackButtonHidden(saving)
        .task{editor.loader=store.loader;editor.inlineEditing=true;editor.prepare(original);selectedDay=TripHomePresentation.dayIndex(original,today:TripProfile.dateString(Date()))}
        .onChange(of:selectedDay){_,_ in editor.acceptSaved(original);checked=false;saveError=nil}
        .onDisappear{editor.stop()}
    }
    private func check(){editor.acceptSaved(original);editor.dayIndex=selectedDay;checked=true;saveError=nil;editor.edit{state in ExplorePresentation.insert(candidate,into:&state,day:selectedDay)}}
    private func save(){
        guard canSave,let value=editor.trip else{return};saving=true;saveError=nil
        Task{let ok=await store.saveAdjustment(value,original:original,dayIndex:selectedDay,inline:true);saving=false
            if ok{store.koalaFeedback=nil;PoiToolbox.shared.add(candidate);onSaved("Added to "+TripHomePresentation.title(original,language:store.language)+".")}
            else{saveError=store.adjustmentError ?? "Couldn’t save this change. Your original trip is unchanged."}
        }
    }
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
    var title:String? {point.displayName}
    init(_ p:ScheduleCandidate){point=p}
}
struct PoiNativeMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var osm:Bool
    var center:CLLocationCoordinate2D;var zoom:Double
    var onCamera:(CLLocationCoordinate2D,Double)->Void;var onSelect:(String)->Void
    var insightMarkers:[InsightMarker]=[]
    var focus:PoiMapFocus?=nil
    var cameraAction:RouteMapCameraAction?=nil
    var bottomInset:CGFloat=0
    var onFeature:(ScheduleCandidate)->Void={_ in}
    var onFeatureStatus:(Bool,String?)->Void={_,_ in}
    func makeCoordinator()->Coordinator{Coordinator(self)}
    static func dismantleUIView(_ map:MKMapView,coordinator:Coordinator){coordinator.featureLookup.cancel();map.delegate=nil}
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:PoiNativeMap;var key="";var focusToken:UUID?;var lastCameraAction:UUID?;var focusing=false;let featureLookup=AppleFeatureLookup()
        init(_ p:PoiNativeMap){parent=p}
        func mapView(_ map:MKMapView,rendererFor overlay:MKOverlay)->MKOverlayRenderer{if let tiles=overlay as? MKTileOverlay{return MKTileOverlayRenderer(tileOverlay:tiles)};return MKOverlayRenderer(overlay:overlay)}
        func mapView(_ map:MKMapView,regionDidChangeAnimated animated:Bool){if focusing{focusing=false;return};parent.onCamera(map.centerCoordinate,log2(360/max(0.00001,map.region.span.longitudeDelta)))}
        func mapView(_ map:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            if let pin=annotation as? InsightMapPin {let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);view.markerTintColor=pin.kind == "price" ? .systemGreen:.systemOrange;view.glyphText=pin.kind == "price" ? "$":"!";view.canShowCallout=true;return view}
            if let cluster=annotation as? MKClusterAnnotation {
                let view=MKMarkerAnnotationView(annotation:cluster,reuseIdentifier:nil);view.markerTintColor = .systemBlue;view.glyphText=String(cluster.memberAnnotations.count);view.displayPriority = .defaultHigh;view.titleVisibility = .hidden;view.subtitleVisibility = .hidden;view.canShowCallout=false;return view
            }
            guard let pin=annotation as? PoiPin else{return nil}
            let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);let selected=pin.point.id==parent.focus?.point.id
            view.markerTintColor=selected ? .systemBlue:UIColor(red:0.34,green:0.47,blue:0.62,alpha:1)
            view.titleVisibility = selected ? .visible:.hidden;view.subtitleVisibility = .hidden;view.collisionMode = .circle
            view.clusteringIdentifier=selected ? nil:"explore-places";view.displayPriority=selected ? .required:.defaultLow;view.zPriority=selected ? .max:.defaultUnselected
            view.transform=selected ? CGAffineTransform(scaleX:1.15,y:1.15):.identity;view.glyphImage=UIImage(systemName:pin.point.discoverySymbol);view.canShowCallout=true;return view
        }
        func mapView(_ map:MKMapView,didSelect view:MKAnnotationView){
            if let cluster=view.annotation as? MKClusterAnnotation {
                map.deselectAnnotation(cluster,animated:false);map.setVisibleMapRect(routeCameraBounds(cluster.memberAnnotations.map(\.coordinate)),edgePadding:UIEdgeInsets(top:150,left:50,bottom:180,right:50),animated:!UIAccessibility.isReduceMotionEnabled);return
            }
            if let pin=view.annotation as? PoiPin,pin.point.id != parent.focus?.point.id{featureLookup.cancel();parent.onFeatureStatus(false,nil);parent.onSelect(pin.point.id)}}
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
    func makeUIView(context:Context)->MKMapView{let map=MKMapView();map.delegate=context.coordinator;map.showsScale=true;map.isPitchEnabled=false;map.preferredConfiguration=MKStandardMapConfiguration(elevationStyle:.flat,emphasisStyle:.muted);map.selectableMapFeatures=osm ? []:[.pointsOfInterest];if osm{map.addOverlay(OsmTiles(),level:.aboveLabels)};let delta=360/pow(2,zoom);map.setRegion(MKCoordinateRegion(center:center,span:MKCoordinateSpan(latitudeDelta:delta,longitudeDelta:delta)),animated:false);return map}
    func applyCameraAction(_ map:MKMapView,coordinator:Coordinator,animated:Bool) {
        guard let action=cameraAction,coordinator.lastCameraAction != action.id else{return}
        coordinator.lastCameraAction=action.id;coordinator.focusing=false
        switch action.kind {
        case .zoomIn,.zoomOut:
            let factor=action.kind == .zoomIn ? 0.5:2.0
            let span=map.region.span
            map.setRegion(MKCoordinateRegion(center:map.centerCoordinate,span:MKCoordinateSpan(latitudeDelta:min(160,max(0.0003,span.latitudeDelta*factor)),longitudeDelta:min(180,max(0.0003,span.longitudeDelta*factor)))),animated:animated)
        case .fit:
            let points=places.isEmpty ? [center]:places.map{CLLocationCoordinate2D(latitude:$0.lat,longitude:$0.lng)}
            map.setVisibleMapRect(routeCameraBounds(points),edgePadding:UIEdgeInsets(top:min(220,map.bounds.height*0.4),left:30,bottom:70,right:76),animated:animated)
        }
    }
    func updateUIView(_ map:MKMapView,context:Context){
        if context.coordinator.parent.osm != osm{context.coordinator.featureLookup.cancel();onFeatureStatus(false,nil)}
        context.coordinator.parent=self
        let margins=UIEdgeInsets(top:120,left:12,bottom:bottomInset+12,right:12)
        if map.layoutMargins != margins{map.layoutMargins=margins}
        applyCameraAction(map,coordinator:context.coordinator,animated:!UIAccessibility.isReduceMotionEnabled)
        map.selectableMapFeatures=osm ? []:[.pointsOfInterest]
        let tiles=map.overlays.filter{$0 is OsmTiles}
        if osm && tiles.isEmpty{map.addOverlay(OsmTiles(),level:.aboveLabels)}
        if !osm{map.removeOverlays(tiles)}
        let key=AppLanguage.current.rawValue+places.map{$0.id+$0.displayName}.joined(separator:",")+insightMarkers.map{$0.id+$0.title}.joined()+(focus?.point.id ?? "")
        if context.coordinator.key != key {
            context.coordinator.key=key;map.removeAnnotations(map.annotations);map.addAnnotations(places.map(PoiPin.init))
            for item in insightMarkers{let pin=InsightMapPin();pin.kind=item.kind;pin.coordinate = .init(latitude:item.lat,longitude:item.lng);pin.title=uiText(item.title);pin.subtitle=uiText(item.summary);map.addAnnotation(pin)}
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
    var cameraAction:RouteMapCameraAction?=nil
    var bottomInset:CGFloat=0
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,GMSMapViewDelegate {
        var parent:PoiGoogleMap;var key="";var focusToken:UUID?;var lastCameraAction:UUID?;var focusing=false;var markers:[String:GMSMarker]=[:]
        init(_ parent:PoiGoogleMap){self.parent=parent}
        func mapView(_ map:GMSMapView,willMove gesture:Bool){if gesture{focusing=false}}
        func mapView(_ map:GMSMapView,idleAt camera:GMSCameraPosition){if focusing{focusing=false;return};parent.onCamera(camera.target,Double(camera.zoom))}
        func mapView(_ map:GMSMapView,didTap marker:GMSMarker)->Bool{
            if let points=marker.userData as? [ScheduleCandidate]{
                let rect=routeCameraBounds(points.map{CLLocationCoordinate2D(latitude:$0.lat,longitude:$0.lng)})
                let bounds=GMSCoordinateBounds(coordinate:MKMapPoint(x:rect.minX,y:rect.minY).coordinate,coordinate:MKMapPoint(x:rect.maxX,y:rect.maxY).coordinate)
                map.animate(with:GMSCameraUpdate.fit(bounds,with:UIEdgeInsets(top:150,left:40,bottom:180,right:40)))
            }else if let id=marker.userData as? String{parent.onSelect(id)};return true
        }
    }
    func makeUIView(context:Context)->GMSMapView {
        let options=GMSMapViewOptions();options.camera=GMSCameraPosition(target:center,zoom:Float(zoom))
        let map=GMSMapView(options:options);map.delegate=context.coordinator;return map
    }
    func updateUIView(_ map:GMSMapView,context:Context){
        context.coordinator.parent=self
        let margins=UIEdgeInsets(top:120,left:12,bottom:bottomInset+12,right:12)
        if !context.coordinator.focusing && map.padding != margins{map.padding=margins}
        if let action=cameraAction,context.coordinator.lastCameraAction != action.id {
            context.coordinator.lastCameraAction=action.id;context.coordinator.focusing=false
            map.selectedMarker=nil;map.padding = margins
            switch action.kind {
            case .zoomIn:map.animate(toZoom:min(20,map.camera.zoom+1))
            case .zoomOut:map.animate(toZoom:max(3,map.camera.zoom-1))
            case .fit:
                let points=places.isEmpty ? [center]:places.map{CLLocationCoordinate2D(latitude:$0.lat,longitude:$0.lng)}
                let rect=routeCameraBounds(points)
                let bounds=GMSCoordinateBounds(coordinate:MKMapPoint(x:rect.minX,y:rect.minY).coordinate,coordinate:MKMapPoint(x:rect.maxX,y:rect.maxY).coordinate)
                map.animate(with:GMSCameraUpdate.fit(bounds,with:UIEdgeInsets(top:min(220,map.bounds.height*0.4),left:30,bottom:70,right:76)))
            }
        }
        let key=AppLanguage.current.rawValue+places.map{$0.id+$0.displayName}.joined(separator:",")+insightMarkers.map{$0.id+$0.title}.joined()+(focus?.point.id ?? "")+String(format:"%.1f",map.camera.zoom)
        if key != context.coordinator.key {context.coordinator.key=key;map.clear();context.coordinator.markers=[:]
        for item in insightMarkers{let pin=GMSMarker(position:.init(latitude:item.lat,longitude:item.lng));pin.title=uiText(item.title);pin.snippet=uiText(item.summary);pin.icon=GMSMarker.markerImage(with:item.kind == "price" ? .systemGreen:.systemOrange);pin.map=map}
        for group in ExplorePresentation.clusters(places,zoom:Double(map.camera.zoom),selected:focus?.point.id){
            guard let point=group.first else{continue}
            let marker=GMSMarker(position:.init(latitude:group.map(\.lat).reduce(0,+)/Double(group.count),longitude:group.map(\.lng).reduce(0,+)/Double(group.count)))
            if group.count>1{
                marker.title="\(group.count) places";marker.userData=group
                let label=UILabel(frame:CGRect(x:0,y:0,width:36,height:36));label.text=String(group.count);label.textAlignment = .center;label.font = .boldSystemFont(ofSize:14);label.textColor = .white;label.backgroundColor = .systemBlue;label.layer.cornerRadius=18;label.clipsToBounds=true;marker.iconView=label
            }else{marker.title=point.displayName;marker.userData=point.id;marker.icon=GMSMarker.markerImage(with:point.id==focus?.point.id ? .systemBlue:UIColor(red:0.34,green:0.47,blue:0.62,alpha:1));context.coordinator.markers[point.id]=marker}
            marker.zIndex=point.id==focus?.point.id ? 1000:0;marker.map=map
        }
        }
        if let focus,context.coordinator.focusToken != focus.token {
            context.coordinator.focusToken=focus.token;context.coordinator.focusing=true
            map.padding=UIEdgeInsets(top:map.bounds.height*0.30,left:24,bottom:max(bottomInset+12,map.bounds.height*0.36),right:24)
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
    @Environment(\.locale) private var interfaceLocale

    var candidate:ScheduleCandidate
    @StateObject private var media=PlaceMediaModel()
    @State private var scene:MKLookAroundScene?
    @State private var panoramaLoading=false
    @State private var panoramaMessage:String?
    @EnvironmentObject var store:AppStore
    var body:some View {
        let _=interfaceLocale
        VStack(alignment:.leading,spacing:12){
            Text(uiText("照片與街景")).font(.headline)
            Text(uiText(candidate.hiddenGem == true ? "社群照片保留作者與授權；附近照片不代表已確認拍攝此景點。":"附近 100 公尺的實景照片，並非已核實的商家或店內照片。")).font(.caption).foregroundStyle(Palette.muted)
            Button(uiText(media.photos.isEmpty ? "載入附近照片":"重新載入照片")){Task{await media.load(candidate,dataSource:store.travelDataSource)}}.disabled(media.loading).accessibilityIdentifier("place-media-retry")
            if media.loading{ProgressView("正在載入照片…").accessibilityIdentifier("place-media-loading")}
            if let message=media.message{Text(uiText(message)).font(.caption).accessibilityIdentifier("place-media-status")}
            ScrollView(.horizontal){HStack(alignment:.top,spacing:12){ForEach(media.photos,content:photoCard)}}
            if store.mapProvider != .google {
                Button(uiText("預覽街景（Apple Look Around）")){Task{
                    panoramaLoading=true;panoramaMessage=nil
                    do{scene=try await MKLookAroundSceneRequest(coordinate:CLLocationCoordinate2D(latitude:candidate.lat,longitude:candidate.lng)).scene
                        if scene==nil{panoramaMessage="此位置尚無可用街景。"}
                    }catch{panoramaMessage="街景暫時無法載入，請稍後重試。"}
                    panoramaLoading=false
                }}.disabled(panoramaLoading)
                if panoramaLoading{ProgressView()}
                if let scene{LookAroundPreview(initialScene:scene).frame(height:210).clipShape(RoundedRectangle(cornerRadius:12))}
                if let panoramaMessage{Text(uiText(panoramaMessage)).font(.caption)}
            }
            Link(uiText("開啟附近街景（外部地圖）"),destination:URL(string:"https://www.mapillary.com/app/?lat=\(candidate.lat)&lng=\(candidate.lng)&z=17")!).font(.caption)
            Text(uiText("街景覆蓋由服務提供者決定；未必有此商家的全景。")).font(.caption).foregroundStyle(Palette.muted)
        }.task(id:candidate.id+"|"+store.travelDataSource.rawValue){
            scene=nil;panoramaMessage=nil;media.loader=store.loader
            await media.prepare(candidate,dataSource:store.travelDataSource)
        }
    }
    @ViewBuilder private func photoCard(_ photo:PlacePhoto)->some View {
        let credit="\(photo.artist) · \(photo.license)"
        VStack(alignment:.leading,spacing:5){
            AsyncImage(url:URL(string:photo.url)){phase in
                switch phase{case .success(let image):image.resizable().scaledToFill();case .failure:Text(uiText("照片無法載入"));default:ProgressView()}
            }.frame(width:260,height:170).clipped().clipShape(RoundedRectangle(cornerRadius:12))
            Text(uiText(photo.title)).font(.caption).lineLimit(2)
            Text(uiText(credit)).font(.caption2).lineLimit(3)
            if photo.matchKind == "exact-place" || photo.matchKind == "guide-file" {Text(uiText("✓ 已配對景點實景照片")).font(.caption2).foregroundStyle(Palette.green)}
            else if photo.matchKind == "nearby-unverified" {Text(uiText("附近照片，景點配對未確認")).font(.caption2)}
            if let url=URL(string:photo.pageUrl){Link(uiText("照片來源與授權"),destination:url).font(.caption)}
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
    static func sourceInterest(_ kind:String?)->String? {
        let kind=(kind ?? "").lowercased()
        if ["art_gallery","gallery","art_museum","contemporary_art_museum","artwork"].contains(kind){return "艺术"}
        if kind=="museum" || kind.hasSuffix("_museum"){return "文化"}
        if ["park","garden","botanical_garden","national_park","state_park","city_park","nature_reserve","beach","waterfall","forest"].contains(kind){return "自然"}
        if ["historic_site","memorial","monument","archaeological_site","heritage_site"].contains(kind){return "文化"}
        return nil
    }
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
                    if ["風景","风景","sights"].contains(p.category),let interest=sourceInterest(kind){p.category=interest}
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
