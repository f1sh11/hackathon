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
    struct MemoryProfile: Codable { var interests: [String]; var maxSteps: Int }
}
struct Health: Codable { var navigationConfigured: Bool?; var paidServicesAllowed: Bool?; var status: String; var nvidiaConfigured: Bool; var googleConfigured: Bool; var iosMapConfigured: Bool; var model: String }
struct Expense: Codable, Identifiable {
    var id = UUID(); var title: String; var cents: Int; var participants: Int
    var shares: [Int] { (0..<participants).map { cents/participants + ($0 < cents%participants ? 1 : 0) } }
}

@MainActor final class AppStore: ObservableObject {
    @Published var preparations: [String] = UserDefaults.standard.stringArray(forKey:"preparations") ?? []
    @Published var trip: Trip? { didSet { refreshedWeather = nil } }
    @Published var proposal: Trip?
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
    @Published var errorMessage: String?
    @Published var health: Health?
    @Published var mapProvider = MapProvider(rawValue: UserDefaults.standard.string(forKey:"mapProvider") ?? "") ?? .osm {
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
    @Published var language = AppLanguage(rawValue:UserDefaults.standard.string(forKey:"appLanguage") ?? "") ?? .traditional {
        didSet {UserDefaults.standard.set(language.rawValue,forKey:"appLanguage")}
    }
    @Published var tab = 1
    @Published var dayIndex = 0
    @Published var expenses: [Expense] = [] { didSet { saveExpenses() } }
    @Published var showOnboarding = !UserDefaults.standard.bool(forKey: "onboarded")
    @Published var agentText = "你好！我是你的旅行小伙伴。告诉我你的想法，一起把悉尼安排得刚刚好。"
    let base = URL(string: "http://127.0.0.1:8787")!
    init() {
        if let data = UserDefaults.standard.data(forKey: "expenses"), let v = try? JSONDecoder().decode([Expense].self, from: data) { expenses = v }
    }
    var currentDay: DayPlan? { guard let days = trip?.days, !days.isEmpty else { return nil }; return days[min(dayIndex, days.count-1)] }
    func saveExpenses() { if let data = try? JSONEncoder().encode(expenses) { UserDefaults.standard.set(data,forKey:"expenses") } }
    func api<T: Decodable>(_ path: String, method: String = "GET", body: Data? = nil) async throws -> T {
        var req = URLRequest(url: base.appendingPathComponent(path)); req.httpMethod = method; req.httpBody = body
        let selected=travelDataSource;selected.apply(to:&req);if path=="weather"{req.cachePolicy = .reloadIgnoringLocalCacheData}
        req.setValue("application/json", forHTTPHeaderField: "Content-Type"); req.timeoutInterval = 240
        let (data,response) = try await URLSession.shared.data(for: req)
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
            var p = profileOverride ?? trip?.profile ?? profile
            p.request = message
            p.weatherOverride = nil
            if rain { p.weatherOverride = "rain" }
            var raw = try JSONSerialization.jsonObject(with: JSONEncoder().encode(p)) as! [String:Any]
            raw["mode"] = live ? (health?.googleConfigured == true ? "live" : "assisted") : "demo"
            let result: Trip = try await api("plan",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            refreshedWeather = nil
            proposal = result
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
            var p=profile;p.request=request;p.weatherOverride=nil
            var raw=try JSONSerialization.jsonObject(with:JSONEncoder().encode(p)) as! [String:Any]
            raw["mode"]=live ? (health?.googleConfigured == true ? "live" : "assisted") : "demo"
            var planned:Trip=try await api("plan",method:"POST",body:JSONSerialization.data(withJSONObject:raw))
            planned.id=nil;planned.revision=nil
            let body:[String:Any]=["trip":try JSONSerialization.jsonObject(with:JSONEncoder().encode(planned)),"expectedRevision":0]
            let accepted:Trip=try await api("trips",method:"POST",body:JSONSerialization.data(withJSONObject:body))
            finishAcceptedTrip(accepted,consumeDraft:true)
            await reload()
        } catch {errorMessage=error.localizedDescription}
    }
    private func finishAcceptedTrip(_ accepted:Trip, consumeDraft:Bool=false) {
        if (showOnboarding || consumeDraft),let id=accepted.id,let session=UserDefaults.standard.string(forKey:"petSession.draft") {
            UserDefaults.standard.set(session,forKey:"petSession."+id);UserDefaults.standard.removeObject(forKey:"petSession.draft")
        }
        trip=accepted;profile=accepted.profile;proposal=nil;dayIndex=0
        if showOnboarding || consumeDraft {pendingTripFields=[:]}
        showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");tab=1
    }
    func acceptProposal() async {
        guard var p = proposal else { return }
        busy = true; defer { busy = false }
        do {
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
        showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");tab=1
    }
    func generatePendingTrip() async {
        guard !pendingTripFields.isEmpty else{return}
        do {
            let profile=try PetTripDraft.profile(pendingTripFields)
            let request=petFields.map{"\($0.1)：\(pendingTripFields[$0.0] ?? "")"}.joined(separator:"；")
            await generateFromPet(profile,request:request,saveImmediately:true)
        } catch {errorMessage=error.localizedDescription}
    }
    func newTrip() { UserDefaults.standard.set(false,forKey:"onboarded"); UserDefaults.standard.removeObject(forKey:"petSession.draft"); let prefs = profile; trip = nil; proposal = nil; profile = TripProfile(); profile.interests = prefs.interests; profile.maxSteps = prefs.maxSteps; showOnboarding = true }
    func saveMemory() async {
        do {
            let p = trip?.profile ?? profile
            let body: [String:Any] = ["consent":true,"summary":"喜欢"+p.interests.joined(separator:"、")+"，每天步数偏好约 \(p.maxSteps) 步", "profile":try JSONSerialization.jsonObject(with:JSONEncoder().encode(p))]
            let _: Memory = try await api("memories",method:"POST",body:JSONSerialization.data(withJSONObject:body)); await reload()
        } catch { errorMessage = error.localizedDescription }
    }
    func deleteMemory(_ memory: Memory) async {
        do { let _: [String:Bool] = try await api("memories/"+memory.id,method:"DELETE"); await reload() }
        catch { errorMessage = error.localizedDescription }
    }
    func deleteTrip(_ value: Trip) async {
        guard let id = value.id else { return }
        do { let _: [String:Bool] = try await api("trips/"+id,method:"DELETE"); if trip?.id==id { trip=nil }; await reload() }
        catch { errorMessage=error.localizedDescription }
    }
}
