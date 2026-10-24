import SwiftUI
import UIKit

enum KoalaAction:String,Codable,CaseIterable {
    case idle,wave,clap,map,weather,budget,tired,celebrate,departure
    case thumbsUp="thumbs-up"
    static func forMood(_ mood:PetMood)->Self {.idle}
}
/// Semantic events are emitted by successful actions or real engine results, never by the LLM's mood.
enum KoalaEvent:String,CaseIterable {
    case welcome="app.welcome",chatReady="chat.ready",dnaCompleted="profile.confirmed",preferenceConfirmed="user.confirmed"
    case planningStarted="plan.started",optimizationStarted="optimization.started",simulationStarted="simulation.started"
    case weatherRisk="risk.weather",budgetRisk="risk.budget",fatigueRisk="risk.energy",tripSaved="trip.saved"
    var action:KoalaAction {
        switch self {
        case .welcome:return .wave
        case .chatReady:return .idle
        case .dnaCompleted:return .clap
        case .preferenceConfirmed:return .thumbsUp
        case .planningStarted,.optimizationStarted:return .map
        case .simulationStarted:return .departure
        case .weatherRisk:return .weather
        case .budgetRisk:return .budget
        case .fatigueRisk:return .tired
        case .tripSaved:return .celebrate
        }
    }
}
struct KoalaCue:Equatable {
    var action:KoalaAction
    var id:String=UUID().uuidString
    init(action:KoalaAction,id:String=UUID().uuidString){self.action=action;self.id=id}
    init(event:KoalaEvent,id:String=UUID().uuidString){self.action=event.action;self.id=id}
}
struct KoalaClip:Decodable {
    var id:String;var fps:Int;var frames:Int;var playback:String;var ending:String;var recovery:String
    var framePath:String;var poster:String
    func path(_ frame:Int)->String {framePath.replacingOccurrences(of:"{frame:03}",with:String(format:"%03d",frame))}
}
struct KoalaManifest:Decodable {
    var animations:[KoalaClip]
    static func load(bundle:Bundle = .main)->Self? {
        guard let url=bundle.url(forResource:"manifest",withExtension:"json",subdirectory:"koala"),let data=try? Data(contentsOf:url) else{return nil}
        return try? JSONDecoder().decode(Self.self,from:data)
    }
}

/// Frame-only playback: finish a gesture, coalesce new events, and reverse reversible props.
/// Unsupported recoveries HOLD; a crossfade must not turn a lying koala into a standing one.
struct KoalaPlayback {
    let clips:[String:KoalaClip]
    private(set) var action:KoalaAction = .idle
    private(set) var frame=1
    private(set) var reversing=false
    private var pending:KoalaAction?
    private var lastToken=""
    init(_ manifest:KoalaManifest){clips=Dictionary(uniqueKeysWithValues:manifest.animations.map{($0.id,$0)})}
    var clip:KoalaClip? {clips[action.rawValue]}
    mutating func request(_ action:KoalaAction,token:String) {
        guard token != lastToken,clips[action.rawValue] != nil else{return}
        lastToken=token
        if action == .idle,let pending,pending != .idle,pending != .map{return}
        if action==self.action && pending==nil{return}
        pending=action
    }
    mutating func advance() {
        guard let clip else{return}
        if action == .idle,let next=pending {pending=nil;action=next;frame=1;reversing=false;return}
        if clip.recovery=="reverse",pending != nil,pending != action {reversing=true}
        if reversing {
            if frame>1{frame-=1}else{action = .idle;frame=1;reversing=false}
        } else if frame<clip.frames {frame+=1}
        else if clip.playback=="loop" {frame=1}
        else if clip.ending=="idle" {action = .idle;frame=1}
        // A non-reversible held endpoint intentionally keeps its support and props.
    }
}

@MainActor final class KoalaPlayer:ObservableObject {
    @Published private(set) var image:UIImage?
    private var playback:KoalaPlayback?
    private static let cache:NSCache<NSString,UIImage> = {let cache=NSCache<NSString,UIImage>();cache.totalCostLimit=12*1024*1024;return cache}()
    init(){if let manifest=KoalaManifest.load(){playback=KoalaPlayback(manifest)};render()}
    func request(_ action:KoalaAction,token:String){playback?.request(action,token:token)}
    private func render() {
        guard let playback,let clip=playback.clip,let url=Bundle.main.resourceURL?.appendingPathComponent("koala/"+clip.path(playback.frame)) else{return}
        let key=url.path as NSString
        if let cached=Self.cache.object(forKey:key){image=cached;return}
        if let value=UIImage(contentsOfFile:url.path){Self.cache.setObject(value,forKey:key,cost:256*256*4);image=value}
    }
    func run() async {
        do {
            while !Task.isCancelled {
                playback?.advance();render()
                try await Task.sleep(nanoseconds:UInt64(1_000_000_000/max(1,playback?.clip?.fps ?? 12)))
            }
        }catch{}
    }
}

struct PetAvatar:View {
    var mood:PetMood = .greeting
    var cue:KoalaCue? = nil
    var animated=true
    @StateObject private var player=KoalaPlayer()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase
    private var interactionLabel:String {
        switch mood {case .thinking:return "正在思考";case .listening:return "正在听你说";case .speaking:return "正在回复";case .pouting,.confused:return "等你补充信息";default:return "陪你准备旅行"}
    }
    private var interactionSymbol:String? {
        switch mood {case .thinking:return "ellipsis";case .listening:return "ear";case .speaking:return "waveform";case .pouting,.confused:return "questionmark";default:return nil}
    }
    var body:some View {
        Group {
            if let image=player.image{Image(uiImage:image).resizable().interpolation(.high).scaledToFit()}
            else {Image(systemName:"leaf.circle.fill").resizable().scaledToFit().foregroundStyle(Palette.green)}
        }.frame(width:108,height:116)
        .overlay(alignment:.bottomTrailing) {
            if animated,let symbol=interactionSymbol {Image(systemName:symbol).font(.system(size:11,weight:.semibold)).foregroundStyle(Palette.green).padding(6).background(Palette.cream,in:Circle()).offset(x:-3,y:-5)}
        }
        .accessibilityElement(children:.ignore).accessibilityLabel("旅行伙伴 Koko，"+interactionLabel)
        .onAppear{player.request(cue?.action ?? KoalaAction.forMood(mood),token:cue?.id ?? "mood:"+mood.rawValue)}
        .onChange(of:mood){_,value in player.request(KoalaAction.forMood(value),token:"mood:"+value.rawValue)}
        .onChange(of:cue){_,value in if let value{player.request(value.action,token:value.id)}}
        .task(id:animated && !reduceMotion && scenePhase == .active){
            if animated && !reduceMotion && scenePhase == .active{await player.run()}
        }
    }
}

struct KoalaFeedbackBanner:View {
    let cue:KoalaCue
    let dismiss:()->Void
    private var held:Bool {[.map,.weather,.budget,.tired,.departure].contains(cue.action)}
    private var title:String {
        switch cue.action {
        case .clap:return "旅行灵感整理好了"
        case .thumbsUp:return "已保存你的修改"
        case .celebrate:return "行程已保存"
        case .budget:return "预计费用超出预算"
        case .weather:return "有天气风险"
        case .tired:return "模拟发现高疲劳风险"
        case .departure:return "旅行模拟已开始"
        default:return "Buddy 陪着你"
        }
    }
    var body:some View {
        HStack(spacing:10){PetAvatar(cue:cue).scaleEffect(0.65).frame(width:70,height:72);VStack(alignment:.leading,spacing:8){Text(title).font(.subheadline.bold());if held{Text("你可以检查详情后继续调整").font(.caption).foregroundStyle(Palette.muted)}};Spacer(minLength:0);Button(action:dismiss){Image(systemName:"xmark.circle.fill")}.accessibilityLabel("关闭 Koko 提示")}
            .padding(.horizontal,14).background(Palette.cream,in:RoundedRectangle(cornerRadius:24))
            .task(id:cue.id){if !held{do{try await Task.sleep(nanoseconds:cue.action == .celebrate ? 2_200_000_000:1_700_000_000);dismiss()}catch{}}}
    }
}
