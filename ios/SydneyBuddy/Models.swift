import Foundation
import CoreLocation

enum TravelDataSource:String,CaseIterable,Identifiable {
    case direct,zetaris
    var id:String {rawValue}
    var label:String {self == .direct ? "直接來源":"透過 Zetaris"}
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
    static func dateString(_ date: Date) -> String {
        let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd"; f.timeZone = TimeZone(identifier: "Australia/Sydney"); return f.string(from: date)
    }
}
struct Stop: Codable, Identifiable {
    var id: String; var name: String; var lat: Double; var lng: Double
    var category: String; var indoor: Bool; var visitMinutes: Int; var cost: Double
    var note: String; var arrival: String; var departure: String; var hoursVerified: Bool
    var mapsURL: String; var steps: Int
    var coordinate: CLLocationCoordinate2D { .init(latitude: lat, longitude: lng) }
    var symbol: String {
        switch category { case "美食": return "fork.knife"; case "自然": return "leaf.fill"; case "艺术", "文化": return "building.columns.fill"; case "城市": return "building.2.fill"; default: return "sparkles" }
    }
}
struct RouteLeg: Codable { var meters: Int; var minutes: Int; var polyline: String; var source: String }
struct DayPlan: Codable, Identifiable {
    var date: String; var stops: [Stop]; var legs: [RouteLeg]; var estimatedCost: Double
    var estimatedSteps: Int; var finishTime: String; var warnings: [String]
    var start: Coordinate; var end: Coordinate
    var freeTime: Bool? = nil
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
    var schedule:ScheduleState? = nil
    var id: String?; var revision: Int?; var title: String; var mode: String
    var profile: TripProfile; var days: [DayPlan]; var weather: WeatherInfo
    var explanation: String; var warnings: [String]; var trace: [Trace]; var createdAt: String
}
struct Memory: Codable, Identifiable {
    var id: String; var summary: String; var profile: MemoryProfile
    var provenance:Provenance? = nil
    struct Provenance:Codable {var createdAt:String}
    struct MemoryProfile: Codable { var interests: [String]; var maxSteps: Int; var habits:UserTravelProfile? = nil }
}
struct Health: Codable { var navigationConfigured: Bool?; var paidServicesAllowed: Bool?; var status: String; var nvidiaConfigured: Bool; var googleConfigured: Bool; var iosMapConfigured: Bool; var model: String }
struct Expense: Codable, Identifiable {
    var id = UUID(); var title: String; var cents: Int; var participants: Int
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
struct TravelPlanningTask:Codable {
    var id=UUID().uuidString;var status="draft";var fields:[String:String];var tripID:String?;var message:String?;var personalProfile:UserTravelProfile?=nil;var assumptions:[String]?=nil
}

@MainActor final class AppStore: ObservableObject {
    @Published var personalProfile=UserTravelProfile.load() {
        didSet{if let data=try? JSONEncoder().encode(personalProfile){UserDefaults.standard.set(data,forKey:"userTravelProfile")}}
    }
    @Published var pendingPersonalProfile=UserTravelProfile.load(key:"pendingPersonalProfile") {
        didSet{if let data=try? JSONEncoder().encode(pendingPersonalProfile){UserDefaults.standard.set(data,forKey:"pendingPersonalProfile")}}
    }
    @Published var travelTask:TravelPlanningTask? {
        didSet{if let value=travelTask,let data=try? JSONEncoder().encode(value){UserDefaults.standard.set(data,forKey:"travelPlanningTask")}}
    }
    @Published var preparations: [String] = UserDefaults.standard.stringArray(forKey:"preparations") ?? []
    @Published var trip: Trip? { didSet { refreshedWeather = nil } }
    @Published var proposal: Trip? {didSet{if proposal == nil{proposalIsSuggestion=false}}}
    @Published var proposalIsSuggestion=false
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
    @Published var canCancelOperation=false
    private var planningTask:Task<Void,Never>?
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
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
    @Published var showPersonalPreferences = !UserDefaults.standard.bool(forKey:"personalPreferences.v2.completed")
    @Published var showOnboarding = !UserDefaults.standard.bool(forKey: "onboarded")
    @Published var agentText = "你好！我是你的旅行小伙伴。告诉我你的想法，一起把悉尼安排得刚刚好。"
    let base = URL(string: "http://127.0.0.1:8787")!
    init() {
        if let data=UserDefaults.standard.data(forKey:"travelPlanningTask"),var task=try? JSONDecoder().decode(TravelPlanningTask.self,from:data){if task.status=="generating"{task.status="draft";task.message="上次生成已中断，可以继续。"};travelTask=task}
        if let data = UserDefaults.standard.data(forKey: "expenses"), let v = try? JSONDecoder().decode([Expense].self, from: data) { expenses = v }
    }
    var currentDay: DayPlan? { guard let days = trip?.days, !days.isEmpty else { return nil }; return days[min(dayIndex, days.count-1)] }
    func saveExpenses() { if let data = try? JSONEncoder().encode(expenses) { UserDefaults.standard.set(data,forKey:"expenses") } }
    func api<T: Decodable>(_ path: String, method: String = "GET", body: Data? = nil) async throws -> T {
        var req = URLRequest(url: base.appendingPathComponent(path)); req.httpMethod = method; req.httpBody = body
        let selected=travelDataSource;selected.apply(to:&req);if path=="weather"{req.cachePolicy = .reloadIgnoringLocalCacheData}
        req.setValue("application/json", forHTTPHeaderField: "Content-Type"); req.timeoutInterval = 240
        let (data,response) = try await loader(req)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            let e = (try? JSONSerialization.jsonObject(with:data)) as? [String:Any]
            throw NSError(domain:"SydneyBuddy",code:1,userInfo:[NSLocalizedDescriptionKey:e?["error"] as? String ?? "服务暂不可用，请检查本机服务。"])
        }
        if ["weather","plan","catalog","data/map-layers","data/status","data/insights"].contains(path),selected != travelDataSource {throw CancellationError()}
        return try JSONDecoder().decode(T.self, from: data)
    }
    func reload() async {
        do { health = try await api("health"); saved = try await api("trips"); saved.sort { $0.createdAt > $1.createdAt }; if trip == nil && !showOnboarding && pendingTripFields.isEmpty { trip = saved.first }; memories = try await api("memories") }
        catch { errorMessage = "无法连接本机服务。请在电脑启动 Sydney Buddy 服务后重试。" }
    }
    func prepared(_ name: String) -> Bool { preparations.contains((trip?.id ?? "draft")+":"+name) }
    func markPrepared(_ name: String, undo: Bool = false) {
        guard let id=trip?.id else { return }
        let key=id+":"+name
        if undo { preparations.removeAll { $0==key } }
        else if !preparations.contains(key) { preparations.append(key) }
        UserDefaults.standard.set(preparations,forKey:"preparations")
    }
    var currentWeather:WeatherInfo {refreshedWeather ?? WeatherInfo(summary:"天氣尚未查詢",rainy:false,source:travelDataSource == .zetaris ? "Open-Meteo / Zetaris":"Open-Meteo / direct",status:"unavailable")}
    func refreshWeather() async {
        guard !weatherBusy else { return }; weatherBusy = true; refreshedWeather=WeatherInfo(summary:"正在查詢即時天氣…",rainy:false,source:travelDataSource == .zetaris ? "Open-Meteo / Zetaris":"Open-Meteo / direct",status:"loading"); defer { weatherBusy = false }
        do {
            refreshedWeather = try await api("weather",method:"POST",body:JSONEncoder().encode(trip?.profile ?? profile))
            if refreshedWeather?.status == "ok" { markPrepared("weather") }
        }
        catch { refreshedWeather=WeatherInfo(summary:"即時天氣查詢失敗，請重試。",rainy:false,source:travelDataSource == .zetaris ? "Open-Meteo / Zetaris":"Open-Meteo / direct",status:"unavailable");errorMessage = error.localizedDescription }
    }
    func generate(_ message: String = "", rain: Bool = false, profileOverride:TripProfile? = nil) async {
        guard !busy else { return }; busy = true; defer { busy = false }
        do {
            operationTitle="正在调整行程…";operationDetail="检查兴趣、时间、预算和步数。"
            var p = profileOverride ?? trip?.profile ?? profile
            p.request = message
            p.weatherOverride = nil
            if rain { p.weatherOverride = "rain" }
            var raw = try JSONSerialization.jsonObject(with: JSONEncoder().encode(p)) as! [String:Any]
            raw["mode"] = live ? (health?.googleConfigured == true ? "live" : "assisted") : "demo"
            var result: Trip = try await api("plan",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            result.title=trip?.title ?? result.suggestedTitle
            refreshedWeather = nil
            proposalIsSuggestion=true;proposal = result
            agentText = result.explanation
        } catch { errorMessage = error.localizedDescription }
    }
    func generateFromPet(_ profile:TripProfile, request:String, saveImmediately:Bool=false) async {
        guard saveImmediately && (showOnboarding || !pendingTripFields.isEmpty) else {
            self.profile=profile
            await generate(request,profileOverride:profile)
            return
        }
        guard !busy else{return};busy=true;defer{busy=false}
        do {
            planningStage=1
            operationTitle="正在生成旅行路线…";operationDetail="根据兴趣、活动预算和步数安排行程。"
            var p=profile;p.request=request;p.weatherOverride=nil
            var raw=try JSONSerialization.jsonObject(with:JSONEncoder().encode(p)) as! [String:Any]
            raw["mode"]=live ? (health?.googleConfigured == true ? "live" : "assisted") : "demo"
            var planned:Trip=try await api("plan",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            try Task.checkCancellation()
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
    private func finishAcceptedTrip(_ accepted:Trip, consumeDraft:Bool=false) {
        if (showOnboarding || consumeDraft),let id=accepted.id,let session=UserDefaults.standard.string(forKey:"petSession.draft") {
            UserDefaults.standard.set(session,forKey:"petSession."+id);UserDefaults.standard.removeObject(forKey:"petSession.draft")
        }
        trip=accepted;profile=accepted.profile;proposal=nil;dayIndex=0;selectedStopID=nil
        if consumeDraft{UserDefaults.standard.removeObject(forKey:"petComposer.draft")}
        if consumeDraft{tripToOpen=accepted}
        if showOnboarding || consumeDraft {pendingTripFields=[:]}
        showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");tab=1
    }
    func saveAdjustment(_ candidate:Trip,original:Trip,dayIndex:Int?=nil) async -> Bool {
        guard !busy,original.id != nil else{return false}
        busy=true;defer{busy=false}
        do {
            operationTitle="正在保存调整…";operationDetail="保存成功后更新地图和当天行程。"
            var value=candidate;value.id=original.id;value.revision=original.revision;value.title=original.title
            var raw:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(value)),"expectedRevision":original.revision ?? 0]
            if let dayIndex{raw["adjustedDay"]=dayIndex}
            let accepted:Trip=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            lastAdjustment=original
            if let i=saved.firstIndex(where:{$0.id==accepted.id}){saved[i]=accepted}
            trip=accepted;selectedStopID=nil;adjustmentError=nil;return true
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
        busy = true; defer { busy = false }
        do {
            operationTitle="正在保存行程…";operationDetail="保存成功后返回你的行程。"
            p.id = trip?.id; p.revision = trip?.revision
            let raw: [String:Any] = ["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(p)),"expectedRevision":p.revision ?? 0]
            let accepted: Trip = try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            finishAcceptedTrip(accepted)
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
        guard planningTask==nil,!busy else{return}
        canCancelOperation=true
        planningTask=Task {
            defer{planningTask=nil;canCancelOperation=false}
            await generatePendingTrip()
        }
    }
    func cancelPendingGeneration(){if canCancelOperation{planningTask?.cancel()}}
    func generatePendingTrip() async {
        guard !pendingTripFields.isEmpty else{return}
        do {
            planningStage=0
            let profile=try TravelRequirements.readyProfile(pendingTripFields,personal:pendingPersonalProfile)
            travelTask=TravelPlanningTask(id:travelTask?.id ?? UUID().uuidString,status:"generating",fields:pendingTripFields,personalProfile:pendingPersonalProfile,assumptions:TravelRequirements.assumptions(pendingTripFields,personal:pendingPersonalProfile))
            errorMessage=nil
            let request=profile.request ?? ""
            await generateFromPet(profile,request:request,saveImmediately:true)
            if Task.isCancelled{travelTask?.status="draft";travelTask?.message="生成已取消，需求已保留。"}
            else if let errorMessage{travelTask?.status="failed";travelTask?.message=errorMessage}
            else{travelTask?.status="completed";travelTask?.tripID=trip?.id;travelTask?.message=nil}
        } catch {errorMessage=error.localizedDescription;travelTask?.status="failed";travelTask?.message=error.localizedDescription}
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
        trip=nil;proposal=nil;showOnboarding=true
    }
    func newTrip() { UserDefaults.standard.removeObject(forKey:"petComposer.draft"); tripToOpen=nil;pendingPersonalProfile=personalProfile;UserDefaults.standard.removeObject(forKey:"petSession.draft");pendingTripFields=[:];travelTask=nil;UserDefaults.standard.removeObject(forKey:"travelPlanningTask");trip=nil;proposal=nil;profile=TripProfile();showOnboarding=true }

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
