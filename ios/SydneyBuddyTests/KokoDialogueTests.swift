import XCTest
@testable import SydneyBuddy

final class KokoDialogueTests:XCTestCase {
    func testGuidedDetailsResumesAtMissingGroupWithoutMutatingDraft() {
        var fields=["destination":"悉尼","date":"2026-10-25","days":"15"]
        XCTAssertEqual(GuidedTripDetails.firstMissing(fields),2)
        fields["travelers"]="2"
        XCTAssertEqual(GuidedTripDetails.firstMissing(fields),3)
        fields["days"]="16"
        XCTAssertEqual(GuidedTripDetails.firstMissing(fields),1)
        fields["days"]="2";fields["date"]="2026-02-30"
        XCTAssertEqual(GuidedTripDetails.firstMissing(fields),1)
        fields["destination"]="未确定"
        XCTAssertEqual(GuidedTripDetails.firstMissing(fields),0)
        XCTAssertNil(fields["budget"])
        XCTAssertEqual(GuidedTripDetails.correctionStage(["destination":"悉尼","budgetCurrency":"CNY"]),3)
        XCTAssertEqual(GuidedTripDetails.correctionStage(["destination":"悉尼","budgetCoverage":"totalTravel"]),3)
        XCTAssertEqual(GuidedTripDetails.correctionStage(["destination":"墨尔本","budgetCurrency":"CNY"]),0)
    }
    private func session(inspiration:Bool=false)->PetSession {
        let reply=PetQuickReply(id:"route-choice",text:"顺路再去",intent:"respond")
        let message=PetMessage(id:"scene-1",role:"assistant",text:"如果路线上有合适的展览，咱们顺路看看？",quickReplies:[reply])
        var result=PetSession(id:"journey",messages:[message],fields:["budget":"120"],pendingMemory:"",nextField:"destination",question:"目的地",options:[],completed:1,total:7)
        result.dialogue=ProfilingDialogueState(version:2,responseCount:inspiration ? 4:2,phase:inspiration ? "inspiration":"exploring",nickname:"小鱼",ideas:["如果有合适的展览，顺路看看。"],inspiration:inspiration ? TravelInspiration(title:"目的地待定 · 旅行灵感",ideas:["如果有合适的展览，顺路看看。"],status:"inspiration",placesVerified:false):nil)
        return result
    }
    func testV2DraftRestoresContextualChoicesWithoutMakingUnknownTravelDataReady() throws {
        let original=session(inspiration:true)
        let restored=try JSONDecoder().decode(PetSession.self,from:JSONEncoder().encode(original))
        XCTAssertEqual(restored.dialogue?.responseCount,4)
        XCTAssertEqual(restored.contextualReplies.first?.id,"route-choice")
        XCTAssertEqual(restored.replyQuestion?.id,"scene-1")
        XCTAssertEqual(restored.dialogue?.inspiration?.placesVerified,false)
        XCTAssertThrowsError(try TravelRequirements.readyProfile(restored.effectiveFields,personal:UserTravelProfile()))
        XCTAssertNil(restored.fields["destination"]);XCTAssertNil(restored.fields["date"])
    }
    @MainActor func testSavedDraftIsUpgradedInPlaceWithNoNewSessionOrRepeatedNameRequest() async throws {
        let defaults=UserDefaults.standard
        let keys=["petSession.koko-upgrade-test","koko.nickname","koko.hasMet"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for(key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        defaults.set("journey",forKey:keys[0]);defaults.set("小鱼",forKey:"koko.nickname");defaults.set(true,forKey:"koko.hasMet")
        let chat=PetChatStore();let upgraded=session();var old=upgraded;old.dialogue=nil
        var methods:[String]=[];var patch:[String:Any]=[:]
        chat.loader={request in
            methods.append(request.httpMethod!)
            if request.httpMethod=="PATCH"{patch=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]}
            let value=request.httpMethod=="PATCH" ? upgraded:old
            return (try JSONEncoder().encode(value),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await chat.load(scope:"koko-upgrade-test",profiling:true)
        XCTAssertNil(chat.error);XCTAssertEqual(methods,["GET","PATCH"])
        XCTAssertEqual(patch["profilingVersion"] as? Int,2);XCTAssertEqual(patch["nickname"] as? String,"小鱼")
        XCTAssertEqual(chat.session?.fields["budget"],"120");XCTAssertNil(patch["fields"])
        XCTAssertEqual(chat.session?.dialogue?.responseCount,2)
    }
    @MainActor func testNewTripReusesOnlyExplicitNicknameWithoutSeedingSimulationPreferences() async throws {
        let defaults=UserDefaults.standard
        let keys=["petSession.koko-new-test","koko.nickname","koko.hasMet"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for(key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        defaults.removeObject(forKey:keys[0]);defaults.set("小鱼",forKey:"koko.nickname");defaults.set(true,forKey:"koko.hasMet")
        let chat=PetChatStore();var bodies:[[String:Any]]=[];let value=session()
        chat.loader={request in
            bodies.append(try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any])
            return (try JSONEncoder().encode(value),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await chat.load(scope:"koko-new-test",profiling:true)
        XCTAssertNil(chat.error);XCTAssertEqual(bodies.count,1)
        XCTAssertEqual(bodies[0]["nickname"] as? String,"小鱼");XCTAssertEqual(bodies[0]["hasMetKoko"] as? Bool,true)
        XCTAssertEqual(bodies[0]["profilingVersion"] as? Int,2)
        XCTAssertTrue((bodies[0]["fields"] as! [String:String]).isEmpty)
        XCTAssertNil(bodies[0]["travelDna"]);XCTAssertNil(bodies[0]["consent"])
    }
    func testChoiceProvenanceSurvivesPreferenceEncoding() throws {
        let json=#"{"id":"claim","key":"interests","dimension":"interests","value":"顺路看看展览","condition":"如果路线上有展览","scope":"activity","source":"contextual_choice","status":"trip_only","confidence":0.9,"evidence":"顺路再去","sourceMessageId":"u1","memoryId":"","conflictingMemoryIds":[],"previousValues":[],"choice":{"questionMessageId":"scene-1","questionText":"如果路线上有展览，咱们看看？","optionId":"route-choice","optionText":"顺路再去"}}"#
        let preference=try JSONDecoder().decode(TravelPreference.self,from:Data(json.utf8))
        let restored=try JSONDecoder().decode(TravelPreference.self,from:JSONEncoder().encode(preference))
        XCTAssertEqual(restored.choice?.questionMessageId,"scene-1");XCTAssertEqual(restored.choice?.optionId,"route-choice")
        XCTAssertEqual(restored.scope,"activity");XCTAssertEqual(restored.status,"trip_only")
    }
}
