import XCTest
@testable import SydneyBuddy

final class KoalaAnimationTests:XCTestCase {
    private var manifest:KoalaManifest {KoalaManifest(animations:[
        KoalaClip(id:"idle",fps:12,frames:36,playback:"loop",ending:"loop",recovery:"none",framePath:"",poster:""),
        KoalaClip(id:"clap",fps:12,frames:12,playback:"once",ending:"idle",recovery:"none",framePath:"",poster:""),
        KoalaClip(id:"map",fps:12,frames:36,playback:"once",ending:"hold",recovery:"reverse",framePath:"",poster:""),
        KoalaClip(id:"tired",fps:12,frames:36,playback:"once",ending:"hold",recovery:"hold",framePath:"",poster:"")])}
    func testOneEventPlaysOnceAndDuplicateTokensCannotRestartIt() {
        var player=KoalaPlayback(manifest);player.request(.clap,token:"one");player.advance()
        for _ in 0..<5{player.advance()};let frame=player.frame
        player.request(.clap,token:"one");XCTAssertEqual(frame,player.frame)
        for _ in 0..<10{player.advance()};XCTAssertEqual(.idle,player.action)
    }
    func testMapRecoversThroughExistingFramesInsteadOfTeleporting() {
        var player=KoalaPlayback(manifest);player.request(.map,token:"thinking");player.advance()
        for _ in 0..<10{player.advance()};XCTAssertEqual(11,player.frame)
        player.request(.idle,token:"reply");player.advance();XCTAssertTrue(player.reversing);XCTAssertEqual(10,player.frame)
        for _ in 0..<12{player.advance()};XCTAssertEqual(.idle,player.action)
    }
    func testLyingEndpointCannotSnapBackWithoutAnAuthoredRecovery() {
        var player=KoalaPlayback(manifest);player.request(.tired,token:"tired");player.advance()
        for _ in 0..<40{player.advance()};player.request(.idle,token:"rested")
        for _ in 0..<40{player.advance()};XCTAssertEqual(.tired,player.action);XCTAssertEqual(36,player.frame)
    }
    func testBundledManifestAndEveryNumberedFrameAreIncluded() throws {
        let manifest=try XCTUnwrap(KoalaManifest.load());XCTAssertEqual(10,manifest.animations.count)
        XCTAssertEqual(276,manifest.animations.reduce(0){$0+$1.frames})
        for clip in manifest.animations{for frame in 1...clip.frames {
            XCTAssertTrue(FileManager.default.fileExists(atPath:Bundle.main.resourceURL!.appendingPathComponent("koala/"+clip.path(frame)).path))
        }}
        let recoveries=try XCTUnwrap(manifest.recoveries);XCTAssertEqual(4,recoveries.count);XCTAssertEqual(124,recoveries.reduce(0){$0+$1.frames})
        for recovery in recoveries {
            let action=try XCTUnwrap(manifest.animations.first{$0.id==recovery.sourceAction})
            XCTAssertEqual(action.recoveryId,recovery.id);XCTAssertEqual("source_frame_reversal",recovery.quality)
            for frame in 1...recovery.frames{XCTAssertTrue(FileManager.default.fileExists(atPath:Bundle.main.resourceURL!.appendingPathComponent("koala/"+recovery.path(frame)).path))}
            let first=try Data(contentsOf:Bundle.main.resourceURL!.appendingPathComponent("koala/"+recovery.path(1)))
            let source=try Data(contentsOf:Bundle.main.resourceURL!.appendingPathComponent("koala/"+action.path(action.frames)))
            let last=try Data(contentsOf:Bundle.main.resourceURL!.appendingPathComponent("koala/"+recovery.path(recovery.frames)))
            let idle=try Data(contentsOf:Bundle.main.resourceURL!.appendingPathComponent("koala/animations/idle/frames/001.png"))
            XCTAssertEqual(first,source);XCTAssertEqual(last,idle)
        }
        XCTAssertNil(manifest.animations.first{$0.id=="departure"}?.recoveryId)
    }
    private var returningManifest:KoalaManifest {
        var value=manifest
        value.animations.append(KoalaClip(id:"weather",fps:12,frames:30,playback:"once",ending:"hold",recovery:"reverse",framePath:"weather/{frame:03}",poster:""))
        value.animations.append(KoalaClip(id:"departure",fps:12,frames:36,playback:"once",ending:"hold",recovery:"hold",framePath:"departure/{frame:03}",poster:""))
        value.recoveries=[]
        for index in value.animations.indices where ["tired","map","weather"].contains(value.animations[index].id) {
            let action=value.animations[index].id;value.animations[index].recoveryId=action+"-to-idle"
            value.recoveries!.append(KoalaRecoveryClip(id:action+"-to-idle",sourceAction:action,fps:12,frames:6,framePath:"recoveries/"+action+"/{frame:03}",quality:"source_frame_reversal"))
        }
        return value
    }
    func testCompleteProfilesCelebrateWhileSingleSubmissionsOnlyClap() {
        XCTAssertEqual(.wave,KoalaEvent.welcome.action)
        XCTAssertEqual(.clap,KoalaEvent.informationSubmitted.action)
        for event in [KoalaEvent.choiceAccepted,.stepCompleted,.optimizationProgress,.preferenceConfirmed]{XCTAssertEqual(.thumbsUp,event.action)}
        for event in [KoalaEvent.dnaCompleted,.tripSaved]{XCTAssertEqual(.celebrate,event.action)}
        XCTAssertEqual(.departure,KoalaEvent.journeyStarted.action)
        XCTAssertEqual(.map,KoalaEvent.simulationStarted.action)
        XCTAssertEqual(.map,KoalaEvent.planningStarted.action);XCTAssertEqual(.map,KoalaEvent.optimizationStarted.action)
    }
    func testConversationReactionsUseExplicitCurrentIntentWithoutClaimingRisk() {
        XCTAssertEqual(.playfulTired,KoalaConversationFeedback.event(text:"15,000 步 · 多探索"))
        XCTAssertEqual(.playfulTired,KoalaConversationFeedback.event(text:"15000",scene:"这趟想走多少步？"))
        XCTAssertEqual(.playfulTired,KoalaConversationFeedback.event(text:"一万五千步也可以"))
        XCTAssertNil(KoalaConversationFeedback.event(text:"预算15000澳元",scene:"想走多少步？"))
        XCTAssertNil(KoalaConversationFeedback.event(text:"不是15000步，改成10000步"))
        XCTAssertNil(KoalaConversationFeedback.event(text:"不想走15000步"))
        XCTAssertNil(KoalaConversationFeedback.event(text:"我平时喜欢宅家"))
        XCTAssertEqual(.rainConversation,KoalaConversationFeedback.event(text:"走，带上伞",scene:"这趟要是下点小雨，撑伞逛逛？"))
        XCTAssertEqual(.rainConversation,KoalaConversationFeedback.event(text:"下雨也没关系"))
        XCTAssertNil(KoalaConversationFeedback.event(text:"换个室内活动",scene:"下雨也出去？"))
        XCTAssertNil(KoalaConversationFeedback.event(text:"我不喜欢下雨"))
        XCTAssertNil(KoalaConversationFeedback.event(text:"看天气再决定",scene:"这趟下雨怎么办？"))
        XCTAssertTrue(KoalaCue(event:.playfulTired).returnsToIdle);XCTAssertTrue(KoalaCue(event:.rainConversation).returnsToIdle)
        XCTAssertFalse(KoalaCue(event:.fatigueRisk).returnsToIdle);XCTAssertFalse(KoalaCue(event:.weatherRisk).returnsToIdle)
        XCTAssertNotEqual(KoalaEvent.playfulTired.rawValue,KoalaEvent.fatigueRisk.rawValue)
    }
    func testTemporaryReactionsPlayTheReturnClipThenBecomeIdle() {
        for action in [KoalaAction.tired,.weather] {
            var player=KoalaPlayback(returningManifest);player.request(action,token:action.rawValue,returnToIdle:true);player.advance()
            var sawRecovery=false
            for _ in 0..<100{if player.recovering{sawRecovery=true;XCTAssertTrue(player.framePath!.hasPrefix("recoveries/"))};player.advance()}
            XCTAssertTrue(sawRecovery);XCTAssertEqual(.idle,player.action);XCTAssertFalse(player.recovering)
        }
    }
    func testRealRiskHoldsUntilReleasedEvenWhenReturnFramesExist() {
        var player=KoalaPlayback(returningManifest);player.request(.tired,token:"real-risk");player.advance()
        for _ in 0..<100{player.advance()};XCTAssertEqual(.tired,player.action);XCTAssertEqual(36,player.frame);XCTAssertFalse(player.recovering)
        player.request(.idle,token:"acknowledged");player.advance();XCTAssertTrue(player.recovering);XCTAssertEqual(1,player.frame)
        for _ in 0..<6{player.advance()};XCTAssertEqual(.idle,player.action)
    }
    func testNextChoiceWaitsForPhysicalReturnAndOrdinaryIdleCannotDiscardIt() {
        var player=KoalaPlayback(returningManifest);player.request(.tired,token:"joke",returnToIdle:true);player.advance()
        for _ in 0..<36{player.advance()};XCTAssertTrue(player.recovering)
        player.request(.clap,token:"next-choice");player.request(.idle,token:"ordinary-chat")
        for _ in 0..<6{player.advance()};XCTAssertEqual(.idle,player.action)
        player.advance();XCTAssertEqual(.clap,player.action);XCTAssertEqual(1,player.frame)
    }
    func testSuitcaseCannotDissolveIntoStandingWhenNoPhysicalReturnExists() {
        var player=KoalaPlayback(returningManifest);player.request(.departure,token:"journey",returnToIdle:true);player.advance()
        for _ in 0..<40{player.advance()};player.request(.clap,token:"later")
        for _ in 0..<100{player.advance()};XCTAssertEqual(.departure,player.action);XCTAssertEqual(36,player.frame);XCTAssertFalse(player.recovering)
    }
    func testSameActionRealRiskRevokesPlayfulAutoReturn() {
        for action in [KoalaAction.tired,.weather] {
            var player=KoalaPlayback(returningManifest);player.request(action,token:"playful",returnToIdle:true);player.advance()
            for _ in 0..<5{player.advance()}
            player.request(action,token:"actual-risk",returnToIdle:false)
            for _ in 0..<120{player.advance()}
            XCTAssertEqual(action,player.action);XCTAssertEqual(player.clip!.frames,player.frame);XCTAssertFalse(player.recovering)
        }
    }
    func testRealRiskDuringRecoveryIsReplayedAndHeld() {
        var player=KoalaPlayback(returningManifest);player.request(.tired,token:"joke",returnToIdle:true);player.advance()
        for _ in 0..<36{player.advance()};XCTAssertTrue(player.recovering)
        player.request(.tired,token:"actual-risk",returnToIdle:false)
        for _ in 0..<120{player.advance()}
        XCTAssertEqual(.tired,player.action);XCTAssertEqual(36,player.frame);XCTAssertFalse(player.recovering)
    }
    @MainActor func testIdleCallbackFiresOnlyAfterARealReturnAndNotOnIdleLoops() {
        let player=KoalaPlayer(manifest:returningManifest);var count=0;player.onIdle={count+=1}
        for _ in 0..<10{player.advance()};XCTAssertEqual(0,count)
        player.request(.tired,token:"joke",returnToIdle:true)
        for _ in 0..<120{player.advance()};XCTAssertEqual(1,count)
        for _ in 0..<120{player.advance()};XCTAssertEqual(1,count)
        player.request(.clap,token:"next-step")
        for _ in 0..<20{player.advance()};XCTAssertEqual(2,count)
    }
    @MainActor func testWorkEndingBeforeItsFirstTickStillReleasesAndSignalsIdle() {
        for action in [KoalaAction.map,.tired,.weather] {
            let player=KoalaPlayer(manifest:returningManifest);var count=0;player.onIdle={count+=1}
            player.request(action,token:"brief-event");player.request(.idle,token:"ended-before-first-frame")
            for _ in 0..<120{player.advance()};XCTAssertEqual(1,count)
            for _ in 0..<20{player.advance()};XCTAssertEqual(1,count)
        }
    }
    @MainActor func testIdleCallbackWaitsForTheQueuedGestureInsteadOfDismissingItEarly() {
        let player=KoalaPlayer(manifest:returningManifest);var count=0;player.onIdle={count+=1}
        player.request(.tired,token:"playful",returnToIdle:true)
        for _ in 0..<37{player.advance()}
        player.request(.clap,token:"new-acknowledgement")
        for _ in 0..<6{player.advance()}
        XCTAssertEqual(0,count)
        for _ in 0..<20{player.advance()}
        XCTAssertEqual(1,count)
    }
}
