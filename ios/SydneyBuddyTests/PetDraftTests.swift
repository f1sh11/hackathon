import XCTest
@testable import SydneyBuddy

final class PetDraftTests:XCTestCase {
    private var fields:[String:String] {["destination":"悉尼","date":"2026-11-01","days":"2","budget":"100","pace":"每天最多5000步","interests":"艺术和美食"]}
    @MainActor func testCompletingPreferencesOpensTripsWithoutPlanningAndRestoresDraft() throws {
        let defaults=UserDefaults.standard
        let keys=["pendingTripFields","onboarded"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for (key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        let store=AppStore()
        store.completePetPreferences(try PetTripDraft.profile(fields),fields:fields)
        XCTAssertFalse(store.showOnboarding)
        XCTAssertEqual(store.tab,1)
        XCTAssertNil(store.trip)
        XCTAssertNil(store.proposal)
        XCTAssertEqual(store.pendingTripFields,fields)
        let restored=AppStore()
        XCTAssertEqual(restored.pendingTripFields,fields)
        XCTAssertFalse(restored.showOnboarding)
    }
    func testTripReviewPreservesExplicitWalkingLimitAndBudget() throws {
        let profile=try PetTripDraft.profile(fields)
        XCTAssertEqual(profile.maxSteps,5000);XCTAssertEqual(profile.budget,100);XCTAssertEqual(profile.days,2)
        XCTAssertEqual(Set(profile.interests),Set(["艺术","美食"]))
    }
    func testUnsupportedDestinationCannotProduceSydneyItinerary() {
        var values=fields;values["destination"]="墨尔本"
        XCTAssertThrowsError(try PetTripDraft.profile(values))
    }
    func testInvalidDatesUnknownDaysAndNonfiniteBudgetsAreRejected() {
        for (key,value) in [("date","2026-02-30"),("date","2026-2-1"),("days","未确定"),("days","4"),("budget","nan"),("budget","-1"),("pace","每天99999步")] {
            var values=fields;values[key]=value;XCTAssertThrowsError(try PetTripDraft.profile(values),key+value)
        }
    }
    func testSessionDecodesStreamingFinalStateAndRestoresPendingConsent() throws {
        let data=Data(#"{"type":"done","session":{"id":"test","messages":[{"id":"m1","role":"user","text":"喜欢艺术"}],"fields":{"destination":"悉尼"},"pendingMemory":"喜欢艺术","nextField":"date","question":"哪天出发","options":["今天"],"completed":1,"total":8}}"#.utf8)
        let event=try JSONDecoder().decode(PetEvent.self,from:data)
        XCTAssertEqual(event.session?.pendingMemory,"喜欢艺术")
        XCTAssertEqual(event.session?.messages.first?.text,"喜欢艺术")
    }
}
