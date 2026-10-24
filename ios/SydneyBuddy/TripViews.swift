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
    var body:some View { NavigationStack { ScrollView {VStack(alignment:.leading,spacing:20){if let t=store.trip {Text(t.explanation).font(.subheadline);WeatherCard(weather:store.currentWeather);ForEach(t.days){DaySummary(day:$0)};ForEach(t.warnings,id:\.self){Text($0).font(.caption).foregroundStyle(Palette.muted)};DisclosureGroup("规划与验证记录"){ForEach(t.trace){r in VStack(alignment:.leading,spacing:6){Text(r.title).font(.headline);Text(r.detail).font(.caption);Text(r.source).font(.caption2).foregroundStyle(Palette.green)}.padding(.vertical,8)}}}}.padding(24)}.background(Palette.cream).navigationTitle("这次的小冒险").onAppear {store.markPrepared("route")}.task(id:store.travelDataSource){await store.refreshWeather()} } }
}
struct TripsView:View {
    @EnvironmentObject var store:AppStore
    @State var detail=false
    @State private var navigationTrip:Trip?
    @State private var routeTrip:Trip?
    var body:some View {
        NavigationStack {
            ScrollView {
                VStack(alignment:.leading,spacing:22){
                    HStack {VStack(alignment:.leading,spacing:5){Text("下一站，去发现").font(.subheadline).foregroundStyle(Palette.muted);Text("每次冒险都值得收藏").font(.caption).foregroundStyle(Palette.muted)};Spacer();Button{store.newTrip()}label:{Label("新建",systemImage:"plus").font(.subheadline.bold()).padding(13).foregroundStyle(.white).background(Palette.green,in:Capsule())}}
                    if !store.pendingTripFields.isEmpty {
                        VStack(alignment:.leading,spacing:16) {
                            Label("旅行偏好已准备好",systemImage:"backpack.fill").font(.headline).foregroundStyle(Palette.green)
                            Text("\(store.pendingTripFields["destination"] ?? "悉尼") · \(store.pendingTripFields["days"] ?? "1") 天 · \(store.pendingTripFields["companions"] ?? "")").font(.title3.bold())
                            Text("\(store.pendingTripFields["date"] ?? "") · \(store.pendingTripFields["interests"] ?? "")").font(.subheadline).foregroundStyle(Palette.muted)
                            Text("\(store.pendingTripFields["pace"] ?? "") · 每日 A$\(store.pendingTripFields["budget"] ?? "100")").font(.caption).foregroundStyle(Palette.muted)
                            PrimaryButton(title:"生成这次旅行路线",symbol:"point.topleft.down.curvedto.point.bottomright.up") {Task{await store.generatePendingTrip()}}.disabled(store.busy)
                            Text(store.live ? "生成后保存在我的行程，可查看路线与导航。" : "当前生成示例路线，完成后保存在我的行程。").font(.caption).foregroundStyle(Palette.muted)
                        }.padding(22).background(Palette.mint.opacity(0.6),in:RoundedRectangle(cornerRadius:26))
                    }
                    Button("試用 3 天時間軸"){Task{await store.tryScheduleDemo()}}.buttonStyle(.borderedProminent).tint(Palette.green)
                    if store.saved.isEmpty && store.pendingTripFields.isEmpty {ContentUnavailableView("还没有行程",systemImage:"suitcase",description:Text("创建你的第一份悉尼旅行计划"))}
                    ForEach(store.saved,id:\.id){t in
                        VStack(alignment:.leading,spacing:16){
                            Button{store.trip=t;store.proposal=t} label:{VStack(alignment:.leading,spacing:16){
                            ZStack(alignment:.bottomLeading){LinearGradient(colors:[Palette.mint,Color(red:0.68,green:0.85,blue:0.90)],startPoint:.bottomLeading,endPoint:.topTrailing);Image(systemName:"sailboat.fill").font(.system(size:80)).foregroundStyle(.white).frame(maxWidth:.infinity,alignment:.trailing).padding(22);Text("SYDNEY").font(.system(size:25,weight:.black,design:.rounded)).tracking(4).foregroundStyle(Palette.green).padding(22)}.frame(height:145).clipShape(RoundedRectangle(cornerRadius:20))
                            HStack {VStack(alignment:.leading,spacing:7){Text(t.title).font(.headline);Text("\(t.profile.date) · \(t.days.count) 天 · \(t.days.flatMap(\.stops).count) 个地点").font(.caption).foregroundStyle(Palette.muted)};Spacer();Image(systemName:"arrow.up.right").foregroundStyle(Palette.green)}
                            }}.buttonStyle(.plain).accessibilityLabel("編輯行程："+t.title).accessibilityIdentifier("edit-trip-"+(t.id ?? "draft"))
                            HStack {Button{navigationTrip=t}label:{Label("開始導航",systemImage:"location.fill")}.font(.subheadline.bold()).tint(Palette.green).accessibilityIdentifier("navigate-trip-"+(t.id ?? "draft"));Button("查看路线地图"){store.trip=t;store.dayIndex=0;routeTrip=t}.font(.subheadline.bold()).tint(Palette.green);Spacer();Text(LocalizedStringKey(t.mode=="live" ? "在线数据规划" : (t.mode=="assisted" ? "Nemotron · 示例地点" : "演示行程"))).font(.caption2).foregroundStyle(Palette.muted);Menu{Button("删除行程",role:.destructive){Task{await store.deleteTrip(t)}}}label:{Image(systemName:"ellipsis").padding(8)}}
                        }.padding(18).background(.white,in:RoundedRectangle(cornerRadius:26))
                    }
                    HStack{Robot(size:70);Text("不必赶路，\n按自己的节奏出发。").font(.headline).foregroundStyle(Palette.ink)}.padding(.vertical,10)
                }.padding(24)
            }.background(Palette.cream).navigationTitle(store.language.text("我的行程")).refreshable{await store.reload()}
        }.fullScreenCover(item:$navigationTrip){trip in SavedTripNavigationView(trip:trip)}
        .fullScreenCover(item:$routeTrip){_ in NavigationStack{HomeView().toolbar{ToolbarItem(placement:.topBarLeading){Button("返回我的行程"){routeTrip=nil}}}}}
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
