import XCTest
@testable import SydneyBuddy

final class PetDraftTests:XCTestCase {
    func testCustomValuesAndFifteenDayBoundary() throws {
        var values=fields;values["days"]="15";values["pace"]="自定义（约8000步）";values["budget"]="125.50"
        let profile=try PetTripDraft.profile(values)
        XCTAssertEqual(profile.days,15);XCTAssertEqual(profile.maxSteps,8000);XCTAssertEqual(profile.budget,125.5)
        values["days"]="16";XCTAssertThrowsError(try PetTripDraft.profile(values))
    }
    @MainActor func testPersonalOnboardingDoesNotCreateTripAndNewTripDoesNotRepeatIt() {
        let defaults=UserDefaults.standard
        let keys=["personalPreferences.completed","personalPreferences.v2.completed","onboarded","pendingTripFields","petSession.draft","travelPlanningTask","pendingPersonalProfile"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for (key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        defaults.removeObject(forKey:"pendingTripFields")
        let store=AppStore();store.completePersonalPreferences()
        XCTAssertFalse(store.showPersonalPreferences);XCTAssertTrue(store.showOnboarding)
        XCTAssertTrue(store.pendingTripFields.isEmpty)
        store.newTrip()
        XCTAssertTrue(store.showOnboarding);XCTAssertFalse(store.showPersonalPreferences)
        XCTAssertTrue(defaults.bool(forKey:"onboarded"))
    }
    @MainActor func testPersonalPreferenceEditUpdatesStructuredMemoryWithoutTripFields() async throws {
        let chat=PetChatStore();var saved:[String:Any]=[:];var method=""
        let memory=Memory(id:"personal",summary:"喜欢艺术",profile:.init(interests:["艺术"],maxSteps:8000))
        chat.loader={request in
            if request.httpMethod=="PUT"{method=request.httpMethod!;saved=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]}
            let data=try JSONEncoder().encode(request.httpMethod=="PUT" ? [memory]:[memory])
            let payload=request.httpMethod=="PUT" ? try JSONEncoder().encode(memory):data
            return (payload,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        var profile=TripProfile();profile.interests=["艺术"];profile.maxSteps=8000
        await chat.savePersonalPreferences(profile,existing:memory)
        XCTAssertNil(chat.error);XCTAssertEqual(method,"PUT")
        let body=saved["profile"] as! [String:Any]
        XCTAssertEqual(Set(body.keys),Set(["interests","maxSteps"]))
        XCTAssertEqual(body["maxSteps"] as? Int,8000)
    }
    func testUnknownHabitsRemainUnknownAndAreNotSeededIntoTrip() throws {
        let unknown=UserTravelProfile()
        let restored=try JSONDecoder().decode(UserTravelProfile.self,from:JSONEncoder().encode(unknown))
        XCTAssertNil(restored.dailySteps);XCTAssertNil(restored.energy);XCTAssertNil(restored.rhythm)
        XCTAssertTrue(restored.seed.isEmpty)
        XCTAssertTrue(TravelRequirements.assumptions([:],personal:restored).contains{$0.contains("10,000")})
    }
    func testGroupTripBudgetNormalizesWithoutChangingPersonalProfile() throws {
        let personal=UserTravelProfile(queueTolerance:"尽量不排队",dailySteps:6000,energy:"低精力",rhythm:"轻松随意",interests:["自然"])
        var values=fields;values["days"]="5";values["budget"]="2000";values["budgetCurrency"]="AUD";values["budgetBasis"]="groupTrip";values["travelers"]="2";values["pace"]="约8000步";values["origin"]="中央车站"
        let profile=try TravelRequirements.profile(values,personal:personal)
        XCTAssertEqual(profile.budget,200);XCTAssertEqual(profile.budgetAmount,2000);XCTAssertEqual(profile.travelers,2);XCTAssertEqual(profile.maxSteps,8000)
        XCTAssertEqual(profile.start,.central);XCTAssertEqual(profile.energy,"低精力")
        XCTAssertEqual(personal.dailySteps,6000)
        values["budgetCurrency"]="CNY";XCTAssertThrowsError(try TravelRequirements.profile(values,personal:personal))
        values["budgetCurrency"]="AUD";values["budgetBasis"]="不清楚";XCTAssertThrowsError(try TravelRequirements.profile(values,personal:personal))
    }
    @MainActor func testNewTripClearsPreviousTripFieldsButPreservesPersonalProfile() {
        let defaults=UserDefaults.standard
        let keys=["userTravelProfile","pendingTripFields","petSession.draft","travelPlanningTask","pendingPersonalProfile"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for (key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        let store=AppStore();store.personalProfile=UserTravelProfile(dailySteps:7000,interests:["文化"])
        store.pendingTripFields=fields;store.newTrip()
        XCTAssertTrue(store.pendingTripFields.isEmpty)
        XCTAssertEqual(store.personalProfile.dailySteps,7000)
        XCTAssertEqual(UserTravelProfile.load().dailySteps,7000)
        store.personalProfile.dailySteps=12000
        XCTAssertEqual(store.pendingPersonalProfile.dailySteps,7000)
    }
    func testDraftDisplayNeverInventsDestinationDaysOrEmptySeparators() {
        let blank=TripDraftSummary(fields:[:])
        XCTAssertEqual(blank.title,"目的地待确定 · 天数待确定")
        XCTAssertEqual(blank.subtitle,"日期待确定 · 同行人待确定")
        let unknown=TripDraftSummary(fields:["destination":"未确定","days":"未确定","date":"","companions":""])
        XCTAssertEqual(unknown.title,blank.title);XCTAssertEqual(unknown.subtitle,blank.subtitle)
        XCTAssertTrue(TravelRequirements.missingFields([:]).contains("目的地"))
    }
    @MainActor func testReturningFromIncompleteChatOnlySavesDraftWithoutPlanningValidation() {
        let defaults=UserDefaults.standard
        let keys=["pendingTripFields","onboarded"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for (key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        let store=AppStore();let personal=store.pendingPersonalProfile
        store.showOnboarding=true
        store.returnToTrips(draft:["interests":"艺术"])
        XCTAssertNil(store.errorMessage);XCTAssertFalse(store.showOnboarding)
        XCTAssertEqual(store.pendingTripFields,["interests":"艺术"])
        XCTAssertEqual(store.pendingPersonalProfile,personal);XCTAssertEqual(store.tab,1)
        store.resumePendingTrip()
        XCTAssertTrue(store.showOnboarding);XCTAssertNil(store.trip)
        XCTAssertEqual(store.pendingTripFields,["interests":"艺术"])
    }
    func testChatCanGenerateAsSoonAsCoreRequirementsAreKnownWithoutOptionalAnswers() throws {
        var values=fields
        values["budgetCurrency"]="AUD";values["budgetBasis"]="groupTrip";values["travelers"]="2";values["budget"]="400"
        let profile=try TravelRequirements.readyProfile(values,personal:UserTravelProfile())
        XCTAssertEqual(profile.budget,100)
        XCTAssertNil(values["origin"]);XCTAssertNil(values["mustDo"]);XCTAssertNil(values["avoid"])
        XCTAssertTrue(TravelRequirements.assumptions(values,personal:UserTravelProfile()).contains{$0.contains("环形码头")})
        values["budgetCurrency"]=nil
        XCTAssertThrowsError(try TravelRequirements.readyProfile(values,personal:UserTravelProfile()))
    }
    func testUncertainDestinationNeverOffersGenerationAndPoutingIsSupported() {
        for destination in ["", "还没想好", "暂不确定", "不知道", "未确定"] {
            var values=fields
            values["destination"]=destination;values["budgetCurrency"]="AUD";values["budgetBasis"]="perPersonPerDay";values["travelers"]="2"
            XCTAssertThrowsError(try TravelRequirements.readyProfile(values,personal:UserTravelProfile()))
        }
        XCTAssertEqual(PetMood.reply("pouting"),.pouting)
    }
    func testChatReadinessDoesNotAcceptUnknownCoreFieldsOrUnsupportedCurrency() {
        var values=fields
        values["budgetCurrency"]="AUD";values["budgetBasis"]="perPersonPerDay";values["travelers"]="未确定"
        XCTAssertThrowsError(try TravelRequirements.readyProfile(values,personal:UserTravelProfile()))
        values["travelers"]="2";values["budgetCurrency"]="CNY"
        XCTAssertThrowsError(try TravelRequirements.readyProfile(values,personal:UserTravelProfile()))
    }
    private var fields:[String:String] {["destination":"悉尼","date":"2026-11-01","days":"2","budget":"100","pace":"每天最多5000步","interests":"艺术和美食"]}
    @MainActor func testCompletingPreferencesOpensTripsWithoutPlanningAndRestoresDraft() throws {
        let defaults=UserDefaults.standard
        let keys=["pendingTripFields","onboarded","travelPlanningTask"]
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
    func testOnlyExplicitlyConfirmedCardsReachConversation() {
        let values=PetOnboardingDraft.initialValues(memories:[])
        let opening=PetOnboardingDraft.confirmedPayload(values,confirmed:[])
        XCTAssertTrue(opening.values.allSatisfy(\.isEmpty))
        let first=PetOnboardingDraft.confirmedPayload(values,confirmed:["destination"])
        XCTAssertEqual(first["destination"],"悉尼")
        for key in ["date","days","companions","budget","interests","pace","notes"] {XCTAssertEqual(first[key],"")}
        let dates=PetOnboardingDraft.confirmedPayload(values,confirmed:["destination","date","days"])
        XCTAssertFalse(dates["date"]!.isEmpty);XCTAssertEqual(dates["interests"],"")
    }
    func testNewestConfirmedPreferencesPrefillDraftAndCurrentChoicesOverrideThem() throws {
        let old=Memory(id:"old",summary:"喜欢自然，每天约15000步",profile:.init(interests:["自然"],maxSteps:15000),provenance:.init(createdAt:"2026-10-01T00:00:00Z"))
        let latest=Memory(id:"new",summary:"喜欢艺术，每天约6000步",profile:.init(interests:["艺术"],maxSteps:6000),provenance:.init(createdAt:"2026-10-24T00:00:00Z"))
        var values=PetOnboardingDraft.initialValues(memories:[old,latest],today:"2026-11-01")
        var profile=try PetTripDraft.profile(values)
        XCTAssertEqual(profile.interests,["艺术"]);XCTAssertEqual(profile.maxSteps,6000)
        values["interests"]="美食";values["pace"]="多逛逛（约15000步）"
        profile=try PetTripDraft.profile(values)
        XCTAssertEqual(profile.interests,["美食"]);XCTAssertEqual(profile.maxSteps,15000)
        let revoked=try PetTripDraft.profile(PetOnboardingDraft.initialValues(memories:[],today:"2026-11-01"))
        XCTAssertEqual(revoked.maxSteps,10000);XCTAssertEqual(Set(revoked.interests),Set(["自然","风景"]))
    }
    func testTextOnlyMemoryDoesNotInventWalkingPreference() throws {
        let memory=Memory(id:"text",summary:"喜欢艺术",profile:.init(interests:[],maxSteps:10000))
        let values=PetOnboardingDraft.initialValues(memories:[memory],today:"2026-11-01")
        XCTAssertEqual(values["interests"],"艺术")
        XCTAssertEqual(try PetTripDraft.profile(values).maxSteps,10000)
    }
    @MainActor func testLongTermSaveRequiresConsentAndExcludesTripOnlyData() async throws {
        let chat=PetChatStore();var bodies:[[String:Any]]=[]
        chat.loader={request in
            bodies.append(try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any])
            let data=Data(#"{"id":"saved","summary":"喜欢艺术","profile":{"interests":["艺术"],"maxSteps":5000}}"#.utf8)
            return (data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let profile=try PetTripDraft.profile(fields)
        await chat.savePreferences(profile,consent:false)
        XCTAssertTrue(bodies.isEmpty);XCTAssertTrue(chat.memories.isEmpty)
        await chat.savePreferences(profile,consent:true)
        XCTAssertNil(chat.error);XCTAssertEqual(bodies.count,1)
        XCTAssertEqual(bodies[0]["consent"] as? Bool,true)
        let saved=bodies[0]["profile"] as! [String:Any]
        XCTAssertEqual(Set(saved.keys),Set(["interests","maxSteps"]))
        XCTAssertEqual(saved["maxSteps"] as? Int,5000)
        XCTAssertFalse((bodies[0]["summary"] as! String).contains("2026-11-01"))
        XCTAssertEqual(chat.memories.count,1)
    }
    @MainActor func testCancellingPlanningPreservesDraftAndDoesNotSaveTrip() async throws {
        let store=AppStore();let original=store.pendingTripFields
        defer{store.pendingTripFields=original}
        var complete=fields;complete["budgetCurrency"]="AUD";complete["budgetBasis"]="perPersonPerDay";complete["travelers"]="2"
        store.pendingTripFields=complete
        var paths:[String]=[]
        store.loader={request in paths.append(request.url!.path);try await Task.sleep(nanoseconds:5_000_000_000);throw URLError(.badServerResponse)}
        store.startGeneratingPendingTrip()
        for _ in 0..<100{if store.busy{break};try await Task.sleep(nanoseconds:1_000_000)}
        XCTAssertTrue(store.busy);XCTAssertTrue(store.canCancelOperation)
        store.cancelPendingGeneration()
        for _ in 0..<100{if !store.busy{break};try await Task.sleep(nanoseconds:1_000_000)}
        XCTAssertFalse(store.busy);XCTAssertFalse(store.canCancelOperation)
        XCTAssertEqual(store.pendingTripFields,complete);XCTAssertNil(store.errorMessage)
        XCTAssertEqual(paths,["/plan"]);XCTAssertTrue(store.saved.isEmpty)
    }
    func testPetReactionMatchesChosenAnswerAndNeverClaimsLongTermSave() {
        XCTAssertEqual(PetReaction.answer("companions","一个人").mood,.relaxed)
        XCTAssertEqual(PetReaction.answer("companions","和朋友").mood,.excited)
        XCTAssertEqual(PetReaction.answer("companions","和家人").mood,.remembering)
        XCTAssertEqual(PetReaction.answer("pace","慢悠悠（约6000步）").mood,.relaxed)
        XCTAssertEqual(PetReaction.answer("pace","多逛逛（约15000步）").mood,.excited)
        XCTAssertEqual(PetReaction.answer("interests","美食").mood,.excited)
        XCTAssertEqual(PetReaction.answer("interests","艺术").mood,.curious)
        for key in ["companions","interests","pace"] {XCTAssertFalse(PetReaction.answer(key,"和朋友").text.contains("永久记住"))}
    }
    func testReplyEmotionIsBackwardCompatibleAndUnknownValuesAreSafe() throws {
        let reply=try JSONDecoder().decode(PetMessage.self,from:Data(#"{"id":"m","role":"assistant","text":"慢慢走就好","mood":"relaxed"}"#.utf8))
        XCTAssertEqual(PetMood.reply(reply.mood),.relaxed)
        let old=try JSONDecoder().decode(PetMessage.self,from:Data(#"{"id":"m","role":"assistant","text":"你好"}"#.utf8))
        XCTAssertEqual(PetMood.reply(old.mood),.curious)
        XCTAssertEqual(PetMood.reply("angry"),.curious)
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
        for (key,value) in [("date","2026-02-30"),("date","2026-2-1"),("days","未确定"),("days","16"),("budget","nan"),("budget","-1"),("pace","每天99999步")] {
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
