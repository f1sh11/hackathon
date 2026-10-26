import Foundation
import Security
import CoreLocation
import FerrostarCore

enum TravelDataSource:String,CaseIterable,Identifiable {
    case direct,zetaris
    case zetarisCloud="zetaris-cloud"
    var id:String {rawValue}
    var label:String {switch self{case .direct:return "直接來源";case .zetaris:return "本機 Zetaris";case .zetarisCloud:return "雲端 Zetaris（雪梨）"}}
    var warehouse:Bool {self != .direct}
    static var current:Self {Self(rawValue:UserDefaults.standard.string(forKey:"travelDataSource") ?? "") ?? .direct}
    func apply(to request:inout URLRequest){request.setValue(rawValue,forHTTPHeaderField:"X-Travel-Data-Source")}
}
struct TravelMapLayer:Decodable,Identifiable {var id:String;var name:String}
struct TravelMapConfiguration:Decodable {var dataSource:String;var layers:[TravelMapLayer]}
struct TravelDataStatus:Decodable {var dataSource:String;var status:String;var catalogCount:Int}

struct TravelInsights:Decodable {
    var dataSource:String;var fetchedAt:String;var scope:String;var traffic:InsightTraffic;var places:[InsightPlace];var attribution:String;var pricePlaces:[InsightMarker]?
}
struct InsightTraffic:Decodable {var status:String;var summary:String;var source:String;var items:[InsightMarker]}
struct InsightMarker:Decodable,Identifiable {var id:String;var lat:Double;var lng:Double;var title:String;var summary:String;var source:String;var kind:String?}
struct InsightPlace:Decodable,Identifiable {var id:String;var name:String;var price:InsightPrice;var crowd:InsightCrowd;var googleFallback:String}
struct InsightCrowd:Decodable {var status:String;var summary:String}
struct InsightPrice:Decodable {var status:String;var summary:String;var source:String;var scope:String?;var sourceUrl:String?;var range:InsightPriceRange?}
struct InsightPriceRange:Decodable {var startPrice:InsightMoney?;var endPrice:InsightMoney?}
struct InsightMoney:Decodable {var currencyCode:String?;var units:String?;var nanos:Int?;var label:String {"\(currencyCode ?? "") \(units ?? "0")"}}

struct Coordinate: Codable, Hashable {
    var name: String
    var lat: Double
    var lng: Double
    var location: CLLocationCoordinate2D { .init(latitude: lat, longitude: lng) }
    static let quay = Coordinate(name: "环形码头", lat: -33.8611, lng: 151.2126)
    static let central = Coordinate(name: "中央车站", lat: -33.8830, lng: 151.2065)
    static let airport = Coordinate(name: "悉尼机场 · 已完成入境取行李", lat: -33.9357, lng: 151.1660)
}
struct TripProfile: Codable {
    var date: String = Self.dateString(Date())
    var days: Int = 1
    var budget: Double = 100
    var maxSteps: Int = 10000
    var startMinute: Int = 600
    var endMinute: Int = 1080
    var interests: [String] = ["风景", "美食"]
    var budgetAmount:Double?=nil
    var budgetCurrency:String?=nil
    var budgetBasis:String?=nil
    var travelers:Int?=nil
    var origin:String?=nil
    var mustDo:String?=nil
    var avoid:String?=nil
    var energy:String? = nil
    var rhythm:String? = nil
    var queueTolerance:String? = nil
    var dayStyle:String? = nil
    var mustVisit: [String] = []
    var start = Coordinate.quay
    var end = Coordinate.quay
    var request:String? = ""
    var weatherOverride: String? = nil
    var transportPreference:String? = nil
    static func dateString(_ date: Date) -> String {
        let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd"; f.timeZone = TimeZone(identifier: "Australia/Sydney"); return f.string(from: date)
    }
}
struct Stop: Codable, Identifiable {
    var id: String; var name: String; var lat: Double; var lng: Double
    var category: String; var indoor: Bool; var visitMinutes: Int; var cost: Double
    var note: String; var arrival: String; var departure: String; var hoursVerified: Bool
    var mapsURL: String; var steps: Int
    var placeId:String? = nil
    var activityType:String? = nil
    var tags:[String]? = nil
    var sourceURL:String? = nil
    var costKnown:Bool? = nil;var indoorKnown:Bool? = nil
    var costLabel:String {costKnown == false ? (cost>0 ? "每人预留 A$\(Int(cost))":"费用待确认"):"每人约 A$\(Int(cost))"}
    var indoorLabel:String {indoorKnown == false ? "室内外待确认":(indoor ? "室内":"室外")}
    var coordinate: CLLocationCoordinate2D { .init(latitude: lat, longitude: lng) }
    var symbol: String {
        switch category { case "美食": return "fork.knife"; case "自然": return "leaf.fill"; case "艺术", "文化": return "building.columns.fill"; case "城市": return "building.2.fill"; default: return "sparkles" }
    }
}
struct TransitObservation:Codable {var from:Coordinate;var to:Coordinate;var date:String;var minute:Int;var leg:RouteLeg}
struct RouteLeg: Codable {
    var meters:Int;var minutes:Int;var polyline:String;var source:String
    var mode:String? = nil;var walkingMeters:Int? = nil;var steps:[JourneyStep]? = nil
    var departureTime:String? = nil;var arrivalTime:String? = nil;var fetchedAt:String? = nil
    var label:String {mode=="transit" ? (steps?.filter{$0.mode != "walk"}.map{transportLabel($0.mode)+($0.line.map{" "+$0} ?? "")}.joined(separator:" → ") ?? "公共交通"):"步行"}
}
struct DayPlan: Codable, Identifiable {
    var date: String; var stops: [Stop]; var legs: [RouteLeg]; var estimatedCost: Double
    var estimatedSteps: Int; var finishTime: String; var warnings: [String]
    var start: Coordinate; var end: Coordinate
    var completedStops:[Stop]? = nil
    var skippedStops:[Stop]? = nil
    var planningStartMinute:Int? = nil
    var executionStarted:Bool? = nil
    var freeTime: Bool? = nil
    var hasTransit:Bool {legs.contains{$0.mode=="transit"}}
    var transportModes:[String] {legs.count==stops.count+1 ? legs.map{$0.mode ?? "walk"}:Array(repeating:"walk",count:stops.count+1)}
    var hasWalkingJourney:Bool {!stops.isEmpty || CLLocation(latitude:start.lat,longitude:start.lng).distance(from:CLLocation(latitude:end.lat,longitude:end.lng))>1}
    var id: String { date }
}
struct WeatherDay: Codable { var date: String; var summary: String; var rainy: Bool; var available: Bool }
struct WeatherInfo: Codable {
    var summary: String; var rainy: Bool; var source: String
    var status: String?; var days: [WeatherDay]?; var fetchedAt: String?
}
struct Trace: Codable, Identifiable {
    var title: String; var detail: String; var source: String; var fetchedAt: String
    var id: String { title }
}
struct Trip: Codable, Identifiable {
    var transportRoutes:[TransitObservation]? = nil
    var planningCandidates:[ScheduleCandidate]? = nil
    var recommendationStatus:String? = nil
    var walkingRoutes:[WalkingObservation]? = nil
    var schedule:ScheduleState? = nil
    var id: String?; var revision: Int?; var title: String; var mode: String
    var profile: TripProfile; var days: [DayPlan]; var weather: WeatherInfo
    var explanation: String; var warnings: [String]; var trace: [Trace]; var createdAt: String
}
struct Memory: Codable, Identifiable {
    var id: String; var summary: String; var profile: MemoryProfile
    var provenance:Provenance? = nil
    struct Provenance:Codable {var createdAt:String}
    struct MemoryProfile: Codable { var interests: [String]; var maxSteps: Int; var habits:UserTravelProfile? = nil;var travelDna:TravelPreference? = nil }
}
struct Health: Codable { var managedTravelDataSource:String?; var navigationConfigured: Bool?; var paidServicesAllowed: Bool?; var status: String; var nvidiaConfigured: Bool; var googleConfigured: Bool; var iosMapConfigured: Bool; var model: String }
struct Expense: Codable, Identifiable {
    var id = UUID(); var title: String; var cents: Int; var participants: Int
    var tripID: String? = nil
    var shares: [Int] { (0..<participants).map { cents/participants + ($0 < cents%participants ? 1 : 0) } }
}

extension Trip {
    var suggestedTitle:String {
        let theme=profile.interests.prefix(2).joined(separator:"与")
        return "悉尼 · "+(theme.isEmpty ? "自在漫游":theme)+" · \(days.count)天"
    }
}
enum TripAdjustmentMerge {
    static func preservingOtherDays(_ candidate:Trip,original:Trip,dayIndex:Int)->Trip {
        var value=candidate
        let preserved=original.schedule ?? ScheduleState.preserving(original)
        for i in original.days.indices where i != dayIndex {
            if value.days.indices.contains(i){value.days[i]=original.days[i]}
            if value.schedule?.days.indices.contains(i)==true{value.schedule?.days[i]=preserved.days[i]}
        }
        value.schedule?.totalCost=value.days.reduce(0){$0+$1.estimatedCost};value.weather=original.weather
        return value
    }
}
struct UserTravelProfile:Codable,Equatable {
    var queueTolerance:String?;var dailySteps:Int?;var energy:String?;var rhythm:String?;var interests:[String]?
    static func load(key:String="userTravelProfile")->UserTravelProfile {
        guard let data=UserDefaults.standard.data(forKey:key),let value=try? JSONDecoder().decode(Self.self,from:data) else{return .init()};return value
    }
    var summary:String {
        [queueTolerance,dailySteps.map{"每天约 \($0) 步"},energy,rhythm,interests?.joined(separator:"、")].compactMap{$0}.filter{!$0.isEmpty}.joined(separator:" · ")
    }
    var seed:[String:String] {
        var result:[String:String]=[:]
        if let interests,!interests.isEmpty{result["interests"]=interests.joined(separator:"、")}
        if let dailySteps{result["pace"]="约\(dailySteps)步"}
        if let energy{result["energy"]=energy};if let rhythm{result["rhythm"]=rhythm};if let queueTolerance{result["queueTolerance"]=queueTolerance}
        return result
    }
}
struct TripDraftSummary {
    let fields:[String:String]
    var title:String {
        let destination=TravelRequirements.known(fields["destination"]) ? fields["destination"]!:"目的地待确定"
        let days=Int(fields["days"] ?? "").flatMap{(1...15).contains($0) ? "\($0) 天":nil} ?? "天数待确定"
        return destination+" · "+days
    }
    var subtitle:String {
        let date=TravelRequirements.known(fields["date"]) ? fields["date"]!:"日期待确定"
        let companion=TravelRequirements.known(fields["companions"]) ? fields["companions"]!:(Int(fields["travelers"] ?? "").map{"\($0) 人同行"} ?? "同行人待确定")
        return date+" · "+companion
    }
}
enum PlanningPhase:String,Codable {case places,roads,routes,saving,finished
    var step:Int {switch self{case .places:return 0;case .roads,.routes:return 1;case .saving:return 2;case .finished:return 3}}
}
struct PlanningProgress:Codable {
    var phase:PlanningPhase = .places
    var detail="正在整理这次旅行的需求…"
    var completed:Int?;var total:Int?
}
struct TravelPlanningTask:Codable {
    var id=UUID().uuidString;var status="draft";var fields:[String:String];var tripID:String?;var message:String?;var personalProfile:UserTravelProfile?=nil;var assumptions:[String]?=nil
    var progress:PlanningProgress?=nil;var draft:Trip?=nil
}

@MainActor final class AppStore: ObservableObject {
    @Published var personalProfile=UserTravelProfile.load() {
        didSet{if let data=try? JSONEncoder().encode(personalProfile){UserDefaults.standard.set(data,forKey:"userTravelProfile")}}
    }
    @Published var pendingPersonalProfile=UserTravelProfile.load(key:"pendingPersonalProfile") {
        didSet{if let data=try? JSONEncoder().encode(pendingPersonalProfile){UserDefaults.standard.set(data,forKey:"pendingPersonalProfile")}}
    }
    @Published var travelTask:TravelPlanningTask? {
        didSet{if let value=travelTask,let data=try? JSONEncoder().encode(value){UserDefaults.standard.set(data,forKey:"travelPlanningTask")}else{UserDefaults.standard.removeObject(forKey:"travelPlanningTask")}}
    }
    var isGeneratingTrip:Bool {travelTask?.status=="generating"}
    var canCancelPlanning:Bool {isGeneratingTrip && travelTask?.progress?.phase != .saving}
    private var planningDataSource:TravelDataSource?
    private var planningMapProvider:MapProvider?
    @Published var preparations: [String] = UserDefaults.standard.stringArray(forKey:"preparations") ?? []
    @Published var trip: Trip? { didSet { refreshedWeather = nil } }
    @Published var proposal: Trip? {didSet{if proposal == nil{proposalIsSuggestion=false;proposalFromNewTrip=false;remainingRequest=nil}}}
    @Published var proposalIsSuggestion=false
    @Published var proposalFromNewTrip=false
    @Published var selectedStopID:String?
    @Published var lastAdjustment:Trip?
    @Published var adjustmentError:String?
    @Published var planningStage=0
    @Published var tripToOpen:Trip?
    @Published var saved: [Trip] = []
    @Published var pendingTripFields: [String:String] = UserDefaults.standard.dictionary(forKey:"pendingTripFields") as? [String:String] ?? [:] {
        didSet {UserDefaults.standard.set(pendingTripFields,forKey:"pendingTripFields")}
    }
    @Published var memories: [Memory] = []
    @Published var profile = TripProfile()
    @Published var insights:TravelInsights?
    @Published var insightsBusy=false
    @Published var insightsError:String?
    private var insightGeneration=UUID()
    func refreshInsights(places requested:[[String:Any]]?=nil) async {
        let generation=UUID();insightGeneration=generation;insightsBusy=true;insights=nil;insightsError=nil
        let selected=travelDataSource;let provider=mapProvider;let scene=mapSceneKey(currentDay)
        let stops=currentDay?.stops ?? []
        var places:[[String:Any]]=stops.prefix(28).map{["id":$0.id,"name":$0.name,"lat":$0.lat,"lng":$0.lng]}
        let start=currentDay?.start ?? profile.start
        places.insert(["id":"start","name":start.name,"lat":start.lat,"lng":start.lng],at:0)
        if let requested {places=Array(requested.prefix(30))}
        do {
            let body:[String:Any]=["places":places,"date":currentDay?.date ?? profile.date,"mapProvider":provider.rawValue]
            let response:TravelInsights=try await api("data/insights",method:"POST",body:JSONSerialization.data(withJSONObject:body))
            guard generation==insightGeneration,selected==travelDataSource,provider==mapProvider,scene==mapSceneKey(currentDay) else{return}
            insights=response
        }catch{if generation==insightGeneration{insightsError="交通、人潮與費用暫時無法更新，請重試。"}}
        if generation==insightGeneration{insightsBusy=false}
    }
    @Published var weatherBusy = false
    @Published var refreshedWeather: WeatherInfo?
    @Published var busy = false
    @Published var koalaFeedback:KoalaCue?
    @Published var koalaWork:KoalaCue?
    @Published var koalaWelcomePending = !UserDefaults.standard.bool(forKey:"koala.welcome.seen") && !UserDefaults.standard.bool(forKey:"personalPreferences.v2.completed")
    @Published var canCancelOperation=false
    private var planningTask:Task<Void,Never>?
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await BackendConnection.data(for:$0)}
    @Published var operationTitle="正在准备行程…"
    @Published var operationDetail="请稍等，完成后会自动显示结果。"
    @Published var errorMessage: String?
    @Published var health: Health?
    @Published var mapProvider = MapProvider.preferred() {
        didSet { UserDefaults.standard.set(mapProvider.rawValue,forKey:"mapProvider") }
    }
    @Published var travelDataSource=TravelDataSource.current {
        didSet{UserDefaults.standard.set(travelDataSource.rawValue,forKey:"travelDataSource");refreshedWeather=nil;insights=nil;dataConnection=nil;mapLayerIDs=nil}
    }
    @Published var dataConnection:String?
    @Published var mapLayerIDs:Set<String>?
    var availableMapProviders:[MapProvider] {MapProvider.allCases.filter{mapLayerIDs?.contains($0.rawValue) ?? true}}
    func reloadTravelData() async {
        let selected=travelDataSource
        do{let maps:TravelMapConfiguration=try await api("data/map-layers");let status:TravelDataStatus=try await api("data/status")
            guard selected==travelDataSource else{return}
            guard maps.dataSource==selected.rawValue,status.dataSource==selected.rawValue,!maps.layers.isEmpty else{throw URLError(.cannotParseResponse)}
            mapLayerIDs=Set(maps.layers.map(\.id));dataConnection=language.text("資料來源已連線 · {0} 個行程景點",String(status.catalogCount))
        }catch{if selected==travelDataSource{dataConnection=error.localizedDescription}}
    }
    var mapViewport: MapViewport?
    @Published var live = false
    @Published var language = AppLanguage(rawValue:UserDefaults.standard.string(forKey:"appLanguage") ?? "") ?? .simplified {
        didSet {UserDefaults.standard.set(language.rawValue,forKey:"appLanguage")}
    }
    @Published var tab = 1
    @Published var dayIndex = 0
    @Published var expenses: [Expense] = [] { didSet { saveExpenses() } }
    @Published var showPersonalPreferences = !UserDefaults.standard.bool(forKey:"personalPreferences.v2.completed") && !UserDefaults.standard.bool(forKey:"onboarded") && !UserDefaults.standard.bool(forKey:"koko.personalChat.finished")
    @Published var showOnboarding = !UserDefaults.standard.bool(forKey: "onboarded")
    @Published var agentText = "你好！我是你的旅行小伙伴。告诉我你的想法，一起把悉尼安排得刚刚好。"
    @Published var backendMode=UserDefaults.standard.string(forKey:"backendMode") ?? "local"
    private var backendRevision=UUID()
    var base:URL { BackendConnection.base }
    func chooseBackend(_ mode:String,address:String,accessCode:String?=nil) async {
        guard !busy && !isGeneratingTrip, ["local","cloud"].contains(mode),let url=BackendConnection.validated(address) else {errorMessage="請填入 HTTPS 網址，或本機 127.0.0.1 的 HTTP 網址。";return}
        if mode == "cloud",let code=accessCode,!code.isEmpty {
            do{try BackendConnection.saveAccessCode(code,for:url)}catch{errorMessage="無法安全保存連線碼，設定未變更。";return}
        }
        UserDefaults.standard.set(url.absoluteString,forKey:mode == "cloud" ? "cloudBackendURL":"localBackendURL")
        UserDefaults.standard.set(mode,forKey:"backendMode");backendMode=mode
        backendRevision=UUID();travelDataSource = .direct
        health=nil;saved=[];memories=[];trip=nil;proposal=nil;tripToOpen=nil;lastAdjustment=nil
        insights=nil;refreshedWeather=nil;dataConnection=nil;mapLayerIDs=nil;mapViewport=nil;dayIndex=0;errorMessage=nil;live=false
        await reload();await reloadTravelData()
    }
    init() {
        if let data=UserDefaults.standard.data(forKey:"travelPlanningTask"),var task=try? JSONDecoder().decode(TravelPlanningTask.self,from:data){if task.status=="generating"{task.status="draft";task.message="上次生成已中断，需求已保留，点开可重试。"};travelTask=task}
        if let data = UserDefaults.standard.data(forKey: "expenses"), let v = try? JSONDecoder().decode([Expense].self, from: data) { expenses = v }
    }
    var currentDay: DayPlan? { guard let days = trip?.days, !days.isEmpty else { return nil }; return days[min(dayIndex, days.count-1)] }
    func saveExpenses() { if let data = try? JSONEncoder().encode(expenses) { UserDefaults.standard.set(data,forKey:"expenses") } }
    func api<T: Decodable>(_ path: String, method: String = "GET", body: Data? = nil,source:TravelDataSource?=nil) async throws -> T {
        guard base.host != "backend-not-configured.invalid" else{throw NSError(domain:"Configuration",code:1,userInfo:[NSLocalizedDescriptionKey:"请先配置实机可访问的后端地址（SydneyBuddyBackendURL），再重试。"])}
        let requestBase=base;let requestRevision=backendRevision
        var req = URLRequest(url: requestBase.appendingPathComponent(path)); req.httpMethod = method; req.httpBody = body
        let selected=source ?? travelDataSource;selected.apply(to:&req);if path=="weather"{req.cachePolicy = .reloadIgnoringLocalCacheData}
        req.setValue("application/json", forHTTPHeaderField: "Content-Type"); req.timeoutInterval = 240
        let loaded:(Data,URLResponse)
        do {loaded=try await loader(req)}
        catch {guard requestBase==base,requestRevision==backendRevision else{throw CancellationError()};throw error}
        let (data,response)=loaded
        guard requestBase==base,requestRevision==backendRevision else {throw CancellationError()}
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            let e = (try? JSONSerialization.jsonObject(with:data)) as? [String:Any]
            throw NSError(domain:"SydneyBuddy",code:1,userInfo:[NSLocalizedDescriptionKey:e?["error"] as? String ?? "服务暂不可用，请检查本机服务。"])
        }
        if ["weather","plan","plan/prepare","plan/remaining","catalog","data/map-layers","data/status","data/insights"].contains(path),source == nil,selected != travelDataSource {throw CancellationError()}
        return try JSONDecoder().decode(T.self, from: data)
    }
    func reload() async {
        do { health = try await api("health"); if let managed=health?.managedTravelDataSource,let selected=TravelDataSource(rawValue:managed){travelDataSource=selected}; saved = try await api("trips"); saved.sort { $0.createdAt > $1.createdAt }; if trip == nil && !showOnboarding && pendingTripFields.isEmpty { trip = saved.first }; memories = try await api("memories") }
        catch is CancellationError {}
        catch { errorMessage = "無法連接所選後端，請檢查網址、連線碼與網路。" }
    }
    func prepared(_ name: String) -> Bool { preparations.contains((trip?.id ?? "draft")+":"+name) }
    func markPrepared(_ name: String, undo: Bool = false) {
        guard let id=trip?.id else { return }
        let key=id+":"+name
        if undo { preparations.removeAll { $0==key } }
        else if !preparations.contains(key) { preparations.append(key) }
        UserDefaults.standard.set(preparations,forKey:"preparations")
    }
    var currentWeather:WeatherInfo {refreshedWeather ?? WeatherInfo(summary:"天氣尚未查詢",rainy:false,source:travelDataSource.warehouse ? "Open-Meteo / Zetaris":"Open-Meteo / direct",status:"unavailable")}
    func refreshWeather() async {
        guard !weatherBusy else { return }; weatherBusy = true; refreshedWeather=WeatherInfo(summary:"正在查詢即時天氣…",rainy:false,source:travelDataSource.warehouse ? "Open-Meteo / Zetaris":"Open-Meteo / direct",status:"loading"); defer { weatherBusy = false }
        do {
            refreshedWeather = try await api("weather",method:"POST",body:JSONEncoder().encode(trip?.profile ?? profile))
            if refreshedWeather?.status == "ok" { markPrepared("weather") }
        }
        catch { refreshedWeather=WeatherInfo(summary:"即時天氣查詢失敗，請重試。",rainy:false,source:travelDataSource.warehouse ? "Open-Meteo / Zetaris":"Open-Meteo / direct",status:"unavailable");errorMessage = error.localizedDescription }
    }
    func generate(_ message: String = "", rain: Bool = false, profileOverride:TripProfile? = nil) async {
        guard !busy,!isGeneratingTrip else { return }; busy = true;koalaWork=KoalaCue(event:.planningStarted); defer { busy = false;koalaWork=nil }
        do {
            operationTitle="正在调整行程…";operationDetail="检查兴趣、时间、预算和步数。"
            var p = profileOverride ?? trip?.profile ?? profile
            p.request = message
            p.weatherOverride = nil
            if rain { p.weatherOverride = "rain" }
            var raw = try JSONSerialization.jsonObject(with: JSONEncoder().encode(p)) as! [String:Any]
            raw["mode"] = live ? (health?.googleConfigured == true ? "live" : "assisted") : "demo"
            var result = try await roadPlan(raw,profile:p)
            result.title=trip?.title ?? result.suggestedTitle
            refreshedWeather = nil
            proposalIsSuggestion=true;proposal = result
            if result.days.contains(where:{$0.estimatedCost>result.profile.budget}){koalaFeedback=KoalaCue(event:.budgetRisk)}
            agentText = result.explanation
        } catch { errorMessage = error.localizedDescription }
    }
    func generateFromPet(_ profile:TripProfile, request:String, saveImmediately:Bool=false) async {
        guard saveImmediately && (showOnboarding || !pendingTripFields.isEmpty) else {
            self.profile=profile
            await generate(request,profileOverride:profile)
            return
        }
        guard !busy else{return};busy=true;koalaWork=KoalaCue(event:.planningStarted);defer{busy=false;koalaWork=nil}
        do {
            planningStage=1
            operationTitle="正在生成旅行路线…";operationDetail="根据兴趣、活动预算和步数安排行程。"
            var p=profile;p.request=request;p.weatherOverride=nil;p.transportPreference=p.transportPreference ?? "balanced"
            var raw=try JSONSerialization.jsonObject(with:JSONEncoder().encode(p)) as! [String:Any]
            raw["mode"]=live ? (health?.googleConfigured == true ? "live" : "assisted") : "demo"
            var planned=try await roadPlan(raw,profile:p,replanExisting:false)
            try Task.checkCancellation()
            if planned.recommendationStatus == "partial" {
                planned.id=nil;planned.revision=nil;planned.title=planned.suggestedTitle
                proposalIsSuggestion=true;proposal=planned;proposalFromNewTrip=true;agentText=planned.explanation
                return
            }
            canCancelOperation=false
            planned.id=nil;planned.revision=nil;planned.title=planned.suggestedTitle
            planningStage=2
            operationTitle="正在保存到我的行程…";operationDetail="保存后可以查看地图、编辑日程和导航。"
            let body:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(planned)),"expectedRevision":0]
            let accepted:Trip=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:body))
            finishAcceptedTrip(accepted,consumeDraft:true)
            await reload()
        } catch {if !Task.isCancelled{errorMessage=error.localizedDescription}}
    }
    var planningDiscovery:(TripProfile) async throws -> [ScheduleCandidate] = {try await ApplePlanningDiscovery.search($0)}
    func discoverPlanningCandidates(_ profile:TripProfile,source:TravelDataSource) async throws -> [ScheduleCandidate] {
        guard source.warehouse else{return try await planningDiscovery(profile)}
        return try await ApplePlanningDiscovery.search(profile,preserveDetails:true,lookup:{point,category,query in
            struct Result:Decodable {var places:[ScheduleCandidate];var dataSource:String}
            let kinds=["restaurants":"restaurants","parks":"parks","museums":"museums"]
            let kind=kinds[query] ?? category
            let text=category == "all" ? query:""
            let body:[String:Any]=["lat":point.latitude,"lng":point.longitude,"radius":5000,"category":kind,"query":text,"provider":"osm"]
            let result:Result=try await self.api("poi/search",method:"POST",body:JSONSerialization.data(withJSONObject:body),source:source)
            guard result.dataSource==source.rawValue else{throw NSError(domain:"Planning",code:4,userInfo:[NSLocalizedDescriptionKey:"规划地点来源不一致，请重新选择资料来源后再试。"])}
            return result.places
        })
    }
    var roadLegs:([DayPlan]) async throws -> [WalkingObservation] = {try await AppleWalkingRoads.shared.legs($0)}
    private var remainingRequest:[String:Any]?
    private func roadPlan(_ raw:[String:Any],profile:TripProfile,replanExisting:Bool=true,background:Bool=false) async throws -> Trip {
        let mapProvider=background ? (planningMapProvider ?? self.mapProvider):self.mapProvider
        let requestSource=background ? (planningDataSource ?? travelDataSource):travelDataSource
        func report(_ detail:String,_ phase:PlanningPhase = .roads,_ done:Int?=nil,_ total:Int?=nil) {
            if background {travelTask?.progress=PlanningProgress(phase:phase,detail:detail,completed:done,total:total)}else{operationDetail=detail}
        }
        var profile=profile
        var payload=raw
        if health?.nvidiaConfigured==true,(!replanExisting || trip==nil),let origin=profile.origin,!origin.isEmpty,!["环形码头","Circular Quay","中央车站","中央車站","悉尼机场","悉尼機場","未确定","暂不确定","未知","稍后补充","还没确定","还没想好","未提供","unknown","不确定","不知道"].contains(origin) {
            report("正在确认住宿或出发地点的位置…")
            let found=try await ApplePoiProvider.search(profile.start.location,"all",origin)
            guard let first=found.first,found.count==1 || first.name.localizedCaseInsensitiveContains(origin) || origin.localizedCaseInsensitiveContains(first.name) else {throw NSError(domain:"Plan",code:4,userInfo:[NSLocalizedDescriptionKey:"还不能唯一确认住宿或出发地点，请补充酒店全名或具体地址。原需求已保留。"])}
            profile.start=Coordinate(name:first.name,lat:first.lat,lng:first.lng);profile.end=profile.start
            payload["start"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(profile.start));payload["end"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(profile.end))
        }
        if !background{remainingRequest=nil}
        let existing=replanExisting ? trip.flatMap{ $0.id != nil && $0.mode != "live" && $0.profile.date==profile.date && $0.days.count==profile.days ? $0:nil }:nil
        var origin:Coordinate?
        var currentMinute:Int?
        if let existing,existing.days.indices.contains(dayIndex),existing.days[dayIndex].date==TripProfile.dateString(Date()),let id=existing.id {
            let day=existing.days[dayIndex]
            let savedPosition=UserDefaults.standard.dictionary(forKey:JourneyExecution.positionKey(tripID:id,date:day.date))
            if let position=savedPosition,let lat=position["lat"] as? Double,let lng=position["lng"] as? Double,let at=position["at"] as? Double,Date().timeIntervalSince1970-at<120 {
                origin=Coordinate(name:"当前位置",lat:lat,lng:lng)
            }else if savedPosition != nil || !JourneyExecution.completed(tripID:id,date:day.date).isEmpty || !JourneyExecution.skipped(tripID:id,date:day.date).isEmpty || !(day.completedStops ?? []).isEmpty {
                report("正在更新当前位置，保留已经完成的地点…")
                origin=try await currentJourneyPosition()
            }
            if origin != nil {var calendar=Calendar(identifier:.gregorian);calendar.timeZone=TimeZone(identifier:"Australia/Sydney")!;let parts=calendar.dateComponents([.hour,.minute],from:Date());currentMinute=(parts.hour ?? 0)*60+(parts.minute ?? 0)}
        }
        if mapProvider == .apple,raw["mode"] as? String != "live" {
            let baseCatalog:[PlanningCatalogPlace]=try await api("catalog",source:requestSource)
            var catalog=baseCatalog.map(\.candidate)
            if let stored=existing?.planningCandidates{catalog+=stored.filter{p in !catalog.contains{$0.id==p.id}}}
            report("正在查询 Apple 真实步行时间…")
            var observations:[WalkingObservation]=[]
            if let existing,existing.days.indices.contains(dayIndex) {
                let day=existing.days[dayIndex]
                let needsCandidates = profile.mustDo != existing.profile.mustDo || profile.avoid != existing.profile.avoid || (profile.request ?? "").range(of:"多一点美食|加.*餐|more food|add.*restaurant",options:.regularExpression) != nil
                if needsCandidates,requestSource.warehouse || health?.nvidiaConfigured==true {
                    let additions=try await discoverPlanningCandidates(profile,source:requestSource)
                    let combined=(existing.planningCandidates ?? [])+additions.filter{p in !(existing.planningCandidates ?? []).contains{$0.id==p.id}}
                    payload["planningCandidates"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(Array(combined.prefix(60))))
                    catalog+=additions.filter{p in !catalog.contains{$0.id==p.id}}
                }
                let extra=needsCandidates ? Array(catalog.sorted{CLLocation(latitude:$0.lat,longitude:$0.lng).distance(from:CLLocation(latitude:day.start.lat,longitude:day.start.lng))<CLLocation(latitude:$1.lat,longitude:$1.lng).distance(from:CLLocation(latitude:day.start.lat,longitude:day.start.lng))}.prefix(8)):[]
                let points=[day.start,day.end]+day.stops.map{Coordinate(name:$0.name,lat:$0.lat,lng:$0.lng)}+(origin.map{[$0]} ?? [])+extra.map{Coordinate(name:$0.name,lat:$0.lat,lng:$0.lng)}
                observations=try await AppleWalkingRoads.shared.matrix(points){done,total in report("正在核对步行路段 \(done)/\(total)…",.roads,done,total)}
            }else {
                struct Shortlist:Decodable {var candidateDays:[[String]];var candidates:[ScheduleCandidate]?}
                if requestSource.warehouse || health?.nvidiaConfigured==true {
                    report(requestSource.warehouse ? "正在从所选 Zetaris 来源查找地点…":"正在搜索符合你兴趣的真实地点…",.places)
                    let discovered=try await discoverPlanningCandidates(profile,source:requestSource)
                    guard !discovered.isEmpty else{throw NSError(domain:"Plan",code:3,userInfo:[NSLocalizedDescriptionKey:"暂时没有搜到可用地点，请缩小范围或补充想去的地方。未生成示例替代行程。"])}
                    payload["planningCandidates"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(discovered))
                    payload["useAgent"]=health?.nvidiaConfigured==true
                }
                report("正在挑选活动与合适的用餐地点…",.places)
                let shortlist:Shortlist=try await api("plan/prepare",method:"POST",body:JSONSerialization.data(withJSONObject:payload),source:requestSource)
                if let candidates=shortlist.candidates{catalog+=candidates.filter{p in !catalog.contains{$0.id==p.id}}}
                guard shortlist.candidateDays.count==profile.days else {throw NSError(domain:"Plan",code:1,userInfo:[NSLocalizedDescriptionKey:"每天的地点候选不完整，请重试。"])}
                var seen=Set<String>()
                for (index,ids) in shortlist.candidateDays.enumerated() {
                    let picks=catalog.filter{ids.contains($0.id)}
                    guard picks.count==ids.count else {throw NSError(domain:"Plan",code:2,userInfo:[NSLocalizedDescriptionKey:"地点目录已更新，请重新生成。"])}
                    let points=[profile.start,profile.end]+picks.map{Coordinate(name:$0.name,lat:$0.lat,lng:$0.lng)}
                    let values=try await AppleWalkingRoads.shared.matrix(points){done,total in report("第 \(index+1)/\(profile.days) 天：核对步行路段 \(done)/\(total)…",.roads,done,total)}
                    observations+=values.filter{seen.insert(AppleWalkingRoads.key($0.from,$0.to)).inserted}
                }
                payload["walkingDayCandidateIds"]=shortlist.candidateDays
            }
            if let existing {let others=existing.days.enumerated().filter{$0.offset != dayIndex}.map(\.element);observations+=try await AppleWalkingRoads.shared.legs(others);var seen=Set<String>();observations=observations.filter{seen.insert(AppleWalkingRoads.key($0.from,$0.to)).inserted}}
            payload["walkingRoutes"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(observations))
            report(profile.transportPreference == "walk" ? "正在检查步行顺序与返程时间…":"正在核对步行、等车和公共交通班次…",.routes)
        }
        var result:Trip
        if let existing,mapProvider == .apple,existing.days.indices.contains(dayIndex),let id=existing.id {
            let day=existing.days[dayIndex];let currentIDs=Set(day.stops.map(\.id));let completed=JourneyExecution.completed(tripID:id,date:day.date).intersection(currentIDs)
            if !completed.isEmpty && origin==nil {throw NSError(domain:"Journey",code:3,userInfo:[NSLocalizedDescriptionKey:"已保留你的完成进度。请先开启步行导航取得当前位置，再调整剩余行程。"])}
            var body:[String:Any]=["tripId":id,"expectedRevision":existing.revision ?? 0,"day":dayIndex,"completedIds":Array(completed),"request":profile.request ?? "","rain":profile.weatherOverride=="rain","walkingRoutes":payload["walkingRoutes"] ?? [],"constraints":["budget":profile.budget,"maxSteps":profile.maxSteps,"endMinute":profile.endMinute,"mustDo":profile.mustDo ?? "无","avoid":profile.avoid ?? "无","interests":profile.interests,"transportPreference":profile.transportPreference ?? "walk"],"skippedIds":Array(JourneyExecution.skipped(tripID:id,date:day.date).intersection(currentIDs))]
            if let candidates=payload["planningCandidates"]{body["planningCandidates"]=candidates}
            if let origin {body["origin"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(origin))};if let currentMinute{body["currentMinute"]=currentMinute}
            result=try await api("plan/remaining",method:"POST",body:JSONSerialization.data(withJSONObject:body));remainingRequest=body
        }else{result=try await api("plan",method:"POST",body:JSONSerialization.data(withJSONObject:payload),source:requestSource)}
        if let data=payload["walkingRoutes"] {result.walkingRoutes=try JSONDecoder().decode([WalkingObservation].self,from:JSONSerialization.data(withJSONObject:data))}
        return result
    }
    private func currentJourneyPosition() async throws -> Coordinate {
        let source=CoreLocationProvider(activityType:.fitness,allowBackgroundLocationUpdates:false)
        source.startUpdating();defer{source.stopUpdating()}
        for _ in 0..<75 {
            try Task.checkCancellation()
            if source.authorizationStatus == .denied || source.authorizationStatus == .restricted {throw NSError(domain:"Journey",code:4,userInfo:[NSLocalizedDescriptionKey:"已保留完成进度。请允许定位后重试，才能从当前位置调整剩余行程。"])}
            if let fix=source.lastLocation,fix.horizontalAccuracy>=0,fix.horizontalAccuracy<=50,abs(fix.timestamp.timeIntervalSinceNow)<30 {return Coordinate(name:"当前位置",lat:fix.coordinates.lat,lng:fix.coordinates.lng)}
            try await Task.sleep(for:.milliseconds(200))
        }
        throw NSError(domain:"Journey",code:5,userInfo:[NSLocalizedDescriptionKey:"暂时无法取得准确位置，原行程已保留。到开阔处后可直接重试调整。"])
    }
    private func finishAcceptedTrip(_ accepted:Trip, consumeDraft:Bool=false) {
        if (showOnboarding || consumeDraft),let id=accepted.id,let session=UserDefaults.standard.string(forKey:"petSession.draft") {
            UserDefaults.standard.set(session,forKey:"petSession."+id);UserDefaults.standard.removeObject(forKey:"petSession.draft")
        }
        trip=accepted;profile=accepted.profile;proposal=nil;dayIndex=0;selectedStopID=nil
        if consumeDraft{UserDefaults.standard.removeObject(forKey:"petComposer.draft")}
        koalaFeedback=KoalaCue(event:.tripSaved)
        if consumeDraft{tripToOpen=accepted}
        if showOnboarding || consumeDraft {pendingTripFields=[:]}
        showPersonalPreferences=false;showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");tab=1
    }
    func saveAdjustment(_ candidate:Trip,original:Trip,dayIndex:Int?=nil,inline:Bool=false) async -> Bool {
        guard !busy,original.id != nil else{return false}
        if !inline{busy=true};defer{if !inline{busy=false}}
        do {
            operationTitle="正在保存调整…";operationDetail="保存成功后更新地图和当天行程。"
            var value=candidate;value.id=original.id;value.revision=original.revision;value.title=original.title
            var raw:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(value)),"expectedRevision":original.revision ?? 0]
            if let dayIndex{raw["adjustedDay"]=dayIndex}
            else if inline,let state=candidate.schedule{raw["adjustedDays"]=ScheduleInlineDraft.changedDays(original:original,draft:state).sorted()}
            let accepted:Trip=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            lastAdjustment=original
            if let i=saved.firstIndex(where:{$0.id==accepted.id}){saved[i]=accepted}
            if !inline || trip?.id==original.id{trip=accepted;selectedStopID=nil};adjustmentError=nil;koalaFeedback=KoalaCue(event:.preferenceConfirmed);return true
        }catch{adjustmentError=error.localizedDescription;return false}
    }
    func updateWalkingTimes(_ plan:JourneyPlan,original:Trip,dayIndex:Int) async -> Bool {
        guard !busy,original.id != nil,original.days.indices.contains(dayIndex),plan.source=="Apple Maps / MapKit",plan.legs.flatMap(\.steps).allSatisfy({$0.mode=="walk"}),plan.legs.count==original.days[dayIndex].stops.count+1 else{return false}
        busy=true;defer{busy=false}
        do {
            operationTitle="正在更新当天时间…";operationDetail="校验实际步行、休息、步数和返程时间。"
            let legs=plan.legs.map{leg in ["meters":leg.steps.reduce(0){$0+$1.meters},"seconds":leg.steps.reduce(0){$0+$1.seconds}]}
            let accepted:Trip
            if let state=original.schedule {
                var observations=try await roadLegs(original.days)
                let points=[original.days[dayIndex].start]+original.days[dayIndex].stops.map{Coordinate(name:$0.name,lat:$0.lat,lng:$0.lng)}+[original.days[dayIndex].end]
                for i in plan.legs.indices {
                    let key=AppleWalkingRoads.key(points[i],points[i+1]);observations.removeAll{AppleWalkingRoads.key($0.from,$0.to)==key}
                    observations.append(WalkingObservation(from:points[i],to:points[i+1],meters:plan.legs[i].steps.reduce(0){$0+$1.meters},seconds:plan.legs[i].steps.reduce(0){$0+$1.seconds},fetchedAt:ISO8601DateFormatter().string(from:Date())))
                }
                let body:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(original)),"settings":try JSONSerialization.jsonObject(with:JSONEncoder().encode(state.settings)),"days":try JSONSerialization.jsonObject(with:JSONEncoder().encode(state.days)),"walkingRoutes":try JSONSerialization.jsonObject(with:JSONEncoder().encode(observations))]
                let candidate:Trip=try await api("schedule/editor",method:"POST",body:JSONSerialization.data(withJSONObject:body))
                if let issue=candidate.schedule?.issues.first(where:{$0.blocking && ($0.day==dayIndex || $0.day == -1)}){throw NSError(domain:"Walking",code:1,userInfo:[NSLocalizedDescriptionKey:issue.message+" 原安排已保留。"])}
                let save:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(candidate)),"expectedRevision":original.revision ?? 0,"adjustedDay":dayIndex]
                accepted=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:save))
            }else{
                let raw:[String:Any]=["trip":["id":original.id!],"expectedRevision":original.revision ?? 0,"walkingTimeDay":dayIndex,"walkingLegs":legs]
                accepted=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            }
            lastAdjustment=original;if let i=saved.firstIndex(where:{$0.id==accepted.id}){saved[i]=accepted};trip=accepted;self.dayIndex=dayIndex;selectedStopID=nil;adjustmentError=nil;koalaFeedback=KoalaCue(event:.preferenceConfirmed);return true
        }catch{adjustmentError=error.localizedDescription;return false}
    }
    func undoLastAdjustment() async -> Bool {
        guard !busy,let original=trip,lastAdjustment?.id==original.id else{return false}
        busy=true;defer{busy=false}
        do {
            operationTitle="正在恢复原安排…"
            let raw:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(original)),"expectedRevision":original.revision ?? 0,"undoAdjustment":true]
            let accepted:Trip=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            if let i=saved.firstIndex(where:{$0.id==accepted.id}){saved[i]=accepted}
            trip=accepted;lastAdjustment=nil;selectedStopID=nil;return true
        }catch{errorMessage=error.localizedDescription;return false}
    }
    func renameTrip(_ original:Trip,to value:String) async -> Bool {
        let title=value.trimmingCharacters(in:.whitespacesAndNewlines)
        guard !busy,!title.isEmpty,title.count<=40,original.id != nil else{return false}
        busy=true;defer{busy=false}
        do {
            operationTitle="正在保存旅行名称…";operationDetail="保留原来的地点和时间安排。"
            var renamed=original;renamed.title=title
            let raw:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(renamed)),"expectedRevision":original.revision ?? 0,"renameOnly":true]
            let accepted:Trip=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            if let i=saved.firstIndex(where:{$0.id==accepted.id}){saved[i]=accepted}
            if trip?.id==accepted.id{trip=accepted}
            return true
        }catch{errorMessage=error.localizedDescription;return false}
    }
    func acceptProposal() async {
        guard var p = proposal else { return }
        let adjustmentRequest=remainingRequest;let before=trip;let newTrip=proposalFromNewTrip
        busy = true; defer { busy = false }
        do {
            operationTitle="正在保存行程…";operationDetail="保存成功后返回你的行程。"
            p.id = newTrip ? nil:trip?.id; p.revision = newTrip ? nil:trip?.revision
            var raw: [String:Any] = ["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(p)),"expectedRevision":p.revision ?? 0]
            if let adjustmentRequest{raw["remainingRequest"]=adjustmentRequest}
            let accepted: Trip = try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            if newTrip{completeGeneratedTrip(accepted);proposal=nil}else{finishAcceptedTrip(accepted)}
            if let adjustmentRequest{dayIndex=adjustmentRequest["day"] as? Int ?? 0;lastAdjustment=before}
            await reload()
        } catch { errorMessage = error.localizedDescription }
    }
    func tryScheduleDemo() async {
        guard !busy else{return};busy=true;defer{busy=false}
        do {var p=TripProfile();p.days=3;p.budget=250;p.endMinute=1140
            var raw=try JSONSerialization.jsonObject(with:JSONEncoder().encode(p)) as! [String:Any];raw["mode"]="demo"
            let result:Trip=try await api("plan",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            trip=nil;proposal=result
        }catch{errorMessage=error.localizedDescription}
    }
    func completePetPreferences(_ profile:TripProfile, fields:[String:String]) {
        self.profile=profile;pendingTripFields=fields;trip=nil;proposal=nil
        travelTask=TravelPlanningTask(fields:fields,personalProfile:pendingPersonalProfile,assumptions:TravelRequirements.assumptions(fields,personal:pendingPersonalProfile))
        showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");tab=1
    }
    func startGeneratingPendingTrip() {
        guard planningTask==nil,!busy,!isGeneratingTrip else{return}
        restorePlanningFieldsIfNeeded()
        planningTask=Task {defer{planningTask=nil};await generatePendingTrip()}
    }
    private func restorePlanningFieldsIfNeeded() {
        if pendingTripFields.isEmpty,let task=travelTask,task.status != "completed" {
            pendingTripFields=task.fields
            if let personal=task.personalProfile{pendingPersonalProfile=personal}
        }
    }
    func cancelPendingGeneration(){if canCancelPlanning{planningTask?.cancel()}}
    func openPlanningDraft() {
        guard let draft=travelTask?.draft,travelTask?.status=="review",proposal==nil else{return}
        proposal=draft;proposalIsSuggestion=true;proposalFromNewTrip=true
    }
    private func completeGeneratedTrip(_ accepted:Trip) {
        if let id=accepted.id,let session=UserDefaults.standard.string(forKey:"petSession.draft") {
            UserDefaults.standard.set(session,forKey:"petSession."+id);UserDefaults.standard.removeObject(forKey:"petSession.draft")
        }
        saved.removeAll{$0.id==accepted.id};saved.insert(accepted,at:0)
        travelTask?.status="completed";travelTask?.tripID=accepted.id;travelTask?.draft=nil;travelTask?.message=nil
        travelTask?.progress=PlanningProgress(phase:.finished,detail:"路线收好啦，点开一起看看。")
        if pendingTripFields==travelTask?.fields{pendingTripFields=[:]}
        UserDefaults.standard.removeObject(forKey:"petComposer.draft")
        // Completion updates the card only: never replace the open trip, tab or navigation.
    }
    func generatePendingTrip() async {
        guard !pendingTripFields.isEmpty,!isGeneratingTrip else{return}
        let fields=pendingTripFields;let personal=pendingPersonalProfile
        planningDataSource=travelDataSource;planningMapProvider=mapProvider
        defer{planningDataSource=nil;planningMapProvider=nil}
        travelTask=TravelPlanningTask(id:travelTask?.id ?? UUID().uuidString,status:"generating",fields:fields,personalProfile:personal,assumptions:TravelRequirements.assumptions(fields,personal:personal),progress:PlanningProgress())
        do {
            var p=try TravelRequirements.readyProfile(fields,personal:personal);p.weatherOverride=nil;p.transportPreference=p.transportPreference ?? "balanced"
            var raw=try JSONSerialization.jsonObject(with:JSONEncoder().encode(p)) as! [String:Any]
            raw["mode"]=live ? (health?.googleConfigured == true ? "live":"assisted"):"demo"
            var planned=try await roadPlan(raw,profile:p,replanExisting:false,background:true)
            try Task.checkCancellation();planned.id=nil;planned.revision=nil;planned.title=planned.suggestedTitle
            if planned.recommendationStatus=="partial" {
                travelTask?.status="review";travelTask?.draft=planned;travelTask?.message="部分需求还需要确认，点开看看草案。";return
            }
            travelTask?.progress=PlanningProgress(phase:.saving,detail:"路线已核对，正在保存到你的小旅行…")
            let body:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(planned)),"expectedRevision":0]
            let accepted:Trip=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:body),source:planningDataSource)
            completeGeneratedTrip(accepted)
        } catch {
            if Task.isCancelled || error is CancellationError {
                travelTask?.status="draft";travelTask?.message="已暂停，旅行需求还在，随时可以重试。"
            }else{travelTask?.status="failed";travelTask?.message=error.localizedDescription}
        }
    }
    func completePersonalPreferences() {
        UserDefaults.standard.set(true,forKey:"personalPreferences.completed")
        UserDefaults.standard.set(true,forKey:"personalPreferences.v2.completed")
        UserDefaults.standard.set(true,forKey:"onboarded")
        if pendingTripFields.isEmpty{pendingPersonalProfile=personalProfile}
        showPersonalPreferences=false;showOnboarding=true;tab=1
    }
    func returnToTrips(draft:[String:String]) {
        pendingTripFields=draft
        showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");tab=1
    }
    func resumePendingTrip() {
        guard !isGeneratingTrip else{return}
        restorePlanningFieldsIfNeeded()
        trip=nil;proposal=nil;showOnboarding=true
    }
    func newTrip() { guard !isGeneratingTrip else{return};UserDefaults.standard.removeObject(forKey:"petComposer.draft"); tripToOpen=nil;pendingPersonalProfile=personalProfile;UserDefaults.standard.removeObject(forKey:"petSession.draft");pendingTripFields=[:];travelTask=nil;UserDefaults.standard.removeObject(forKey:"travelPlanningTask");trip=nil;proposal=nil;profile=TripProfile();showOnboarding=true }

    func saveMemory() async {
        do {
            let p = trip?.profile ?? profile
            let body: [String:Any] = ["consent":true,"summary":"喜欢"+p.interests.joined(separator:"、")+"，每天步数偏好约 \(p.maxSteps) 步", "profile":try JSONSerialization.jsonObject(with:JSONEncoder().encode(p))]
            let _: Memory = try await api("memories",method:"POST",body:JSONSerialization.data(withJSONObject:body)); await reload()
        } catch { errorMessage = error.localizedDescription }
    }
    func deleteMemory(_ memory: Memory) async {
        do { let _: [String:Bool] = try await api("memories/"+memory.id,method:"DELETE");if memory.id==UserDefaults.standard.string(forKey:"personalProfile.memoryID"){personalProfile=UserTravelProfile();UserDefaults.standard.removeObject(forKey:"personalProfile.memoryID")}; await reload() }
        catch { errorMessage = error.localizedDescription }
    }
    @discardableResult func deleteTrip(_ value: Trip) async -> Bool {
        guard let id=value.id else{return false}
        do {
            let _: [String:Bool] = try await api("trips/"+id,method:"DELETE")
            saved.removeAll{$0.id==id}
            if trip?.id==id{trip=nil;selectedStopID=nil}
            if tripToOpen?.id==id{tripToOpen=nil}
            if lastAdjustment?.id==id{lastAdjustment=nil}
            if travelTask?.tripID==id{travelTask=nil}
            return true
        }catch{errorMessage=error.localizedDescription;return false}
    }
}

// Display the same per-person activity-cost basis at every stage.
extension TripProfile {
    var activityBudgetSummary:String {
        guard let amount=budgetAmount,let basis=budgetBasis else{return "每人每天活动预算 A$\(Int(budget))"}
        let label=TravelRequirements.budgetBases.first{$0.0==basis}?.1 ?? "每人每天"
        return "\(label)活动预算 A$\(amount.formatted(.number.precision(.fractionLength(0...2))))"
    }
}
extension DayPlan {
    var buddySummary:String {
        if freeTime == true || stops.isEmpty{return "🐨 这天先留给你自由安排：候选地点暂时无法满足约束，可以自己补充喜欢的地方。"}
        let hour=Int(finishTime.split(separator:":").first ?? "24") ?? 24
        return "🐨 今天安排 \(stops.count) 站，约 \(estimatedSteps.formatted()) 步。"+(hour<15 ? "景点集中在上午，\(finishTime) 后自由活动；想逛久一点可以调整。":"按顺序慢慢逛，点活动就能在地图上找到它。")
    }
}

 enum BackendConnection {
    static func saveAccessCode(_ code:String,for url:URL) throws {
        guard url.scheme=="https",code.count>=24,code.count<=256,!code.contains(where:{$0.isWhitespace}) else{throw URLError(.badURL)}
        let query:[String:Any]=[kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:"SydneyBuddy.backend",kSecAttrAccount as String:url.absoluteString]
        let attributes:[String:Any]=[kSecValueData as String:Data(code.utf8),kSecAttrAccessible as String:kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
        let status=SecItemUpdate(query as CFDictionary,attributes as CFDictionary)
        if status==errSecItemNotFound {var item=query;attributes.forEach{item[$0.key]=$0.value};guard SecItemAdd(item as CFDictionary,nil)==errSecSuccess else{throw URLError(.cannotWriteToFile)}}
        else if status != errSecSuccess {throw URLError(.cannotWriteToFile)}
    }
    static func accessCode(for url:URL)->String? {
        let query:[String:Any]=[kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:"SydneyBuddy.backend",kSecAttrAccount as String:url.absoluteString,kSecReturnData as String:true,kSecMatchLimit as String:kSecMatchLimitOne]
        var item:CFTypeRef?;guard SecItemCopyMatching(query as CFDictionary,&item)==errSecSuccess,let data=item as? Data else{return nil};return String(data:data,encoding:.utf8)
    }
    static func authorized(_ request:URLRequest)->URLRequest {
        var result=request;let target=base
        if let url=request.url,url.scheme=="https",url.host==target.host,url.port==target.port,let token=accessCode(for:target){result.setValue("Bearer "+token,forHTTPHeaderField:"Authorization")}
        return result
    }
    private final class NoRedirect:NSObject,URLSessionTaskDelegate,@unchecked Sendable {
        func urlSession(_ session:URLSession,task:URLSessionTask,willPerformHTTPRedirection response:HTTPURLResponse,newRequest request:URLRequest,completionHandler:@escaping (URLRequest?)->Void){completionHandler(nil)}
    }
    static let session=URLSession(configuration:.ephemeral,delegate:NoRedirect(),delegateQueue:nil)
    static func data(for request:URLRequest) async throws -> (Data,URLResponse){try await session.data(for:authorized(request))}
    static func bytes(for request:URLRequest) async throws -> (URLSession.AsyncBytes,URLResponse){try await session.bytes(for:authorized(request))}
    static func validated(_ value:String)->URL? {
        guard let parts=URLComponents(string:value.trimmingCharacters(in:.whitespacesAndNewlines)),let host=parts.host,!host.isEmpty,parts.user==nil,parts.password==nil,parts.query==nil,parts.fragment==nil,
              parts.scheme=="https" || (parts.scheme=="http" && ["localhost","127.0.0.1"].contains(host)),let url=parts.url else{return nil}
        return url
    }
    static func address(_ mode:String)->String {
        let prefs=UserDefaults.standard
        if let saved=prefs.string(forKey:mode == "cloud" ? "cloudBackendURL":"localBackendURL"){return saved}
        if mode == "cloud" {return "http://127.0.0.1:8790"}
        if let legacy=prefs.string(forKey:"backendURL"),validated(legacy) != nil{return legacy}
        if let bundled=Bundle.main.object(forInfoDictionaryKey:"SydneyBuddyBackendURL") as? String,validated(bundled) != nil{return bundled}
        #if targetEnvironment(simulator)
        return "http://127.0.0.1:8787"
        #else
        return "https://backend-not-configured.invalid"
        #endif
    }
    static var base:URL {validated(address(UserDefaults.standard.string(forKey:"backendMode") ?? "local")) ?? URL(string:"https://backend-not-configured.invalid")!}
    static func url(_ path:String)->URL {base.appendingPathComponent(path)}
}

/// /catalog contains factual place records; recommendation reasons are added later.
struct PlanningCatalogPlace:Decodable {
    var id:String;var name:String;var category:String;var indoor:Bool;var visitMinutes:Int;var cost:Double;var lat:Double;var lng:Double
    var description:String?;var note:String?;var openMinute:Int?;var closeMinute:Int?;var activityType:String?
    var candidate:ScheduleCandidate {ScheduleCandidate(id:id,name:name,category:category,indoor:indoor,visitMinutes:visitMinutes,cost:cost,lat:lat,lng:lng,reason:"地点目录候选",description:description,note:note,openMinute:openMinute,closeMinute:closeMinute,activityType:activityType)}
}

// Saved-trip checks are isolated from chat/generation state; no global busy flag or profile mutation.
struct WorldSummary:Codable {
    var walkingSteps:Int;var cost:Double;var transportMinutes:Int;var outdoorMinutes:Int
    var energyRisk:String;var budgetRisk:String;var weatherRisk:String;var source:String
}
struct WorldSuggestion:Codable,Identifiable {
    var id:String;var dayIndex:Int;var title:String;var detail:String;var action:String;var status:String
}
struct WorldDecision:Codable {var suggestionId:String;var decision:String}
struct WorldRun:Codable,Identifiable {
    var id:String;var tripId:String;var tripRevision:Int;var status:String;var stale:Bool
    var summary:WorldSummary;var suggestions:[WorldSuggestion];var decisions:[WorldDecision]
    func decision(for suggestion:WorldSuggestion)->String? {decisions.last{$0.suggestionId==suggestion.id}?.decision}
    func matches(_ trip:Trip)->Bool {trip.id==tripId && trip.revision==tripRevision && !stale && status=="completed"}
}
struct WorldRunEnvelope:Codable {var available:Bool;var run:WorldRun?}

@MainActor extension AppStore {
    func finishPersonalConversation(_ session:PetSession?,useClues:Bool) {
        if useClues {
            for (key,value) in KokoConversationPresentation.tripSeed(from:session) where !TravelRequirements.known(pendingTripFields[key]) {pendingTripFields[key]=value}
        }
        // Completing/skipping the introduction does not claim that long-term memory was saved.
        UserDefaults.standard.set(true,forKey:"koko.personalChat.finished")
        showPersonalPreferences=false;showOnboarding=true;tab=1
    }
}
