import SwiftUI

struct PetQuickReply:Codable,Identifiable,Equatable {
    var id:String;var text:String;var intent:String
}
struct PetChoiceEvidence:Codable,Equatable {
    var questionMessageId:String;var questionText:String;var optionId:String;var optionText:String
}
struct ProfilingDialogueState:Codable,Equatable {
    var version:Int;var responseCount:Int;var phase:String;var nickname:String
    var ideas:[String];var inspiration:TravelInspiration?
}
struct TravelInspiration:Codable,Equatable {
    var title:String;var ideas:[String];var status:String;var placesVerified:Bool
}

struct KokoQuickReplies:View {
    let replies:[PetQuickReply]
    let busy:Bool
    let choose:(PetQuickReply)->Void
    var body:some View {
        HStack(spacing:8) {
            ForEach(replies){reply in
                Button{choose(reply)}label:{Text(reply.text).font(.subheadline.weight(.medium)).multilineTextAlignment(.center).frame(maxWidth:.infinity,minHeight:44).padding(.horizontal,8).padding(.vertical,5).background(.white,in:RoundedRectangle(cornerRadius:15)).overlay(RoundedRectangle(cornerRadius:15).stroke(Palette.coral.opacity(0.25)))}
                    .buttonStyle(.plain).disabled(busy).accessibilityIdentifier("koko-reply-"+reply.intent)
            }
        }.padding(.horizontal,20).padding(.top,8).accessibilityIdentifier("koko-contextual-replies")
    }
}

struct KokoInspirationCard:View {
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
        VStack(alignment:.leading,spacing:13) {
            Label("咱们的旅行灵感",systemImage:"sparkles").font(.headline).foregroundStyle(Palette.coral)
            Text(inspiration.title).font(.subheadline.bold())
            ForEach(Array(inspiration.ideas.enumerated()),id:\.offset){index,idea in
                HStack(alignment:.top,spacing:8){Text("\(index+1)").font(.caption.bold()).foregroundStyle(Palette.coral);Text(idea).font(.subheadline)}
            }
            Divider()
            Text("已经了解的线索").font(.subheadline.bold())
            if facts.isEmpty{Text("还没有明确的偏好；可以先看看灵感，之后再调整。").font(.caption).foregroundStyle(Palette.muted)}
            ForEach(facts){fact in
                VStack(alignment:.leading,spacing:3){Text(fact.summary).font(.caption);Text(fact.status=="confirmed" ? "已确认的长期偏好":fact.source=="contextual_choice" ? "这次提议中的选择":"当前对话的线索").font(.caption2).foregroundStyle(Palette.muted)}
            }
            if !unknownDimensions.isEmpty{Text("仍待了解："+unknownDimensions+"。相关上限、条件和例外也按实际表达保留。").font(.caption).foregroundStyle(Palette.muted)}
            Text(TripDraftSummary(fields:session.fields).subtitle).font(.caption).foregroundStyle(Palette.muted)
            Text("具体场所、日期、费用和交通尚需核实；这份灵感可以随时改，出行资料可以之后一起补。").font(.caption).foregroundStyle(Palette.muted)
        }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(Palette.blush.opacity(0.7),in:RoundedRectangle(cornerRadius:20)).accessibilityIdentifier("koko-inspiration")
    }
}
