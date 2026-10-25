import SwiftUI

struct ScheduleSettings:Codable {
    var pace:String;var timeLimit:Int;var restroomInterval:Int;var mealCost:Double
    var dailyBudget:Double;var totalBudget:Double;var maxSteps:Int;var luggage:Bool
}
struct ScheduleActivity:Codable,Identifiable {
    var id:String;var placeId:String?;var kind:String;var name:String
    var lat:Double;var lng:Double;var duration:Int;var cost:Double
    var transportLabel:String? = nil
    var confirmed:Bool?;var locked:Bool?;var fixedStart:Int?;var fixedDuration:Int?
    var openMinute:Int?;var closeMinute:Int?;var queueMinutes:Int?;var queueSource:String?
    var arrival:String;var departure:String;var scheduledStart:Int;var travelMinutes:Int
    var indoor:Bool?;var hoursVerified:Bool?;var restaurantEstimateId:String?=nil
    var label:String {switch kind {case "meal":return "用餐";case "restroom":return "廁所";case "rest":return "休息";case "stay":return "入住";case "checkout":return "退房";case "bag-drop":return "寄放";case "bag-pickup":return "取件";default:return "景點"}}
}
struct ScheduleStay:Codable {var name:String;var lat:Double;var lng:Double;var cost:Double;var confirmed:Bool?;var checkIn:Int;var checkOut:Int}
struct ScheduleDay:Codable,Identifiable {
    var date:String;var startMinute:Int;var endMinute:Int;var start:Coordinate;var end:Coordinate;var stay:ScheduleStay
    var maxSteps:Int? = nil;var dailyBudget:Double? = nil
    var activities:[ScheduleActivity];var transportMinutes:Int;var queueMinutes:Int;var timeUsed:Int;var timeRemaining:Int
    var unknownQueue:Bool;var estimatedSteps:Int;var estimatedCost:Double;var finishTime:String
    var id:String {date}
}
struct CommunityGuide:Codable {var directions:String;var directionsStatus:String?;var sourceUrl:String;var revision:String;var license:String;var licenseUrl:String;var attribution:String;var retrievedAt:String;var coordinateSource:String?}
struct CommunityReview:Codable,Identifiable {var id:String;var author:String;var text:String;var sourceUrl:String;var publishedAt:String?}
struct ScheduleCandidate:Codable,Identifiable {var id:String;var name:String;var category:String;var indoor:Bool;var visitMinutes:Int;var cost:Double;var lat:Double;var lng:Double;var reason:String;var description:String?;var note:String?;var openMinute:Int?;var closeMinute:Int?;var source:String?;var poiKind:String?;var openingHoursText:String?;var website:String?;var phone:String?;var address:String?;var sourceRelease:String?;var activityType:String? = nil;var guide:CommunityGuide?;var photos:[PlacePhoto]?;var reviews:[CommunityReview]?;var hiddenGem:Bool?;var accessStatus:String?;var sources:[String]?;var sourceIds:[String]?;var sourceUrl:String?;var sourceLicense:String?;var retrievedAt:String?;var wikidata:String?;var restaurantData:RestaurantData?;var socialRecommendations:[RestaurantSocialRecommendation]?}
extension ScheduleCandidate {
    var discoverySymbol:String {switch category{case "住宿":return "bed.double";case "店家":return "bag";case "美食":return "fork.knife";case "設施":return "figure.stand";default:return hiddenGem == true ? "sparkles":"mappin.and.ellipse"}}
    var importedPoi:Bool {source=="OpenStreetMap" || source=="Apple Maps" || source=="Overture Maps" || source=="Wikivoyage" || source=="Restaurant evidence" || source=="Community social recommendation"}

    var activityKind:String {if activityType=="meal" {return "meal"};guard importedPoi else{return "attraction"};return poiKind=="toilets" ? "restroom":poiKind=="drinking_water" ? "rest":category=="美食" ? "meal":"attraction"}
}
struct ScheduleIssue:Codable,Identifiable {var code:String;var message:String;var day:Int;var activityId:String?;var blocking:Bool;var id:String {code+message+(activityId ?? "")}}
struct ScheduleRadar:Codable {var name:String;var value:Double}
struct ScheduleState:Codable {
    var version:Int;var settings:ScheduleSettings;var days:[ScheduleDay];var candidates:[ScheduleCandidate];var issues:[ScheduleIssue]
    var totalCost:Double;var rainy:Bool;var source:String;var radar:[ScheduleRadar]
}
extension ScheduleState {
    /// Adapt an existing route for display only. Opening/closing/restoring never reschedules it.
    static func preserving(_ trip:Trip) -> ScheduleState {
        func minute(_ time:String)->Int {let parts=time.split(separator:":").compactMap{Int($0)};return parts.count==2 ? parts[0]*60+parts[1] : trip.profile.startMinute}
        let settings=ScheduleSettings(pace:"balanced",timeLimit:180,restroomInterval:120,mealCost:25,dailyBudget:trip.profile.budget,totalBudget:trip.profile.budget*Double(trip.days.count),maxSteps:trip.profile.maxSteps,luggage:false)
        let days=trip.days.map { day in
            let activities=day.stops.enumerated().map { i,stop in
                ScheduleActivity(id:stop.id,placeId:stop.placeId ?? stop.id,kind:stop.activityType == "meal" ? "meal":"attraction",name:stop.name,lat:stop.lat,lng:stop.lng,duration:stop.visitMinutes,cost:stop.cost,queueMinutes:nil,queueSource:"未知",arrival:stop.arrival,departure:stop.departure,scheduledStart:minute(stop.arrival),travelMinutes:day.legs.indices.contains(i) ? day.legs[i].minutes : 0,indoor:stop.indoor,hoursVerified:stop.hoursVerified)
            }
            let transport=day.legs.reduce(0){$0+$1.minutes}
            return ScheduleDay(date:day.date,startMinute:trip.profile.startMinute,endMinute:trip.profile.endMinute,start:day.start,end:day.end,stay:ScheduleStay(name:day.end.name,lat:day.end.lat,lng:day.end.lng,cost:0,confirmed:false,checkIn:900,checkOut:660),activities:activities,transportMinutes:transport,queueMinutes:0,timeUsed:transport,timeRemaining:180-transport,unknownQueue:!activities.isEmpty,estimatedSteps:day.estimatedSteps,estimatedCost:day.estimatedCost,finishTime:day.finishTime)
        }
        var seen=Set<String>()
        let candidates=trip.days.flatMap(\.stops).filter{seen.insert($0.id).inserted}.map{stop in trip.planningCandidates?.first(where:{$0.id==(stop.placeId ?? stop.id)}) ?? ScheduleCandidate(id:stop.id,name:stop.name,category:stop.category,indoor:stop.indoor,visitMinutes:stop.visitMinutes,cost:stop.cost,lat:stop.lat,lng:stop.lng,reason:"原行程地点",note:stop.note,activityType:stop.activityType)}
        return ScheduleState(version:0,settings:settings,days:days,candidates:candidates,issues:[],totalCost:trip.days.reduce(0){$0+$1.estimatedCost},rainy:trip.weather.rainy,source:"原行程 · 地点、顺序、时间与预算保持不变；修改或重新建议时才试算。",radar:[])
    }
}
enum ScheduleInlineDraft {
    private struct Signature:Encodable {
        var id:String;var place:String?;var kind:String;var name:String;var duration:Int;var cost:Double;var lat:Double;var lng:Double;var locked:Bool?;var fixedStart:Int?;var fixedDuration:Int?
        init(_ a:ScheduleActivity){id=a.id;place=a.placeId;kind=a.kind;name=a.name;duration=a.duration;cost=a.cost;lat=a.lat;lng=a.lng;locked=a.locked;fixedStart=a.fixedStart;fixedDuration=a.fixedDuration}
    }
    static func changedDays(original:Trip,draft:ScheduleState)->Set<Int> {
        let baseline=original.schedule ?? ScheduleState.preserving(original)
        let encoder=JSONEncoder();encoder.outputFormatting = .sortedKeys
        return Set(draft.days.indices.filter {i in
            guard baseline.days.indices.contains(i) else{return true}
            return (try? encoder.encode(draft.days[i].activities.map(Signature.init))) != (try? encoder.encode(baseline.days[i].activities.map(Signature.init)))
        })
    }
    static func preserveUntouched(_ result:Trip,original:Trip,changed:Set<Int>)->Trip {
        var value=result
        let baseline=original.schedule ?? ScheduleState.preserving(original)
        for i in original.days.indices where !changed.contains(i) && value.days.indices.contains(i){
            value.days[i]=original.days[i]
            if value.schedule?.days.indices.contains(i)==true{value.schedule?.days[i]=baseline.days[i]}
        }
        value.schedule?.totalCost=value.days.reduce(0){$0+$1.estimatedCost}
        value.schedule?.issues.removeAll{ $0.day>=0 && !changed.contains($0.day) }
        return value
    }
}

@MainActor final class ScheduleEditorModel:ObservableObject {
    @Published var trip:Trip?;@Published var state:ScheduleState?;@Published var busy=false;@Published var error:String?
    @Published var notice:String?;@Published var dayIndex=0;@Published var dirty=false
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
    private var pendingSnapshot:Trip?
    private var lastNotice:String?
    private var history:[Trip]=[];private var job:Task<Void,Never>?;private var generation=0
    var roadLoader:([DayPlan]) async throws -> [WalkingObservation] = {try await AppleWalkingRoads.shared.legs($0)}
    var inlineEditing=false
    var seed:Trip?
    var hasUnsavedChanges:Bool {!history.isEmpty || dirty || pendingSnapshot != nil}
    var canUndo:Bool {!history.isEmpty}
    func acceptSaved(_ value:Trip){stop();history=[];pendingSnapshot=nil;seed=value;trip=value;state=value.schedule ?? ScheduleState.preserving(value);dirty=false;error=nil;notice=nil}

    func prepare(_ value:Trip) {guard seed == nil else{return};seed=value;trip=value;state=value.schedule ?? ScheduleState.preserving(value)}
    func loadCatalog() async {
        guard seed?.schedule == nil else{return}
        let token=generation
        do {
            var request=URLRequest(url:BackendConnection.url("catalog"));request.timeoutInterval=15
            let (data,response)=try await loader(request)
            guard !Task.isCancelled,token==generation,(response as? HTTPURLResponse)?.statusCode==200 else{return}
            struct Entry:Decodable {var id:String;var name:String;var category:String;var indoor:Bool;var visitMinutes:Int;var cost:Double;var lat:Double;var lng:Double;var note:String}
            let places=try JSONDecoder().decode([Entry].self,from:data)
            guard var state else{return}
            for p in places where !state.candidates.contains(where:{$0.id==p.id}) {state.candidates.append(ScheduleCandidate(id:p.id,name:p.name,category:p.category,indoor:p.indoor,visitMinutes:p.visitMinutes,cost:p.cost,lat:p.lat,lng:p.lng,reason:"可添加的候选地点",note:p.note))}
            self.state=state
        } catch { /* Candidate discovery is optional; the saved route remains fully available. */ }
    }
    func recompute(regenerate:Bool=false) {
        guard let trip else{return}
        job?.cancel();generation+=1;let token=generation;busy=true;dirty=true;error=nil;notice=nil
        var body:[String:Any]=[:]
        let changedDays=(inlineEditing && seed != nil && state != nil) ? ScheduleInlineDraft.changedDays(original:seed!,draft:state!):Set<Int>()
        do {
            body["trip"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(trip))
            if let state {
                body["candidates"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(state.candidates))
                body["settings"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(state.settings));body["version"]=state.version;body["rainy"]=state.rainy
                if !regenerate {body["days"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(state.days))}
            }
            if !changedDays.isEmpty{body["transportDays"]=changedDays.sorted()}
            let initialBody=body
            job=Task {
                do {
                    var body=initialBody
                    if trip.mode != "live",let state=self.state {
                        var draft=trip;draft.days=state.days.map {d in
                            DayPlan(date:d.date,stops:d.activities.map{a in Stop(id:a.id,name:a.name,lat:a.lat,lng:a.lng,category:a.kind,indoor:a.indoor ?? false,visitMinutes:a.duration,cost:a.cost,note:"",arrival:a.arrival,departure:a.departure,hoursVerified:a.hoursVerified ?? false,mapsURL:"",steps:0)},legs:[],estimatedCost:d.estimatedCost,estimatedSteps:d.estimatedSteps,finishTime:d.finishTime,warnings:[],start:d.start,end:d.end)
                        }
                        let observations=try await self.roadLoader(draft.days)
                        body["walkingRoutes"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(observations))
                    }
                    let data=try JSONSerialization.data(withJSONObject:body)
                    var request=URLRequest(url:BackendConnection.url("schedule/editor"));request.httpMethod="POST";TravelDataSource.current.apply(to:&request);request.httpBody=data;request.timeoutInterval=180;request.setValue("application/json",forHTTPHeaderField:"Content-Type")
                    let (response,http)=try await loader(request)
                    guard (http as? HTTPURLResponse)?.statusCode == 200 else {let message=(try? JSONSerialization.jsonObject(with:response)) as? [String:Any];throw NSError(domain:"schedule",code:1,userInfo:[NSLocalizedDescriptionKey:message?["error"] as? String ?? "試算失敗，草案保留，可重試。"])}
                    var result=try JSONDecoder().decode(Trip.self,from:response)
                    if self.inlineEditing,let seed=self.seed{result=ScheduleInlineDraft.preserveUntouched(result,original:seed,changed:changedDays)}
                    guard !Task.isCancelled,token==generation else{return}
                    self.trip=result;self.state=result.schedule;self.busy=false;self.dirty=false
                    let limits=result.schedule?.issues.filter{["steps","budget","total-budget","time-budget"].contains($0.code)} ?? []
                    if !limits.isEmpty {let message=limits.map(\.message).joined(separator:"\n");if message != self.lastNotice {self.notice=message};self.lastNotice=message}else{self.lastNotice=nil}
                }catch {guard !Task.isCancelled,token==generation else{return};self.busy=false;self.error=error.localizedDescription}
            }
        }catch {busy=false;self.error=error.localizedDescription}
    }
    func edit(_ change:(inout ScheduleState)->Void) {
        guard var value=state else{return};if var snapshot=pendingSnapshot ?? trip {if pendingSnapshot == nil && snapshot.schedule != nil {snapshot.schedule=value};history.append(snapshot);if history.count>25{history.removeFirst()}};pendingSnapshot=nil
        change(&value);state=value;recompute()
    }
    func retime(_ id:String,start:Int,duration:Int) {
        guard let a=state?.days[dayIndex].activities.first(where:{$0.id==id}),a.locked != true else{return}
        edit {state in
            let i=state.days[dayIndex].activities.firstIndex(where:{$0.id==id})!
            state.days[dayIndex].activities[i].fixedStart=max(0,min(1439,start))
            state.days[dayIndex].activities[i].scheduledStart=max(0,min(1439,start))
            state.days[dayIndex].activities[i].duration=max(5,min(240,duration))
            state.days[dayIndex].activities=state.days[dayIndex].activities.enumerated().sorted {
                let l=$0.element.fixedStart ?? $0.element.scheduledStart,r=$1.element.fixedStart ?? $1.element.scheduledStart
                return l==r ? $0.offset<$1.offset:l<r
            }.map(\.element)
        }
    }
    func reorder(_ id:String,before target:String) {
        guard let day=state?.days[dayIndex],let source=day.activities.firstIndex(where:{$0.id==id}),let dest=day.activities.firstIndex(where:{$0.id==target}),source != dest else{return}
        edit {let a=$0.days[dayIndex].activities.remove(at:source);$0.days[dayIndex].activities.insert(a,at:min(dest,$0.days[dayIndex].activities.count))}
    }
    func move(_ id:String,by offset:Int) {guard let index=state?.days[dayIndex].activities.firstIndex(where:{$0.id==id}),let count=state?.days[dayIndex].activities.count,index+offset>=0,index+offset<count else{return};edit{$0.days[dayIndex].activities.swapAt(index,index+offset)}}
    func undo(){guard let previous=history.popLast() else{return};job?.cancel();generation+=1;trip=previous;state=previous.schedule ?? ScheduleState.preserving(previous);busy=false;dirty=false;error=nil;notice=nil}
    func restore(){guard let seed else{return};job?.cancel();generation+=1;history=[];trip=seed;state=seed.schedule ?? ScheduleState.preserving(seed);busy=false;dirty=false;error=nil;notice=nil}
    func stop(){job?.cancel();generation+=1;busy=false}
    func toggleMust(_ candidate:ScheduleCandidate) {
        guard var trip else{return}
        var before=trip;before.schedule=state;pendingSnapshot=before
        if trip.profile.mustVisit.contains(candidate.id){trip.profile.mustVisit.removeAll{$0==candidate.id}}else{trip.profile.mustVisit.append(candidate.id)}
        self.trip=trip
        if !(state?.days.flatMap(\.activities).contains{$0.placeId==candidate.id} ?? true){add(candidate)}else{edit{_ in}}
    }
    func addFacility(_ kind:String,at position:Int?=nil) {
        guard let day=state?.days[dayIndex] else{return}
        let anchor=position.flatMap{p -> ScheduleActivity? in p>0 && p<=day.activities.count ? day.activities[p-1]:nil}
        let labels=["meal":"用餐","restroom":"廁所","rest":"休息","bag-drop":"寄放","bag-pickup":"取件"]
        let a=ScheduleActivity(id:UUID().uuidString,kind:kind,name:(labels[kind] ?? kind)+" · 待確認",lat:anchor?.lat ?? day.start.lat,lng:anchor?.lng ?? day.start.lng,duration:kind=="meal" ? 45:10,cost:kind=="meal" ? (state?.settings.mealCost ?? 25):0,confirmed:false,openMinute:0,closeMinute:1440,arrival:"",departure:"",scheduledStart:day.startMinute,travelMinutes:0)
        edit{value in let index=min(max(0,position ?? value.days[dayIndex].activities.count),value.days[dayIndex].activities.count);value.days[dayIndex].activities.insert(a,at:index)}
    }
    func importPlaces(_ places:[ScheduleCandidate]) {
        guard !busy,let state else{return}
        let additions=places.filter{p in !state.candidates.contains{$0.id==p.id}}
        guard !additions.isEmpty else{return}
        edit{$0.candidates.append(contentsOf:additions)}
    }
    func place(_ candidate:ScheduleCandidate,at minute:Int,moveAcrossDays:Bool=false) {
        guard !busy,let state,minute>=state.days[dayIndex].startMinute,minute<state.days[dayIndex].endMinute else{return}
        if let existing=state.days[dayIndex].activities.first(where:{$0.placeId==candidate.id}) {
            retime(existing.id,start:minute,duration:existing.duration);return
        }
        if let source=state.days.firstIndex(where:{$0.activities.contains{$0.placeId==candidate.id}}) {
            guard moveAcrossDays,let existing=state.days[source].activities.first(where:{$0.placeId==candidate.id}),existing.locked != true else{return}
            edit {s in
                s.days[source].activities.removeAll{$0.id==existing.id}
                var moved=existing;moved.fixedStart=minute;moved.scheduledStart=minute
                let index=s.days[dayIndex].activities.firstIndex{($0.fixedStart ?? $0.scheduledStart)>minute} ?? s.days[dayIndex].activities.count
                s.days[dayIndex].activities.insert(moved,at:index)
            };return
        }
        let a=ScheduleActivity(id:UUID().uuidString,placeId:candidate.id,kind:candidate.activityKind,name:candidate.name,lat:candidate.lat,lng:candidate.lng,duration:candidate.visitMinutes,cost:candidate.cost,fixedStart:minute,arrival:"",departure:"",scheduledStart:minute,travelMinutes:0,indoor:candidate.indoor)
        edit {s in
            let index=s.days[dayIndex].activities.firstIndex{($0.fixedStart ?? $0.scheduledStart)>minute} ?? s.days[dayIndex].activities.count
            s.days[dayIndex].activities.insert(a,at:index)
        }
    }
    func importAndAdd(_ candidate:ScheduleCandidate) {
        guard !busy, state != nil else{return}
        // One edit/recompute: importing first would start a concurrent calculation.
        let a=ScheduleActivity(id:UUID().uuidString,placeId:candidate.id,kind:candidate.activityKind,name:candidate.name,lat:candidate.lat,lng:candidate.lng,duration:candidate.visitMinutes,cost:candidate.activityKind=="meal" ? (state?.settings.mealCost ?? 25):candidate.cost,arrival:"",departure:"",scheduledStart:state!.days[dayIndex].startMinute,travelMinutes:0,indoor:candidate.indoor)
        guard !state!.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id}) else{return}
        edit{s in
            if !s.candidates.contains(where:{$0.id==candidate.id}){s.candidates.append(candidate)}
            s.days[dayIndex].activities.append(a)
        }
    }
    func add(_ candidate:ScheduleCandidate) {
        guard let day=state?.days[dayIndex],!(state?.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id}) ?? true) else{return}
        let a=ScheduleActivity(id:UUID().uuidString,placeId:candidate.id,kind:candidate.activityKind,name:candidate.name,lat:candidate.lat,lng:candidate.lng,duration:candidate.visitMinutes,cost:candidate.cost,arrival:"",departure:"",scheduledStart:day.startMinute,travelMinutes:0,indoor:candidate.indoor)
        edit {let i=$0.days[dayIndex].activities.firstIndex(where:{$0.kind=="stay" || $0.kind=="bag-pickup"}) ?? $0.days[dayIndex].activities.count;$0.days[dayIndex].activities.insert(a,at:i)}
    }
}

struct ScheduleRadarView:View {
    var axes:[ScheduleRadar]
    var body:some View {
        VStack(alignment:.leading) {
            Text("旅行偏好").font(.headline)
            Canvas {context,size in
                let center=CGPoint(x:size.width/2,y:size.height/2),radius=min(size.width,size.height)*0.32
                func point(_ i:Int,_ value:Double)->CGPoint {let angle=Double(i)*2 * .pi/Double(max(1,axes.count)) - .pi/2;return CGPoint(x:center.x+cos(angle)*radius*value,y:center.y+sin(angle)*radius*value)}
                for fraction in [0.25,0.5,0.75,1.0] {var path=Path();for i in axes.indices {let p=point(i,fraction);if i==0{path.move(to:p)}else{path.addLine(to:p)}};path.closeSubpath();context.stroke(path,with:.color(Palette.green.opacity(0.2)),lineWidth:1)}
                var shape=Path();for i in axes.indices {let p=point(i,axes[i].value);if i==0{shape.move(to:p)}else{shape.addLine(to:p)};context.draw(Text(axes[i].name).font(.system(size:10)),at:point(i,1.3))};shape.closeSubpath();context.fill(shape,with:.color(Palette.green.opacity(0.22)));context.stroke(shape,with:.color(Palette.green),lineWidth:2)
            }.frame(height:205).accessibilityLabel(axes.map{$0.name+" \(Int($0.value*100))"}.joined(separator:"，"))
        }
    }
}
@MainActor struct ScheduleEditorView:View {
    @EnvironmentObject var store:AppStore
    @StateObject private var model:ScheduleEditorModel
    init(model:ScheduleEditorModel? = nil){_model=StateObject(wrappedValue:model ?? ScheduleEditorModel())}
    @State private var candidateSheet=false;@State private var settingsSheet=false;@State private var editing:ScheduleActivity?
    @State private var saveWarning=false
    var body:some View {
        NavigationStack {
            ScrollView {
                VStack(alignment:.leading,spacing:16) {
                    if let state=model.state {
                        let day=state.days[min(model.dayIndex,state.days.count-1)]
                        ScrollView(.horizontal,showsIndicators:false){HStack{ForEach(state.days.indices,id:\.self){i in
                            Button("第 \(i+1) 天"){model.dayIndex=i}.buttonStyle(.bordered).tint(model.dayIndex==i ? Palette.green:Palette.muted)
                        }}}
                        Text(day.date+" · "+day.start.name+" 出发").font(.caption).foregroundStyle(Palette.muted)
                        HStack{Text("当天活动").font(.headline);Spacer();Button("添加安排"){candidateSheet=true}.font(.subheadline.bold())}
                        if day.activities.isEmpty{Text("这天还没有活动，添加一个想去的地方吧。").font(.subheadline).foregroundStyle(Palette.muted)}
                        ForEach(day.activities){activity in
                            Button{editing=activity}label:{HStack(spacing:12){
                                Text(activity.arrival.isEmpty ? "待安排":activity.arrival).font(.subheadline.monospacedDigit()).foregroundStyle(Palette.green)
                                VStack(alignment:.leading,spacing:5){Text(activity.name).font(.headline);Text("\(activity.duration) 分钟 · 每人约 A$"+String(format:"%.0f",activity.cost)).font(.caption).foregroundStyle(Palette.muted)}
                                Spacer();Image(systemName:"pencil").foregroundStyle(Palette.green)
                            }.padding(16).background(.white,in:RoundedRectangle(cornerRadius:16))}.buttonStyle(.plain).foregroundStyle(Palette.ink).accessibilityLabel("编辑活动："+activity.name)
                        }
                        if model.busy {HStack{PetAvatar(cue:KoalaCue(event:.optimizationStarted,id:"schedule-recompute"));ProgressView("更新交通与等待时间…").font(.caption)}}
                        ForEach(state.issues.filter{$0.day==model.dayIndex || $0.day == -1}) {issue in Label(issue.message,systemImage:issue.blocking ? "exclamationmark.triangle.fill":"info.circle").font(.caption).foregroundStyle(issue.blocking ? .red:.orange)}
                        HStack{Button("撤销上一步"){model.undo()};Spacer();Button("偏好与住宿"){settingsSheet=true}}.font(.subheadline)
                        DisclosureGroup("查看步数、预算与交通余量"){summary(day,state)}.font(.subheadline)
                        DisclosureGroup("高级调整：拖拽时间轴"){
                            ScheduleWorkbench(model:model,onEdit:{editing=$0},onAdd:{candidateSheet=true})
                        }.font(.subheadline)
                        DisclosureGroup("规划依据与更多操作"){
                            Text(state.source).font(.caption).foregroundStyle(Palette.muted)
                            ScheduleRadarView(axes:state.radar)
                            Button("回到原建议"){model.restore()}.font(.caption)
                            Button("按目前偏好重新建议（会重选活动）"){model.recompute(regenerate:true)}.font(.caption)
                            DisclosureGroup("开发测试"){
                            if state.rainy {Text("户外活动已标示，可替换为室内地点。").font(.caption)}
                            Button(state.rainy ? "切回晴天示例":"测试下雨影响"){model.edit{$0.rainy.toggle()}}
                            }.font(.caption).foregroundStyle(Palette.muted)
                        }.font(.subheadline)
                    }else if model.busy {ProgressView("建立多日首版行程…")}else {Text(model.error ?? "準備行程編輯器");Button("重試"){model.recompute(regenerate:true)}}
                }.padding(20)
            }.background(Palette.cream).tint(Palette.green).navigationTitle("编辑当天行程").navigationBarTitleDisplayMode(.inline)
            .toolbar {ToolbarItem(placement:.topBarLeading){Button("取消"){model.stop();store.proposal=nil}}}
            .safeAreaInset(edge:.bottom){PrimaryButton(title:"保存行程",symbol:"checkmark"){saveWarning=true}.disabled(model.busy || model.dirty || model.state == nil || model.state?.issues.contains(where:\.blocking)==true).padding().frame(maxWidth:.infinity).background(Palette.cream)}
        }
        .task {if let trip=store.proposal {model.prepare(trip);model.dayIndex=min(store.dayIndex,max(0,trip.days.count-1));await model.loadCatalog()}}
        .onDisappear{model.stop()}
        .sheet(isPresented:$candidateSheet){candidates}
        .sheet(isPresented:$settingsSheet){ScheduleSettingsView(model:model)}
        .sheet(item:$editing){activity in ScheduleActivityView(activity:activity,days:model.state?.days.count ?? 1,currentDay:model.dayIndex,onSave:{updated,target in
            model.edit{state in let source=model.dayIndex;state.days[source].activities.removeAll{$0.id==updated.id};let dest=min(target,state.days.count-1);state.days[dest].activities.append(updated)}
        },onReplace:{updated in model.edit{state in if let index=state.days[model.dayIndex].activities.firstIndex(where:{$0.id==updated.id}){state.days[model.dayIndex].activities[index]=updated}}},onDelete:{model.edit{$0.days[model.dayIndex].activities.removeAll{$0.id==activity.id}}})}
        .alert("需要留意",isPresented:Binding(get:{model.notice != nil},set:{if !$0{model.notice=nil}})){Button("繼續調整"){}}message:{Text(model.notice ?? "")}
        .alert("試算未完成",isPresented:Binding(get:{model.error != nil},set:{if !$0{model.error=nil}})){Button("重試"){model.recompute()};Button("保留草案",role:.cancel){}}message:{Text(model.error ?? "")}
        .confirmationDialog("保存目前草案？估算與待確認資料會一併保存。",isPresented:$saveWarning,titleVisibility:.visible){Button("確認保存"){if let trip=model.trip {store.proposal=trip;Task{await store.acceptProposal()}}}}
    }
    func summary(_ day:ScheduleDay,_ state:ScheduleState)->some View {
        ScheduleAllowanceSummary(day:day,state:state,dayNumber:model.dayIndex+1)
    }
    var candidates:some View {NavigationStack{List{Section("新增生活安排（地點待確認）"){ForEach(["meal","restroom","rest","bag-drop","bag-pickup"],id:\.self){kind in Button(["meal":"＋用餐","restroom":"＋廁所","rest":"＋休息","bag-drop":"＋寄放","bag-pickup":"＋取件"][kind] ?? kind){model.addFacility(kind)}}};if let state=model.state {ForEach(state.candidates){c in let selected=state.days.flatMap(\.activities).contains{$0.placeId==c.id};Button{if selected {model.edit{$0.days=model.state!.days.map{day in var day=day;day.activities.removeAll{$0.placeId==c.id};return day}}}else{model.add(c)}}label:{HStack{Image(systemName:selected ? "checkmark.circle.fill":"circle");VStack(alignment:.leading){Text(c.name);Text(c.reason).font(.caption);Text("A$\(Int(c.cost)) · 遊覽 \(c.visitMinutes) 分 · 排隊未知").font(.caption2)}}};Button(model.trip?.profile.mustVisit.contains(c.id)==true ? "★ 必去":"☆ 設為必去"){model.toggleMust(c)}.font(.caption)}}}.navigationTitle("景點選擇").toolbar{Button("完成"){candidateSheet=false}}}}
}

struct ScheduleSettingsView:View {
    @ObservedObject var model:ScheduleEditorModel
    @Environment(\.dismiss) var dismiss
    @State private var settings=ScheduleSettings(pace:"balanced",timeLimit:180,restroomInterval:120,mealCost:25,dailyBudget:100,totalBudget:300,maxSteps:10000,luggage:false)
    @State private var stays:[ScheduleStay]=[]
    @State private var windows:[[Int]]=[]
    var body:some View {NavigationStack{Form {
        Section("偏好 · 沿用問答，可在這裡微調") {
            Picker("行程鬆緊",selection:$settings.pace){Text("悠閒").tag("relaxed");Text("適中").tag("balanced");Text("緊湊").tag("packed")}
            TextField("每日步數上限",value:$settings.maxSteps,format:.number)
            TextField("每日交通＋景點等待（分鐘）",value:$settings.timeLimit,format:.number)
            TextField("廁所間隔（分鐘）",value:$settings.restroomInterval,format:.number)
            TextField("每日預算 A$",value:$settings.dailyBudget,format:.number)
            TextField("每人全旅程活動預算 A$",value:$settings.totalBudget,format:.number)
            TextField("每餐預留 A$",value:$settings.mealCost,format:.number)
            Toggle("首版需要行李寄放／領取",isOn:$settings.luggage)
            Text("節奏／行李偏好儲存後，按重新建議可重選活動。現有手動順序不會自動覆蓋。餐飲與設施可逐項編輯。").font(.caption)
        }
        ForEach(stays.indices,id:\.self){i in Section("第 \(i+1) 晚住宿（最後一天不收住宿費）") {
            Stepper("當日開始 \(windows[i][0]/60):\(String(format:"%02d",windows[i][0]%60))",value:$windows[i][0],in:0...1380,step:15)
            Stepper("當日結束 \(windows[i][1]/60):\(String(format:"%02d",windows[i][1]%60))",value:$windows[i][1],in:15...1440,step:15)
            TextField("住宿名稱／暫定區域",text:$stays[i].name)
            TextField("緯度",value:$stays[i].lat,format:.number);TextField("經度",value:$stays[i].lng,format:.number)
            TextField("每晚房費 A$（本機單人試算）",value:$stays[i].cost,format:.number)
            Stepper("最早入住 \(stays[i].checkIn/60):\(String(format:"%02d",stays[i].checkIn%60))",value:$stays[i].checkIn,in:0...1380,step:15)
            Stepper("最晚退房 \(stays[i].checkOut/60):\(String(format:"%02d",stays[i].checkOut%60))",value:$stays[i].checkOut,in:0...1380,step:15)
            Text("座標與價格由你確認；此頁不訂房，也不代表可寄放行李。").font(.caption)
        }}
    }.navigationTitle("偏好與住宿").toolbar{ToolbarItem(placement:.topBarLeading){Button("取消"){dismiss()}};ToolbarItem(placement:.topBarTrailing){Button("套用"){model.edit{value in value.settings=settings;for i in stays.indices where i<value.days.count{value.days[i].stay=stays[i];value.days[i].startMinute=windows[i][0];value.days[i].endMinute=windows[i][1]}};dismiss()}}}
    .onAppear{if let state=model.state {settings=state.settings;stays=state.days.map(\.stay);windows=state.days.map{[$0.startMinute,$0.endMinute]}}}}}
}
struct ScheduleActivityView:View {
    @State var activity:ScheduleActivity
    var days:Int;var currentDay:Int=0
    var onSave:(ScheduleActivity,Int)->Void;var onReplace:(ScheduleActivity)->Void;var onDelete:()->Void = {}
    @State private var targetDay=0
    @State private var assumedQueue=false
    @State private var useStart=false
    @Environment(\.dismiss) var dismiss
    var body:some View {NavigationStack{Form {
        Section("活動") {
            Text(activity.name)
            Button("移除此活動",role:.destructive){onDelete();dismiss()}
            if activity.kind != "attraction" {TextField("名稱",text:$activity.name);TextField("緯度",value:$activity.lat,format:.number);TextField("經度",value:$activity.lng,format:.number);TextField("費用 A$",value:$activity.cost,format:.number)}
            Stepper("停留 \(activity.duration) 分",value:$activity.duration,in:5...240,step:5).disabled(activity.locked==true)
            Toggle("指定開始時間",isOn:$useStart)
            if useStart {Stepper("\((activity.fixedStart ?? activity.scheduledStart)/60):\(String(format:"%02d",(activity.fixedStart ?? activity.scheduledStart)%60))",value:Binding(get:{activity.fixedStart ?? activity.scheduledStart},set:{activity.fixedStart=$0}),in:0...1439,step:15)}
            Toggle("鎖定時段與停留",isOn:Binding(get:{activity.locked ?? false},set:{activity.locked=$0;if $0 {useStart=true;activity.fixedStart=activity.fixedStart ?? activity.scheduledStart;activity.fixedDuration=activity.duration}else{activity.fixedDuration=nil}}))
            Picker("日期",selection:$targetDay){ForEach(0..<days,id:\.self){Text("第 \($0+1) 天").tag($0)}}
        }
        if activity.kind=="attraction" {Section("排隊假設 · 不是即時資料") {Toggle("填入估算",isOn:$assumedQueue);if assumedQueue{Stepper("排隊 \(activity.queueMinutes ?? 0) 分",value:Binding(get:{activity.queueMinutes ?? 0},set:{activity.queueMinutes=$0}),in:0...360,step:5)}}}
        else {Section("服務窗口 · 請自行確認") {Stepper("開放 \((activity.openMinute ?? 0)/60):\(String(format:"%02d",(activity.openMinute ?? 0)%60))",value:Binding(get:{activity.openMinute ?? 0},set:{activity.openMinute=$0}),in:0...1440,step:15);Stepper("結束 \((activity.closeMinute ?? 1440)/60):\(String(format:"%02d",(activity.closeMinute ?? 1440)%60))",value:Binding(get:{activity.closeMinute ?? 1440},set:{activity.closeMinute=$0}),in:0...1440,step:15)}}
    }.navigationTitle("活動時間").toolbar{ToolbarItem(placement:.topBarLeading){Button("取消"){dismiss()}};ToolbarItem(placement:.topBarTrailing){Button("套用"){if !useStart {activity.fixedStart=nil;activity.locked=false};if !assumedQueue{activity.queueMinutes=nil};if targetDay==currentDay{onReplace(activity)}else{onSave(activity,targetDay)};dismiss()}}}.onAppear{targetDay=currentDay;assumedQueue=activity.queueMinutes != nil;useStart=activity.fixedStart != nil}}}
}


// Remaining values may go negative; only the visual bar is clamped to its track.
struct ScheduleAllowance {
    var limit:Double;var used:Double
    var remaining:Double{limit-used}
    var fraction:Double{limit>0 ? max(0,min(1,remaining/limit)):0}
    var exceeded:Bool{remaining<0}
}
struct ScheduleAllowanceSummary:View {
    var day:ScheduleDay;var state:ScheduleState;var dayNumber:Int
    var animateWarnings=true
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private var steps:ScheduleAllowance{.init(limit:Double(state.settings.maxSteps),used:Double(day.estimatedSteps))}
    private var budget:ScheduleAllowance{.init(limit:state.settings.dailyBudget,used:day.estimatedCost)}
    private var time:ScheduleAllowance{.init(limit:Double(state.settings.timeLimit),used:Double(day.timeUsed))}
    private var total:ScheduleAllowance{.init(limit:state.settings.totalBudget,used:state.totalCost)}
    private var overdrawn:Bool{steps.exceeded || budget.exceeded || time.exceeded || total.exceeded}
    private func integer(_ value:Double)->String{Int(value).formatted()}
    private func money(_ value:Double)->String{String(format:"A$%.2f",value)}
    var body:some View {
        TimelineView(.animation(minimumInterval:0.1,paused:!overdrawn || reduceMotion || !animateWarnings)){context in
            let pulse=reduceMotion || !animateWarnings ? 1:0.55+0.45*(sin(context.date.timeIntervalSinceReferenceDate * .pi)+1)/2
            VStack(alignment:.leading,spacing:12){
                HStack{VStack(alignment:.leading,spacing:3){Text("行程剩餘額度").font(.headline);Text("第 \(dayNumber) 天 · \(day.date)").font(.caption2).foregroundStyle(Palette.muted)};Spacer();if overdrawn{Image(systemName:"exclamationmark.triangle.fill").foregroundStyle(.red).opacity(pulse).accessibilityLabel("行程額度超出上限")}}
                bar("步行精力",id:"steps",allowance:steps,value:integer(steps.remaining)+" 步",color:Palette.green,pulse:pulse)
                bar("當日預算",id:"budget",allowance:budget,value:money(budget.remaining),color:.teal,pulse:pulse)
                bar("交通＋排隊時間",id:"time",allowance:time,value:integer(time.remaining)+" 分",color:.blue,pulse:pulse)
                HStack(alignment:.top,spacing:6){
                    tile("預估步數",value:integer(Double(day.estimatedSteps)),detail:"上限 \(integer(steps.limit)) 步",symbol:"figure.walk")
                    tile("每人當日活動花費",value:money(day.estimatedCost),detail:"每人全旅程 \(money(state.totalCost))",symbol:"dollarsign.circle")
                    tile("交通＋排隊",value:"\(day.timeUsed) 分",detail:"交通 \(day.transportMinutes)＋排隊 \(day.queueMinutes)",symbol:"clock")
                }
                HStack(alignment:.top,spacing:5){if total.exceeded{Image(systemName:"exclamationmark.triangle.fill").opacity(pulse)};Text("每人全旅程活動預算剩餘 "+money(total.remaining)+" / "+money(total.limit)).fixedSize(horizontal:false,vertical:true)}.font(.caption2).foregroundStyle(total.exceeded ? .red:Palette.muted)
                if day.unknownQueue{Text("含未知排隊，剩餘僅供規劃參考").font(.caption2).foregroundStyle(Palette.muted)}
                Text("\(String(format:"%02d:%02d",day.startMinute/60,day.startMinute%60)) 出發 · \(day.finishTime) 結束").font(.caption2).foregroundStyle(Palette.muted)
            }.padding(16).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:20))
        }
    }
    private func bar(_ title:String,id:String,allowance:ScheduleAllowance,value:String,color:Color,pulse:Double)->some View {
        let tint:Color=allowance.exceeded ? .red:allowance.fraction<=0.2 ? .orange:color
        return VStack(alignment:.leading,spacing:6){
            HStack{Text(title).font(.caption);Spacer();Text("剩餘 "+value).font(.system(size:13,weight:.bold,design:.rounded)).monospacedDigit().foregroundStyle(tint);if allowance.exceeded{Image(systemName:"exclamationmark.circle.fill").foregroundStyle(.red).opacity(pulse).accessibilityIdentifier("allowance-warning-"+id)}}
            HStack(spacing:3){ForEach(0..<16){index in
                GeometryReader{geometry in
                    RoundedRectangle(cornerRadius:2).fill(tint.opacity(0.13))
                    RoundedRectangle(cornerRadius:2).fill(tint.gradient).frame(width:geometry.size.width*max(0,min(1,allowance.fraction*16-Double(index))))
                }
            }}.frame(height:12).padding(3).overlay(RoundedRectangle(cornerRadius:5).stroke(tint.opacity(allowance.exceeded ? pulse:0.25),lineWidth:1))
        }.accessibilityElement(children:.ignore).accessibilityLabel(title+"，剩餘 "+value+(allowance.exceeded ? "，已超額":"")).accessibilityIdentifier("allowance-"+id)
    }
    private func tile(_ title:String,value:String,detail:String,symbol:String)->some View {
        VStack(alignment:.leading,spacing:6){Label(title,systemImage:symbol).font(.system(size:10)).foregroundStyle(Palette.muted).lineLimit(1).minimumScaleFactor(0.8);Text(value).font(.system(size:15,weight:.bold,design:.rounded)).monospacedDigit().lineLimit(1).minimumScaleFactor(0.65);Text(detail).font(.system(size:9)).foregroundStyle(Palette.muted).lineLimit(2).fixedSize(horizontal:false,vertical:true)}.frame(maxWidth:.infinity,minHeight:68,alignment:.topLeading).padding(8).background(.white.opacity(0.85),in:RoundedRectangle(cornerRadius:12))
    }
}

/// The saved itinerary stays unchanged while this view previews an editable copy.
struct InlineTripSchedule:View {
    let trip:Trip
    var onMap:()->Void
    var onAdvanced:()->Void
    var showsDayPicker=true
    var onDraftChange:(Bool)->Void = {_ in}
    var onSavingChange:(Bool)->Void = {_ in}
    @EnvironmentObject private var store:AppStore
    @StateObject private var model=ScheduleEditorModel()
    @State private var expanded:String?
    @State private var durationActivity:ScheduleActivity?
    @State private var candidateRequest:ScheduleCandidateRequest?
    @State private var ledger=false
    @State private var chatRequest:ScheduleChatRequest?
    @State private var saving=false
    @State private var discard=false
    @State private var savedMessage=false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private var index:Int {store.dayIndex}
    private var day:ScheduleDay? {guard let state=model.state,state.days.indices.contains(index) else{return nil};return state.days[index]}
    private var changed:Bool {model.hasUnsavedChanges}
    private var editable:Bool {trip.mode != "live"}
    private var blocked:Bool {model.busy || saving}
    private var canSave:Bool {changed && !blocked && !model.dirty && model.error==nil && model.state?.issues.contains(where:{$0.blocking})==false && model.state.map{!ScheduleInlineDraft.changedDays(original:trip,draft:$0).isEmpty}==true}
    private var impact:String {
        guard let day,trip.days.indices.contains(index) else{return "修改已暂存，还没有保存"}
        let old=trip.days[index].finishTime.split(separator:":").compactMap{Int($0)}
        let now=day.finishTime.split(separator:":").compactMap{Int($0)}
        guard old.count==2,now.count==2 else{return "修改已暂存，还没有保存"}
        let delta=now[0]*60+now[1]-old[0]*60-old[1]
        return delta==0 ? "返回时间不变 · 修改尚未保存":"重新试算 \(day.finishTime) 返回 · 原 \(trip.days[index].finishTime)"
    }
    var body:some View {
        VStack(spacing:0){
            if showsDayPicker{TripDayPicker(trip:trip).padding(.horizontal,20).padding(.bottom,8)}
            ScrollView {
                VStack(alignment:.leading,spacing:0){
                    if let day {
                        heading(day)
                        endpoint(day.start.name,time:"出发",symbol:"location.fill")
                        ForEach(Array(day.activities.enumerated()),id:\.element.id){position,activity in
                            connection(position:position,minutes:activity.travelMinutes)
                            activityRow(activity,position:position)
                        }
                        connection(position:day.activities.count,minutes:nil)
                        endpoint(day.end.name,time:day.finishTime,symbol:"flag.checkered")
                        if editable{Text("点停留时间可调整 · 长按地点卡可排序").font(.caption).foregroundStyle(Palette.muted).padding(.top,20)}
                        Text(model.trip?.days.indices.contains(index)==true && model.trip!.days[index].legs.allSatisfy{$0.source.contains("Apple")} ? "交通按路线来源查询安排；费用、停留和未知排队仍为估算。":changed ? "步行和排队会重新校验；道路查询失败时保留草稿。":"时间和费用为估算；修改时将查询 Apple 步行路线。").font(.caption).foregroundStyle(Palette.muted).padding(.top,10)
                    }
                    issues
                    Divider().padding(.vertical,22)
                    Button{ledger=true}label:{HStack{Label("预算与花费",systemImage:"creditcard");Spacer();Image(systemName:"chevron.right").font(.caption)}}.font(.subheadline).frame(minHeight:44).buttonStyle(BuddyPressStyle()).accessibilityIdentifier("trip-budget")
                    Button{askKoko(nil)}label:{HStack(spacing:12){PetAvatar(mood:.curious,size:48).frame(width:48,height:54);VStack(alignment:.leading,spacing:4){Text("要不要一起调整一下？").font(.subheadline.bold());Text("换个地点，或留一点发呆的时间").font(.caption).foregroundStyle(Palette.muted)};Spacer();Image(systemName:"bubble.left.and.bubble.right")}.padding(12).background(Palette.lavender.opacity(0.6),in:RoundedRectangle(cornerRadius:20))}.buttonStyle(BuddyPressStyle()).padding(.vertical,12).disabled(changed || saving).accessibilityIdentifier("schedule-ask-koko")
                    DisclosureGroup("旅行说明"){VStack(alignment:.leading,spacing:10){Text(store.language.display(trip.explanation));if trip.days.indices.contains(index){if let past=trip.days[index].completedStops,!past.isEmpty {Text("已完成："+past.map(\.name).joined(separator:"、"))};if let skipped=trip.days[index].skippedStops,!skipped.isEmpty {Text("已跳过："+skipped.map(\.name).joined(separator:"、"))}};ForEach(trip.warnings,id:\.self){Text(store.language.display($0))}}.font(.caption).foregroundStyle(Palette.muted).padding(.top,8)}.font(.caption)
                }.padding(20)
            }
            footer
        }.foregroundStyle(Palette.ink).background(Palette.cream)
        .task(id:trip.id){model.inlineEditing=true;model.prepare(trip);model.dayIndex=index;store.adjustmentError=nil;await model.loadCatalog()}
        .onAppear{if model.dirty && !model.busy{model.recompute()}}
        .onChange(of:index){_,value in model.dayIndex=value;expanded=nil}
        .onChange(of:changed){_,value in onDraftChange(value);if value{savedMessage=false}}
        .onChange(of:saving){_,value in onSavingChange(value)}
        .onChange(of:trip.revision){_,_ in if !changed || saving{model.acceptSaved(trip)}}
        .onDisappear{model.stop()}
        .sheet(item:$durationActivity){activity in InlineDurationSheet(activity:activity){minutes in
            model.dayIndex=index
            model.edit{state in if let i=state.days[index].activities.firstIndex(where:{$0.id==activity.id}){state.days[index].activities[i].duration=minutes}}
        }}
        .sheet(item:$candidateRequest){request in candidatePicker(request)}
        .sheet(isPresented:$ledger){JournalView(tripID:trip.id)}
        .sheet(item:$chatRequest){request in AgentView(context:request.text)}
        .confirmationDialog("放弃这次尚未保存的调整？",isPresented:$discard,titleVisibility:.visible){Button("放弃修改",role:.destructive){model.restore();expanded=nil};Button("继续调整",role:.cancel){}}
        .sensoryFeedback(.success,trigger:savedMessage)
    }
    private func heading(_ day:ScheduleDay)->some View {
        VStack(alignment:.leading,spacing:8){
            HStack{Text("第 \(index+1) 天").font(.system(.title2,design:.rounded,weight:.bold));Text(day.date).font(.subheadline).foregroundStyle(Palette.muted);Spacer();if editable{Menu{Button("添加安排",systemImage:"plus"){openAdd(day.activities.count)};Button("完整时间与住宿设置",systemImage:"slider.horizontal.3"){onAdvanced()}.disabled(changed)}label:{Image(systemName:"slider.horizontal.3").frame(width:44,height:44)}.accessibilityLabel("当天调整")}}
            HStack(spacing:14){Button{ledger=true}label:{Label("每人估算 A$\(Int(day.estimatedCost))",systemImage:"creditcard")}.buttonStyle(.plain);Label("约 \(day.estimatedSteps.formatted()) 步",systemImage:"figure.walk")}.font(.caption).foregroundStyle(Palette.muted)
            if changed{Label("修改预览 · 原行程仍保留",systemImage:"pencil.line").font(.caption.bold()).foregroundStyle(Palette.berry)}
            if savedMessage{Label("收好啦，新的安排已经保存",systemImage:"checkmark.circle.fill").font(.caption).foregroundStyle(Palette.berry)}
        }.padding(.bottom,24)
    }
    private func endpoint(_ name:String,time:String,symbol:String)->some View {
        HStack(alignment:.top,spacing:12){Text(time).font(.caption.monospacedDigit()).foregroundStyle(Palette.muted).frame(width:46,alignment:.leading);Image(systemName:symbol).foregroundStyle(Palette.berry).frame(width:24);Text(name).font(.subheadline.weight(.semibold));Spacer()}.padding(.vertical,8)
    }
    private func transportTitle(_ position:Int)->String {
        if let plan=model.trip,plan.days.indices.contains(index),plan.days[index].legs.indices.contains(position){return plan.days[index].legs[position].label}
        return "交通"
    }
    private func connection(position:Int,minutes:Int?)->some View {
        HStack(spacing:12){Color.clear.frame(width:46,height:1);Rectangle().fill(Palette.berry.opacity(0.18)).frame(width:1,height:32).frame(width:24);if let minutes,minutes>0{Text("\(transportTitle(position))约 \(minutes) 分钟").font(.caption).foregroundStyle(Palette.muted)};Spacer();if editable{Button{openAdd(position)}label:{Image(systemName:"plus").font(.caption.bold()).frame(width:44,height:44)}.buttonStyle(BuddyPressStyle()).disabled(blocked).accessibilityLabel("在第 \(position+1) 个位置添加安排").accessibilityIdentifier("schedule-insert-\(position)")}}
    }
    private func activityRow(_ activity:ScheduleActivity,position:Int)->some View {
        VStack(alignment:.leading,spacing:0){
            HStack(alignment:.top,spacing:12){
                Text(activity.arrival.isEmpty ? "试算中":activity.arrival).font(.caption.monospacedDigit()).foregroundStyle(Palette.muted).frame(width:46,alignment:.leading).background(timeChanged(activity) ? Palette.blush:.clear,in:RoundedRectangle(cornerRadius:6)).padding(.top,13)
                Image(systemName:symbol(activity.kind)).font(.subheadline).foregroundStyle(Palette.berry).frame(width:30,height:34).background(activity.kind=="attraction" ? Palette.lavender:Palette.peach,in:RoundedRectangle(cornerRadius:10)).padding(.top,4).accessibilityHidden(true)
                VStack(alignment:.leading,spacing:4){
                    Button{withAnimation(reduceMotion ? nil:.easeInOut(duration:0.2)){expanded=expanded==activity.id ? nil:activity.id}}label:{HStack(alignment:.top){Text(activity.name).font(.system(.headline,design:.rounded)).multilineTextAlignment(.leading);Spacer(minLength:4);Image(systemName:expanded==activity.id ? "chevron.up":"chevron.down").font(.caption).padding(.top,4)}.frame(minHeight:40)}.buttonStyle(.plain).accessibilityIdentifier("schedule-stop-"+activity.id)
                    Button{durationActivity=activity}label:{HStack(spacing:5){Text("停留 \(activity.duration) 分钟");Image(systemName:activity.locked==true ? "lock.fill":"pencil").font(.caption2)}.font(.caption).foregroundStyle(Palette.berry).frame(minHeight:36)}.buttonStyle(BuddyPressStyle()).disabled(!editable || blocked || activity.locked==true).accessibilityIdentifier("schedule-duration-"+activity.id)
                }
            }
            if expanded==activity.id{activityActions(activity,position:position).padding(.leading,58).padding(.top,10)}
        }.padding(12).background(expanded==activity.id ? Color.white:Color.white.opacity(0.45),in:RoundedRectangle(cornerRadius:20)).overlay(RoundedRectangle(cornerRadius:20).stroke(expanded==activity.id ? Palette.berry.opacity(0.28):.clear))
        .draggable("koko-schedule|\(trip.id ?? "")|\(index)|\(activity.id)")
        .dropDestination(for:String.self){values,_ in
            guard editable,!blocked,let value=values.first else{return false}
            let prefix="koko-schedule|\(trip.id ?? "")|\(index)|"
            guard value.hasPrefix(prefix) else{return false}
            let id=String(value.dropFirst(prefix.count));guard id != activity.id,day?.activities.contains(where:{$0.id==id && $0.locked != true})==true,activity.locked != true else{return false}
            model.reorder(id,before:activity.id);UISelectionFeedbackGenerator().selectionChanged();return true
        }
    }
    private func activityActions(_ activity:ScheduleActivity,position:Int)->some View {
        VStack(alignment:.leading,spacing:8){
            if changed{Text("保存调整后，可查看这个安排的地图。").font(.caption).foregroundStyle(Palette.muted)}
            HStack{Button("看地图",systemImage:"map"){store.selectedStopID=activity.id;onMap()}.disabled(changed);Spacer();Button("问 Koko",systemImage:"sparkles"){askKoko(activity)}.disabled(changed)}.font(.subheadline).frame(minHeight:44)
            if editable{
                HStack(spacing:16){Button("替换",systemImage:"arrow.triangle.2.circlepath"){candidateRequest=ScheduleCandidateRequest(position:position,replacing:activity.id)}.disabled(activity.locked==true || required(activity));Spacer();Button("移除",systemImage:"minus.circle",role:.destructive){model.edit{$0.days[index].activities.removeAll{$0.id==activity.id}};expanded=nil}.disabled(activity.locked==true || required(activity))}.font(.subheadline).frame(minHeight:44)
                HStack{Button("上移",systemImage:"arrow.up"){model.move(activity.id,by:-1)}.disabled(position==0 || activity.locked==true);Spacer();Button("下移",systemImage:"arrow.down"){model.move(activity.id,by:1)}.disabled(position==(day?.activities.count ?? 0)-1 || activity.locked==true)}.font(.caption).frame(minHeight:44)
                if required(activity){Text("这是你的必去地点；可在完整设置中修改必去要求。").font(.caption).foregroundStyle(Palette.muted)}
            }
        }.disabled(blocked)
    }
    @ViewBuilder private var issues:some View {
        if model.busy{Label("正在更新当天时间与步数…",systemImage:"hourglass").font(.caption).foregroundStyle(Palette.berry).padding(.top,16)}
        if let error=model.error{VStack(alignment:.leading,spacing:8){Text(error).font(.caption);Button("保留草稿并重试"){model.recompute()}.font(.subheadline.bold())}.padding(14).background(Palette.peach,in:RoundedRectangle(cornerRadius:14)).padding(.top,16)}
        ForEach(model.state?.issues.filter{$0.day==index || $0.day == -1} ?? []){issue in Label(store.language.display(issue.message),systemImage:issue.blocking ? "exclamationmark.triangle":"info.circle").font(.caption).foregroundStyle(issue.blocking ? Color.red:Palette.muted).padding(.top,10)}
        if let error=store.adjustmentError{Text(error).font(.caption).foregroundStyle(.red).padding(.top,12)}
    }
    private var footer:some View {
        VStack(spacing:10){
            if changed{
                Text(model.busy ? "正在计算调整影响…":impact).font(.caption).foregroundStyle(Palette.muted).frame(maxWidth:.infinity,alignment:.leading)
                HStack(spacing:12){Button("撤销"){model.undo()}.frame(minWidth:44,minHeight:44).disabled(!model.canUndo || saving);Button("取消"){discard=true}.frame(minWidth:44,minHeight:44).disabled(saving);PrimaryButton(title:saving ? "正在保存…":"保存调整",symbol:"checkmark"){save()}.disabled(!canSave)}
            }else{
                HStack{Text("第 \(index+1) 天 · \(day?.activities.count ?? 0) 个安排").font(.caption).foregroundStyle(Palette.muted);Spacer();Button{onMap()}label:{Label("查看路线",systemImage:"map").font(.subheadline.bold()).padding(.horizontal,16).frame(minHeight:48)}.buttonStyle(KokoPrimaryStyle())}
            }
        }.padding(16).background(.white).accessibilityIdentifier("schedule-action-bar")
    }
    private func candidatePicker(_ request:ScheduleCandidateRequest)->some View {
        NavigationStack{List{
            if request.replacing==nil{Section("给旅途留点空隙"){Button("添加休息",systemImage:"cup.and.saucer"){model.addFacility("rest",at:request.position);candidateRequest=nil};Button("添加用餐",systemImage:"fork.knife"){model.addFacility("meal",at:request.position);candidateRequest=nil};Text("先在前一站附近预留时间；还没有选定具体商家或设施。").font(.caption).foregroundStyle(Palette.muted)}}
            Section(request.replacing==nil ? "添加想去的地方":"换个地方看看"){
                ForEach(model.state?.candidates.filter{candidate in !(model.state?.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id}) ?? true)} ?? []){candidate in
                    Button{insert(candidate,request:request)}label:{VStack(alignment:.leading,spacing:6){Text(candidate.name).font(.headline);Text("停留约 \(candidate.visitMinutes) 分钟 · 每人估算 A$\(Int(candidate.cost))").font(.caption).foregroundStyle(Palette.muted)}}
                }
            }
        }.navigationTitle(request.replacing==nil ? "加一点小安排":"替换地点").navigationBarTitleDisplayMode(.inline).toolbar{Button("取消"){candidateRequest=nil}}}
    }
    private func insert(_ candidate:ScheduleCandidate,request:ScheduleCandidateRequest){
        guard let day else{return}
        let activity=ScheduleActivity(id:UUID().uuidString,placeId:candidate.id,kind:candidate.activityKind,name:candidate.name,lat:candidate.lat,lng:candidate.lng,duration:candidate.visitMinutes,cost:candidate.cost,arrival:"",departure:"",scheduledStart:day.startMinute,travelMinutes:0,indoor:candidate.indoor)
        let position=request.position,old=request.replacing
        model.edit{state in if let old{state.days[index].activities.removeAll{$0.id==old}};state.days[index].activities.insert(activity,at:min(position,state.days[index].activities.count))}
        candidateRequest=nil;expanded=nil
    }
    private func openAdd(_ position:Int){candidateRequest=ScheduleCandidateRequest(position:position,replacing:nil)}
    private func timeChanged(_ activity:ScheduleActivity)->Bool {
        guard changed else{return false}
        let baseline=trip.schedule ?? ScheduleState.preserving(trip)
        guard baseline.days.indices.contains(index) else{return true}
        return baseline.days[index].activities.first(where:{$0.id==activity.id})?.arrival != activity.arrival
    }
    private func required(_ activity:ScheduleActivity)->Bool {trip.profile.mustVisit.contains(activity.placeId ?? activity.id)}
    private func symbol(_ kind:String)->String {switch kind{case "meal":return "fork.knife";case "rest","restroom":return "cup.and.saucer";default:return "mappin.and.ellipse"}}
    private func askKoko(_ activity:ScheduleActivity?){
        guard !changed else{return}
        let context="关于「\(trip.title)」第 \(index+1) 天（\(day?.date ?? "")）"+(activity.map{"的「\($0.name)」"} ?? "的安排")+"，我想调整一下。请先给我建议，确认后再修改。"
        chatRequest=ScheduleChatRequest(text:context)
    }
    private func save(){
        guard canSave,let candidate=model.trip else{return};saving=true
        Task{let ok=await store.saveAdjustment(candidate,original:trip,inline:true);if ok,let accepted=store.saved.first(where:{$0.id==trip.id}){model.acceptSaved(accepted);expanded=nil;savedMessage=true};saving=false}
    }
}

private struct ScheduleCandidateRequest:Identifiable {let id=UUID();let position:Int;let replacing:String?}

private struct ScheduleChatRequest:Identifiable {let id=UUID();let text:String}

private struct InlineDurationSheet:View {
    let activity:ScheduleActivity
    var apply:(Int)->Void
    @Environment(\.dismiss) private var dismiss
    @State private var minutes=60
    var body:some View {
        NavigationStack{ScrollView{VStack(alignment:.leading,spacing:24){
            Text(activity.name).font(.system(.title3,design:.rounded,weight:.bold))
            Text("想在这里待多久？").font(.subheadline).foregroundStyle(Palette.muted)
            Text("\(minutes) 分钟").font(.system(size:38,weight:.bold,design:.rounded)).foregroundStyle(Palette.berry).frame(maxWidth:.infinity)
            Slider(value:Binding(get:{Double(minutes)},set:{minutes=Int($0)}),in:5...240,step:5).accessibilityLabel("停留分钟数")
            HStack{ForEach([30,60,90,120],id:\.self){value in Button("\(value) 分"){minutes=value}.font(.caption.bold()).frame(maxWidth:.infinity,minHeight:44).background(minutes==value ? Palette.blush:Color.white,in:Capsule()).buttonStyle(BuddyPressStyle())}}
            Stepper("自定义停留",value:$minutes,in:5...240,step:5)
            Text("应用后先预览当天变化，保存调整才会更新行程。").font(.caption).foregroundStyle(Palette.muted)
            PrimaryButton(title:"预览调整",symbol:"clock"){apply(minutes);dismiss()}.accessibilityIdentifier("schedule-duration-apply")
        }.padding(24)}.background(Palette.cream).navigationTitle("留一点喜欢的时间").navigationBarTitleDisplayMode(.inline).toolbar{Button("取消"){dismiss()}}}
        .presentationDetents([.height(560),.large]).onAppear{minutes=activity.duration}
    }
}
