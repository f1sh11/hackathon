import Foundation
import CoreLocation

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
    @Published var weatherBusy = false
    @Published var refreshedWeather: WeatherInfo?
    @Published var busy = false
    @Published var errorMessage: String?
    @Published var health: Health?
    @Published var mapProvider = MapProvider(rawValue: UserDefaults.standard.string(forKey:"mapProvider") ?? "") ?? .osm {
        didSet { UserDefaults.standard.set(mapProvider.rawValue,forKey:"mapProvider") }
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
        req.setValue("application/json", forHTTPHeaderField: "Content-Type"); req.timeoutInterval = 240
        let (data,response) = try await URLSession.shared.data(for: req)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            let e = (try? JSONSerialization.jsonObject(with:data)) as? [String:Any]
            throw NSError(domain:"SydneyBuddy",code:1,userInfo:[NSLocalizedDescriptionKey:e?["error"] as? String ?? "服务暂不可用，请检查本机服务。"])
        }
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
    func refreshWeather() async {
        guard health?.paidServicesAllowed == true else { errorMessage="后端已禁止付费服务，无法刷新实时天气。"; return }
        guard !weatherBusy else { return }; weatherBusy = true; defer { weatherBusy = false }
        do {
            refreshedWeather = try await api("weather",method:"POST",body:JSONEncoder().encode(trip?.profile ?? profile))
            if refreshedWeather?.status == "ok" { markPrepared("weather") }
        }
        catch { errorMessage = error.localizedDescription }
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
