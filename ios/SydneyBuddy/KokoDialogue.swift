import SwiftUI

struct PetQuickReply:Codable,Identifiable,Equatable {
    var id:String;var text:String;var intent:String
}
struct PetChoiceEvidence:Codable,Equatable {
    var questionMessageId:String;var questionText:String;var optionId:String;var optionText:String
}
struct ProfilingDialogueState:Codable,Equatable {
    var companionVersion:Int? = nil
    var version:Int;var responseCount:Int;var phase:String;var nickname:String
    var ideas:[String];var inspiration:TravelInspiration?
}
struct TravelInspiration:Codable,Equatable {
    var title:String;var ideas:[String];var status:String;var placesVerified:Bool
}

struct KokoQuickReplies:View {
    @Environment(\.locale) private var interfaceLocale

    let replies:[PetQuickReply]
    let busy:Bool
    var stacked=false
    var compact=false
    let choose:(PetQuickReply)->Void
    @State private var selectedID:String?
    var body:some View {
        let _=interfaceLocale
        LazyVGrid(columns:Array(repeating:GridItem(.flexible(),spacing:8),count:replies.count>=2 ? 2:1),spacing:8) {
            ForEach(replies){reply in
                Button{guard selectedID==nil else{return}; selectedID=reply.id; UISelectionFeedbackGenerator().selectionChanged(); choose(reply)}label:{Text(uiText(reply.text)).font(.subheadline.weight(.semibold)).multilineTextAlignment(.center).frame(maxWidth:.infinity,minHeight:44).padding(.horizontal,8).padding(.vertical,5)}
                    .buttonStyle(KokoOptionButtonStyle(prominent:selectedID==reply.id,compact:compact)).disabled(busy || selectedID != nil).accessibilityIdentifier("koko-reply-"+reply.intent)
            }
        }.padding(.top,8).accessibilityIdentifier("koko-contextual-replies")
        .onChange(of:replies){_,_ in selectedID=nil}
        .onChange(of:busy){_,value in if !value{selectedID=nil}}
        .task(id:selectedID){guard selectedID != nil else{return}; do{try await Task.sleep(for:.milliseconds(500));selectedID=nil}catch{}}
    }
}

struct KokoInspirationCard:View {
    @Environment(\.locale) private var interfaceLocale

    let session:PetSession
    let inspiration:TravelInspiration
    private var facts:[TravelPreference] {
        let explicit=(session.travelDna ?? [])+(session.tripPreferences ?? [])
        var seen=Set<String>()
        return explicit.filter{$0.source != "ai_inference" && seen.insert($0.key+"|"+$0.condition+"|"+$0.value).inserted}
    }
    private var unknownDimensions:String {
        let dimensions=[("sleep","作息"),("fitness","体力"),("pace","节奏"),("queue","排队"),("crowds","人群"),("interests","兴趣"),("spending","消费"),("weather","天气"),("food","饮食"),("transport","交通"),("style","旅行风格"),("habits","特殊习惯")]
        let known=Set(facts.map(\.dimension))
        return dimensions.filter{!known.contains($0.0)}.map{$0.1}.joined(separator:"、")
    }
    var body:some View {
        let _=interfaceLocale
        DisclosureGroup(uiText("查看旅行灵感")){VStack(alignment:.leading,spacing:13) {
            Label(uiText("咱们的旅行灵感"),systemImage:"sparkles").font(.headline).foregroundStyle(Palette.green)
            Text(uiText((TravelRequirements.known(session.effectiveFields["destination"]) ? session.effectiveFields["destination"]!:"目的地待定")+" · "+(inspiration.title.components(separatedBy:" · ").last ?? "旅行灵感"))).font(.subheadline.bold())
            ForEach(Array(inspiration.ideas.enumerated()),id:\.offset){index,idea in
                HStack(alignment:.top,spacing:8){Text(uiText("\(index+1)")).font(.caption.bold()).foregroundStyle(Palette.green);Text(uiText(idea)).font(.subheadline)}
            }
            Divider()
            Text(uiText("已经了解的线索")).font(.subheadline.bold())
            if let interests=session.effectiveFields["interests"],TravelRequirements.known(interests){Text(uiText("本次兴趣："+interests)).font(.caption)}
            if facts.isEmpty && !TravelRequirements.known(session.effectiveFields["interests"]){Text(uiText("还没有明确的偏好；可以先看看灵感，之后再调整。")).font(.caption).foregroundStyle(Palette.muted)}
            ForEach(facts){fact in
                VStack(alignment:.leading,spacing:3){Text(uiText(fact.summary)).font(.caption);Text(uiText(fact.status=="confirmed" ? "已确认的长期偏好":fact.source=="contextual_choice" ? "这次提议中的选择":"当前对话的线索")).font(.caption2).foregroundStyle(Palette.muted)}
            }
            if !unknownDimensions.isEmpty{DisclosureGroup(uiText("其他习惯以后慢慢了解")){Text(uiText("仍未知："+unknownDimensions+"。暂不确定的内容不会替你填写。")).font(.caption).foregroundStyle(Palette.muted)}.font(.caption).foregroundStyle(Palette.muted)}
            Text(uiText(TripDraftSummary(fields:session.fields).subtitle)).font(.caption).foregroundStyle(Palette.muted)
            Text(uiText("具体场所、日期、费用和交通尚需核实；这份灵感可以随时改，出行资料可以之后一起补。")).font(.caption).foregroundStyle(Palette.muted)
        }}.font(.subheadline).padding(14).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint.opacity(0.7),in:RoundedRectangle(cornerRadius:20)).accessibilityIdentifier("koko-inspiration")
    }
}



// UI projection intentionally exposes one current assistant utterance, never a transcript.
enum KokoConversationPresentation {
    static let preferenceFields:Set<String>=["interests","pace","rhythm","energy","queueTolerance","notes"]
    static func tripSeed(from session:PetSession?)->[String:String] {
        guard let session else{return [:]}
        var result=session.fields.filter{preferenceFields.contains($0.key) && TravelRequirements.known($0.value)}
        var notes=[result["notes"] ?? ""].filter{!$0.isEmpty}
        for fact in (session.pendingPreferences ?? [])+(session.tripPreferences ?? []) where fact.source != "ai_inference" {
            let target=["daily_steps":"pace","rhythm":"rhythm","interests":"interests","queue_tolerance_minutes":"queueTolerance"][fact.key]
            if let target,fact.condition.isEmpty,result[target]==nil{result[target]=fact.key=="daily_steps" ? "约"+fact.value+"步":fact.value}
            else{notes.append(fact.summary)}
        }
        if !notes.isEmpty{result["notes"]=Array(Set(notes)).sorted().joined(separator:"；")}
        return result
    }
    static func utterance(session:PetSession?,streaming:String,busy:Bool,personalSetup:Bool)->String {
        if !streaming.isEmpty{return streaming}
        if busy{return "让我想一想，咱们怎么接着安排。"}
        return session?.replyQuestion?.text ?? (personalSetup ? "嗨，我叫 Koko。咱们先认识一下？":"我在这里，咱们一起准备这趟旅行。")
    }
}
