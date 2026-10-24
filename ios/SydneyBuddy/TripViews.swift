import SwiftUI

struct ProposalView:View {
    @EnvironmentObject var store:AppStore
    var body:some View {
        if store.proposal?.mode == "demo" {ScheduleEditorView()} else { legacy }
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
    var body:some View {
        VStack(alignment:.leading,spacing:16) {
            HStack {Text(day.date).font(.headline);Spacer();Text("约 A$\(Int(day.estimatedCost))").font(.subheadline.bold()).foregroundStyle(Palette.green)}
            Label("约 \(day.estimatedSteps) 步 · \(day.finishTime) 返回",systemImage:"figure.walk").font(.caption).foregroundStyle(Palette.muted)
            Text("\(day.start.name) 出发 → \(day.end.name) 返回").font(.caption2).foregroundStyle(Palette.muted)
            if day.freeTime == true {Label("自由安排 · 景点待补充",systemImage:"leaf").font(.subheadline).foregroundStyle(Palette.green)}
            ForEach(Array(day.stops.enumerated()),id:\.element.id){i,s in
                HStack(alignment:.top,spacing:13) {
                    Text("\(i+1)").font(.caption.bold()).frame(width:27,height:27).foregroundStyle(.white).background(Palette.green,in:Circle())
                    VStack(alignment:.leading,spacing:7) {
                        HStack {Text(s.name).font(.subheadline.bold());Spacer();Image(systemName:s.symbol).foregroundStyle(Palette.green)}
                        Text("\(s.arrival)–\(s.departure) · \(s.indoor ? "室内" : "室外") · 约 A$\(Int(s.cost))").font(.caption).foregroundStyle(Palette.muted)
                        Text(s.note).font(.caption2).foregroundStyle(Palette.muted)
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
    var body:some View { NavigationStack { ScrollView {VStack(alignment:.leading,spacing:20){if let t=store.trip {Text(t.explanation).font(.subheadline);WeatherCard(weather:store.refreshedWeather ?? t.weather);ForEach(t.days){DaySummary(day:$0)};ForEach(t.warnings,id:\.self){Text($0).font(.caption).foregroundStyle(Palette.muted)};DisclosureGroup("规划与验证记录"){ForEach(t.trace){r in VStack(alignment:.leading,spacing:6){Text(r.title).font(.headline);Text(r.detail).font(.caption);Text(r.source).font(.caption2).foregroundStyle(Palette.green)}.padding(.vertical,8)}}}}.padding(24)}.background(Palette.cream).navigationTitle("这次的小冒险").onAppear {store.markPrepared("route")} } }
}
struct TripsView:View {
    @EnvironmentObject var store:AppStore
    @State private var navigationTrip:Trip?
    @State private var routeTrip:Trip?
    @State private var selectedTrip:Trip?
    @State private var editingDraft=false
    @State private var chatting=false
    @State private var deleting:Trip?
    var body:some View {
        NavigationStack {
            ScrollView {
                VStack(alignment:.leading,spacing:20){
                    if !store.pendingTripFields.isEmpty { draftCard }
                    HStack{Text("已保存的旅行").font(.headline);Spacer();Text("\(store.saved.count) 份").font(.caption).foregroundStyle(Palette.muted)}
                    if store.saved.isEmpty {ContentUnavailableView("下一段旅程，等你出发",systemImage:"suitcase.rolling",description:Text("准备好偏好后，生成你的第一份路线。"))}
                    ForEach(store.saved,id:\.id){t in
                        Button{store.trip=t;selectedTrip=t}label:{
                            TripListRow(trip:t)
                        }.buttonStyle(.plain).accessibilityLabel("查看行程："+t.title+"，"+t.profile.date).accessibilityIdentifier("view-trip-"+(t.id ?? "draft"))
                        .contextMenu{Button("删除行程",role:.destructive){deleting=t}}
                    }
                }.padding(20)
            }.background(Palette.cream).navigationTitle(store.language.text("我的行程"))
            .toolbar{ToolbarItem(placement:.topBarTrailing){Button{store.newTrip()}label:{Label("新旅行",systemImage:"plus")}.disabled(store.busy)}}
            .refreshable{await store.reload()}
            .navigationDestination(isPresented:Binding(get:{selectedTrip != nil},set:{if !$0{selectedTrip=nil}})){
                if let selected=selectedTrip {
                    let current=store.saved.first(where:{$0.id==selected.id}) ?? selected
                    SavedTripOverview(trip:current,onEdit:{store.trip=current;store.proposal=current},onMap:{store.trip=current;store.dayIndex=0;routeTrip=current},onNavigate:{navigationTrip=current},onDelete:{deleting=current})
                }
            }
        }.fullScreenCover(item:$navigationTrip){trip in SavedTripNavigationView(trip:trip)}
        .fullScreenCover(item:$routeTrip){_ in NavigationStack{HomeView().toolbar{ToolbarItem(placement:.topBarLeading){Button("返回行程"){routeTrip=nil}}}}}
        .sheet(isPresented:$editingDraft){TripDraftEditor(fields:store.pendingTripFields)}
        .sheet(isPresented:$chatting,onDismiss:{Task{await syncDraft()}}){NavigationStack{PetChatView(conversationOnly:true,draftScope:true).toolbar{ToolbarItem(placement:.topBarTrailing){Button("完成"){chatting=false}}}}}
        .confirmationDialog("删除这份行程？此操作无法撤销。",isPresented:Binding(get:{deleting != nil},set:{if !$0{deleting=nil}}),titleVisibility:.visible){Button("删除行程",role:.destructive){if let trip=deleting{Task{await store.deleteTrip(trip);if selectedTrip?.id==trip.id{selectedTrip=nil};deleting=nil}}}}
    }
    private var draftCard:some View {
        VStack(alignment:.leading,spacing:14){
            HStack{Label("待生成的旅行",systemImage:"backpack.fill").font(.caption.bold()).foregroundStyle(Palette.green);Spacer();Button("编辑"){editingDraft=true}.font(.subheadline.bold()).frame(minHeight:44)}
            Text("\(store.pendingTripFields["destination"] ?? "悉尼") · \(store.pendingTripFields["days"] ?? "1") 天").font(.title2.bold())
            Text("\(store.pendingTripFields["date"] ?? "") · \(store.pendingTripFields["companions"] ?? "")").font(.subheadline).foregroundStyle(Palette.muted)
            Text(store.pendingTripFields["interests"] ?? "").font(.subheadline).foregroundStyle(Palette.ink)
            PrimaryButton(title:"生成旅行路线",symbol:"sparkles"){store.startGeneratingPendingTrip()}.disabled(store.busy)
            HStack{Button{chatting=true}label:{Label("再和 Buddy 聊聊",systemImage:"bubble.left.and.bubble.right")}.font(.subheadline);Spacer();if !store.live{Text("示例数据").font(.caption).foregroundStyle(Palette.muted)}}
        }.padding(18).background(Palette.mint.opacity(0.65),in:RoundedRectangle(cornerRadius:24))
    }
    private func syncDraft() async {
        let chat=PetChatStore();await chat.load(scope:"draft")
        guard let session=chat.session,chat.error==nil else{store.errorMessage=chat.error;return}
        let fields=store.pendingTripFields.merging(session.fields){_,new in new}
        do {let profile=try PetTripDraft.profile(fields);store.completePetPreferences(profile,fields:fields)}
        catch {store.errorMessage="对话已保存，请在编辑偏好中检查："+error.localizedDescription}
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
struct SavedTripOverview:View {
    let trip:Trip
    var onEdit:()->Void;var onMap:()->Void;var onNavigate:()->Void;var onDelete:()->Void
    var body:some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20){
                HStack(spacing:16){PetAvatar(mood:.ready);VStack(alignment:.leading,spacing:7){Text(trip.title).font(.title2.bold());Text("\(trip.profile.date) · \(trip.days.count) 天").font(.subheadline).foregroundStyle(Palette.muted);if trip.mode != "live"{Text("示例路线 · 出发前请核实开放时间").font(.caption).foregroundStyle(Palette.muted)}}}
                HStack(spacing:12){stat("地点",String(trip.days.flatMap(\.stops).count));stat("预计步数",trip.days.reduce(0){$0+$1.estimatedSteps}.formatted());stat("活动估算","A$\(Int(trip.days.reduce(0){$0+$1.estimatedCost}))")}
                PrimaryButton(title:"查看路线地图",symbol:"map.fill",action:onMap)
                HStack{Button(action:onEdit){Label("编辑行程",systemImage:"slider.horizontal.3")};Spacer();Button(action:onNavigate){Label("开始导航",systemImage:"location.fill")}}.font(.subheadline.bold()).padding(.vertical,6)
                ForEach(trip.days){day in DaySummary(day:day)}
                DisclosureGroup("规划说明与待确认信息"){VStack(alignment:.leading,spacing:10){Text(trip.explanation);ForEach(trip.warnings,id:\.self){Text($0)}}.font(.caption).foregroundStyle(Palette.muted).padding(.top,8)}
            }.padding(20)
        }.background(Palette.cream).navigationTitle("行程详情").navigationBarTitleDisplayMode(.inline)
        .toolbar{ToolbarItem(placement:.topBarTrailing){Menu{Button("删除行程",role:.destructive,action:onDelete)}label:{Image(systemName:"ellipsis").frame(width:44,height:44)}.accessibilityLabel("行程选项")}}
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
                    field("budget","每天活动预算（AUD）")
                    field("notes","补充要求")
                }
                Section{Text("修改只用于这次旅行，不会更改长期记忆。当前支持悉尼 1–15 天，活动预算单位为 AUD。").font(.caption).foregroundStyle(Palette.muted)}
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
        do{let profile=try PetTripDraft.profile(fields);await chat.load(scope:"draft");guard chat.error==nil else{problem=chat.error;return};await chat.update(fields);guard chat.error==nil else{problem=chat.error;return};store.completePetPreferences(profile,fields:fields);dismiss()}
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
            if let index=dayIndex,trip.days.indices.contains(index) {
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
        }.onAppear{if let dayIndex{select(dayIndex)}}
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
