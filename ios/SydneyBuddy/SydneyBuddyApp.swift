import SwiftUI
import GoogleMaps

@main struct SydneyBuddyApp: App {
    @StateObject private var store = AppStore()
    static var mapConfigured: Bool {
        guard let key=Bundle.main.object(forInfoDictionaryKey:"GoogleMapsAPIKey") as? String else {return false}
        return !key.isEmpty && !key.contains("$(") && !key.contains("YOUR_")
    }
    // Initialize Google only when that provider is actually selected.
    static let mapReady: Bool = {
        guard let key = Bundle.main.object(forInfoDictionaryKey: "GoogleMapsAPIKey") as? String,
              !key.isEmpty, !key.contains("$("), !key.contains("YOUR_") else { return false }
        return GMSServices.provideAPIKey(key)
    }()
    var body: some Scene {
        WindowGroup { RootView().environmentObject(store).preferredColorScheme(.light).task { await store.reload() } }
    }
}

enum Palette {
    static let green = Color(red:0.05,green:0.51,blue:0.37)
    static let ink = Color(red:0.10,green:0.22,blue:0.19)
    static let cream = Color(red:0.98,green:0.97,blue:0.94)
    static let mint = Color(red:0.86,green:0.95,blue:0.88)
    static let muted = Color(red:0.42,green:0.49,blue:0.45)
}
struct PrimaryButton: View {
    var title: String; var symbol = "arrow.right"; var action: () -> Void
    var body: some View {
        Button(action:action) { HStack { Spacer(); Text(LocalizedStringKey(title)).fontWeight(.bold); Image(systemName:symbol); Spacer() }.padding(.vertical,18) }
            .buttonStyle(BuddyPressStyle()).foregroundStyle(.white).background(Palette.green,in:RoundedRectangle(cornerRadius:22)).padding(.bottom,5).background(Palette.ink,in:RoundedRectangle(cornerRadius:22)).accessibilityLabel(title)
    }
}
struct Robot: View {
    var size: CGFloat = 130
    var body: some View {
        ZStack {
            Ellipse().fill(Palette.green.opacity(0.10)).frame(width:size*0.8,height:size*0.10).offset(y:size*0.46)
            Capsule().fill(Palette.green).frame(width:size*0.06,height:size*0.18).rotationEffect(.degrees(20)).offset(x:size*0.05,y:-size*0.40)
            Circle().fill(Palette.mint).overlay(Circle().stroke(Palette.green.opacity(0.4),lineWidth:2)).frame(width:size*0.12).offset(x:size*0.08,y:-size*0.50)
            RoundedRectangle(cornerRadius:size*0.20).fill(LinearGradient(colors:[Palette.mint,.white],startPoint:.topLeading,endPoint:.bottomTrailing)).frame(width:size*0.64,height:size*0.55).offset(y:size*0.19)
            Capsule().fill(Palette.green).frame(width:size*0.13,height:size*0.19).rotationEffect(.degrees(25)).offset(x:-size*0.20,y:size*0.40)
            Capsule().fill(Palette.green).frame(width:size*0.13,height:size*0.19).rotationEffect(.degrees(-25)).offset(x:size*0.20,y:size*0.40)
            Capsule().fill(Palette.green.opacity(0.8)).frame(width:size*0.12,height:size*0.32).rotationEffect(.degrees(-30)).offset(x:-size*0.38,y:size*0.1)
            Capsule().fill(Palette.green.opacity(0.8)).frame(width:size*0.12,height:size*0.32).rotationEffect(.degrees(-55)).offset(x:size*0.37,y:-size*0.02)
            RoundedRectangle(cornerRadius:size*0.30).fill(LinearGradient(colors:[.white,Palette.mint],startPoint:.top,endPoint:.bottom)).overlay(RoundedRectangle(cornerRadius:size*0.30).stroke(Palette.green.opacity(0.3),lineWidth:2)).frame(width:size*0.82,height:size*0.65).offset(y:-size*0.11)
            RoundedRectangle(cornerRadius:size*0.20).fill(Palette.ink).frame(width:size*0.63,height:size*0.39).offset(y:-size*0.10)
            HStack(spacing:size*0.18) { Capsule().fill(.white).frame(width:size*0.06,height:size*0.11); Capsule().fill(.white).frame(width:size*0.06,height:size*0.11) }.offset(y:-size*0.12)
            Image(systemName:"mouth").font(.system(size:size*0.12,weight:.bold)).foregroundStyle(.white).offset(y:size*0.01)
            Circle().fill(Color.orange.opacity(0.65)).frame(width:size*0.09).offset(y:size*0.27)
        }.frame(width:size,height:size*1.15).accessibilityLabel("旅行机器人 Buddy")
    }
}
struct RootView: View {
    @EnvironmentObject var store: AppStore
    var body: some View {
        ZStack {
            Palette.cream.ignoresSafeArea()
            if store.showOnboarding { PetChatView(onboarding:true) }
            else {
                TabView(selection:$store.tab) {
                    TripsView().tabItem { Label(store.language.text("行程"),systemImage:"suitcase.rolling.fill") }.tag(1)
                    HomeView().tabItem { Label(store.language.text("地图"),systemImage:"map.fill") }.tag(0)
                    JournalView().tabItem { Label(store.language.text("记账"),systemImage:"creditcard.fill") }.tag(2)
                    SettingsView().tabItem { Label(store.language.text("我的"),systemImage:"person.crop.circle") }.tag(3)
                }.tint(Palette.green).modifier(CompactTabs())
            }
            if store.busy {
                ZStack { Color.black.opacity(0.15).ignoresSafeArea(); VStack(spacing:14) { Robot(size:75); ProgressView(); Text("正在检查整条行程…").font(.headline); Text("时间、步数、预算和返程都要刚刚好").font(.caption).foregroundStyle(Palette.muted) }.padding(30).background(Palette.cream,in:RoundedRectangle(cornerRadius:28)) }
            }
        }
        .sheet(isPresented:Binding(get:{store.proposal != nil},set:{if !$0 {store.proposal=nil}})) { ProposalView() }
        .alert("需要留意",isPresented:Binding(get:{store.errorMessage != nil},set:{if !$0 {store.errorMessage=nil}})) { Button("知道了",role:.cancel) {} } message: { Text(store.errorMessage ?? "") }
        .environment(\.locale,Locale(identifier:store.language.rawValue))
    }
}
struct OnboardingView: View {
    @EnvironmentObject var store: AppStore
    @Environment(\.accessibilityReduceMotion) var reduceMotion
    @State var step = 0
    @State var selectedDate = Date()
    @State var feedback = "每个选择都没有标准答案，适合你就好。"
    let interests = [("风景","sparkles"),("美食","fork.knife"),("自然","leaf.fill"),("艺术","paintpalette.fill"),("文化","building.columns.fill"),("城市","building.2.fill")]
    var body: some View {
        VStack(spacing:0) {
            HStack(spacing:16) {
                Button { if step>0 { withAnimation { step-=1 } } else if !store.saved.isEmpty {store.showOnboarding=false} } label: { Image(systemName:"chevron.left").font(.headline).foregroundStyle(Palette.muted) }.accessibilityLabel("返回上一步")
                GeometryReader { g in
                    ZStack(alignment:.leading) {
                        Capsule().fill(Palette.green.opacity(0.12))
                        Capsule().fill(Palette.green).frame(width:g.size.width*Double(step)/8)
                    }
                }.frame(height:13).accessibilityLabel("已完成 \(step) 个问题，共 8 个")
                Text("\(step+1)/8").font(.caption.bold()).monospacedDigit().foregroundStyle(Palette.green)
            }.padding(24)
            ScrollView {
                VStack(alignment:.leading,spacing:24) {
                    HStack(spacing:12) {
                        Robot(size:76)
                        Text(feedback).font(.subheadline.bold()).foregroundStyle(Palette.ink).padding(16).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:20)).overlay(RoundedRectangle(cornerRadius:20).stroke(Palette.green.opacity(0.16),lineWidth:2))
                    }
                    VStack(alignment:.leading,spacing:8) {
                        Text("小冒险 · 出发准备").font(.caption.bold()).foregroundStyle(Palette.green)
                        Text(title).font(.system(size:30,weight:.heavy,design:.rounded)).foregroundStyle(Palette.ink)
                        Text(subtitle).font(.subheadline).foregroundStyle(Palette.muted)
                    }
                    Group {
                        switch step {
                        case 0:
                            Label("SYDNEY · 悉尼",systemImage:"sailboat.fill").font(.title2.bold()).foregroundStyle(Palette.green).padding(24).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:24))
                            DatePicker("出发日期",selection:$selectedDate,in:Date()...,displayedComponents:.date).datePickerStyle(.graphical).tint(Palette.green).padding(12).background(.white,in:RoundedRectangle(cornerRadius:24))
                        case 1:
                            ForEach(1...3,id:\.self) { n in
                                choice("\(n) 天",detail:["一次轻巧的城市探索","多留一点时间，慢慢发现","给风景和休息都留些空间"][n-1],symbol:"sun.max.fill",selected:store.profile.days==n) { store.profile.days=n; respond("好，给你安排 \(n) 天的小冒险。") }
                            }
                        case 2:
                            ForEach([(5000,"轻松逛逛","约 5,000 步 · 多休息，少赶路","cup.and.saucer.fill"),(10000,"刚刚好","约 10,000 步 · 散步和探索兼顾","figure.walk"),(18000,"活力满满","约 18,000 步 · 愿意多走走","figure.hiking")],id:\.0) { v in
                                choice(v.1,detail:v.2,symbol:v.3,selected:store.profile.maxSteps==v.0) { store.profile.maxSteps=v.0; respond(v.0==5000 ? "收到！休息也是旅行的一部分。" : "记住啦，我会帮你把体力留给喜欢的地方。") }
                            }
                            Text("步数会计入景点内活动；上限越低，可安排的地点可能越少。").font(.caption).foregroundStyle(Palette.muted)
                        case 3:
                            LazyVGrid(columns:[GridItem(.flexible()),GridItem(.flexible())],spacing:14) {
                                ForEach(interests,id:\.0) { v in
                                    let selected=store.profile.interests.contains(v.0)
                                    Button {
                                        if selected {store.profile.interests.removeAll{$0==v.0}} else {store.profile.interests.append(v.0)}
                                        respond(store.profile.interests.isEmpty ? "也可以随心出发，我会平衡推荐。" : "收到："+store.profile.interests.joined(separator:"、"))
                                    } label: {
                                        VStack(spacing:12) { Image(systemName:v.1).font(.system(size:28));Text(v.0).font(.headline);Image(systemName:selected ? "checkmark.circle.fill" : "circle").font(.caption) }.frame(maxWidth:.infinity).frame(height:132).foregroundStyle(selected ? Palette.green : Palette.muted).background(selected ? Palette.mint : .white,in:RoundedRectangle(cornerRadius:22)).overlay(RoundedRectangle(cornerRadius:22).stroke(selected ? Palette.green : Palette.green.opacity(0.12),lineWidth:2))
                                    }.buttonStyle(.plain).accessibilityAddTraits(selected ? .isSelected : [])
                                }
                            }
                        case 4:
                            ForEach([50,100,150],id:\.self) { n in
                                choice("A$\(n) / 天",detail:n==50 ? "以免费景点为主" : n==100 ? "留出景点与部分餐饮预算" : "为更多付费体验留空间",symbol:"wallet.bifold.fill",selected:store.profile.budget==Double(n)) {store.profile.budget=Double(n);respond("预算记好啦，先估算，再由你决定。")}
                            }
                            VStack {Text("自定 A$\(Int(store.profile.budget))").font(.headline);Slider(value:$store.profile.budget,in:0...300,step:10).tint(Palette.green)}.padding(20).background(.white,in:RoundedRectangle(cornerRadius:22))
                            Text("活动预算为估算，不含住宿、机票及全部餐饮。").font(.caption).foregroundStyle(Palette.muted)
                        case 5:
                            choice("环形码头",detail:"从悉尼海港开始",symbol:"ferry.fill",selected:store.profile.start==Coordinate.quay) {store.profile.start = .quay;respond("海港出发，沿途慢慢发现。")}
                            choice("中央车站",detail:"从城市交通枢纽开始",symbol:"tram.fill",selected:store.profile.start==Coordinate.central) {store.profile.start = .central;respond("从中央车站出发，也会安排好返程。")}
                            Text("每天返回同一起点，之后可在“我的”修改。").font(.caption).foregroundStyle(Palette.muted)
                        case 6:
                            ForEach([(600,1080,"悠闲一天","10:00 出发 · 18:00 前返回"),(540,1020,"早一点出发","09:00 出发 · 17:00 前返回"),(780,1080,"午后小冒险","13:00 出发 · 18:00 前返回")],id:\.0) { v in
                                choice(v.2,detail:v.3,symbol:"clock.fill",selected:store.profile.startMinute==v.0 && store.profile.endMinute==v.1) {store.profile.startMinute=v.0;store.profile.endMinute=v.1;respond("时间留好啦，步行和休息也会算进去。")}
                            }
                        default:
                            VStack(alignment:.leading,spacing:16) {
                                Label("准备完成！",systemImage:"checkmark.seal.fill").font(.title2.bold()).foregroundStyle(Palette.green)
                                Text("悉尼 · \(store.profile.days) 天 · 每天约 \(store.profile.maxSteps) 步以内").font(.headline)
                                Text("\(store.profile.start.name)出发并返回 · 每日 A$\(Int(store.profile.budget)) 活动预算").font(.subheadline)
                                Text(store.profile.interests.isEmpty ? "随心探索" : store.profile.interests.joined(separator:" · ")).font(.subheadline).foregroundStyle(Palette.muted)
                            }.padding(24).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:24))
                            Toggle("使用 Nemotron 智能推荐",isOn:$store.live).font(.headline).tint(Palette.green).disabled(store.health?.paidServicesAllowed != true)
                            Text(store.live ? "在线查询地点、路线和天气，AI 提供候选建议。你确认后才保存。" : "演示模式使用示例数据；你也可以打开在线推荐。").font(.caption).foregroundStyle(Palette.muted)
                        }
                    }.id(step).transition(.opacity)
                }.padding(.horizontal,24).padding(.bottom,24)
            }
            VStack(spacing:10) {
                Text(step==7 ? "你做主，Buddy 来帮忙" : "随时可以返回修改").font(.caption).foregroundStyle(Palette.muted)
                PrimaryButton(title:step==7 ? "让 Buddy 规划路线" : "选好了，继续",symbol:step==7 ? "sparkles" : "arrow.right") {
                    UISelectionFeedbackGenerator().selectionChanged()
                    if step<7 {
                        withAnimation(reduceMotion ? nil : .easeInOut(duration:0.2)) { step+=1;feedback=messages[step] }
                    } else {store.profile.date=TripProfile.dateString(selectedDate);store.profile.end=store.profile.start;Task {await store.generate()}}
                }
            }.padding(24).background(Palette.cream)
        }.background(Palette.cream)
    }
    var title:String { ["哪天去悉尼玩？","这次玩几天？","想用什么节奏？","什么最让你心动？","每天准备花多少？","从哪里开始？","想几点出发返回？","一起出发吧！"][step] }
    var subtitle:String { ["选个日子，其他的慢慢来。","先留出时间，不必一次想好所有事。","选舒服的，不用挑战步数纪录。","可以多选，也可以暂时不选。","先定个大概，随时可以修改。","选一个方便的起点。","给探索和休息都留一点空间。","看一眼你的选择，生成后还可以调整。"][step] }
    var messages:[String] { ["每个选择都没有标准答案，适合你就好。","第一步完成！你想在悉尼留多久？","旅行不需要赶进度，按你的节奏来。","让我多了解一点你的喜好。","好体验，也要在舒服的预算里。","已经过半啦！选个出发点就好。","快好啦，最后留好出发和返程时间。","准备完成！我来检查天气、距离和营业时间。"] }
    func respond(_ text:String) { feedback=text;UISelectionFeedbackGenerator().selectionChanged() }
    func choice(_ name:String,detail:String,symbol:String,selected:Bool,action:@escaping()->Void) -> some View {
        Button(action:action) { HStack(spacing:16) { Image(systemName:symbol).font(.title2).frame(width:42).foregroundStyle(Palette.green);VStack(alignment:.leading,spacing:7){Text(name).font(.headline).foregroundStyle(Palette.ink);Text(detail).font(.caption).foregroundStyle(Palette.muted)};Spacer();Image(systemName:selected ? "checkmark.circle.fill" : "circle").foregroundStyle(selected ? Palette.green : Palette.muted) }.padding(20).frame(maxWidth:.infinity,alignment:.leading).background(selected ? Palette.mint : .white,in:RoundedRectangle(cornerRadius:22)).overlay(RoundedRectangle(cornerRadius:22).stroke(selected ? Palette.green : Palette.green.opacity(0.12),lineWidth:2)).padding(.bottom,selected ? 4 : 0).background(Palette.green.opacity(selected ? 0.3 : 0.1),in:RoundedRectangle(cornerRadius:22)) }.buttonStyle(.plain).accessibilityAddTraits(selected ? .isSelected : [])
    }
}

struct CompactTabs: ViewModifier {
    func body(content: Content) -> some View {
        if #available(iOS 18.0, *) { content.tabViewStyle(.tabBarOnly) } else { content }
    }
}

struct BuddyPressStyle: ButtonStyle {
    @Environment(\.accessibilityReduceMotion) var reduceMotion
    func makeBody(configuration:Configuration) -> some View {
        configuration.label.offset(y:configuration.isPressed ? 3 : 0).opacity(configuration.isPressed ? 0.85 : 1).animation(reduceMotion ? nil : .easeOut(duration:0.12),value:configuration.isPressed)
    }
}
