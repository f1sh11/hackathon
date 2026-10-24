import SwiftUI

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

struct KokoCurrentSpeech:View {
    let text:String
    var busy=false
    var compact=false
    var body:some View {
        VStack(spacing:10){
            Text(text).font(.system(compact ? .body:.title3,design:.rounded,weight:.semibold)).multilineTextAlignment(.center).lineSpacing(5).fixedSize(horizontal:false,vertical:true)
            if busy{ProgressView().tint(Palette.coral)}
        }.padding(compact ? 14:20).frame(maxWidth:.infinity).background(.white,in:RoundedRectangle(cornerRadius:24))
            .overlay(alignment:.bottom){RoundedRectangle(cornerRadius:3).fill(.white).frame(width:17,height:17).rotationEffect(.degrees(45)).offset(y:7)}
            .accessibilityIdentifier("koko-current-utterance")
    }
}
