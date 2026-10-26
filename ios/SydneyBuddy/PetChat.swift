import SwiftUI

struct PetMessage: Codable, Identifiable { var id: String; var role: String; var text: String; var mood:String? = nil;var quickReplies:[PetQuickReply]? = nil;var choice:PetChoiceEvidence? = nil }
extension PetMessage {
    func displayText(language:AppLanguage)->String {
        role == "user" && choice == nil ? text:language.display(text)
    }
}
struct PetSession: Codable {
    var id: String; var messages: [PetMessage]; var fields: [String:String]; var pendingMemory: String
    var nextField: String; var question: String; var options: [String]; var completed: Int; var total: Int
    var dnaCompleted:Bool? = nil;var planningFields:[String:String]? = nil;var travelDna:[TravelPreference]? = nil;var pendingPreferences:[TravelPreference]? = nil;var tripPreferences:[TravelPreference]? = nil;var memoryWarning:String? = nil
    var dialogue:ProfilingDialogueState? = nil
    var effectiveFields:[String:String] {planningFields ?? fields}
    var replyQuestion:PetMessage? {messages.last(where:{$0.role=="assistant"})}
    var contextualReplies:[PetQuickReply] {dialogue == nil ? []:replyQuestion?.quickReplies ?? []}
}
struct PetEvent: Decodable { var type: String; var text: String?; var error: String?; var session: PetSession? }
let petFields: [(String,String)] = [("destination","目的地"),("date","出发日期 YYYY-MM-DD"),("days","旅行天数"),("companions","同行伙伴"),("budget","预算金额"),("interests","这次想玩什么"),("pace","旅行节奏"),("notes","补充要求")]
let travelExtraFields:[(String,String)]=[("budgetCoverage","预算包含范围"),("budgetCurrency","预算币种"),("travelers","同行总人数"),("origin","出发地点或住宿"),("mustDo","特别想去或想做的"),("avoid","必须避开的安排"),("energy","本次精力"),("rhythm","本次节奏"),("queueTolerance","本次排队偏好"),("flights","航班"),("bookings","住宿与交通预订"),("tickets","固定门票"),("companionNeeds","同行者需要"),("specialLimits","特殊限制")]


@MainActor final class PetChatStore: ObservableObject {
    @Published var session: PetSession?
    @Published var busy = false
    @Published var streaming = ""
    @Published var outgoing = ""
    @Published var outgoingIsChoice = false
    @Published var error: String?
    @Published var memories: [Memory] = []
    @Published var choices = false
    @Published var koalaCue:KoalaCue?
    private var scope = "draft"
    private var profiling = false
    var interactionContext=""
    var personalizingTrip=false
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await BackendConnection.data(for:$0)}
    private var base:URL {BackendConnection.base}
    private var pendingRequest: (String,String)?
    private var storageKey: String { "petSession." + scope }
    private func request<T:Decodable>(_ path:String, method:String="GET", body:[String:Any]?=nil) async throws -> T {
        var req=URLRequest(url:base.appendingPathComponent(path));req.httpMethod=method;TravelDataSource.current.apply(to:&req);req.timeoutInterval=120
        req.setValue("application/json",forHTTPHeaderField:"Content-Type");req.setValue(AppLanguage.current.rawValue,forHTTPHeaderField:"X-App-Language")
        if var body {if path.hasPrefix("pet/sessions"){body["language"]=AppLanguage.current.rawValue};req.httpBody=try JSONSerialization.data(withJSONObject:body)}
        let (data,response)=try await loader(req)
        guard let http=response as? HTTPURLResponse,(200..<300).contains(http.statusCode) else {
            let obj=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any]
            throw NSError(domain:"Pet",code:1,userInfo:[NSLocalizedDescriptionKey:obj?["error"] as? String ?? "暂时连不上 Koko，请检查电脑上的服务。"])
        }
        return try JSONDecoder().decode(T.self,from:data)
    }
    private func run(_ action:() async throws -> Void) async {
        guard !busy else{return};busy=true;error=nil
        defer{busy=false}
        do{try await action()}catch{self.error=error.localizedDescription}
    }
    private var dialogueInput:[String:Any] {
        ["profilingVersion":2,"companionVersion":scope=="personalProfile" ? 0:1,"nickname":UserDefaults.standard.string(forKey:"koko.nickname") ?? "","hasMetKoko":UserDefaults.standard.bool(forKey:"koko.hasMet")]
    }
    private func rememberIntroduction() {
        guard let dialogue=session?.dialogue else{return}
        UserDefaults.standard.set(true,forKey:"koko.hasMet")
        if !dialogue.nickname.isEmpty{UserDefaults.standard.set(dialogue.nickname,forKey:"koko.nickname")}
    }
    func load(scope:String, seed:[String:String]=[:],personal:UserTravelProfile?=nil,profiling:Bool=false) async {
        guard !busy else{return}
        if self.scope==scope,session != nil{return}
        self.scope=scope;self.profiling=profiling;session=nil;streaming="";outgoing="";pendingRequest=nil
        choices=UserDefaults.standard.object(forKey:"petQuickAnswers.v3") as? Bool ?? false
        await run {
            if let id=UserDefaults.standard.string(forKey:storageKey) {
                session=try await request("pet/sessions/"+id)
                if (profiling || session?.dialogue != nil),session?.dialogue?.companionVersion != (scope=="personalProfile" ? 0:1){session=try await request("pet/sessions/"+id,method:"PATCH",body:dialogueInput)}
            } else {
                var body:[String:Any]=["fields":seed,"personalProfile":personal.flatMap{try? JSONSerialization.jsonObject(with:JSONEncoder().encode($0))} ?? [:]]
                if profiling{body.merge(dialogueInput){_,new in new}}
                let value:PetSession=try await request("pet/sessions",method:"POST",body:body);session=value;UserDefaults.standard.set(value.id,forKey:storageKey)
            }
            rememberIntroduction()
        }
    }
    func reloadSession() async { session=nil;await load(scope:scope,profiling:profiling) }
    func mode(_ value:Bool){choices=value;UserDefaults.standard.set(value,forKey:"petQuickAnswers.v3")}
    func send(_ text:String, field:String?=nil, value:String?=nil,reply:PetQuickReply?=nil) async {
        guard let id=session?.id,!text.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty else{return}
        if let reply,session?.contextualReplies.contains(where:{$0.id==reply.id}) != true{return}
        await run {
            let scene=session?.replyQuestion?.text ?? ""
            let questionId=reply == nil ? nil:session?.replyQuestion?.id
            let fingerprint=interactionContext+"|"+text+"|"+(field ?? "")+"|"+(value ?? "")+"|"+(reply?.id ?? "")+"|"+(questionId ?? "")
            let requestID=pendingRequest?.0 == fingerprint ? pendingRequest!.1 : UUID().uuidString
            pendingRequest=(fingerprint,requestID);outgoing=text;outgoingIsChoice=reply != nil || field != nil;streaming=""
            var body:[String:Any] = ["message":text,"requestId":requestID,"language":AppLanguage.current.rawValue]
            if personalizingTrip{body["conversationPhase"]="personalize"}
            if !interactionContext.isEmpty{body["interactionContext"]=interactionContext}
            if let field {body["field"]=field;body["value"]=value ?? text}
            if let reply,let questionId{body["optionId"]=reply.id;body["questionId"]=questionId}
            var req=URLRequest(url:base.appendingPathComponent("pet/sessions/\(id)/turn"));req.httpMethod="POST";TravelDataSource.current.apply(to:&req);req.timeoutInterval=120
            req.setValue("application/json",forHTTPHeaderField:"Content-Type");req.httpBody=try JSONSerialization.data(withJSONObject:body)
            let(bytes,response)=try await BackendConnection.bytes(for:req)
            guard let http=response as? HTTPURLResponse,(200..<300).contains(http.statusCode) else{throw NSError(domain:"Pet",code:2,userInfo:[NSLocalizedDescriptionKey:"这次没有发送成功，请重试。"])}
            var completed=false
            for try await line in bytes.lines {
                guard !line.isEmpty else{continue}
                let event=try JSONDecoder().decode(PetEvent.self,from:Data(line.utf8))
                if event.type=="reply" {streaming=event.text ?? ""}
                if event.type=="error" {throw NSError(domain:"Pet",code:3,userInfo:[NSLocalizedDescriptionKey:event.error ?? "请重试"])}
                if let final=event.session {
                    let finished=session?.dialogue?.inspiration == nil && final.dialogue?.inspiration != nil
                    session=final;completed=true;streaming="";outgoing="";pendingRequest=nil;rememberIntroduction()
                    if finished{koalaCue=KoalaCue(event:.dnaCompleted,id:"initial-profile:"+id)}
                    else {
                        let count=final.messages.filter{$0.role=="user"}.count
                        koalaCue=KoalaCue(event:KoalaConversationFeedback.event(text:text,scene:scene) ?? (count>0 && count.isMultiple(of:3) ? .informationSubmitted:.choiceAccepted))
                    }
                }
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
        await run{session=try await request("pet/sessions/\(id)/memory",method:"POST",body:["consent":consent]);if consent{koalaCue=KoalaCue(event:.preferenceConfirmed)}}
    }
    func decidePreference(_ preference:TravelPreference,consent:Bool,dismiss:Bool=false) async {
        guard let id=session?.id else{return}
        await run {
            session=try await request("pet/sessions/\(id)/memory",method:"POST",body:["candidateId":preference.id,"consent":consent,"dismiss":dismiss,"expectedConflicts":preference.conflictingMemoryIds])
            memories=try await request("memories")
            if consent{koalaCue=KoalaCue(event:.preferenceConfirmed,id:"confirmed:"+preference.id)}
        }
    }
    func completeDna() async {
        guard let id=session?.id else{return}
        await run {
            session=try await request("pet/sessions/\(id)/profile/complete",method:"POST",body:[:])
            koalaCue=KoalaCue(event:.dnaCompleted,id:"profile:"+id)
        }
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
    func refreshMemories() async {await run{memories=try await request("memories");if let id=session?.id{session=try await request("pet/sessions/"+id)}}}
    func deleteMemory(_ id:String) async {
        await run{let _: [String:Bool]=try await request("memories/"+id,method:"DELETE");memories=try await request("memories")}
    }
    func editMemory(_ memory:Memory, text:String) async {
        await run {
            var profile:[String:Any]=[:];var summary=text
            if var dna=memory.profile.travelDna {
                dna.value=text;dna.source="user_correction";dna.evidence=text;dna.sourceMessageId=""
                profile["travelDna"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(dna));summary=dna.summary
            }
            let _:Memory=try await request("memories/"+memory.id,method:"PUT",body:["summary":summary,"consent":true,"profile":profile])
            memories=try await request("memories")
            if let id=session?.id{session=try await request("pet/sessions/"+id)}
        }
    }
    func clear() async {
        await run {
            if let id=session?.id ?? UserDefaults.standard.string(forKey:storageKey){let _:[String:Bool]=try await request("pet/sessions/"+id,method:"DELETE")}
            UserDefaults.standard.removeObject(forKey:storageKey)
            let value:PetSession=try await request("pet/sessions",method:"POST",body:profiling ? dialogueInput:[:]);session=value;UserDefaults.standard.set(value.id,forKey:storageKey);rememberIntroduction()
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
struct PetSpeechBubble:View {
    @Environment(\.locale) private var interfaceLocale

    let text:String
    var companionStyle=false
    var animateText=true
    var avatarBelow=false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var visible=""
    var body:some View {
        let _=interfaceLocale
        VStack(alignment:.leading,spacing:5){if !companionStyle{Text(uiText("KOKO")).font(.caption2.bold()).tracking(2).foregroundStyle(Palette.green)};ZStack(alignment:.topLeading){Text(uiText(text)).hidden();Text(verbatim:visible.isEmpty ? " ":visible)}.font(.system(.body,design:.rounded,weight:.semibold)).lineSpacing(5).fixedSize(horizontal:false,vertical:true).frame(maxWidth:.infinity,alignment:.leading)}
            .padding(20).background(.white,in:RoundedRectangle(cornerRadius:23))
            .overlay(alignment:companionStyle ? (avatarBelow ? .bottom:.top):.leading){RoundedRectangle(cornerRadius:3).fill(.white).frame(width:16,height:16).rotationEffect(.degrees(45)).offset(x:companionStyle ? 0:-7,y:companionStyle ? (avatarBelow ? 7:-7):18)}
            .accessibilityElement(children:.ignore).accessibilityLabel(uiText(text))
            .task(id:uiText(text)+String(reduceMotion)+String(animateText)){visible="";if reduceMotion || !animateText{visible=uiText(text);return};do{for character in uiText(text){try Task.checkCancellation();visible.append(character);try await Task.sleep(nanoseconds:22_000_000)}}catch{}}
    }
}

private struct TripDetailRoute:Identifiable {let id:Int}

enum PetComposerDraft {
    static func restored(_ pending:String,contextual:Bool)->String {
        guard contextual else{return pending}
        // Remove only the exact legacy app-generated context, never arbitrary user prose.
        let pattern = #"(?m)^关于「[^\n]+」第 \d+ 天（\d{4}-\d{2}-\d{2}）。[^\n]*仅调整这一天，保留其余日期和已完成活动；先展示建议，确认后再保存。(?:\n|$)"#
        return pending.replacingOccurrences(of:pattern,with:"",options:.regularExpression).trimmingCharacters(in:.newlines)
    }
}

struct PetChatView:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject var store:AppStore
    @StateObject private var chat=PetChatStore()
    @State private var message=""
    @State private var review=false
    @State private var detailRoute:TripDetailRoute?
    @State private var memories=false
    @State private var clearing=false
    @State private var historyExpanded=false
    @State private var expandedUser=false
    @State private var acceptedInspiration:String?
    @State private var requestedInspiration=false
    @State private var inspirationOverride:String?
    @State private var fields:[String:String]=[:]
    @State private var assumptionsConfirmed=false
    @State private var editingRequirements=false
    @FocusState private var composing:Bool
    @Environment(\.dismiss) private var dismiss
    var onboarding=false
    var personalSetup=false
    var conversationOnly=false
    var draftScope=false
    var initialMessage=""
    var interactionContext=""
    var autoSendInitial=false
    @State private var sentInitial=false
    @State private var parkedMessage=""
    @State private var basicsConfirmed=false
    @State private var studioStartID:String?
    private var scope:String {personalSetup ? "personalProfile":(onboarding || draftScope) ? "draft" : (store.trip?.id ?? "draft")}
    private var faceToFace:Bool {personalSetup || onboarding || draftScope}
    private var displayedMessages:[PetMessage] {
        let messages=chat.session?.messages ?? []
        if !onboarding && !draftScope,store.trip != nil,messages.first?.role=="assistant" {return Array(messages.dropFirst())}
        return messages
    }
    private var currentTurn:[PetMessage] {
        guard let index=displayedMessages.lastIndex(where:{$0.role=="assistant"}) else{return Array(displayedMessages.suffix(1))}
        let start=index>0 && displayedMessages[index-1].role=="user" ? index-1:index
        return Array(displayedMessages[start...])
    }
    private var olderMessages:[PetMessage] {let ids=Set((canPlanDirectly ? currentTurn.filter{$0.role=="assistant"}:currentTurn).map(\.id));return displayedMessages.filter{!ids.contains($0.id)}}
    private var companionMood:PetMood {chat.error != nil ? .confused:chat.busy ? .thinking:!message.isEmpty ? .listening:canPlanDirectly ? .ready:PetMood.reply(displayedMessages.last?.mood)}
    private var companionCue:KoalaCue? {
        if chat.busy{return KoalaCue(action:.map,id:"thinking:"+scope)}
        if !message.isEmpty{return KoalaCue(action:.idle,id:"listening:"+scope)}
        if let cue=chat.koalaCue{return cue}
        if canPlanDirectly{return KoalaCue(action:.idle,id:"ready:"+scope)}
        return chat.koalaCue ?? KoalaCue(action:.idle,id:"chat-idle:"+scope)
    }
    private var currentSpeech:String {
        if !chat.streaming.isEmpty{return chat.streaming}
        if chat.busy{return "让我翻翻小地图，把你的想法一个个收好～"}
        return displayedMessages.last(where:{$0.role=="assistant"})?.text ?? "我在这儿，慢慢说就好。我们一起想想这次怎么玩～"
    }
    private var conversationContent:some View {
        VStack(spacing:0) {
            HStack(spacing:12){
                Button{if personalSetup{store.finishPersonalConversation(nil,useClues:false)}else if onboarding{store.returnToTrips(draft:liveFields)}else{dismiss()}}label:{Image(systemName:"chevron.left").font(.headline).frame(width:36,height:44)}.accessibilityLabel(uiText(onboarding ? "返回我的行程":"返回")).accessibilityIdentifier("chat-back")
                Text(uiText(personalSetup ? "先认识一下":onboarding || draftScope ? "和 Koko 规划":"和 Koko 聊聊")).font(.headline)
                Spacer()
                Menu{
                    Button(uiText("我的记忆"),systemImage:"brain.head.profile"){memories=true}
                    if !personalSetup,chat.session?.dialogue?.inspiration != nil{Button(uiText("Get a travel idea"),systemImage:"sparkles"){acceptedInspiration=nil;inspirationOverride=nil;requestedInspiration=true}}
                    if !parkedMessage.isEmpty{Button(uiText("恢复未发送的内容"),systemImage:"text.bubble"){message=parkedMessage;parkedMessage="";UserDefaults.standard.removeObject(forKey:"petComposer.parked."+scope)}}
                    if chat.session?.dialogue==nil{Button(uiText(chat.choices ? "收起快捷填写":"展开快捷填写")){chat.mode(!chat.choices)}}
                    Button(uiText("清空本次对话"),systemImage:"trash",role:.destructive){clearing=true}
                }label:{Image(systemName:"ellipsis").frame(width:44,height:44)}.accessibilityLabel(uiText("聊天更多操作"))
            }.padding(.horizontal,12)
            if personalSetup {Text(uiText("几个小选择认识你 · 不确定的可以留白")).font(.caption).foregroundStyle(Palette.muted).padding(.vertical,6)}else{backpack}
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment:.leading,spacing:14){
                        if personalSetup {
                        if !olderMessages.isEmpty && !composing {
                            DisclosureGroup(uiText("前面的对话"),isExpanded:$historyExpanded){ForEach(olderMessages){item in bubble(item.displayText(language:store.language),user:item.role=="user",showAvatar:false)}}.font(.caption).foregroundStyle(Palette.muted).accessibilityIdentifier("chat-history")
                        }
                        if !composing,!canPlanDirectly,chat.outgoing.isEmpty,let user=currentTurn.first(where:{$0.role=="user"}){Button{expandedUser.toggle()}label:{bubble(user.displayText(language:store.language),user:true,lineLimit:expandedUser ? nil:2)}.buttonStyle(.plain).accessibilityHint(uiText("点按展开或收起这条消息"))}
                        if !chat.outgoing.isEmpty{bubble(chat.outgoingIsChoice ? uiText(chat.outgoing):chat.outgoing,user:true,lineLimit:2)}
                        VStack(spacing:8){
                            if faceToFace {
                                if composing {
                                    HStack(alignment:.top,spacing:10){PetAvatar(mood:companionMood,cue:companionCue,size:52);Text(uiText(currentSpeech)).font(.caption).fixedSize(horizontal:false,vertical:true).padding(12).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:16))}
                                }else {
                                    PetSpeechBubble(text:currentSpeech,companionStyle:true,animateText:false,avatarBelow:true)
                                    KokoCompanionStage(cue:companionCue,size:personalSetup ? 188:176,mood:companionMood).frame(height:personalSetup ? 188:176)
                                }
                            }else {
                                KokoCompanionScene(mood:companionMood,cue:companionCue,ready:canPlanDirectly && !chat.busy,height:composing ? 100:canPlanDirectly ? 132:168)
                                PetSpeechBubble(text:currentSpeech,companionStyle:true,animateText:false)
                            }
                            if chat.busy{Label(uiText("在认真整理你的想法…"),systemImage:"ellipsis").font(.caption).foregroundStyle(Palette.green)}
                        }.id("companion-turn").accessibilityIdentifier("chat-companion-turn")
                        replyChoices
                        }else{
                            ForEach(displayedMessages){item in
                                if item.role=="user"{bubble(item.displayText(language:store.language),user:true,showAvatar:false)}
                                else{
                                    let text=item.displayText(language:store.language)
                                    let latest=item.id==displayedMessages.last(where:{$0.role=="assistant"})?.id
                                    if latest,canPlanDirectly,isRepeatedReadyAcknowledgement(text){
                                        bubble(uiText("All packed! Shall I put our day together?"),user:false)
                                        DisclosureGroup(uiText("Full reply")){Text(text).font(.caption).foregroundStyle(Palette.muted)}.font(.caption).foregroundStyle(Palette.muted).padding(.leading,68)
                                    }else{bubble(text,user:false,showAvatar:latest)}
                                }
                            }
                            if chat.busy{
                                HStack(alignment:.top,spacing:10){PetAvatar(mood:companionMood,cue:companionCue,size:48);Text(uiText(chat.streaming.isEmpty ? "在认真整理你的想法…":chat.streaming)).font(.subheadline).foregroundStyle(Palette.muted).padding(12)}
                            }
                            Color.clear.frame(height:1).id("companion-turn")
                        }
                        if !personalSetup,(!canPlanDirectly || requestedInspiration),let session=chat.session,presentsInspiration(session),let direction=inspirationOverride ?? KokoInspirationPresentation.direction(session){
                            KokoInspirationCard(direction:direction,busy:chat.busy,accept:{
                                composing=false
                                Task{if inspirationOverride==nil,let reply=session.contextualReplies.first(where:{$0.intent=="accept_direction"}){await chat.send(reply.text,reply:reply)}else{await chat.update(["interests":KokoInspirationPresentation.interests(direction)])};if chat.error==nil{acceptedInspiration=direction;requestedInspiration=false;inspirationOverride=nil}}
                            },another:{
                                composing=false
                                let directions=["nature","culture","food"]
                                inspirationOverride=directions[((directions.firstIndex(of:direction) ?? 0)+1)%directions.count]
                                requestedInspiration=true;acceptedInspiration=nil
                            },close:{acceptedInspiration=direction;requestedInspiration=false;inspirationOverride=nil}).id("inspiration")
                        }
                        ForEach(chat.session?.pendingPreferences ?? []){preference in
                            TravelPreferenceCard(preference:preference,busy:chat.busy,decide:{consent,ignored in Task{await chat.decidePreference(preference,consent:consent,dismiss:ignored)}},correct:{message="关于"+preference.label+"，其实我";composing=true})
                        }
                        if let warning=chat.session?.memoryWarning,!warning.isEmpty{Text(uiText(warning)).font(.caption).foregroundStyle(Palette.muted)}
                        if !personalSetup,let session=chat.session,session.dnaCompleted != true,(session.pendingPreferences ?? []).isEmpty,(session.travelDna ?? []).contains(where:{$0.status=="confirmed"}) {
                            Button(uiText("完成旅行习惯整理")){Task{await chat.completeDna();if chat.error==nil{store.showPersonalPreferences=false;UserDefaults.standard.set(true,forKey:"personalPreferences.v2.completed")}}}.buttonStyle(.borderedProminent).tint(Palette.green).disabled(chat.busy).accessibilityIdentifier("complete-travel-dna")
                        }
                        if let s=chat.session,!s.pendingMemory.isEmpty {
                            VStack(alignment:.leading,spacing:10){Text(uiText("以后也这样安排，好吗？")).font(.headline);Text(uiText(s.pendingMemory));HStack{Button(uiText("以后都记住")){Task{await chat.decideMemory(true)}}.buttonStyle(.borderedProminent);Button(uiText("仅限这次")){Task{await chat.decideMemory(false)}}.buttonStyle(.bordered)}}.padding(16).background(Palette.mint,in:RoundedRectangle(cornerRadius:20)).disabled(chat.busy)
                        }
                        if waitingForDeferredDates && !chat.busy{
                            VStack(alignment:.leading,spacing:10){
                                Text(uiText("Your ideas are saved")).font(.headline)
                                Text(uiText("Dates are still open. Choose them whenever you’re ready to generate your itinerary.")).font(.subheadline).foregroundStyle(Palette.muted)
                                Button{composing=false;detailRoute=TripDetailRoute(id:1)}label:{Text(uiText("Choose dates to generate")).font(.headline).frame(maxWidth:.infinity,minHeight:50)}.buttonStyle(TripsCreateButtonStyle()).accessibilityIdentifier("chat-choose-dates")
                            }.padding(16).background(chatBlue.opacity(0.035),in:RoundedRectangle(cornerRadius:16)).accessibilityIdentifier("chat-deferred-dates")
                        }
                        if canPlanDirectly && !chat.busy{directPlanningCard}
                        if let error=chat.error{Text(uiText(error)).font(.caption).foregroundStyle(.red)}
                        Color.clear.frame(height:1).id("bottom")
                    }.padding(20)
                }.scrollDismissesKeyboard(.interactively)
                .onChange(of:chat.session?.messages.count){_,_ in if !historyExpanded{withAnimation{proxy.scrollTo(personalSetup ? "companion-turn":"bottom",anchor:personalSetup ? .top:.bottom)}}}
                .onChange(of:chat.streaming.isEmpty){_,_ in if !historyExpanded{proxy.scrollTo(personalSetup ? "companion-turn":"bottom",anchor:personalSetup ? .top:.bottom)}}
                .onChange(of:chat.busy){_,value in if value && !historyExpanded{proxy.scrollTo(personalSetup ? "companion-turn":"bottom",anchor:personalSetup ? .top:.bottom)}}
                .onChange(of:composing){_,value in if value{proxy.scrollTo(personalSetup ? "companion-turn":"bottom",anchor:personalSetup ? .top:.bottom)}}
                .onChange(of:requestedInspiration){_,_ in Task{@MainActor in try? await Task.sleep(for:.milliseconds(120));withAnimation{proxy.scrollTo("bottom",anchor:.bottom)}}}
            }
            if personalSetup {
                if chat.session?.dialogue?.inspiration != nil {
                    PrimaryButton(title:"开始准备旅行",symbol:"arrow.right"){store.finishPersonalConversation(chat.session,useClues:true)}.padding(.horizontal,20).disabled(chat.busy || !message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty).accessibilityIdentifier("personal-start-planning")
                }
                if !composing{Button(uiText("先跳过，之后再认识")){store.finishPersonalConversation(nil,useClues:false)}.font(.caption).frame(minHeight:40).disabled(chat.busy).accessibilityIdentifier("personal-skip")}
            }else if !canPlanDirectly{replyChoices.padding(.horizontal,16).padding(.vertical,6)}
            if !personalSetup,!composing,let issue=generationIssue {
                VStack(alignment:.leading,spacing:6) {
                    Label(uiText("暂时还不能生成"),systemImage:"info.circle.fill").font(.subheadline.bold())
                    Text(uiText(issue)).font(.caption).fixedSize(horizontal:false,vertical:true)
                    if liveFields["adjustmentNeed"] != "endTime",liveFields["adjustmentEndMinute"]==nil{Button(uiText("修改相关资料")){composing=false;detailRoute=TripDetailRoute(id:correctionStage)}.font(.subheadline.bold())}
                }.padding(12).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:14)).padding(.horizontal,20).accessibilityIdentifier("chat-generation-issue")
            }

            HStack(alignment:.bottom){
                if chat.busy{Text(uiText("等我一下，马上回来～")).foregroundStyle(Palette.muted).padding(12).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:16))}else{TextField(uiText("也可以直接告诉我…"),text:$message,axis:.vertical).focused($composing).lineLimit(1...4).padding(12).background(Color.gray.opacity(0.07),in:RoundedRectangle(cornerRadius:22)).accessibilityIdentifier("chat-composer").disabled(chat.busy)}
                Button{UIImpactFeedbackGenerator(style:.soft).impactOccurred();let text=message;composing=false;Task{await chat.send(text);if chat.error==nil{message=""}}}label:{Image(systemName:"arrow.up").font(.system(size:21,weight:.semibold)).foregroundStyle(.white).frame(width:46,height:46).background(chatBlue,in:Circle())}.buttonStyle(BuddyPressStyle()).accessibilityLabel(uiText("发送消息")).disabled(chat.busy || message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || message.count>2000)
            }.padding(.horizontal,20).padding(.vertical,8)
        }.background(Color(red:0.985,green:0.985,blue:0.975)).foregroundStyle(Color(red:0.09,green:0.15,blue:0.24)).tint(chatBlue)
    }
    private var usesPlanningStudio:Bool {!personalSetup && (onboarding || draftScope)}
    private var studioBasicsReady:Bool {canPlanDirectly || waitingForDeferredDates}
    private var studioMessages:[PetMessage] {
        guard let marker=studioStartID else{return []}
        guard let index=displayedMessages.firstIndex(where:{$0.id==marker}) else{return Array(displayedMessages.suffix(20))}
        return Array(displayedMessages.dropFirst(index+1))
    }
    private var planningStudio:some View {
        VStack(spacing:0){
            HStack{
                Button{composing=false;store.returnToTrips(draft:liveFields)}label:{Image(systemName:"chevron.left").frame(width:44,height:44)}.accessibilityLabel(uiText("返回我的行程")).accessibilityIdentifier("chat-back")
                Spacer();Text(uiText("和 Koko 规划")).font(.headline);Spacer()
                Menu{
                    Button(uiText("我的记忆"),systemImage:"brain.head.profile"){memories=true}
                    Button(uiText("Edit trip basics"),systemImage:"pencil"){basicsConfirmed=false;composing=false}
                    Button(uiText("清空本次对话"),systemImage:"trash",role:.destructive){clearing=true}
                }label:{Image(systemName:"ellipsis").frame(width:44,height:44)}.accessibilityLabel(uiText("聊天更多操作"))
            }.padding(.horizontal,8)
            if !basicsConfirmed || !studioBasicsReady{studioBasics}else{studioPersonalize}
        }.background(.white).foregroundStyle(Color(red:0.09,green:0.15,blue:0.24)).tint(chatBlue)
    }
    private var studioBasics:some View {
        VStack(spacing:0){
            ScrollView{
                VStack(spacing:12){
                    Text(uiText("Let’s get our bearings.")).font(.system(.title2,design:.rounded,weight:.bold)).multilineTextAlignment(.center).padding(.top,8)
                    studioKoala(large:true)
                    PlanningBasicsRows(fields:liveFields,language:store.language,busy:chat.busy || chat.session==nil){detailRoute=TripDetailRoute(id:$0)}
                    Text(uiText("Activities only. Meals, stays and transport are extra.")).font(.caption2).foregroundStyle(Palette.muted).multilineTextAlignment(.center)
                    if let issue=generationIssue{Text(uiText(issue)).font(.caption).foregroundStyle(Palette.muted)}
                    if let error=chat.error{Text(uiText(error)).font(.caption).foregroundStyle(.red);Button(uiText("Retry")){Task{await chat.reloadSession()}}}
                    if chat.busy{ProgressView(uiText("正在收好…")).font(.caption)}
                }.padding(.horizontal,20).padding(.bottom,16)
            }
            Button{
                guard studioBasicsReady,let id=chat.session?.id else{return}
                basicsConfirmed=true;if studioStartID==nil{studioStartID=chat.session?.messages.last?.id};chat.personalizingTrip=true
                UserDefaults.standard.set(true,forKey:"koko.studio.basics."+id)
                UserDefaults.standard.set(studioStartID,forKey:"koko.studio.start."+id)
            }label:{Label(uiText("Make it my kind of trip"),systemImage:"arrow.right").font(.headline).frame(maxWidth:.infinity,minHeight:52)}.buttonStyle(TripsCreateButtonStyle()).disabled(!studioBasicsReady || chat.busy).accessibilityIdentifier("studio-continue").padding(.horizontal,20).padding(.vertical,12)
        }
    }
    private func studioKoala(large:Bool)->some View {
        let listening=composing || !message.isEmpty
        let size:CGFloat=large ? 240:(listening ? 100:210)
        let cue=chat.busy ? KoalaCue(action:.map,id:"studio-thinking"):listening ? KoalaCue(action:.idle,id:"studio-listening"):KoalaCue(action:.map,id:"studio-map")
        return PetAvatar(mood:listening ? .listening:.thinking,cue:cue,size:size)
            .frame(maxWidth:.infinity).frame(height:large ? 210:(listening ? 88:185)).clipped()
            .accessibilityIdentifier("studio-koko")
    }
    private var studioSummary:String {
        var parts=[uiText(liveFields["destination"] ?? "Destination")]
        if let days=liveFields["days"],TravelRequirements.known(days){parts.append(days=="1" ? uiText("1 day"):store.language.text("{0} days",days))}
        if let date=liveFields["date"],TravelRequirements.known(date){parts.append(date)}
        if let people=liveFields["travelers"],TravelRequirements.known(people){parts.append(people=="1" ? uiText("Just me"):store.language.text("{0} people",people))}
        return parts.joined(separator:" · ")
    }
    private var studioPersonalize:some View {
        VStack(spacing:0){
            Button{composing=false;basicsConfirmed=false}label:{HStack(spacing:8){Image(systemName:"mappin.and.ellipse");Text(studioSummary).font(.caption).lineLimit(2);Spacer();Image(systemName:"pencil")}.padding(12).background(chatBlue.opacity(0.05),in:RoundedRectangle(cornerRadius:14))}.buttonStyle(.plain).padding(.horizontal,20).accessibilityLabel(uiText("Edit trip basics")).accessibilityIdentifier("studio-edit-basics")
            studioKoala(large:false)
            ScrollViewReader{proxy in
                ScrollView{
                    VStack(alignment:.leading,spacing:14){
                        if studioMessages.isEmpty && !chat.busy{
                            Text(uiText("Tell me something about yourself.")).font(.system(.title2,design:.rounded,weight:.bold)).multilineTextAlignment(.center).frame(maxWidth:.infinity)
                            Text(uiText("What makes a good day for you?")).font(.subheadline).foregroundStyle(Palette.muted).frame(maxWidth:.infinity)
                        }
                        ForEach(studioMessages){item in bubble(item.displayText(language:store.language),user:item.role=="user",showAvatar:false)}
                        if !chat.outgoing.isEmpty{bubble(chat.outgoing,user:true,showAvatar:false)}
                        if chat.busy{HStack{ProgressView();Text(uiText("在认真整理你的想法…")).font(.caption).foregroundStyle(Palette.muted)}}
                        if requestedInspiration{
                            let direction=inspirationOverride ?? chat.session.flatMap{KokoInspirationPresentation.direction($0)} ?? "nature"
                            KokoInspirationCard(direction:direction,busy:chat.busy,accept:{
                                Task{await chat.update(["interests":KokoInspirationPresentation.interests(direction)]);if chat.error==nil{acceptedInspiration=direction;requestedInspiration=false;inspirationOverride=nil}}
                            },another:{let all=["nature","culture","food"];inspirationOverride=all[((all.firstIndex(of:direction) ?? 0)+1)%all.count]},close:{requestedInspiration=false;inspirationOverride=nil})
                        }
                        ForEach(chat.session?.pendingPreferences ?? []){preference in
                            TravelPreferenceCard(preference:preference,busy:chat.busy,decide:{consent,ignored in Task{await chat.decidePreference(preference,consent:consent,dismiss:ignored)}},correct:{message=preference.label+": ";composing=true})
                        }
                        if let warning=chat.session?.memoryWarning,!warning.isEmpty{Text(uiText(warning)).font(.caption).foregroundStyle(Palette.muted)}
                        if let session=chat.session,!session.pendingMemory.isEmpty{
                            VStack(alignment:.leading,spacing:8){Text(uiText("以后也这样安排，好吗？")).font(.headline);Text(uiText(session.pendingMemory)).font(.subheadline);HStack{Button(uiText("以后都记住")){Task{await chat.decideMemory(true)}};Button(uiText("仅限这次")){Task{await chat.decideMemory(false)}}}}.padding(14).background(chatBlue.opacity(0.05),in:RoundedRectangle(cornerRadius:14)).disabled(chat.busy)
                        }
                        if let error=chat.error{Text(uiText(error)).font(.caption).foregroundStyle(.red)}
                        Color.clear.frame(height:1).id("studio-bottom")
                    }.padding(.horizontal,20).padding(.bottom,8)
                }.scrollDismissesKeyboard(.interactively)
                .onChange(of:chat.session?.messages.count){_,_ in withAnimation{proxy.scrollTo("studio-bottom",anchor:.bottom)}}
                .onChange(of:requestedInspiration){_,_ in Task{@MainActor in await Task.yield();withAnimation{proxy.scrollTo("studio-bottom",anchor:.bottom)}}}
                .onChange(of:composing){_,_ in Task{@MainActor in await Task.yield();proxy.scrollTo("studio-bottom",anchor:.bottom)}}
            }
            if !requestedInspiration && !composing{
                Button{requestedInspiration=true;inspirationOverride=nil}label:{Label(uiText("Get a travel idea"),systemImage:"sparkles").font(.subheadline.weight(.semibold)).frame(maxWidth:.infinity,minHeight:48).background(chatBlue.opacity(0.035),in:RoundedRectangle(cornerRadius:14)).overlay(RoundedRectangle(cornerRadius:14).stroke(chatBlue.opacity(0.5)))}.buttonStyle(BuddyPressStyle()).padding(.horizontal,20).disabled(chat.busy).accessibilityIdentifier("studio-inspiration")
            }
            Button{
                composing=false;fields=liveFields;assumptionsConfirmed=true;Task{await preparePlan()}
            }label:{Label(uiText("生成行程"),systemImage:"arrow.right").font(.subheadline.weight(.semibold)).frame(minHeight:44)}.buttonStyle(.plain).disabled(!canPlanDirectly || chat.busy || store.busy || store.isGeneratingTrip || !message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty).accessibilityIdentifier("chat-generate")
            if waitingForDeferredDates{Button(uiText("Choose dates to generate")){basicsConfirmed=false}.font(.caption).frame(minHeight:44)}
            HStack(alignment:.bottom,spacing:10){
                TextField(uiText("Tell Koko what makes a good day…"),text:$message,axis:.vertical).focused($composing).lineLimit(1...4).padding(14).background(Color.gray.opacity(0.055),in:RoundedRectangle(cornerRadius:20)).accessibilityIdentifier("chat-composer")
                Button{let text=message;composing=false;Task{await chat.send(text);if chat.error==nil{message=""}}}label:{Image(systemName:"arrow.up").font(.headline).foregroundStyle(.white).frame(width:46,height:46).background(chatBlue,in:Circle())}.disabled(chat.busy || message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty || message.count>2000).accessibilityLabel(uiText("发送消息"))
            }.padding(.horizontal,20).padding(.bottom,12)
        }
    }
    private let chatBlue=Color(red:0.20,green:0.36,blue:0.79)
    private var waitingForDeferredDates:Bool {
        !personalSetup && chat.session?.dialogue?.dateDeferred==true &&
        ["destination","travelers","budget","budgetCurrency","budgetBasis"].allSatisfy{TravelRequirements.known(liveFields[$0])} &&
        (!TravelRequirements.known(liveFields["date"]) || !TravelRequirements.known(liveFields["days"]))
    }
    private func presentsInspiration(_ session:PetSession)->Bool {
        (requestedInspiration || KokoInspirationPresentation.shouldShow(session)) && acceptedInspiration != (inspirationOverride ?? KokoInspirationPresentation.direction(session))
    }
    private var replyChoices:some View {Group {
            if !chat.busy,let session=chat.session,!session.contextualReplies.isEmpty,!canPlanDirectly,!waitingForDeferredDates,(personalSetup || !presentsInspiration(session)) {
                KokoQuickReplies(replies:session.contextualReplies.filter{personalSetup || !presentsInspiration(session) || !["accept_direction","change_direction"].contains($0.intent)},busy:chat.busy,stacked:personalSetup,compact:composing,quiet:!personalSetup){reply in
                    switch reply.intent {
                    case "compose_name":message="叫我";composing=true
                    case "details":if personalSetup{store.finishPersonalConversation(chat.session,useClues:true)}else{composing=false;detailRoute=TripDetailRoute(id:-1)}
                    case "details_destination":composing=false;detailRoute=TripDetailRoute(id:0)
                    case "details_time":composing=false;detailRoute=TripDetailRoute(id:1)
                    case "details_people":composing=false;detailRoute=TripDetailRoute(id:2)
                    case "details_budget":composing=false;detailRoute=TripDetailRoute(id:3)
                    case "keep_draft":if personalSetup{store.finishPersonalConversation(chat.session,useClues:true)}else if onboarding{store.returnToTrips(draft:liveFields)}else{dismiss()}
                    case "compose_idea":composing=true
                    default:composing=false;Task{await chat.send(reply.text,reply:reply)}
                    }
                }
            }
            if !personalSetup,!chat.busy,chat.session?.dialogue==nil,!canPlanDirectly,let session=chat.session,!session.nextField.isEmpty{quickAnswers(session)}
    }}
    @State private var choosingDestination=false
    var body:some View {
        let _=interfaceLocale
        ZStack {
            Group{if usesPlanningStudio{planningStudio}else{conversationContent}}.opacity(review ? 0:1).allowsHitTesting(!review).accessibilityHidden(review)
            if review{requirementsPanel}
        }
        .toolbar(.hidden,for:.navigationBar)
        .onAppear {
            if store.koalaWelcomePending {chat.koalaCue=KoalaCue(event:.welcome,id:"welcome");store.koalaWelcomePending=false;UserDefaults.standard.set(true,forKey:"koala.welcome.seen")}
        }
        .onChange(of:chat.session?.id){old,new in if old != nil,old != new{basicsConfirmed=false;studioStartID=nil;chat.personalizingTrip=false}}
        .onChange(of:review){_,value in if value{composing=false}}
        .onChange(of:chat.session?.dialogue?.proposedDirection){_,_ in acceptedInspiration=nil;inspirationOverride=nil}
        .task(id:scope){
            acceptedInspiration=nil;requestedInspiration=false;inspirationOverride=nil;basicsConfirmed=false;studioStartID=nil
            var seed:[String:String]=personalSetup ? [:]:(onboarding || draftScope) ? store.pendingTripFields:[:]
            // Legacy habits remain context; only explicit trip answers populate fields.
            if !personalSetup && !onboarding && !draftScope,let t=store.trip{seed=TripConversationSeed.fields(t.profile)}
            chat.interactionContext=interactionContext
            let rawPending=UserDefaults.standard.string(forKey:"petComposer."+scope) ?? ""
            let pendingMessage=PetComposerDraft.restored(rawPending,contextual:!interactionContext.isEmpty)
            if rawPending != pendingMessage{UserDefaults.standard.set(rawPending,forKey:"petComposer.legacyContext."+scope)}
            parkedMessage=UserDefaults.standard.string(forKey:"petComposer.parked."+scope) ?? ""
            if autoSendInitial {
                if !pendingMessage.isEmpty{parkedMessage=pendingMessage;UserDefaults.standard.set(pendingMessage,forKey:"petComposer.parked."+scope)}
                message=""
            }else{message=initialMessage.isEmpty || pendingMessage.hasPrefix(initialMessage) ? pendingMessage:initialMessage+(pendingMessage.isEmpty ? "":"\n"+pendingMessage)}
            await chat.load(scope:scope,seed:seed,personal:(onboarding || draftScope) ? store.pendingPersonalProfile:nil,profiling:personalSetup || onboarding || draftScope)
            if usesPlanningStudio,let id=chat.session?.id {
                basicsConfirmed=UserDefaults.standard.bool(forKey:"koko.studio.basics."+id) && studioBasicsReady
                studioStartID=UserDefaults.standard.string(forKey:"koko.studio.start."+id)
                chat.personalizingTrip=basicsConfirmed
            }
            if autoSendInitial,!sentInitial,!initialMessage.isEmpty,chat.error==nil {
                sentInitial=true;await chat.send(initialMessage)
                if chat.error != nil{message=initialMessage+(message.isEmpty ? "":"\n"+message)}
            }
        }
        .onChange(of:message){_,value in UserDefaults.standard.set(value,forKey:"petComposer."+scope)}
        .onChange(of:chat.session?.fields){_,value in
            if (onboarding || draftScope),let value{store.pendingTripFields=chat.session?.planningFields ?? value}
            if value != nil{assumptionsConfirmed=false}
            if canPlanDirectly{composing=false}
        }
        .onChange(of:chat.session?.planningFields){_,value in
            if (onboarding || draftScope),let value{store.pendingTripFields=value;assumptionsConfirmed=false}
        }
        .sheet(isPresented:$choosingDestination){destinationIdeas}
        .sheet(item:$detailRoute){route in
            if route.id>=0 {GuidedTripDetails(fields:liveFields,startStage:route.id,single:true,save:{delta in await chat.update(delta);return chat.error})}
            else{TripDetailsSummary(fields:liveFields,save:{delta in await chat.update(delta);return chat.error})}
        }
        .sheet(isPresented:$memories){PetMemoryView(chat:chat)}
        .confirmationDialog(uiText("清空本次对话和旅行草稿？已保存行程与长期偏好保留。"),isPresented:$clearing,titleVisibility:.visible){Button(uiText("清空"),role:.destructive){Task{await chat.clear();if chat.error==nil{message=""}}}}
    }
    private var planningShortcuts:some View {
        ScrollView(.horizontal,showsIndicators:false){HStack(spacing:8){ForEach(TripPlanningShortcuts.items(liveFields,ready:canPlanDirectly),id:\.stage){item in
            Button{composing=false;detailRoute=TripDetailRoute(id:item.stage)}label:{Label(uiText(item.title),systemImage:"pencil").font(.caption.weight(.semibold)).padding(.horizontal,13).frame(minHeight:44).background(Palette.mint.opacity(0.6),in:Capsule())}.buttonStyle(BuddyPressStyle()).accessibilityIdentifier("chat-edit-stage-"+String(item.stage))
        }}}.padding(.horizontal,20).padding(.vertical,4)
    }
    private var preparationStatus:String {
        if !onboarding && !draftScope{return "告诉 Koko 你想怎么调整～"}
        if let blocker=directPlanningBlocker{return blocker}
        let missing=TravelRequirements.missingFields(liveFields)
        return canPlanDirectly ? "小背包准备好了，随时可以生成":"还差："+missing.prefix(2).joined(separator:"、")+(missing.count>2 ? "等信息":"")
    }
    private var backpack:some View {
        Button{composing=false;detailRoute=TripDetailRoute(id:-1)}label:{HStack(spacing:8){
            Image(systemName:"mappin.and.ellipse").foregroundStyle(chatBlue)
            Text(uiText(TripDraftSummary(fields:liveFields).title+" · "+(waitingForDeferredDates ? "Dates undecided":TripDraftSummary(fields:liveFields).subtitle))).font(.caption).foregroundStyle(Palette.muted).lineLimit(2)
            Spacer(minLength:0);Image(systemName:"pencil").font(.subheadline).foregroundStyle(chatBlue)
        }.padding(.horizontal,14).padding(.vertical,12).frame(maxWidth:.infinity,alignment:.leading).background(Color.clear)}.buttonStyle(.plain).padding(.horizontal,20).padding(.bottom,8).disabled(chat.busy).accessibilityLabel(uiText("旅行资料，\(TripDraftSummary(fields:liveFields).title)，点击修改")).accessibilityIdentifier("chat-trip-details")
    }

    private var liveFields:[String:String]{chat.session?.effectiveFields ?? [:]}
    private var directProfile:TripProfile? {
        guard !personalSetup,chat.session != nil else{return nil}
        if onboarding || draftScope{return try? TravelRequirements.readyProfile(liveFields,personal:store.pendingPersonalProfile)}
        return try? TripConversationSeed.profile(liveFields,base:store.trip?.profile ?? store.profile)
    }
    private var canPlanDirectly:Bool{directProfile != nil}
    private var generationIssue:String? {
        guard !personalSetup,chat.session != nil,!canPlanDirectly else{return nil}
        if TravelRequirements.known(liveFields["destination"]),!TravelRequirements.supportedDestination(liveFields["destination"]){return "资料已保存。目前仅支持悉尼路线；你可以修改城市，或返回我的行程保留草稿。"}
        if TravelRequirements.known(liveFields["budgetCurrency"]),liveFields["budgetCurrency"] != "AUD"{return "预算已保留，当前路线需要确认 AUD 活动预算，不会自动换算汇率。"}
        if liveFields["budgetCoverage"]=="totalTravel"{return "这笔钱包含住宿或交通，需要先单独确认活动预算，才能生成路线。"}
        if TravelRequirements.missingFields(liveFields).isEmpty{return directPlanningBlocker}
        return nil
    }
    private var correctionStage:Int {GuidedTripDetails.correctionStage(liveFields)}
    private var directPlanningBlocker:String? {
        guard (!conversationOnly || onboarding),let session=chat.session else{return nil}
        let missing=TravelRequirements.missingFields(liveFields)
        if !missing.isEmpty{return session.nextField.isEmpty ? "还需要确认："+missing.joined(separator:"、")+"。直接在下面告诉 Koko 就好。":nil}
        do{if onboarding || draftScope{_=try TravelRequirements.readyProfile(liveFields,personal:store.pendingPersonalProfile)}else{_=try TripConversationSeed.profile(liveFields,base:store.trip?.profile ?? store.profile)};return nil}catch{return error.localizedDescription}
    }
    private func quickAnswers(_ session:PetSession)->some View {
        VStack(alignment:.leading,spacing:7){
            HStack(alignment:.firstTextBaseline){Text(uiText(canPlanDirectly ? "可选补充："+session.question:session.question)).font(.caption).foregroundStyle(Palette.muted).lineLimit(2);Spacer();Button(uiText(TravelRequirements.requiredKeys.contains(session.nextField) ? "暂不确定":"稍后补充")){composing=false;Task{await chat.send("稍后补充",field:session.nextField,value:"未确定")}}.font(.caption).disabled(chat.busy)}
            ScrollView(.horizontal,showsIndicators:false){HStack(spacing:8){ForEach(session.options,id:\.self){option in
                Button{composing=false;let label=TravelRequirements.optionTitle(option,field:session.nextField);Task{await chat.send(label,field:session.nextField,value:option)}}label:{Text(uiText(TravelRequirements.optionTitle(option,field:session.nextField))).font(.subheadline.weight(.medium)).padding(.horizontal,14).padding(.vertical,10).background(.white,in:Capsule()).overlay(Capsule().stroke(Palette.green.opacity(0.2)))}.buttonStyle(.plain).disabled(chat.busy)
            }}}
            if session.nextField=="destination" {Button(uiText("还没想好？让 Koko 帮我选"),systemImage:"sparkles"){choosingDestination=true}.font(.subheadline.bold()).disabled(chat.busy)}
        }.padding(.horizontal,20).padding(.top,8).accessibilityIdentifier("chat-quick-answers")
    }
    private var destinationIdeas:some View {
        NavigationStack {
            ScrollView {VStack(alignment:.leading,spacing:18){
                HStack{PetAvatar(mood:.curious).frame(width:100,height:110);Text(uiText("哼唧，选不出来就交给我嘛～先看看你想过哪种小假期？")).font(.headline)}
                Text(uiText("目前能实际生成路线的城市是悉尼。下面是悉尼的玩法灵感，选择后仍可继续调整需求。")).font(.caption).foregroundStyle(Palette.muted)
                if !store.pendingPersonalProfile.summary.isEmpty{Text(uiText("我记得你："+store.pendingPersonalProfile.summary)).font(.caption).foregroundStyle(Palette.green)}
                ForEach([("海风与绿意","歌剧院外观、皇家植物园，适合喜欢自然与风景的你。"),("城市与美食","岩石区、城市街区，给逛吃留些时间。"),("艺术与故事","博物馆、美术馆，慢慢发现城市的另一面。")],id:\.0){title,detail in
                    VStack(alignment:.leading,spacing:10){Text(uiText(title)).font(.headline);Text(uiText(detail)).font(.subheadline).foregroundStyle(Palette.muted)
                        Button(uiText("选悉尼，继续聊")){choosingDestination=false;Task{let interests=title=="海风与绿意" ? "自然、风景":title=="城市与美食" ? "城市、美食":"艺术、文化";await chat.update(["interests":interests]);if chat.error==nil{await chat.send("这次想体验"+title+"，我选悉尼",field:"destination",value:"悉尼")}}}.buttonStyle(.borderedProminent).tint(Palette.green)
                    }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:18))
                }
            }.padding(20)}.background(Palette.cream).navigationTitle(uiText("Koko 帮你选")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("关闭")){choosingDestination=false}}
        }
    }
    // Compact only the known fixed acknowledgement already duplicated by the summary.
    // Free-form assistant replies remain verbatim, and the original stays available inline.
    private func isRepeatedReadyAcknowledgement(_ text:String)->Bool {
        text.range(of:#"^We[’']ll use an activity budget of A\$[0-9.,]+ per person per day\. Accommodation, transport and meals are separate\.\s+Your trip details are ready\. Generate the itinerary, keep chatting or change a detail whenever you like\.$"#,options:.regularExpression) != nil
    }
    private func planningDate(_ profile:TripProfile)->String {
        let parser=DateFormatter();parser.dateFormat="yyyy-MM-dd";parser.locale=Locale(identifier:"en_US_POSIX");parser.timeZone=TimeZone(identifier:"Australia/Sydney")
        let formatter=DateFormatter();formatter.locale=Locale(identifier:store.language.rawValue);formatter.timeZone=parser.timeZone;formatter.setLocalizedDateFormatFromTemplate("MMM d")
        let date=parser.date(from:profile.date).map{formatter.string(from:$0)} ?? profile.date
        return profile.days==1 ? store.language.text("{0} · 1 day",date):store.language.text("{0} · {1} days",date,String(profile.days))
    }
    private func summaryRow(_ title:String,_ value:String,stage:Int)->some View {
        Button{composing=false;detailRoute=TripDetailRoute(id:stage)}label:{
            HStack(alignment:.firstTextBaseline,spacing:12){Text(uiText(title)).font(.caption).foregroundStyle(Palette.muted).frame(width:76,alignment:.leading);Text(uiText(value)).font(.subheadline).multilineTextAlignment(.leading);Spacer(minLength:0);Image(systemName:"chevron.right").font(.caption2)}.frame(minHeight:44).contentShape(Rectangle())
        }.buttonStyle(.plain).accessibilityIdentifier("chat-edit-stage-"+String(stage))
    }
    private var directPlanningCard:some View {
        VStack(alignment:.leading,spacing:8){
            Text(uiText("Our trip plan")).font(.system(.title3,design:.rounded,weight:.bold))
            if let profile=directProfile{
                summaryRow("When",planningDate(profile),stage:1)
                Divider()
                summaryRow("Starting near",profile.origin ?? uiText("Details to confirm"),stage:4)
                Divider()
                summaryRow("Traveling",String(profile.travelers ?? 1)+" · "+uiText(profile.rhythm ?? "Relaxed"),stage:2)
                Divider()
                summaryRow("Activities",profile.activityBudgetSummary,stage:3)
            }
            DisclosureGroup(uiText("查看安排细节与临时假设")){
            Picker(uiText("这次怎么出行"),selection:Binding(get:{liveFields["transportPreference"] ?? "balanced"},set:{value in Task{await chat.update(["transportPreference":value])}})){
                Text(uiText("步行＋公交")).tag("balanced");Text(uiText("尽量少走")).tag("lessWalking");Text(uiText("只步行")).tag("walk")
            }.pickerStyle(.segmented).accessibilityIdentifier("trip-transport-preference")

                if let profile=directProfile{
                if let must=profile.mustDo,TravelRequirements.known(must),must != "无"{Text(uiText("想去："+must)).font(.caption)}
                if let avoid=profile.avoid,TravelRequirements.known(avoid),avoid != "无"{Text(uiText("避开："+avoid)).font(.caption)}
            }
                Picker(uiText("每天想怎么玩"),selection:Binding(get:{liveFields["dayStyle"] ?? "flexible"},set:{value in Task{await chat.update(["dayStyle":value])}})){
                    Text(uiText("暂不确定")).tag("flexible");Text(uiText("半天游玩")).tag("halfDay");Text(uiText("慢逛一天")).tag("fullDay")
                }.pickerStyle(.segmented).accessibilityIdentifier("trip-day-style")
                Text(uiText("半天游玩在15:00前返回；慢逛一天可安排至18:00。地点不足时保留自由活动。")).font(.caption)
                ForEach(TravelRequirements.assumptions(liveFields,personal:store.pendingPersonalProfile),id:\.self){Text(uiText($0)).font(.caption).fixedSize(horizontal:false,vertical:true)}
            }.font(.caption).foregroundStyle(Palette.muted)
            Text(uiText("Meals, accommodation and transport extra.")).font(.caption2).foregroundStyle(Palette.muted)
            Button{composing=false;fields=liveFields;assumptionsConfirmed=true;Task{await preparePlan()}}label:{Label(uiText(onboarding || draftScope ? "生成行程":"按这些信息调整行程"),systemImage:"arrow.right").font(.headline).frame(maxWidth:.infinity,minHeight:50)}.buttonStyle(TripsCreateButtonStyle()).disabled(chat.busy || store.busy || store.isGeneratingTrip || !message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty).accessibilityIdentifier("chat-generate")
            if !message.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty{Text(uiText("先发送新想法，再按最新需求生成")).font(.caption).foregroundStyle(Palette.muted)}
            if store.isGeneratingTrip{Text(uiText("另一段旅行正在准备，你可以继续聊天；完成后再生成新的安排。")).font(.caption).foregroundStyle(Palette.muted)}
        }.padding(16).frame(maxWidth:.infinity,alignment:.leading).background(chatBlue.opacity(0.035),in:RoundedRectangle(cornerRadius:18)).overlay(RoundedRectangle(cornerRadius:18).stroke(chatBlue.opacity(0.18))).accessibilityIdentifier("chat-ready-summary")
    }
    private func bubble(_ text:String,user:Bool,mood:PetMood = .curious,showAvatar:Bool=true,lineLimit:Int?=nil)->some View {
        HStack(alignment:.top){if user{Spacer(minLength:30)}else if showAvatar{PetAvatar(mood:companionMood,cue:companionCue,size:58).frame(width:54,height:62)}else{Color.clear.frame(width:4,height:1)};Text(verbatim:user ? text:uiText(text)).font(.subheadline).lineSpacing(4).lineLimit(lineLimit).padding(16).background(user ? chatBlue.opacity(0.10):Color.gray.opacity(0.055),in:RoundedRectangle(cornerRadius:18));if !user{Spacer(minLength:25)}}
    }
    private var requirementsPanel:some View {
        VStack(spacing:0) {
            HStack {
                Button {Task{await saveReview(leave:false)}} label:{Label(uiText("返回对话"),systemImage:"chevron.left")}.accessibilityIdentifier("requirements-back")
                Spacer();Text(uiText("旅行需求摘要")).font(.headline)
            }.padding(20).disabled(chat.busy)
            ScrollView {
                VStack(alignment:.leading,spacing:16) {
                    VStack(alignment:.leading,spacing:12) {
                        Text(uiText(TripDraftSummary(fields:fields).title)).font(.title3.bold())
                        Text(uiText(TripDraftSummary(fields:fields).subtitle)).font(.subheadline).foregroundStyle(Palette.muted)
                        ForEach((petFields+travelExtraFields).filter{TravelRequirements.known(fields[$0.0]) && !["destination","days","date","companions"].contains($0.0)},id:\.0){key,label in
                            HStack(alignment:.top){Text(uiText(label)).foregroundStyle(Palette.muted);Spacer();Text(uiText(key=="budgetCoverage" ? (fields[key]=="activities" ? "活动费用":fields[key]=="totalTravel" ? "包含住宿或交通的总费用":"待确认"):fields[key] ?? "")).multilineTextAlignment(.trailing)}.font(.subheadline)
                        }
                        if let basis=TravelRequirements.budgetBases.first(where:{$0.0==fields["budgetBasis"]}){LabeledContent(uiText("预算范围"),value:basis.1).font(.subheadline)}
                    }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:22))
                    if let issue=validationIssue {
                        Label(uiText(issue),systemImage:"info.circle").font(.subheadline).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)
                    }
                    DisclosureGroup(uiText("修改或补充需求"),isExpanded:$editingRequirements) {
                        VStack(spacing:16){
                            ForEach(petFields+travelExtraFields,id:\.0){key,label in
                                if key=="budgetCoverage" {
                                    Picker(uiText(label),selection:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0})){Text(uiText("待确认")).tag("");Text(uiText("活动费用")).tag("activities");Text(uiText("包含住宿或交通的总费用")).tag("totalTravel")}
                                } else {
                                    VStack(alignment:.leading,spacing:5){Text(uiText(label)).font(.caption).foregroundStyle(Palette.muted);TextField(uiText("暂未确定"),text:Binding(get:{fields[key] ?? ""},set:{fields[key]=$0}),axis:.vertical).textFieldStyle(.roundedBorder).accessibilityLabel(uiText(label))}
                                }
                            }
                            Picker(uiText("预算范围"),selection:Binding(get:{fields["budgetBasis"] ?? ""},set:{fields["budgetBasis"]=$0})){
                                Text(uiText("请选择")).tag("");ForEach(TravelRequirements.budgetBases,id:\.0){Text(uiText($0.1)).tag($0.0)}
                            }
                        }.padding(.top,14)
                    }.padding(18).background(.white,in:RoundedRectangle(cornerRadius:22))
                    DisclosureGroup(uiText("临时假设与待核实信息")) {
                        VStack(alignment:.leading,spacing:10){
                            ForEach(TravelRequirements.assumptions(fields,personal:store.pendingPersonalProfile),id:\.self){Text(uiText("• "+$0)).font(.caption)}
                            Text(uiText("当前支持悉尼 1–15 天、AUD 活动预算。示例地点、交通、排队及特殊要求仍需核实。")).font(.caption)
                        }.padding(.top,12)
                    }.padding(18).background(.white,in:RoundedRectangle(cornerRadius:22))
                    Toggle(uiText("我已查看并确认需求及临时假设"),isOn:$assumptionsConfirmed).font(.subheadline).tint(Palette.green)
                    if let error=chat.error{Text(uiText(error)).font(.caption).foregroundStyle(.red)}
                }.padding(.horizontal,20).padding(.bottom,20)
            }.scrollDismissesKeyboard(.interactively)
            VStack(spacing:12){
                if !conversationOnly || onboarding {
                    PrimaryButton(title:onboarding ? "生成行程":"用这些信息调整行程",symbol:"sparkles"){Task{await preparePlan()}}.disabled(chat.busy || store.busy || validationIssue != nil || !assumptionsConfirmed)
                }
                Button(uiText("保存需求，返回我的行程")){Task{await saveReview(leave:true)}}.font(.subheadline).disabled(chat.busy)
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
            } else {_=try TripConversationSeed.profile(fields,base:store.trip?.profile ?? store.profile)}
            return nil
        }catch{return error.localizedDescription}
    }
    private func preparePlan() async {
        guard assumptionsConfirmed else{return}
        await chat.update(fields);guard chat.error==nil else{return}
        let profile:TripProfile
        do {profile=try (onboarding || draftScope) ? TravelRequirements.readyProfile(fields,personal:store.pendingPersonalProfile):TripConversationSeed.profile(fields,base:store.trip?.profile ?? store.profile)}
        catch {chat.error=error.localizedDescription;review=true;return}
        review=false
        if onboarding || draftScope {
            fields["assumptionsConfirmed"]="true"
            store.completePetPreferences(profile,fields:fields)
            store.startGeneratingPendingTrip()
            return
        }
        dismiss()
        await store.generateFromPet(profile,request:TripAdjustmentRequest.make(messages:chat.session?.messages ?? [],fallback:profile.request ?? ""))
    }

}

struct PetMemoryView:View {
    @Environment(\.locale) private var interfaceLocale

    @EnvironmentObject private var store:AppStore
    @ObservedObject var chat:PetChatStore
    @Environment(\.dismiss) private var dismiss
    @State private var edit:Memory?
    @State private var text=""
    @State private var deleting:Memory?
    @State private var editingProfile=false
    private func displayValue(_ key:String,_ value:String)->String {
        guard key=="budgetCoverage" else{return value}
        switch value{case "activities":return "活动费用";case "totalTravel":return "含住宿或交通的总费用";default:return "待确认"}
    }
    private var tripMemoryRows:[(key:String,label:String,value:String)] {
        var rows:[(key:String,label:String,value:String)]=[]
        for (key,label) in petFields+travelExtraFields {
            if let value=chat.session?.fields[key]{rows.append((key,key=="date" ? "出发日期":label,displayValue(key,value)))}
        }
        if let basis=chat.session?.fields["budgetBasis"],let item=TravelRequirements.budgetBases.first(where:{$0.0==basis}){rows.append(("budgetBasis","预算范围",item.1))}
        return rows
    }
    var body:some View {
        let _=interfaceLocale
        NavigationStack {
            List {
                Section(uiText("本次旅行")) {ForEach(tripMemoryRows,id:\.key){row in LabeledContent(uiText(row.label),value:row.value)}}
                Section(uiText("旅行习惯 · 已确认与待确认")) {
                    ForEach(chat.session?.travelDna ?? []){fact in VStack(alignment:.leading,spacing:4){Text(uiText(fact.summary));Text(uiText(fact.status=="confirmed" ? "已确认":"等待你确认")).font(.caption).foregroundStyle(Palette.muted)}}
                }
                Section(uiText("本次偏好")) {ForEach(chat.session?.tripPreferences ?? []){fact in Text(uiText(fact.summary))}}
                Section(uiText("长期偏好 · 下次也会参考")) {
                    if chat.memories.isEmpty{Text(uiText("还没有长期偏好。聊一聊，确认后我再记住。"))}
                    ForEach(chat.memories){m in VStack(alignment:.leading,spacing:8){Text(uiText(m.summary));HStack{Button(uiText("修改")){if m.profile.habits != nil{editingProfile=true}else{edit=m;text=m.profile.travelDna?.value ?? m.summary}};Spacer();Button(uiText("撤回"),role:.destructive){deleting=m}}.font(.caption)}}
                }
                if let error=chat.error{Text(uiText(error)).foregroundStyle(.red)}
            }.navigationTitle(uiText("Koko 记住的事")).toolbar{Button(uiText("完成")){dismiss()}}.task{await chat.refreshMemories()}
            .sheet(isPresented:$editingProfile,onDismiss:{Task{await chat.refreshMemories()}}){PersonalPreferencesView(editing:true)}
            .alert(uiText("修改长期偏好"),isPresented:Binding(get:{edit != nil},set:{if !$0{edit=nil}})){TextField(uiText("偏好"),text:$text);Button(uiText("取消"),role:.cancel){edit=nil};Button(uiText("保存")){if let m=edit{Task{await chat.editMemory(m,text:text)}};edit=nil}.disabled(text.trimmingCharacters(in:.whitespaces).isEmpty)}
            .confirmationDialog(uiText("撤回后，未来对话和规划将不再检索这条偏好。"),isPresented:Binding(get:{deleting != nil},set:{if !$0{deleting=nil}})){Button(uiText("撤回偏好"),role:.destructive){if let m=deleting{Task{await store.deleteMemory(m);await chat.refreshMemories()}};deleting=nil}}
        }
    }
}


struct PetTripDraft {
    static func profile(_ fields:[String:String],base:TripProfile=TripProfile()) throws -> TripProfile {
        func failure(_ message:String)->NSError{NSError(domain:"PetDraft",code:1,userInfo:[NSLocalizedDescriptionKey:message])}
        guard TravelRequirements.supportedDestination(fields["destination"]) else{throw failure("这份资料可保存。当前只能生成悉尼路线，其他城市暂未接入。")}
        guard let days=Int(fields["days"] ?? ""),(1...15).contains(days),let budget=Double(fields["budget"] ?? ""),budget.isFinite,budget>=0,budget<=10000 else{throw failure("请填写 1–15 天和有效的每日活动预算（纯数字，最多 10000 AUD）。")}
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
    @Environment(\.locale) private var interfaceLocale

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
        let _=interfaceLocale
        VStack(spacing:0) {
            HStack(spacing:16) {
                Button { step=step==5 ? 2:max(0,step-1) } label:{Image(systemName:"chevron.left").font(.headline).frame(width:36,height:36)}.disabled(step==0 || working).accessibilityLabel(uiText("上一题"))
                ProgressView(value:Double(step==5 ? 3:min(step,2)),total:3).tint(Palette.green)
                Text(uiText(step==5 ? "准备出发" : "\(min(step+1,3)) / 3")).font(.caption.bold()).foregroundStyle(Palette.muted)
                Menu { Button(uiText("重新体验开场")){restarting=true}; Button(uiText("查看已有行程")){store.showOnboarding=false;UserDefaults.standard.set(true,forKey:"onboarded");Task{await store.reload()}} } label:{Image(systemName:"ellipsis").frame(width:32,height:36)}.disabled(working)
            }.padding(.horizontal,20).padding(.top,12)
            HStack(alignment:.center,spacing:8){
                VStack(spacing:2){Button{reaction=PetReaction(text:"嘿嘿，我在这儿！小短腿已经准备好啦～你慢慢选，我陪着你。",mood:.excited)}label:{PetAvatar(mood:sceneMood).scaleEffect(1.15).frame(width:122,height:137)}.buttonStyle(.plain).accessibilityLabel(uiText("摸摸 Koko"));Text(uiText("戳戳我呀")).font(.caption2).foregroundStyle(Palette.muted)}
                PetSpeechBubble(text:speech)
            }.padding(.horizontal,18).padding(.vertical,12)
            if !lastAnswer.isEmpty {HStack{Spacer(minLength:30);Text(verbatim:lastAnswer).font(.subheadline.bold()).padding(.horizontal,16).padding(.vertical,10).background(Palette.mint,in:RoundedRectangle(cornerRadius:18)).accessibilityLabel(uiText("你的回答："+lastAnswer))}.padding(.horizontal,24).padding(.bottom,10)}
            ScrollView {
                VStack(spacing:14) {
                    if !chat.memories.isEmpty && (step==3 || step==4){Text(uiText("参考你同意保存的偏好，选项仍可修改。")).font(.caption).foregroundStyle(Palette.green)}
                    VStack(spacing:12) { cardContent }.padding(.top,8)
                    if let problem { Label(uiText(problem),systemImage:"exclamationmark.circle").font(.subheadline).foregroundStyle(.red) }
                    if let error=chat.error { VStack(spacing:10){Text(uiText(error)).font(.caption).foregroundStyle(.red);Button(uiText("重新连接")){Task{await load()}}} }
                }.padding(.horizontal,26).padding(.bottom,24)
            }.scrollDismissesKeyboard(.interactively)
            VStack(spacing:14) {
                PrimaryButton(title:step==5 ? "去我的行程" : reaction==nil ? "就这样告诉 Koko" : "接着聊",symbol:step==5 ? "suitcase.rolling.fill" : "arrow.right") { Task{await advance()} }.disabled(working || !loaded)
                if step<5 {
                    HStack {
                        Button(uiText("先用推荐选项")){Task{await advance()}}.font(.caption).foregroundStyle(Palette.muted)
                        Spacer()
                        Button { Task{await openChat()} } label:{Label(uiText("我想自己说"),systemImage:"bubble.left.and.bubble.right")}.font(.subheadline.bold())
                    }.disabled(working || !loaded)
                } else {
                    Text(uiText("先保存这次旅行偏好，到“我的行程”再生成路线。")).font(.caption).foregroundStyle(Palette.muted)
                }
            }.padding(.horizontal,26).padding(.top,12).padding(.bottom,18).background(Palette.cream)
        }.background(Palette.cream).foregroundStyle(Palette.ink)
        .task { await load() }
        .onChange(of:confirmed){_,keys in if let id=chat.session?.id{UserDefaults.standard.set(Array(keys),forKey:"petWelcome.confirmed."+id)}}
        .onChange(of:step){_,value in reaction=nil;lastAnswer="";problem=nil;if loaded{UserDefaults.standard.set(value,forKey:"petWelcome.step")}}
        .onChange(of:values){_,value in if loaded,let id=chat.session?.id,let data=try? JSONEncoder().encode(value){UserDefaults.standard.set(data,forKey:"petWelcome.values."+id)}}
        .sheet(isPresented:$talking,onDismiss:{Task{await syncChat()}}){NavigationStack{PetChatView(conversationOnly:true).toolbar{ToolbarItem(placement:.topBarTrailing){Button(uiText("回到小背包")){talking=false}}}}}
        .confirmationDialog(uiText("重新开始这次提问？历史行程和长期偏好保留。"),isPresented:$restarting,titleVisibility:.visible){Button(uiText("重新开始"),role:.destructive){Task{await chat.clear();if chat.error==nil{values=defaults;confirmed=[];step=0;loaded=true;UserDefaults.standard.set(chat.session?.id,forKey:"petWelcome.session");UserDefaults.standard.set(0,forKey:"petWelcome.step")}}}}
    }
    @ViewBuilder private var cardContent: some View {
        switch step {
        case 0:
            option("好呀，一起去悉尼！",detail:"海港、花园，还有街角的小惊喜",icon:"sun.horizon.fill",selected:true){choose("destination","悉尼")}
            Text(uiText("这一版先陪你探索悉尼。其他城市还在准备中。")).font(.caption).foregroundStyle(Palette.muted)
        case 1:
            DatePicker(uiText("出发日期"),selection:day,displayedComponents:.date).datePickerStyle(.compact).padding(18).background(.white,in:RoundedRectangle(cornerRadius:20))
            VStack(spacing:14) {
                Stepper(uiText("旅行天数：\(values["days"] ?? "1") 天"),value:Binding(get:{Int(values["days"] ?? "1") ?? 1},set:{choose("days",String($0))}),in:1...15)
                Slider(value:Binding(get:{Double(values["days"] ?? "1") ?? 1},set:{values["days"]=String(Int($0))}),in:1...15,step:1,onEditingChanged:{editing in if !editing{choose("days",values["days"] ?? "1")}}).tint(Palette.green).accessibilityLabel(uiText("旅行天数"))
                HStack{Text(uiText("1 天"));Spacer();Text(uiText("15 天"))}.font(.caption).foregroundStyle(Palette.muted)
            }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:20)).disabled(working)

        case 2:
            ForEach([("一个人","person.fill"),("两个人","heart.fill"),("和朋友","person.2.fill"),("和家人","house.fill")],id:\.0){label,icon in option(label,icon:icon,selected:values["companions"]==label){choose("companions",label)}}
        case 3:
            ForEach([("自然","leaf.fill"),("风景","sun.max.fill"),("美食","fork.knife"),("文化","building.columns.fill"),("艺术","paintpalette.fill"),("城市","building.2.fill")],id:\.0){label,icon in option(label,icon:icon,selected:(values["interests"] ?? "").contains(label)){var picked=(values["interests"] ?? "").components(separatedBy:"、").filter{!$0.isEmpty};if picked.contains(label){picked.removeAll{$0==label}}else{picked.append(label)};choose("interests",picked.joined(separator:"、"),reactingTo:label);if !picked.contains(label){reaction=PetReaction(text:"收到，这个先不放进背包。还有别的喜欢的吗？",mood:.curious)}}}
        case 4:
            if (values["pace"] ?? "").hasPrefix("沿用偏好"){option(values["pace"]!,detail:"你之前同意保存的步数",icon:"heart.fill",selected:true){}}
            ForEach([("慢悠悠（约6000步）","多坐一会儿，多看看风景","tortoise.fill"),("适中（约10000步）","走走停停，刚刚好","figure.walk"),("多逛逛（约15000步）","想把一天装得满一点","shoeprints.fill")],id:\.0){label,detail,icon in option(label,detail:detail,icon:icon,selected:values["pace"]==label){choose("pace",label)}}
            VStack(alignment:.leading,spacing:12) {
                HStack{Text(uiText("每天步数上限")).font(.headline);Spacer();Text(uiText("\(walkingSteps) 步")).font(.headline).monospacedDigit()}
                Slider(value:Binding(get:{Double(walkingSteps)},set:{values["pace"]="自定义（约\(Int($0))步）"}),in:1000...30000,step:1000,onEditingChanged:{editing in if !editing{choose("pace",values["pace"] ?? "")}}).tint(Palette.green).accessibilityLabel(uiText("每天步数上限"))
                HStack{Text(uiText("1,000 步"));Spacer();Text(uiText("30,000 步"))}.font(.caption).foregroundStyle(Palette.muted)
                Text(uiText("推荐节奏也可以微调，Buddy 会按这个上限安排路线。")).font(.caption).foregroundStyle(Palette.muted)
            }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:20)).disabled(working)
        default:
            VStack(spacing:16){summary("目的地","\(values["destination"] ?? "悉尼") · \(values["days"] ?? "1") 天");summary("出发",values["date"] ?? "");summary("同行",values["companions"] ?? "")}.padding(20).background(.white,in:RoundedRectangle(cornerRadius:24))
            VStack(alignment:.leading,spacing:14) {
                HStack{Text(uiText("每人每日活动预算")).font(.subheadline.bold());Spacer();TextField(uiText("100"),text:Binding(get:{values["budget"] ?? "100"},set:{values["budget"]=$0})).keyboardType(.decimalPad).multilineTextAlignment(.trailing).frame(width:80).accessibilityLabel(uiText("每人每日活动预算"));Text(uiText("AUD")).foregroundStyle(Palette.muted)}
                HStack{ForEach([50,100,200,500],id:\.self){amount in Button{values["budget"]=String(amount)}label:{Text(uiText("$\(amount)")).font(.subheadline.bold()).frame(maxWidth:.infinity).padding(.vertical,10).background(Double(values["budget"] ?? "")==Double(amount) ? Palette.mint:Palette.cream,in:RoundedRectangle(cornerRadius:12))}.buttonStyle(.plain)}}
                Text(uiText("可直接输入金额，最多 10,000 AUD。不包含住宿、交通票价和全部餐饮；活动费用为估算。")).font(.caption).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)
            }.padding(18).background(.white,in:RoundedRectangle(cornerRadius:20)).disabled(working)
            TextField(uiText("还有什么想让我照顾的？（选填）"),text:Binding(get:{values["notes"] ?? ""},set:{values["notes"]=$0}),axis:.vertical).padding(18).background(.white,in:RoundedRectangle(cornerRadius:18))
            VStack(alignment:.leading,spacing:14) {
                Label(uiText("这次带上的喜好"),systemImage:"sparkles").font(.headline)
                summary("兴趣",values["interests"] ?? "")
                summary("节奏",values["pace"] ?? "")
                Divider()
                Button(uiText("调整本次兴趣与步数")){step=3}.font(.subheadline.bold()).disabled(working)
                Text(uiText("这些偏好从个人档案带入。本次调整只用于这次旅行；长期偏好请到“我的”修改。"))
                    .font(.caption).foregroundStyle(Palette.muted).fixedSize(horizontal:false,vertical:true)
            }.padding(20).background(.white,in:RoundedRectangle(cornerRadius:22))
        }
    }
    private var walkingSteps:Int {
        let pace=values["pace"] ?? ""
        if let range=pace.range(of:"[0-9]{3,5}",options:.regularExpression),let steps=Int(pace[range]){return min(30000,max(1000,steps))}
        return 10000
    }
    private func summary(_ label:String,_ value:String)->some View { HStack(alignment:.top){Text(uiText(label)).foregroundStyle(Palette.muted);Spacer();Text(uiText(value)).multilineTextAlignment(.trailing).fontWeight(.medium)}.font(.subheadline) }
    private func option(_ title:String,detail:String?=nil,icon:String,selected:Bool,action:@escaping()->Void)->some View {
        Button(action:action){HStack(spacing:14){Image(systemName:icon).font(.system(size:21)).foregroundStyle(Palette.green).frame(width:28);VStack(alignment:.leading,spacing:5){Text(uiText(title)).font(.system(size:16,weight:.semibold,design:.rounded));if let detail{Text(uiText(detail)).font(.caption).foregroundStyle(Palette.muted)}};Spacer();Image(systemName:selected ? "checkmark.circle.fill" : "circle").foregroundStyle(selected ? Palette.green : Palette.muted.opacity(0.3))}.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(selected ? Palette.mint.opacity(0.6) : .white,in:RoundedRectangle(cornerRadius:21)).overlay(RoundedRectangle(cornerRadius:21).stroke(selected ? Palette.green : Color.clear,lineWidth:2))}.buttonStyle(.plain).disabled(working)
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
        if known(fields["mustDo"]),fields["mustDo"] != "无"{result.append("明确的必去地点会参与规划；无法识别或无法满足时会提示修改。")}
        if known(fields["avoid"]),fields["avoid"] != "无"{result.append("明确的避开地点和类别会参与筛选；无法自动验证的要求会提示确认。")}
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
            guard let days=Int(fields["days"] ?? ""),(1...15).contains(days),let amount=Double(fields["budget"] ?? ""),amount.isFinite,amount>=0,amount<=1000000 else{throw fail("请确认有效的预算金额和 1–15 天的旅行时长。")}
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
        result.dayStyle=fields["dayStyle"];result.transportPreference=["walk","balanced","lessWalking"].contains(fields["transportPreference"] ?? "") ? fields["transportPreference"]:"balanced"
        if fields["dayStyle"]=="halfDay"{result.endMinute=900}
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
    @Environment(\.locale) private var interfaceLocale

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
        let _=interfaceLocale
        NavigationStack {
            VStack(spacing:0) {
                HStack{if step>0{Button{step-=1;response=nil}label:{Image(systemName:"chevron.left")}};Text(uiText(editing ? "修改旅行画像":"认识一下你")).font(.headline);Spacer();Text(uiText(step==5 ? "画像总结":"\(step+1) / 5")).font(.caption).foregroundStyle(Palette.muted)}.padding(20)
                ProgressView(value:Double(step),total:5).tint(Palette.green).padding(.horizontal,22)
                HStack(spacing:8){PetAvatar(mood:chat.busy ? .remembering:step==5 ? .ready:response==nil ? .curious:.relaxed).frame(width:110,height:126);PetSpeechBubble(text:response ?? (step==5 ? "这就是你的旅行小画像！没确定的事情可以以后再慢慢了解。":questions[step]))}.padding(18)
                ScrollView {
                    VStack(spacing:12){
                        switch step {
                        case 0:singleOptions(["尽量不排队","可以等一会儿","为喜欢的地方愿意排队"],value:draft.queueTolerance){draft.queueTolerance=$0}
                        case 1:
                            ForEach([(5000,"轻松走走 · 约 3,000–6,000 步"),(10000,"走走停停 · 约 7,000–12,000 步"),(15000,"多多探索 · 约 13,000–18,000 步")],id:\.0){n,title in choice(title,selected:draft.dailySteps==n){draft.dailySteps=n;response="收到，我会参考这个步数上限照顾你的节奏。"}}
                            VStack(spacing:12){HStack{Text(uiText("自定义步数"));Spacer();Text(uiText(draft.dailySteps.map{"\($0) 步"} ?? "暂未设置")).bold()};Slider(value:Binding(get:{Double(draft.dailySteps ?? 10000)},set:{draft.dailySteps=Int($0)}),in:1000...30000,step:1000).tint(Palette.green).accessibilityLabel(uiText("个人步数偏好"))}.padding(18).background(.white,in:RoundedRectangle(cornerRadius:20))
                        case 2:singleOptions(["低精力","中等精力","高精力"],value:draft.energy){draft.energy=$0}
                        case 3:singleOptions(["轻松随意","适中","紧凑充实"],value:draft.rhythm){draft.rhythm=$0}
                        case 4:
                            LazyVGrid(columns:[GridItem(.flexible()),GridItem(.flexible())],spacing:12){ForEach(interests,id:\.self){item in choice(item,selected:draft.interests?.contains(item)==true){var values=draft.interests ?? [];if values.contains(item){values.removeAll{$0==item}}else{values.append(item)};draft.interests=values.isEmpty ? nil:values;response="小背包收到你的喜好啦！"}}}
                        default:
                            VStack(alignment:.leading,spacing:16){summary("排队",draft.queueTolerance);summary("步数",draft.dailySteps.map{"每天约 \($0) 步"});summary("精力",draft.energy);summary("节奏",draft.rhythm);summary("兴趣",draft.interests?.joined(separator:"、"))}.padding(20).background(.white,in:RoundedRectangle(cornerRadius:22))
                            Text(uiText("这是长期旅行习惯。每次旅行仍可调整，不会自动改写画像。可在“我的 → 个人画像”修改。")).font(.caption).foregroundStyle(Palette.muted)
                        }
                        if let error=chat.error{Text(uiText(error)).font(.caption).foregroundStyle(.red);if !loaded{Button(uiText("重新载入")){Task{await load()}}}}
                    }.padding(22)
                }.disabled(!loaded || chat.busy)
                VStack(spacing:12){
                    PrimaryButton(title:step==5 ? (editing ? "保存画像":"保存画像，开始规划旅行"):(answered ? "下一题":"暂不确定，下一题"),symbol:step==5 ? "sparkles":"arrow.right"){
                        if step<5{step+=1;response=nil}else{Task{await save()}}
                    }.disabled(!loaded || chat.busy)
                    if step<5 && answered{Button(uiText("暂不确定")){clearCurrent();response="没关系，这题先留白。";step+=1}.font(.caption).foregroundStyle(Palette.muted).disabled(chat.busy || !loaded)}
                    if editing{Button(uiText("取消")){dismiss()}.font(.caption).disabled(chat.busy)}
                }.padding(22)
            }.background(Palette.cream).foregroundStyle(Palette.ink).task{await load()}
        }.interactiveDismissDisabled(chat.busy)
    }
    private var answered:Bool{switch step{case 0:return draft.queueTolerance != nil;case 1:return draft.dailySteps != nil;case 2:return draft.energy != nil;case 3:return draft.rhythm != nil;case 4:return draft.interests != nil;default:return true}}
    private func clearCurrent(){switch step{case 0:draft.queueTolerance=nil;case 1:draft.dailySteps=nil;case 2:draft.energy=nil;case 3:draft.rhythm=nil;case 4:draft.interests=nil;default:break}}
    private func summary(_ title:String,_ value:String?)->some View{HStack(alignment:.top){Text(uiText(title)).foregroundStyle(Palette.muted);Spacer();Text(uiText(value ?? "暂不确定")).multilineTextAlignment(.trailing)}}
    private func choice(_ title:String,selected:Bool,action:@escaping()->Void)->some View{Button(action:action){HStack{Text(uiText(title)).font(.subheadline.bold());Spacer();Image(systemName:selected ? "checkmark.circle.fill":"circle")}.padding(18).background(selected ? Palette.mint:.white,in:RoundedRectangle(cornerRadius:18)).foregroundStyle(Palette.ink)}.buttonStyle(.plain)}
    private func singleOptions(_ options:[String],value:String?,set:@escaping(String)->Void)->some View{ForEach(options,id:\.self){option in choice(option,selected:value==option){set(option);response=preferenceReply(option)}}}
    private func preferenceReply(_ option:String)->String {
        switch option {
        case "尽量不排队":return "收到，小爪爪记下啦！我会参考这个偏好，排队情况仍要出发前确认哦。"
        case "低精力":return "小电池也能玩得开心，我会给你多留一点喘口气的空间～"
        case "高精力":return "探索能量满格！不过每天的步数上限我也会记在心里。"
        case "轻松随意":return "好呀，留白也是旅行的一部分，我们慢慢来～"
        case "紧凑充实":return "收到，想看的都先收进背包，再检查时间够不够！"
        default:return "\(option)，收到啦！这会作为以后的旅行参考，随时都能改。"
        }
    }
    private func load() async {
        draft=editing ? store.personalProfile:UserTravelProfile()
        await chat.refreshMemories()
        // The questionnaire remains usable offline; save reports connection errors without losing answers.
        existing=editing ? chat.memories.first{$0.id==UserDefaults.standard.string(forKey:"personalProfile.memoryID")}:nil
        loaded=true
    }
    private func save() async {
        await chat.saveTravelProfile(draft,existing:existing);guard chat.error==nil else{return}
        store.personalProfile=draft;store.memories=chat.memories
        if let memory=chat.memories.filter({$0.profile.habits != nil}).sorted(by:{($0.provenance?.createdAt ?? "") > ($1.provenance?.createdAt ?? "")}).first{UserDefaults.standard.set(memory.id,forKey:"personalProfile.memoryID")}
        store.koalaFeedback=KoalaCue(event:.dnaCompleted)
        if editing{dismiss()}else{store.completePersonalPreferences()}
    }
}

 enum TripAdjustmentRequest {
    static func make(messages:[PetMessage],fallback:String)->String {
        let acknowledgements=Set(["好","好的","可以","谢谢","谢谢你","ok","okay","yes"])
        let latest=messages.last{message in message.role=="user" && !acknowledgements.contains(message.text.trimmingCharacters(in:.whitespacesAndNewlines).lowercased())}?.text ?? fallback
        // The backend bounds requests by UTF-16 length; keep the latest intent ahead of old summaries.
        return String(decoding:Array(latest.utf16.prefix(1400)),as:UTF16.self)
    }
}

struct TripPlanningShortcut {let stage:Int;let title:String}
enum TripPlanningShortcuts {
    static func items(_ fields:[String:String],ready:Bool)->[TripPlanningShortcut] {
        if ready{return [.init(stage:1,title:"改日期／天数"),.init(stage:3,title:"改预算"),.init(stage:2,title:"改同行人")]}
        let groups:[(Int,String,[String])]=[(0,"选目的地",["destination"]),(1,"定日期／天数",["date","days"]),(2,"同行人数",["travelers"]),(3,"活动预算",["budget","budgetCurrency","budgetBasis"])]
        return Array(groups.filter{$0.2.contains{!TravelRequirements.known(fields[$0])}}.prefix(2)).map{.init(stage:$0.0,title:$0.1)}
    }
}
enum TripConversationSeed {
    static func profile(_ fields:[String:String],base:TripProfile) throws -> TripProfile {
        if fields["adjustmentNeed"]=="endTime"{throw NSError(domain:"TripConversation",code:1,userInfo:[NSLocalizedDescriptionKey:"先告诉 Koko 这一天最晚几点结束，或选择‘先不改时间’。"])}
        var result=try TravelRequirements.profile(fields,personal:UserTravelProfile())
        result.start=base.start;result.end=base.end
        result.startMinute=base.startMinute;result.endMinute=base.endMinute
        result.mustVisit=base.mustVisit;result.weatherOverride=base.weatherOverride
        // Changing an origin requires coordinate resolution; the saved-trip editor owns that flow.
        guard !TravelRequirements.known(fields["origin"]) || fields["origin"]==base.origin || fields["origin"]==base.start.name else {
            throw NSError(domain:"TripConversation",code:1,userInfo:[NSLocalizedDescriptionKey:"更换出发地点请打开‘完整时间与住宿设置’，选定地图位置后再重算。其他修改已保留。"])
        }
        if fields["dayStyle"]=="halfDay"{result.endMinute=min(result.endMinute,900)}
        if let raw=fields["adjustmentEndMinute"],let minute=Int(raw) {
            guard minute>result.startMinute,minute<1440 else{throw NSError(domain:"TripConversation",code:1,userInfo:[NSLocalizedDescriptionKey:"结束时间需要晚于当天出发时间。请告诉 Koko 一个更晚的结束时间。"])}
            result.endMinute=minute
        }
        return result
    }

    static func fields(_ p:TripProfile)->[String:String] {
        var fields=["destination":"悉尼","date":p.date,"days":String(p.days),"travelers":String(p.travelers ?? 1),"budget":String(p.budget),"budgetCurrency":"AUD","budgetBasis":"perPersonPerDay","budgetCoverage":"activities","interests":p.interests.joined(separator:"、"),"pace":"约\(p.maxSteps)步","transportPreference":p.transportPreference ?? "balanced","origin":p.origin ?? p.start.name,"mustDo":p.mustDo ?? "无","avoid":p.avoid ?? "无","dayStyle":p.dayStyle ?? "fullDay"]
        if let energy=p.energy{fields["energy"]=energy};if let rhythm=p.rhythm{fields["rhythm"]=rhythm};if let queue=p.queueTolerance{fields["queueTolerance"]=queue}
        return fields
    }
}

private struct PlanningBasicsRows:View {
    var fields:[String:String]
    var language:AppLanguage
    var busy:Bool
    var edit:(Int)->Void
    private let blue=Color(red:0.20,green:0.36,blue:0.79)
    private func value(_ key:String)->String? {TravelRequirements.known(fields[key]) ? fields[key]:nil}
    private var people:String {guard let count=value("travelers") else{return uiText("Choose")};return count=="1" ? uiText("Just me"):language.text("{0} people",count)}
    private var budget:String {guard let amount=value("budget"),let currency=value("budgetCurrency"),let basis=value("budgetBasis") else{return uiText("Choose")};return amount+" "+currency+" · "+uiText(TravelRequirements.optionTitle(basis,field:"budgetBasis"))}
    private var dates:String {guard let date=value("date"),let days=value("days") else{return uiText("Dates undecided")};return date+" · "+language.text("{0} days",days)}
    var body:some View {
        VStack(spacing:0){
            row("Traveling with",people,"person.fill",2);Divider().padding(.leading,42)
            row("Activity budget",budget,"creditcard",3);Divider().padding(.leading,42)
            row("Dates",dates,"calendar",1);Divider().padding(.leading,42)
            row("Destination",value("destination").map{uiText($0)} ?? uiText("Choose"),"mappin.and.ellipse",0)
        }.padding(.horizontal,12).background(Color.gray.opacity(0.025),in:RoundedRectangle(cornerRadius:16)).overlay(RoundedRectangle(cornerRadius:16).stroke(Color.gray.opacity(0.14))).disabled(busy)
    }
    private func row(_ label:String,_ detail:String,_ symbol:String,_ stage:Int)->some View {
        Button{edit(stage)}label:{HStack(spacing:10){Image(systemName:symbol).foregroundStyle(blue).frame(width:24);Text(uiText(label)).font(.subheadline.weight(.semibold));Spacer(minLength:6);Text(detail).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.trailing);Image(systemName:"chevron.right").font(.caption2).foregroundStyle(.secondary)}.frame(minHeight:54).padding(.vertical,3).contentShape(Rectangle())}.buttonStyle(.plain).accessibilityIdentifier("studio-basics-"+String(stage))
    }
}
