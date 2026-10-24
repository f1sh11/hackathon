import XCTest
@testable import SydneyBuddy

@MainActor final class KokoEntryTests:XCTestCase {
    private func preserveDefaults()->()->Void {
        let defaults=UserDefaults.standard
        let keys=["koko.personalIntroduction.v3.completed","koko.personalIntroduction.v3.started","koko.hasMet","personalPreferences.completed","personalPreferences.v2.completed","onboarded","pendingTripFields","pendingPersonalProfile","userTravelProfile","travelPlanningTask","petSession.draft","petSession.personalProfile","petSession.koko-entry-accepted","petComposer.draft","personalProfile.memoryID"]
        let prior=keys.map{defaults.object(forKey:$0)}
        return {for(key,value) in zip(keys,prior){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
    }
    private func fixture() throws -> Trip {
        let url=Bundle(for:Self.self).url(forResource:"schedule-trip-test",withExtension:"json")!
        var trip=try JSONDecoder().decode(Trip.self,from:Data(contentsOf:url))
        trip.id="koko-entry-accepted";trip.revision=1;return trip
    }
    func testLegacyTripOnboardingDoesNotHideTheNewPersonalIntroduction() {
        let restore=preserveDefaults();defer{restore()}
        let defaults=UserDefaults.standard
        defaults.set(true,forKey:"onboarded");defaults.set(true,forKey:"personalPreferences.v2.completed")
        defaults.set(false,forKey:"koko.personalIntroduction.v3.completed");defaults.set(false,forKey:"koko.personalIntroduction.v3.started")
        let store=AppStore()
        XCTAssertTrue(store.showPersonalPreferences);XCTAssertFalse(store.personalIntroductionStarted)
        XCTAssertFalse(store.showOnboarding)
        store.beginPersonalIntroduction()
        XCTAssertTrue(store.personalIntroductionStarted)
        let restored=AppStore()
        XCTAssertTrue(restored.showPersonalPreferences);XCTAssertTrue(restored.personalIntroductionStarted)
    }
    func testSkippingIntroductionKeepsUnknownsMemoriesAndBothConversationSessions() throws {
        let restore=preserveDefaults();defer{restore()}
        let defaults=UserDefaults.standard
        defaults.set(false,forKey:"koko.personalIntroduction.v3.completed");defaults.set(false,forKey:"personalPreferences.v2.completed")
        defaults.set("trip-draft-session",forKey:"petSession.draft");defaults.set("personal-conversation-session",forKey:"petSession.personalProfile")
        defaults.set("confirmed-memory",forKey:"personalProfile.memoryID")
        let store=AppStore();let saved=try fixture();store.saved=[saved]
        store.personalProfile=UserTravelProfile(interests:["艺术"])
        store.pendingTripFields=["destination":"悉尼","budget":"120"]
        store.beginPersonalIntroduction();store.skipPersonalIntroduction()
        XCTAssertFalse(store.showPersonalPreferences);XCTAssertTrue(store.showOnboarding)
        XCTAssertEqual(store.pendingTripFields,["destination":"悉尼","budget":"120"])
        XCTAssertNil(store.personalProfile.dailySteps);XCTAssertEqual(store.personalProfile.interests,["艺术"])
        XCTAssertEqual(store.saved.map(\.id),[saved.id])
        XCTAssertEqual(defaults.string(forKey:"petSession.draft"),"trip-draft-session")
        XCTAssertEqual(defaults.string(forKey:"petSession.personalProfile"),"personal-conversation-session")
        XCTAssertEqual(defaults.string(forKey:"personalProfile.memoryID"),"confirmed-memory")
        XCTAssertFalse(defaults.bool(forKey:"personalPreferences.v2.completed"))
        XCTAssertTrue(defaults.bool(forKey:"koko.hasMet"))
        XCTAssertFalse(AppStore().showPersonalPreferences)
    }
    func testReturningToTripsClosesBothRootEntryFlagsWithoutClaimingProfileCompletion() {
        let restore=preserveDefaults();defer{restore()}
        let defaults=UserDefaults.standard
        defaults.set(false,forKey:"koko.personalIntroduction.v3.completed")
        let store=AppStore();store.beginPersonalIntroduction();store.showOnboarding=true
        store.returnToTrips(draft:["interests":"自然"])
        XCTAssertFalse(store.showPersonalPreferences);XCTAssertFalse(store.showOnboarding)
        XCTAssertEqual(store.pendingTripFields,["interests":"自然"])
        XCTAssertFalse(defaults.bool(forKey:"koko.personalIntroduction.v3.completed"))
    }
    func testActualSavedTripPresentsCenteredCelebrationAndPlanningUsesMap() async throws {
        let restore=preserveDefaults();defer{restore()}
        let accepted=try fixture();let data=try JSONEncoder().encode(accepted);let list=try JSONEncoder().encode([accepted])
        let store=AppStore();store.showOnboarding=true;store.pendingTripFields=["destination":"悉尼"]
        var paths:[String]=[];var sawMap=false
        store.loader={request in
            paths.append((request.httpMethod ?? "GET")+" "+request.url!.path)
            let response:Data
            switch request.url!.path {
            case "/plan":
                sawMap=await MainActor.run{store.koalaWork?.action == .map};response=data
            case "/trips":response=request.httpMethod=="POST" ? data:list
            case "/health":response=Data(#"{"status":"ok","nvidiaConfigured":false,"googleConfigured":false,"iosMapConfigured":false,"model":"test"}"#.utf8)
            case "/memories":response=Data("[]".utf8)
            default:throw URLError(.unsupportedURL)
            }
            return(response,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await store.generateFromPet(accepted.profile,request:"测试保存流程",saveImmediately:true)
        XCTAssertNil(store.errorMessage);XCTAssertTrue(sawMap);XCTAssertNil(store.koalaWork)
        XCTAssertEqual(store.koalaCompletion?.trip.id,accepted.id);XCTAssertEqual(store.koalaCompletion?.cue.action,.celebrate)
        XCTAssertNil(store.tripToOpen,"The celebration must remain visible until its navigation choice.")
        XCTAssertEqual(paths.filter{$0=="POST /trips"}.count,1)
        XCTAssertFalse(paths.contains("POST /memories"))
        store.finishTripCelebration(.viewTrip)
        XCTAssertNil(store.koalaCompletion);XCTAssertEqual(store.tripToOpen?.id,accepted.id)
    }
    func testFailedSaveDoesNotCelebrateOrDiscardTheProposal() async throws {
        let restore=preserveDefaults();defer{restore()}
        let store=AppStore();store.proposal=try fixture()
        store.loader={request in
            (Data(#"{"error":"保存暂时失败"}"#.utf8),HTTPURLResponse(url:request.url!,statusCode:500,httpVersion:nil,headerFields:nil)!)
        }
        await store.acceptProposal()
        XCTAssertNil(store.koalaCompletion);XCTAssertNotNil(store.errorMessage);XCTAssertNotNil(store.proposal)
    }
    func testCompletionButtonsChooseOneExplicitDestination() throws {
        let restore=preserveDefaults();defer{restore()}
        let saved=try fixture();let store=AppStore()
        store.koalaCompletion=KokoTripCompletion(trip:saved);store.tripToOpen=saved
        store.finishTripCelebration(.myTrips)
        XCTAssertNil(store.tripToOpen);XCTAssertFalse(store.showCompletionAdjustment);XCTAssertEqual(store.tab,1)
        store.koalaCompletion=KokoTripCompletion(trip:saved)
        store.finishTripCelebration(.adjustTrip)
        XCTAssertNil(store.tripToOpen);XCTAssertTrue(store.showCompletionAdjustment);XCTAssertEqual(store.trip?.id,saved.id)
    }
    func testViewingTripAndSimulationDoNotPretendToStartARealJourney() throws {
        let restore=preserveDefaults();defer{restore()}
        let saved=try fixture();let store=AppStore();store.saved=[saved]
        store.koalaCompletion=KokoTripCompletion(trip:saved)
        store.finishTripCelebration(.viewTrip)
        XCTAssertNil(store.koalaFeedback)
        XCTAssertFalse(store.journeyDidStart(tripID:saved.id!,simulation:true));XCTAssertNil(store.koalaFeedback)
        XCTAssertFalse(store.journeyDidStart(tripID:"unknown-trip"));XCTAssertNil(store.koalaFeedback)
        XCTAssertTrue(store.journeyDidStart(tripID:saved.id!+":1"))
        XCTAssertEqual(store.koalaFeedback?.event,.journeyStarted);XCTAssertEqual(store.koalaFeedback?.action,.departure)
        let cueID=store.koalaFeedback?.id
        XCTAssertFalse(store.journeyDidStart(tripID:saved.id!))
        XCTAssertEqual(store.koalaFeedback?.id,cueID)
    }
    func testAlreadyAtDestinationDoesNotPublishNavigationStarted() async throws {
        var day=try fixture().days[0];day.stops=[];day.end=day.start
        let navigator=WalkingNavigator(day:day,tripID:"koko-arrived-"+UUID().uuidString,endpoint:"http://127.0.0.1:1/unreachable")
        defer{navigator.pause()}
        navigator.setSimulation(true);navigator.start()
        for _ in 0..<100{if !navigator.busy{break};try await Task.sleep(nanoseconds:20_000_000)}
        XCTAssertNil(navigator.message);XCTAssertTrue(navigator.progress.arrived)
        XCTAssertFalse(navigator.active);XCTAssertNil(navigator.navigationStart)
    }
    func testFinishedModelWorkRetainsPlayerUntilTheActualIdleCallback() {
        var presentation=KokoWorkPresentationState()
        presentation.synchronize(KoalaCue(event:.planningStarted,id:"map-task"))
        presentation.synchronize(nil)
        XCTAssertTrue(presentation.awaitingIdle)
        XCTAssertEqual(presentation.retainedCue?.action,.idle)
        XCTAssertNotNil(presentation.retainedCue,"The closing-map player must remain mounted; completion is still gated.")
        let returnID=presentation.retainedCue?.id
        presentation.reachedIdle(cueID:"map-task")
        XCTAssertNotNil(presentation.retainedCue,"An old callback cannot finish the recovery.")
        presentation.reachedIdle(cueID:returnID)
        XCTAssertNil(presentation.retainedCue);XCTAssertFalse(presentation.awaitingIdle)
    }
    func testNewWorkDuringRecoveryIgnoresThePreviousIdleCallback() {
        var presentation=KokoWorkPresentationState()
        presentation.synchronize(KoalaCue(event:.planningStarted,id:"first"));presentation.synchronize(nil)
        let oldReturn=presentation.retainedCue?.id
        presentation.synchronize(KoalaCue(event:.planningStarted,id:"second"))
        XCTAssertFalse(presentation.awaitingIdle);XCTAssertEqual(presentation.retainedCue?.id,"second")
        presentation.reachedIdle(cueID:oldReturn)
        XCTAssertEqual(presentation.retainedCue?.id,"second")
        presentation.synchronize(nil)
        presentation.reachedIdle(cueID:presentation.retainedCue?.id)
        XCTAssertNil(presentation.retainedCue)
    }
    func testReducedMotionAndNoPriorWorkDoNotWaitForAnIdleTransitionThatCannotPlay() {
        var presentation=KokoWorkPresentationState()
        presentation.synchronize(nil)
        XCTAssertNil(presentation.retainedCue);XCTAssertFalse(presentation.awaitingIdle)
        presentation.synchronize(KoalaCue(event:.planningStarted,id:"reduced-motion-work"),reduceMotion:true)
        presentation.synchronize(nil,reduceMotion:true)
        XCTAssertNil(presentation.retainedCue);XCTAssertFalse(presentation.awaitingIdle)
    }
}
