import SwiftUI

struct PetMessage: Codable, Identifiable { var id: String; var role: String; var text: String }
struct PetSession: Codable {
    var id: String; var messages: [PetMessage]; var fields: [String:String]; var pendingMemory: String
    var nextField: String; var question: String; var options: [String]; var completed: Int; var total: Int
}
struct PetEvent: Decodable { var type: String; var text: String?; var error: String?; var session: PetSession? }
let petFields: [(String,String)] = [("destination","目的地"),("date","出发日期 YYYY-MM-DD"),("days","旅行天数"),("companions","同行伙伴"),("budget","每人每日活动预算 AUD"),("interests","这次想玩什么"),("pace","旅行节奏"),("notes","补充要求")]

@MainActor final class PetChatStore: ObservableObject {
    @Published var session: PetSession?
    @Published var busy = false
    @Published var streaming = ""
    @Published var outgoing = ""
    @Published var error: String?
    @Published var memories: [Memory] = []
    @Published var choices = true
    private var scope = "draft"
    private let base = URL(string:"http://127.0.0.1:8787")!
    private var pendingRequest: (String,String)?
    private var storageKey: String { "petSession." + scope }
    private func request<T:Decodable>(_ path:String, method:String="GET", body:[String:Any]?=nil) async throws -> T {
        var req=URLRequest(url:base.appendingPathComponent(path));req.httpMethod=method;TravelDataSource.current.apply(to:&req);req.timeoutInterval=120
        req.setValue("application/json",forHTTPHeaderField:"Content-Type")
        if let body {req.httpBody=try JSONSerialization.data(withJSONObject:body)}
        let (data,response)=try await URLSession.shared.data(for:req)
        guard let http=response as? HTTPURLResponse,(200..<300).contains(http.statusCode) else {
            let obj=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any]
            throw NSError(domain:"Pet",code:1,userInfo:[NSLocalizedDescriptionKey:obj?["error"] as? String ?? "暂时连不上 Buddy，请检查电脑上的服务。"])
        }
        return try JSONDecoder().decode(T.self,from:data)
    }
    private func run(_ action:() async throws -> Void) async {
        guard !busy else{return};busy=true;error=nil
        defer{busy=false}
        do{try await action()}catch{self.error=error.localizedDescription}
    }
    func load(scope:String, seed:[String:String]=[:]) async {
        guard !busy else{return}
        if self.scope==scope,session != nil{return}
        self.scope=scope;session=nil;streaming="";outgoing="";pendingRequest=nil
        choices=UserDefaults.standard.object(forKey:"petChoices") as? Bool ?? true
        await run {
            if let id=UserDefaults.standard.string(forKey:storageKey) {session=try await request("pet/sessions/"+id)}
            else {let value:PetSession=try await request("pet/sessions",method:"POST",body:["fields":seed]);session=value;UserDefaults.standard.set(value.id,forKey:storageKey)}
        }
    }
    func reloadSession() async { session=nil;await load(scope:scope) }
    func mode(_ value:Bool){choices=value;UserDefaults.standard.set(value,forKey:"petChoices")}
    func send(_ text:String, field:String?=nil, value:String?=nil) async {
        guard let id=session?.id,!text.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty else{return}
        await run {
            let fingerprint=text+"|"+(field ?? "")+"|"+(value ?? "")
            let requestID=pendingRequest?.0 == fingerprint ? pendingRequest!.1 : UUID().uuidString
            pendingRequest=(fingerprint,requestID);outgoing=text;streaming=""
            var body:[String:Any] = ["message":text,"requestId":requestID]
            if let field {body["field"]=field;body["value"]=value ?? text}
            var req=URLRequest(url:base.appendingPathComponent("pet/sessions/\(id)/turn"));req.httpMethod="POST";TravelDataSource.current.apply(to:&req);req.timeoutInterval=120
            req.setValue("application/json",forHTTPHeaderField:"Content-Type");req.httpBody=try JSONSerialization.data(withJSONObject:body)
            let(bytes,response)=try await URLSession.shared.bytes(for:req)
            guard let http=response as? HTTPURLResponse,(200..<300).contains(http.statusCode) else{throw NSError(domain:"Pet",code:2,userInfo:[NSLocalizedDescriptionKey:"这次没有发送成功，请重试。"])}
            var completed=false
            for try await line in bytes.lines {
                guard !line.isEmpty else{continue}
                let event=try JSONDecoder().decode(PetEvent.self,from:Data(line.utf8))
                if event.type=="reply" {streaming=event.text ?? ""}
                if event.type=="error" {throw NSError(domain:"Pet",code:3,userInfo:[NSLocalizedDescriptionKey:event.error ?? "请重试"])}
                if let final=event.session {session=final;completed=true;streaming="";outgoing="";pendingRequest=nil}
            }
            if !completed{throw NSError(domain:"Pet",code:4,userInfo:[NSLocalizedDescriptionKey:"回复中断，请重试；同一条消息不会重复保存。"])}
        }
        if error != nil {streaming="";outgoing=""}
    }
    func update(_ fields:[String:String]) async {
        guard let id=session?.id else{return}
        await run{session=try await request("pet/sessions/"+id,method:"PATCH",body:["fields":fields])}
    }
    func decideMemory(_ consent:Bool) async {
        guard let id=session?.id else{return}
        await run{session=try await request("pet/sessions/\(id)/memory",method:"POST",body:["consent":consent])}
    }
    func refreshMemories() async {await run{memories=try await request("memories")}}
    func deleteMemory(_ id:String) async {
        await run{let _: [String:Bool]=try await request("memories/"+id,method:"DELETE");memories=try await request("memories")}
    }
    func editMemory(_ memory:Memory, text:String) async {
        await run {
            let _:Memory=try await request("memories/"+memory.id,method:"PUT",body:["summary":text,"consent":true,"profile":[: ] as [String:String]])
            memories=try await request("memories")
        }
    }
    func clear() async {
        await run {
            if let id=session?.id ?? UserDefaults.standard.string(forKey:storageKey){let _:[String:Bool]=try await request("pet/sessions/"+id,method:"DELETE")}
            UserDefaults.standard.removeObject(forKey:storageKey)
            let value:PetSession=try await request("pet/sessions",method:"POST",body:[:]);session=value;UserDefaults.standard.set(value.id,forKey:storageKey)
            pendingRequest=nil;streaming="";outgoing=""
        }
    }
}

enum PetMood { case greeting, listening, thinking, remembering, ready, confused }
struct PetAvatar: View {
    var mood:PetMood = .greeting
    @Environment(\.accessibilityReduceMotion) var reduceMotion
    @State private var float=false
    private var symbol:String {switch mood{case .thinking:return "ellipsis";case .remembering:return "heart.fill";case .ready:return "checkmark";case .confused:return "questionmark";case .listening:return "ear.fill";case .greeting:return "sparkles"}}
    var body:some View {
        ZStack {
            Circle().fill(LinearGradient(colors:[Palette.mint,Color(red:0.91,green:0.96,blue:0.83)],startPoint:.topLeading,endPoint:.bottomTrailing)).frame(width:94,height:94)
            Ellipse().fill(Palette.green.opacity(0.12)).frame(width:55,height:8).offset(y:41)
            ZStack {
                Ellipse().fill(Color(red:0.65,green:0.73,blue:0.73)).frame(width:49,height:48).offset(y:21)
                Capsule().fill(Palette.green).frame(width:43,height:10).rotationEffect(.degrees(-5)).offset(y:14)
                Capsule().fill(Palette.green).frame(width:10,height:21).rotationEffect(.degrees(-15)).offset(x:14,y:23)
                ForEach([-1.0,1.0],id:\.self){side in
                    Circle().fill(Color(red:0.66,green:0.74,blue:0.74)).frame(width:31,height:31).overlay(Circle().fill(Color(red:0.94,green:0.80,blue:0.78)).padding(7)).offset(x:side*31,y:-23)
                    Ellipse().fill(Color(red:0.74,green:0.80,blue:0.79)).frame(width:17,height:23).rotationEffect(.degrees(side * (float ? 28 : 8))).offset(x:side*29,y:19)
                }
                Ellipse().fill(LinearGradient(colors:[Color(red:0.84,green:0.88,blue:0.86),Color(red:0.71,green:0.79,blue:0.78)],startPoint:.top,endPoint:.bottom)).frame(width:70,height:61).offset(y:-11)
                HStack(spacing:25){ForEach(0..<2){_ in Capsule().fill(Palette.ink).frame(width:5,height:mood == .remembering ? 4 : 9)}}.offset(y:-15)
                Ellipse().fill(Palette.ink).frame(width:16,height:23).overlay(Ellipse().fill(.white.opacity(0.25)).frame(width:5,height:8).offset(x:-3,y:-4)).offset(y:-7)
                HStack(spacing:39){Circle().fill(Color.pink.opacity(0.25)).frame(width:11);Circle().fill(Color.pink.opacity(0.25)).frame(width:11)}.offset(y:-2)
                Path { path in path.move(to:CGPoint(x:0,y:0));path.addQuadCurve(to:CGPoint(x:12,y:0),control:CGPoint(x:6,y:8)) }.stroke(Palette.ink,style:StrokeStyle(lineWidth:2,lineCap:.round)).frame(width:12,height:6).offset(y:9)
            }.rotationEffect(.degrees(reduceMotion ? 0 : (float ? (mood == .confused ? -9 : 3) : -3))).offset(y:float && !reduceMotion ? -2 : 2)
            Image(systemName:symbol).font(.system(size:12,weight:.bold)).foregroundStyle(mood == .remembering ? .pink : Palette.green).padding(8).background(.white,in:Circle()).offset(x:36,y:-34)
        }.frame(width:104,height:108).onAppear{if !reduceMotion{withAnimation(.easeInOut(duration:1.4).repeatForever(autoreverses:true)){float=true}}}
        .accessibilityElement(children:.ignore).accessibilityLabel(mood == .thinking ? "Buddy 正在想一想" : "考拉 Buddy")
    }
}

struct PetChatView:View {
    @EnvironmentObject var store:AppStore
    @StateObject private var chat=PetChatStore()
    @State private var message=""
    @State private var review=false
    @State private var memories=false
    @State private var clearing=false
    @State private var fields:[String:String]=[:]
    @Environment(\.dismiss) private var dismiss
    var onboarding=false
    var conversationOnly=false
    private var scope:String {onboarding ? "draft" : (store.trip?.id ?? "draft")}
    var body:some View {
        VStack(spacing:0) {
            HStack(spacing:12) {
                PetAvatar(mood:chat.busy ? .thinking : chat.error != nil ? .confused : !(chat.session?.pendingMemory ?? "").isEmpty ? .remembering : chat.session?.completed == 8 ? .ready : !message.isEmpty ? .listening : .greeting)
                VStack(alignment:.leading,spacing:5){Text("Buddy 的小背包").font(.title2.bold());Text(chat.busy ? "在认真听你说…" : "一起聊聊，慢慢准备出发").font(.caption).foregroundStyle(Palette.muted);if let s=chat.session{ProgressView(value:Double(s.completed),total:Double(s.total)).tint(Palette.green)}}
                Spacer(minLength:0)
            }.padding(.horizontal,20)
            Picker("回答方式",selection:Binding(get:{chat.choices},set:{chat.mode($0)})){Text("点选回答").tag(true);Text("自由聊").tag(false)}.pickerStyle(.segmented).padding(.horizontal,20).padding(.bottom,10)
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment:.leading,spacing:14){
                        ForEach(chat.session?.messages ?? []){item in bubble(item.text,user:item.role=="user")}
                        if !chat.outgoing.isEmpty{bubble(chat.outgoing,user:true)}
                        if !chat.streaming.isEmpty{bubble(chat.streaming,user:false)}
                        else if chat.busy {HStack{ProgressView();Text("Buddy 想一想…").font(.caption)}}
                        if let s=chat.session,!s.pendingMemory.isEmpty {
                            VStack(alignment:.leading,spacing:10){Text("以后也这样安排，好吗？").font(.headline);Text(s.pendingMemory);HStack{Button("以后都记住"){Task{await chat.decideMemory(true)}}.buttonStyle(.borderedProminent);Button("仅限这次"){Task{await chat.decideMemory(false)}}.buttonStyle(.bordered)}}.padding(16).background(Palette.mint,in:RoundedRectangle(cornerRadius:20)).disabled(chat.busy)
                        }
                        if chat.choices,let s=chat.session,!s.nextField.isEmpty {
                            Text(s.question).font(.headline)
                            ForEach(s.options,id:\.self){option in Button{Task{await chat.send(option,field:s.nextField)}}label:{HStack{Text(option);Spacer();Image(systemName:"chevron.right")}.padding(14).background(.white,in:RoundedRectangle(cornerRadius:16))}.buttonStyle(.plain).disabled(chat.busy)}
                            HStack{Button("跳过这题"){Task{await chat.send("跳过",field:s.nextField,value:"未确定")}};Spacer();Button("其他，自己说"){chat.mode(false)}}.font(.caption).disabled(chat.busy)
                        }
                        if let error=chat.error{Text(error).font(.caption).foregroundStyle(.red)}
                        Color.clear.frame(height:1).id("bottom")
                    }.padding(20)
                }.onChange(of:chat.streaming){_,_ in proxy.scrollTo("bottom",anchor:.bottom)}.onChange(of:chat.session?.messages.count){_,_ in proxy.scrollTo("bottom",anchor:.bottom)}
            }
            if onboarding,let session=chat.session,session.nextField.isEmpty {
                VStack(spacing:8) {
                    Text("回答完成，准备出发啦！").font(.headline)
                    PrimaryButton(title:"保存偏好，去我的行程",symbol:"map.fill") {
                        fields=session.fields
                        Task{await preparePlan()}
                    }.disabled(chat.busy || store.busy)
                    Text("先收好偏好，再到我的行程生成路线。").font(.caption2).foregroundStyle(Palette.muted)
                }.padding(.horizontal,20).padding(.vertical,12)
            }
            if !chat.choices {
                HStack(alignment:.bottom){TextField("告诉 Buddy 你的想法…",text:$message,axis:.vertical).lineLimit(1...4).padding(12).background(.white,in:RoundedRectangle(cornerRadius:16));Button{let text=message;Task{await chat.send(text);if chat.error==nil{message=""}}}label:{Image(systemName:"arrow.up.circle.fill").font(.system(size:34))}.disabled(chat.busy || message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || message.count>2000)}.padding(.horizontal,20).padding(.vertical,8)
                Text("AI 会收到这次对话和已确认偏好。长期保存需你确认。").font(.caption2).foregroundStyle(Palette.muted)
            }
            HStack {
                Button("我的记忆"){memories=true}
                Spacer()
                Button("检查旅行信息"){fields=chat.session?.fields ?? [:];review=true}.fontWeight(.bold)
                Menu{Button("清空本次对话",role:.destructive){clearing=true};if onboarding{Button("先看看已有行程"){store.showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");Task{await store.reload()}}}}label:{Image(systemName:"ellipsis.circle")}
            }.font(.subheadline).padding(20).disabled(chat.busy)
        }.background(Palette.cream).foregroundStyle(Palette.ink)
        .task(id:scope){var seed:[String:String]=[:];if !onboarding,let t=store.trip{seed=["destination":"悉尼","date":t.profile.date,"days":String(t.profile.days),"budget":String(Int(t.profile.budget)),"interests":t.profile.interests.joined(separator:"、"),"pace":"约\(t.profile.maxSteps)步"]};await chat.load(scope:scope,seed:seed)}
        .sheet(isPresented:$review){reviewSheet}
        .sheet(isPresented:$memories){PetMemoryView(chat:chat)}
        .confirmationDialog("清空本次对话和旅行草稿？已保存行程与长期偏好保留。",isPresented:$clearing,titleVisibility:.visible){Button("清空",role:.destructive){Task{await chat.clear()}}}
    }
    private func bubble(_ text:String,user:Bool)->some View {
        HStack{if user{Spacer(minLength:30)};Text(text).font(.subheadline).lineSpacing(4).padding(16).background(user ? Palette.mint : .white,in:RoundedRectangle(cornerRadius:20));if !user{Spacer(minLength:25)}}
    }
    private var reviewSheet:some View {
        NavigationStack {
            Form {
                Section("这次旅行的小背包") {ForEach(petFields,id:\.0){key,label in TextField(label,text:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0}))}}
                Section{Text(store.live ? "路线使用当前在线规划设置。" : "宠物自由聊天使用在线 AI；路线草案目前使用示例地点和路线数据。")
                    Text("目前支持生成悉尼 1–3 天的路线。其他目的地可先保存资料。未确定的日期、天数和预算请补齐；预算为每人每日活动费用。").font(.caption)}
                Section {
                    Button("保存修改"){Task{await chat.update(fields);if chat.error==nil{review=false}}}.disabled(chat.busy)
                    if !conversationOnly { Button(onboarding ? "保存偏好，去我的行程" : "用这些信息调整行程"){Task{await preparePlan()}}.disabled(chat.busy || store.busy) }
                }
                if let error=chat.error{Text(error).foregroundStyle(.red)}
            }.navigationTitle("检查旅行信息").toolbar{Button("完成"){review=false}}
        }
    }
    private func preparePlan() async {
        await chat.update(fields);guard chat.error==nil else{return}
        let profile:TripProfile
        do {profile=try PetTripDraft.profile(fields,base:onboarding ? TripProfile() : (store.trip?.profile ?? store.profile))}
        catch {chat.error=error.localizedDescription;review=true;return}
        let request=petFields.map{label in "\(label.1)：\(fields[label.0] ?? "未确定")"}.joined(separator:"；")
        review=false
        if onboarding {store.completePetPreferences(profile,fields:fields);return}
        dismiss()
        await store.generateFromPet(profile,request:request)
    }
}

struct PetMemoryView:View {
    @ObservedObject var chat:PetChatStore
    @Environment(\.dismiss) private var dismiss
    @State private var edit:Memory?
    @State private var text=""
    @State private var deleting:Memory?
    var body:some View {
        NavigationStack {
            List {
                Section("本次旅行") {ForEach(petFields,id:\.0){key,label in if let value=chat.session?.fields[key]{LabeledContent(label,value:value)}}}
                Section("长期偏好 · 下次也会参考") {
                    if chat.memories.isEmpty{Text("还没有长期偏好。聊一聊，确认后我再记住。")}
                    ForEach(chat.memories){m in VStack(alignment:.leading,spacing:8){Text(m.summary);HStack{Button("修改"){edit=m;text=m.summary};Spacer();Button("撤回",role:.destructive){deleting=m}}.font(.caption)}}
                }
                if let error=chat.error{Text(error).foregroundStyle(.red)}
            }.navigationTitle("Buddy 记住的事").toolbar{Button("完成"){dismiss()}}.task{await chat.refreshMemories()}
            .alert("修改长期偏好",isPresented:Binding(get:{edit != nil},set:{if !$0{edit=nil}})){TextField("偏好",text:$text);Button("取消",role:.cancel){edit=nil};Button("保存"){if let m=edit{Task{await chat.editMemory(m,text:text)}};edit=nil}.disabled(text.trimmingCharacters(in:.whitespaces).isEmpty)}
            .confirmationDialog("撤回后，未来对话和规划将不再检索这条偏好。",isPresented:Binding(get:{deleting != nil},set:{if !$0{deleting=nil}})){Button("撤回偏好",role:.destructive){if let m=deleting{Task{await chat.deleteMemory(m.id)}};deleting=nil}}
        }
    }
}


struct PetTripDraft {
    static func profile(_ fields:[String:String],base:TripProfile=TripProfile()) throws -> TripProfile {
        func failure(_ message:String)->NSError{NSError(domain:"PetDraft",code:1,userInfo:[NSLocalizedDescriptionKey:message])}
        guard ["悉尼","sydney"].contains((fields["destination"] ?? "").trimmingCharacters(in:.whitespacesAndNewlines).lowercased()) else{throw failure("这份资料可保存。当前只能生成悉尼路线，其他城市暂未接入。")}
        guard let days=Int(fields["days"] ?? ""),(1...3).contains(days),let budget=Double(fields["budget"] ?? ""),budget.isFinite,budget>0,budget<=10000 else{throw failure("请填写 1–3 天和有效的每日活动预算（纯数字，最多 10000 AUD）。")}
        let format=DateFormatter();format.locale=Locale(identifier:"en_US_POSIX");format.dateFormat="yyyy-MM-dd";format.isLenient=false
        guard let date=fields["date"],let parsed=format.date(from:date),format.string(from:parsed)==date else{throw failure("请填写 YYYY-MM-DD 格式的有效出发日期。")}
        var result=base;result.date=date;result.days=days;result.budget=budget
        let interests=fields["interests"] ?? "";result.interests=["风景","美食","自然","文化","艺术","城市"].filter{interests.contains($0)}
        let pace=fields["pace"] ?? ""
        if let range=pace.range(of:"[0-9]{3,5}",options:.regularExpression),let steps=Int(pace[range]) {
            guard (1000...30000).contains(steps) else{throw failure("每日步数请设在 1000–30000 之间。")};result.maxSteps=steps
        } else {result.maxSteps=pace.contains("慢") || pace.contains("少走") ? 6000 : pace.contains("多逛") ? 15000 : 10000}
        return result
    }
}

/// Five calm cards share the same persisted session as the optional AI conversation.
struct PetWelcomeView: View {
    @EnvironmentObject private var store: AppStore
    @StateObject private var chat = PetChatStore()
    @State private var step = 0
    @State private var values: [String:String] = [:]
    @State private var talking = false
    @State private var restarting = false
    @State private var loaded = false
    @State private var problem: String?
    private let titles = ["先从悉尼开始，好吗？", "哪天一起出发？", "这次和谁一起呀？", "什么会让你眼睛发亮？", "你喜欢怎样的节奏？", "小背包，装好啦"]
    private let hints = ["我是 Buddy，你的考拉旅行搭子。", "留一点时间，给期待的风景。", "一个人的自在，或一群人的快乐。", "可以选好几个，我都帮你装进去。", "不用赶路，按舒服的步调走。", "看一眼，我们就出发。"]
    private var working: Bool { chat.busy || store.busy }
    private var day: Binding<Date> { Binding(get:{ date(values["date"]) },set:{ values["date"] = Self.format.string(from:$0) }) }
    private static var format: DateFormatter { let f=DateFormatter();f.locale=Locale(identifier:"en_US_POSIX");f.dateFormat="yyyy-MM-dd";return f }
    private func date(_ value: String?) -> Date { value.flatMap{Self.format.date(from:$0)} ?? Date() }
    private var defaults: [String:String] { ["destination":"悉尼","date":Self.format.string(from:Date()),"days":"1","companions":"一个人","budget":"100","interests":"自然、风景","pace":"适中（约10000步）","notes":"没有其他要求"] }
    var body: some View {
        VStack(spacing:0) {
            HStack(spacing:16) {
                Button { step=max(0,step-1) } label:{Image(systemName:"chevron.left").font(.headline).frame(width:36,height:36)}.disabled(step==0 || working).accessibilityLabel("上一题")
                ProgressView(value:Double(step),total:5).tint(Palette.green)
                Text(step==5 ? "准备出发" : "\(step+1) / 5").font(.caption.bold()).foregroundStyle(Palette.muted)
                Menu { Button("重新体验开场"){restarting=true}; Button("查看已有行程"){store.showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");Task{await store.reload()}} } label:{Image(systemName:"ellipsis").frame(width:32,height:36)}.disabled(working)
            }.padding(.horizontal,20).padding(.top,12)
            ScrollView {
                VStack(spacing:22) {
                    ZStack {
                        Circle().fill(Palette.mint.opacity(0.5)).frame(width:190,height:190)
                        Image(systemName:"leaf.fill").font(.system(size:22)).rotationEffect(.degrees(-25)).foregroundStyle(Palette.green.opacity(0.4)).offset(x:-85,y:-40)
                        PetAvatar(mood:working ? .thinking : step==5 ? .ready : .greeting).scaleEffect(1.65)
                    }.frame(height:205).padding(.top,12)
                    VStack(spacing:10) {
                        Text(titles[step]).font(.system(size:27,weight:.bold,design:.rounded)).multilineTextAlignment(.center)
                        Text(hints[step]).font(.subheadline).foregroundStyle(Palette.muted).multilineTextAlignment(.center)
                    }
                    VStack(spacing:12) { cardContent }.padding(.top,8)
                    if let problem { Label(problem,systemImage:"exclamationmark.circle").font(.subheadline).foregroundStyle(.red) }
                    if let error=chat.error { VStack(spacing:10){Text(error).font(.caption).foregroundStyle(.red);Button("重新连接"){Task{await load()}}} }
                }.padding(.horizontal,26).padding(.bottom,24)
            }.scrollDismissesKeyboard(.interactively)
            VStack(spacing:14) {
                PrimaryButton(title:step==5 ? "保存偏好，去我的行程" : "就这样，继续",symbol:step==5 ? "suitcase.rolling.fill" : "arrow.right") { Task{await advance()} }.disabled(working || !loaded)
                if step<5 {
                    HStack {
                        Button("先用推荐选项"){Task{await advance()}}.font(.caption).foregroundStyle(Palette.muted)
                        Spacer()
                        Button { Task{await openChat()} } label:{Label("我想自己说",systemImage:"bubble.left.and.bubble.right")}.font(.subheadline.bold())
                    }.disabled(working || !loaded)
                } else {
                    Text("先保存这次旅行偏好，到“我的行程”再生成路线。").font(.caption).foregroundStyle(Palette.muted)
                }
            }.padding(.horizontal,26).padding(.top,12).padding(.bottom,18).background(Palette.cream)
        }.background(Palette.cream).foregroundStyle(Palette.ink)
        .task { await load() }
        .onChange(of:step){_,value in if loaded{UserDefaults.standard.set(value,forKey:"petWelcome.step")}}
        .onChange(of:values){_,value in if loaded,let id=chat.session?.id,let data=try? JSONEncoder().encode(value){UserDefaults.standard.set(data,forKey:"petWelcome.values."+id)}}
        .sheet(isPresented:$talking,onDismiss:{Task{await syncChat()}}){NavigationStack{PetChatView(conversationOnly:true).toolbar{ToolbarItem(placement:.topBarTrailing){Button("回到小背包"){talking=false}}}}}
        .confirmationDialog("重新开始这次提问？历史行程和长期偏好保留。",isPresented:$restarting,titleVisibility:.visible){Button("重新开始",role:.destructive){Task{await chat.clear();if chat.error==nil{values=defaults;step=0;await chat.update(values);loaded=chat.error==nil;UserDefaults.standard.set(0,forKey:"petWelcome.step")}}}}
    }
    @ViewBuilder private var cardContent: some View {
        switch step {
        case 0:
            option("悉尼",detail:"海港、花园，还有街角的小惊喜",icon:"sun.horizon.fill",selected:true){}
            Text("这一版先陪你探索悉尼。其他城市还在准备中。").font(.caption).foregroundStyle(Palette.muted)
        case 1:
            DatePicker("出发日期",selection:day,displayedComponents:.date).datePickerStyle(.compact).padding(18).background(.white,in:RoundedRectangle(cornerRadius:20))
            HStack(spacing:10){ForEach(1...3,id:\.self){n in Button{values["days"]=String(n)}label:{Text("\(n) 天").font(.headline).frame(maxWidth:.infinity).padding(.vertical,18).background(values["days"]==String(n) ? Palette.mint : .white,in:RoundedRectangle(cornerRadius:18)).overlay(RoundedRectangle(cornerRadius:18).stroke(values["days"]==String(n) ? Palette.green : Color.clear,lineWidth:2))}.buttonStyle(.plain)}}
        case 2:
            ForEach([("一个人","person.fill"),("两个人","heart.fill"),("和朋友","person.2.fill"),("和家人","house.fill")],id:\.0){label,icon in option(label,icon:icon,selected:values["companions"]==label){values["companions"]=label}}
        case 3:
            ForEach([("自然","leaf.fill"),("风景","sun.max.fill"),("美食","fork.knife"),("文化","building.columns.fill"),("艺术","paintpalette.fill"),("城市","building.2.fill")],id:\.0){label,icon in option(label,icon:icon,selected:(values["interests"] ?? "").contains(label)){var picked=(values["interests"] ?? "").components(separatedBy:"、").filter{!$0.isEmpty};if picked.contains(label){picked.removeAll{$0==label}}else{picked.append(label)};values["interests"]=picked.joined(separator:"、")}}
        case 4:
            ForEach([("慢悠悠（约6000步）","多坐一会儿，多看看风景","tortoise.fill"),("适中（约10000步）","走走停停，刚刚好","figure.walk"),("多逛逛（约15000步）","想把一天装得满一点","shoeprints.fill")],id:\.0){label,detail,icon in option(label,detail:detail,icon:icon,selected:values["pace"]==label){values["pace"]=label}}
        default:
            VStack(spacing:16){summary("目的地","\(values["destination"] ?? "悉尼") · \(values["days"] ?? "1") 天");summary("出发",values["date"] ?? "");summary("同行",values["companions"] ?? "");summary("喜欢",values["interests"] ?? "");summary("节奏",values["pace"] ?? "")}.padding(20).background(.white,in:RoundedRectangle(cornerRadius:24))
            HStack{Text("每日活动预算").font(.subheadline);Spacer();TextField("100",text:Binding(get:{values["budget"] ?? "100"},set:{values["budget"]=$0})).keyboardType(.decimalPad).multilineTextAlignment(.trailing).frame(width:70);Text("AUD").foregroundStyle(Palette.muted)}.padding(18).background(.white,in:RoundedRectangle(cornerRadius:18))
            TextField("还有什么想让我照顾的？（选填）",text:Binding(get:{values["notes"] ?? ""},set:{values["notes"]=$0}),axis:.vertical).padding(18).background(.white,in:RoundedRectangle(cornerRadius:18))
            Text("这些回答先放在本次小背包里。长期记住之前，我会再征求你的同意。").font(.caption).foregroundStyle(Palette.muted)
        }
    }
    private func summary(_ label:String,_ value:String)->some View { HStack(alignment:.top){Text(label).foregroundStyle(Palette.muted);Spacer();Text(value).multilineTextAlignment(.trailing).fontWeight(.medium)}.font(.subheadline) }
    private func option(_ title:String,detail:String?=nil,icon:String,selected:Bool,action:@escaping()->Void)->some View {
        Button(action:action){HStack(spacing:14){Image(systemName:icon).font(.system(size:21)).foregroundStyle(Palette.green).frame(width:28);VStack(alignment:.leading,spacing:5){Text(title).font(.system(size:16,weight:.semibold,design:.rounded));if let detail{Text(detail).font(.caption).foregroundStyle(Palette.muted)}};Spacer();Image(systemName:selected ? "checkmark.circle.fill" : "circle").foregroundStyle(selected ? Palette.green : Palette.muted.opacity(0.3))}.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(selected ? Palette.mint.opacity(0.6) : .white,in:RoundedRectangle(cornerRadius:21)).overlay(RoundedRectangle(cornerRadius:21).stroke(selected ? Palette.green : Color.clear,lineWidth:2))}.buttonStyle(.plain).disabled(working)
    }
    private func load() async {
        await chat.load(scope:"draft")
        guard let session=chat.session else{return}
        values=defaults.merging(session.fields){_,new in new}
        if let data=UserDefaults.standard.data(forKey:"petWelcome.values."+session.id),let saved=try? JSONDecoder().decode([String:String].self,from:data){values=values.merging(saved){_,new in new}}
        let key="petWelcome.session"
        step=UserDefaults.standard.string(forKey:key)==session.id ? min(5,max(0,UserDefaults.standard.integer(forKey:"petWelcome.step"))) : 0
        UserDefaults.standard.set(session.id,forKey:key);loaded=true
    }
    private func advance() async {
        guard !working else{return};problem=nil
        if step==3 && (values["interests"] ?? "").isEmpty {problem="选一个喜欢的方向吧，也可以先选自然。";return}
        if step==5 {
            do {
                let profile=try PetTripDraft.profile(values)
                await chat.update(values);guard chat.error==nil else{return}
                store.completePetPreferences(profile,fields:values)
            } catch {problem=error.localizedDescription}
        } else {
            await chat.update(values);guard chat.error==nil else{return}
            step+=1;UserDefaults.standard.set(step,forKey:"petWelcome.step")
        }
    }
    private func openChat() async {await chat.update(values);if chat.error==nil{chat.mode(false);talking=true}}
    private func syncChat() async {
        // The chat sheet owns its own store; reload the shared persisted session after it closes.
        await chat.reloadSession()
        if let session=chat.session{values=defaults.merging(session.fields){_,new in new}}
    }
}
