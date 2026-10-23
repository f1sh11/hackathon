import XCTest
import SwiftUI
import MapKit
@testable import SydneyBuddy

@MainActor final class ScheduleTests:XCTestCase {
    func testHomeAutoLoadsWalkingRouteOnceAndDoesNotAutoCallGoogle() async throws {
        let day=try fixture().days[0];let routeData=try Data(contentsOf:Bundle(for:Self.self).url(forResource:"walking-sydney",withExtension:"json")!)
        let overview=ItineraryOverview();var calls=0
        overview.loader={request in
            calls+=1
            let requestJSON=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            let count=(requestJSON["locations"] as! [[String:Double]]).count-1
            var json=try JSONSerialization.jsonObject(with:routeData) as! [String:Any]
            var routes=json["routes"] as! [[String:Any]]
            routes[0]["legs"]=Array(repeating:["duration":60.0],count:count);json["routes"]=routes
            return(try JSONSerialization.data(withJSONObject:json),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        overview.loadHome(day,provider:.osm,contentSource:"open")
        for _ in 0..<100{if !overview.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(calls,1);XCTAssertNotNil(overview.route)
        overview.loadHome(day,provider:.osm,contentSource:"open");XCTAssertEqual(calls,1)
        overview.loadHome(day,provider:.google,contentSource:"google");XCTAssertEqual(calls,1);XCTAssertNil(overview.route)
    }
    func testHomeCoLocatedStopsKeepOriginalLegOrderWithoutDuplicateQueryPoints() async throws {
        var day=try fixture().days[0]
        day.stops[0].lat=day.start.lat;day.stops[0].lng=day.start.lng
        let originalCount=day.stops.count+1
        let data=try Data(contentsOf:Bundle(for:Self.self).url(forResource:"walking-sydney",withExtension:"json")!)
        let overview=ItineraryOverview()
        overview.loader={request in
            let json=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            let locations=json["locations"] as! [[String:Double]]
            XCTAssertLessThan(locations.count,originalCount+1)
            for pair in zip(locations,locations.dropFirst()){XCTAssertNotEqual(pair.0,pair.1)}
            var response=try JSONSerialization.jsonObject(with:data) as! [String:Any]
            var routes=response["routes"] as! [[String:Any]]
            routes[0]["legs"]=Array(repeating:["duration":60.0],count:locations.count-1);response["routes"]=routes
            return(try JSONSerialization.data(withJSONObject:response),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        overview.load(day)
        for _ in 0..<100{if !overview.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertNil(overview.message);XCTAssertEqual(overview.route?.legSeconds.count,originalCount)
        XCTAssertEqual(overview.route?.legSeconds.first,0)
        XCTAssertEqual(overviewTransportLegs(day,route:overview.route,plan:nil).first?.transport,"同一位置")
    }
    func testHomeTransportLegsUseActualBusNumberInsteadOfCallingItWalking() throws {
        let plan=try JSONDecoder().decode(JourneyPlan.self,from:Data(contentsOf:Bundle(for:Self.self).url(forResource:"journey-plan-test",withExtension:"json")!))
        let rows=overviewTransportLegs(try fixture().days[0],route:plan.overview,plan:plan)
        XCTAssertTrue(rows.contains{$0.transport.contains("TEST") && $0.transport.contains("公车")})
        XCTAssertEqual(rows.count,plan.legs.count)
    }
    func testCompactHomeRendersSavedTripWithoutChangingIt() async throws {
        let store=AppStore();let original=store.mapProvider;defer{store.mapProvider=original};store.mapProvider = .schematic;store.trip=try fixture()
        let before=store.trip!.days.flatMap(\.stops).map(\.id)
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        window.rootViewController=UIHostingController(rootView:HomeView().environmentObject(store))
        window.makeKeyAndVisible();defer{window.isHidden=true;old?.makeKeyAndVisible()}
        try await Task.sleep(nanoseconds:300_000_000)
        XCTAssertEqual(store.trip!.days.flatMap(\.stops).map(\.id),before);XCTAssertNil(store.proposal)
    }
    func testAllowanceKeepsNegativeRemainingAndClampsOnlyBar() {
        let over=ScheduleAllowance(limit:10000,used:11234)
        XCTAssertEqual(over.remaining,-1234);XCTAssertEqual(over.fraction,0);XCTAssertTrue(over.exceeded)
        XCTAssertEqual(ScheduleAllowance(limit:250,used:50).fraction,0.8)
        XCTAssertEqual(ScheduleAllowance(limit:180,used:195).remaining,-15)
        XCTAssertEqual(ScheduleAllowance(limit:0,used:10).fraction,0)
        XCTAssertTrue(ScheduleAllowance(limit:0,used:10).exceeded)
        XCTAssertFalse(ScheduleAllowance(limit:0,used:0).exceeded)
        XCTAssertFalse(ScheduleAllowance(limit:100,used:100).exceeded)
    }
    func testAllowanceSummaryRendersNormalAndExceededDrafts() async throws {
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        defer{window.isHidden=true;old?.makeKeyAndVisible()}
        for exceeded in [false,true] {
            var state=try fixture().schedule!;var day=state.days[0]
            if exceeded{day.estimatedSteps=state.settings.maxSteps+1234;day.estimatedCost=state.settings.dailyBudget+25;day.timeUsed=state.settings.timeLimit+15;day.transportMinutes=day.timeUsed;day.queueMinutes=0;state.totalCost=state.settings.totalBudget+50}
            window.rootViewController=UIHostingController(rootView:VStack{ScheduleAllowanceSummary(day:day,state:state,dayNumber:1,animateWarnings:false);Spacer()}.padding(18).background(Palette.cream))
            window.makeKeyAndVisible();try await Task.sleep(nanoseconds:250_000_000)
            let image=UIGraphicsImageRenderer(bounds:window.bounds).image{_ in window.drawHierarchy(in:window.bounds,afterScreenUpdates:true)}
            let url=FileManager.default.temporaryDirectory.appendingPathComponent("allowance-"+(exceeded ? "exceeded":"normal")+".png")
            try image.pngData()!.write(to:url);print("ALLOWANCE_PREVIEW: "+url.path)
            XCTAssertGreaterThan(image.size.width,300)
        }
    }
    func testPoiNativeMapRemovesOsmTilesOnAppleSwitchAndKeepsPinsAndCamera() async throws {
        let store=AppStore();let original=store.mapProvider;defer{store.mapProvider=original};store.mapProvider = .osm
        let points=try fixture().schedule!.candidates
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        window.rootViewController=UIHostingController(rootView:PoiProviderTestHost(store:store,places:points).frame(height:250))
        window.makeKeyAndVisible();defer{window.isHidden=true;old?.makeKeyAndVisible()}
        try await Task.sleep(nanoseconds:400_000_000)
        func findMap(_ view:UIView)->MKMapView? {if let map=view as? MKMapView{return map};return view.subviews.compactMap{findMap($0)}.first}
        let map=try XCTUnwrap(findMap(window));XCTAssertEqual(map.overlays.filter{$0 is OsmTiles}.count,1);XCTAssertEqual(map.annotations.count,points.count)
        let center=map.centerCoordinate
        store.mapProvider = .apple;try await Task.sleep(nanoseconds:250_000_000)
        XCTAssertTrue(map.overlays.isEmpty);XCTAssertEqual(map.annotations.count,points.count)
        XCTAssertEqual(map.centerCoordinate.latitude,center.latitude,accuracy:0.0001);XCTAssertEqual(map.centerCoordinate.longitude,center.longitude,accuracy:0.0001)
        store.mapProvider = .osm;try await Task.sleep(nanoseconds:250_000_000)
        XCTAssertEqual(map.overlays.filter{$0 is OsmTiles}.count,1);XCTAssertEqual(map.annotations.count,points.count)
    }
    func testSavedTripNavigationChoosesTodayOrAsksForDate() throws {
        var trip=try fixture()
        XCTAssertEqual(savedTripNavigationDay(trip,today:trip.days[1].date),1)
        XCTAssertNil(savedTripNavigationDay(trip,today:"2099-01-01"))
        trip.days=Array(trip.days.prefix(1));XCTAssertEqual(savedTripNavigationDay(trip,today:"2099-01-01"),0)
        trip.days=[];XCTAssertNil(savedTripNavigationDay(trip,today:"2099-01-01"))
    }
    func testSavedDayNavigationKeepsSavedOrderStopsAndReturnPoint() throws {
        let trip=try fixture();let day=trip.days[1]
        let navigator=WalkingNavigator(day:day,tripID:"saved-trip-card-test",endpoint:"http://127.0.0.1:8787/navigation/route")
        XCTAssertEqual(navigator.points.map(\.name),[day.start.name]+day.stops.map(\.name)+[day.end.name+"（返回）"])
        XCTAssertEqual(Array(navigator.points.dropFirst().dropLast()).map(\.stay),day.stops.map(\.visitMinutes))
        let other=WalkingNavigator(day:trip.days[0],tripID:"saved-trip-card-test",endpoint:"http://127.0.0.1:8787/navigation/route")
        XCTAssertNotEqual(navigator.storageKey,other.storageKey);navigator.pause();other.pause()
    }
    func testPoiSearchDistinguishesFailureEmptyAndSuccessfulRetry() async throws {
        let model=PoiSearchModel();var status=503;var body=Data("{\"error\":\"服務測試中斷\"}".utf8)
        model.category="facilities"
        model.loader={request in
            let input=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            XCTAssertEqual(input["category"] as? String,"facilities")
            return(body,HTTPURLResponse(url:request.url!,statusCode:status,httpVersion:nil,headerFields:nil)!)
        }
        model.search()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertTrue(model.hasSearched);XCTAssertTrue(model.error!.contains("服務測試中斷"));XCTAssertTrue(model.places.isEmpty)
        status=200;body=Data("{\"places\":[]}".utf8);model.search()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertNil(model.error);XCTAssertTrue(model.places.isEmpty)
        body=try JSONSerialization.data(withJSONObject:["places":JSONSerialization.jsonObject(with:JSONEncoder().encode(fixture().schedule!.candidates))]);model.search()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertNil(model.error);XCTAssertFalse(model.places.isEmpty);model.stop()
    }
    func testPoiNewCategoryCannotBeOverwrittenByCancelledSearch() async throws {
        let model=PoiSearchModel();let points=try JSONEncoder().encode(fixture().schedule!.candidates)
        model.loader={request in
            let input=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            if input["category"] as? String=="sights" {try? await Task.sleep(nanoseconds:150_000_000);return(try JSONSerialization.data(withJSONObject:["places":JSONSerialization.jsonObject(with:points)]),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
            return(Data("{\"places\":[]}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        model.search();try await Task.sleep(nanoseconds:20_000_000)
        model.category="food";model.search();try await Task.sleep(nanoseconds:200_000_000)
        XCTAssertFalse(model.busy);XCTAssertNil(model.error);XCTAssertTrue(model.places.isEmpty);model.stop()
    }
    func testHoverDateDwellSwitchesWithoutEditingAndCancellationStopsSwitch() async throws {
        let trip=try fixture();let model=ScheduleEditorModel();model.prepare(trip)
        let before=model.state!.days.flatMap(\.activities).map{($0.id)+String($0.scheduledStart)}
        let drag=ScheduleToolboxDragSession();drag.candidate=trip.schedule!.candidates[0]
        drag.dayFrames=[1:CGRect(x:100,y:0,width:80,height:50)]
        drag.hover(at:CGPoint(x:120,y:20),currentDay:0){model.dayIndex=$0}
        try await Task.sleep(nanoseconds:550_000_000)
        XCTAssertEqual(model.dayIndex,1);XCTAssertEqual(model.state!.days.flatMap(\.activities).map{($0.id)+String($0.scheduledStart)},before)
        model.dayIndex=0
        drag.hover(at:CGPoint.zero,currentDay:0){model.dayIndex=$0}
        drag.hover(at:CGPoint(x:120,y:20),currentDay:0){model.dayIndex=$0}
        drag.finish()
        try await Task.sleep(nanoseconds:550_000_000)
        XCTAssertEqual(model.dayIndex,0);XCTAssertFalse(model.busy);model.stop()
    }
    func testCrossDayPlacementPreservesIdentityDurationAndUndo() async throws {
        let trip=try fixture();let model=ScheduleEditorModel();model.prepare(trip)
        model.loader={request in let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any];state["days"]=body["days"];raw["schedule"]=state;return (try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let original=model.state!.days[0].activities.first{$0.placeId != nil}!
        let candidate=model.state!.candidates.first{$0.id==original.placeId}!
        model.dayIndex=1;model.place(candidate,at:720,moveAcrossDays:true)
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertFalse(model.state!.days[0].activities.contains{$0.id==original.id})
        let moved=model.state!.days[1].activities.first{$0.id==original.id}!
        XCTAssertEqual(moved.duration,original.duration);XCTAssertEqual(moved.fixedStart,720)
        XCTAssertEqual(model.state!.days.flatMap(\.activities).filter{$0.placeId==candidate.id}.count,1)
        model.undo()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertTrue(model.state!.days[0].activities.contains{$0.id==original.id})
        XCTAssertFalse(model.state!.days[1].activities.contains{$0.id==original.id});model.stop()
    }
    func testCanvasReportsActualGeometryForCrossColumnDrag() async throws {
        let day=try fixture().schedule!.days[0]
        var layout=ScheduleCanvasGeometry()
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        window.rootViewController=UIHostingController(rootView:ScrollView{ScheduleTimeCanvas(day:day,onChange:{_,_,_ in},onEdit:{_ in},onAdd:{},onGeometry:{layout=$0}).frame(width:220)}.coordinateSpace(name:"scheduleWorkbench"))
        window.makeKeyAndVisible();defer{window.isHidden=true;old?.makeKeyAndVisible()}
        try await Task.sleep(nanoseconds:500_000_000)
        XCTAssertGreaterThan(layout.frame.width,200);XCTAssertGreaterThan(layout.frame.height,1000)
        XCTAssertEqual(layout.first,600);XCTAssertEqual(layout.scale,3)
    }
    func testCrossColumnDropAccountsForScrollZoomAndExcludesRulerAndHeader() {
        let viewport=CGRect(x:130,y:80,width:220,height:500)
        var canvas=ScheduleCanvasGeometry(frame:CGRect(x:130,y:150,width:220,height:1700),first:600,scale:3)
        XCTAssertEqual(ScheduleDropTime.minute(at:CGPoint(x:220,y:330),viewport:viewport,canvas:canvas,start:600,end:1140),660)
        canvas.frame.origin.y = -30
        XCTAssertEqual(ScheduleDropTime.minute(at:CGPoint(x:220,y:330),viewport:viewport,canvas:canvas,start:600,end:1140),720)
        canvas.scale=6
        XCTAssertEqual(ScheduleDropTime.minute(at:CGPoint(x:220,y:330),viewport:viewport,canvas:canvas,start:600,end:1140),660)
        XCTAssertNil(ScheduleDropTime.minute(at:CGPoint(x:140,y:330),viewport:viewport,canvas:canvas,start:600,end:1140))
        XCTAssertNil(ScheduleDropTime.minute(at:CGPoint(x:220,y:50),viewport:viewport,canvas:canvas,start:600,end:1140))
        XCTAssertNil(ScheduleDropTime.minute(at:CGPoint(x:220,y:590),viewport:viewport,canvas:canvas,start:600,end:1140))
    }
    func testOtherDayAndLockedAttractionsCannotBeMovedFromToolbox() throws {
        var trip=try fixture();trip.schedule!.days[0].activities[1].locked=true
        let model=ScheduleEditorModel();model.prepare(trip)
        let locked=trip.schedule!.days[0].activities[1]
        let same=trip.schedule!.candidates.first{$0.id==locked.placeId}!
        model.place(same,at:720)
        XCTAssertFalse(model.busy);XCTAssertEqual(model.state!.days[0].activities[1].scheduledStart,locked.scheduledStart)
        let otherID=trip.schedule!.days[1].activities.first{$0.placeId != nil}!.placeId!
        model.place(trip.schedule!.candidates.first{$0.id==otherID}!,at:720)
        XCTAssertFalse(model.busy);XCTAssertFalse(model.state!.days[0].activities.contains{$0.placeId==otherID});model.stop()
    }
    func testImportedMapPoiIsIncludedInSharedApiRequest() async throws {
        let trip=try fixture();let model=ScheduleEditorModel();model.prepare(trip)
        var candidate=trip.schedule!.candidates[0];candidate.id="osm:node:123";candidate.source="OpenStreetMap";candidate.name="Test Cafe"
        var sent=false
        model.loader={request in let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];let candidates=body["candidates"] as! [[String:Any]];sent=candidates.contains{$0["id"] as? String==candidate.id};var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any];state["candidates"]=candidates;state["days"]=body["days"];raw["schedule"]=state;return (try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        model.importPlaces([candidate])
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertTrue(sent);XCTAssertEqual(model.state!.candidates.filter{$0.id==candidate.id}.count,1)
        model.importPlaces([candidate]);XCTAssertFalse(model.busy);model.stop()
    }
    func testToolboxPlacementUsesSpecifiedTimeAndDoesNotDuplicateSelectedPlaces() async throws {
        let trip=try fixture();let model=ScheduleEditorModel();model.prepare(trip)
        model.loader={request in let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any];state["days"]=body["days"];raw["schedule"]=state;return (try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let candidate=model.state!.candidates.first{$0.id=="museum"}!
        XCTAssertFalse(candidate.description!.isEmpty)
        model.place(candidate,at:660)
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        let added=model.state!.days[0].activities.first{$0.placeId==candidate.id}!
        XCTAssertEqual(added.fixedStart,660)
        model.place(candidate,at:720)
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(model.state!.days[0].activities.filter{$0.placeId==candidate.id}.count,1)
        XCTAssertEqual(model.state!.days[0].activities.first{$0.placeId==candidate.id}?.fixedStart,720)
        model.undo()
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(model.state!.days[0].activities.first{$0.placeId==candidate.id}?.fixedStart,660);model.stop()
    }
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

@MainActor private struct PoiProviderTestHost:View {
    @ObservedObject var store:AppStore
    var places:[ScheduleCandidate]
    var body:some View{PoiNativeMap(places:places,osm:store.mapProvider == .osm,center:CLLocationCoordinate2D(latitude:-33.87,longitude:151.20),zoom:14,onCamera:{_,_ in},onSelect:{_ in})}
}
