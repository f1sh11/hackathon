import SwiftUI

struct ProposalView:View {
    @EnvironmentObject var store:AppStore
    var body:some View {
        if store.proposal?.mode == "demo" && !store.proposalIsSuggestion {ScheduleEditorView()} else { legacy }
    }
    var legacy:some View {
        NavigationStack {
            ScrollView {
                if let plan=store.proposal {
                    VStack(alignment:.leading,spacing:20) {
                        HStack { Robot(size:65);VStack(alignment:.leading,spacing:6) {Text(store.trip==nil ? "你的路线准备好了" : "看看这次怎么调整").font(.title2.bold());Text("确认之后，才会保存到你的行程").font(.caption).foregroundStyle(Palette.muted)} }
                        Text(plan.explanation).font(.subheadline).padding(18).background(Palette.mint,in:RoundedRectangle(cornerRadius:20))
                        if let old=store.trip {
                            let before=Set(old.days.flatMap(\.stops).map(\.name)),after=Set(plan.days.flatMap(\.stops).map(\.name))
                            VStack(alignment:.leading,spacing:8) { Text("调整对比").font(.headline);Text("新增："+(after.subtracting(before).sorted().joined(separator:"、").isEmpty ? "无" : after.subtracting(before).sorted().joined(separator:"、")));Text("移除："+(before.subtracting(after).sorted().joined(separator:"、").isEmpty ? "无" : before.subtracting(after).sorted().joined(separator:"、")));Text("预计步数：\(old.days.reduce(0){$0+$1.estimatedSteps}) → \(plan.days.reduce(0){$0+$1.estimatedSteps})") }.font(.caption).padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:20))
                        }
                        WeatherCard(weather:plan.weather)
                        ForEach(plan.days) { day in DaySummary(day:day) }
                        VStack(alignment:.leading,spacing:8) {Text("数据与限制").font(.headline);ForEach(plan.warnings,id:\.self){Text("· "+$0).font(.caption).foregroundStyle(Palette.muted)}}
                        DisclosureGroup("查看规划依据与验证记录") {ForEach(plan.trace){trace in VStack(alignment:.leading,spacing:5){Text(trace.title).font(.subheadline.bold());Text(trace.detail).font(.caption);Text(trace.source).font(.caption2).foregroundStyle(Palette.green)}.padding(.vertical,8)}}
                    }.padding(24)
                }
            }.background(Palette.cream).navigationTitle("行程草案").navigationBarTitleDisplayMode(.inline)
            .toolbar {ToolbarItem(placement:.topBarLeading){Button("取消"){store.proposal=nil}.tint(Palette.muted)}}
            .safeAreaInset(edge:.bottom){PrimaryButton(title:"确认并保存",symbol:"checkmark"){Task{await store.acceptProposal()}}.padding(20).background(Palette.cream)}
        }
    }
}
struct DaySummary:View {
    var day:DayPlan
    @EnvironmentObject private var store:AppStore
    var body:some View {
        VStack(alignment:.leading,spacing:16) {
            HStack {Text(day.date).font(.headline);Spacer();Text("每人约 A$\(Int(day.estimatedCost))").font(.subheadline.bold()).foregroundStyle(Palette.green)}
            Label("约 \(day.estimatedSteps) 步 · \(day.finishTime) 返回",systemImage:"figure.walk").font(.caption).foregroundStyle(Palette.muted)
            Text("\(day.start.name) 出发 → \(day.end.name) 返回").font(.caption2).foregroundStyle(Palette.muted)
            if day.freeTime == true {Label("自由安排 · 景点待补充",systemImage:"leaf").font(.subheadline).foregroundStyle(Palette.green)}
            Text(day.buddySummary).font(.caption).foregroundStyle(Palette.green)
            ForEach(Array(day.stops.enumerated()),id:\.element.id){i,s in
                HStack(alignment:.top,spacing:13) {
                    Text("\(i+1)").font(.caption.bold()).frame(width:27,height:27).foregroundStyle(.white).background(Palette.green,in:Circle())
                    VStack(alignment:.leading,spacing:7) {
                        HStack {Text(s.name).font(.subheadline.bold());Spacer();Image(systemName:s.symbol).foregroundStyle(Palette.green)}
                        Text("\(s.arrival)–\(s.departure) · \(s.indoor ? "室内" : "室外") · 每人约 A$\(Int(s.cost))").font(.caption).foregroundStyle(Palette.muted)
                        Text(store.language.display(s.note)).font(.caption2).foregroundStyle(Palette.muted)
                        if !s.hoursVerified {Label("营业时间待核实",systemImage:"exclamationmark.circle").font(.caption2).foregroundStyle(.orange)}
                    }
                }
            }
        }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:24))
    }
}
struct AgentView:View {
    var body:some View { PetChatView() }
}
struct TripDetailView:View {
    @EnvironmentObject var store:AppStore
    var body:some View { NavigationStack { ScrollView {VStack(alignment:.leading,spacing:20){if let t=store.trip {Text(t.explanation).font(.subheadline);WeatherCard(weather:store.currentWeather);ForEach(t.days){DaySummary(day:$0)};ForEach(t.warnings,id:\.self){Text($0).font(.caption).foregroundStyle(Palette.muted)};DisclosureGroup("规划与验证记录"){ForEach(t.trace){r in VStack(alignment:.leading,spacing:6){Text(r.title).font(.headline);Text(r.detail).font(.caption);Text(r.source).font(.caption2).foregroundStyle(Palette.green)}.padding(.vertical,8)}}}}.padding(24)}.background(Palette.cream).navigationTitle("这次的小冒险").onAppear {store.markPrepared("route")}.task(id:store.travelDataSource){await store.refreshWeather()} } }
}
struct TripsView:View {
    @EnvironmentObject var store:AppStore
    @State private var navigationTrip:Trip?
    @State private var selectedTrip:Trip?
    @State private var editingDraft=false
    @State private var deleting:Trip?
    @State private var deletingInProgress=false
    private var deletionPresented:Binding<Bool> {Binding(get:{deleting != nil},set:{if !$0{deleting=nil}})}
    private var selectionPresented:Binding<Bool> {Binding(get:{selectedTrip != nil},set:{if !$0{selectedTrip=nil}})}
    var body:some View {
        NavigationStack { tripList }
            .fullScreenCover(item:$navigationTrip){trip in SavedTripNavigationView(trip:trip)}
            .sheet(isPresented:$editingDraft){TripDraftEditor(fields:store.pendingTripFields)}
            .alert("删除「\(deleting?.title ?? "这份行程")」？",isPresented:deletionPresented){
                Button("确认删除",role:.destructive){
                    if let trip=deleting{deletingInProgress=true;Task{let success=await store.deleteTrip(trip);if success,selectedTrip?.id==trip.id{selectedTrip=nil};deletingInProgress=false;deleting=nil}}
                }
                Button("保留行程",role:.cancel){}
            }message:{Text("删除后无法恢复，需要重新生成行程。")}

    }
    private var tripList:some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20){
                taskStatus
                if TravelRequirements.requiredKeys.contains(where:{TravelRequirements.known(store.pendingTripFields[$0])}) {draftCard}
                HStack{Text("已保存的旅行").font(.headline);Spacer();Text("\(store.saved.count) 份").font(.caption).foregroundStyle(Palette.muted)}
                if store.saved.isEmpty {ContentUnavailableView{Label("下一段旅程，等你出发",systemImage:"suitcase.rolling")}description:{Text("告诉 Buddy 想去哪儿，一起安排你的旅行。")}actions:{Button("和 Buddy 开始规划"){store.resumePendingTrip()}.buttonStyle(.borderedProminent)}}
                savedRows
            }.padding(20)
        }.background(Palette.cream).navigationTitle(store.language.text("我的行程"))
            .toolbar{ToolbarItem(placement:.topBarTrailing){Button{store.newTrip()}label:{Label("新旅行",systemImage:"plus")}.disabled(store.busy)}}
            .refreshable{await store.reload()}
            .onAppear{openRequestedTrip()}
            .onChange(of:store.tripToOpen?.id){_,_ in openRequestedTrip()}
            .navigationDestination(isPresented:selectionPresented){selectedDestination}
    }
    @ViewBuilder private var taskStatus:some View {
        if let task=store.travelTask,task.status=="failed" || task.status=="generating" {
            Label(task.status=="failed" ? "生成失败，需求已保留":"正在生成行程",systemImage:task.status=="failed" ? "exclamationmark.circle":"sparkles").font(.subheadline)
            if let message=task.message{Text(message).font(.caption).foregroundStyle(Palette.muted)}
        }
        if store.travelTask?.status=="failed",!store.pendingTripFields.isEmpty {HStack{Button("重试生成"){store.startGeneratingPendingTrip()};Button("返回修改"){store.resumePendingTrip()}}.disabled(store.busy)}
    }
    private var savedRows:some View {
        ForEach(store.saved,id:\.id){trip in savedRow(trip)}
    }
    private func savedRow(_ trip:Trip)->some View {
        let title:String = "查看行程：" + trip.title + "，" + trip.profile.date
        let identifier:String = "view-trip-" + (trip.id ?? "draft")
        return VStack(spacing:0){
            Button(action:{store.trip=trip;selectedTrip=trip}) {TripListRow(trip:trip)}
                .buttonStyle(PlainButtonStyle())
                .accessibilityLabel(Text(title)).accessibilityIdentifier(identifier)
            Divider().padding(.horizontal,16)
            HStack{Spacer();Button(role:.destructive){deleting=trip}label:{Label("删除",systemImage:"trash").font(.subheadline).frame(minWidth:70,minHeight:44)}.accessibilityLabel(Text("删除行程："+trip.title)).accessibilityIdentifier("delete-trip-"+(trip.id ?? "draft"))}.padding(.horizontal,16)
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
        VStack(alignment:.leading,spacing:14){
            HStack{Label("待生成的旅行",systemImage:"backpack.fill").font(.caption.bold()).foregroundStyle(Palette.green);Spacer();Button("编辑"){editingDraft=true}.font(.subheadline.bold()).frame(minHeight:44)}
            Text(TripDraftSummary(fields:store.pendingTripFields).title).font(.title2.bold())
            Text(TripDraftSummary(fields:store.pendingTripFields).subtitle).font(.subheadline).foregroundStyle(Palette.muted)
            Text(store.pendingTripFields["interests"] ?? "").font(.subheadline).foregroundStyle(Palette.ink)
            PrimaryButton(title:"继续和 Buddy 规划",symbol:"bubble.left.and.bubble.right"){store.resumePendingTrip()}.disabled(store.busy)
            if !store.live{Text("示例数据").font(.caption).foregroundStyle(Palette.muted)}
        }.padding(18).background(Palette.mint.opacity(0.65),in:RoundedRectangle(cornerRadius:24))
    }

}

struct TripListRow:View {
    let trip:Trip
    private var subtitle:String {"\(trip.profile.date) · \(trip.days.count) 天 · \(trip.days.reduce(0){$0+$1.stops.count}) 个地点"}
    private var highlights:String {(trip.days.first?.stops ?? []).prefix(2).map{$0.name}.joined(separator:" · ")}
    var body:some View {
        HStack(alignment:.top,spacing:14){
            Image(systemName:"sailboat.fill").font(.title2).foregroundStyle(Palette.green).frame(width:56,height:64).background(Palette.mint,in:RoundedRectangle(cornerRadius:16))
            VStack(alignment:.leading,spacing:7){Text(trip.title).font(.headline);Text(subtitle).font(.subheadline).foregroundStyle(Palette.muted);Text(highlights).font(.caption).foregroundStyle(Palette.muted).lineLimit(1);if trip.mode != "live"{Text("示例路线").font(.caption2).foregroundStyle(Palette.green)}}.frame(maxWidth:.infinity,alignment:.leading)
            Image(systemName:"chevron.right").font(.caption.bold()).foregroundStyle(Palette.muted).padding(.top,5)
        }.padding(16).background(.white,in:RoundedRectangle(cornerRadius:22))
    }
}
struct SavedTripWorkspace:View {
    let trip:Trip
    var onNavigate:()->Void
    var onDelete:()->Void
    @EnvironmentObject private var store:AppStore
    @State private var section="map"
    @State private var renaming=false
    @State private var newTitle=""
    var body:some View {
        VStack(spacing:0) {
            HStack{Text("\(trip.profile.date) · \(trip.days.count) 天").font(.caption).foregroundStyle(Palette.muted);Spacer();Button{newTitle=trip.title;renaming=true}label:{Label("改名",systemImage:"pencil").font(.caption)}.accessibilityLabel("修改旅行名称："+trip.title)}.padding(.horizontal,20).padding(.top,4)
            Picker("旅行工作台",selection:$section){Text("路线地图").tag("map");Text("行程详情").tag("details")}.pickerStyle(.segmented).padding(.horizontal,20).padding(.vertical,10).accessibilityIdentifier("trip-workspace-section")
            if section=="map" {HomeView()}
            else {SavedTripOverview(trip:trip,onEdit:openEditor,onMap:{section="map"},onNavigate:onNavigate,onDelete:onDelete)}
        }.background(Palette.cream).navigationTitle(trip.title).navigationBarTitleDisplayMode(.inline)
        .toolbar{ToolbarItemGroup(placement:.topBarTrailing){Button("编辑行程",action:openEditor).disabled(store.busy);Button("删除",role:.destructive,action:onDelete).disabled(store.busy)}}
        .sheet(isPresented:$renaming){NavigationStack{Form{
            TextField("旅行名称",text:$newTitle).accessibilityIdentifier("trip-name-field")
            Text("最多 40 个字，只修改名称，地点和日期保持不变。").font(.caption).foregroundStyle(Palette.muted)
            Button("保存名称"){Task{if await store.renameTrip(trip,to:newTitle){renaming=false}}}.disabled(store.busy || newTitle.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || newTitle.count>40)
        }.navigationTitle("给旅行起个名字").navigationBarTitleDisplayMode(.inline).toolbar{Button("取消"){renaming=false}}}}
        .onAppear{store.trip=trip;store.dayIndex=0;store.selectedStopID=nil}
        .onChange(of:trip.revision){_,_ in store.trip=trip}
    }
    private func openEditor(){store.trip=trip;store.proposalIsSuggestion=false;store.proposal=trip}
}

struct SavedTripOverview:View {
    let trip:Trip
    var onEdit:()->Void;var onMap:()->Void;var onNavigate:()->Void;var onDelete:()->Void
    @EnvironmentObject private var store:AppStore
    var body:some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20){
                HStack(spacing:16){PetAvatar(mood:.ready);VStack(alignment:.leading,spacing:7){Text(trip.title).font(.title2.bold());Text("\(trip.profile.date) · \(trip.days.count) 天").font(.subheadline).foregroundStyle(Palette.muted);if trip.mode != "live"{Text("示例路线 · 出发前请核实开放时间").font(.caption).foregroundStyle(Palette.muted)}}}
                HStack(spacing:12){stat("地点",String(trip.days.flatMap(\.stops).count));stat("预计步数",trip.days.reduce(0){$0+$1.estimatedSteps}.formatted());stat("每人全程活动估算","A$\(Int(trip.days.reduce(0){$0+$1.estimatedCost}))")}
                Text(trip.profile.activityBudgetSummary+" · \(trip.profile.travelers ?? 1) 人同行").font(.caption).foregroundStyle(Palette.muted)
                PrimaryButton(title:"查看路线地图",symbol:"map.fill",action:onMap)
                Button(action:onEdit){Label("编辑行程",systemImage:"slider.horizontal.3")}.font(.subheadline.bold())
                TripNavigationAction(day:trip.days.first,mode:trip.mode,action:onNavigate)
                ForEach(trip.days){day in DaySummary(day:day)}
                DisclosureGroup("规划说明与待确认信息"){VStack(alignment:.leading,spacing:10){Text(store.language.display(trip.explanation));ForEach(trip.warnings,id:\.self){Text(store.language.display($0))}}.font(.caption).foregroundStyle(Palette.muted).padding(.top,8)}
            }.padding(20)
        }.background(Palette.cream).navigationTitle("行程详情").navigationBarTitleDisplayMode(.inline)

    }
    private func stat(_ name:String,_ value:String)->some View {VStack(alignment:.leading,spacing:6){Text(LocalizedStringKey(name)).font(.caption).foregroundStyle(Palette.muted);Text(value).font(.subheadline.bold())}.frame(maxWidth:.infinity,alignment:.leading).padding(14).background(.white,in:RoundedRectangle(cornerRadius:16))}
}
struct TripDraftEditor:View {
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var chat=PetChatStore()
    @State var fields:[String:String]
    @State private var problem:String?
    @State private var saving=false
    var body:some View {
        NavigationStack{
            Form{
                Section("这次旅行"){
                    field("destination","目的地")
                    DatePicker("出发日期",selection:Binding(get:{dateValue},set:{fields["date"]=TripProfile.dateString($0)}),displayedComponents:.date)
                    Stepper("旅行天数：\(fields["days"] ?? "1") 天",value:Binding(get:{Int(fields["days"] ?? "1") ?? 1},set:{fields["days"]=String($0)}),in:1...15)
                    field("companions","同行伙伴")
                }
                Section("兴趣与节奏"){
                    field("interests","这次想玩什么")
                    field("pace","旅行节奏与步数")
                }
                Section("预算与补充"){
                    field("budget","预算金额")
                    field("notes","补充要求")
                    ForEach(travelExtraFields,id:\.0){key,label in field(key,label)}
                    Picker("预算范围",selection:Binding(get:{fields["budgetBasis"] ?? ""},set:{fields["budgetBasis"]=$0})){Text("请选择").tag("");ForEach(TravelRequirements.budgetBases,id:\.0){Text($0.1).tag($0.0)}}
                }
                Section{Text("修改只用于这次旅行，不会更改长期记忆。当前支持悉尼 1–15 天，预算按确认的范围计算；路线费用仅支持 AUD。").font(.caption).foregroundStyle(Palette.muted)}
                if let problem{Section{Text(problem).foregroundStyle(.red)}}
            }.navigationTitle("编辑旅行偏好").navigationBarTitleDisplayMode(.inline)
            .toolbar{ToolbarItem(placement:.topBarLeading){Button("取消"){dismiss()}.disabled(saving)};ToolbarItem(placement:.topBarTrailing){Button(saving ? "保存中…":"保存"){Task{await save()}}.bold().disabled(saving)}}
        }.interactiveDismissDisabled(saving)
    }
    private var dateValue:Date {
        let format=DateFormatter();format.locale=Locale(identifier:"en_US_POSIX");format.timeZone=TimeZone(identifier:"Australia/Sydney");format.dateFormat="yyyy-MM-dd"
        return format.date(from:fields["date"] ?? "") ?? Date()
    }
    private func field(_ key:String,_ label:String)->some View {
        VStack(alignment:.leading,spacing:8){Text(LocalizedStringKey(label)).font(.caption).foregroundStyle(Palette.muted);TextField("",text:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0}),axis:.vertical).accessibilityLabel(label).keyboardType(key=="budget" ? .decimalPad:.default)}.padding(.vertical,4)
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
    let trip:Trip
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @State private var dayIndex:Int?
    init(trip:Trip){self.trip=trip;_dayIndex=State(initialValue:savedTripNavigationDay(trip))}
    var body:some View {
        Group {
            if store.mapProvider == .apple,trip.mode != "live" {
                AppleTripWalkingView(trip:trip,initialDay:store.dayIndex)
            } else if let index=dayIndex,trip.days.indices.contains(index) {
                if trip.mode=="live" {
                    NavigationStack{VStack(spacing:20){Text("這份行程使用 Google 地點資料，目前 App 內逐站步行導航僅支援開源行程。請從查看地圖使用 Google 交通路線規劃。");Button("關閉"){dismiss()}}.padding().navigationTitle("開始導航")}
                } else {
                    WalkingNavigationView(day:trip.days[index],tripID:(trip.id ?? "draft")+":\(trip.revision ?? 0)",autoStart:true)
                }
            } else {
                NavigationStack{List{Section("今天不在旅程日期內，請選擇要導航的行程"){ForEach(Array(trip.days.enumerated()),id:\.offset){i,day in
                    Button{select(i)}label:{VStack(alignment:.leading,spacing:6){Text("第 \(i+1) 天 · \(day.date)").bold();Text("\(day.start.name) → \(day.stops.count) 個站點 → \(day.end.name)").font(.caption).foregroundStyle(Palette.muted)}}
                }}}.navigationTitle("選擇導航日期").toolbar{Button("取消"){dismiss()}}}
            }
        }.onAppear{if store.mapProvider != .apple,let dayIndex{select(dayIndex)}}
    }
    private func select(_ index:Int){store.trip=trip;store.dayIndex=index;dayIndex=index}
}

struct WeatherCard: View {
    @EnvironmentObject var store: AppStore
    var weather: WeatherInfo
    var compact = false
    var body: some View {
        VStack(alignment:.leading,spacing:8) {
            HStack {
                Label("悉尼天气",systemImage:weather.status == "unavailable" ? "cloud.slash" : weather.rainy ? "cloud.rain.fill" : "cloud.sun.fill").font(.subheadline.bold())
                Spacer()
                Button { Task { await store.refreshWeather() } } label: {
                    if store.weatherBusy { ProgressView() } else { Image(systemName:"arrow.clockwise") }
                }.disabled(store.weatherBusy).accessibilityLabel("刷新天气")
            }
            if let days=weather.days, !days.isEmpty {
                ForEach(days.filter { !compact || $0.date == store.currentDay?.date },id: \.date) { day in
                    Text(day.date + " · " + day.summary).font(.caption)
                }
            } else { Text(weather.summary).font(.caption) }
            Text(weather.source).font(.caption2).foregroundStyle(Palette.muted)
            if !compact {
                Text("日间 07–19 时 / 夜间 19–07 时预报；气温为全日范围。刷新仅更新天气，调整路线请点问 AI。").font(.caption2).foregroundStyle(Palette.muted)
                if let date=weather.fetchedAt { Text("查询于 " + date).font(.caption2).foregroundStyle(Palette.muted) }
            }
        }.padding(14).background(.white,in:RoundedRectangle(cornerRadius:18))
    }
}

struct PreparationTrail: View {
    @EnvironmentObject var store: AppStore
    var openRoute: () -> Void
    @State var expanded = false
    var count: Int { ["route","weather","bag"].filter {store.prepared($0)}.count }
    var body: some View {
        VStack(alignment:.leading,spacing:10) {
            Button { withAnimation { expanded.toggle() } } label: { HStack {
                Label(count==3 ? "出发准备小达人" : "出发前的 3 个小任务",systemImage:count==3 ? "medal.fill" : "flag.checkered").font(.subheadline.bold()).foregroundStyle(Palette.green)
                Spacer();Text("\(count)/3").font(.caption.bold()).foregroundStyle(Palette.muted)
                Image(systemName:expanded ? "chevron.up" : "chevron.down").font(.caption).foregroundStyle(Palette.green)
            }}.buttonStyle(.plain).accessibilityLabel("出发准备，\(count)项已完成，点按展开或收起")
            if expanded { HStack(spacing:8) {
                task("route","看路线","map.fill",action:openRoute)
                task("weather","看天气","cloud.sun.fill") { Task { await store.refreshWeather() } }
                task("bag","行李备好","backpack.fill") {store.markPrepared("bag",undo:store.prepared("bag"))}
            }
            Text(count==3 ? "准备得很棒！按自己的节奏出发。" : "随时回来继续，旅行没有断签惩罚。 ").font(.caption2).foregroundStyle(Palette.muted)
            }
        }.padding(14).background(.white,in:RoundedRectangle(cornerRadius:20))
    }
    func task(_ key:String,_ title:String,_ icon:String,action:@escaping()->Void) -> some View {
        Button(action:action) {
            VStack(spacing:5) {
                Image(systemName:store.prepared(key) ? "checkmark.circle.fill" : icon).font(.title3)
                Text(title).font(.caption2.bold())
            }.frame(maxWidth:.infinity).padding(.vertical,10).foregroundStyle(store.prepared(key) ? .white : Palette.green).background(store.prepared(key) ? Palette.green : Palette.mint,in:RoundedRectangle(cornerRadius:14))
        }.buttonStyle(BuddyPressStyle()).accessibilityLabel(title + (store.prepared(key) ? "，已完成" : "，待完成"))
    }
}

/// Suggestions stay local to this sheet until the user accepts the validated result.
struct TripAdjustmentView:View {
    let original:Trip
    let dayIndex:Int
    @EnvironmentObject private var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model=ScheduleEditorModel()
    @State private var target:String?
    @State private var notice:String?
    @State private var requested=false
    @State private var discoveringFood=false
    private var day:ScheduleDay? {guard let state=model.state,state.days.indices.contains(dayIndex) else{return nil};return state.days[dayIndex]}
    private var choices:[ScheduleCandidate] {
        guard let state=model.state else{return []}
        let used=Set(state.days.flatMap(\.activities).compactMap(\.placeId))
        return state.candidates.filter{!used.contains($0.id) && !$0.importedPoi}
    }
    private var proposedTrip:Trip? {model.trip.map{TripAdjustmentMerge.preservingOtherDays($0,original:original,dayIndex:dayIndex)}}
    private var canAdopt:Bool {requested && !model.busy && !model.dirty && model.error==nil && model.trip != nil && model.state?.issues.contains(where:{$0.blocking && ($0.day==dayIndex || $0.day == -1)})==false}
    var body:some View {
        NavigationStack{ScrollView{VStack(alignment:.leading,spacing:16){
            HStack{PetAvatar(mood:model.busy ? .thinking:requested ? .ready:.curious).frame(width:100,height:110);Text("我们只改第 \(dayIndex+1) 天。先看看建议，喜欢再收进正式行程～").font(.headline)}
            if original.mode != "demo" {Text("当前快捷调整仅支持示例行程；在线行程可通过聊天提出调整建议。").foregroundStyle(Palette.muted)}
            else {
                HStack{Button("再轻松一点"){lighten()}.disabled((day?.activities.count ?? 0)<2);Button("多一点美食"){addFood()}.disabled(!choices.contains{$0.category=="美食"});Button("加一段休息"){addRest()}}
                    .buttonStyle(.bordered).disabled(model.busy || requested)
                if !requested,!choices.contains(where:{$0.category=="美食"}) {
                    Text("当前没有未安排的餐饮候选。可以直接找一家餐厅，加入后先预览，不会立即保存。").font(.caption).foregroundStyle(Palette.muted)
                    Button("寻找餐厅并加入当天",systemImage:"magnifyingglass"){discoveringFood=true}.disabled(model.busy)
                }
                if !requested,(day?.activities.count ?? 0)<2{Text("当天已很精简，可在编辑行程中调整停留和休息。").font(.caption).foregroundStyle(Palette.muted)}
                if !requested,let day {
                    Text("换掉某一站").font(.headline)
                    ForEach(day.activities.filter{$0.locked != true && !original.profile.mustVisit.contains($0.placeId ?? "")}){a in
                        Button{target=a.id}label:{Label(a.name,systemImage:target==a.id ? "checkmark.circle.fill":"circle")}.buttonStyle(.bordered)
                    }
                    if target != nil {Text("选择替代地点").font(.subheadline.bold());ForEach(choices){candidate in Button(candidate.name){replace(with:candidate)}.buttonStyle(.bordered)};if choices.isEmpty{Text("当前没有未安排的示例候选地点。")}}
                }
                if model.busy{ProgressView("正在校验交通、步数和预算…")}
                if let error=model.error{Text(error).foregroundStyle(.red);Button("重试校验"){model.recompute()}}
                if let notice{Text(notice).font(.subheadline).foregroundStyle(Palette.muted)}
                if requested,let proposed=proposedTrip,!model.busy,model.error==nil {
                    let before=original.days[dayIndex],after=proposed.days[dayIndex]
                    VStack(alignment:.leading,spacing:10){Text("建议变化 · 尚未保存").font(.headline)
                        Text("原来："+before.stops.map(\.name).joined(separator:" → "))
                        Text("建议："+after.stops.map(\.name).joined(separator:" → "))
                        Text("预计步数：\(before.estimatedSteps) → \(after.estimatedSteps)")
                        Text("预计返回：\(before.finishTime) → \(after.finishTime)")
                        Text("每人活动估算：A$\(Int(before.estimatedCost)) → A$\(Int(after.estimatedCost))")
                        if before.stops.contains(where:{$0.id=="rocks" && $0.cost>0}),after.stops.contains(where:{$0.id=="rocks" && $0.cost==0}) {
                            Text("岩石区原先包含餐饮预留。调整后街区游览不计餐费，餐饮按用餐站另计，避免重复预留。").font(.caption).foregroundStyle(Palette.muted)
                        }
                        Text("其他日期保留原安排。搜索加入的餐饮费用按每餐预留金额估算，实际价格、营业时间和排队仍需确认。").font(.caption).foregroundStyle(Palette.muted)
                    }.font(.subheadline).padding(16).background(Palette.mint,in:RoundedRectangle(cornerRadius:18))
                    ForEach((model.state?.issues ?? []).filter{$0.day==dayIndex || $0.day == -1}){issue in Text(store.language.display(issue.message)).font(.caption).foregroundStyle(issue.blocking ? .red:.orange)}
                    Button("换一种调整"){model.restore();requested=false;target=nil;notice=nil}
                }
                if let error=store.adjustmentError{Text(error).foregroundStyle(.red)}
            }
        }.padding(20)}.background(Palette.cream).navigationTitle("调整这一天").navigationBarTitleDisplayMode(.inline)
        .toolbar{Button("保留原行程"){dismiss()}.disabled(store.busy)}
        .safeAreaInset(edge:.bottom){if requested{PrimaryButton(title:"采用调整",symbol:"checkmark"){Task{if let value=proposedTrip,await store.saveAdjustment(value,original:original,dayIndex:dayIndex){dismiss()}}}.disabled(!canAdopt || store.busy).padding(20).background(Palette.cream)}}.tint(Palette.green)}
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
        var replacement=a;replacement.id=UUID().uuidString;replacement.placeId=candidate.id;replacement.name=candidate.name;replacement.lat=candidate.lat;replacement.lng=candidate.lng;replacement.duration=candidate.visitMinutes;replacement.cost=candidate.cost;replacement.fixedDuration=nil;replacement.indoor=candidate.indoor;replacement.hoursVerified=false
        requested=true;model.edit{state in if let i=state.days[dayIndex].activities.firstIndex(where:{$0.id==target}){state.days[dayIndex].activities[i]=replacement}}
    }
}

// Expose capability before entering date selection or a non-functional navigator.
enum TripNavigationAvailability {
    static func canNavigate(configured:Bool?,mode:String)->Bool {configured == true && mode != "live"}

}
struct TripNavigationAction:View {
    let day:DayPlan?
    let mode:String
    var action:()->Void
    @EnvironmentObject private var store:AppStore
    var body:some View {
        VStack(alignment:.leading,spacing:7){
            if day?.stops.isEmpty != false {
                Text("当天为自由安排，添加地点后即可查看前往路线。").font(.caption).foregroundStyle(Palette.muted)
            } else if store.mapProvider == .apple,mode != "live" {
                PrimaryButton(title:"查看 App 内步行路线",symbol:"figure.walk",action:action).accessibilityIdentifier("trip-walking-route")
            } else if TripNavigationAvailability.canNavigate(configured:store.health?.navigationConfigured,mode:mode) {
                PrimaryButton(title:"开始导航",symbol:"location.fill",action:action)
            } else if mode != "live" {
                PrimaryButton(title:"用 Apple 查看 App 内步行路线",symbol:"figure.walk"){store.mapProvider = .apple;action()}
            } else {
                Text("此行程使用 Google 地点资料，请切换到 Google 底图查看匹配的路线。").font(.caption).foregroundStyle(Palette.muted)
            }
        }
    }
}
