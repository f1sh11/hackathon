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
        case .simulationStarted:return .map
        case .journeyStarted:return .departure
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
        let english=text.folding(options:[.widthInsensitive,.caseInsensitive],locale:Locale(identifier:"en_US_POSIX"))
            .replacingOccurrences(of:"’",with:"'").replacingOccurrences(of:",",with:"").replacingOccurrences(of:"，",with:"")
            .replacingOccurrences(of:"一万五千",with:"15000").replacingOccurrences(of:"一万五",with:"15000")
        let englishScene=scene.lowercased()
        let normalized=english.replacingOccurrences(of:" ",with:"")
        func matches(_ pattern:String,_ value:String)->Bool {value.range(of:pattern,options:.regularExpression) != nil}
        let walkingScene=scene.contains("步") || scene.contains("步行") || scene.contains("走多少") || matches(#"\b(?:steps?|walk(?:ing)?)\b"#,englishScene)
        let walkingExpression=try? NSRegularExpression(pattern:#"([0-9]+)\s*步|\b([0-9]+)\s*steps?\b"#,options:[.caseInsensitive])
        let quantities=walkingExpression?.matches(in:english,range:NSRange(english.startIndex...,in:english)).compactMap{match -> String? in
            for index in 1..<match.numberOfRanges {if let range=Range(match.range(at:index),in:english){return String(english[range])}}
            return nil
        } ?? []
        let longWalk=quantities.last == "15000" || walkingScene && normalized == "15000"
        let negatedWalk=matches("(?:不是|不走|不想走|不打算走|不愿走|不要超过|不要|不超过|不到|少于|不满|别走|取消|不选)15000",normalized)
            || matches(#"\b(?:not|no|don't|do not|won't|will not|can't|cannot|never|under|below|less than|fewer than|at most|maximum|max)\b.{0,32}\b15000\b"#,english)
            || matches(#"[<≤]\s*=?\s*15000\b"#,english)
        if longWalk && !negatedWalk{return .playfulTired}
        let rainScene=scene.contains("雨") || scene.contains("伞") || normalized.contains("雨") || normalized.contains("伞")
            || matches(#"\b(?:rain|rains|rainy|raining|umbrella)\b"#,englishScene+" "+english)
        let declines=["不要","不想","不去","不行","不愿","不可以","不喜欢","不用","不需要","先不","不打算","换","改","等","躲","避","室内","晴天","看天气","再定","再决定"]
        let accepts=["带伞","带上伞","撑伞","没关系","不怕","可以","愿意","都行","照样","照常","也能","喜欢","走吧","去吧"]
        // Keep word boundaries for English; removing spaces would confuse “rain” with “train”.
        let refusalText=english.replacingOccurrences(of:#"\b(?:don't mind|do not mind|not afraid|not worried|no problem)\b"#,with:"",options:.regularExpression)
        let declinesEnglish=matches(#"\b(?:no|not|never|don't|do not|won't|will not|can't|cannot|avoid|skip|indoors?|inside|wait|change|instead|rather)\b"#,refusalText)
        let acceptsEnglish=matches(#"\b(?:fine|okay|ok|all right|no problem|don't mind|do not mind|not afraid|not worried|happy to|let's go|still go|go anyway|rain or shine|love|like)\b"#,english)
            || matches(#"\b(?:bring|take|use|carry|open)\b.{0,16}\bumbrella\b"#,english)
        if rainScene && !declines.contains(where:normalized.contains) && !declinesEnglish && (accepts.contains(where:normalized.contains) || acceptsEnglish){return .rainConversation}
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
    private static let cache:NSCache<NSString,UIImage> = {let cache=NSCache<NSString,UIImage>();cache.countLimit=90;cache.totalCostLimit=24*1024*1024;return cache}()
    init(manifest:KoalaManifest?=KoalaManifest.load()){if let manifest{playback=KoalaPlayback(manifest)};render()}
    func request(_ action:KoalaAction,token:String,returnToIdle:Bool=false){playback?.request(action,token:token,returnToIdle:returnToIdle)}
    private func render() {
        guard let playback,let path=playback.framePath,let url=Bundle.main.resourceURL?.appendingPathComponent("koala/"+path) else{return}
        let key=url.path as NSString
        if let cached=Self.cache.object(forKey:key){image=cached;return}
        if let value=UIImage(contentsOfFile:url.path){Self.cache.setObject(value,forKey:key,cost:Int(value.size.width*value.scale)*Int(value.size.height*value.scale)*4);image=value}
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
    @Environment(\.locale) private var interfaceLocale

    var mood:PetMood = .greeting
    var cue:KoalaCue? = nil
    var animated=true
    var size:CGFloat=108
    var onIdle:(()->Void)? = nil
    @StateObject private var player=KoalaPlayer()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase
    var body:some View {
        let _=interfaceLocale
        Group {
            if let image=player.image{Image(uiImage:image).resizable().interpolation(.high).scaledToFit()}
            else {Image(systemName:"leaf.circle.fill").resizable().scaledToFit().foregroundStyle(Palette.green)}
        }.frame(width:size,height:size*116/108)
        .accessibilityElement(children:.ignore).accessibilityLabel(uiText("旅行伙伴 Koko，\(mood == .thinking ? "正在思考":mood == .listening ? "正在听你说":"陪你准备旅行")"))
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
    @Environment(\.locale) private var interfaceLocale

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
        case .departure:return "咱们出发吧"
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
        let _=interfaceLocale
        HStack(spacing:8){PetAvatar(cue:visibleCue,onIdle:{if closing || !held{dismiss()}});VStack(alignment:.leading,spacing:8){Text(uiText(title)).font(.subheadline.bold());if held{Button(uiText(closing ? "咱们继续":"知道了"),action:close).font(.caption).disabled(closing)}}}
            .padding(.horizontal,18).background(Palette.cream,in:RoundedRectangle(cornerRadius:24))
            .onChange(of:cue.id){_,_ in closing=false}
            .task(id:cue.id){if reduceMotion && !held{do{try await Task.sleep(nanoseconds:1_700_000_000);dismiss()}catch{}}}
    }
}


/// One companion stage, shared by the welcome and current conversational turn.
/// Prop gestures use authored clips; listening uses a small pose change only.
struct KokoCompanionScene:View {
    @Environment(\.locale) private var interfaceLocale

    var mood:PetMood = .greeting
    var cue:KoalaCue? = nil
    var ready=false
    var height:CGFloat=226
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var tapped:KoalaCue?
    private var activeCue:KoalaCue? {tapped ?? cue}
    var body:some View {
        let _=interfaceLocale
        ZStack(alignment:.bottom){
            Ellipse().fill(Palette.mint.opacity(0.75)).frame(width:250,height:28).padding(.bottom,7)
            HStack{Image(systemName:"leaf.fill").rotationEffect(.degrees(-35));Spacer();Image(systemName:"leaf.fill").rotationEffect(.degrees(35))}.font(.system(size:22)).foregroundStyle(Palette.green.opacity(0.25)).frame(width:270).padding(.bottom,24).accessibilityHidden(true)
            if ready {
                HStack(spacing:134){packingCard("leaf.fill",angle:-12);packingCard("map.fill",angle:12)}.padding(.bottom,64).transition(.opacity.combined(with:.scale(scale:0.85)))
            }
            Button{if mood != .thinking{tapped=KoalaCue(action:.wave)}}label:{
                PetAvatar(mood:mood,cue:activeCue,size:height*1.12)
                    .rotationEffect(.degrees(!reduceMotion && mood == .listening ? -4:0),anchor:.bottom)
                    .offset(y:!reduceMotion && mood == .excited ? -5:0)
            }.buttonStyle(.plain).accessibilityLabel(uiText(mood == .thinking ? "Koko 正在看小地图":mood == .listening ? "Koko 正在听你说":ready ? "Koko 已准备好，点击打招呼":"和 Koko 打个招呼")).accessibilityIdentifier("koko-companion")
        }.frame(maxWidth:.infinity).frame(height:height).animation(reduceMotion ? nil:.spring(response:0.45,dampingFraction:0.8),value:mood).animation(reduceMotion ? nil:.easeInOut(duration:0.3),value:ready)
        .task(id:tapped?.id){
            guard let token=tapped?.id else{return}
            do{try await Task.sleep(nanoseconds:ready ? 4_500_000_000:1_500_000_000);if tapped?.id==token{tapped=nil}}catch{}
        }
        .onChange(of:cue){_,_ in tapped=nil}
        .onChange(of:mood){_,_ in tapped=nil}
    }
    private func packingCard(_ symbol:String,angle:Double)->some View {
        Image(systemName:symbol).font(.title2).foregroundStyle(Palette.green).frame(width:42,height:52).background(.white,in:RoundedRectangle(cornerRadius:9)).overlay(RoundedRectangle(cornerRadius:9).stroke(Palette.green.opacity(0.15))).rotationEffect(.degrees(angle))
    }
}

import SwiftUI

enum KokoCompanionSizing {
    // The approved Idle body spans y=50...233 inside its transparent 256px canvas.
    static let background:UIImage = {
        guard let url=Bundle.main.resourceURL?.appendingPathComponent("koala/coastal-background.png"),let image=UIImage(contentsOfFile:url.path) else{return UIImage()};return image
    }()
    static let visibleHeightRatio:CGFloat = 183.0/256.0
    static func characterSize(viewport:CGSize)->CGFloat {
        let bodyHeight=viewport.height*(viewport.height<700 ? 0.22:0.28)
        return max(110,min(viewport.width*0.80,bodyHeight/visibleHeightRatio))
    }
}

struct KokoCompanionStage:View {
    let cue:KoalaCue?
    let size:CGFloat
    var mood:PetMood = .listening
    var body:some View {
        GeometryReader { space in
            ZStack(alignment:.bottom){
                Image(uiImage:KokoCompanionSizing.background).resizable().scaledToFill()
                    .frame(width:space.size.width,height:space.size.height).clipped()
                    .mask(LinearGradient(stops:[.init(color:.clear,location:0),.init(color:.white,location:0.16),.init(color:.white,location:0.86),.init(color:.clear,location:1)],startPoint:.top,endPoint:.bottom))
                    .accessibilityHidden(true)
                // Anchor the character's feet, rather than the transparent canvas edge.
                PetAvatar(mood:mood,cue:cue,size:size)
                    .offset(y:size*(23.0/256.0+4.0/108.0)).padding(.bottom,8)
            }.frame(width:space.size.width,height:space.size.height)
        }.frame(minHeight:size*233.0/256.0+16)
    }
}

struct KokoOptionButtonStyle:ButtonStyle {
    var prominent=false
    var compact=false
    var quiet=false
    private var accent:Color {quiet ? Color(red:0.20,green:0.36,blue:0.79):Palette.green}
    func makeBody(configuration:Configuration)->some View {
        configuration.label
            .foregroundStyle(prominent ? .white:Palette.ink)
            .frame(maxWidth:.infinity,minHeight:44)
            .padding(.horizontal,16).padding(.vertical,compact ? 5:7)
            .background(RoundedRectangle(cornerRadius:18).fill(prominent ? accent:(quiet ? .white:Palette.cream)))
            .overlay(RoundedRectangle(cornerRadius:18).stroke(accent.opacity(quiet ? 0.3:0.8),lineWidth:quiet ? 1:1.5))
            .background(RoundedRectangle(cornerRadius:18).fill(quiet ? .clear:accent).offset(y:configuration.isPressed ? 0:5))
            .offset(y:configuration.isPressed ? 4:0).padding(.bottom,quiet ? 0:5)
            .animation(.easeOut(duration:0.10),value:configuration.isPressed)
    }
}
