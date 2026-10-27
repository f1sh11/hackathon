import XCTest
@testable import SydneyBuddy

final class PetDraftTests:XCTestCase {
    func testConfirmedPreferenceOverridesLegacySnapshotWithoutMutatingOtherHabits() throws {
        let base=UserTravelProfile(queueTolerance:"Can wait a moment",dailySteps:10000,energy:"Medium energy",rhythm:"Easy-going",interests:["Art"])
        let data=Data(#"{"id":"new","summary":"Daily steps: 6000","profile":{"interests":[],"maxSteps":10000,"travelDna":{"id":"fact","key":"daily_steps","dimension":"pace","value":"6000","condition":"","scope":"long_term","source":"user_correction","status":"confirmed","confidence":1,"evidence":"6000 steps","sourceMessageId":"m","memoryId":"new","conflictingMemoryIds":[],"previousValues":[]}},"provenance":{"createdAt":"2026-10-27T01:00:00Z"}}"#.utf8)
        let memory=try JSONDecoder().decode(Memory.self,from:data)
        let value=UserTravelProfile.resolved(base:base,memories:[memory])
        XCTAssertEqual(value.dailySteps,6000);XCTAssertEqual(value.energy,base.energy);XCTAssertEqual(value.interests,["Art"])
        var pending=memory;pending.profile.travelDna?.status="awaiting_confirmation"
        XCTAssertEqual(UserTravelProfile.resolved(base:base,memories:[pending]).dailySteps,10000)
        pending.profile.travelDna?.status="confirmed";pending.profile.travelDna?.condition="Only for short breaks"
        XCTAssertEqual(UserTravelProfile.resolved(base:base,memories:[pending]).dailySteps,10000)
        XCTAssertEqual(UserTravelProfile.resolved(base:base,memories:[]),base)
    }

    @MainActor func testResumeRestoresTripFieldsWhenOnlyNotesRemainWithoutOverwritingExplicitDateChoice() {
        let keys=["pendingTripFields","travelPlanningTask","pendingPersonalProfile"]
        let defaults=UserDefaults.standard,prior=Dictionary(uniqueKeysWithValues:keys.map{($0,defaults.object(forKey:$0))})
        defer{for key in keys{if let value=prior[key] ?? nil{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        let store=AppStore()
        store.pendingTripFields=["notes":"A quiet day, please","date":""]
        store.travelTask=TravelPlanningTask(fields:["destination":"Sydney","days":"2","date":"2026-10-28","budget":"60"])
        store.resumePendingTrip()
        XCTAssertEqual(store.pendingTripFields["destination"],"Sydney")
        XCTAssertEqual(store.pendingTripFields["days"],"2")
        XCTAssertEqual(store.pendingTripFields["budget"],"60")
        XCTAssertEqual(store.pendingTripFields["notes"],"A quiet day, please")
        XCTAssertEqual(store.pendingTripFields["date"],"")
    }

    func testNewTripCompanionRecallAndWelcomeAreEnglish() {
        let fixtures=[
            "那这次不用努力打卡啦。我们给坐着吹风和发呆留点位置。\n我记得你确认过「轻松随意」。这次也参考它吗？想换换也可以。",
            "好呀，小馋雷达亮了！我们给好吃的留个位置，预算还是听你的。\n我记得你确认过「每天约 6000 步」。这次也参考它吗？想换换也可以。",
            "Alex，咱们先挑一种玩法：绿意慢走，还是城市故事？你也可以自己说。",
            "Alex，咱们先找一点这趟旅行的灵感。",
            "这次按每人每天 60 AUD 活动预算来安排，不含住宿、交通和全部餐饮。\n资料准备齐啦！可以直接生成行程，也可以继续聊或单独修改。",
            "这次按每人每天 0 AUD 活动预算来安排，不含住宿、交通和全部餐饮。\n这次一共几个人？记得包括自己呀。",
            "沿用这个习惯","这次想换换","先说想去哪儿","这次轻松点","这次充实点","少走些，约6000步","多逛些，约12000步"
        ]
        for source in fixtures {
            let translated=AppLanguage.english.display(source)
            XCTAssertFalse(translated.unicodeScalars.contains{(0x3400...0x9FFF).contains($0.value)},translated)
        }
        XCTAssertTrue(AppLanguage.english.display(fixtures[1]).contains("6000"))
        XCTAssertTrue(AppLanguage.english.display("小林，咱们先找一点这趟旅行的灵感。").hasPrefix("小林,"))
        XCTAssertEqual(AppLanguage.english.display("我想去一个还没取英文名的小店"),"我想去一个还没取英文名的小店")
    }

    func testContextMigrationRemovesOnlyGeneratedLinesAndKeepsUserDraft() {
        let first="关于「Test」第 1 天（2026-10-27）。我想聊聊这一天的安排。仅调整这一天，保留其余日期和已完成活动；先展示建议，确认后再保存。"
        let second=first.replacingOccurrences(of:"第 1 天",with:"第 2 天")
        XCTAssertEqual(PetComposerDraft.restored(first+"\n"+second,contextual:true),"")
        XCTAssertEqual(PetComposerDraft.restored(first+"\nI want more coffee stops.",contextual:true),"I want more coffee stops.")
        XCTAssertEqual(PetComposerDraft.restored("关于这一天，我想少走一点。",contextual:true),"关于这一天，我想少走一点。")
        XCTAssertEqual(PetComposerDraft.restored(first,contextual:false),first)
    }
    func testComposedDetailsAcknowledgementIsFullyEnglish() {
        let text=AppLanguage.english.display("目的地、日期、天数、人数、预算、出发地点、必去安排收好啦。\n资料准备齐啦！可以直接生成行程，也可以继续聊或单独修改。")
        XCTAssertEqual(text,"All set. Want to tweak anything before we plan?")
        XCTAssertFalse(text.unicodeScalars.contains{(0x3400...0x9FFF).contains($0.value)},text)
    }

    func testZeroActivityBudgetCanReachPlanningForEveryConfirmedBudgetBasis() throws {
        for basis in TravelRequirements.budgetBases.map(\.0) {
            var values=fields
            values["budget"]="0";values["budgetCurrency"]="AUD";values["budgetBasis"]=basis;values["travelers"]="2";values["budgetCoverage"]="activities"
            let profile=try TravelRequirements.readyProfile(values,personal:UserTravelProfile())
            XCTAssertEqual(profile.budget,0,basis);XCTAssertEqual(profile.budgetAmount,0,basis)
            XCTAssertEqual(profile.budgetBasis,basis);XCTAssertTrue(TravelRequirements.missingFields(values).isEmpty)
            values["budget"]="-0.01"
            XCTAssertThrowsError(try TravelRequirements.readyProfile(values,personal:UserTravelProfile()),basis)
        }
    }
    func testSavedTripConversationKeepsKnownFactsAndNormalizedBudget() {
        var p=TripProfile();p.days=5;p.travelers=2;p.budget=80;p.budgetAmount=800;p.budgetBasis="groupTrip";p.origin="中央车站";p.mustDo="悉尼歌剧院";p.avoid="购物";p.transportPreference="lessWalking"
        let fields=TripConversationSeed.fields(p)
        XCTAssertEqual(fields["days"],"5");XCTAssertEqual(fields["travelers"],"2");XCTAssertEqual(fields["origin"],"中央车站");XCTAssertEqual(fields["mustDo"],"悉尼歌剧院");XCTAssertEqual(fields["avoid"],"购物")
        XCTAssertEqual(fields["budget"],"80.0");XCTAssertEqual(fields["budgetBasis"],"perPersonPerDay");XCTAssertEqual(fields["transportPreference"],"lessWalking");XCTAssertTrue(TravelRequirements.missingFields(fields).isEmpty)
    }
    func testChatShortcutsExposeOnlyMissingGroupsAndReadyEditsDirectly() {
        let initial=TripPlanningShortcuts.items([:],ready:false);XCTAssertEqual(initial.map(\.stage),[0,1])
        let partial=["destination":"悉尼","date":"2026-10-28","days":"3"]
        XCTAssertEqual(TripPlanningShortcuts.items(partial,ready:false).map(\.stage),[2,3])
        XCTAssertEqual(TripPlanningShortcuts.items(partial,ready:true).map(\.stage),[1,3,2])
    }
    func testAdjustmentUsesLatestUserIntentRatherThanOldProfileSummary() {
        let messages=[PetMessage(id:"1",role:"user",text:"有点累，今天接下来少走些，明天不改"),PetMessage(id:"2",role:"assistant",text:"我会调整剩余安排"),PetMessage(id:"3",role:"user",text:"好的")]
        XCTAssertEqual(TripAdjustmentRequest.make(messages:messages,fallback:"旧行程资料"),messages[0].text)
        let large=[PetMessage(id:"4",role:"user",text:String(repeating:"🐨",count:2000))]
        XCTAssertLessThanOrEqual(TripAdjustmentRequest.make(messages:large,fallback:"").utf16.count,1400)
    }
    func testExpenseMigrationPreservesLegacyRecordsAndTripAssociation() throws {
        let legacy=Data("{\"id\":\"12345678-1234-1234-1234-123456789012\",\"title\":\"午餐\",\"cents\":1001,\"participants\":2}".utf8)
        let old=try JSONDecoder().decode(Expense.self,from:legacy)
        XCTAssertNil(old.tripID);XCTAssertEqual(old.shares,[501,500])
        let current=Expense(title:"门票",cents:2400,participants:2,tripID:"trip-a")
        let restored=try JSONDecoder().decode(Expense.self,from:JSONEncoder().encode(current))
        XCTAssertEqual(restored.tripID,"trip-a");XCTAssertEqual(restored.cents,2400)
    }

    func testHalfDayChoiceChangesOnlyTemporaryTimeWindowAndBudgetKeepsBasis() throws {
        let personal=UserTravelProfile(dailySteps:5000,rhythm:"轻松随意",interests:["自然"])
        var fields=["destination":"悉尼","date":"2026-10-25","days":"3","travelers":"2","budget":"600","budgetCurrency":"AUD","budgetBasis":"groupTrip","dayStyle":"halfDay"]
        let half=try TravelRequirements.readyProfile(fields,personal:personal)
        XCTAssertEqual(half.endMinute,900);XCTAssertEqual(half.budget,100)
        XCTAssertTrue(half.activityBudgetSummary.contains("全体全程"));XCTAssertTrue(half.activityBudgetSummary.contains("600"))
        fields["dayStyle"]="fullDay"
        XCTAssertEqual(try TravelRequirements.readyProfile(fields,personal:personal).endMinute,1200)
        XCTAssertEqual(personal.dailySteps,5000)
    }

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
        let keys=["pendingTripFields","onboarded","travelPlanningTask","pendingPersonalProfile"]
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
        let originalTask=UserDefaults.standard.data(forKey:"travelPlanningTask")
        defer{store.pendingTripFields=original;if let originalTask{UserDefaults.standard.set(originalTask,forKey:"travelPlanningTask")}else{UserDefaults.standard.removeObject(forKey:"travelPlanningTask")}}
        var complete=fields;complete["budgetCurrency"]="AUD";complete["budgetBasis"]="perPersonPerDay";complete["travelers"]="2"
        store.pendingTripFields=complete
        var paths:[String]=[]
        store.loader={request in paths.append(request.url!.path);try await Task.sleep(nanoseconds:5_000_000_000);throw URLError(.badServerResponse)}
        store.startGeneratingPendingTrip()
        for _ in 0..<100{if store.isGeneratingTrip{break};try await Task.sleep(nanoseconds:1_000_000)}
        XCTAssertFalse(store.busy);XCTAssertTrue(store.isGeneratingTrip);XCTAssertTrue(store.canCancelPlanning)
        store.cancelPendingGeneration()
        for _ in 0..<100{if !store.isGeneratingTrip{break};try await Task.sleep(nanoseconds:1_000_000)}
        XCTAssertFalse(store.busy);XCTAssertFalse(store.isGeneratingTrip);XCTAssertFalse(store.canCancelPlanning)
        XCTAssertEqual(store.pendingTripFields,complete);XCTAssertNil(store.errorMessage)
        XCTAssertEqual(paths,["/catalog"]);XCTAssertTrue(store.saved.isEmpty)
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
    func testRecalledWalkingLimitSurvivesTheRequirementsReviewInput() throws {
        let raw=["destination":"悉尼","date":"2026-11-01","days":"2","budget":"100","budgetCurrency":"AUD","budgetBasis":"perPersonPerDay","travelers":"1"]
        var effective=raw;effective["pace"]="约5000步";effective["notes"]="怕晒，需要安排阴凉的休息点"
        let session=PetSession(id:"test",messages:[],fields:raw,pendingMemory:"",nextField:"",question:"",options:[],completed:7,total:7,planningFields:effective)
        let reviewed=try TravelRequirements.readyProfile(session.effectiveFields,personal:UserTravelProfile())
        XCTAssertEqual(reviewed.maxSteps,5000)
        XCTAssertTrue(reviewed.request?.contains("阴凉")==true)
        XCTAssertNil(session.fields["pace"])
    }

    func testExistingTripEditsNormalizeGroupBudgetAndPreserveAnchors() throws {
        var base=TripProfile();base.days=2;base.startMinute=660;base.endMinute=1020;base.start = .central;base.end = .airport
        var fields=TripConversationSeed.fields(base)
        fields["travelers"]="3";fields["budget"]="600";fields["budgetBasis"]="groupTrip";fields["transportPreference"]="lessWalking"
        let edited=try TripConversationSeed.profile(fields,base:base)
        XCTAssertEqual(edited.budget,100);XCTAssertEqual(edited.travelers,3)
        XCTAssertEqual(edited.budgetAmount,600);XCTAssertEqual(edited.transportPreference,"lessWalking")
        XCTAssertEqual(edited.start.name,base.start.name);XCTAssertEqual(edited.end.name,base.end.name)
        XCTAssertEqual(edited.startMinute,660);XCTAssertEqual(edited.endMinute,1020)
        fields["origin"]="另一家酒店"
        XCTAssertThrowsError(try TripConversationSeed.profile(fields,base:base))
    }

    func testTimeAdjustmentNeedsAnExplicitEndAndRejectsBeforeDeparture() throws {
        let base=TripProfile();var fields=TripConversationSeed.fields(base)
        fields["adjustmentNeed"]="endTime"
        XCTAssertThrowsError(try TripConversationSeed.profile(fields,base:base))
        fields.removeValue(forKey:"adjustmentNeed");fields["adjustmentEndMinute"]="840"
        XCTAssertEqual(try TripConversationSeed.profile(fields,base:base).endMinute,840)
        fields["adjustmentEndMinute"]="540"
        XCTAssertThrowsError(try TripConversationSeed.profile(fields,base:base))
    }

    func testBookedHotelHintsRequireActualBookingAndPreserveFixedEndpoints() {
        XCTAssertEqual(BookingTextHints.bookedHotel("已訂 Hilton Sydney"),"Hilton Sydney")
        XCTAssertEqual(BookingTextHints.bookedHotel("Booked hotel at Hilton Sydney"),"Hilton Sydney")
        XCTAssertNil(BookingTextHints.bookedHotel("還沒訂 Hilton Sydney"))
        XCTAssertNil(BookingTextHints.bookedHotel("booked train to Sydney"))
        XCTAssertTrue(BookingTextHints.mayUseHotel(.quay,explicit:nil))
        XCTAssertFalse(BookingTextHints.mayUseHotel(.airport,explicit:nil))
        XCTAssertFalse(BookingTextHints.mayUseHotel(.quay,explicit:true))
    }
    func testLuggageProfileRoundTripPreservesEvidenceAndDate() throws {
        var profile=TripProfile();profile.heavyLuggage=true;profile.luggageStored=true
        profile.luggageStoredDate="2026-10-28";profile.luggageDays=["2026-10-28"]
        profile.luggageStorage=LuggageStoragePoint(name:"Hotel storage",lat:-33.86,lng:151.21,storageConfirmed:false,openMinute:480,closeMinute:1200)
        let saved=try JSONDecoder().decode(TripProfile.self,from:JSONEncoder().encode(profile))
        XCTAssertEqual(saved.luggageStorage?.storageConfirmed,false)
        XCTAssertEqual(saved.luggageStorage?.openMinute,480)
        XCTAssertEqual(saved.luggageStoredDate,"2026-10-28")
        XCTAssertEqual(saved.luggageDays,["2026-10-28"])
    }

}
