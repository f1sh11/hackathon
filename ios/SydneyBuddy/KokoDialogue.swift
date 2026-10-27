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
    var proposedDirection:String?=nil
    var selectedDirection:String?=nil
    var dateDeferred:Bool?=nil
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
    var quiet=false
    let choose:(PetQuickReply)->Void
    @State private var selectedID:String?
    var body:some View {
        let _=interfaceLocale
        LazyVGrid(columns:Array(repeating:GridItem(.flexible(),spacing:8),count:stacked ? 1:(replies.count>=2 ? 2:1)),spacing:8) {
            ForEach(replies){reply in
                Button{guard selectedID==nil else{return}; selectedID=reply.id; UISelectionFeedbackGenerator().selectionChanged(); choose(reply)}label:{Text(uiText(reply.text)).font(.subheadline.weight(.semibold)).multilineTextAlignment(.center).frame(maxWidth:.infinity,minHeight:44).padding(.horizontal,8).padding(.vertical,5)}
                    .buttonStyle(KokoOptionButtonStyle(prominent:selectedID==reply.id,compact:compact,quiet:quiet)).disabled(busy || selectedID != nil).accessibilityIdentifier("koko-reply-"+reply.intent)
            }
        }.padding(.top,8).accessibilityIdentifier("koko-contextual-replies")
        .onChange(of:replies){_,_ in selectedID=nil}
        .onChange(of:busy){_,value in if !value{selectedID=nil}}
        .task(id:selectedID){guard selectedID != nil else{return}; do{try await Task.sleep(for:.milliseconds(500));selectedID=nil}catch{}}
    }
}

enum KokoInspirationPresentation {
    static func direction(_ session:PetSession)->String? {
        guard let key=session.dialogue?.proposedDirection,["nature","culture","food"].contains(key) else{return nil}
        return key
    }
    static func shouldShow(_ session:PetSession)->Bool {
        guard session.dialogue?.inspiration != nil,direction(session) != nil else{return false}
        if session.contextualReplies.contains(where:{$0.intent=="accept_direction"}){return true}
        let latest=session.messages.last(where:{$0.role=="user"})?.text.lowercased() ?? ""
        let asks=["没想好","给我点建议","灵感","换个想法","换个思路","ideas","inspiration","suggest something","not sure what","surprise me"]
        return asks.contains(where:latest.contains) && session.dialogue?.selectedDirection != session.dialogue?.proposedDirection
    }
    static func interests(_ direction:String)->String {
        switch direction{case "culture":return "文化、艺术";case "food":return "美食、城市";default:return "自然、风景"}
    }
    static func title(_ direction:String)->String {
        switch direction{case "culture":return "A little culture, a little wandering.";case "food":return "Good food, without the rush.";default:return "A little greenery, a slower day."}
    }
    static func description(_ direction:String)->String {
        switch direction{case "culture":return "Local stories, interesting streets, and time to pause.";case "food":return "A food-filled neighborhood, small shops, and a well-earned break.";default:return "A waterside wander, a café break, and room to explore."}
    }
}
struct KokoInspirationCard:View {
    let direction:String
    var busy=false
    var accepted=false
    var accept:()->Void
    var another:()->Void
    var close:()->Void
    private let blue=Color(red:0.20,green:0.36,blue:0.79)
    var body:some View {
        VStack(alignment:.leading,spacing:12){
            HStack{Label(uiText("Koko’s little idea"),systemImage:"sparkles").font(.caption.weight(.semibold)).foregroundStyle(blue);Spacer();Button(action:close){Image(systemName:"xmark").font(.caption).frame(width:44,height:44)}.buttonStyle(.plain).accessibilityLabel(uiText("Back to planning"))}
            Text(uiText(KokoInspirationPresentation.title(direction))).font(.system(.headline,design:.rounded,weight:.bold))
            Text(uiText(KokoInspirationPresentation.description(direction))).font(.subheadline).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
            ViewThatFits(in:.horizontal){
                HStack(spacing:12){acceptButton;anotherButton}
                VStack(spacing:8){acceptButton;anotherButton}
            }
            Text(uiText("An idea for now. Places and routes come next.")).font(.caption2).foregroundStyle(.secondary)
        }.padding(16).frame(maxWidth:.infinity,alignment:.leading).background(blue.opacity(0.035),in:RoundedRectangle(cornerRadius:16)).overlay(RoundedRectangle(cornerRadius:16).stroke(blue.opacity(0.13))).disabled(busy).accessibilityIdentifier("koko-inspiration")
    }
    private var acceptButton:some View {
        Button(action:accept){Text(uiText(busy ? "Saving…":accepted ? "Added to this trip":"Sounds like me")).font(.subheadline.weight(.semibold)).frame(maxWidth:.infinity,minHeight:46)}.buttonStyle(TripsCreateButtonStyle()).disabled(accepted).accessibilityIdentifier("inspiration-accept")
    }
    private var anotherButton:some View {
        Button(action:another){Text(uiText("Try another idea")).font(.subheadline.weight(.semibold)).foregroundStyle(blue).frame(maxWidth:.infinity,minHeight:46)}.buttonStyle(BuddyPressStyle()).accessibilityIdentifier("inspiration-another")
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
