import XCTest
import Combine
import CoreLocation
import MapKit
import FerrostarCore
import FerrostarCoreFFI
@testable import SydneyBuddy

@MainActor final class NavigationTests: XCTestCase {
    func testAppleCompletionSurvivesDurationAndOrderChanges() {
        var plan=day();let point=plan.end
        plan.stops=[Stop(id:"stable-activity",name:point.name,lat:point.lat,lng:point.lng,category:"自然",indoor:false,visitMinutes:30,cost:0,note:"",arrival:"10:00",departure:"10:30",hoursVerified:false,mapsURL:"",steps:0)]
        let id="stable-"+UUID().uuidString
        let nav=WalkingNavigator(day:plan,tripID:id,endpoint:"unused",apple:true)
        defer{nav.pause();UserDefaults.standard.removeObject(forKey:nav.storageKey);UserDefaults.standard.removeObject(forKey:JourneyExecution.key(tripID:id,date:plan.date))}
        nav.arrived();plan.stops[0].visitMinutes=90
        let restored=WalkingNavigator(day:plan,tripID:id,endpoint:"unused",apple:true)
        XCTAssertEqual(nav.storageKey,restored.storageKey);XCTAssertEqual(restored.progress.leg,1)
        XCTAssertTrue(JourneyExecution.completed(tripID:id,date:plan.date).contains("stable-activity"))
    }
    func testProjectionMeasuresRoadDistanceAndDeviation() {
        let a=CLLocationCoordinate2D(latitude:-33.86,longitude:151.21),b=CLLocationCoordinate2D(latitude:-33.86,longitude:151.22)
        let middle=RouteProjection.locate(.init(latitude:-33.86,longitude:151.215),on:[a,b])
        XCTAssertLessThan(middle.distance,1);XCTAssertEqual(middle.fraction,0.5,accuracy:0.01);XCTAssertGreaterThan(middle.remaining,400)
        XCTAssertGreaterThan(RouteProjection.locate(.init(latitude:-33.861,longitude:151.215),on:[a,b]).distance,100)
    }
    func testArrivalUsesRoadEndAndRejectsInaccurateFixOrWrongLoopSegment() {
        XCTAssertTrue(RouteProjection.acceptsArrival(distanceToRoadEnd:3,remaining:0,accuracy:5))
        XCTAssertFalse(RouteProjection.acceptsArrival(distanceToRoadEnd:3,remaining:0,accuracy:50))
        XCTAssertFalse(RouteProjection.acceptsArrival(distanceToRoadEnd:3,remaining:500,accuracy:5))
        XCTAssertFalse(RouteProjection.acceptsArrival(distanceToRoadEnd:40,remaining:0,accuracy:5))
        XCTAssertFalse(RouteProjection.acceptsArrival(distanceToRoadEnd:0,remaining:0,accuracy:-1))
    }
    func testReturnOnlyDayRemainsNavigable() {
        let plan=day();XCTAssertTrue(plan.hasWalkingJourney)
        var empty=plan;empty.end=empty.start;XCTAssertFalse(empty.hasWalkingJourney)
    }
    func testAppleThrottleHonorsProviderCooldown() {
        let error=NSError(domain:MKError.errorDomain,code:Int(MKError.loadingThrottled.rawValue),userInfo:["MKErrorGEOErrorUserInfo":["timeUntilReset":35]])
        XCTAssertEqual(AppleWalkingRoads.retryDelay(error),36)
    }
    func testAppleNetworkSlotsStayBelowBurstLimitAcrossConcurrentCallers() {
        let now=Date();var next=now;var starts:[Date]=[]
        for _ in 0..<51 {let slot=AppleWalkingRoads.querySlot(now:now,next:next);starts.append(slot.start);next=slot.next}
        XCTAssertGreaterThan(starts[50].timeIntervalSince(starts[0]),60)
        XCTAssertEqual(starts[1].timeIntervalSince(starts[0]),1.6,accuracy:0.001)
    }
    func testAppleThrottleRecognizesUnderlyingGeoError() {
        let geo=NSError(domain:"GEOErrorDomain",code:-3,userInfo:["timeUntilReset":44])
        XCTAssertTrue(AppleWalkingRoads.isThrottled(geo))
        XCTAssertTrue(AppleWalkingRoads.isThrottled(NSError(domain:"wrapper",code:1,userInfo:[NSUnderlyingErrorKey:geo])))
        XCTAssertEqual(AppleWalkingRoads.retryDelay(geo),45)
        XCTAssertFalse(AppleWalkingRoads.isThrottled(URLError(.notConnectedToInternet)))
    }
    func testProviderArrivalInstructionCannotClaimArrivalHundredsOfMetersEarly() {
        XCTAssertEqual(NavigationInstruction.display("到达目的地",remaining:280,target:"环形码头"),"沿路线继续前往环形码头，剩余约 280 米")
        XCTAssertEqual(NavigationInstruction.display("Turn right",remaining:280,target:"Quay"),"Turn right")
        XCTAssertEqual(NavigationInstruction.display("目的地在你右侧",remaining:10,target:"Quay"),"目的地在你右侧")
    }
    func testDefaultMapMigratesToAppleOnceAndPreservesLaterChoice() {
        let name="apple-default-test-"+UUID().uuidString
        let defaults=UserDefaults(suiteName:name)!
        defer{defaults.removePersistentDomain(forName:name)}
        defaults.removePersistentDomain(forName:name)
        defaults.set("osm",forKey:"mapProvider")
        XCTAssertEqual(MapProvider.preferred(defaults:defaults),.apple)
        defaults.set("osm",forKey:"mapProvider")
        XCTAssertEqual(MapProvider.preferred(defaults:defaults),.osm)
        defaults.set("invalid",forKey:"mapProvider")
        XCTAssertEqual(MapProvider.preferred(defaults:defaults),.apple)
    }
    func testCoLocatedAppleWalkingLegsNeedNoRouteServiceAndKeepAllStops() async throws {
        var plan=day();plan.end=plan.start
        plan.stops=[Stop(id:"same",name:"同址休息",lat:plan.start.lat,lng:plan.start.lng,category:"自然",indoor:false,visitMinutes:10,cost:0,note:"",arrival:"10:00",departure:"10:10",hoursVerified:false,mapsURL:"",steps:0)]
        let result=try await AppleJourneyProvider.plan(day:plan,modes:["walk","walk"],departure:Date())
        XCTAssertEqual(result.source,"Apple Maps / MapKit");XCTAssertEqual(result.legs.count,2)
        XCTAssertEqual(result.meters,0);XCTAssertEqual(result.travelSeconds,0);XCTAssertEqual(result.elapsedSeconds,600)
        XCTAssertEqual(result.legs.first?.steps.first?.walkingDirections?.first?.text,"同一位置，无需步行")
    }
    func testUnavailableValhallaStillDoesNotEnableItsNavigator() {
        XCTAssertFalse(TripNavigationAvailability.canNavigate(configured:nil,mode:"demo"))
        XCTAssertFalse(TripNavigationAvailability.canNavigate(configured:false,mode:"demo"))
        XCTAssertFalse(TripNavigationAvailability.canNavigate(configured:true,mode:"live"))
        XCTAssertTrue(TripNavigationAvailability.canNavigate(configured:true,mode:"demo"))
    }

    func day() -> DayPlan {
        DayPlan(date: "2026-10-23", stops: [], legs: [], estimatedCost: 0, estimatedSteps: 0, finishTime: "18:00", warnings: [], start: .quay, end: Coordinate(name: "Opera House", lat: -33.8568, lng: 151.2153))
    }
    func testOverviewIdentityIsStableAndPinsIncludeReturn() {
        let plan=day();let identity=mapSceneKey(plan)
        for _ in 0..<100 {XCTAssertEqual(mapSceneKey(plan),identity)}
        var loop=plan;loop.end=loop.start
        XCTAssertEqual(overviewPins(loop).map(\.badge),["起/终"])
        XCTAssertEqual(overviewPins(plan).map(\.badge),["起","终"])
    }
    func testOverviewParsesPolyline6AndRejectsBrokenGeometry() throws {
        let url=Bundle(for:Self.self).url(forResource:"walking-sydney",withExtension:"json")!
        let route=try OverviewRoute.parse(Data(contentsOf:url))
        XCTAssertGreaterThan(route.coordinates.count,10)
        XCTAssertEqual(route.coordinates[0].latitude,-33.861,accuracy:0.003)
        XCTAssertEqual(route.coordinates[0].longitude,151.213,accuracy:0.003)
        XCTAssertGreaterThan(route.seconds,0)
        XCTAssertThrowsError(try OverviewRoute.decode("~"))
        XCTAssertThrowsError(try OverviewRoute.parse(Data("{\"code\":\"Error\"}".utf8)))
    }
    func testStopsCannotAdvanceUntilArrivalAndReturnLegCompletesOnce() {
        var p = WalkingProgress()
        p.advance(legCount: 2); XCTAssertEqual(p.leg, 0)
        p.arrive(); XCTAssertTrue(p.arrived); p.advance(legCount: 2)
        XCTAssertEqual(p.leg, 1); XCTAssertFalse(p.arrived)
        p.advance(legCount: 2); XCTAssertFalse(p.finished)
        p.arrive(); p.advance(legCount: 2); XCTAssertTrue(p.finished)
        p.advance(legCount: 2); XCTAssertEqual(p.leg, 1)
    }
    func testSimulationNeverChangesSavedRealProgressAndIdentityIsStable() {
        let id = "test-" + UUID().uuidString
        let nav = WalkingNavigator(day: day(), tripID: id, endpoint: "http://127.0.0.1/unused")
        defer { UserDefaults.standard.removeObject(forKey: nav.storageKey); nav.pause() }
        XCTAssertEqual(nav.storageKey, WalkingNavigator(day: day(), tripID: id, endpoint: "http://127.0.0.1/unused").storageKey)
        nav.setSimulation(true); nav.arrived(); XCTAssertTrue(nav.progress.arrived)
        nav.setSimulation(false); XCTAssertFalse(nav.progress.arrived)
        nav.arrived()
        let restored = WalkingNavigator(day: day(), tripID: id, endpoint: "http://127.0.0.1/unused")
        XCTAssertTrue(restored.progress.arrived)
        XCTAssertNotEqual(nav.storageKey, WalkingNavigator(day: day(), tripID: id + ":revision2", endpoint: "http://127.0.0.1/unused").storageKey)
    }
    func testAlreadyAtDestinationCompletesWithoutCallingRoutingServer() async throws {
        var plan = day(); plan.end = plan.start
        let nav = WalkingNavigator(day: plan, tripID: "zero-distance-" + UUID().uuidString, endpoint: "http://127.0.0.1:1/unreachable")
        defer { nav.pause() }
        nav.setSimulation(true); nav.start()
        for _ in 0..<100 { if !nav.busy { break }; try await Task.sleep(nanoseconds: 20_000_000) }
        XCTAssertNil(nav.message); XCTAssertTrue(nav.progress.arrived); XCTAssertFalse(nav.active)
    }
    func testFerrostarParsesRealSydneyRouteAndSimulationReachesArrival() async throws {
        let url = Bundle(for: Self.self).url(forResource: "walking-sydney", withExtension: "json")!
        let adapter = try RouteAdapter.fromWellKnownRouteProvider(wellKnownRouteProvider: .valhalla(endpointUrl: "http://127.0.0.1/unused", profile: "pedestrian"))
        let route = try XCTUnwrap(adapter.parseResponse(response: Data(contentsOf: url)).first)
        XCTAssertGreaterThan(route.geometry.count, 10)
        XCTAssertGreaterThan(route.distance, 500)
        XCTAssertGreaterThan(route.steps.count, 2)
        let source = SimulatedLocationProvider(location: CLLocation(coordinate: route.geometry.first!.clLocationCoordinate2D, altitude: 0, horizontalAccuracy: 5, verticalAccuracy: 5, timestamp: Date()))
        source.warpFactor = 10
        try source.setSimulatedRoute(route)
        let core = try FerrostarCore(wellKnownRouteProvider: .valhalla(endpointUrl: "http://127.0.0.1/unused", profile: "pedestrian"), locationProvider: source, navigationControllerConfig: WalkingNavigator.config())
        core.spokenInstructionObserver.toggleMute()
        let arrived = expectation(description: "Ferrostar detects destination arrival")
        var completed = false
        let observation = core.$state.sink { state in
            if let state, case .complete = state.tripState, !completed { completed = true; arrived.fulfill() }
        }
        defer { observation.cancel(); core.stopNavigation() }
        try core.startNavigation(route: route)
        await fulfillment(of: [arrived], timeout: 25)
        XCTAssertTrue(completed)
    }
}
