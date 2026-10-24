import SwiftUI
import UIKit

enum KoalaAction:String,Codable,CaseIterable {
    case idle,wave,clap,map,weather,budget,tired,celebrate,departure
    case thumbsUp="thumbs-up"
    static func forMood(_ mood:PetMood)->Self {.idle}
}
/// Work/risk events reflect real results; conversation reactions are explicitly separate playful gestures.
enum KoalaEvent:String,CaseIterable {
    case welcome="app.welcome",chatReady="chat.ready",dnaCompleted="profile.confirmed",preferenceConfirmed="user.confirmed"
    case planningStarted="plan.started",optimizationStarted="optimization.started",simulationStarted="simulation.started"
    case weatherRisk="risk.weather",budgetRisk="risk.budget",fatigueRisk="risk.energy",tripSaved="trip.saved"
    case choiceAccepted="conversation.choice",rainConversation="conversation.rain",playfulTired="conversation.long-walk"
    case informationSubmitted="information.submitted",stepCompleted="step.completed",optimizationProgress="optimization.progress",journeyStarted="journey.started"
    var action:KoalaAction {
        switch self {
        case .welcome:return .wave
        case .chatReady:return .idle
        case .informationSubmitted:return .clap
        case .preferenceConfirmed,.choiceAccepted,.stepCompleted,.optimizationProgress:return .thumbsUp
        case .planningStarted,.optimizationStarted:return .map
        case .simulationStarted,.journeyStarted:return .departure
        case .weatherRisk,.rainConversation:return .weather
        case .budgetRisk:return .budget
        case .fatigueRisk,.playfulTired:return .tired
        case .dnaCompleted,.tripSaved:return .celebrate
        }
    }
    var returnsToIdle:Bool {self == .choiceAccepted || self == .rainConversation || self == .playfulTired}
}
/// Conservative presentation hints from the current utterance/choice, never profile facts or risk analysis.
enum KoalaConversationFeedback {
    static func event(text:String,scene:String="")->KoalaEvent? {
        let normalized=text.folding(options:[.widthInsensitive],locale:nil).replacingOccurrences(of:",",with:"").replacingOccurrences(of:"，",with:"").replacingOccurrences(of:" ",with:"")
            .replacingOccurrences(of:"一万五千",with:"15000").replacingOccurrences(of:"一万五",with:"15000")
        let walkingScene=scene.contains("步") || scene.contains("步行") || scene.contains("走多少")
        let walkingExpression=try? NSRegularExpression(pattern:"([0-9]+)步")
        let quantities=walkingExpression?.matches(in:normalized,range:NSRange(normalized.startIndex...,in:normalized)).compactMap{match in
            Range(match.range(at:1),in:normalized).map{String(normalized[$0])}
        } ?? []
        let longWalk=quantities.last == "15000" || walkingScene && normalized == "15000"
        let negatedWalk=normalized.range(of:"(?:不是|不走|不想走|不打算走|不愿走|不要超过|不要|不超过|不到|少于|不满|别走|取消|不选)15000",options:.regularExpression) != nil
        if longWalk && !negatedWalk{return .playfulTired}
        let rainScene=scene.contains("雨") || scene.contains("伞") || normalized.contains("雨") || normalized.contains("伞")
        let declines=["不要","不想","不去","不行","不愿","不可以","不喜欢","不用","不需要","先不","不打算","换","改","等","躲","避","室内","晴天","看天气","再定","再决定"]
        let accepts=["带伞","带上伞","撑伞","没关系","不怕","可以","愿意","都行","照样","照常","也能","喜欢","走吧","去吧"]
        if rainScene && !declines.contains(where:normalized.contains) && accepts.contains(where:normalized.contains){return .rainConversation}
        return nil
    }
}
struct KoalaCue:Equatable {
    var action:KoalaAction
    var id:String=UUID().uuidString
    var event:KoalaEvent? = nil
    var returnsToIdle=false
    init(action:KoalaAction,id:String=UUID().uuidString,returnsToIdle:Bool=false){self.action=action;self.id=id;self.returnsToIdle=returnsToIdle}
    init(event:KoalaEvent,id:String=UUID().uuidString){self.action=event.action;self.id=id;self.event=event;self.returnsToIdle=event.returnsToIdle}
}
struct KoalaClip:Decodable {
    var id:String;var fps:Int;var frames:Int;var playback:String;var ending:String;var recovery:String
    var framePath:String;var poster:String
    var recoveryId:String? = nil
    func path(_ frame:Int)->String {framePath.replacingOccurrences(of:"{frame:03}",with:String(format:"%03d",frame))}
}
struct KoalaRecoveryClip:Decodable {
    var id:String;var sourceAction:String;var fps:Int;var frames:Int;var framePath:String;var quality:String
    func path(_ frame:Int)->String {framePath.replacingOccurrences(of:"{frame:03}",with:String(format:"%03d",frame))}
}
struct KoalaManifest:Decodable {
    var animations:[KoalaClip]
    var recoveries:[KoalaRecoveryClip]? = nil
    static func load(bundle:Bundle = .main)->Self? {
        guard let url=bundle.url(forResource:"manifest",withExtension:"json",subdirectory:"koala"),let data=try? Data(contentsOf:url) else{return nil}
        return try? JSONDecoder().decode(Self.self,from:data)
    }
}

/// Finish gestures and supplied return clips before starting a queued action.
/// Unavailable physical recoveries still HOLD; no reset or dissolve stands a lying koala up.
struct KoalaPlayback {
    let clips:[String:KoalaClip]
    let recoveries:[String:KoalaRecoveryClip]
    private(set) var action:KoalaAction = .idle
    private(set) var frame=1
    private(set) var reversing=false
    private(set) var recovering=false
    private var pending:KoalaAction?
    private var pendingReturnToIdle=false
    private var releaseQueuedWork=false
    private var returnAfterPlayback=false
    private var lastToken=""
    init(_ manifest:KoalaManifest){clips=Dictionary(uniqueKeysWithValues:manifest.animations.map{($0.id,$0)});recoveries=Dictionary(uniqueKeysWithValues:(manifest.recoveries ?? []).map{($0.id,$0)})}
    var clip:KoalaClip? {clips[action.rawValue]}
    var recoveryClip:KoalaRecoveryClip? {
        guard let id=clip?.recoveryId,let recovery=recoveries[id],recovery.sourceAction==action.rawValue,recovery.quality=="source_frame_reversal" else{return nil}
        return recovery
    }
    var framePath:String? {recovering ? recoveryClip?.path(frame):clip?.path(frame)}
    var fps:Int {recovering ? recoveryClip?.fps ?? 12:clip?.fps ?? 12}
    var isSettledAtIdle:Bool {action == .idle && (pending == nil || pending == .idle)}
    mutating func request(_ action:KoalaAction,token:String,returnToIdle:Bool=false) {
        guard token != lastToken,clips[action.rawValue] != nil else{return}
        lastToken=token
        if action == .idle,self.action == .idle,let queued=pending,let queuedClip=clips[queued.rawValue],queuedClip.ending=="hold",
           queuedClip.recovery=="reverse" || queuedClip.recoveryId.flatMap({recoveries[$0]})?.quality=="source_frame_reversal" {
            releaseQueuedWork=true;return
        }
        if action == .idle,let pending,pending != .idle,pending != .map{return}
        if action==self.action && !recovering && !reversing {
            // A newer real risk must revoke a previous playful auto-return, even for the same action.
            returnAfterPlayback=returnToIdle;pending=nil;pendingReturnToIdle=false;releaseQueuedWork=false;return
        }
        pending=action;pendingReturnToIdle=returnToIdle;releaseQueuedWork=false
    }
    private mutating func finishAtIdle(){action = .idle;frame=1;reversing=false;recovering=false;returnAfterPlayback=false;if pending == .idle{pending=nil}}
    mutating func advance() {
        guard let clip else{return}
        if action == .idle,let next=pending {pending=releaseQueuedWork ? .idle:nil;releaseQueuedWork=false;action=next;frame=1;reversing=false;recovering=false;returnAfterPlayback=pendingReturnToIdle;return}
        if recovering {
            if let recovery=recoveryClip,frame<recovery.frames{frame+=1}else{finishAtIdle()}
            return
        }
        let nextRequested=pending != nil && pending != action
        // Early exit from an opening map/umbrella uses the frames already reached, without jumping to its terminal.
        if clip.recovery=="reverse",nextRequested,frame<clip.frames{reversing=true}
        if reversing {
            if frame>1{frame-=1}else{finishAtIdle()}
        } else if frame<clip.frames {frame+=1}
        else if clip.playback=="loop" {frame=1}
        else if clip.ending=="idle" {finishAtIdle()}
        else if nextRequested || returnAfterPlayback {
            if recoveryClip != nil{recovering=true;frame=1}
            else if clip.recovery=="reverse"{reversing=true;if frame>1{frame-=1}}
        }
        // A non-reversible held endpoint intentionally keeps its support and props.
    }
}

@MainActor final class KoalaPlayer:ObservableObject {
    @Published private(set) var image:UIImage?
    var onIdle:(()->Void)?
    private var playback:KoalaPlayback?
    private static let cache:NSCache<NSString,UIImage> = {let cache=NSCache<NSString,UIImage>();cache.countLimit=90;return cache}()
    init(manifest:KoalaManifest?=KoalaManifest.load()){if let manifest{playback=KoalaPlayback(manifest)};render()}
    func request(_ action:KoalaAction,token:String,returnToIdle:Bool=false){playback?.request(action,token:token,returnToIdle:returnToIdle)}
    private func render() {
        guard let playback,let path=playback.framePath,let url=Bundle.main.resourceURL?.appendingPathComponent("koala/"+path) else{return}
        let key=url.path as NSString
        if let cached=Self.cache.object(forKey:key){image=cached;return}
        if let value=UIImage(contentsOfFile:url.path){Self.cache.setObject(value,forKey:key);image=value}
    }
    func advance() {
        let previous=playback?.action
        playback?.advance();render()
        // A return can pass through Idle before an already queued gesture. Keep the view alive
        // until that gesture also finishes, otherwise an incoming acknowledgement is never seen.
        if let previous,previous != .idle,playback?.isSettledAtIdle == true{onIdle?()}
    }
    func run() async {
        do {
            while !Task.isCancelled {
                advance()
                try await Task.sleep(nanoseconds:UInt64(1_000_000_000/max(1,playback?.fps ?? 12)))
            }
        }catch{}
    }
}

struct PetAvatar:View {
    var mood:PetMood = .greeting
    var cue:KoalaCue? = nil
    var animated=true
    var size:CGFloat=108
    var onIdle:(()->Void)? = nil
    @StateObject private var player=KoalaPlayer()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase
    var body:some View {
        Group {
            if let image=player.image{Image(uiImage:image).resizable().interpolation(.high).scaledToFit()}
            else {Image(systemName:"leaf.circle.fill").resizable().scaledToFit().foregroundStyle(Palette.green)}
        }.frame(width:size,height:size*116/108)
        .accessibilityLabel("旅行伙伴 Koko")
        .onAppear{player.onIdle=onIdle;player.request(cue?.action ?? KoalaAction.forMood(mood),token:cue?.id ?? "mood:"+mood.rawValue,returnToIdle:cue?.returnsToIdle ?? false)}
        .onChange(of:mood){_,value in player.request(KoalaAction.forMood(value),token:"mood:"+value.rawValue)}
        .onChange(of:cue){_,value in
            player.onIdle=onIdle
            if let value{player.request(value.action,token:value.id,returnToIdle:value.returnsToIdle)}
            else{player.request(.idle,token:"cue-cleared:"+UUID().uuidString)}
        }
        .task(id:animated && !reduceMotion && scenePhase == .active){
            if animated && !reduceMotion && scenePhase == .active{await player.run()}
        }
    }
}

struct KoalaFeedbackBanner:View {
    let cue:KoalaCue
    let dismiss:()->Void
    @State private var closing=false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private var visibleCue:KoalaCue {closing ? KoalaCue(event:.chatReady,id:"dismiss:"+cue.id):cue}
    private var held:Bool {!cue.returnsToIdle && [.map,.weather,.budget,.tired,.departure].contains(cue.action)}
    private var title:String {
        if cue.event == .playfulTired{return "多走也记得歇一歇"}
        if cue.event == .rainConversation{return "带上伞，按天气调整"}
        if cue.event == .choiceAccepted{return "听到你的选择啦"}
        if cue.event == .dnaCompleted{return "Travel DNA 已完成"}
        if cue.event == .journeyStarted{return "咱们出发吧"}
        switch cue.action {
        case .clap:return "听到了，咱们继续"
        case .thumbsUp:return "已确认你的选择"
        case .celebrate:return "行程已保存"
        case .budget:return "预计费用超出预算"
        case .weather:return "有天气风险"
        case .tired:return "模拟发现高疲劳风险"
        case .departure:return "旅行模拟已开始"
        default:return "Koko 陪着你"
        }
    }
    private func close() {
        guard !closing else{return}
        // Departure has no physical return. End this scene instead of pretending the suitcase vanished.
        if reduceMotion || cue.action == .departure || cue.action == .idle{dismiss()}
        else{closing=true}
    }
    var body:some View {
        HStack(spacing:8){PetAvatar(cue:visibleCue,onIdle:{if closing || !held{dismiss()}});VStack(alignment:.leading,spacing:8){Text(title).font(.subheadline.bold());if held{Button(closing ? "咱们继续":"知道了",action:close).font(.caption).disabled(closing)}}}
            .padding(.horizontal,18).background(Palette.cream,in:RoundedRectangle(cornerRadius:24))
            .onChange(of:cue.id){_,_ in closing=false}
            .task(id:cue.id){if reduceMotion && !held{do{try await Task.sleep(nanoseconds:1_700_000_000);dismiss()}catch{}}}
    }
}
