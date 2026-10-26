import XCTest
import SwiftUI
import Security
@testable import SydneyBuddy

private final class JourneyStub:URLProtocol {
    static var responseData=Data()
    static var status=200
    static var lastRequest:URLRequest?
    override class func canInit(with request:URLRequest)->Bool {true}
    override class func canonicalRequest(for request:URLRequest)->URLRequest {request}
    override func startLoading(){
        Self.lastRequest=request
        client?.urlProtocol(self,didReceive:HTTPURLResponse(url:request.url!,statusCode:Self.status,httpVersion:nil,headerFields:["Content-Type":"application/json"])!,cacheStoragePolicy:.notAllowed)
        client?.urlProtocol(self,didLoad:Self.responseData);client?.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading(){}
}
@MainActor final class JourneyTests:XCTestCase {
    func testCloudAccessCodeOnlyGoesToConfiguredHTTPSOrigin() throws {
        let prefs=UserDefaults.standard;let previous=prefs.object(forKey:"cloudBackendURL");let previousMode=prefs.object(forKey:"backendMode")
        let base=URL(string:"https://backend-test.example.invalid")!;let code="synthetic-test-access-code-123456789"
        defer{if let previous{prefs.set(previous,forKey:"cloudBackendURL")}else{prefs.removeObject(forKey:"cloudBackendURL")};if let previousMode{prefs.set(previousMode,forKey:"backendMode")}else{prefs.removeObject(forKey:"backendMode")};SecItemDelete([kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:"SydneyBuddy.backend",kSecAttrAccount as String:base.absoluteString] as CFDictionary)}
        try BackendConnection.saveAccessCode(code,for:base);prefs.set(base.absoluteString,forKey:"cloudBackendURL");prefs.set("cloud",forKey:"backendMode")
        XCTAssertEqual(BackendConnection.authorized(URLRequest(url:base.appendingPathComponent("health"))).value(forHTTPHeaderField:"Authorization"),"Bearer "+code)
        XCTAssertNil(BackendConnection.authorized(URLRequest(url:URL(string:"https://third-party.example.invalid")!)).value(forHTTPHeaderField:"Authorization"))
        XCTAssertNil(BackendConnection.authorized(URLRequest(url:URL(string:"http://backend-test.example.invalid")!)).value(forHTTPHeaderField:"Authorization"))
    }
    func testBackendURLRejectsCredentialsAndPublicHTTP() {
        XCTAssertNil(BackendConnection.validated("http://203.0.113.1:8787"))
        XCTAssertNil(BackendConnection.validated("https://user:password@example.com"))
        XCTAssertNil(BackendConnection.validated("https://example.com?token=secret"))
        XCTAssertEqual(BackendConnection.validated("http://127.0.0.1:8790")?.port,8790)
        XCTAssertNotNil(BackendConnection.validated("https://api.example.com"))
    }
    func testBackendSwitchDiscardsAnEarlierResponse() async throws {
        let prefs=UserDefaults.standard
        let keys=["backendMode","localBackendURL","cloudBackendURL"]
        let previous=keys.map{prefs.object(forKey:$0)}
        defer {for (key,value) in zip(keys,previous){if let value{prefs.set(value,forKey:key)}else{prefs.removeObject(forKey:key)}}}
        prefs.set("local",forKey:"backendMode");prefs.set("http://127.0.0.1:8787",forKey:"localBackendURL")
        let store=AppStore()
        store.loader={req in
            prefs.set("cloud",forKey:"backendMode");prefs.set("http://127.0.0.1:8790",forKey:"cloudBackendURL")
            return (Data("[]".utf8),HTTPURLResponse(url:req.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        do{let _: [Trip]=try await store.api("trips");XCTFail("An old backend response must not be accepted")}
        catch is CancellationError {}
        XCTAssertEqual(store.base.port,8790)
    }
    func testBackendSwitchDiscardsAnEarlierNetworkFailure() async throws {
        let prefs=UserDefaults.standard;let keys=["backendMode","localBackendURL","cloudBackendURL"];let previous=keys.map{prefs.object(forKey:$0)}
        defer {for (key,value) in zip(keys,previous){if let value{prefs.set(value,forKey:key)}else{prefs.removeObject(forKey:key)}}}
        prefs.set("local",forKey:"backendMode");prefs.set("http://127.0.0.1:8787",forKey:"localBackendURL")
        let store=AppStore()
        store.loader={_ in prefs.set("cloud",forKey:"backendMode");prefs.set("http://127.0.0.1:8790",forKey:"cloudBackendURL");throw URLError(.timedOut)}
        do{let _: [Trip]=try await store.api("trips");XCTFail("An old backend error must not replace the new connection state")}
        catch is CancellationError {}
    }
    func testCloudPlanningUsesWarehouseAndKeepsPriceEvidence() async throws {
        let store=AppStore();var profile=TripProfile();profile.interests=["美食"]
        store.planningDiscovery={_ in XCTFail("Cloud planning must not use Apple place discovery");return []}
        var calls=0
        store.loader={request in
            calls+=1
            XCTAssertEqual(request.url?.lastPathComponent,"search")
            XCTAssertEqual(request.value(forHTTPHeaderField:"X-Travel-Data-Source"),"zetaris-cloud")
            let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            let food=body["category"] as? String == "restaurants"
            let point:[String:Any]=["id":food ? "osm:meal":"osm:museum","name":food ? "Real Restaurant":"Real Museum","category":food ? "美食":"文化","indoor":true,"visitMinutes":food ? 45:75,"cost":food ? 65:12,"costKnown":false,"lat":-33.86,"lng":151.21,"reason":"test","source":"OpenStreetMap / Zetaris","poiKind":food ? "restaurant":"museum"]
            return(try JSONSerialization.data(withJSONObject:["dataSource":"zetaris-cloud","places":[point]]),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let result=try await store.discoverPlanningCandidates(profile,source:.zetarisCloud)
        XCTAssertGreaterThan(calls,0);XCTAssertEqual(Set(result.map(\.id)),Set(["osm:meal","osm:museum"]))
        XCTAssertEqual(result.first(where:{$0.id=="osm:meal"})?.cost,65)
        XCTAssertEqual(result.first(where:{$0.id=="osm:meal"})?.activityType,"meal")
        XCTAssertEqual(result.first(where:{$0.id=="osm:meal"})?.source,"OpenStreetMap / Zetaris")
    }
    func testCloudPlanningRejectsWrongSourceWithoutAppleFallback() async throws {
        let store=AppStore();var profile=TripProfile();profile.interests=["文化"]
        store.planningDiscovery={_ in XCTFail("No silent Apple fallback");return []}
        store.loader={request in (Data(#"{"dataSource":"direct","places":[]}"#.utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        do{_ = try await store.discoverPlanningCandidates(profile,source:.zetarisCloud);XCTFail("Wrong source accepted")}
        catch{XCTAssertTrue(error.localizedDescription.contains("来源不一致"))}
    }
    func testAdjustmentComparisonIgnoresMetadataButDetectsTimeAndRouteChanges() throws {
        let original=try JSONDecoder().decode(Trip.self,from:Data(contentsOf:Bundle(for:Self.self).url(forResource:"schedule-trip-test",withExtension:"json")!))
        var same=original;same.explanation="重新核对成功"
        XCTAssertFalse(TripAdjustmentComparison.hasVisibleChanges(original,same))
        var changed=original;changed.days[0].finishTime="23:59"
        XCTAssertTrue(TripAdjustmentComparison.hasVisibleChanges(original,changed))
        changed=original;changed.days[0].estimatedSteps+=100
        XCTAssertTrue(TripAdjustmentComparison.hasVisibleChanges(original,changed))
        changed=original;changed.profile.budget+=10
        XCTAssertTrue(TripAdjustmentComparison.hasVisibleChanges(original,changed))
        XCTAssertFalse(TripAdjustmentComparison.hasVisibleChanges(original,changed,includeConstraints:false))
    }
    func testEmptyKeywordSearchStillUsesRealNearbyMuseum() async throws {
        let data=Data(#"{"id":"apple:museum","name":"Real Nearby Museum","category":"文化","indoor":false,"visitMinutes":60,"cost":0,"lat":-33.86,"lng":151.21,"reason":"test","source":"Apple Maps","poiKind":"attraction"}"#.utf8)
        let museum=try JSONDecoder().decode(ScheduleCandidate.self,from:data)
        var profile=TripProfile();profile.interests=["文化"]
        var queries:[String]=[]
        let results=try await ApplePlanningDiscovery.search(profile,lookup:{_,_,query in queries.append(query);return query.isEmpty ? [museum]:[]})
        XCTAssertEqual(queries,["museums",""]);XCTAssertEqual(results.map(\.id),[museum.id]);XCTAssertEqual(results.first?.category,"文化")
    }
    func testNamedRestaurantKeepsMealClassificationFromAllSearch() throws {
        let data=Data(#"{"id":"apple:test","name":"Named Restaurant","category":"美食","indoor":false,"visitMinutes":60,"cost":0,"lat":-33.86,"lng":151.21,"reason":"test","source":"Apple Maps","poiKind":"restaurant"}"#.utf8)
        let result=ApplePlanningDiscovery.planningCandidate(try JSONDecoder().decode(ScheduleCandidate.self,from:data))
        XCTAssertEqual(result.activityType,"meal");XCTAssertEqual(result.cost,30);XCTAssertEqual(result.visitMinutes,45)
        var pontoon=result;pontoon.name="Eastern Pontoon Circular Quay";pontoon.category="风景";pontoon.poiKind="attraction"
        XCTAssertTrue(ApplePlanningDiscovery.isTransportAccess(pontoon));pontoon.name="Sydney Opera House";XCTAssertFalse(ApplePlanningDiscovery.isTransportAccess(pontoon))
        var restaurant=result;restaurant.name="Wharf Restaurant";XCTAssertFalse(ApplePlanningDiscovery.isTransportAccess(restaurant))
    }
    func testLateDepartureRejectsSavedTransitAndFreshLookupUsesRequestedTime() async throws {
        var d=day();let start=journeyDate("2026-10-23T10:00:00+11:00")!,late=start.addingTimeInterval(3600)
        func step(_ date:Date)->JourneyStep {let f=ISO8601DateFormatter();return JourneyStep(mode:"bus",line:"fresh",headsign:nil,from:d.start.name,to:d.end.name,departureTime:f.string(from:date),arrivalTime:f.string(from:date.addingTimeInterval(900)),realtime:false,stopCount:nil,meters:1000,seconds:900,waitSeconds:0,platform:nil,agency:nil,geometry:[])}
        let old=step(start);d.legs=[RouteLeg(meters:1000,minutes:15,polyline:"",source:"Transport for NSW",mode:"transit",steps:[old],departureTime:old.departureTime,arrivalTime:old.arrivalTime)]
        do{_ = try await AppleJourneyProvider.plan(day:d,modes:["transit"],departure:late);XCTFail("Old bus must be rejected")}catch{XCTAssertTrue(error.localizedDescription.contains("刷新"))}
        var requested:Date?
        let result=try await AppleJourneyProvider.plan(day:d,modes:["transit"],departure:late,useSavedTransit:false,transitLookup:{_,_,date in requested=date;return [step(date)]})
        XCTAssertEqual(requested,late);XCTAssertEqual(journeyDate(result.legs[0].departureTime),late);XCTAssertEqual(result.legs[0].steps[0].line,"fresh")
        do{_ = try await AppleJourneyProvider.plan(day:d,modes:["transit"],departure:late,useSavedTransit:false,transitLookup:{_,_,_ in throw URLError(.notConnectedToInternet)});XCTFail("Failed lookup must not reuse old bus")}catch{}
    }
    func testPrimaryMealsExcludeStationGroceriesAndSnackCounters() {
        XCTAssertFalse(ApplePlanningDiscovery.primaryMealName("CFM BAKERY - Central Food Market"))
        XCTAssertFalse(ApplePlanningDiscovery.primaryMealName("Central Fruit Juice"))
        XCTAssertTrue(ApplePlanningDiscovery.primaryMealName("Nom Restaurant"))
        XCTAssertTrue(ApplePlanningDiscovery.primaryMealName("Haymarket Pizzeria"))
    }
    func testPlanningCatalogDoesNotRequireRecommendationReason() throws {
        let data=Data(#"[{"id":"opera","name":"悉尼歌剧院","category":"风景","indoor":false,"visitMinutes":60,"cost":0,"lat":-33.8568,"lng":151.2153}]"#.utf8)
        let places=try JSONDecoder().decode([PlanningCatalogPlace].self,from:data)
        XCTAssertEqual(places.first?.candidate.id,"opera")
        XCTAssertEqual(places.first?.candidate.reason,"地点目录候选")
    }

    private func fixture() throws -> Data {try Data(contentsOf:Bundle(for:Self.self).url(forResource:"journey-plan-test",withExtension:"json")!)}
    private func day()->DayPlan {DayPlan(date:"2026-10-23",stops:[],legs:[],estimatedCost:0,estimatedSteps:1800,finishTime:"12:00",warnings:[],start:Coordinate(name:"测试起点",lat:-33.8611,lng:151.2126),end:Coordinate(name:"测试终点",lat:-33.8568,lng:151.2153))}
    private func model()->JourneyModel {
        let config=URLSessionConfiguration.ephemeral;config.protocolClasses=[JourneyStub.self]
        return JourneyModel(session:URLSession(configuration:config))
    }
    private func wait(_ model:JourneyModel) async throws {
        for _ in 0..<100 {if !model.busy{return};try await Task.sleep(nanoseconds:20_000_000)}
        XCTFail("Journey request did not finish")
    }
    func testTransitDataKeepsLineBoardingDirectionAndWait() throws {
        let plan=try JSONDecoder().decode(JourneyPlan.self,from:fixture());let bus=plan.legs[0].steps[1]
        XCTAssertEqual(bus.line,"TEST-01");XCTAssertEqual(bus.title,"公车 · TEST-01")
        XCTAssertEqual(bus.from,"测试上车站 A");XCTAssertEqual(bus.to,"测试下车站 B");XCTAssertEqual(bus.stopCount,4)
        XCTAssertEqual(bus.waitSeconds,300);XCTAssertFalse(bus.realtime)
        XCTAssertEqual(journeyTime(bus.departureTime),"10:10");XCTAssertEqual(plan.sections.map(\.mode),["walk","bus","walk"])
    }
    func testClientLoadsNormalizedJourneyAndInvalidatesOldResult() async throws {
        JourneyStub.status=200;JourneyStub.responseData=try fixture()
        let model=model();defer{model.session.invalidateAndCancel()}
        model.load(day:day(),modes:["transit"],departure:Date());try await wait(model)
        XCTAssertNil(model.error);XCTAssertEqual(model.plan?.legs[0].steps[1].line,"TEST-01")
        XCTAssertEqual(JourneyStub.lastRequest?.url?.path,"/journey/plan");XCTAssertEqual(JourneyStub.lastRequest?.httpMethod,"POST")
        model.invalidate();XCTAssertNil(model.plan);XCTAssertFalse(model.busy)
    }
    func testUnconfiguredServiceErrorIsShownWithoutFallbackBus() async throws {
        JourneyStub.status=422;JourneyStub.responseData=Data("{\"error\":\"大众运输尚未设置，请配置 GTFS / OTP。\"}".utf8)
        let model=model();defer{model.session.invalidateAndCancel()}
        model.load(day:day(),modes:["transit"],departure:Date());try await wait(model)
        XCTAssertNil(model.plan);XCTAssertTrue(model.error?.contains("GTFS") == true)
    }
    func testSourceAndMapAreSentAndSwitchingSourceDiscardsOldResults() async throws {
        JourneyStub.status=200;JourneyStub.responseData=try fixture()
        let model=model();defer{model.session.invalidateAndCancel()}
        model.load(day:day(),modes:["transit"],departure:Date(),routingProvider:"nsw",mapProvider:"apple")
        try await wait(model)
        // URLProtocol may expose the body as an input stream rather than httpBody.
        let request=try XCTUnwrap(JourneyStub.lastRequest)
        var data=request.httpBody
        if data == nil,let stream=request.httpBodyStream {
            stream.open();defer{stream.close()};var bytes=[UInt8](repeating:0,count:4096);var body=Data()
            while stream.hasBytesAvailable {let count=stream.read(&bytes,maxLength:bytes.count);if count<=0{break};body.append(bytes,count:count)}
            data=body
        }
        let payload=try JSONSerialization.jsonObject(with:XCTUnwrap(data)) as! [String:Any]
        XCTAssertEqual(payload["routingProvider"] as? String,"nsw");XCTAssertEqual(payload["mapProvider"] as? String,"apple")
        model.invalidate();XCTAssertNil(model.plan)
    }
    func testSavedNswTransitRendersWithoutInventingWalkingAndCountsWaiting() async throws {
        var plan=day()
        let start="2026-10-23T10:00:00.0000000+11:00",end="2026-10-23T10:15:00.0000000+11:00"
        let bus=JourneyStep(mode:"bus",line:"304",headsign:"Central",from:"Quay Stand A",to:"Central",departureTime:start,arrivalTime:end,realtime:false,stopCount:3,meters:3000,seconds:600,waitSeconds:300,platform:nil,agency:"NSW",geometry:[JourneyCoordinate(lat:plan.start.lat,lng:plan.start.lng),JourneyCoordinate(lat:plan.end.lat,lng:plan.end.lng)])
        plan.legs=[RouteLeg(meters:3000,minutes:15,polyline:"",source:"Transport for NSW / Trip Planner",mode:"transit",walkingMeters:0,steps:[bus],departureTime:start,arrivalTime:end)]
        let previous=AppleWalkingRoads.shared.query;defer{AppleWalkingRoads.shared.query=previous}
        AppleWalkingRoads.shared.query={_,_ in XCTFail("Must not replace a bus by a walking query");throw URLError(.cancelled)}
        let result=try await AppleJourneyProvider.plan(day:plan,modes:plan.transportModes,departure:journeyDate(start)!)
        XCTAssertTrue(plan.hasTransit);XCTAssertEqual(result.travelSeconds,900)
        XCTAssertEqual(result.legs[0].mode,"transit");XCTAssertEqual(result.legs[0].steps[0].line,"304")
        XCTAssertTrue(result.source.contains("Transport for NSW"));XCTAssertTrue(result.legs[0].walkingSummary.contains("304"))
    }
    func testAppleTransitFailsWithoutQueryingOrInventingBusDetails() async {
        do { _ = try await AppleJourneyProvider.plan(day:day(),modes:["transit"],departure:Date());XCTFail("MapKit transit directions must not be invented") }
        catch {XCTAssertTrue(error.localizedDescription.contains("NSW"))}
    }
    func testAppleOverviewRequestsNativeProviderAndKeepsRoadGeometry() async throws {
        let data=try Data(contentsOf:Bundle(for:Self.self).url(forResource:"walking-sydney",withExtension:"json")!)
        let route=try OverviewRoute.parse(data)
        let step=JourneyStep(mode:"walk",line:nil,headsign:nil,from:"起点",to:"终点",departureTime:"2026-10-23T10:00:00+11:00",arrivalTime:"2026-10-23T10:10:00+11:00",realtime:false,stopCount:nil,meters:route.meters,seconds:route.seconds,waitSeconds:0,platform:nil,agency:nil,geometry:route.coordinates.map{JourneyCoordinate(lat:$0.latitude,lng:$0.longitude)})
        let leg=JourneyLeg(index:0,mode:"walk",from:step.from,to:step.to,departureTime:step.departureTime,arrivalTime:step.arrivalTime,staySeconds:0,steps:[step])
        let plan=JourneyPlan(departureTime:step.departureTime,arrivalTime:step.arrivalTime,travelSeconds:route.seconds,elapsedSeconds:route.seconds,meters:route.meters,legs:[leg],warnings:[],source:"Apple Maps / MapKit")
        var nativeCalls=0
        let overview=ItineraryOverview(applePlan:{_,_ in nativeCalls+=1;return plan})
        overview.load(day(),provider:.apple)
        for _ in 0..<100 {if !overview.busy{break};try await Task.sleep(nanoseconds:20_000_000)}
        XCTAssertFalse(overview.busy);XCTAssertEqual(nativeCalls,1)
        XCTAssertEqual(overview.route?.coordinates.count,route.coordinates.count)
        XCTAssertEqual(overview.plan?.source,"Apple Maps / MapKit")
        overview.load(day(),provider:.apple);XCTAssertEqual(nativeCalls,1)
    }
    func testPlannerResultIsSharedWithOverviewAndClearedOnProviderChange() throws {
        let result=try JSONDecoder().decode(JourneyPlan.self,from:fixture())
        let overview=ItineraryOverview()
        overview.accept(result,day:day())
        XCTAssertEqual(overview.key,mapSceneKey(day()))
        XCTAssertEqual(overview.plan?.sections.map(\.mode),["walk","bus","walk"])
        XCTAssertEqual(overview.route?.meters,result.meters)
        overview.clear()
        XCTAssertNil(overview.route);XCTAssertNil(overview.plan);XCTAssertNil(overview.message)
    }
    func testRenderClearlyLabeledTransitFixture() async throws {
        let plan=try JSONDecoder().decode(JourneyPlan.self,from:fixture())
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        window.rootViewController=UIHostingController(rootView:JourneyPlannerView(day:day(),tripID:"test-only",provider:.osm,fixture:plan))
        window.makeKeyAndVisible();defer{window.isHidden=true;old?.makeKeyAndVisible()}
        try await Task.sleep(nanoseconds:2_000_000_000)
        let image=UIGraphicsImageRenderer(bounds:window.bounds).image{_ in window.drawHierarchy(in:window.bounds,afterScreenUpdates:true)}
        let url=FileManager.default.urls(for:.documentDirectory,in:.userDomainMask)[0].appendingPathComponent("journey-ios-fixture.png")
        try XCTUnwrap(image.pngData()).write(to:url)
        XCTAssertEqual(plan.source,"Synthetic fixture / NOT LIVE")
    }
}
