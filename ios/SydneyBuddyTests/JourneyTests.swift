import XCTest
import SwiftUI
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
    func testAppleTransitFailsWithoutQueryingOrInventingBusDetails() async {
        do { _ = try await AppleJourneyProvider.plan(day:day(),modes:["transit"],departure:Date());XCTFail("MapKit transit directions must not be invented") }
        catch {XCTAssertTrue(error.localizedDescription.contains("NSW"))}
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
