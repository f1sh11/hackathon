import XCTest
import UIKit
@testable import SydneyBuddy

final class KokoPresentationTests:XCTestCase {
    func testCoastalBackgroundIsActuallyBundledWithTheNativeApp() {
        XCTAssertNotNil(UIImage(named:"KokoCoastalBackground"))
    }
    func testProfileChoicesSeedOnlyThisTripWithoutInventingTripContextOrFitness() {
        let fact=TravelPreference(id:"p",key:"interests",dimension:"interests",value:"自然",condition:"",scope:"long_term",source:"user_statement",status:"awaiting_confirmation",confidence:0.9,evidence:"喜欢自然",sourceMessageId:"u",memoryId:"",conflictingMemoryIds:[],previousValues:[])
        let s=PetSession(id:"test",messages:[],fields:["destination":"悉尼","date":"2026-11-01"],pendingMemory:"",nextField:"",question:"",options:[],completed:0,total:7,pendingPreferences:[fact])
        let seed=KokoConversationPresentation.tripSeed(from:s)
        XCTAssertEqual(seed["interests"],"自然");XCTAssertNil(seed["destination"]);XCTAssertNil(seed["date"]);XCTAssertNil(seed["pace"])
        XCTAssertEqual(s.pendingPreferences?.first?.status,"awaiting_confirmation")
    }
    func testPresentationExposesCurrentAssistantOnlyWhileKeepingSessionEvidenceIntact() {
        let messages=[PetMessage(id:"old",role:"assistant",text:"旧问题"),PetMessage(id:"u",role:"user",text:"用户自己的原话"),PetMessage(id:"now",role:"assistant",text:"咱们顺路看看？")]
        let s=PetSession(id:"test",messages:messages,fields:[:],pendingMemory:"",nextField:"",question:"",options:[],completed:0,total:7)
        let visible=KokoConversationPresentation.utterance(session:s,streaming:"",busy:false,personalSetup:false)
        XCTAssertEqual(visible,"咱们顺路看看？")
        XCTAssertFalse(visible.contains("旧问题"));XCTAssertFalse(visible.contains("用户自己的原话"))
        XCTAssertEqual(s.messages.count,3)
    }
    func testWaitingDoesNotRedisplayLastQuestionOrOutgoingUserText() {
        let visible=KokoConversationPresentation.utterance(session:nil,streaming:"",busy:true,personalSetup:false)
        XCTAssertEqual(visible,"让我想一想，咱们怎么接着安排。")
        XCTAssertEqual(KokoConversationPresentation.utterance(session:nil,streaming:"新的回答",busy:true,personalSetup:false),"新的回答")
    }
    func testAProfileCorrectionStillShowsTheNewReplyAfterInitialCompletion() {
        var s=PetSession(id:"test",messages:[PetMessage(id:"new",role:"assistant",text:"那这次就改成少走一点，留出休息时间。")],fields:[:],pendingMemory:"",nextField:"",question:"",options:[],completed:0,total:7)
        s.dialogue=ProfilingDialogueState(version:2,responseCount:4,phase:"inspiration",nickname:"",ideas:[],inspiration:TravelInspiration(title:"灵感",ideas:[],status:"inspiration",placesVerified:false))
        XCTAssertEqual(KokoConversationPresentation.utterance(session:s,streaming:"",busy:false,personalSetup:true),s.messages[0].text)
    }
}
