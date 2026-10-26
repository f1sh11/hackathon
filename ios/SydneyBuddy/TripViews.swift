import SwiftUI

enum TripAdjustmentComparison {
    static func hasVisibleChanges(_ before:Trip,_ after:Trip,includeConstraints:Bool=true)->Bool {
        func signature(_ trip:Trip)->Data? {
            guard let data=try? JSONEncoder().encode(trip),let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any],let days=raw["days"] as? [[String:Any]] else{return nil}
            let reduced=days.map {day -> [String:Any] in
                var result:[String:Any]=[:]
                for key in ["date","start","end","finishTime","estimatedSteps","estimatedCost"]{result[key]=day[key]}
                let stops=(day["completedStops"] as? [[String:Any]] ?? [])+(day["stops"] as? [[String:Any]] ?? [])
                result["stops"]=stops.map {stop in Dictionary(uniqueKeysWithValues:["id","name","lat","lng","arrival","departure","visitMinutes","cost"].compactMap {key in stop[key].map{(key,$0)}})}
                result["legs"]=(day["legs"] as? [[String:Any]] ?? []).map {leg -> [String:Any] in
                    ["mode":leg["mode"] ?? "walk","minutes":leg["minutes"] ?? 0,"meters":leg["meters"] ?? 0,"walkingMeters":leg["walkingMeters"] ?? leg["meters"] ?? 0]
                }
                return result
            }
            let profile=raw["profile"] as? [String:Any] ?? [:]
            let constraints=Dictionary(uniqueKeysWithValues:["destination","date","days","budget","maxSteps","startMinute","endMinute","mustDo","avoid","interests","transportPreference","budgetCurrency","budgetBasis","budgetAmount","travelers"].compactMap {key in profile[key].map{(key,$0)}})
            return try? JSONSerialization.data(withJSONObject:["days":reduced,"constraints":includeConstraints ? constraints:[:]],options:.sortedKeys)
        }
        guard let old=signature(before),let new=signature(after) else{return true}
        return old != new
    }
}
struct ProposalView:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store:AppStore
    var unchanged:Bool {
        guard !store.proposalFromNewTrip,let old=store.trip,let plan=store.proposal else{return false}
        return !TripAdjustmentComparison.hasVisibleChanges(old,plan,includeConstraints:false)
    }
    var unchangedRequest:Bool {
        guard !store.proposalFromNewTrip,let old=store.trip,let plan=store.proposal else{return false}
        return !TripAdjustmentComparison.hasVisibleChanges(old,plan)
    }
    var body:some View {
        let _=interfaceLocale
        if store.proposal?.mode == "demo" && !store.proposalIsSuggestion {ScheduleEditorView()} else { legacy }
    }
    var legacy:some View {
        NavigationStack {
            ScrollView {
                if let plan=store.proposal {
                    VStack(alignment:.leading,spacing:20) {
                        HStack { Robot(size:65);VStack(alignment:.leading,spacing:6) {Text(uiText(unchanged ? "这次试算没有改变安排":plan.recommendationStatus == "partial" ? "这份草案还需要你看看":store.trip==nil || store.proposalFromNewTrip ? "你的路线准备好了" : "看看这次怎么调整")).font(.title2.bold());Text(uiText(unchanged ? (unchangedRequest ? "原行程保持不变，不需要再次保存":"路线未变；可以确认保存这次的需求更新"):"确认之后，才会保存到你的行程")).font(.caption).foregroundStyle(Palette.muted)} }
                        Text(uiText(unchanged ? "这次试算没有改变地点、时间或预计步数，也没有减少步行。可以先保留原行程，或返回日程缩短停留、移除可选活动，再重新核对路线。":plan.explanation)).font(.subheadline).padding(18).background(Palette.mint,in:RoundedRectangle(cornerRadius:20))
                        if let old=store.trip,!store.proposalFromNewTrip {
                            let before=Set(old.days.flatMap(\.stops).map(\.name)),after=Set(plan.days.flatMap{ $0.stops+($0.completedStops ?? []) }.map(\.name))
                            VStack(alignment:.leading,spacing:8) { Text(uiText("调整对比")).font(.headline);Text(uiText("新增："+(after.subtracting(before).sorted().joined(separator:"、").isEmpty ? "无" : after.subtracting(before).sorted().joined(separator:"、"))));Text(uiText("移除："+(before.subtracting(after).sorted().joined(separator:"、").isEmpty ? "无" : before.subtracting(after).sorted().joined(separator:"、"))));Text(uiText("预计步数：\(old.days.reduce(0){$0+$1.estimatedSteps}) → \(plan.days.reduce(0){$0+$1.estimatedSteps})")) }.font(.caption).padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:20))
                        }
                        WeatherCard(weather:plan.weather,profile:plan.profile)
                        ForEach(plan.days) { day in DaySummary(day:day) }
                        VStack(alignment:.leading,spacing:8) {Text(uiText("数据与限制")).font(.headline);ForEach(plan.warnings,id:\.self){Text(uiText("· "+$0)).font(.caption).foregroundStyle(Palette.muted)}}
                        DisclosureGroup(uiText("查看规划依据与验证记录")) {ForEach(plan.trace){trace in VStack(alignment:.leading,spacing:5){Text(uiText(trace.title)).font(.subheadline.bold());Text(uiText(trace.detail)).font(.caption);Text(uiText(trace.source)).font(.caption2).foregroundStyle(Palette.green)}.padding(.vertical,8)}}
                    }.padding(24)
                }
            }.background(Palette.cream).navigationTitle(uiText("行程草案")).navigationBarTitleDisplayMode(.inline)
            .toolbar {ToolbarItem(placement:.topBarLeading){Button(uiText("取消")){store.proposal=nil}.tint(Palette.muted)}}
            .safeAreaInset(edge:.bottom){PrimaryButton(title:unchangedRequest ? "保留原行程":unchanged ? "保存需求，保留路线":"确认并保存",symbol:"checkmark"){if unchangedRequest{store.proposal=nil}else{Task{await store.acceptProposal()}}}.padding(20).background(Palette.cream)}
        }
    }
}
struct DaySummary:View {
    @Environment(\.locale) private var interfaceLocale

    var day:DayPlan
    @EnvironmentObject private var store:AppStore
    var body:some View {
        let _=interfaceLocale
        VStack(alignment:.leading,spacing:16) {
            HStack {Text(uiText(day.date)).font(.headline);Spacer();Text(uiText("安排估算 A$\(Int(day.estimatedCost))/人")).font(.subheadline.bold()).foregroundStyle(Palette.green)}
            Label(uiText("约 \(day.estimatedSteps) 步 · \(day.finishTime) 返回"),systemImage:"figure.walk").font(.caption).foregroundStyle(Palette.muted)
            if day.stops.contains(where:{$0.costKnown == false}){Text(uiText("未确认费用另计，估算不代表完整总价。")).font(.caption2).foregroundStyle(Palette.muted)}
            Text(uiText("\(day.start.name) 出发 → \(day.end.name) 返回")).font(.caption2).foregroundStyle(Palette.muted)
            if day.freeTime == true {Label(uiText("自由安排 · 景点待补充"),systemImage:"leaf").font(.subheadline).foregroundStyle(Palette.green)}
            Text(uiText(day.buddySummary)).font(.caption).foregroundStyle(Palette.green)
            ForEach(Array(day.stops.enumerated()),id:\.element.id){i,s in
                HStack(alignment:.top,spacing:13) {
                    Text(uiText("\(i+1)")).font(.caption.bold()).frame(width:27,height:27).foregroundStyle(.white).background(Palette.green,in:Circle())
                    VStack(alignment:.leading,spacing:7) {
                        HStack {Text(s.name).font(.subheadline.bold());Spacer();Image(systemName:s.symbol).foregroundStyle(Palette.green)}
                        Text(uiText("\(s.arrival)–\(s.departure) · \(s.indoorLabel) · \(s.costLabel)")).font(.caption).foregroundStyle(Palette.muted)
                        Text(uiText(store.language.display(s.note))).font(.caption2).foregroundStyle(Palette.muted)
                        if !s.hoursVerified {Label(uiText("营业时间待核实"),systemImage:"exclamationmark.circle").font(.caption2).foregroundStyle(.orange)}
                    }
                }
            }
        }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:24))
    }
}
struct AgentView:View {
    var context=""
    var autoSend=false
    var body:some View { PetChatView(initialMessage:context,autoSendInitial:autoSend) }
}
/// Koko checks live inside the existing trip workspace. The original itinerary stays untouched.
struct WorldModelView:View {
    @Environment(\.locale) private var interfaceLocale

    let trip:Trip
    var onAdjust:(WorldSuggestion)->Void
    @EnvironmentObject private var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @State private var run:WorldRun?
    @State private var loading=false
    @State private var active=false
    @State private var error:String?
    @State private var requestID=UUID().uuidString
    private var current:Bool {run?.matches(trip)==true}
    var body:some View {
        let _=interfaceLocale
        NavigationStack {ScrollView {VStack(alignment:.leading,spacing:18) {
            HStack(spacing:12){PetAvatar(mood:loading ? .thinking:.curious).frame(width:78,height:90)
                VStack(alignment:.leading,spacing:6){Text(uiText("出发前，我帮你看看～")).font(.headline);Text(uiText("检查每天的体力、交通、预算和已保存的天气。原行程会好好留着。")).font(.caption).foregroundStyle(Palette.muted)}}
            if loading {ProgressView("正在检查已保存的安排…").frame(maxWidth:.infinity).padding()}
            if let error {Text(uiText(error)).font(.subheadline).foregroundStyle(.red).accessibilityIdentifier("world-error")}
            if let run {
                if !current {Label(uiText("行程已经修改，这份检查需要更新。"),systemImage:"arrow.clockwise").font(.subheadline).accessibilityIdentifier("world-stale")}
                else {
                    Text(uiText("全程约 \(run.summary.walkingSteps) 步 · 每人活动估算 A$\(Int(run.summary.cost))")).font(.subheadline.bold())
                    Text(uiText("每日风险：体力 \(risk(run.summary.energyRisk)) · 预算 \(risk(run.summary.budgetRisk)) · 天气 \(risk(run.summary.weatherRisk))")).font(.caption).foregroundStyle(Palette.muted)
                    if run.suggestions.isEmpty {Text(uiText("按当前资料，没有发现超限问题。按自己的节奏出发，想调整随时叫我～")).font(.subheadline)}
                    ForEach(run.suggestions){suggestion in
                        VStack(alignment:.leading,spacing:10){Text(uiText("第 \(suggestion.dayIndex+1) 天 · \(suggestion.title)")).font(.subheadline.bold());Text(uiText(suggestion.detail)).font(.caption).foregroundStyle(Palette.muted)
                            if run.decision(for:suggestion)=="rejected" {Label(uiText("这次按你的想法保留，不改这一项。"),systemImage:"heart").font(.caption).foregroundStyle(Palette.green)}
                            else {HStack {
                                Button(uiText(suggestion.action=="review" ? "问 Koko 怎么确认":"看看怎么调整")) {Task{await decide(suggestion,"accepted")}}.buttonStyle(.borderedProminent).accessibilityIdentifier("world-adjust-"+suggestion.id)
                                if run.decision(for:suggestion)==nil {Button(uiText("先保留")){Task{await decide(suggestion,"rejected")}}.buttonStyle(.bordered).accessibilityIdentifier("world-keep-"+suggestion.id)}
                            }.disabled(loading)}
                        }.padding(16).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:20))
                    }
                    Text(uiText(run.summary.source)).font(.caption2).foregroundStyle(Palette.muted)
                }
            }
            if !loading {Button(uiText(current ? "重新检查":"让 Koko 检查一下")){Task{await check()}}.buttonStyle(.bordered).accessibilityIdentifier("world-check")}
            Text(uiText("只使用这份行程中的资料，不会自动修改长期偏好。调整仍需预览并保存。")).font(.caption2).foregroundStyle(Palette.muted)
        }.padding(20)}.background(Palette.cream).navigationTitle(uiText("Koko 行程检查")).navigationBarTitleDisplayMode(.inline)
            .toolbar{ToolbarItem(placement:.topBarTrailing){Button(uiText("完成")){dismiss()}}}
            .task {active=true;await load()}.onDisappear{active=false}
        }.tint(Palette.green)
    }
    private func risk(_ value:String)->String {switch value{case "high":return "需留意";case "medium":return "接近上限";case "low":return "未超限";default:return "待确认"}}
    private func load() async {
        guard let id=trip.id else{return};loading=true;defer{loading=false}
        do {let result:WorldRunEnvelope=try await store.api("trips/"+id+"/world");guard !Task.isCancelled,active else{return};run=result.run}
        catch {if !Task.isCancelled{self.error="暂时没能读取检查记录，可以重试。"}}
    }
    private func check() async {
        guard let id=trip.id,!loading else{return};loading=true;error=nil;defer{loading=false}
        do {let body=try JSONSerialization.data(withJSONObject:["requestId":requestID]);let result:WorldRun=try await store.api("trips/"+id+"/simulation-runs",method:"POST",body:body)
            guard !Task.isCancelled,active else{return};run=result;requestID=UUID().uuidString
        }catch {if !Task.isCancelled{self.error=error.localizedDescription}}
    }
    private func decide(_ suggestion:WorldSuggestion,_ decision:String) async {
        guard let run,current,!loading else{return};loading=true;error=nil;defer{loading=false}
        do {let body=try JSONSerialization.data(withJSONObject:["suggestionId":suggestion.id,"decision":decision]);let result:WorldRun=try await store.api("simulation-runs/"+run.id+"/decisions",method:"POST",body:body)
            guard !Task.isCancelled,active else{return};self.run=result
            if decision=="accepted" {onAdjust(suggestion)}
        }catch {if !Task.isCancelled{self.error=error.localizedDescription}}
    }
}
struct TripDetailView:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store:AppStore
    var body:some View {let _=interfaceLocale
         NavigationStack { ScrollView {VStack(alignment:.leading,spacing:20){if let t=store.trip {Text(uiText(t.explanation)).font(.subheadline);WeatherCard(weather:store.currentWeather);ForEach(t.days){DaySummary(day:$0)};ForEach(t.warnings,id:\.self){Text(uiText($0)).font(.caption).foregroundStyle(Palette.muted)};DisclosureGroup(uiText("规划与验证记录")){ForEach(t.trace){r in VStack(alignment:.leading,spacing:6){Text(uiText(r.title)).font(.headline);Text(uiText(r.detail)).font(.caption);Text(uiText(r.source)).font(.caption2).foregroundStyle(Palette.green)}.padding(.vertical,8)}}}}.padding(24)}.background(Palette.cream).navigationTitle(uiText("这次的小冒险")).onAppear {store.markPrepared("route")}.task(id:store.travelDataSource){await store.refreshWeather()} } }
}
struct TripsView:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store:AppStore
    @State private var searchText=""
    @State private var calendarExpanded=false
    @State private var filterDate:String?
    @State private var calendarMonth=Date()
    @State private var navigationTrip:Trip?
    @State private var selectedTrip:Trip?
    @State private var namingTrip:Trip?
    @State private var deleting:Trip?
    @State private var deletingInProgress=false
    @State private var showingPlanning=false
    @State private var planningAction=""
    private var deletionPresented:Binding<Bool> {Binding(get:{deleting != nil},set:{if !$0{deleting=nil}})}
    private var selectionPresented:Binding<Bool> {Binding(get:{selectedTrip != nil},set:{if !$0{selectedTrip=nil}})}
    var body:some View {
        let _=interfaceLocale
        NavigationStack { tripList }
            .fullScreenCover(item:$navigationTrip){trip in SavedTripNavigationView(trip:trip)}
            .sheet(item:$namingTrip){TripNameEditor(trip:$0)}
            .alert(uiText("删除「\(deleting?.title ?? "这份行程")」？"),isPresented:deletionPresented){
                Button(uiText("确认删除"),role:.destructive){
                    if let trip=deleting{deletingInProgress=true;Task{let success=await store.deleteTrip(trip);if success,selectedTrip?.id==trip.id{selectedTrip=nil};deletingInProgress=false;deleting=nil}}
                }
                Button(uiText("保留行程"),role:.cancel){}
            }message:{Text(uiText("删除后无法恢复，需要重新生成行程。"))}

    }
    private var filteredTrips:[Trip] {store.saved.filter {TripLibrary.matches($0,query:searchText,date:filterDate)}}
    private var tripList:some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20){
                homeHeader
                if store.travelTask != nil || !upcoming.isEmpty {
                    Text(uiText("我们的小旅行")).font(.system(.headline,design:.rounded)).foregroundStyle(Palette.ink)
                    if let task=store.travelTask {
                        if task.status=="completed",let trip=taskSavedTrip {if trip.id != homeTrip?.id && !filteringLibrary{savedRow(trip)}}
                        else {planningCard(task)}
                    }
                }
                libraryFilters
                if filteringLibrary{Text(uiText("符合条件：\(filteredTrips.count) 段旅行")).font(.caption).foregroundStyle(Palette.muted)}
                savedRows
            }.padding(20)
        }.background(Palette.cream).navigationTitle(uiText("Koko")).navigationBarTitleDisplayMode(.inline)
            .toolbar{ToolbarItem(placement:.topBarTrailing){Button{store.tab=3}label:{Image(systemName:"person.crop.circle")}.accessibilityLabel(uiText("我的偏好与设置"))}}
            .confirmationDialog(uiText("开启新的旅行？当前未生成的草稿会被替换，已保存的旅行会保留。"),isPresented:$replacingDraft,titleVisibility:.visible){Button(uiText("开启新旅行")){store.newTrip()}}
            .refreshable{await store.reload()}
            .onAppear{openRequestedTrip()}
            .onChange(of:store.tripToOpen?.id){_,_ in openRequestedTrip()}
            .navigationDestination(isPresented:selectionPresented){selectedDestination}
            .sheet(isPresented:$showingPlanning,onDismiss:{
                let action=planningAction;planningAction=""
                if action=="review"{store.openPlanningDraft()}
                if action=="edit"{store.resumePendingTrip()}
                if action=="open",let trip=taskSavedTrip{store.trip=trip;selectedTrip=trip}
            }){PlanningTaskDetails{action in planningAction=action;showingPlanning=false}.environmentObject(store)}
    }
    private var homeTrip:Trip? {TripHomeFocus.pick(store.saved,lastOpenedID:UserDefaults.standard.string(forKey:"lastOpenedTripID"),today:TripProfile.dateString(Date()))}
    private var homeCue:KoalaCue {
        if let task=store.travelTask,task.status=="completed",task.tripID==homeTrip?.id {return KoalaCue(event:.tripSaved,id:"saved:"+task.id)}
        return KoalaCue(event:.welcome,id:"home-welcome")
    }
    private var homeHeader:some View {
        VStack(alignment:.leading,spacing:12){
            HStack(spacing:16){
                KokoCompanionScene(cue:homeCue,height:homeTrip == nil ? 144:104).frame(width:homeTrip == nil ? 144:108)
                VStack(alignment:.leading,spacing:7){
                    Text(uiText(homeTrip == nil ? "今天想怎么过？":"小背包在，Koko 也在")).font(.system(.title2,design:.rounded,weight:.bold)).foregroundStyle(Palette.ink)
                    Text(uiText(store.isGeneratingTrip ? "我去核对路线，你可以先看看之前的小旅行。":hasDraft ? "上次的小想法还在，我们接着聊。":homeTrip.map{TripCompanionContext.speech($0)} ?? "想吃点好的，还是慢慢逛？告诉我一个念头就好。"))
                        .font(.subheadline).foregroundStyle(Palette.muted).lineSpacing(3)
                }
            }
            if let trip=homeTrip {
                Text(uiText("\(trip.profile.date) · \(trip.days.count) 天 · \(trip.profile.travelers ?? 1) 人")).font(.caption).foregroundStyle(Palette.muted)
                PrimaryButton(title:"继续 · \(trip.title)",symbol:"map"){store.trip=trip;selectedTrip=trip}.accessibilityIdentifier("home-continue-trip")
            }
            if store.travelTask == nil || store.travelTask?.status=="completed" {
                if homeTrip != nil {
                    Button{if hasDraft{store.resumePendingTrip()}else{store.newTrip()}}label:{Label(uiText(hasDraft ? "继续和 Koko 准备新旅行":"和 Koko 规划新旅行"),systemImage:"plus.bubble").font(.subheadline.bold()).frame(maxWidth:.infinity,minHeight:44)}.buttonStyle(BuddyPressStyle()).disabled(store.busy).accessibilityIdentifier("koko-home-plan")
                }else{PrimaryButton(title:hasDraft ? "继续和 Koko 聊聊":"帮我安排一段旅行"){if hasDraft{store.resumePendingTrip()}else{store.newTrip()}}.disabled(store.busy).accessibilityIdentifier("koko-home-plan")}
            }
            if hasDraft && !store.isGeneratingTrip{Button(uiText("另开一段旅行")){replacingDraft=true}.font(.caption).frame(minHeight:44).foregroundStyle(Palette.muted)}
        }
    }
    private var libraryFilters:some View {
        VStack(spacing:12) {
            HStack {
                Image(systemName:"magnifyingglass").foregroundStyle(Palette.muted)
                TextField(uiText("搜尋名稱、景點或日期"),text:$searchText).accessibilityIdentifier("trip-library-search")
                if !searchText.isEmpty {Button{searchText=""}label:{Image(systemName:"xmark.circle.fill")}.accessibilityLabel(uiText("清除搜尋"))}
            }.padding(14).background(.white,in:RoundedRectangle(cornerRadius:16))
            HStack {
                Button{withAnimation{calendarExpanded.toggle()}}label:{Label(uiText("旅遊月曆"),systemImage:"calendar");Image(systemName:calendarExpanded ? "chevron.up":"chevron.down")}.accessibilityIdentifier("trip-library-calendar")
                Spacer()
                if let filterDate {Text(uiText(filterDate)).font(.caption.monospacedDigit())}
                if filterDate != nil || !searchText.isEmpty {Button(uiText("顯示全部")){filterDate=nil;searchText=""}.font(.caption.bold()).accessibilityIdentifier("trip-library-reset")}
            }
            if calendarExpanded {
                TripLibraryCalendar(month:$calendarMonth,selection:$filterDate,marked:Set(store.saved.flatMap{TripLibrary.dates($0)}),language:store.language)
                Text(uiText("圓點表示當天有行程；點選日期篩選")).font(.caption).foregroundStyle(Palette.muted)
            }
        }
    }
    private var taskSavedTrip:Trip? {store.saved.first{$0.id==store.travelTask?.tripID}}
    private func planningCard(_ task:TravelPlanningTask)->some View {
        Button{if task.status=="draft"{store.resumePendingTrip()}else{showingPlanning=true}}label:{
            HStack(alignment:.top,spacing:14){
                PetAvatar(mood:task.status=="generating" ? .thinking:task.status=="failed" ? .confused:.ready,cue:task.status=="generating" ? KoalaCue(event:.planningStarted,id:task.id):nil,size:64).frame(width:64,height:72).accessibilityHidden(true)
                VStack(alignment:.leading,spacing:9){
                    Text(uiText(TripDraftSummary(fields:task.fields).title)).font(.system(.headline,design:.rounded)).foregroundStyle(Palette.ink)
                    Text(uiText(task.status=="generating" ? (task.progress?.detail ?? "正在整理旅行需求…"):task.status=="review" ? "草案收好啦，有些安排需要你确认":task.status=="failed" ? "这段路线还没查好，需求已保留":"已暂停，需求还在这里")).font(.subheadline).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)
                    if task.status=="generating"{PlanningSteps(progress:task.progress ?? PlanningProgress())}
                    else{Label(uiText(task.status=="review" ? "查看草案":task.status=="failed" ? "查看原因并重试":"继续准备"),systemImage:"arrow.right").font(.caption.bold()).foregroundStyle(Palette.green)}
                }
                Spacer(minLength:0)
                Image(systemName:"chevron.right").font(.caption.bold()).foregroundStyle(Palette.muted).padding(.top,5)
            }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:22)).overlay(RoundedRectangle(cornerRadius:22).stroke(Palette.green.opacity(0.16)))
        }.buttonStyle(BuddyPressStyle())
            .accessibilityLabel(AppLanguage.current.text("{0}，{1}，点开查看",uiText(TripDraftSummary(fields:task.fields).title),uiText(task.status=="generating" ? task.progress?.detail ?? "正在准备":task.status=="review" ? "草案需要确认":task.status=="failed" ? "查询失败，需求已保留":"已暂停，需求已保留")))
            .accessibilityIdentifier("planning-trip-card")
    }
    @State private var replacingDraft=false
    private var hasDraft:Bool {store.travelTask == nil && (!store.pendingTripFields.isEmpty || !(UserDefaults.standard.string(forKey:"petSession.draft") ?? "").isEmpty)}
    @State private var otherTripsExpanded=false
    @State private var pastExpanded=false
    private var orderedTrips:[Trip] {store.saved.sorted{$0.profile.date < $1.profile.date}}
    private var upcoming:[Trip] {orderedTrips.filter{($0.days.last?.date ?? $0.profile.date) >= TripProfile.dateString(Date()) && !($0.id==store.travelTask?.tripID && store.travelTask?.status=="completed")}}
    private var past:[Trip] {orderedTrips.filter{($0.days.last?.date ?? $0.profile.date) < TripProfile.dateString(Date()) && !($0.id==store.travelTask?.tripID && store.travelTask?.status=="completed")}.reversed()}
    private var filteringLibrary:Bool {filterDate != nil || !searchText.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty}
    private var otherUpcoming:[Trip] {upcoming.filter{$0.id != homeTrip?.id}}
    @ViewBuilder private var savedRows:some View {
        if filteringLibrary {
            ForEach(filteredTrips,id:\.id){trip in savedRow(trip)}
        }else{
            VStack(spacing:16){
                if !otherUpcoming.isEmpty{DisclosureGroup(uiText("其他小旅行（\(otherUpcoming.count)）"),isExpanded:$otherTripsExpanded){VStack(spacing:12){ForEach(otherUpcoming,id:\.id){trip in savedRow(trip)}}.padding(.top,12)}.font(.subheadline)}
                if !past.isEmpty{DisclosureGroup(uiText("过去的旅行（\(past.count)）"),isExpanded:$pastExpanded){VStack(spacing:12){ForEach(past,id:\.id){trip in savedRow(trip)}}.padding(.top,12)}.font(.subheadline).foregroundStyle(Palette.ink)}
            }
        }
    }
    private func savedRow(_ trip:Trip,featured:Bool=false)->some View {
        let title:String = "查看行程：" + trip.title + "，" + trip.profile.date
        let identifier:String = "view-trip-" + (trip.id ?? "draft")
        return VStack(spacing:0){
            if let task=store.travelTask,task.status=="completed",task.tripID==trip.id {
                HStack(spacing:10){PetAvatar(cue:KoalaCue(event:.tripSaved,id:"saved:"+task.id),size:52);Text(uiText("路线收好啦，一起看看吧～")).font(.subheadline.bold());Spacer()}.padding(.horizontal,16).accessibilityIdentifier("trip-saved-celebration")
            }
            Button(action:{store.trip=trip;selectedTrip=trip}) {TripListRow(trip:trip,featured:featured)}
                .buttonStyle(PlainButtonStyle())
                .accessibilityLabel(Text(uiText(title))).accessibilityIdentifier(identifier)
            Divider().padding(.horizontal,16)
            HStack{
                Button{namingTrip=trip}label:{Label(uiText("改名"),systemImage:"pencil").font(.subheadline).frame(minHeight:44)}.accessibilityIdentifier("rename-trip-"+(trip.id ?? "draft"))
                Spacer();Button(role:.destructive){deleting=trip}label:{Label(uiText("删除"),systemImage:"trash").font(.subheadline).frame(minWidth:70,minHeight:44)}.accessibilityLabel(Text(uiText("删除行程："+trip.title))).accessibilityIdentifier("delete-trip-"+(trip.id ?? "draft"))}.padding(.horizontal,16)
        }.background(.white,in:RoundedRectangle(cornerRadius:22)).disabled(deletingInProgress || store.busy)
    }
    @ViewBuilder private var selectedDestination:some View {
        if let selected=selectedTrip {
            let current=store.saved.first(where:{$0.id==selected.id}) ?? selected
            SavedTripWorkspace(trip:current,onNavigate:{navigationTrip=current},onDelete:{deleting=current})
        }
    }
    private func openRequestedTrip(){
        guard let trip=store.tripToOpen else{return}
        selectedTrip=trip;store.tripToOpen=nil
    }
    private var draftCard:some View {
        VStack(alignment:.leading,spacing:10){
            Text(uiText("继续规划")).font(.headline)
            Button{store.resumePendingTrip()}label:{HStack(spacing:12){
                Image(systemName:"bubble.left.and.bubble.right").font(.title3).foregroundStyle(Palette.green)
                VStack(alignment:.leading,spacing:5){Text(uiText(TripDraftSummary(fields:store.pendingTripFields).title)).font(.subheadline.bold());Text(uiText(TripDraftSummary(fields:store.pendingTripFields).subtitle)).font(.caption).foregroundStyle(Palette.muted)}
                Spacer();Image(systemName:"chevron.right").font(.caption)
            }.padding(16).background(Palette.mint.opacity(0.5),in:RoundedRectangle(cornerRadius:18))}.buttonStyle(.plain).disabled(store.busy)
        }.padding(.top,8)
    }

}

private struct PlanningSteps:View {
    @Environment(\.locale) private var interfaceLocale

    var progress:PlanningProgress
    var body:some View {
        let _=interfaceLocale
        HStack(spacing:12){ForEach(Array(["找地点","核对路线","保存"].enumerated()),id:\.offset){index,title in
            HStack(spacing:4){Image(systemName:index<progress.phase.step ? "checkmark.circle.fill":index==progress.phase.step ? "circle.inset.filled":"circle");Text(uiText(title))}
                .font(.caption2.weight(index==progress.phase.step ? .semibold:.regular)).foregroundStyle(index<=progress.phase.step ? Palette.green:Palette.muted.opacity(0.7))
        }}.accessibilityElement(children:.ignore).accessibilityLabel(uiText("准备进度："+progress.detail))
    }
}
private struct PlanningTaskDetails:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store:AppStore
    var action:(String)->Void
    var body:some View {
        let _=interfaceLocale
        NavigationStack{ScrollView{VStack(alignment:.leading,spacing:20){
            if let task=store.travelTask {
                HStack(spacing:14){PetAvatar(mood:task.status=="generating" ? .thinking:.ready,size:80).frame(width:80,height:90);VStack(alignment:.leading,spacing:8){Text(uiText(TripDraftSummary(fields:task.fields).title)).font(.title3.bold());Text(uiText(task.status=="generating" ? "你可以先去逛逛，这里会继续准备。":task.status=="completed" ? "路线收好啦，点开一起看看。":"需求都还在，不用重新回答。")).font(.subheadline).foregroundStyle(Palette.muted)}}
                if task.status=="generating",let progress=task.progress {Text(uiText(progress.detail)).font(.headline);PlanningSteps(progress:progress);Text(uiText("按实际查询结果更新进度；班次核对期间无法准确预估剩余时间。")).font(.caption).foregroundStyle(Palette.muted)}
                if let message=task.message{Text(uiText(message)).font(.subheadline).textSelection(.enabled)}
                if task.status=="completed"{PrimaryButton(title:"看看我们的路线"){action("open")}}
                if task.status=="review"{PrimaryButton(title:"查看待确认的草案"){action("review")};Button(uiText("重新核对地点与路线")){store.startGeneratingPendingTrip()}.frame(minHeight:44);Button(uiText("修改旅行需求")){action("edit")}.frame(minHeight:44)}
                if task.status=="failed" || task.status=="draft"{PrimaryButton(title:"重新准备"){store.startGeneratingPendingTrip()};Button(uiText("修改旅行需求")){action("edit")}.frame(minHeight:44)}
                if store.canCancelPlanning{Button(uiText("暂停生成"),role:.destructive){store.cancelPendingGeneration()}.frame(minHeight:44)}
                if task.status=="generating",task.progress?.phase == .saving{Text(uiText("正在保存最后结果，很快就能在原来的旅行卡上查看。")).font(.caption).foregroundStyle(Palette.muted)}
            }
        }.padding(24)}.background(Palette.cream).navigationTitle(uiText("这次的小旅行")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("关闭")){action("")}}}.tint(Palette.green)
    }
}

enum TripCreationDate {
    static func formatted(_ raw:String,timeZone:TimeZone = .current)->String? {
        let parser=ISO8601DateFormatter();parser.formatOptions=[.withInternetDateTime,.withFractionalSeconds]
        var date=parser.date(from:raw)
        if date==nil{parser.formatOptions=[.withInternetDateTime];date=parser.date(from:raw)}
        guard let date else{return nil}
        let formatter=DateFormatter();formatter.locale=Locale(identifier:"en_US_POSIX");formatter.calendar=Calendar(identifier:.gregorian);formatter.timeZone=timeZone;formatter.dateFormat="yyyy-MM-dd HH:mm"
        return formatter.string(from:date)
    }
}
struct TripNameEditor:View {
    @Environment(\.locale) private var interfaceLocale

    let trip:Trip
    @EnvironmentObject private var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @State private var title:String
    @State private var problem:String?
    init(trip:Trip){self.trip=trip;_title=State(initialValue:trip.title)}
    var body:some View {
        let _=interfaceLocale
        NavigationStack{Form{
            TextField(uiText("旅行名称"),text:$title).accessibilityIdentifier("trip-name-field")
            Text(uiText("最多 40 个字，只修改名称，地点和日期保持不变。")).font(.caption).foregroundStyle(Palette.muted)
            if let problem{Text(uiText(problem)).font(.caption).foregroundStyle(.red)}
            Button(uiText("保存名称")){Task{problem=nil;if await store.renameTrip(trip,to:title){dismiss()}else{problem=store.errorMessage ?? store.language.text("名稱未儲存，請重試。")}}}
                .disabled(store.busy || title.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || title.count>40)
                .accessibilityIdentifier("trip-name-save")
        }.navigationTitle(uiText("给旅行起个名字")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("取消")){dismiss()}.disabled(store.busy)}}
        .interactiveDismissDisabled(store.busy)
    }
}
struct TripListRow:View {
    @Environment(\.locale) private var interfaceLocale

    let trip:Trip
    var featured=false
    @EnvironmentObject private var store:AppStore
    private var subtitle:String {store.language.text("{0} · {1} 天 · {2} 个地点",trip.profile.date,String(trip.days.count),String(trip.days.reduce(0){$0+$1.stops.count}))}
    private var created:String {TripCreationDate.formatted(trip.createdAt).map{store.language.text("建立於 {0}",$0)} ?? store.language.text("建立日期未知")}
    private var highlights:String {(trip.days.first?.stops ?? []).prefix(2).map{$0.name}.joined(separator:" · ")}
    var body:some View {
        let _=interfaceLocale
        HStack(alignment:.top,spacing:14){
            VStack(spacing:4){
                Text(uiText(String(trip.profile.date.dropFirst(5)).replacingOccurrences(of:"-",with:"/"))).font(.headline.monospacedDigit())
                Text(uiText(store.language.text("{0} 天",String(trip.days.count)))).font(.caption.bold())
                Text(uiText(String(trip.profile.date.prefix(4)))).font(.caption2)
            }.foregroundStyle(Palette.green).frame(width:76).frame(minHeight:86).background(Palette.mint,in:RoundedRectangle(cornerRadius:16))
            VStack(alignment:.leading,spacing:7){
                Text(uiText(trip.title)).font(.headline)
                Label(uiText(subtitle),systemImage:"calendar").font(.subheadline).foregroundStyle(Palette.muted)
                Label(uiText(created),systemImage:"clock").font(.caption).foregroundStyle(Palette.muted).accessibilityIdentifier("trip-created-"+(trip.id ?? "draft"))
                Text(uiText(highlights)).font(.caption).foregroundStyle(Palette.muted).lineLimit(1)
                if trip.mode != "live"{Text(uiText("示例路线")).font(.caption2).foregroundStyle(Palette.green)}
            }.frame(maxWidth:.infinity,alignment:.leading)
            Image(systemName:"chevron.right").font(.caption.bold()).foregroundStyle(Palette.muted).padding(.top,5)
        }.padding(16).background(.white,in:RoundedRectangle(cornerRadius:22))
    }
}
struct SavedTripWorkspace:View {
    @Environment(\.locale) private var interfaceLocale

    let trip:Trip
    var onNavigate:()->Void
    var onDelete:()->Void
    @EnvironmentObject private var store:AppStore
    @State private var section="map"
    @State private var hasScheduleDraft=false
    @State private var savingSchedule=false
    @State private var initializedTripID=""
    @State private var leaving=false
    @Environment(\.dismiss) private var dismiss
    @State private var renaming=false
    @State private var companionRequest:TripCompanionRequest?
    @State private var adjustingDay=false
    @State private var replacingStop=false
    @State private var checkingWorld=false
    @State private var worldAction:WorldSuggestion?
    var body:some View {
        let _=interfaceLocale
        VStack(spacing:0) {
            HStack{Text(uiText("\(trip.profile.date) · \(trip.days.count) 天")).font(.caption).foregroundStyle(Palette.muted);Spacer();Text(uiText("\(trip.profile.travelers ?? 1) 人同行")).font(.caption).foregroundStyle(Palette.muted)}.padding(.horizontal,20).padding(.top,4)
            Picker(uiText("旅行工作台"),selection:$section){Text(uiText("地图")).tag("map");Text(uiText(hasScheduleDraft ? "日程 · 未保存":"日程")).tag("details")}.pickerStyle(.segmented).padding(.horizontal,20).padding(.vertical,10).accessibilityIdentifier("trip-workspace-section").disabled(savingSchedule)
                .accessibilityRepresentation {HStack {Button(uiText("地图")){section="map"}.accessibilityIdentifier("trip-workspace-map").accessibilityAddTraits(section=="map" ? .isSelected:[]);Button(uiText(hasScheduleDraft ? "日程，未保存":"日程")){section="details"}.accessibilityIdentifier("trip-workspace-details").accessibilityAddTraits(section=="details" ? .isSelected:[])}}

            TripDayPicker(trip:trip).padding(.horizontal,20).padding(.bottom,10).disabled(savingSchedule)
            if hasScheduleDraft && section=="map" {
                HStack{Text(uiText("地图显示已保存的安排")).font(.caption);Spacer();Button(uiText("返回修改预览")){section="details"}.font(.caption.bold())}.padding(.horizontal,20).padding(.vertical,8).background(Palette.mint)
            }
            KokoTripActions(trip:trip,dayIndex:store.dayIndex,disabled:hasScheduleDraft || savingSchedule){intent in
                if intent == .checkPlan{checkingWorld=true;return}
                if intent == .replace{replacingStop=true;return}
                companionRequest=TripCompanionRequest(text:TripCompanionContext.request(trip:trip,dayIndex:store.dayIndex,intent:intent),autoSend:intent != nil)
            }.padding(.horizontal,20).padding(.bottom,8)
            ZStack {
                HomeView(showsDayPicker:false,sharedCompanion:true).opacity(section=="map" ? 1:0).allowsHitTesting(section=="map").accessibilityHidden(section != "map")
                SavedTripOverview(trip:trip,onEdit:openEditor,onMap:{section="map"},onNavigate:onNavigate,onDelete:onDelete,showsDayPicker:false,sharedCompanion:true,onCompanion:{text in companionRequest=TripCompanionRequest(text:text,autoSend:false)},onDraftChange:{hasScheduleDraft=$0},onSavingChange:{savingSchedule=$0}).opacity(section=="details" ? 1:0).allowsHitTesting(section=="details").accessibilityHidden(section != "details")
            }

        }.background(Palette.cream).navigationTitle(uiText(trip.title)).navigationBarTitleDisplayMode(.inline)
        .navigationBarBackButtonHidden(true)
        .toolbar{ToolbarItem(placement:.topBarLeading){Button{if hasScheduleDraft{leaving=true}else{dismiss()}}label:{Image(systemName:"chevron.left").frame(width:44,height:44)}.accessibilityLabel(uiText("返回我的行程")).disabled(savingSchedule)}}
        .alert(uiText("还有未保存的调整"),isPresented:$leaving){Button(uiText("继续调整"),role:.cancel){section="details"};Button(uiText("放弃修改并返回"),role:.destructive){dismiss()}}message:{Text(uiText("已保存的行程不会改变。你也可以留在日程页，保存这次调整。"))}
        .toolbar{ToolbarItem(placement:.topBarTrailing){Menu{
            Button(uiText("调整当天安排"),systemImage:"slider.horizontal.3"){adjustingDay=true}
            Button(uiText("完整时间与住宿设置"),systemImage:"calendar",action:openEditor)
            Button(uiText("修改名称"),systemImage:"pencil"){renaming=true}
            Button(uiText("删除行程"),systemImage:"trash",role:.destructive,action:onDelete)
        }label:{Image(systemName:"ellipsis").frame(width:44,height:36)}.accessibilityLabel(uiText("行程更多操作")).disabled(store.busy || hasScheduleDraft)}}
        .sheet(isPresented:$checkingWorld,onDismiss:{
            guard let action=worldAction else{return};worldAction=nil
            guard trip.days.indices.contains(action.dayIndex) else{return};store.dayIndex=action.dayIndex
            if action.action=="review" {companionRequest=TripCompanionRequest(text:TripCompanionContext.checkRequest(trip:trip,suggestion:action),autoSend:true)}
            else if action.action=="replace" {replacingStop=true} else {adjustingDay=true}
        }){WorldModelView(trip:trip){action in worldAction=action;checkingWorld=false}.id((trip.id ?? "")+String(trip.revision ?? 0))}
        .sheet(isPresented:$renaming){TripNameEditor(trip:trip)}
        .sheet(isPresented:$adjustingDay){TripAdjustmentView(original:trip,dayIndex:store.dayIndex)}
        .sheet(isPresented:$replacingStop){TripAdjustmentView(original:trip,dayIndex:store.dayIndex,focusReplacement:true)}
        .sheet(item:$companionRequest){request in AgentView(context:request.text,autoSend:request.autoSend)}
        .onAppear{let key=trip.id ?? "unsaved";if initializedTripID != key{initializedTripID=key;if let id=trip.id{UserDefaults.standard.set(id,forKey:"lastOpenedTripID")};store.trip=trip;store.dayIndex=0;store.selectedStopID=nil}}
        .sensoryFeedback(.selection,trigger:section)
        .onChange(of:trip.revision){_,_ in store.trip=trip}
    }
    private func openEditor(){store.trip=trip;store.proposalIsSuggestion=false;store.proposal=trip}
}

struct SavedTripOverview:View {
    let trip:Trip
    var onEdit:()->Void;var onMap:()->Void;var onNavigate:()->Void;var onDelete:()->Void
    var showsDayPicker=true
    var sharedCompanion=false
    var onCompanion:((String)->Void)?=nil
    var onDraftChange:(Bool)->Void = {_ in}
    var onSavingChange:(Bool)->Void = {_ in}
    var body:some View {InlineTripSchedule(trip:trip,onMap:onMap,onAdvanced:onEdit,showsDayPicker:showsDayPicker,sharedCompanion:sharedCompanion,onCompanion:onCompanion,onDraftChange:onDraftChange,onSavingChange:onSavingChange)}
}

struct TripDayPicker:View {
    let trip:Trip
    @EnvironmentObject private var store:AppStore
    var body:some View {
        MapDaySelector(dates:trip.days.map(\.date),selection:Binding(get:{store.dayIndex},set:{index in
            guard store.dayIndex != index else{return}
            UISelectionFeedbackGenerator().selectionChanged();store.dayIndex=index;store.selectedStopID=nil
        }),language:store.language,identifierPrefix:"trip-day-")
    }
}
struct TripDraftEditor:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var chat=PetChatStore()
    @State var fields:[String:String]
    @State private var problem:String?
    @State private var saving=false
    var body:some View {
        let _=interfaceLocale
        NavigationStack{
            Form{
                Section(uiText("这次旅行")){
                    field("destination","目的地")
                    DatePicker("出发日期",selection:Binding(get:{dateValue},set:{fields["date"]=TripProfile.dateString($0)}),displayedComponents:.date)
                    Stepper("旅行天数：\(fields["days"] ?? "1") 天",value:Binding(get:{Int(fields["days"] ?? "1") ?? 1},set:{fields["days"]=String($0)}),in:1...15)
                    field("companions","同行伙伴")
                }
                Section(uiText("兴趣与节奏")){
                    field("interests","这次想玩什么")
                    field("pace","旅行节奏与步数")
                }
                Section(uiText("预算与补充")){
                    field("budget","预算金额")
                    field("notes","补充要求")
                    ForEach(travelExtraFields,id:\.0){key,label in field(key,label)}
                    Picker(uiText("预算范围"),selection:Binding(get:{fields["budgetBasis"] ?? ""},set:{fields["budgetBasis"]=$0})){Text(uiText("请选择")).tag("");ForEach(TravelRequirements.budgetBases,id:\.0){Text(uiText($0.1)).tag($0.0)}}
                }
                Section{Text(uiText("修改只用于这次旅行，不会更改长期记忆。当前支持悉尼 1–15 天，预算按确认的范围计算；路线费用仅支持 AUD。")).font(.caption).foregroundStyle(Palette.muted)}
                if let problem{Section{Text(uiText(problem)).foregroundStyle(.red)}}
            }.navigationTitle(uiText("编辑旅行偏好")).navigationBarTitleDisplayMode(.inline)
            .toolbar{ToolbarItem(placement:.topBarLeading){Button(uiText("取消")){dismiss()}.disabled(saving)};ToolbarItem(placement:.topBarTrailing){Button(uiText(saving ? "保存中…":"保存")){Task{await save()}}.bold().disabled(saving)}}
        }.interactiveDismissDisabled(saving)
    }
    private var dateValue:Date {
        let format=DateFormatter();format.locale=Locale(identifier:"en_US_POSIX");format.timeZone=TimeZone(identifier:"Australia/Sydney");format.dateFormat="yyyy-MM-dd"
        return format.date(from:fields["date"] ?? "") ?? Date()
    }
    private func field(_ key:String,_ label:String)->some View {
        VStack(alignment:.leading,spacing:8){Text(uiText(label)).font(.caption).foregroundStyle(Palette.muted);TextField(uiText(""),text:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0}),axis:.vertical).accessibilityLabel(uiText(label)).keyboardType(key=="budget" ? .decimalPad:.default)}.padding(.vertical,4)
    }
    private func save() async {
        guard !saving else{return};saving=true;defer{saving=false}
        do{let profile=try TravelRequirements.profile(fields,personal:store.pendingPersonalProfile);await chat.load(scope:"draft");guard chat.error==nil else{problem=chat.error;return};await chat.update(fields);guard chat.error==nil else{problem=chat.error;return};store.completePetPreferences(profile,fields:fields);dismiss()}
        catch{problem=error.localizedDescription}
    }
}

// Use the actual calendar date; a trip outside today must explicitly choose a day.
func savedTripNavigationDay(_ trip:Trip,today:String=TripProfile.dateString(Date()))->Int? {
    if let index=trip.days.firstIndex(where:{$0.date==today}){return index}
    return trip.days.count==1 ? 0:nil
}
struct SavedTripNavigationView:View {
    @Environment(\.locale) private var interfaceLocale

    let trip:Trip
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @State private var dayIndex:Int?
    init(trip:Trip){self.trip=trip;_dayIndex=State(initialValue:savedTripNavigationDay(trip))}
    var body:some View {
        let _=interfaceLocale
        Group {
            if store.mapProvider == .apple,trip.mode != "live",let index=dayIndex {
                if trip.days[index].hasTransit {AppleTripWalkingView(trip:trip,initialDay:index)}else{WalkingNavigationView(day:trip.days[index],tripID:trip.id ?? "draft",autoStart:false,apple:true)}
            } else if let index=dayIndex,trip.days.indices.contains(index) {
                if trip.mode=="live" {
                    NavigationStack{VStack(spacing:20){Text(uiText("這份行程使用 Google 地點資料，目前 App 內逐站步行導航僅支援開源行程。請從查看地圖使用 Google 交通路線規劃。"));Button(uiText("關閉")){dismiss()}}.padding().navigationTitle(uiText("開始導航"))}
                } else {
                    WalkingNavigationView(day:trip.days[index],tripID:trip.id ?? "draft",autoStart:true)
                }
            } else {
                NavigationStack{List{Section(uiText("今天不在旅程日期內，請選擇要導航的行程")){ForEach(Array(trip.days.enumerated()),id:\.offset){i,day in
                    Button{select(i)}label:{VStack(alignment:.leading,spacing:6){Text(uiText("第 \(i+1) 天 · \(day.date)")).bold();Text(uiText("\(day.start.name) → \(day.stops.count) 個站點 → \(day.end.name)")).font(.caption).foregroundStyle(Palette.muted)}}
                }}}.navigationTitle(uiText("選擇導航日期")).toolbar{Button(uiText("取消")){dismiss()}}}
            }
        }.onAppear{if store.mapProvider != .apple,let dayIndex{select(dayIndex)}}
    }
    private func select(_ index:Int){store.trip=trip;store.dayIndex=index;dayIndex=index}
}

struct WeatherCard: View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store: AppStore
    var weather: WeatherInfo
    var compact = false
    var profile:TripProfile? = nil
    @State private var refreshed:WeatherInfo?
    @State private var refreshing=false
    @State private var refreshError:String?
    private var forecast:WeatherInfo {refreshed ?? weather}
    var body: some View {
        let _=interfaceLocale
        VStack(alignment:.leading,spacing:12) {
            HStack {
                Label(uiText("悉尼天气"),systemImage:"calendar").font(.subheadline.bold())
                Spacer()
                Button { Task {
                    refreshing=true;refreshError=nil
                    defer {refreshing=false}
                    do {
                        refreshed=try await store.api("weather",method:"POST",body:JSONEncoder().encode(profile ?? store.trip?.profile ?? store.profile))
                    } catch {refreshError=store.language.text("天氣暫時無法更新")}
                } } label: {
                    if refreshing { ProgressView() } else { Image(systemName:"arrow.clockwise") }
                }.disabled(refreshing).accessibilityLabel(uiText("刷新天气"))
            }
            if let days=forecast.days, !days.isEmpty {
                ScrollView(.horizontal,showsIndicators:false) {
                    HStack(spacing:8) {
                        ForEach(days.filter { !compact || $0.date == store.currentDay?.date },id: \.date) { day in
                            VStack(spacing:8) {
                                Text(String(day.date.suffix(5)).replacingOccurrences(of:"-",with:"/")).font(.caption.bold()).monospacedDigit()
                                Image(systemName:day.symbol).symbolRenderingMode(.multicolor).font(.system(size:27)).frame(width:42,height:42).background((day.available ? Color.blue:Color.gray).opacity(0.10),in:Circle())
                                if let temperature=day.temperatureLabel {
                                    Text(temperature).font(.caption.bold()).monospacedDigit().lineLimit(1).minimumScaleFactor(0.8)
                                    if let rain=day.precipitationProbability {
                                        Label("\(Int(rain.rounded()))%",systemImage:"drop.fill").font(.caption2).foregroundStyle(.blue)
                                    }
                                } else {
                                    Text(store.language.text(day.stale == true ? "待更新":"待預報")).font(.caption).foregroundStyle(Palette.muted)
                                }
                            }
                            .frame(width:80).frame(minHeight:112).padding(.vertical,8)
                            .background(Palette.cream,in:RoundedRectangle(cornerRadius:14))
                            .accessibilityElement(children:.ignore)
                            .accessibilityLabel(day.date+", "+day.summary)
                            .accessibilityIdentifier("weather-day-"+day.date)
                        }
                    }
                }.accessibilityIdentifier("weather-days")
                if !compact {
                    DisclosureGroup(store.language.text("天氣詳情")) {
                        VStack(alignment:.leading,spacing:6) {
                            ForEach(days,id:\.date){Text(uiText($0.date+" · "+$0.summary)).font(.caption)}
                            Text(uiText(forecast.source)).font(.caption2)
                            if let date=forecast.fetchedAt{Text(uiText("查询于 "+date)).font(.caption2)}
                        }.foregroundStyle(Palette.muted)
                    }.font(.caption).foregroundStyle(Palette.muted)
                }
            } else {Text(uiText(forecast.summary)).font(.caption).foregroundStyle(Palette.muted)}
            Text(uiText(forecast.source)).font(.caption2).foregroundStyle(Palette.muted)
            if let refreshError{Text(refreshError).font(.caption).foregroundStyle(Palette.muted)}
        }.padding(14).background(.white,in:RoundedRectangle(cornerRadius:18))
    }
}

struct PreparationTrail: View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store: AppStore
    var openRoute: () -> Void
    @State var expanded = false
    var count: Int { ["route","weather","bag"].filter {store.prepared($0)}.count }
    var body: some View {
        let _=interfaceLocale
        VStack(alignment:.leading,spacing:10) {
            Button { withAnimation { expanded.toggle() } } label: { HStack {
                Label(uiText(count==3 ? "出发准备小达人" : "出发前的 3 个小任务"),systemImage:count==3 ? "medal.fill" : "flag.checkered").font(.subheadline.bold()).foregroundStyle(Palette.green)
                Spacer();Text(uiText("\(count)/3")).font(.caption.bold()).foregroundStyle(Palette.muted)
                Image(systemName:expanded ? "chevron.up" : "chevron.down").font(.caption).foregroundStyle(Palette.green)
            }}.buttonStyle(.plain).accessibilityLabel(uiText("出发准备，\(count)项已完成，点按展开或收起"))
            if expanded { HStack(spacing:8) {
                task("route","看路线","map.fill",action:openRoute)
                task("weather","看天气","cloud.sun.fill") { Task { await store.refreshWeather() } }
                task("bag","行李备好","backpack.fill") {store.markPrepared("bag",undo:store.prepared("bag"))}
            }
            Text(uiText(count==3 ? "准备得很棒！按自己的节奏出发。" : "随时回来继续，旅行没有断签惩罚。 ")).font(.caption2).foregroundStyle(Palette.muted)
            }
        }.padding(14).background(.white,in:RoundedRectangle(cornerRadius:20))
    }
    func task(_ key:String,_ title:String,_ icon:String,action:@escaping()->Void) -> some View {
        Button(action:action) {
            VStack(spacing:5) {
                Image(systemName:store.prepared(key) ? "checkmark.circle.fill" : icon).font(.title3)
                Text(uiText(title)).font(.caption2.bold())
            }.frame(maxWidth:.infinity).padding(.vertical,10).foregroundStyle(store.prepared(key) ? .white : Palette.green).background(store.prepared(key) ? Palette.green : Palette.mint,in:RoundedRectangle(cornerRadius:14))
        }.buttonStyle(BuddyPressStyle()).accessibilityLabel(uiText(title + (store.prepared(key) ? "，已完成" : "，待完成")))
    }
}

/// Suggestions stay local to this sheet until the user accepts the validated result.
struct TripAdjustmentView:View {
    @Environment(\.locale) private var interfaceLocale

    let original:Trip
    let dayIndex:Int
    var focusReplacement=false
    @EnvironmentObject private var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model=ScheduleEditorModel()
    @State private var target:String?
    @State private var notice:String?
    @State private var requested=false
    @State private var discoveringFood=false
    @State private var discoveringReplacement=false
    private var day:ScheduleDay? {guard let state=model.state,state.days.indices.contains(dayIndex) else{return nil};return state.days[dayIndex]}
    private var choices:[ScheduleCandidate] {
        guard let state=model.state else{return []}
        let used=Set(state.days.flatMap(\.activities).compactMap(\.placeId))
        return state.candidates.filter{!used.contains($0.id) && !$0.importedPoi}
    }
    private var proposedTrip:Trip? {model.trip.map{TripAdjustmentMerge.preservingOtherDays($0,original:original,dayIndex:dayIndex)}}
    private var canAdopt:Bool {requested && !model.busy && !model.dirty && model.error==nil && model.trip != nil && model.state?.issues.contains(where:{$0.blocking && ($0.day==dayIndex || $0.day == -1)})==false}
    var body:some View {
        let _=interfaceLocale
        NavigationStack{ScrollView{VStack(alignment:.leading,spacing:16){
            HStack{PetAvatar(mood:model.busy ? .thinking:requested ? .ready:.curious).frame(width:100,height:110);Text(uiText(focusReplacement ? "想换哪一站呀？选中后一起找个新地方。先看路线变化，喜欢再保存～":"我们只改第 \(dayIndex+1) 天。先看看建议，喜欢再收进正式行程～")).font(.headline)}
            if original.mode == "live" {Text(uiText("Google 来源行程请使用匹配的道路服务调整。")).foregroundStyle(Palette.muted)}
            else {
                if !focusReplacement{HStack{Button(uiText("再轻松一点")){lighten()}.disabled((day?.activities.count ?? 0)<2);Button(uiText("多一点美食")){addFood()}.disabled(!choices.contains{$0.category=="美食"});Button(uiText("加一段休息")){addRest()}}
                    .buttonStyle(.bordered).disabled(model.busy || requested)}
                if !focusReplacement,!requested,!choices.contains(where:{$0.category=="美食"}) {
                    Text(uiText("当前没有未安排的餐饮候选。可以直接找一家餐厅，加入后先预览，不会立即保存。")).font(.caption).foregroundStyle(Palette.muted)
                    Button(uiText("寻找餐厅并加入当天"),systemImage:"magnifyingglass"){discoveringFood=true}.disabled(model.busy)
                }
                if !requested,(day?.activities.count ?? 0)<2{Text(uiText("当天已很精简，可在编辑行程中调整停留和休息。")).font(.caption).foregroundStyle(Palette.muted)}
                if !requested,let day {
                    Text(uiText("换掉某一站")).font(.headline)
                    ForEach(day.activities.filter{$0.locked != true && !original.profile.mustVisit.contains($0.placeId ?? "")}){a in
                        Button{target=a.id}label:{Label(a.name,systemImage:target==a.id ? "checkmark.circle.fill":"circle")}.buttonStyle(.bordered)
                    }
                    if target != nil {
                        Button(uiText("搜索替代地点"),systemImage:"magnifyingglass"){discoveringReplacement=true}.buttonStyle(.borderedProminent).disabled(model.busy).accessibilityIdentifier("replace-search-place")
                        if !focusReplacement{Text(uiText("已有候选")).font(.subheadline.bold());ForEach(choices){candidate in Button(candidate.name){replace(with:candidate)}.buttonStyle(.bordered)}}
                    }
                }
                if model.busy{ProgressView("正在校验交通、步数和预算…")}
                if let error=model.error{Text(uiText(error)).foregroundStyle(.red);Button(uiText("重试校验")){model.recompute()}}
                if let notice{Text(uiText(notice)).font(.subheadline).foregroundStyle(Palette.muted)}
                if requested,let proposed=proposedTrip,!model.busy,model.error==nil {
                    let before=original.days[dayIndex],after=proposed.days[dayIndex]
                    VStack(alignment:.leading,spacing:10){Text(uiText("建议变化 · 尚未保存")).font(.headline)
                        Text(uiText("原来："+before.stops.map(\.name).joined(separator:" → ")))
                        Text(uiText("建议："+after.stops.map(\.name).joined(separator:" → ")))
                        Text(uiText("预计步数：\(before.estimatedSteps) → \(after.estimatedSteps)"))
                        Text(uiText("预计返回：\(before.finishTime) → \(after.finishTime)"))
                        Text(uiText("每人活动估算：A$\(Int(before.estimatedCost)) → A$\(Int(after.estimatedCost))"))
                        if before.stops.contains(where:{$0.id=="rocks" && $0.cost>0}),after.stops.contains(where:{$0.id=="rocks" && $0.cost==0}) {
                            Text(uiText("岩石区原先包含餐饮预留。调整后街区游览不计餐费，餐饮按用餐站另计，避免重复预留。")).font(.caption).foregroundStyle(Palette.muted)
                        }
                        Text(uiText("其他日期保留原安排。搜索加入的餐饮费用按每餐预留金额估算，实际价格、营业时间和排队仍需确认。")).font(.caption).foregroundStyle(Palette.muted)
                    }.font(.subheadline).padding(16).background(Palette.mint,in:RoundedRectangle(cornerRadius:18))
                    ForEach((model.state?.issues ?? []).filter{$0.day==dayIndex || $0.day == -1}){issue in Text(uiText(store.language.display(issue.message))).font(.caption).foregroundStyle(issue.blocking ? .red:.orange)}
                    Button(uiText("换一种调整")){model.restore();requested=false;target=nil;notice=nil}
                }
                if let error=store.adjustmentError{Text(uiText(error)).foregroundStyle(.red)}
            }
        }.padding(20)}.background(Palette.cream).navigationTitle(uiText("调整这一天")).navigationBarTitleDisplayMode(.inline)
        .toolbar{Button(uiText("保留原行程")){dismiss()}.disabled(store.busy)}
        .safeAreaInset(edge:.bottom){if requested{PrimaryButton(title:"采用调整",symbol:"checkmark"){Task{if let value=proposedTrip,await store.saveAdjustment(value,original:original,dayIndex:dayIndex){dismiss()}}}.disabled(!canAdopt || store.busy).padding(20).background(Palette.cream)}}.tint(Palette.green)}
        .sheet(isPresented:$discoveringReplacement){PoiDiscoveryView(addingToDay:true,replacingPlace:true){candidate in
            discoveringReplacement=false
            guard model.state?.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id})==false else{notice="这个地点已经在行程里啦，换一个还没安排的地方吧。";return}
            replace(with:candidate)
        }}
        .sheet(isPresented:$discoveringFood){PoiDiscoveryView(addingToDay:true){candidate in
            discoveringFood=false
            guard model.state?.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id})==false else{notice="这个地点已经在行程里啦，可以在编辑行程中移动到当天。";return}
            requested=true;model.importAndAdd(candidate)
        }}
        .task{store.adjustmentError=nil;model.prepare(original);model.dayIndex=dayIndex;await model.loadCatalog()}.onDisappear{model.stop()}
    }
    private func addRest() {
        guard let day else{return};let last=day.activities.last
        let rest=ScheduleActivity(id:UUID().uuidString,kind:"rest",name:"附近休息 · 位置待确认",lat:last?.lat ?? day.end.lat,lng:last?.lng ?? day.end.lng,duration:30,cost:0,confirmed:false,locked:false,openMinute:0,closeMinute:1440,arrival:"",departure:"",scheduledStart:0,travelMinutes:0)
        let start=(last?.scheduledStart ?? day.startMinute)+(last?.duration ?? 0)+15
        requested=true;model.edit{state in
            for i in state.days[dayIndex].activities.indices {if state.days[dayIndex].activities[i].fixedStart == nil {state.days[dayIndex].activities[i].fixedStart=state.days[dayIndex].activities[i].scheduledStart}}
            var value=rest;value.fixedStart=start;state.days[dayIndex].activities.append(value)
        }
        notice="保留原活动开始时间，新增30分钟休息；调整试算的交通仍为估算，位置和营业情况需确认。"
    }
    private func lighten(){
        guard let day,let a=day.activities.last(where:{$0.locked != true && !original.profile.mustVisit.contains($0.placeId ?? "")}),day.activities.count>1 else{notice="今天安排已经很少啦，可以在编辑器里增加休息或缩短停留。";return}
        requested=true;model.edit{$0.days[dayIndex].activities.removeAll{$0.id==a.id}}
    }
    private func addFood(){
        guard let candidate=choices.first(where:{$0.category=="美食"}) else{notice="目前没有未安排的餐饮候选地点。可以到探索地图收藏地点，再手动加入。";return}
        requested=true;model.add(candidate)
    }
    private func replace(with candidate:ScheduleCandidate){
        guard let target,let a=day?.activities.first(where:{$0.id==target}) else{return}
        var replacement=a;replacement.id=UUID().uuidString;replacement.placeId=candidate.id;replacement.name=candidate.name;replacement.lat=candidate.lat;replacement.lng=candidate.lng;replacement.duration=candidate.visitMinutes;replacement.kind=candidate.activityKind;replacement.cost=candidate.activityKind=="meal" ? (model.state?.settings.mealCost ?? 25):candidate.cost;replacement.fixedDuration=nil;replacement.indoor=candidate.indoor;replacement.hoursVerified=false;replacement.openMinute=nil;replacement.closeMinute=nil;replacement.queueMinutes=nil;replacement.queueSource=nil;replacement.confirmed=false
        requested=true;model.edit{state in
            if !state.candidates.contains(where:{$0.id==candidate.id}){state.candidates.append(candidate)}
            if let i=state.days[dayIndex].activities.firstIndex(where:{$0.id==target}){state.days[dayIndex].activities[i]=replacement}
        }
    }
}

// Expose capability before entering date selection or a non-functional navigator.
enum TripNavigationAvailability {
    static func canNavigate(configured:Bool?,mode:String)->Bool {configured == true && mode != "live"}

}
struct TripNavigationAction:View {
    @Environment(\.locale) private var interfaceLocale

    let day:DayPlan?
    let mode:String
    var action:()->Void
    @EnvironmentObject private var store:AppStore
    var body:some View {
        let _=interfaceLocale
        VStack(alignment:.leading,spacing:7){
            if day?.hasWalkingJourney != true {
                Text(uiText("当天为自由安排，添加地点后即可查看前往路线。")).font(.caption).foregroundStyle(Palette.muted)
            } else if store.mapProvider == .apple,mode != "live" {
                PrimaryButton(title:day?.hasTransit == true ? "查看乘车与接驳":"开始步行导航",symbol:day?.hasTransit == true ? "tram.fill":"figure.walk",action:action).accessibilityIdentifier("trip-walking-route")
            } else if TripNavigationAvailability.canNavigate(configured:store.health?.navigationConfigured,mode:mode) {
                PrimaryButton(title:"开始导航",symbol:"location.fill",action:action)
            } else if mode != "live" {
                PrimaryButton(title:"用 Apple 查看 App 内步行路线",symbol:"figure.walk"){store.mapProvider = .apple;action()}
            } else {
                Text(uiText("此行程使用 Google 地点资料，请切换到 Google 底图查看匹配的路线。")).font(.caption).foregroundStyle(Palette.muted)
            }
        }
    }
}

struct TripCompanionRequest:Identifiable {let id=UUID();let text:String;let autoSend:Bool}
enum TripCompanionIntent:String,CaseIterable,Identifiable {
    case checkPlan,tired,replace,shortOnTime
    var id:String {rawValue}
    var label:String {switch self{case .checkPlan:return "帮我检查";case .tired:return "累了想歇歇";case .replace:return "换个地方";case .shortOnTime:return "时间不够了"}}
    var message:String {switch self {
        case .checkPlan:return "请检查这份已保存行程中的风险，区分已知与未知。"
        case .tired:return "这次有点累了，想减少步行、留出休息。请给我具体调整建议。"
        case .replace:return "我想换一个地方。请先问我想换哪一站，再给出顺路的真实候选。"
        case .shortOnTime:return "这一天时间不够了。请先问我还剩多久，再建议保留哪些安排。"
    }}
}
enum TripHomeFocus {
    static func pick(_ trips:[Trip],lastOpenedID:String?,today:String)->Trip? {
        let upcoming=trips.filter{($0.days.last?.date ?? $0.profile.date)>=today}
        let current=upcoming.filter{$0.profile.date<=today}
        if let last=current.first(where:{$0.id==lastOpenedID}){return last}
        if let first=current.sorted(by:{$0.profile.date>$1.profile.date}).first{return first}
        return upcoming.sorted{$0.profile.date<$1.profile.date}.first
    }
}
enum TripCompanionContext {
    static func checkRequest(trip:Trip,suggestion:WorldSuggestion)->String {
        guard trip.days.indices.contains(suggestion.dayIndex) else{return suggestion.detail}
        let day=trip.days[suggestion.dayIndex]
        let places=day.stops.prefix(8).map { stop in
            stop.name+"（"+stop.costLabel+"，"+stop.indoorLabel+"，"+(stop.hoursVerified ? "营业时间有记录":"营业时间待确认")+"）"
        }.joined(separator:"；")
        return request(trip:trip,dayIndex:suggestion.dayIndex,intent:nil)+" 检查发现："+suggestion.detail+" 已保存的当天地点："+places+"。我现在想知道如何确认这些未知资料，请针对上述地点给出一到两个具体的确认步骤，不要重复询问已知旅行资料，不要重新生成行程。区分已保存信息与待核实信息；没有实际查询结果时，不要声称已查到天气、票价或营业时间。"
    }
    static func speech(_ trip:Trip)->String {
        if trip.profile.transportPreference=="lessWalking"{return "这趟尽量少走路。想歇一会儿，随时叫我一起调整。"}
        if trip.profile.rhythm=="轻松随意"{return "这趟按你选的轻松节奏来。喜欢哪里多待一会儿，也没关系。"}
        return "\(trip.days.count) 天的小旅行收好啦。路线、日程和临时调整，都在旅行里。"
    }
    static func request(trip:Trip,dayIndex:Int,intent:TripCompanionIntent?)->String {
        let index=max(0,min(dayIndex,max(0,trip.days.count-1)))
        let date=trip.days.indices.contains(index) ? trip.days[index].date:trip.profile.date
        return "关于「\(trip.title)」第 \(index+1) 天（\(date)）。"+(intent?.message ?? "我想聊聊这一天的安排。")+"仅调整这一天，保留其余日期和已完成活动；先展示建议，确认后再保存。"
    }
}
struct KokoTripActions:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject private var store:AppStore
    let trip:Trip;let dayIndex:Int;var disabled=false
    let choose:(TripCompanionIntent?)->Void
    var body:some View {
        let _=interfaceLocale
        HStack(spacing:6){
            Button{choose(nil)}label:{PetAvatar(mood:store.busy ? .thinking:.listening,cue:store.koalaFeedback,size:36).frame(width:44,height:44)}.buttonStyle(BuddyPressStyle()).accessibilityLabel(uiText("和 Koko 聊这一天")).accessibilityIdentifier("trip-companion-chat")
            ScrollView(.horizontal,showsIndicators:false){HStack(spacing:7){ForEach(TripCompanionIntent.allCases){intent in
                Button{choose(intent)}label:{Text(uiText(intent.label)).font(.caption.weight(.semibold)).padding(.horizontal,12).frame(minHeight:44).background(Palette.mint.opacity(0.6),in:Capsule())}.buttonStyle(BuddyPressStyle()).accessibilityIdentifier("trip-companion-"+intent.rawValue)
            }}}
        }.disabled(disabled || store.busy)
    }
}
/// Library filters use scheduled days, including every day of a multi-day trip.
enum TripLibrary {
    static func dates(_ trip:Trip)->[String] {trip.days.isEmpty ? [trip.profile.date]:trip.days.map(\.date)}
    static func matches(_ trip:Trip,query:String,date:String?)->Bool {
        if let date,!dates(trip).contains(date){return false}
        let needle=query.trimmingCharacters(in:.whitespacesAndNewlines)
        let text=([trip.title,trip.profile.date,trip.createdAt]+dates(trip)+trip.days.flatMap{$0.stops.map(\.name)}).joined(separator:" ")
        return needle.isEmpty || text.localizedStandardContains(needle)
    }
}
struct TripLibraryCalendar:View {
    @Environment(\.locale) private var interfaceLocale

    @Binding var month:Date
    @Binding var selection:String?
    let marked:Set<String>
    let language:AppLanguage
    private var calendar:Calendar {var c=Calendar(identifier:.gregorian);c.firstWeekday=2;c.locale=Locale(identifier:language.rawValue);return c}
    private var first:Date {calendar.date(from:calendar.dateComponents([.year,.month],from:month)) ?? month}
    private var count:Int {calendar.range(of:.day,in:.month,for:first)?.count ?? 30}
    private var offset:Int {(calendar.component(.weekday,from:first)+5)%7}
    private var title:String {let f=DateFormatter();f.locale=Locale(identifier:language.rawValue);f.dateFormat="yyyy MMMM";return f.string(from:first)}
    var body:some View {
        let _=interfaceLocale
        VStack(spacing:6) {
            HStack {
                Button{shift(-1)}label:{Image(systemName:"chevron.left").frame(width:44,height:44)}.accessibilityIdentifier("trip-month-previous").accessibilityLabel(uiText("上個月"))
                Text(uiText(title)).font(.headline).frame(maxWidth:.infinity)
                Button{shift(1)}label:{Image(systemName:"chevron.right").frame(width:44,height:44)}.accessibilityLabel(uiText("下個月"))
            }
            let symbols=calendar.shortStandaloneWeekdaySymbols
            LazyVGrid(columns:Array(repeating:GridItem(.flexible(),spacing:0),count:7),spacing:4) {
                ForEach(0..<7,id:\.self){index in Text(uiText(symbols[(index+1)%7])).font(.caption2).foregroundStyle(Palette.muted)}
                ForEach(0..<((offset+count+6)/7)*7,id:\.self){cell in
                    let day=cell-offset+1
                    if day>0 && day<=count,let date=calendar.date(byAdding:.day,value:day-1,to:first) {
                        let key=TripProfile.dateString(date)
                        Button{selection=selection==key ? nil:key}label:{
                            VStack(spacing:3){Text(uiText(String(day))).font(.subheadline.monospacedDigit());Circle().fill(marked.contains(key) ? (selection==key ? Color.white:Palette.green):Color.clear).frame(width:5,height:5)}
                                .frame(maxWidth:.infinity,minHeight:40).foregroundStyle(selection==key ? Color.white:Palette.ink)
                                .background(selection==key ? Palette.green:Color.clear,in:RoundedRectangle(cornerRadius:12))
                        }.buttonStyle(.plain).accessibilityIdentifier("trip-date-"+key).accessibilityLabel(uiText(key)).accessibilityAddTraits(selection==key ? .isSelected:[])
                    }else{Color.clear.frame(height:40)}
                }
            }
        }.padding(10).background(.white,in:RoundedRectangle(cornerRadius:20))
    }
    private func shift(_ amount:Int){month=calendar.date(byAdding:.month,value:amount,to:first) ?? month}
}
