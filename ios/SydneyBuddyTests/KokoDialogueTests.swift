import XCTest
@testable import SydneyBuddy

final class KokoDialogueTests:XCTestCase {
    @MainActor func testRequestsRespectInterfaceLanguageAndKeepTypedFacts() async throws {
        let defaults=UserDefaults.standard
        let keys=["appLanguage","petSession.english-integration-test"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for(key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        for language in ["en","zh-Hant"] {
            defaults.set(language,forKey:"appLanguage");defaults.removeObject(forKey:keys[1])
            let chat=PetChatStore();let value=session();var bodies:[[String:Any]]=[];var headers:[String?]=[]
            chat.loader={request in
                headers.append(request.value(forHTTPHeaderField:"X-App-Language"))
                if let data=request.httpBody{bodies.append(try JSONSerialization.jsonObject(with:data) as! [String:Any])}
                return(try JSONEncoder().encode(value),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
            }
            let typed="我想慢慢走，保留这句原话"
            await chat.load(scope:"english-integration-test",seed:["notes":typed],profiling:true)
            await chat.update(["notes":typed])
            XCTAssertNil(chat.error);XCTAssertEqual(bodies.count,2)
            XCTAssertTrue(headers.allSatisfy{$0==language})
            for body in bodies {XCTAssertEqual(body["language"] as? String,language);XCTAssertEqual((body["fields"] as? [String:String])?["notes"],typed)}
        }
    }

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
    func testInspirationIsHiddenDuringOrdinaryTripQuestions() {
        var value=session(inspiration:true);value.dialogue?.proposedDirection="nature"
        XCTAssertFalse(KokoInspirationPresentation.shouldShow(value))
        value.messages.append(PetMessage(id:"idea-request",role:"user",text:"Any ideas for this trip?"))
        XCTAssertTrue(KokoInspirationPresentation.shouldShow(value))
        value.dialogue?.selectedDirection="nature"
        XCTAssertFalse(KokoInspirationPresentation.shouldShow(value))
        value.dialogue?.proposedDirection="unknown"
        XCTAssertNil(KokoInspirationPresentation.direction(value))
        XCTAssertFalse(KokoInspirationPresentation.shouldShow(value))
    }
    @MainActor func testInspirationSelectionUpdatesTripWithoutMemoryConsent() async throws {
        let key="petSession.inspiration-isolation-test";let prior=UserDefaults.standard.object(forKey:key)
        defer{if let prior{UserDefaults.standard.set(prior,forKey:key)}else{UserDefaults.standard.removeObject(forKey:key)}}
        UserDefaults.standard.removeObject(forKey:key)
        let chat=PetChatStore();let value=session(inspiration:true);var requests:[URLRequest]=[]
        chat.loader={request in requests.append(request);return(try JSONEncoder().encode(value),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        await chat.load(scope:"inspiration-isolation-test",profiling:true)
        requests=[]
        await chat.update(["interests":KokoInspirationPresentation.interests("nature")])
        XCTAssertNil(chat.error);XCTAssertEqual(requests.count,1)
        let request=try XCTUnwrap(requests.first)
        XCTAssertEqual(request.httpMethod,"PATCH");XCTAssertFalse(request.url!.path.contains("memory"))
        let body=try XCTUnwrap(JSONSerialization.jsonObject(with:request.httpBody!) as? [String:Any])
        XCTAssertEqual((body["fields"] as? [String:String])?["interests"],"自然、风景")
        XCTAssertNil(body["consent"]);XCTAssertNil(body["travelDna"])
    }
    private func session(inspiration:Bool=false)->PetSession {
        let reply=PetQuickReply(id:"route-choice",text:"顺路再去",intent:"respond")
        let message=PetMessage(id:"scene-1",role:"assistant",text:"如果路线上有合适的展览，咱们顺路看看？",quickReplies:[reply])
        var result=PetSession(id:"journey",messages:[message],fields:["budget":"120"],pendingMemory:"",nextField:"destination",question:"目的地",options:[],completed:1,total:7)
        result.dialogue=ProfilingDialogueState(version:2,responseCount:inspiration ? 4:2,phase:inspiration ? "inspiration":"exploring",nickname:"小鱼",ideas:["如果有合适的展览，顺路看看。"],inspiration:inspiration ? TravelInspiration(title:"目的地待定 · 旅行灵感",ideas:["如果有合适的展览，顺路看看。"],status:"inspiration",placesVerified:false):nil)
        return result
    }
    @MainActor func testPersonalConversationHasSeparateScopeAndNoTripDefaults() async throws {
        let defaults=UserDefaults.standard;let key="petSession.personalProfile";let prior=defaults.object(forKey:key)
        defer{if let prior{defaults.set(prior,forKey:key)}else{defaults.removeObject(forKey:key)}};defaults.removeObject(forKey:key)
        let chat=PetChatStore();var body:[String:Any]=[:];let value=session()
        chat.loader={request in body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];return(try JSONEncoder().encode(value),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        await chat.load(scope:"personalProfile",profiling:true)
        XCTAssertNil(chat.error);XCTAssertEqual(body["companionVersion"] as? Int,0)
        XCTAssertTrue((body["fields"] as! [String:String]).isEmpty)
        XCTAssertNil(body["consent"]);XCTAssertNil(body["travelDna"])
    }
    @MainActor func testPersonalAgentStartsWithThreeChoicesAndResumesWithoutASecondOpening() async throws {
        let defaults=UserDefaults.standard;let key="petSession.personalProfileAgent";let prior=defaults.object(forKey:key)
        defer{if let prior{defaults.set(prior,forKey:key)}else{defaults.removeObject(forKey:key)}};defaults.removeObject(forKey:key)
        var value=session();value.fields=[:];value.planningFields=[:];value.nextField="";value.purpose="personal_profile"
        value.dialogue=ProfilingDialogueState(version:3,responseCount:0,phase:"personal_profile",nickname:"",ideas:[],inspiration:nil)
        value.messages=[PetMessage(id:"personal-opening",role:"assistant",text:"What do you usually enjoy?",quickReplies:[
            PetQuickReply(id:"personal-1",text:"Quiet places",intent:"respond"),
            PetQuickReply(id:"personal-2",text:"Something active",intent:"respond"),
            PetQuickReply(id:"personal-3",text:"It depends",intent:"respond")])]
        let chat=PetChatStore();var created:[[String:Any]]=[]
        chat.loader={request in
            if request.httpMethod=="POST"{created.append(try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any])}
            let data=request.url!.path.hasSuffix("/memories") ? Data("[]".utf8):try JSONEncoder().encode(value)
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await chat.load(scope:"personalProfileAgent",profiling:true)
        XCTAssertNil(chat.error);XCTAssertEqual(created.count,1)
        XCTAssertEqual(created[0]["purpose"] as? String,"personal_profile");XCTAssertEqual(created[0]["profilingVersion"] as? Int,3)
        XCTAssertTrue((created[0]["fields"] as! [String:String]).isEmpty)
        XCTAssertEqual(chat.session?.contextualReplies.map(\.id),["personal-1","personal-2","personal-3"])
        await chat.reloadSession()
        XCTAssertNil(chat.error);XCTAssertEqual(created.count,1)
        XCTAssertEqual(chat.session?.contextualReplies.map(\.id),["personal-1","personal-2","personal-3"])
    }
    @MainActor func testPersonalAgentDoesNotResumeAnOldTripConversation() async throws {
        let defaults=UserDefaults.standard;let key="petSession.personalProfileAgent";let prior=defaults.object(forKey:key)
        defer{if let prior{defaults.set(prior,forKey:key)}else{defaults.removeObject(forKey:key)}}
        defaults.set("legacy-session",forKey:key)
        var legacy=session();legacy.id="legacy-session";legacy.purpose=nil
        var fresh=session();fresh.id="personal-session";fresh.purpose="personal_profile"
        let chat=PetChatStore();var creates=0;var patches=0
        chat.loader={request in
            if request.httpMethod=="POST"{creates+=1;let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];XCTAssertEqual(body["purpose"] as? String,"personal_profile");XCTAssertNil(body["fields"])}
            if request.httpMethod=="PATCH"{patches+=1}
            let data=request.url!.path.hasSuffix("/memories") ? Data("[]".utf8):try JSONEncoder().encode(request.httpMethod=="GET" ? legacy:fresh)
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await chat.load(scope:"personalProfileAgent",profiling:true)
        XCTAssertNil(chat.error);XCTAssertEqual(chat.session?.id,"personal-session");XCTAssertEqual(creates,1);XCTAssertEqual(patches,0)
        XCTAssertEqual(defaults.string(forKey:key),"personal-session")
    }
    @MainActor func testResumeDraftRestoresOnlyMissingFieldsAndKeepsChatEdits() async throws {
        let defaults=UserDefaults.standard;let key="petSession.draft";let prior=defaults.object(forKey:key)
        defer{if let prior{defaults.set(prior,forKey:key)}else{defaults.removeObject(forKey:key)}}
        defaults.set("resume-session",forKey:key)
        var value=session();value.id="resume-session";value.fields=["destination":"Sydney","budget":"80"];value.planningFields=value.fields
        value.dialogue=ProfilingDialogueState(version:2,responseCount:1,phase:"collect",nickname:"",ideas:[],inspiration:nil);value.dialogue?.companionVersion=1
        let chat=PetChatStore();var patches:[[String:Any]]=[]
        chat.loader={request in
            if request.httpMethod=="PATCH"{let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];patches.append(body);if let fields=body["fields"] as? [String:String]{value.fields.merge(fields){_,new in new};value.planningFields=value.fields}}
            return(try JSONEncoder().encode(value),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await chat.load(scope:"draft",seed:["destination":"Sydney","days":"2","budget":"40"],profiling:true)
        XCTAssertNil(chat.error);XCTAssertEqual(patches.count,1)
        XCTAssertEqual(patches.first?["fields"] as? [String:String],["days":"2"])
        XCTAssertEqual(chat.session?.effectiveFields["budget"],"80");XCTAssertEqual(chat.session?.effectiveFields["days"],"2")
    }
    @MainActor func testNetworkFailureUsesEnglishAndKeepsExistingSessionForRetry() async {
        let defaults=UserDefaults.standard,key="petSession.network-failure-test"
        let previous=defaults.object(forKey:key),language=defaults.object(forKey:"appLanguage")
        defer{for (name,value) in [(key,previous),("appLanguage",language)]{if let value{defaults.set(value,forKey:name)}else{defaults.removeObject(forKey:name)}}}
        defaults.set("existing-session",forKey:key);defaults.set("en",forKey:"appLanguage")
        for code in [NSURLErrorTimedOut,NSURLErrorNotConnectedToInternet] {
            let chat=PetChatStore();var requests=0
            chat.loader={_ in requests+=1;throw NSError(domain:NSURLErrorDomain,code:code,userInfo:[NSLocalizedDescriptionKey:"请求超时。"])}
            await chat.load(scope:"network-failure-test")
            XCTAssertEqual(requests,1);XCTAssertFalse(chat.busy)
            XCTAssertEqual(defaults.string(forKey:key),"existing-session")
            XCTAssertEqual(chat.error,code == NSURLErrorTimedOut ? "Koko took too long to reply. Please try again.":"I couldn’t reach Koko. Check your connection and try again.")
        }
    }

    @MainActor func testMissingCloudSessionRecoversDraftButAuthenticationAndServerErrorsDoNotResetIt() async throws {
        for status in [400,404,422,401,500] {
            let scope="backend-recovery-test",key="petSession."+scope,archive=key+".previousIDs"
            let defaults=UserDefaults.standard,prior=defaults.object(forKey:key),priorArchive=defaults.object(forKey:archive)
            defer{for (name,value) in [(key,prior),(archive,priorArchive)]{if let value{defaults.set(value,forKey:name)}else{defaults.removeObject(forKey:name)}}}
            defaults.set("local-session",forKey:key)
            let chat=PetChatStore();var creates=0;var value=session();value.id="cloud-session";value.dialogue?.companionVersion=1
            value.fields=["destination":"Sydney","days":"2"];value.planningFields=value.fields
            chat.loader={request in
                if request.httpMethod=="GET"{return(Data("{\"error\":\"This conversation has been cleared. Start a new one.\"}".utf8),HTTPURLResponse(url:request.url!,statusCode:status,httpVersion:nil,headerFields:nil)!)}
                creates+=1
                let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
                XCTAssertEqual(body["fields"] as? [String:String],["destination":"Sydney","days":"2"])
                return(try JSONEncoder().encode(value),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
            }
            await chat.load(scope:scope,seed:["destination":"Sydney","days":"2"],profiling:true)
            if status==400 || status==404 || status==422{XCTAssertNil(chat.error);XCTAssertEqual(creates,1);XCTAssertEqual(chat.session?.id,"cloud-session");XCTAssertTrue(defaults.stringArray(forKey:archive)?.contains("local-session")==true)}
            else{XCTAssertNotNil(chat.error);XCTAssertEqual(creates,0);XCTAssertEqual(defaults.string(forKey:key),"local-session")}
        }
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
