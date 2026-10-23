import XCTest
import SwiftUI
@testable import SydneyBuddy

@MainActor final class ScheduleTests:XCTestCase {
    func testTimeHandlesSnapAndKeepOppositeEdge() {
        XCTAssertEqual(ScheduleTimeEdit.drag(start:600,duration:60,queue:0,delta:-2.5,edge:0),ScheduleTimeEdit(start:595,duration:60))
        XCTAssertEqual(ScheduleTimeEdit.drag(start:600,duration:60,queue:10,delta:13,edge:0),ScheduleTimeEdit(start:615,duration:60))
        let top=ScheduleTimeEdit.drag(start:600,duration:60,queue:10,delta:13,edge:-1)
        XCTAssertEqual(top,ScheduleTimeEdit(start:615,duration:45))
        XCTAssertEqual(top.start+10+top.duration,670)
        XCTAssertEqual(ScheduleTimeEdit.drag(start:600,duration:60,queue:10,delta:13,edge:1),ScheduleTimeEdit(start:600,duration:75))
        XCTAssertEqual(ScheduleTimeEdit.drag(start:0,duration:5,queue:0,delta:-100,edge:-1),ScheduleTimeEdit(start:0,duration:5))
        XCTAssertEqual(ScheduleTimeEdit.drag(start:600,duration:60,queue:0,delta:1000,edge:-1).duration,5)
        XCTAssertEqual(ScheduleTimeEdit.drag(start:600,duration:60,queue:0,delta:1000,edge:1).duration,240)
    }
    func testMovingTimeSortsDraftAndUndoRestoresIt() async throws {
        let trip=try fixture();let model=ScheduleEditorModel();model.prepare(trip)
        model.loader={request in let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any];state["days"]=body["days"];raw["schedule"]=state;return (try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let first=model.state!.days[0].activities[0]
        model.retime(first.id,start:750,duration:first.duration)
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertNotEqual(model.state!.days[0].activities[0].id,first.id)
        XCTAssertEqual(model.state!.days[0].activities.first{$0.id==first.id}?.fixedStart,750)
        model.undo()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(model.state!.days[0].activities[0].id,first.id)
        XCTAssertNil(model.state!.days[0].activities[0].fixedStart);model.stop()
    }
    func fixture() throws -> Trip {try JSONDecoder().decode(Trip.self,from:Data(contentsOf:Bundle(for:Self.self).url(forResource:"schedule-trip-test",withExtension:"json")!))}
    func testLegacyAndroidProfileWithoutRequestCanStillBeRead() throws {
        var raw=try JSONSerialization.jsonObject(with:JSONEncoder().encode(fixture())) as! [String:Any]
        var profile=raw["profile"] as! [String:Any];profile.removeValue(forKey:"request");raw["profile"]=profile
        let decoded=try JSONDecoder().decode(Trip.self,from:JSONSerialization.data(withJSONObject:raw))
        XCTAssertNil(decoded.profile.request)
    }
    func testSavedScheduleRoundTripsWithAllActivityAndConstraintFields() throws {
        let trip=try fixture();XCTAssertEqual(trip.schedule?.days.count,3)
        XCTAssertTrue(trip.schedule!.days[0].unknownQueue)
        let decoded=try JSONDecoder().decode(Trip.self,from:JSONEncoder().encode(trip))
        XCTAssertEqual(decoded.schedule!.days[0].activities.map(\.id),trip.schedule!.days[0].activities.map(\.id))
        XCTAssertEqual(decoded.schedule!.settings.timeLimit,180)
    }
    func testManualOrderAndDurationAreSentToSharedApiAndUndoRestoresPreviousDraft() async throws {
        let model=ScheduleEditorModel();let trip=try fixture();model.prepare(trip)
        var bodies:[[String:Any]]=[]
        model.loader={request in
            let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];bodies.append(body)
            var result=try JSONSerialization.jsonObject(with:JSONEncoder().encode(trip)) as! [String:Any]
            var schedule=result["schedule"] as! [String:Any];schedule["days"]=body["days"];schedule["settings"]=body["settings"];result["schedule"]=schedule
            return (try JSONSerialization.data(withJSONObject:result),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let before=model.state!.days[0].activities.map(\.id)
        model.move(before[0],by:1)
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(model.state!.days[0].activities[1].id,before[0]);XCTAssertFalse(bodies.isEmpty)
        model.edit{$0.days[0].activities[0].duration=35}
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(model.state!.days[0].activities[0].duration,35)
        model.undo()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertNotEqual(model.state!.days[0].activities[0].duration,35)
        model.stop()
    }
    func testMustVisitSelectionCanBeUndone() async throws {
        let model=ScheduleEditorModel();let original=try fixture();model.prepare(original)
        model.loader={request in let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any];state["days"]=body["days"];raw["schedule"]=state;return (try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let candidate=model.state!.candidates.first!
        model.toggleMust(candidate)
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertTrue(model.trip!.profile.mustVisit.contains(candidate.id))
        model.undo()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertFalse(model.trip!.profile.mustVisit.contains(candidate.id));model.stop()
    }
    func testTimelineRendersRecordedThreeDayPrototypeWithoutSaving() async throws {
        let trip=try fixture();let store=AppStore();store.proposal=trip
        let model=ScheduleEditorModel();model.prepare(trip)
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        window.rootViewController=UIHostingController(rootView:ScheduleEditorView(model:model).environmentObject(store))
        window.makeKeyAndVisible();defer{model.stop();window.isHidden=true;old?.makeKeyAndVisible()}
        try await Task.sleep(nanoseconds:800_000_000)
        let image=UIGraphicsImageRenderer(bounds:window.bounds).image{_ in window.drawHierarchy(in:window.bounds,afterScreenUpdates:true)}
        let url=FileManager.default.urls(for:.documentDirectory,in:.userDomainMask)[0].appendingPathComponent("schedule-ios-test.png")
        try XCTUnwrap(image.pngData()).write(to:url)
        XCTAssertNil(store.trip);XCTAssertEqual(model.state!.days.count,3)
    }
}
