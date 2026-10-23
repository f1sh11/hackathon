import XCTest
import Combine
import CoreLocation
import MapKit
import FerrostarCore
import FerrostarCoreFFI
@testable import SydneyBuddy

@MainActor final class NavigationTests: XCTestCase {
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
