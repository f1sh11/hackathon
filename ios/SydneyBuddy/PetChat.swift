import SwiftUI

struct PetMessage: Codable, Identifiable { var id: String; var role: String; var text: String; var mood:String? = nil }
struct PetSession: Codable {
    var id: String; var messages: [PetMessage]; var fields: [String:String]; var pendingMemory: String
    var nextField: String; var question: String; var options: [String]; var completed: Int; var total: Int
}
struct PetEvent: Decodable { var type: String; var text: String?; var error: String?; var session: PetSession? }
let petFields: [(String,String)] = [("destination","目的地"),("date","出发日期 YYYY-MM-DD"),("days","旅行天数"),("companions","同行伙伴"),("budget","预算金额"),("interests","这次想玩什么"),("pace","旅行节奏"),("notes","补充要求")]
let travelExtraFields:[(String,String)]=[("budgetCoverage","预算包含范围"),("budgetCurrency","预算币种"),("travelers","同行总人数"),("origin","出发地点或住宿"),("mustDo","特别想去或想做的"),("avoid","必须避开的安排"),("energy","本次精力"),("rhythm","本次节奏"),("queueTolerance","本次排队偏好")]


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
    func load(scope:String, seed:[String:String]=[:],personal:UserTravelProfile?=nil) async {
        guard !busy else{return}
        if self.scope==scope,session != nil{return}
        self.scope=scope;session=nil;streaming="";outgoing="";pendingRequest=nil
        choices=UserDefaults.standard.object(forKey:"petChoices") as? Bool ?? true
        await run {
            if let id=UserDefaults.standard.string(forKey:storageKey) {session=try await request("pet/sessions/"+id)}
            else {let value:PetSession=try await request("pet/sessions",method:"POST",body:["fields":seed,"personalProfile":personal.flatMap{try? JSONSerialization.jsonObject(with:JSONEncoder().encode($0))} ?? [:]]);session=value;UserDefaults.standard.set(value.id,forKey:storageKey)}
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
    func savePersonalPreferences(_ profile:TripProfile,existing:Memory?) async {
        await run {
            let body:[String:Any]=["consent":true,"summary":"喜欢"+profile.interests.joined(separator:"、")+"，每天步数偏好约 \(profile.maxSteps) 步","profile":["interests":profile.interests,"maxSteps":profile.maxSteps]]
            let _:Memory=try await request(existing.map{"memories/"+$0.id} ?? "memories",method:existing == nil ? "POST":"PUT",body:body)
            memories=try await request("memories")
        }
    }
    func saveTravelProfile(_ profile:UserTravelProfile,existing:Memory?) async {
        await run {
            let habits=try JSONSerialization.jsonObject(with:JSONEncoder().encode(profile))
            let body:[String:Any]=["consent":true,"summary":profile.summary.isEmpty ? "旅行习惯暂不确定":profile.summary,"profile":["interests":profile.interests ?? [],"maxSteps":profile.dailySteps ?? 10000,"habits":habits]]
            let _:Memory=try await request(existing.map{"memories/"+$0.id} ?? "memories",method:existing == nil ? "POST":"PUT",body:body)
            memories=try await request("memories")
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

enum PetMood:String {case greeting,listening,thinking,remembering,ready,confused,curious,excited,relaxed,speaking,pouting
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
    private var symbol:String {switch mood{case .pouting:return "exclamationmark";case .thinking:return "ellipsis";case .remembering:return "heart.fill";case .ready:return "checkmark";case .confused:return "questionmark";case .listening:return "ear.fill";case .curious:return "leaf.fill";case .relaxed:return "cloud.sun.fill";case .speaking:return "waveform";default:return "sparkles"}}
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
                else if mood == .pouting {
                    Capsule().fill(Palette.ink).frame(width:11,height:3).offset(y:11)
                    HStack(spacing:20){ForEach([-1.0,1.0],id:\.self){side in Capsule().fill(Palette.ink).frame(width:11,height:3).rotationEffect(.degrees(side * -18))}}.offset(y:-24)
                }
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
    @State private var assumptionsConfirmed=false
    @State private var editingRequirements=false
    @FocusState private var composing:Bool
    @Environment(\.dismiss) private var dismiss
    var onboarding=false
    var conversationOnly=false
    var draftScope=false
    private var scope:String {(onboarding || draftScope) ? "draft" : (store.trip?.id ?? "draft")}
    private var displayedMessages:[PetMessage] {
        let messages=chat.session?.messages ?? []
        if !onboarding && !draftScope,store.trip != nil,messages.first?.role=="assistant" {return Array(messages.dropFirst())}
        return messages
    }
    private var conversationContent:some View {
        VStack(spacing:0) {
            HStack {
                Button(onboarding ? "返回我的行程":"返回") {
                    if onboarding{store.returnToTrips(draft:chat.session?.fields ?? store.pendingTripFields)}else{dismiss()}
                }.font(.subheadline.bold()).accessibilityIdentifier("chat-back")
                Spacer()
                if !onboarding && !draftScope,let trip=store.trip{Text(trip.profile.date).font(.caption).foregroundStyle(Palette.muted)}
            }.padding(.horizontal,20).padding(.vertical,10).disabled(chat.busy)
            HStack(spacing:12) {
                PetAvatar(mood:!chat.streaming.isEmpty ? .speaking:chat.busy ? .thinking:!message.isEmpty ? .listening:chat.error != nil ? .confused:!(chat.session?.pendingMemory ?? "").isEmpty ? .remembering:PetMood.reply(chat.session?.messages.last?.mood))
                VStack(alignment:.leading,spacing:5){Text(!onboarding && !draftScope ? (store.trip?.title ?? "Buddy 的小背包"):"Buddy 的小背包").font(.title2.bold());Text(chat.busy ? "在认真听你说…" : "一起聊聊，慢慢准备出发").font(.caption).foregroundStyle(Palette.muted);if let s=chat.session{ProgressView(value:Double(s.completed),total:Double(s.total)).tint(Palette.green)}}
                Spacer(minLength:0)
            }.padding(.horizontal,20)
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment:.leading,spacing:14){
                        if draftScope,!onboarding,let session=chat.session {
                            VStack(alignment:.leading,spacing:8){Text("继续准备这次旅行").font(.headline);Text(TripDraftSummary(fields:session.fields).title).font(.subheadline);Text("已有回答会保留，直接告诉 Buddy 你想补充或调整的内容。").font(.caption).foregroundStyle(Palette.muted)}.padding(16).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:18))
                        }
                        ForEach(displayedMessages){item in bubble(item.text,user:item.role=="user",mood:PetMood.reply(item.mood))}
                        if !chat.outgoing.isEmpty{bubble(chat.outgoing,user:true)}
                        if !chat.streaming.isEmpty{bubble(chat.streaming,user:false)}
                        else if chat.busy {HStack{ProgressView();Text("Buddy 想一想…").font(.caption)}}
                        if let s=chat.session,!s.pendingMemory.isEmpty {
                            VStack(alignment:.leading,spacing:10){Text("以后也这样安排，好吗？").font(.headline);Text(s.pendingMemory);HStack{Button("以后都记住"){Task{await chat.decideMemory(true)}}.buttonStyle(.borderedProminent);Button("仅限这次"){Task{await chat.decideMemory(false)}}.buttonStyle(.bordered)}}.padding(16).background(Palette.mint,in:RoundedRectangle(cornerRadius:20)).disabled(chat.busy)
                        }
                        if let error=chat.error{Text(error).font(.caption).foregroundStyle(.red)}
                        Color.clear.frame(height:1).id("bottom")
                    }.padding(20)
                }.onChange(of:chat.streaming){_,_ in proxy.scrollTo("bottom",anchor:.bottom)}.onChange(of:chat.session?.messages.count){_,_ in proxy.scrollTo("bottom",anchor:.bottom)}
            }
            if let session=chat.session,!session.nextField.isEmpty {quickAnswers(session)}
            if canPlanDirectly {directPlanningCard}
            else if let blocker=directPlanningBlocker {
                VStack(alignment:.leading,spacing:8){Label(blocker,systemImage:"info.circle").font(.caption).foregroundStyle(Palette.muted)
                    if TravelRequirements.known(liveFields["destination"]),!TravelRequirements.supportedDestination(liveFields["destination"]) {
                        Button("改为悉尼，继续规划"){Task{await chat.send("改为悉尼，保留其他旅行需求",field:"destination",value:"悉尼")}}.font(.subheadline.bold()).disabled(chat.busy)
                    } else if let currency=liveFields["budgetCurrency"],TravelRequirements.known(currency),currency != "AUD" {
                        Button("重新填写澳元活动预算"){Task{await chat.update(["budget":"","budgetCurrency":"AUD","budgetCoverage":"activities"])}}.font(.subheadline.bold()).disabled(chat.busy)
                    } else {Button("修改旅行需求"){fields=liveFields;review=true}.font(.subheadline.bold())}
                }.padding(.horizontal,20).padding(.top,6)
            }
            HStack(alignment:.bottom){
                TextField("告诉 Buddy 你的想法，也可以点上面的选项…",text:$message,axis:.vertical).focused($composing).lineLimit(1...4).padding(12).background(.white,in:RoundedRectangle(cornerRadius:16)).accessibilityIdentifier("chat-composer")
                Button{let text=message;composing=false;Task{await chat.send(text);if chat.error==nil{message=""}}}label:{Image(systemName:"arrow.up.circle.fill").font(.system(size:34))}.accessibilityLabel("发送消息").disabled(chat.busy || message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || message.count>2000)
            }.padding(.horizontal,20).padding(.vertical,8)
            HStack {
                Button("我的记忆"){memories=true}
                Spacer()
                Button("旅行需求摘要"){fields=chat.session?.fields ?? [:];editingRequirements=false;assumptionsConfirmed=false;review=true}.fontWeight(.bold)
                Menu{Button("清空本次对话",role:.destructive){clearing=true}}label:{Image(systemName:"ellipsis.circle")}
            }.font(.subheadline).padding(20).disabled(chat.busy)
        }.background(Palette.cream).foregroundStyle(Palette.ink)
    }
    @State private var choosingDestination=false
    var body:some View {
        ZStack {
            conversationContent.opacity(review ? 0:1).allowsHitTesting(!review).accessibilityHidden(review)
            if review{requirementsPanel}
        }
        .onChange(of:review){_,value in if value{composing=false}}
        .task(id:scope){
            var seed:[String:String]=(onboarding || draftScope) ? store.pendingTripFields:[:]
            if onboarding,seed.isEmpty{seed=store.pendingPersonalProfile.seed}
            if !onboarding && !draftScope,let t=store.trip{seed=["destination":"悉尼","date":t.profile.date,"days":String(t.profile.days),"budget":String(Int(t.profile.budget)),"interests":t.profile.interests.joined(separator:"、"),"pace":"约\(t.profile.maxSteps)步"]}
            await chat.load(scope:scope,seed:seed,personal:(onboarding || draftScope) ? store.pendingPersonalProfile:nil)
        }
        .onChange(of:chat.session?.fields){_,value in
            if onboarding,let value{store.pendingTripFields=value}
            if value != nil{assumptionsConfirmed=false}
            if canPlanDirectly{composing=false}
        }
        .sheet(isPresented:$choosingDestination){destinationIdeas}
        .sheet(isPresented:$memories){PetMemoryView(chat:chat)}
        .confirmationDialog("清空本次对话和旅行草稿？已保存行程与长期偏好保留。",isPresented:$clearing,titleVisibility:.visible){Button("清空",role:.destructive){Task{await chat.clear()}}}
    }
    private var liveFields:[String:String]{chat.session?.fields ?? [:]}
    private var directProfile:TripProfile? {
        guard (onboarding || draftScope),(!conversationOnly || onboarding) else{return nil}
        return try? TravelRequirements.readyProfile(liveFields,personal:store.pendingPersonalProfile)
    }
    private var canPlanDirectly:Bool{directProfile != nil}
    private var directPlanningBlocker:String? {
        guard (!conversationOnly || onboarding),(onboarding || draftScope),let session=chat.session else{return nil}
        let missing=TravelRequirements.missingFields(liveFields)
        if !missing.isEmpty{return session.nextField.isEmpty ? "还需要确认："+missing.joined(separator:"、")+"。直接在下面告诉 Buddy 就好。":nil}
        do{_=try TravelRequirements.readyProfile(liveFields,personal:store.pendingPersonalProfile);return nil}catch{return error.localizedDescription}
    }
    private func quickAnswers(_ session:PetSession)->some View {
        VStack(alignment:.leading,spacing:7){
            HStack(alignment:.firstTextBaseline){Text(canPlanDirectly ? "可选补充："+session.question:session.question).font(.caption).foregroundStyle(Palette.muted).lineLimit(2);Spacer();Button(TravelRequirements.requiredKeys.contains(session.nextField) ? "暂不确定":"稍后补充"){composing=false;Task{await chat.send("稍后补充",field:session.nextField,value:"未确定")}}.font(.caption).disabled(chat.busy)}
            ScrollView(.horizontal,showsIndicators:false){HStack(spacing:8){ForEach(session.options,id:\.self){option in
                Button{composing=false;let label=TravelRequirements.optionTitle(option,field:session.nextField);Task{await chat.send(label,field:session.nextField,value:option)}}label:{Text(TravelRequirements.optionTitle(option,field:session.nextField)).font(.subheadline.weight(.medium)).padding(.horizontal,14).padding(.vertical,10).background(.white,in:Capsule()).overlay(Capsule().stroke(Palette.green.opacity(0.2)))}.buttonStyle(.plain).disabled(chat.busy)
            }}}
            if session.nextField=="destination" {Button("还没想好？让 Buddy 帮我选",systemImage:"sparkles"){choosingDestination=true}.font(.subheadline.bold()).disabled(chat.busy)}
        }.padding(.horizontal,20).padding(.top,8).accessibilityIdentifier("chat-quick-answers")
    }
    private var destinationIdeas:some View {
        NavigationStack {
            ScrollView {VStack(alignment:.leading,spacing:18){
                HStack{PetAvatar(mood:.curious).frame(width:100,height:110);Text("哼唧，选不出来就交给我嘛～先看看你想过哪种小假期？").font(.headline)}
                Text("目前能实际生成路线的城市是悉尼。下面是悉尼的玩法灵感，选择后仍可继续调整需求。").font(.caption).foregroundStyle(Palette.muted)
                if !store.pendingPersonalProfile.summary.isEmpty{Text("我记得你："+store.pendingPersonalProfile.summary).font(.caption).foregroundStyle(Palette.green)}
                ForEach([("海风与绿意","歌剧院外观、皇家植物园，适合喜欢自然与风景的你。"),("城市与美食","岩石区、城市街区，给逛吃留些时间。"),("艺术与故事","博物馆、美术馆，慢慢发现城市的另一面。")],id:\.0){title,detail in
                    VStack(alignment:.leading,spacing:10){Text(title).font(.headline);Text(detail).font(.subheadline).foregroundStyle(Palette.muted)
                        Button("选悉尼，继续聊"){choosingDestination=false;Task{let interests=title=="海风与绿意" ? "自然、风景":title=="城市与美食" ? "城市、美食":"艺术、文化";await chat.update(["interests":interests]);if chat.error==nil{await chat.send("这次想体验"+title+"，我选悉尼",field:"destination",value:"悉尼")}}}.buttonStyle(.borderedProminent).tint(Palette.green)
                    }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:18))
                }
            }.padding(20)}.background(Palette.cream).navigationTitle("Buddy 帮你选").navigationBarTitleDisplayMode(.inline).toolbar{Button("关闭"){choosingDestination=false}}
        }
    }
    private var directPlanningCard:some View {
        VStack(alignment:.leading,spacing:8){
            HStack{Label("旅行需求已齐",systemImage:"checkmark.circle.fill").font(.subheadline.bold()).foregroundStyle(Palette.green);Spacer();Button("修改"){fields=liveFields;editingRequirements=false;review=true}.font(.caption)}
            Text(TripDraftSummary(fields:liveFields).title+" · "+(liveFields["date"] ?? "")).font(.caption)
            if let profile=directProfile{Text("每人每日活动预算约 "+String(format:"%.2f",profile.budget)+" AUD").font(.caption).foregroundStyle(Palette.muted)}
            let assumptions=TravelRequirements.assumptions(liveFields,personal:store.pendingPersonalProfile)
            if let origin=assumptions.first(where:{$0.contains("起终点") || $0.contains("尚未定位")}){Text(origin).font(.caption2).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)}
            DisclosureGroup("查看临时假设与待核实信息") {ScrollView{VStack(alignment:.leading,spacing:6){ForEach(assumptions,id:\.self){Text("• "+$0).font(.caption)}}}.frame(maxHeight:100)}.font(.caption).foregroundStyle(Palette.muted)
            PrimaryButton(title:onboarding ? "生成行程":"按这些信息调整行程",symbol:"sparkles"){
                composing=false;fields=liveFields;assumptionsConfirmed=true;Task{await preparePlan()}
            }.disabled(chat.busy || store.busy || !message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty).accessibilityIdentifier("chat-generate")
            Text(message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty ? "点击即按以上需求与临时假设生成；也可以继续聊。":"输入框还有未发送的内容，请先发送或清空。后续会按最新需求生成。").font(.caption2).foregroundStyle(Palette.muted)
        }.padding(14).background(Palette.mint.opacity(0.6),in:RoundedRectangle(cornerRadius:18)).padding(.horizontal,20).padding(.top,8)
    }
    private func bubble(_ text:String,user:Bool,mood:PetMood = .curious)->some View {
        HStack(alignment:.top){if user{Spacer(minLength:30)}else{PetAvatar(mood:mood).scaleEffect(0.38).frame(width:42,height:48)};Text(text).font(.subheadline).lineSpacing(4).padding(16).background(user ? Palette.mint : .white,in:RoundedRectangle(cornerRadius:20));if !user{Spacer(minLength:25)}}
    }
    private var requirementsPanel:some View {
        VStack(spacing:0) {
            HStack {
                Button {Task{await saveReview(leave:false)}} label:{Label("返回对话",systemImage:"chevron.left")}.accessibilityIdentifier("requirements-back")
                Spacer();Text("旅行需求摘要").font(.headline)
            }.padding(20).disabled(chat.busy)
            ScrollView {
                VStack(alignment:.leading,spacing:16) {
                    VStack(alignment:.leading,spacing:12) {
                        Text(TripDraftSummary(fields:fields).title).font(.title3.bold())
                        Text(TripDraftSummary(fields:fields).subtitle).font(.subheadline).foregroundStyle(Palette.muted)
                        ForEach((petFields+travelExtraFields).filter{TravelRequirements.known(fields[$0.0]) && !["destination","days","date","companions"].contains($0.0)},id:\.0){key,label in
                            HStack(alignment:.top){Text(label).foregroundStyle(Palette.muted);Spacer();Text(key=="budgetCoverage" ? (fields[key]=="activities" ? "活动费用":fields[key]=="totalTravel" ? "包含住宿或交通的总费用":"待确认"):fields[key] ?? "").multilineTextAlignment(.trailing)}.font(.subheadline)
                        }
                        if let basis=TravelRequirements.budgetBases.first(where:{$0.0==fields["budgetBasis"]}){LabeledContent("预算范围",value:basis.1).font(.subheadline)}
                    }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:22))
                    if let issue=validationIssue {
                        Label(issue,systemImage:"info.circle").font(.subheadline).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)
                    }
                    DisclosureGroup("修改或补充需求",isExpanded:$editingRequirements) {
                        VStack(spacing:16){
                            ForEach(petFields+travelExtraFields,id:\.0){key,label in
                                if key=="budgetCoverage" {
                                    Picker(label,selection:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0})){Text("待确认").tag("");Text("活动费用").tag("activities");Text("包含住宿或交通的总费用").tag("totalTravel")}
                                } else {
                                    VStack(alignment:.leading,spacing:5){Text(label).font(.caption).foregroundStyle(Palette.muted);TextField("暂未确定",text:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0}),axis:.vertical).textFieldStyle(.roundedBorder).accessibilityLabel(label)}
                                }
                            }
                            Picker("预算范围",selection:Binding(get:{fields["budgetBasis"] ?? ""},set:{fields["budgetBasis"]=$0})){
                                Text("请选择").tag("");ForEach(TravelRequirements.budgetBases,id:\.0){Text($0.1).tag($0.0)}
                            }
                        }.padding(.top,14)
                    }.padding(18).background(.white,in:RoundedRectangle(cornerRadius:22))
                    DisclosureGroup("临时假设与待核实信息") {
                        VStack(alignment:.leading,spacing:10){
                            ForEach(TravelRequirements.assumptions(fields,personal:store.pendingPersonalProfile),id:\.self){Text("• "+$0).font(.caption)}
                            Text("当前支持悉尼 1–15 天、AUD 活动预算。示例地点、交通、排队及特殊要求仍需核实。").font(.caption)
                        }.padding(.top,12)
                    }.padding(18).background(.white,in:RoundedRectangle(cornerRadius:22))
                    Toggle("我已查看并确认需求及临时假设",isOn:$assumptionsConfirmed).font(.subheadline).tint(Palette.green)
                    if let error=chat.error{Text(error).font(.caption).foregroundStyle(.red)}
                }.padding(.horizontal,20).padding(.bottom,20)
            }.scrollDismissesKeyboard(.interactively)
            VStack(spacing:12){
                if !conversationOnly || onboarding {
                    PrimaryButton(title:onboarding ? "生成行程":"用这些信息调整行程",symbol:"sparkles"){Task{await preparePlan()}}.disabled(chat.busy || store.busy || validationIssue != nil || !assumptionsConfirmed)
                }
                Button("保存需求，返回我的行程"){Task{await saveReview(leave:true)}}.font(.subheadline).disabled(chat.busy)
            }.padding(20)
        }.background(Palette.cream).foregroundStyle(Palette.ink)
        .onChange(of:fields){_,_ in assumptionsConfirmed=false}
    }
    private func saveReview(leave:Bool) async {
        if fields != chat.session?.fields{await chat.update(fields);guard chat.error==nil else{return}}
        if onboarding || draftScope{store.pendingTripFields=fields}
        review=false
        if leave{if onboarding{store.returnToTrips(draft:fields)}else{dismiss()}}
    }
    private var validationIssue:String? {
        do {
            if onboarding || draftScope {
                let missing=TravelRequirements.missingFields(fields)
                if !missing.isEmpty{return "还需要确认："+missing.joined(separator:"、")+"。可以返回对话继续聊，或逐项补充。"}
                _=try TravelRequirements.profile(fields,personal:store.pendingPersonalProfile)
            } else {_=try PetTripDraft.profile(fields)}
            return nil
        }catch{return error.localizedDescription}
    }
    private func preparePlan() async {
        guard assumptionsConfirmed else{return}
        await chat.update(fields);guard chat.error==nil else{return}
        let profile:TripProfile
        do {profile=try (onboarding || draftScope) ? TravelRequirements.profile(fields,personal:store.pendingPersonalProfile):PetTripDraft.profile(fields,base:store.trip?.profile ?? store.profile)}
        catch {chat.error=error.localizedDescription;review=true;return}
        review=false
        if onboarding {
            fields["assumptionsConfirmed"]="true"
            store.completePetPreferences(profile,fields:fields)
            store.startGeneratingPendingTrip()
            return
        }
        dismiss()
        await store.generateFromPet(profile,request:profile.request ?? "")
    }

}

struct PetMemoryView:View {
    @EnvironmentObject private var store:AppStore
    @ObservedObject var chat:PetChatStore
    @Environment(\.dismiss) private var dismiss
    @State private var edit:Memory?
    @State private var text=""
    @State private var deleting:Memory?
    @State private var editingProfile=false
    var body:some View {
        NavigationStack {
            List {
                Section("本次旅行") {ForEach(petFields,id:\.0){key,label in if let value=chat.session?.fields[key]{LabeledContent(label,value:value)}}}
                Section("长期偏好 · 下次也会参考") {
                    if chat.memories.isEmpty{Text("还没有长期偏好。聊一聊，确认后我再记住。")}
                    ForEach(chat.memories){m in VStack(alignment:.leading,spacing:8){Text(m.summary);HStack{Button("修改"){if m.profile.habits != nil{editingProfile=true}else{edit=m;text=m.summary}};Spacer();Button("撤回",role:.destructive){deleting=m}}.font(.caption)}}
                }
                if let error=chat.error{Text(error).foregroundStyle(.red)}
            }.navigationTitle("Buddy 记住的事").toolbar{Button("完成"){dismiss()}}.task{await chat.refreshMemories()}
            .sheet(isPresented:$editingProfile,onDismiss:{Task{await chat.refreshMemories()}}){PersonalPreferencesView(editing:true)}
            .alert("修改长期偏好",isPresented:Binding(get:{edit != nil},set:{if !$0{edit=nil}})){TextField("偏好",text:$text);Button("取消",role:.cancel){edit=nil};Button("保存"){if let m=edit{Task{await chat.editMemory(m,text:text)}};edit=nil}.disabled(text.trimmingCharacters(in:.whitespaces).isEmpty)}
            .confirmationDialog("撤回后，未来对话和规划将不再检索这条偏好。",isPresented:Binding(get:{deleting != nil},set:{if !$0{deleting=nil}})){Button("撤回偏好",role:.destructive){if let m=deleting{Task{await store.deleteMemory(m);await chat.refreshMemories()}};deleting=nil}}
        }
    }
}


struct PetTripDraft {
    static func profile(_ fields:[String:String],base:TripProfile=TripProfile()) throws -> TripProfile {
        func failure(_ message:String)->NSError{NSError(domain:"PetDraft",code:1,userInfo:[NSLocalizedDescriptionKey:message])}
        guard TravelRequirements.supportedDestination(fields["destination"]) else{throw failure("这份资料可保存。当前只能生成悉尼路线，其他城市暂未接入。")}
        guard let days=Int(fields["days"] ?? ""),(1...15).contains(days),let budget=Double(fields["budget"] ?? ""),budget.isFinite,budget>0,budget<=10000 else{throw failure("请填写 1–15 天和有效的每日活动预算（纯数字，最多 10000 AUD）。")}
        let format=DateFormatter();format.locale=Locale(identifier:"en_US_POSIX");format.dateFormat="yyyy-MM-dd";format.isLenient=false
        guard let date=fields["date"],let parsed=format.date(from:date),format.string(from:parsed)==date else{throw failure("请填写 YYYY-MM-DD 格式的有效出发日期。")}
        var result=base;result.date=date;result.days=days;result.budget=budget
        let interests=fields["interests"] ?? "";result.interests=["风景","美食","自然","文化","艺术","城市","购物"].filter{interests.contains($0)}
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
    @State private var finishing=false
    @State private var problem: String?
    private let questions=["嗨！我是 Buddy。\n这次可以陪你去悉尼吗？","我先在小日历上画个圈！你准备哪天出发，玩几天呀？","小背包准备几个位置？\n这次和谁一起去呀？","我想把你喜欢的都装进背包。你看到什么会开心呀？","我走得不快哦～\n这次想慢慢逛，还是多探索一点？","这次的旅行小背包准备好啦！个人喜好已带上，我们去“我的行程”看看？"]
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
                Button { step=step==5 ? 2:max(0,step-1) } label:{Image(systemName:"chevron.left").font(.headline).frame(width:36,height:36)}.disabled(step==0 || working).accessibilityLabel("上一题")
                ProgressView(value:Double(step==5 ? 3:min(step,2)),total:3).tint(Palette.green)
                Text(step==5 ? "准备出发" : "\(min(step+1,3)) / 3").font(.caption.bold()).foregroundStyle(Palette.muted)
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
        .onChange(of:confirmed){_,keys in if let id=chat.session?.id{UserDefaults.standard.set(Array(keys),forKey:"petWelcome.confirmed."+id)}}
        .onChange(of:step){_,value in reaction=nil;lastAnswer="";problem=nil;if loaded{UserDefaults.standard.set(value,forKey:"petWelcome.step")}}
        .onChange(of:values){_,value in if loaded,let id=chat.session?.id,let data=try? JSONEncoder().encode(value){UserDefaults.standard.set(data,forKey:"petWelcome.values."+id)}}
        .sheet(isPresented:$talking,onDismiss:{Task{await syncChat()}}){NavigationStack{PetChatView(conversationOnly:true).toolbar{ToolbarItem(placement:.topBarTrailing){Button("回到小背包"){talking=false}}}}}
        .confirmationDialog("重新开始这次提问？历史行程和长期偏好保留。",isPresented:$restarting,titleVisibility:.visible){Button("重新开始",role:.destructive){Task{await chat.clear();if chat.error==nil{values=defaults;confirmed=[];step=0;loaded=true;UserDefaults.standard.set(chat.session?.id,forKey:"petWelcome.session");UserDefaults.standard.set(0,forKey:"petWelcome.step")}}}}
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
                Label("这次带上的喜好",systemImage:"sparkles").font(.headline)
                summary("兴趣",values["interests"] ?? "")
                summary("节奏",values["pace"] ?? "")
                Divider()
                Button("调整本次兴趣与步数"){step=3}.font(.subheadline.bold()).disabled(working)
                Text("这些偏好从个人档案带入。本次调整只用于这次旅行；长期偏好请到“我的”修改。")
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
                await finish(remember:false)
            } catch {problem=error.localizedDescription}
        } else {
            let keys=confirmed.union(PetOnboardingDraft.groups[step])
            await chat.update(PetOnboardingDraft.confirmedPayload(values,confirmed:keys));guard chat.error==nil else{return}
            confirmed=keys;step = step==2 ? 5:step+1;if step==5{confirmed.formUnion(["interests","pace"])};UserDefaults.standard.set(step,forKey:"petWelcome.step")
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


struct TravelRequirements {
    static func supportedDestination(_ value:String?)->Bool{["悉尼","雪梨","sydney"].contains((value ?? "").trimmingCharacters(in:.whitespacesAndNewlines).lowercased())}
    static let requiredKeys=["destination","date","days","budget","budgetCurrency","budgetBasis","travelers"]
    static let unknowns=["","未确定","暂不确定","未知","稍后补充","还没确定","还没想好","未提供","unknown","不确定","不知道"]
    static func known(_ value:String?)->Bool{!unknowns.contains((value ?? "").trimmingCharacters(in:.whitespacesAndNewlines))}
    static func missingFields(_ fields:[String:String])->[String] {
        [("destination","目的地"),("date","出发日期"),("days","旅行天数"),("budget","预算金额"),("budgetCurrency","预算币种"),("budgetBasis","预算范围"),("travelers","同行总人数")].filter{!known(fields[$0.0])}.map{$0.1}
    }
    static func readyProfile(_ fields:[String:String],personal:UserTravelProfile) throws -> TripProfile {
        let missing=missingFields(fields)
        guard missing.isEmpty else{throw NSError(domain:"TravelRequirements",code:1,userInfo:[NSLocalizedDescriptionKey:"还需要确认："+missing.joined(separator:"、")])}
        return try profile(fields,personal:personal)
    }
    static func optionTitle(_ value:String,field:String)->String {
        if field=="budgetBasis"{return budgetBases.first(where:{$0.0==value})?.1 ?? value}
        if field=="days",Int(value) != nil{return value+" 天"}
        if field=="travelers",Int(value) != nil{return value+" 人"}
        return value
    }
    static let budgetBases=[("perPersonPerDay","每人每天"),("groupPerDay","全体每天"),("perPersonTrip","每人全程"),("groupTrip","全体全程")]
    static func assumptions(_ fields:[String:String],personal:UserTravelProfile)->[String] {
        var result:[String]=[]
        if !known(fields["origin"]){result.append("出发地点暂未确定：暂用悉尼环形码头作为每日起终点。")}
        else if !["环形码头","環形碼頭","中央车站","中央車站","悉尼机场","悉尼機場"].contains(fields["origin"]!){result.append("住宿／出发地点尚未定位：暂用环形码头，生成后请修改起终点。")}
        if !known(fields["pace"]),personal.dailySteps==nil{result.append("步数尚未确定：暂按每天 10,000 步上限规划。")}
        if !known(fields["interests"]),personal.interests==nil{result.append("兴趣尚未确定：暂参考风景与美食。")}
        if (fields["interests"] ?? personal.interests?.joined(separator:"、") ?? "").contains("购物"){result.append("购物兴趣已记录，当前示例地点未保证包含购物场所。")}
        result.append("预算按活动费用估算，不含住宿、交通票价和全部餐饮。")
        if known(fields["mustDo"]),fields["mustDo"] != "无"{result.append("特别想去／想做的内容作为需求备注，尚未保证已排入路线。")}
        if known(fields["avoid"]),fields["avoid"] != "无"{result.append("避开要求需要逐项核实；当前自动规划不能保证满足，请在行程详情中检查。")}
        if known(fields["queueTolerance"]) || personal.queueTolerance != nil{result.append("排队时间尚无可靠数据，不能保证免排队。")}
        return result
    }
    static func profile(_ fields:[String:String],personal:UserTravelProfile) throws -> TripProfile {
        func fail(_ message:String)->NSError{NSError(domain:"TravelRequirements",code:1,userInfo:[NSLocalizedDescriptionKey:message])}
        var normalized=fields
        if fields["budgetCoverage"]=="totalTravel"{throw fail("这笔预算包含住宿或交通等费用。请先单独确认活动预算，再将预算包含范围改为 activities。")}
        if !known(fields["interests"]){normalized["interests"]=personal.interests?.joined(separator:"、") ?? "风景、美食"}
        if !known(fields["pace"]){normalized["pace"]="约\(personal.dailySteps ?? 10000)步"}
        else if fields["pace"]?.range(of:"[0-9]",options:.regularExpression)==nil,["轻松","慢","少走"].contains(where:{fields["pace"]!.contains($0)}){normalized["pace"]="约\(min(personal.dailySteps ?? 6000,6000))步"}
        if let currency=fields["budgetCurrency"],!currency.isEmpty,currency.uppercased() != "AUD"{throw fail("需求可以保留。当前路线费用以 AUD 计算，请先提供确认过的澳元活动预算；不会自动猜测汇率。")}
        if fields["budgetCurrency"] != nil || fields["budgetBasis"] != nil {
            guard fields["budgetCurrency"]?.uppercased()=="AUD",let basis=fields["budgetBasis"],budgetBases.contains(where:{$0.0==basis}) else{throw fail("请确认预算币种，以及是每人还是全体、每天还是全程。")}
            guard let people=Int(fields["travelers"] ?? ""),(1...30).contains(people) else{throw fail("请确认同行总人数（1–30 人，包含自己）。")}
            guard let days=Int(fields["days"] ?? ""),(1...15).contains(days),let amount=Double(fields["budget"] ?? ""),amount.isFinite,amount>0,amount<=1000000 else{throw fail("请确认有效的预算金额和 1–15 天的旅行时长。")}
            let perDay=amount / (basis.hasPrefix("group") ? Double(people):1) / (basis.hasSuffix("Trip") ? Double(days):1)
            normalized["budget"]=String(perDay)
        }
        var result=try PetTripDraft.profile(normalized)
        result.budgetAmount=Double(fields["budget"] ?? "")
        result.budgetCurrency=fields["budgetCurrency"];result.budgetBasis=fields["budgetBasis"]
        result.travelers=Int(fields["travelers"] ?? "");result.origin=fields["origin"]
        result.mustDo=fields["mustDo"];result.avoid=fields["avoid"]
        result.energy=known(fields["energy"]) ? fields["energy"]:personal.energy
        result.rhythm=known(fields["rhythm"]) ? fields["rhythm"]:personal.rhythm
        result.queueTolerance=known(fields["queueTolerance"]) ? fields["queueTolerance"]:personal.queueTolerance
        switch fields["origin"] {
        case "中央车站","中央車站":result.start = .central;result.end = .central
        case "悉尼机场","悉尼機場":result.start = .airport;result.end = .airport
        default:break
        }
        result.request=(petFields+travelExtraFields).map{"\($0.1)：\(fields[$0.0] ?? "未确定")"}.joined(separator:"；")+"；采用的假设："+assumptions(fields,personal:personal).joined(separator:"；")
        guard (result.request?.count ?? 0)<=1500 else{throw fail("需求备注过长，请精简到 1,500 字以内后再生成；已填写内容会保留。")}
        return result
    }
}

struct PersonalPreferencesView:View {
    var editing=false
    @EnvironmentObject private var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var chat=PetChatStore()
    @State private var draft=UserTravelProfile()
    @State private var step=0
    @State private var loaded=false
    @State private var existing:Memory?
    @State private var response:String?
    private let interests=["美食","自然","风景","文化","艺术","购物","城市"]
    private let questions=["如果喜欢的地方有队伍，你通常愿意等多久呀？","旅行时，每天走多少路会让你觉得舒服？不用精确哦。","平时旅行，你的小电池通常有多少电？","你喜欢慢慢发现，还是把一天安排得充实一些？","看到哪些事物，会让你忍不住想去看看？可以多选哦。"]
    var body:some View {
        NavigationStack {
            VStack(spacing:0) {
                HStack{if step>0{Button{step-=1;response=nil}label:{Image(systemName:"chevron.left")}};Text(editing ? "修改旅行画像":"认识一下你").font(.headline);Spacer();Text(step==5 ? "画像总结":"\(step+1) / 5").font(.caption).foregroundStyle(Palette.muted)}.padding(20)
                ProgressView(value:Double(step),total:5).tint(Palette.green).padding(.horizontal,22)
                HStack(spacing:8){PetAvatar(mood:chat.busy ? .remembering:step==5 ? .ready:response==nil ? .curious:.relaxed).frame(width:110,height:126);PetSpeechBubble(text:response ?? (step==5 ? "这就是你的旅行小画像！没确定的事情可以以后再慢慢了解。":questions[step]))}.padding(18)
                ScrollView {
                    VStack(spacing:12){
                        switch step {
                        case 0:singleOptions(["尽量不排队","可以等一会儿","为喜欢的地方愿意排队"],value:draft.queueTolerance){draft.queueTolerance=$0}
                        case 1:
                            ForEach([(5000,"轻松走走 · 约 3,000–6,000 步"),(10000,"走走停停 · 约 7,000–12,000 步"),(15000,"多多探索 · 约 13,000–18,000 步")],id:\.0){n,title in choice(title,selected:draft.dailySteps==n){draft.dailySteps=n;response="收到，我会参考这个步数上限照顾你的节奏。"}}
                            VStack(spacing:12){HStack{Text("自定义步数");Spacer();Text(draft.dailySteps.map{"\($0) 步"} ?? "暂未设置").bold()};Slider(value:Binding(get:{Double(draft.dailySteps ?? 10000)},set:{draft.dailySteps=Int($0)}),in:1000...30000,step:1000).tint(Palette.green).accessibilityLabel("个人步数偏好")}.padding(18).background(.white,in:RoundedRectangle(cornerRadius:20))
                        case 2:singleOptions(["低精力","中等精力","高精力"],value:draft.energy){draft.energy=$0}
                        case 3:singleOptions(["轻松随意","适中","紧凑充实"],value:draft.rhythm){draft.rhythm=$0}
                        case 4:
                            LazyVGrid(columns:[GridItem(.flexible()),GridItem(.flexible())],spacing:12){ForEach(interests,id:\.self){item in choice(item,selected:draft.interests?.contains(item)==true){var values=draft.interests ?? [];if values.contains(item){values.removeAll{$0==item}}else{values.append(item)};draft.interests=values.isEmpty ? nil:values;response="小背包收到你的喜好啦！"}}}
                        default:
                            VStack(alignment:.leading,spacing:16){summary("排队",draft.queueTolerance);summary("步数",draft.dailySteps.map{"每天约 \($0) 步"});summary("精力",draft.energy);summary("节奏",draft.rhythm);summary("兴趣",draft.interests?.joined(separator:"、"))}.padding(20).background(.white,in:RoundedRectangle(cornerRadius:22))
                            Text("这是长期旅行习惯。每次旅行仍可调整，不会自动改写画像。可在“我的 → 个人画像”修改。").font(.caption).foregroundStyle(Palette.muted)
                        }
                        if let error=chat.error{Text(error).font(.caption).foregroundStyle(.red);if !loaded{Button("重新载入"){Task{await load()}}}}
                    }.padding(22)
                }.disabled(!loaded || chat.busy)
                VStack(spacing:12){
                    PrimaryButton(title:step==5 ? (editing ? "保存画像":"保存画像，开始规划旅行"):(answered ? "下一题":"暂不确定，下一题"),symbol:step==5 ? "sparkles":"arrow.right"){
                        if step<5{step+=1;response=nil}else{Task{await save()}}
                    }.disabled(!loaded || chat.busy)
                    if step<5 && answered{Button("暂不确定"){clearCurrent();response="没关系，这题先留白。";step+=1}.font(.caption).foregroundStyle(Palette.muted).disabled(chat.busy || !loaded)}
                    if editing{Button("取消"){dismiss()}.font(.caption).disabled(chat.busy)}
                }.padding(22)
            }.background(Palette.cream).foregroundStyle(Palette.ink).task{await load()}
        }.interactiveDismissDisabled(chat.busy)
    }
    private var answered:Bool{switch step{case 0:return draft.queueTolerance != nil;case 1:return draft.dailySteps != nil;case 2:return draft.energy != nil;case 3:return draft.rhythm != nil;case 4:return draft.interests != nil;default:return true}}
    private func clearCurrent(){switch step{case 0:draft.queueTolerance=nil;case 1:draft.dailySteps=nil;case 2:draft.energy=nil;case 3:draft.rhythm=nil;case 4:draft.interests=nil;default:break}}
    private func summary(_ title:String,_ value:String?)->some View{HStack(alignment:.top){Text(title).foregroundStyle(Palette.muted);Spacer();Text(value ?? "暂不确定").multilineTextAlignment(.trailing)}}
    private func choice(_ title:String,selected:Bool,action:@escaping()->Void)->some View{Button(action:action){HStack{Text(title).font(.subheadline.bold());Spacer();Image(systemName:selected ? "checkmark.circle.fill":"circle")}.padding(18).background(selected ? Palette.mint:.white,in:RoundedRectangle(cornerRadius:18)).foregroundStyle(Palette.ink)}.buttonStyle(.plain)}
    private func singleOptions(_ options:[String],value:String?,set:@escaping(String)->Void)->some View{ForEach(options,id:\.self){option in choice(option,selected:value==option){set(option);response="收到！适合自己的旅行方式，就是最好的。"}}}
    private func load() async {
        draft=store.personalProfile
        await chat.refreshMemories()
        // The questionnaire remains usable offline; save reports connection errors without losing answers.
        existing=chat.memories.filter{$0.profile.habits != nil}.sorted{($0.provenance?.createdAt ?? "") > ($1.provenance?.createdAt ?? "")}.first
        if UserDefaults.standard.data(forKey:"userTravelProfile")==nil,let habits=existing?.profile.habits{draft=habits}
        loaded=true
    }
    private func save() async {
        await chat.saveTravelProfile(draft,existing:existing);guard chat.error==nil else{return}
        store.personalProfile=draft;store.memories=chat.memories
        if let memory=chat.memories.filter({$0.profile.habits != nil}).sorted(by:{($0.provenance?.createdAt ?? "") > ($1.provenance?.createdAt ?? "")}).first{UserDefaults.standard.set(memory.id,forKey:"personalProfile.memoryID")}
        if editing{dismiss()}else{store.completePersonalPreferences()}
    }
}
