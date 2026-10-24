import SwiftUI

struct PetMessage: Codable, Identifiable { var id: String; var role: String; var text: String; var mood:String? = nil }
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
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
    private let base = URL(string:"http://127.0.0.1:8787")!
    private var pendingRequest: (String,String)?
    private var storageKey: String { "petSession." + scope }
    private func request<T:Decodable>(_ path:String, method:String="GET", body:[String:Any]?=nil) async throws -> T {
        var req=URLRequest(url:base.appendingPathComponent(path));req.httpMethod=method;req.timeoutInterval=120
        req.setValue("application/json",forHTTPHeaderField:"Content-Type")
        if let body {req.httpBody=try JSONSerialization.data(withJSONObject:body)}
        let (data,response)=try await loader(req)
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
            var req=URLRequest(url:base.appendingPathComponent("pet/sessions/\(id)/turn"));req.httpMethod="POST";req.timeoutInterval=120
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
    func savePreferences(_ profile:TripProfile,consent:Bool) async {
        guard consent else{return}
        await run {
            let memory:Memory=try await request("memories",method:"POST",body:["consent":true,"summary":"喜欢"+profile.interests.joined(separator:"、")+"，每天步数偏好约 \(profile.maxSteps) 步","profile":["interests":profile.interests,"maxSteps":profile.maxSteps]])
            memories.append(memory)
        }
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

enum PetMood:String {case greeting,listening,thinking,remembering,ready,confused,curious,excited,relaxed,speaking
    static func reply(_ value:String?)->PetMood {PetMood(rawValue:value ?? "") ?? .curious}
}
struct PetReaction:Equatable {
    var text:String;var mood:PetMood
    static func answer(_ key:String,_ value:String)->PetReaction {
        switch key {
        case "destination":return .init(text:"悉尼！海风、树荫，还有好多新鲜事。我可以当你的小小旅伴吗？",mood:.excited)
        case "date","days":return .init(text:key=="days" ? "\(value) 天的小冒险，听起来刚刚好！我的小日历准备好啦。":"这一天，听到啦！我已经开始期待出发的阳光了～",mood:.curious)
        case "companions":
            if value.contains("家人"){return .init(text:"全家一起出动呀！听起来，小背包里也要装一份热热闹闹的快乐。",mood:.remembering)}
            if value.contains("朋友"){return .init(text:"好耶，朋友小队！我也想加入，负责给你们加一点快乐～",mood:.excited)}
            if value.contains("两"){return .init(text:"两个人一起发现新地方，真好！我也算半个旅伴吧？",mood:.remembering)}
            return .init(text:"一个人也很棒！你负责自在，我负责陪着你。",mood:.relaxed)
        case "interests":
            if value.contains("美食"){return .init(text:"美食雷达启动！听到好吃的，我的耳朵都竖起来啦。还喜欢什么呀？",mood:.excited)}
            if value.contains("自然") || value.contains("风景"){return .init(text:"你也喜欢大自然！我好想去树荫下闻闻叶子的味道。还想装点什么？",mood:.relaxed)}
            return .init(text:"艺术、故事，还有城市的小秘密！我会带上好奇心，和你一起慢慢看。",mood:.curious)
        case "pace":
            if value.contains("6000") || value.contains("慢"){return .init(text:"慢慢走，看看云。我喜欢这个节奏～小短腿也松了口气。",mood:.relaxed)}
            if value.contains("15000") || value.contains("多逛"){return .init(text:"探险小队集合！让我先伸一伸小短腿，出发时精神满满～",mood:.excited)}
            return .init(text:"走走停停，刚刚好。给路上的小惊喜，也留一点空位吧！",mood:.remembering)
        default:return .init(text:"我在认真听哦，慢慢说就好。",mood:.listening)
        }
    }
}
struct PetAvatar:View {
    var mood:PetMood = .greeting
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var float=false
    @State private var blink=false
    @State private var mouthOpen=false
    private var joyful:Bool {[.excited,.ready,.remembering].contains(mood)}
    private var symbol:String {switch mood{case .thinking:return "ellipsis";case .remembering:return "heart.fill";case .ready:return "checkmark";case .confused:return "questionmark";case .listening:return "ear.fill";case .curious:return "leaf.fill";case .relaxed:return "cloud.sun.fill";case .speaking:return "waveform";default:return "sparkles"}}
    private var tilt:Double {mood == .curious || mood == .confused ? -10 : mood == .listening ? 7:0}
    var body:some View {
        ZStack {
            Ellipse().fill(Palette.green.opacity(0.12)).frame(width:58,height:8).offset(y:47)
            ZStack {
                Ellipse().fill(Color(red:0.67,green:0.77,blue:0.75)).frame(width:49,height:49).offset(y:24)
                Ellipse().fill(Color(red:0.89,green:0.93,blue:0.87)).frame(width:29,height:31).offset(y:28)
                ForEach([-1.0,1.0],id:\.self){side in
                    Ellipse().fill(Color(red:0.58,green:0.70,blue:0.68)).frame(width:19,height:11).offset(x:side*18,y:45)
                    Circle().fill(Color(red:0.65,green:0.76,blue:0.73)).frame(width:35,height:35).overlay(Circle().fill(Color(red:0.96,green:0.79,blue:0.77)).padding(8)).offset(x:side*32,y:-23)
                    Capsule().fill(Color(red:0.71,green:0.81,blue:0.78)).frame(width:16,height:27).rotationEffect(.degrees(side*(joyful ? -135:mood == .listening ? -75:22))).offset(x:side*30,y:joyful ? 3:mood == .listening ? 5:23)
                }
                Ellipse().fill(LinearGradient(colors:[Color(red:0.85,green:0.91,blue:0.86),Color(red:0.70,green:0.81,blue:0.77)],startPoint:.top,endPoint:.bottom)).frame(width:75,height:65).offset(y:-10)
                HStack(spacing:25){eye;eye}.offset(x:mood == .thinking ? 3:0,y:-15)
                Ellipse().fill(Palette.ink).frame(width:17,height:22).overlay(Ellipse().fill(.white.opacity(0.2)).frame(width:5,height:7).offset(x:-3,y:-4)).offset(y:-5)
                HStack(spacing:43){Circle().fill(Color.pink.opacity(joyful ? 0.45:0.22)).frame(width:11);Circle().fill(Color.pink.opacity(joyful ? 0.45:0.22)).frame(width:11)}.offset(y:-1)
                if mood == .speaking && (mouthOpen || reduceMotion) || mood == .excited {Ellipse().fill(Palette.ink).frame(width:12,height:joyful ? 12:8).overlay(Ellipse().fill(.pink.opacity(0.7)).frame(width:7,height:4).offset(y:3)).offset(y:11)}
                else if mood == .thinking || mood == .confused {Ellipse().stroke(Palette.ink,lineWidth:2).frame(width:6,height:7).offset(y:11)}
                else {Path{p in p.move(to:CGPoint(x:0,y:0));p.addQuadCurve(to:CGPoint(x:14,y:0),control:CGPoint(x:7,y:joyful ? 10:6))}.stroke(Palette.ink,style:StrokeStyle(lineWidth:2,lineCap:.round)).frame(width:14,height:7).offset(y:9)}
                ForEach([-1.0,1.0],id:\.self){side in
                    Capsule().fill(Color(red:0.71,green:0.81,blue:0.78)).frame(width:15,height:23)
                        .rotationEffect(.degrees(side*((mood == .excited || mood == .ready) ? -35:20)))
                        .offset(x:side*((mood == .excited || mood == .ready) ? 43:mood == .listening ? 38:19),y:(mood == .excited || mood == .ready) ? -5:mood == .listening ? -9:33)
                }
                Capsule().fill(Palette.green).frame(width:44,height:9).rotationEffect(.degrees(-6)).offset(y:18)
                Capsule().fill(Palette.green).frame(width:10,height:20).rotationEffect(.degrees(-15)).offset(x:15,y:29)
            }.rotationEffect(.degrees(tilt+(reduceMotion ? 0:float ? 2:-2))).offset(y:reduceMotion ? 0:float ? (mood == .excited ? -6:-1):2)
            Image(systemName:symbol).font(.system(size:12,weight:.bold)).foregroundStyle(mood == .remembering ? .pink:Palette.green).padding(8).background(.white,in:Circle()).offset(x:40,y:-34)
        }.frame(width:108,height:116)
        .animation(reduceMotion ? nil:.spring(response:0.32,dampingFraction:0.65),value:mood)
        .onAppear{if !reduceMotion{withAnimation(.easeInOut(duration:1.5).repeatForever(autoreverses:true)){float=true}}}
        .task(id:reduceMotion){guard !reduceMotion else{return};do{while !Task.isCancelled{try await Task.sleep(nanoseconds:3_200_000_000);blink=true;try await Task.sleep(nanoseconds:130_000_000);blink=false}}catch{blink=false}}
        .task(id:mood.rawValue+String(reduceMotion)){mouthOpen=false;guard mood == .speaking && !reduceMotion else{return};do{while !Task.isCancelled{mouthOpen.toggle();try await Task.sleep(nanoseconds:180_000_000)}}catch{mouthOpen=false}}
        .accessibilityElement(children:.ignore).accessibilityLabel("考拉 Buddy，"+mood.rawValue)
    }
    @ViewBuilder private var eye:some View {
        if blink{Capsule().fill(Palette.ink).frame(width:7,height:2)}
        else if joyful || mood == .relaxed {Path{p in p.move(to:CGPoint(x:0,y:5));p.addQuadCurve(to:CGPoint(x:9,y:5),control:CGPoint(x:4.5,y:joyful ? -3:1))}.stroke(Palette.ink,style:StrokeStyle(lineWidth:2.5,lineCap:.round)).frame(width:9,height:8)}
        else {Capsule().fill(Palette.ink).frame(width:mood == .curious ? 6:5,height:mood == .curious ? 11:9).overlay(Circle().fill(.white).frame(width:2,height:2).offset(x:-1,y:-2))}
    }
}
struct PetSpeechBubble:View {
    let text:String
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var visible=""
    var body:some View {
        VStack(alignment:.leading,spacing:5){Text("BUDDY").font(.caption2.bold()).tracking(2).foregroundStyle(Palette.green);ZStack(alignment:.topLeading){Text(text).hidden();Text(visible.isEmpty ? " ":visible)}.font(.system(.body,design:.rounded,weight:.semibold)).lineSpacing(5).fixedSize(horizontal:false,vertical:true).frame(maxWidth:.infinity,alignment:.leading)}
            .padding(20).background(.white,in:RoundedRectangle(cornerRadius:23))
            .overlay(alignment:.leading){RoundedRectangle(cornerRadius:3).fill(.white).frame(width:16,height:16).rotationEffect(.degrees(45)).offset(x:-7,y:18)}
            .accessibilityElement(children:.ignore).accessibilityLabel(text)
            .task(id:text+String(reduceMotion)){visible="";if reduceMotion{visible=text;return};do{for character in text{try Task.checkCancellation();visible.append(character);try await Task.sleep(nanoseconds:22_000_000)}}catch{}}
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
    var draftScope=false
    private var scope:String {(onboarding || draftScope) ? "draft" : (store.trip?.id ?? "draft")}
    private var displayedMessages:[PetMessage] {
        let messages=chat.session?.messages ?? []
        return draftScope && messages.first?.role=="assistant" ? Array(messages.dropFirst()):messages
    }
    var body:some View {
        VStack(spacing:0) {
            HStack(spacing:12) {
                PetAvatar(mood:!chat.streaming.isEmpty ? .speaking:chat.busy ? .thinking:!message.isEmpty ? .listening:chat.error != nil ? .confused:!(chat.session?.pendingMemory ?? "").isEmpty ? .remembering:PetMood.reply(chat.session?.messages.last?.mood))
                VStack(alignment:.leading,spacing:5){Text("Buddy 的小背包").font(.title2.bold());Text(chat.busy ? "在认真听你说…" : "一起聊聊，慢慢准备出发").font(.caption).foregroundStyle(Palette.muted);if let s=chat.session{ProgressView(value:Double(s.completed),total:Double(s.total)).tint(Palette.green)}}
                Spacer(minLength:0)
            }.padding(.horizontal,20)
            Picker("回答方式",selection:Binding(get:{chat.choices},set:{chat.mode($0)})){Text("点选回答").tag(true);Text("自由聊").tag(false)}.pickerStyle(.segmented).padding(.horizontal,20).padding(.bottom,10)
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment:.leading,spacing:14){
                        if draftScope,let session=chat.session {
                            VStack(alignment:.leading,spacing:8){Text("继续准备这次旅行").font(.headline);Text("\(session.fields["destination"] ?? "悉尼") · \(session.fields["days"] ?? "1") 天 · \(session.fields["interests"] ?? "")").font(.subheadline);Text("已有回答会保留，直接告诉 Buddy 你想补充或调整的内容。").font(.caption).foregroundStyle(Palette.muted)}.padding(16).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:18))
                        }
                        ForEach(displayedMessages){item in bubble(item.text,user:item.role=="user",mood:PetMood.reply(item.mood))}
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
        .task(id:scope){var seed:[String:String]=draftScope ? store.pendingTripFields:[:];if !onboarding && !draftScope,let t=store.trip{seed=["destination":"悉尼","date":t.profile.date,"days":String(t.profile.days),"budget":String(Int(t.profile.budget)),"interests":t.profile.interests.joined(separator:"、"),"pace":"约\(t.profile.maxSteps)步"]};await chat.load(scope:scope,seed:seed);if draftScope{chat.mode(false)}}
        .sheet(isPresented:$review){reviewSheet}
        .sheet(isPresented:$memories){PetMemoryView(chat:chat)}
        .confirmationDialog("清空本次对话和旅行草稿？已保存行程与长期偏好保留。",isPresented:$clearing,titleVisibility:.visible){Button("清空",role:.destructive){Task{await chat.clear()}}}
    }
    private func bubble(_ text:String,user:Bool,mood:PetMood = .curious)->some View {
        HStack(alignment:.top){if user{Spacer(minLength:30)}else{PetAvatar(mood:mood).scaleEffect(0.38).frame(width:42,height:48)};Text(text).font(.subheadline).lineSpacing(4).padding(16).background(user ? Palette.mint : .white,in:RoundedRectangle(cornerRadius:20));if !user{Spacer(minLength:25)}}
    }
    private var reviewSheet:some View {
        NavigationStack {
            Form {
                Section("这次旅行的小背包") {ForEach(petFields,id:\.0){key,label in TextField(label,text:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0}))}}
                Section{Text(store.live ? "路线使用当前在线规划设置。" : "宠物自由聊天使用在线 AI；路线草案目前使用示例地点和路线数据。")
                    Text("目前支持生成悉尼 1–15 天的路线。其他目的地可先保存资料。未确定的日期、天数和预算请补齐；预算为每人每日活动费用。").font(.caption)}
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
        guard let days=Int(fields["days"] ?? ""),(1...15).contains(days),let budget=Double(fields["budget"] ?? ""),budget.isFinite,budget>0,budget<=10000 else{throw failure("请填写 1–15 天和有效的每日活动预算（纯数字，最多 10000 AUD）。")}
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

struct PetOnboardingDraft {
    static let groups:[[String]]=[["destination"],["date","days"],["companions"],["interests"],["pace"]]
    static func initialValues(memories:[Memory],today:String=TripProfile.dateString(Date()))->[String:String] {
        var values=["destination":"悉尼","date":today,"days":"1","companions":"一个人","budget":"100","interests":"自然、风景","pace":"适中（约10000步）","notes":"没有其他要求"]
        let sorted=memories.sorted{($0.provenance?.createdAt ?? "")>($1.provenance?.createdAt ?? "")}
        let names=["风景","美食","自然","文化","艺术","城市"]
        if let interests=sorted.lazy.map({m in m.profile.interests.isEmpty ? (m.summary.hasPrefix("喜欢") && !["不喜欢","不要","不想"].contains(where:{m.summary.contains($0)}) ? names.filter{m.summary.contains($0)} : []) : m.profile.interests}).first(where:{!$0.isEmpty}) {values["interests"]=interests.joined(separator:"、")}
        for m in sorted {
            // Legacy text-only records have a default maxSteps=10000, not a confirmed walking limit.
            var steps:Int? = m.profile.interests.isEmpty ? nil : m.profile.maxSteps
            if m.summary.contains("每天步数偏好约"),let range=m.summary.range(of:"[0-9]{3,5}(?= ?步)",options:.regularExpression){steps=Int(m.summary[range])}
            if steps==nil && m.summary.hasPrefix("喜欢") && !["不喜欢","不要","不想"].contains(where:{m.summary.contains($0)}) && ["慢","少走","轻松"].contains(where:{m.summary.contains($0)}){steps=6000}
            if let steps,(1000...30000).contains(steps){values["pace"]=steps==6000 ? "慢悠悠（约6000步）" : steps==10000 ? "适中（约10000步）" : steps==15000 ? "多逛逛（约15000步）" : "沿用偏好（约\(steps)步）";break}
        }
        return values
    }
    static func confirmedPayload(_ values:[String:String],confirmed:Set<String>)->[String:String] {
        Dictionary(uniqueKeysWithValues:petFields.map{($0.0,confirmed.contains($0.0) ? values[$0.0] ?? "" : "")})
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
    @State private var reaction:PetReaction?
    @State private var lastAnswer=""
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var confirmed=Set<String>()
    @State private var rememberPreferences=false
    @State private var finishing=false
    @State private var problem: String?
    private let questions=["嗨！我是 Buddy。\n这次可以陪你去悉尼吗？","我先在小日历上画个圈！你准备哪天出发，玩几天呀？","小背包准备几个位置？\n这次和谁一起去呀？","我想把你喜欢的都装进背包。你看到什么会开心呀？","我走得不快哦～\n这次想慢慢逛，还是多探索一点？","下次也想这样玩吗？我可以记住你喜欢的事物和旅行节奏。"]
    private var sceneMood:PetMood {if chat.error != nil || problem != nil{return .confused};if finishing{return .remembering};return reaction?.mood ?? (step==5 ? .ready:step==0 ? .greeting:.curious)}
    private var speech:String {if let error=chat.error ?? problem{return "刚才绊了一小下，你的选择还在。\n"+error};return reaction?.text ?? questions[step]}
    private var working: Bool { chat.busy || store.busy || finishing }
    private var day: Binding<Date> { Binding(get:{ date(values["date"]) },set:{ choose("date",Self.format.string(from:$0)) }) }
    private static var format: DateFormatter { let f=DateFormatter();f.locale=Locale(identifier:"en_US_POSIX");f.dateFormat="yyyy-MM-dd";return f }
    private func date(_ value: String?) -> Date { value.flatMap{Self.format.date(from:$0)} ?? Date() }
    private var defaults:[String:String] {PetOnboardingDraft.initialValues(memories:chat.memories)}
    var body: some View {
        VStack(spacing:0) {
            HStack(spacing:16) {
                Button { step=max(0,step-1) } label:{Image(systemName:"chevron.left").font(.headline).frame(width:36,height:36)}.disabled(step==0 || working).accessibilityLabel("上一题")
                ProgressView(value:Double(step),total:5).tint(Palette.green)
                Text(step==5 ? "准备出发" : "\(step+1) / 5").font(.caption.bold()).foregroundStyle(Palette.muted)
                Menu { Button("重新体验开场"){restarting=true}; Button("查看已有行程"){store.showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");Task{await store.reload()}} } label:{Image(systemName:"ellipsis").frame(width:32,height:36)}.disabled(working)
            }.padding(.horizontal,20).padding(.top,12)
            HStack(alignment:.center,spacing:8){
                VStack(spacing:2){Button{reaction=PetReaction(text:"嘿嘿，我在这儿！小短腿已经准备好啦～你慢慢选，我陪着你。",mood:.excited)}label:{PetAvatar(mood:sceneMood).scaleEffect(1.15).frame(width:122,height:137)}.buttonStyle(.plain).accessibilityLabel("摸摸 Buddy");Text("戳戳我呀").font(.caption2).foregroundStyle(Palette.muted)}
                PetSpeechBubble(text:speech)
            }.padding(.horizontal,18).padding(.vertical,12)
            if !lastAnswer.isEmpty {HStack{Spacer(minLength:30);Text(lastAnswer).font(.subheadline.bold()).padding(.horizontal,16).padding(.vertical,10).background(Palette.mint,in:RoundedRectangle(cornerRadius:18)).accessibilityLabel("你的回答："+lastAnswer)}.padding(.horizontal,24).padding(.bottom,10)}
            ScrollView {
                VStack(spacing:14) {
                    if !chat.memories.isEmpty && (step==3 || step==4){Text("参考你同意保存的偏好，选项仍可修改。").font(.caption).foregroundStyle(Palette.green)}
                    VStack(spacing:12) { cardContent }.padding(.top,8)
                    if let problem { Label(problem,systemImage:"exclamationmark.circle").font(.subheadline).foregroundStyle(.red) }
                    if let error=chat.error { VStack(spacing:10){Text(error).font(.caption).foregroundStyle(.red);Button("重新连接"){Task{await load()}}} }
                }.padding(.horizontal,26).padding(.bottom,24)
            }.scrollDismissesKeyboard(.interactively)
            VStack(spacing:14) {
                PrimaryButton(title:step==5 ? "去我的行程" : reaction==nil ? "就这样告诉 Buddy" : "接着聊",symbol:step==5 ? "suitcase.rolling.fill" : "arrow.right") { Task{await advance()} }.disabled(working || !loaded)
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
        .onChange(of:rememberPreferences){_,enabled in
            withAnimation(reduceMotion ? nil:.spring(response:0.3,dampingFraction:0.8)) {
                reaction=PetReaction(text:enabled ? "好呀！出发时，我会把这些小喜好收进记忆里。" : "收到，这些选择就放在这次的小背包里。",mood:enabled ? .remembering:.relaxed)
            }
        }
        .onChange(of:confirmed){_,keys in if let id=chat.session?.id{UserDefaults.standard.set(Array(keys),forKey:"petWelcome.confirmed."+id)}}
        .onChange(of:step){_,value in reaction=nil;lastAnswer="";problem=nil;if loaded{UserDefaults.standard.set(value,forKey:"petWelcome.step")}}
        .onChange(of:values){_,value in if loaded,let id=chat.session?.id,let data=try? JSONEncoder().encode(value){UserDefaults.standard.set(data,forKey:"petWelcome.values."+id)}}
        .sheet(isPresented:$talking,onDismiss:{Task{await syncChat()}}){NavigationStack{PetChatView(conversationOnly:true).toolbar{ToolbarItem(placement:.topBarTrailing){Button("回到小背包"){talking=false}}}}}
        .confirmationDialog("重新开始这次提问？历史行程和长期偏好保留。",isPresented:$restarting,titleVisibility:.visible){Button("重新开始",role:.destructive){Task{await chat.clear();if chat.error==nil{values=defaults;confirmed=[];rememberPreferences=false;step=0;loaded=true;UserDefaults.standard.set(chat.session?.id,forKey:"petWelcome.session");UserDefaults.standard.set(0,forKey:"petWelcome.step")}}}}
    }
    @ViewBuilder private var cardContent: some View {
        switch step {
        case 0:
            option("好呀，一起去悉尼！",detail:"海港、花园，还有街角的小惊喜",icon:"sun.horizon.fill",selected:true){choose("destination","悉尼")}
            Text("这一版先陪你探索悉尼。其他城市还在准备中。").font(.caption).foregroundStyle(Palette.muted)
        case 1:
            DatePicker("出发日期",selection:day,displayedComponents:.date).datePickerStyle(.compact).padding(18).background(.white,in:RoundedRectangle(cornerRadius:20))
            VStack(spacing:14) {
                Stepper("旅行天数：\(values["days"] ?? "1") 天",value:Binding(get:{Int(values["days"] ?? "1") ?? 1},set:{choose("days",String($0))}),in:1...15)
                Slider(value:Binding(get:{Double(values["days"] ?? "1") ?? 1},set:{values["days"]=String(Int($0))}),in:1...15,step:1,onEditingChanged:{editing in if !editing{choose("days",values["days"] ?? "1")}}).tint(Palette.green).accessibilityLabel("旅行天数")
                HStack{Text("1 天");Spacer();Text("15 天")}.font(.caption).foregroundStyle(Palette.muted)
            }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:20)).disabled(working)

        case 2:
            ForEach([("一个人","person.fill"),("两个人","heart.fill"),("和朋友","person.2.fill"),("和家人","house.fill")],id:\.0){label,icon in option(label,icon:icon,selected:values["companions"]==label){choose("companions",label)}}
        case 3:
            ForEach([("自然","leaf.fill"),("风景","sun.max.fill"),("美食","fork.knife"),("文化","building.columns.fill"),("艺术","paintpalette.fill"),("城市","building.2.fill")],id:\.0){label,icon in option(label,icon:icon,selected:(values["interests"] ?? "").contains(label)){var picked=(values["interests"] ?? "").components(separatedBy:"、").filter{!$0.isEmpty};if picked.contains(label){picked.removeAll{$0==label}}else{picked.append(label)};choose("interests",picked.joined(separator:"、"),reactingTo:label);if !picked.contains(label){reaction=PetReaction(text:"收到，这个先不放进背包。还有别的喜欢的吗？",mood:.curious)}}}
        case 4:
            if (values["pace"] ?? "").hasPrefix("沿用偏好"){option(values["pace"]!,detail:"你之前同意保存的步数",icon:"heart.fill",selected:true){}}
            ForEach([("慢悠悠（约6000步）","多坐一会儿，多看看风景","tortoise.fill"),("适中（约10000步）","走走停停，刚刚好","figure.walk"),("多逛逛（约15000步）","想把一天装得满一点","shoeprints.fill")],id:\.0){label,detail,icon in option(label,detail:detail,icon:icon,selected:values["pace"]==label){choose("pace",label)}}
            VStack(alignment:.leading,spacing:12) {
                HStack{Text("每天步数上限").font(.headline);Spacer();Text("\(walkingSteps) 步").font(.headline).monospacedDigit()}
                Slider(value:Binding(get:{Double(walkingSteps)},set:{values["pace"]="自定义（约\(Int($0))步）"}),in:1000...30000,step:1000,onEditingChanged:{editing in if !editing{choose("pace",values["pace"] ?? "")}}).tint(Palette.green).accessibilityLabel("每天步数上限")
                HStack{Text("1,000 步");Spacer();Text("30,000 步")}.font(.caption).foregroundStyle(Palette.muted)
                Text("推荐节奏也可以微调，Buddy 会按这个上限安排路线。").font(.caption).foregroundStyle(Palette.muted)
            }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:20)).disabled(working)
        default:
            VStack(spacing:16){summary("目的地","\(values["destination"] ?? "悉尼") · \(values["days"] ?? "1") 天");summary("出发",values["date"] ?? "");summary("同行",values["companions"] ?? "")}.padding(20).background(.white,in:RoundedRectangle(cornerRadius:24))
            VStack(alignment:.leading,spacing:14) {
                HStack{Text("每人每日活动预算").font(.subheadline.bold());Spacer();TextField("100",text:Binding(get:{values["budget"] ?? "100"},set:{values["budget"]=$0})).keyboardType(.decimalPad).multilineTextAlignment(.trailing).frame(width:80).accessibilityLabel("每人每日活动预算");Text("AUD").foregroundStyle(Palette.muted)}
                HStack{ForEach([50,100,200,500],id:\.self){amount in Button{values["budget"]=String(amount)}label:{Text("$\(amount)").font(.subheadline.bold()).frame(maxWidth:.infinity).padding(.vertical,10).background(Double(values["budget"] ?? "")==Double(amount) ? Palette.mint:Palette.cream,in:RoundedRectangle(cornerRadius:12))}.buttonStyle(.plain)}}
                Text("可直接输入金额，最多 10,000 AUD。不包含住宿、交通票价和全部餐饮；活动费用为估算。").font(.caption).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)
            }.padding(18).background(.white,in:RoundedRectangle(cornerRadius:20)).disabled(working)
            TextField("还有什么想让我照顾的？（选填）",text:Binding(get:{values["notes"] ?? ""},set:{values["notes"]=$0}),axis:.vertical).padding(18).background(.white,in:RoundedRectangle(cornerRadius:18))
            VStack(alignment:.leading,spacing:14) {
                Label("留给下次的小记忆",systemImage:"sparkles").font(.headline)
                summary("兴趣",values["interests"] ?? "")
                summary("节奏",values["pace"] ?? "")
                Divider()
                Toggle("下次旅行也参考这些偏好",isOn:$rememberPreferences)
                    .font(.subheadline.weight(.medium)).tint(Palette.green)
                    .accessibilityIdentifier("pet-remember-preferences")
                    .disabled(working)
                Text("仅保存兴趣和节奏，可在记忆页随时删除。日期、同行人和预算只用于这次旅行。")
                    .font(.caption).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)
            }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:22))
        }
    }
    private var walkingSteps:Int {
        let pace=values["pace"] ?? ""
        if let range=pace.range(of:"[0-9]{3,5}",options:.regularExpression),let steps=Int(pace[range]){return min(30000,max(1000,steps))}
        return 10000
    }
    private func summary(_ label:String,_ value:String)->some View { HStack(alignment:.top){Text(label).foregroundStyle(Palette.muted);Spacer();Text(value).multilineTextAlignment(.trailing).fontWeight(.medium)}.font(.subheadline) }
    private func option(_ title:String,detail:String?=nil,icon:String,selected:Bool,action:@escaping()->Void)->some View {
        Button(action:action){HStack(spacing:14){Image(systemName:icon).font(.system(size:21)).foregroundStyle(Palette.green).frame(width:28);VStack(alignment:.leading,spacing:5){Text(title).font(.system(size:16,weight:.semibold,design:.rounded));if let detail{Text(detail).font(.caption).foregroundStyle(Palette.muted)}};Spacer();Image(systemName:selected ? "checkmark.circle.fill" : "circle").foregroundStyle(selected ? Palette.green : Palette.muted.opacity(0.3))}.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(selected ? Palette.mint.opacity(0.6) : .white,in:RoundedRectangle(cornerRadius:21)).overlay(RoundedRectangle(cornerRadius:21).stroke(selected ? Palette.green : Color.clear,lineWidth:2))}.buttonStyle(.plain).disabled(working)
    }
    private func choose(_ key:String,_ value:String,reactingTo:String?=nil) {
        values[key]=value;lastAnswer=key=="days" ? "想玩 \(value) 天":value
        withAnimation(reduceMotion ? nil:.spring(response:0.3,dampingFraction:0.75)){reaction=PetReaction.answer(key,reactingTo ?? value)}
    }
    private func load() async {
        await chat.load(scope:"draft")
        guard let session=chat.session else{return}
        await chat.refreshMemories();guard chat.error==nil else{return}
        values=defaults.merging(session.fields){_,new in new}
        if let data=UserDefaults.standard.data(forKey:"petWelcome.values."+session.id),let saved=try? JSONDecoder().decode([String:String].self,from:data){values=values.merging(saved){_,new in new}}
        let key="petWelcome.session"
        step=UserDefaults.standard.string(forKey:key)==session.id ? min(5,max(0,UserDefaults.standard.integer(forKey:"petWelcome.step"))) : 0
        if let keys=UserDefaults.standard.stringArray(forKey:"petWelcome.confirmed."+session.id){confirmed=Set(keys)}
        else {confirmed=Set(PetOnboardingDraft.groups.prefix(step).flatMap{$0})}
        UserDefaults.standard.set(session.id,forKey:key);loaded=true
    }
    private func advance() async {
        guard !working else{return};problem=nil
        if step==3 && (values["interests"] ?? "").isEmpty {problem="选一个喜欢的方向吧，也可以先选自然。";return}
        if step==5 {
            do {
                _=try PetTripDraft.profile(values)
                await finish(remember:rememberPreferences)
            } catch {problem=error.localizedDescription}
        } else {
            let keys=confirmed.union(PetOnboardingDraft.groups[step])
            await chat.update(PetOnboardingDraft.confirmedPayload(values,confirmed:keys));guard chat.error==nil else{return}
            confirmed=keys;step+=1;UserDefaults.standard.set(step,forKey:"petWelcome.step")
        }
    }
    private func openChat() async {await chat.update(PetOnboardingDraft.confirmedPayload(values,confirmed:confirmed));if chat.error==nil{chat.mode(false);talking=true}}
    private func finish(remember:Bool) async {
        guard !working else{return};finishing=true;defer{finishing=false}
        do {
            let profile=try PetTripDraft.profile(values)
            await chat.update(values);guard chat.error==nil else{return}
            confirmed=Set(values.keys)
            if remember {await chat.savePreferences(profile,consent:true);guard chat.error==nil else{return}}
            reaction=PetReaction(text:remember ? "记住啦！下次我会参考这些偏好。" : "收到！这次就照这个节奏出发。",mood:remember ? .remembering:.ready)
            // Give Buddy's successful acknowledgement time to appear before navigation.
            try await Task.sleep(for:.milliseconds(900))
            store.completePetPreferences(profile,fields:values)
        } catch {problem=error.localizedDescription}
    }
    private func syncChat() async {
        // The chat sheet owns its own store; reload the shared persisted session after it closes.
        await chat.reloadSession()
        if let session=chat.session{values=values.merging(session.fields){_,new in new};confirmed.formUnion(session.fields.keys)}
    }
}
