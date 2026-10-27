import XCTest
import SwiftUI
import MapKit
@testable import SydneyBuddy

@MainActor final class ScheduleTests:XCTestCase {
    func testSydneyDestinationUsesInterfaceLanguageWithoutRewritingOtherCities() {
        for alias in ["悉尼","雪梨","Sydney","SYDNEY"] {
            XCTAssertEqual(GuidedTripDetails.destinationDisplay(alias,language:.english),"Sydney")
        }
        XCTAssertEqual(GuidedTripDetails.destinationDisplay("Sydney",language:.simplified),"悉尼")
        XCTAssertEqual(GuidedTripDetails.destinationDisplay("墨尔本",language:.english),"墨尔本")
        XCTAssertEqual(GuidedTripDetails.destinationDisplay("",language:.english),"")
    }

    func testBudgetAmountNormalizesFullwidthNumbersAndChineseDecimalPoint() {
        XCTAssertEqual(BudgetAmountInput.canonicalAmount("１２５。５０"),"125.50")
        XCTAssertEqual(BudgetAmountInput.canonicalAmount(" ０ "),"0")
        XCTAssertEqual(BudgetAmountInput.canonicalAmount("12.50"),"12.50")
    }

    func testProposalReviewDoesNotPresentUnknownZeroPriceAsFree() throws {
        var plan=try fixture();plan.days[0].estimatedCost=0;plan.days[0].stops[0].costKnown=false
        XCTAssertEqual(ProposalReviewPresentation.cost([plan.days[0]]),"Cost to confirm")
        plan.recommendationStatus="partial"
        XCTAssertTrue(ProposalReviewPresentation.checks(plan).contains("Some requested experiences couldn’t fit this draft yet."))
        XCTAssertTrue(ProposalReviewPresentation.checks(plan).contains("Prices still to confirm."))
        plan.days[0].stops[0].costKnown=nil
        XCTAssertEqual(ProposalReviewPresentation.cost([plan.days[0]]),"Cost to confirm")
        plan.days[0].stops=[]
        XCTAssertEqual(ProposalReviewPresentation.cost([plan.days[0]]),"Cost to confirm")
    }

    func testExploreSearchControlsDoNotInterceptMapGestures() async throws {
        let restore=preservePlanningDefaults();defer{restore()}
        let store=AppStore();store.mapProvider = .apple;store.travelDataSource = .direct
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        window.rootViewController=UIHostingController(rootView:PoiDiscoveryView(embedded:true).environmentObject(store))
        window.makeKeyAndVisible();defer{window.isHidden=true;old?.makeKeyAndVisible()}
        try await Task.sleep(nanoseconds:700_000_000)
        func findMap(_ view:UIView)->MKMapView?{if let map=view as? MKMapView{return map};return view.subviews.compactMap{findMap($0)}.first}
        let map=try XCTUnwrap(findMap(window))
        let sample=window.convert(CGPoint(x:map.bounds.midX,y:map.bounds.height*0.45),from:map)
        var hit=window.hitTest(sample,with:nil);var mapReceivesTouch=false
        while let view=hit{if view===map{mapReceivesTouch=true;break};hit=view.superview}
        XCTAssertTrue(mapReceivesTouch,"Search and category controls must not cover the map’s gesture area")
    }

    func testExploreDrawerLeavesMapVisibleAndSupportsThreePositions() {
        XCTAssertLessThan(ExploreDrawer.collapsed.height(in:700),112)
        XCTAssertLessThan(ExploreDrawer.preview.height(in:700),350)
        XCTAssertLessThan(ExploreDrawer.expanded.height(in:700),600)
        XCTAssertEqual(ExploreDrawer.expanded.height(in:700,header:200),488)
        XCTAssertEqual(ExploreDrawer.collapsed.dragged(up:true),.preview)
        XCTAssertEqual(ExploreDrawer.preview.dragged(up:true),.expanded)
        XCTAssertEqual(ExploreDrawer.expanded.dragged(up:false),.preview)
        XCTAssertEqual(ExploreDrawer.preview.toggled(resultCount:1),.collapsed)
        XCTAssertEqual(ExploreDrawer.preview.toggled(resultCount:4),.expanded)
        XCTAssertTrue(PoiDiscoveryCategory.filters("all").isEmpty)
        XCTAssertEqual(PoiDiscoveryCategory.filters("food").count,4)
    }
    func testExploreRankingUsesRealInterestsAndKeepsSelectedPlaceVisible() {
        let centre=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
        let near=ScheduleCandidate(id:"near",name:"Near",category:"美食",indoor:false,visitMinutes:30,cost:0,lat:centre.latitude,lng:centre.longitude,reason:"")
        var far=near;far.id="far";far.category="自然";far.lat -= 0.01
        XCTAssertEqual(ExplorePresentation.ranked([near,far],interests:["自然"],center:centre,selected:nil).map(\.id),["far","near"])
        XCTAssertEqual(ExplorePresentation.ranked([far,near],interests:[],center:centre,selected:nil).map(\.id),["near","far"])
        XCTAssertEqual(ExplorePresentation.mapPlaces([near,far],selected:"far",limit:1).map(\.id),["far"])
        XCTAssertTrue(ExplorePresentation.mapPlaces([near,far],selected:"far",limit:0).isEmpty)
        XCTAssertTrue(ExplorePresentation.distance(near,from:centre).contains("search centre"))
        XCTAssertFalse(ExplorePresentation.distance(near,from:centre).contains("walk"))
    }
    func testExploreDoesNotShowUnknownPriceAsFreeAndClustersKeepSelectedSeparate() {
        var point=ScheduleCandidate(id:"one",name:"Unknown",category:"文化",indoor:false,visitMinutes:30,cost:0,lat:-33.865,lng:151.212,reason:"",source:"Apple Maps")
        XCTAssertNil(ExplorePresentation.price(point))
        point.estimatedSpendAud=38
        XCTAssertEqual(ExplorePresentation.price(point),"Est. A$38/person")
        var other=point;other.id="two"
        XCTAssertEqual(ExplorePresentation.clusters([point,other],zoom:14,selected:nil).count,1)
        XCTAssertEqual(ExplorePresentation.clusters([point,other],zoom:14,selected:"one").count,2)
        let subject=PoiNativeMap(places:[point,other],osm:false,center:.init(latitude:point.lat,longitude:point.lng),zoom:14,onCamera:{_,_ in},onSelect:{_ in})
        let map=MKMapView();let coordinator=subject.makeCoordinator()
        let cluster=MKClusterAnnotation(memberAnnotations:[PoiPin(point),PoiPin(other)])
        XCTAssertEqual((coordinator.mapView(map,viewFor:cluster) as? MKMarkerAnnotationView)?.glyphText,"2")
    }
    func testExploreFoodInsertionUsesDisplayedEstimateAndDoesNotTurnStaysIntoStops() throws {
        var state=try fixture().schedule!;var point=state.candidates[0];point.id="new-meal";point.category="美食";point.source="Apple Maps";point.estimatedSpendAud=48
        ExplorePresentation.insert(point,into:&state,day:0)
        let meal=try XCTUnwrap(state.days[0].activities.first{$0.placeId==point.id})
        XCTAssertEqual(meal.kind,"meal");XCTAssertEqual(meal.cost,48)
        point.id="new-hotel";point.category="住宿"
        ExplorePresentation.insert(point,into:&state,day:0)
        XCTAssertFalse(state.days[0].activities.contains{$0.placeId==point.id})
    }
    func testExploreBudgetFeedbackUsesActualLimitsAndDeduplicatesUnknownNotes() throws {
        var state=try fixture().schedule!;state.days[0].dailyBudget=12;state.days[0].estimatedCost=40
        state.issues=[ScheduleIssue(code:"budget",message:"中文原始提示",day:0,blocking:true),ScheduleIssue(code:"poi-unknown",message:"第一处未知",day:0,blocking:false),ScheduleIssue(code:"poi-unknown",message:"第二处未知",day:0,blocking:false)]
        let checks=ExplorePresentation.checks(state,day:0)
        XCTAssertEqual(checks.count,2);XCTAssertTrue(checks[0].blocking)
        XCTAssertTrue(checks[0].text.contains("A$40"));XCTAssertTrue(checks[0].text.contains("A$12"))
        XCTAssertFalse(checks[0].text.contains("中文"))
    }
    func testExploreManualAreaSearchKeepsResultsUntilUserRequestsRefresh() async throws {
        let model=PoiSearchModel();let point=try fixture().schedule!.candidates[0];var calls=0
        let data=try JSONSerialization.data(withJSONObject:["places":try JSONSerialization.jsonObject(with:JSONEncoder().encode([point]))])
        model.loader={request in calls+=1;return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        model.configure(.osm)
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:5_000_000)}
        let originalCentre=model.searchCenter
        model.cameraChanged(.init(latitude:-33.90,longitude:151.25),zoom:15,automaticallySearch:false)
        try await Task.sleep(nanoseconds:30_000_000)
        XCTAssertEqual(calls,1);XCTAssertEqual(model.places.map(\.id),[point.id]);XCTAssertFalse(model.busy)
        XCTAssertEqual(model.searchCenter.latitude,originalCentre.latitude)
        model.search()
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:5_000_000)}
        XCTAssertEqual(calls,2);XCTAssertEqual(model.searchCenter.latitude,-33.90,accuracy:0.000001);model.stop()
    }
    func testExploreInsertionChecksChosenDayWithoutSavingOrChangingOriginal() async throws {
        let original=try fixture();let encoder=JSONEncoder();encoder.outputFormatting = .sortedKeys;let before=try encoder.encode(original);let model=makeEditor();model.inlineEditing=true;model.prepare(original)
        let chosen=min(1,original.days.count-1);model.dayIndex=chosen;var calls=0
        var point=original.schedule!.candidates[0];point.id="explore:new";point.name="New place";point.source="OpenStreetMap"
        model.loader={request in
            calls+=1;XCTAssertEqual(request.url!.path,"/schedule/editor")
            let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            XCTAssertEqual(body["transportDays"] as? [Int],[chosen])
            var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any]
            state["days"]=body["days"];state["candidates"]=body["candidates"];raw["schedule"]=state
            return(try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        model.edit{state in ExplorePresentation.insert(point,into:&state,day:chosen)}
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertNil(model.error);XCTAssertEqual(calls,1)
        XCTAssertTrue(model.state!.days[chosen].activities.contains{$0.placeId==point.id})
        for index in original.days.indices where index != chosen{XCTAssertFalse(model.state!.days[index].activities.contains{$0.placeId==point.id})}
        XCTAssertEqual(try encoder.encode(original),before)
        var state=model.state!;ExplorePresentation.insert(point,into:&state,day:chosen);XCTAssertEqual(state.days.flatMap(\.activities).filter{$0.placeId==point.id}.count,1);model.stop()
    }

    func testDiscoveryZoomUsesCurrentCameraAndDoesNotRepeatWhenResultsRefresh() throws {
        let center=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
        let map=MKMapView(frame:CGRect(x:0,y:0,width:390,height:700))
        map.setRegion(MKCoordinateRegion(center:center,span:.init(latitudeDelta:0.02,longitudeDelta:0.02)),animated:false)
        var subject=PoiNativeMap(places:[],osm:false,center:center,zoom:14,onCamera:{_,_ in},onSelect:{_ in})
        let coordinator=subject.makeCoordinator();let original=map.region.span.longitudeDelta
        subject.cameraAction=RouteMapCameraAction(kind:.zoomIn)
        subject.applyCameraAction(map,coordinator:coordinator,animated:false)
        let zoomed=map.region.span.longitudeDelta
        XCTAssertLessThan(zoomed,original)
        subject.places=[try fixture().schedule!.candidates[0]]
        subject.applyCameraAction(map,coordinator:coordinator,animated:false)
        XCTAssertEqual(map.region.span.longitudeDelta,zoomed,accuracy:0.000001)
        subject.cameraAction=RouteMapCameraAction(kind:.zoomOut)
        subject.applyCameraAction(map,coordinator:coordinator,animated:false)
        XCTAssertGreaterThan(map.region.span.longitudeDelta,zoomed)
    }
    func testDiscoveryFitContainsResultsAndWorksWithNoResults() throws {
        let center=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
        let map=MKMapView(frame:CGRect(x:0,y:0,width:390,height:700))
        let points=Array(try fixture().schedule!.candidates.prefix(3))
        var subject=PoiNativeMap(places:points,osm:false,center:center,zoom:14,onCamera:{_,_ in},onSelect:{_ in})
        let coordinator=subject.makeCoordinator()
        subject.cameraAction=RouteMapCameraAction(kind:.fit)
        subject.applyCameraAction(map,coordinator:coordinator,animated:false)
        XCTAssertTrue(points.allSatisfy{map.visibleMapRect.contains(MKMapPoint(CLLocationCoordinate2D(latitude:$0.lat,longitude:$0.lng)))})
        subject.places=[];subject.cameraAction=RouteMapCameraAction(kind:.fit)
        subject.applyCameraAction(map,coordinator:coordinator,animated:false)
        XCTAssertTrue(map.visibleMapRect.contains(MKMapPoint(center)))
        XCTAssertGreaterThan(map.visibleMapRect.width*MKMetersPerMapPointAtLatitude(center.latitude),649)
    }
    func testMealReplacementDoesNotRecommendAnAttraction() {
        let museum=ScheduleCandidate(id:"museum",name:"Museum",category:"文化",indoor:true,visitMinutes:60,cost:0,lat:-33.86,lng:151.21,reason:"")
        var restaurant=museum;restaurant.id="restaurant";restaurant.category="美食";restaurant.activityType="meal"
        XCTAssertFalse(ScheduleSwapOptions.matches(museum,replacingKind:"meal"))
        XCTAssertTrue(ScheduleSwapOptions.matches(restaurant,replacingKind:"meal"))
        XCTAssertTrue(ScheduleSwapOptions.matches(museum,replacingKind:nil))
    }

    func testPlanningRunCorrelationSurvivesTripEncodingAndDoesNotLeakIntoOtherRequests() async throws {
        let store=AppStore();let id=UUID().uuidString.lowercased();var value=try fixture();value.planningRunId=id
        let data=try JSONEncoder().encode(value);XCTAssertEqual(try JSONDecoder().decode(Trip.self,from:data).planningRunId,id)
        var headers:[String?]=[]
        store.loader={request in headers.append(request.value(forHTTPHeaderField:"X-Planning-Run"));return(Data("{}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let _: [String:String] = try await PlanningRunScope.$id.withValue(id){try await store.api("catalog")}
        let _: [String:String] = try await store.api("catalog")
        let saved=try JSONSerialization.data(withJSONObject:["trip":["planningRunId":id]])
        let _: [String:String] = try await store.api("trips",method:"POST",body:saved)
        XCTAssertEqual(headers.count,3);XCTAssertEqual(headers[0],id);XCTAssertNil(headers[1]);XCTAssertEqual(headers[2],id)
    }

    func testInlineUndoDoesNotOpenTheGlobalLoadingScreen() async throws {
        let store=AppStore();var value=try fixture();value.id="inline-undo-test";value.revision=1
        store.trip=value;store.saved=[value];store.lastAdjustment=value
        var accepted=value;accepted.revision=2
        let data=try JSONEncoder().encode(accepted)
        store.loader={request in XCTAssertFalse(store.busy);return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let result=await store.undoLastAdjustment(inline:true)
        XCTAssertTrue(result);XCTAssertFalse(store.busy);XCTAssertNil(store.lastAdjustment);XCTAssertEqual(store.trip?.revision,2)
    }

    func testSmallEditNeverAutoSavesAnExistingManualDraftOrLegacyDemo() {
        XCTAssertTrue(ExperienceEditPolicy.automatic(unified:true,hasDraft:false,alreadyAutomatic:false))
        XCTAssertTrue(ExperienceEditPolicy.automatic(unified:true,hasDraft:true,alreadyAutomatic:true))
        XCTAssertFalse(ExperienceEditPolicy.automatic(unified:true,hasDraft:true,alreadyAutomatic:false))
        XCTAssertFalse(ExperienceEditPolicy.automatic(unified:false,hasDraft:false,alreadyAutomatic:false))
    }

    func testWorkspaceAdjustmentStaysInlineButNewTripsAndAdvancedEditorStillPresent() throws {
        let store=AppStore();var plan=try fixture();plan.id="workspace-trip"
        store.workspaceTripID=plan.id;store.proposalIsSuggestion=true;store.proposal=plan
        XCTAssertEqual(store.workspaceAdjustment?.id,plan.id);XCTAssertFalse(store.shouldPresentProposalSheet)
        store.proposalFromNewTrip=true
        XCTAssertNil(store.workspaceAdjustment);XCTAssertTrue(store.shouldPresentProposalSheet)
        store.proposalFromNewTrip=false;store.proposalIsSuggestion=false
        XCTAssertTrue(store.shouldPresentProposalSheet)
        store.proposalIsSuggestion=true;store.workspaceTripID="another-trip"
        XCTAssertTrue(store.shouldPresentProposalSheet)
        store.proposal=nil
        XCTAssertFalse(store.shouldPresentProposalSheet)
    }

    func testPersonalConversationSkipPreservesTripDraftAndDoesNotClaimMemoryWasSaved() throws {
        let defaults=UserDefaults.standard
        let keys=["koko.personalChat.finished","pendingTripFields","userTravelProfile","personalPreferences.v2.completed","personalPreferences.completed","onboarded","travelPlanningTask"]
        let old=keys.map{defaults.object(forKey:$0)};defer{for (key,value) in zip(keys,old){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        defaults.removeObject(forKey:"travelPlanningTask")
        let completedBefore=defaults.bool(forKey:"personalPreferences.v2.completed")
        let store=AppStore();store.showPersonalPreferences=true;store.pendingTripFields=["destination":"悉尼","budget":"120"];let profile=store.personalProfile
        store.finishPersonalConversation(nil,useClues:false)
        XCTAssertFalse(store.showPersonalPreferences);XCTAssertTrue(store.showOnboarding)
        XCTAssertEqual(store.pendingTripFields,["destination":"悉尼","budget":"120"])
        let encoder=JSONEncoder();encoder.outputFormatting = .sortedKeys
        XCTAssertEqual(try encoder.encode(store.personalProfile),try encoder.encode(profile))
        XCTAssertEqual(defaults.bool(forKey:"personalPreferences.v2.completed"),completedBefore)
        XCTAssertFalse(AppStore().showPersonalPreferences)
    }
    func testWorldReviewCarriesActualPlacesWithoutPretendingTheyAreVerified() throws {
        let trip=try fixture()
        let suggestion=WorldSuggestion(id:"verify-0",dayIndex:0,title:"待确认",detail:"费用未知",action:"review",status:"pending")
        let context=TripCompanionContext.checkRequest(trip:trip,suggestion:suggestion)
        XCTAssertTrue(context.contains(trip.days[0].stops[0].name));XCTAssertTrue(context.contains("不要声称已查到"));XCTAssertTrue(context.contains("不要重新生成行程"))
    }
    func testWorldChecksAreBoundToSavedTripRevision() throws {
        var trip=try fixture();trip.id="world-test";trip.revision=2
        let json=Data(#"{"id":"run-test","tripId":"world-test","tripRevision":2,"status":"completed","stale":false,"summary":{"walkingSteps":1000,"cost":20,"transportMinutes":10,"outdoorMinutes":0,"energyRisk":"low","budgetRisk":"unknown","weatherRisk":"unknown","source":"saved routes"},"suggestions":[],"decisions":[]}"#.utf8)
        var run=try JSONDecoder().decode(WorldRun.self,from:json)
        XCTAssertTrue(run.matches(trip));trip.revision=3;XCTAssertFalse(run.matches(trip))
        trip.revision=2;run.stale=true;XCTAssertFalse(run.matches(trip))
        run.stale=false;trip.id="another-trip";XCTAssertFalse(run.matches(trip))
    }
    func testDayAnchorEditsAreTrackedAndDoNotChangeOtherDays() throws {
        let trip=try fixture();var state=trip.schedule ?? ScheduleState.preserving(trip)
        let encoder=JSONEncoder();encoder.outputFormatting = .sortedKeys
        let original=try encoder.encode(state.days.dropFirst().map{$0})
        let unchanged=state.days[0]
        XCTAssertFalse(ScheduleAnchorEdit.apply(&state,day:0,kind:.start,minute:unchanged.startMinute,point:unchanged.start))
        XCTAssertTrue(ScheduleInlineDraft.changedDays(original:trip,draft:state).isEmpty)
        XCTAssertTrue(ScheduleAnchorEdit.apply(&state,day:0,kind:.start,minute:660,point:.central))
        XCTAssertTrue(state.days[0].startOverride == true)
        XCTAssertEqual(ScheduleInlineDraft.changedDays(original:trip,draft:state),[0])
        XCTAssertEqual(try encoder.encode(state.days.dropFirst().map{$0}),original)
        XCTAssertFalse(ScheduleAnchorEdit.apply(&state,day:0,kind:.end,minute:600,point:.quay))
        XCTAssertEqual(state.days[0].startMinute,660)
        XCTAssertEqual(ScheduleClock.minute(ScheduleClock.date(665)),665)
    }
    func testTripsHomeOpensTodayInsteadOfAlwaysDayOne() throws {
        var trip=try fixture();var first=trip.days[0];first.date="2026-10-27"
        var second=first;second.date="2026-10-28"
        var third=first;third.date="2026-10-30"
        trip.days=[first,second,third]
        XCTAssertEqual(TripHomePresentation.dayIndex(trip,today:"2026-10-28"),1)
        XCTAssertEqual(TripHomePresentation.dayIndex(trip,today:"2026-10-29"),2)
        XCTAssertEqual(TripHomePresentation.dayIndex(trip,today:"2026-10-31"),0)
        trip.days=[];XCTAssertEqual(TripHomePresentation.dayIndex(trip,today:"2026-10-28"),0)
    }
    func testTripsHomePreviewKeepsFeaturedOutAndPrefersUpcomingTrips() throws {
        var featured=try fixture();featured.id="featured";featured.profile.date="2026-10-27";featured.days[0].date="2026-10-27"
        var next=featured;next.id="next";next.profile.date="2026-10-29";next.days[0].date="2026-10-29"
        var past=featured;past.id="past";past.profile.date="2026-10-20";past.days[0].date="2026-10-20"
        let trips=[past,featured,next]
        XCTAssertEqual(TripHomePresentation.previewTrips(trips,featuredID:"featured",limit:2,today:"2026-10-27").map(\.id),["next","past"])
        XCTAssertEqual(TripHomePresentation.previewTrips(trips,featuredID:"featured",limit:1,today:"2026-10-27").map(\.id),["next"])
        XCTAssertTrue(TripHomePresentation.previewTrips(trips,featuredID:"featured",limit:0,today:"2026-10-27").isEmpty)
        XCTAssertEqual(TripHomePresentation.previewTrips(trips,featuredID:"featured",limit:1,today:"2026-10-27",priorityID:"past").map(\.id),["past"])
    }
    func testTripsShareStableOrderingAndReleaseNewTripPriority() throws {
        var current=try fixture();current.id="current";current.profile.date="2026-10-27";current.days=Array(current.days.prefix(1));current.days[0].date="2026-10-27"
        var future=current;future.id="future";future.profile.date="2026-11-01";future.days[0].date="2026-11-01"
        var past=current;past.id="past";past.profile.date="2026-10-20";past.days[0].date="2026-10-20"
        let trips=[past,future,current]
        XCTAssertEqual(TripHomePresentation.orderedTrips(trips,today:"2026-10-27",priorityID:"future").map(\.id),["future","current","past"])
        XCTAssertEqual(TripHomePresentation.orderedTrips(trips,today:"2026-10-27").map(\.id),["current","future","past"])
        XCTAssertEqual(TripHomePresentation.orderedTrips(trips,today:"2026-10-27",priorityID:"deleted").map(\.id),["current","future","past"])
        var earlier=current;earlier.id="earlier";earlier.profile.date="2026-10-25";earlier.days[0].date="2026-10-29"
        XCTAssertEqual(TripHomePresentation.orderedTrips([earlier,current,future],today:"2026-10-27").map(\.id),["current","earlier","future"])
        var same=current;same.id="newer";same.createdAt="2099-01-01T00:00:00Z"
        XCTAssertEqual(TripHomePresentation.orderedTrips([current,same],today:"2026-10-27").first?.id,"newer")
    }
    func testOnlyGeneratedTripTitlesAreLocalized() throws {
        var trip=try fixture();trip.title="悉尼 · 风景与自然 · 2天"
        XCTAssertEqual(TripHomePresentation.title(trip,language:.english),"Sydney · Nature · 2 days")
        XCTAssertTrue(TripLibrary.matches(trip,query:"Nature",date:nil))
        XCTAssertTrue(TripLibrary.matches(trip,query:"风景",date:nil))
        trip.title="Sydney · Food · 1天"
        XCTAssertEqual(TripHomePresentation.title(trip,language:.english),"Sydney · Food · 1 day")
        trip.title="我的毕业旅行"
        XCTAssertEqual(TripHomePresentation.title(trip,language:.english),"我的毕业旅行")
        XCTAssertEqual(TripHomePresentation.planningAction("review"),"Review draft")
        XCTAssertEqual(TripHomePresentation.planningAction("failed"),"View options & retry")
    }
    func testHomeFocusResumesCurrentTripAndDoesNotChoosePastOrDuplicateFuture() throws {
        var today=try fixture();today.id="current";today.profile.date="2026-10-25";today.days=Array(today.days.prefix(1));today.days[0].date="2026-10-25"
        var future=today;future.id="future";future.profile.date="2026-10-28";future.days[0].date="2026-10-28"
        var past=today;past.id="past";past.profile.date="2026-10-23";past.days[0].date="2026-10-23"
        XCTAssertEqual(TripHomeFocus.pick([future,past,today],lastOpenedID:"past",today:"2026-10-25")?.id,"current")
        XCTAssertEqual(TripHomeFocus.pick([past,future],lastOpenedID:"past",today:"2026-10-25")?.id,"future")
        XCTAssertNil(TripHomeFocus.pick([past],lastOpenedID:"past",today:"2026-10-25"))
        var other=today;other.id="other-current";XCTAssertEqual(TripHomeFocus.pick([today,other],lastOpenedID:other.id,today:"2026-10-25")?.id,other.id)
    }
    func testCompanionQuickActionsCarrySelectedDayAndRequireConfirmation() throws {
        let trip=try fixture();let index=min(1,trip.days.count-1)
        let request=TripCompanionContext.request(trip:trip,dayIndex:index,intent:.shortOnTime)
        XCTAssertTrue(request.contains(trip.days[index].date));XCTAssertTrue(request.contains("还剩多久"));XCTAssertTrue(request.contains("确认后再保存"));XCTAssertTrue(request.contains("保留其余日期"))
        XCTAssertTrue(TripCompanionContext.request(trip:trip,dayIndex:-1,intent:.replace).contains(trip.days[0].date))
    }
    func testConnectionsExplainBuffersWithoutChangingTheSchedule() throws {
        var trip=try fixture();trip.profile.startMinute=600
        var day=ScheduleState.preserving(trip).days[0]
        guard !day.activities.isEmpty else{return XCTFail("Fixture needs activities")}
        day.startMinute=600;day.activities[0].arrival="10:11"
        XCTAssertEqual(ScheduleConnection.buffer(day:day,position:0,travel:1),10)
        day.activities=Array(day.activities.prefix(1));day.activities[0].departure="11:11";day.finishTime="11:38"
        XCTAssertEqual(ScheduleConnection.buffer(day:day,position:1,travel:2),25)
        XCTAssertEqual(ScheduleConnection.buffer(day:day,position:1,travel:30),0)
    }
    func testRestaurantSocialSpendCanRemainUnknownWithoutBreakingPlaceDecoding() throws {
        let json="{\"menuItems\":[],\"reportedSpends\":[{\"amount\":null,\"currency\":\"\",\"quote\":\"around forty dollars\",\"source\":{\"url\":\"https://example.com/post\",\"name\":\"Test\",\"observedAt\":\"2026-10-25T00:00:00Z\"}}],\"recommendations\":[],\"reviews\":[],\"budgetEstimate\":{\"status\":\"unknown\"}}"
        let data=try JSONDecoder().decode(RestaurantData.self,from:Data(json.utf8));XCTAssertNil(data.reportedSpends[0].amount);XCTAssertEqual(data.budgetEstimate.status,"unknown")
    }
    func testSocialJsonPreviewDoesNotCallAiAndDraftRequiresConfirmation() async throws {
        let store=AppStore();let model=SocialImportModel();var paths:[String]=[]
        store.loader={request in paths.append(request.url!.path);return (Data("{\"id\":\"social:test\",\"status\":\"draft\",\"mentions\":[]}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let json="{\"platform\":\"threads\",\"postUrl\":\"https://www.threads.com/@test/post/test\",\"text\":\"Cafe\",\"mentions\":[{\"name\":\"Cafe\",\"quote\":\"Cafe\",\"kind\":\"restaurant\"}]}"
        await model.prepare(platform:"",url:"",text:"",json:json,jsonMode:true,store:store)
        XCTAssertNotNil(model.preview);XCTAssertNil(model.record);XCTAssertTrue(paths.isEmpty)
        await model.save(store:store);XCTAssertEqual(paths,["/social/import"]);XCTAssertEqual(model.record?["status"] as? String,"draft")
    }
    func testTripLibraryFiltersEveryTravelDayAndSearchesNames() throws {
        var trip=try fixture();trip.title="Old Sydney adventure"
        XCTAssertTrue(TripLibrary.matches(trip,query:"sYDney",date:nil))
        XCTAssertTrue(TripLibrary.matches(trip,query:"  ",date:trip.days.last!.date))
        XCTAssertTrue(TripLibrary.matches(trip,query:trip.days[0].stops[0].name,date:trip.days[0].date))
        XCTAssertFalse(TripLibrary.matches(trip,query:"Sydney",date:"1999-01-01"))
        XCTAssertFalse(TripLibrary.matches(trip,query:"no-such-place",date:nil))
        trip.days=[]
        XCTAssertEqual(TripLibrary.dates(trip),[trip.profile.date])
    }
    func testDiscoverySubcategoriesRetainParentAndUseExactSearchFilter() async throws {
        XCTAssertEqual(PoiDiscoveryCategory.details.count,12)
        XCTAssertEqual(PoiDiscoveryCategory.parent("cafes"),"food")
        XCTAssertEqual(PoiDiscoveryCategory.filters("toilets").map{$0.0},["toilets","drinking_water"])
        let model=PoiSearchModel();model.category="cafes"
        model.loader={request in
            let input=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            XCTAssertEqual(input["category"] as? String,"cafes")
            return(Data("{\"places\":[]}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        model.configure(.osm,dataSource:.zetaris)
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:5_000_000)}
        XCTAssertNil(model.error);model.stop()
        let unsupported=try await ApplePoiProvider.search(.init(latitude:-33.865,longitude:151.212),"drinking_water","")
        XCTAssertTrue(unsupported.isEmpty)
    }
    func testCreationDateUsesRecordedTimestampAndLocalTimeZone() throws {
        let utc=try XCTUnwrap(TimeZone(secondsFromGMT:0)),sydney=try XCTUnwrap(TimeZone(identifier:"Australia/Sydney"))
        XCTAssertEqual(TripCreationDate.formatted("2026-10-24T22:30:00.1234567+00:00",timeZone:utc),"2026-10-24 22:30")
        XCTAssertEqual(TripCreationDate.formatted("2026-10-24T22:30:00Z",timeZone:sydney),"2026-10-25 09:30")
        XCTAssertNil(TripCreationDate.formatted("unknown"));XCTAssertNil(TripCreationDate.formatted(""))
    }
    func testRenameConflictPreservesExistingNameDatesAndRoute() async throws {
        let store=AppStore();var trip=try fixture();trip.id="rename-conflict";trip.revision=3
        store.saved=[trip];store.trip=trip
        store.loader={request in (Data("{\"error\":\"版本已更新\"}".utf8),HTTPURLResponse(url:request.url!,statusCode:409,httpVersion:nil,headerFields:nil)!)}
        let success=await store.renameTrip(trip,to:"New name");XCTAssertFalse(success)
        XCTAssertEqual(store.saved.first?.title,trip.title);XCTAssertEqual(store.trip?.createdAt,trip.createdAt);XCTAssertEqual(store.trip?.profile.date,trip.profile.date);XCTAssertEqual(store.trip?.revision,3)
    }
    func testPlaceMediaAutomaticallyLoadsWhenCandidateHasNoPhotos() async throws {
        let model=PlaceMediaModel();let candidate=try fixture().schedule!.candidates[0];var calls=0
        let photo=PlacePhoto(url:"https://upload.wikimedia.org/test.jpg",pageUrl:"https://commons.wikimedia.org/wiki/File:Test.jpg",title:"Test photo",artist:"Test author",license:"CC BY-SA")
        let data=try JSONSerialization.data(withJSONObject:["photos":JSONSerialization.jsonObject(with:JSONEncoder().encode([photo]))])
        model.loader={request in calls+=1;XCTAssertEqual(request.url?.path,"/poi/media");XCTAssertEqual(request.value(forHTTPHeaderField:"X-Travel-Data-Source"),"zetaris");return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        await model.prepare(candidate,dataSource:.zetaris)
        XCTAssertEqual(calls,1);XCTAssertEqual(model.photos.first?.title,"Test photo");XCTAssertFalse(model.loading);XCTAssertNil(model.message)
    }
    func testPlaceMediaUsesExistingPhotosWithoutRequest() async throws {
        let model=PlaceMediaModel();var candidate=try fixture().schedule!.candidates[0]
        candidate.photos=[PlacePhoto(url:"https://upload.wikimedia.org/existing.jpg",pageUrl:"https://commons.wikimedia.org/wiki/File:Existing.jpg",title:"Existing",artist:"Author",license:"CC BY-SA")]
        model.loader={_ in XCTFail("Embedded photos should display immediately");throw URLError(.badServerResponse)}
        await model.prepare(candidate,dataSource:.direct)
        XCTAssertEqual(model.photos.first?.title,"Existing");XCTAssertNil(model.message)
    }
    func testPlaceMediaFailureCanRetryAndDistinguishesNoPhotos() async throws {
        let model=PlaceMediaModel();let candidate=try fixture().schedule!.candidates[0];var status=503
        model.loader={request in (Data("{\"photos\":[]}".utf8),HTTPURLResponse(url:request.url!,statusCode:status,httpVersion:nil,headerFields:nil)!)}
        await model.prepare(candidate,dataSource:.direct)
        XCTAssertEqual(model.message,"照片暫時無法載入，請稍後重試。");XCTAssertFalse(model.loading)
        status=200;await model.load(candidate,dataSource:.direct)
        XCTAssertEqual(model.message,"附近尚無可用的開放照片。");XCTAssertFalse(model.loading)
    }
    func testOldPlaceMediaResponseCannotReplaceNewPlacePhotos() async throws {
        let model=PlaceMediaModel();let first=try fixture().schedule!.candidates[0];var second=first;second.id="second-place"
        second.photos=[PlacePhoto(url:"https://upload.wikimedia.org/new.jpg",pageUrl:"https://commons.wikimedia.org/wiki/File:New.jpg",title:"New place",artist:"Author",license:"CC BY-SA")]
        model.loader={request in try? await Task.sleep(nanoseconds:60_000_000);return(Data("{\"photos\":[]}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let pending=Task{@MainActor in await model.prepare(first,dataSource:.direct)}
        try await Task.sleep(nanoseconds:10_000_000)
        await model.prepare(second,dataSource:.direct);await pending.value
        XCTAssertEqual(model.photos.first?.title,"New place");XCTAssertNil(model.message);XCTAssertFalse(model.loading)
    }
    func testNearbyPinsRefreshWithoutMovingRouteCameraOrSelectingTripStop() throws {
        let point=try fixture().schedule!.candidates[0]
        var selected:String?;var selectedPlace:String?
        var subject=NativeRouteMap(day:nil,provider:.apple,viewport:nil,onViewport:{_ in},onSelect:{selected=$0},nearbyPlaces:[point],onPoiSelect:{selectedPlace=$0.id})
        let delegate=subject.makeCoordinator(),map=MKMapView()
        let center=CLLocationCoordinate2D(latitude:-33.9,longitude:151.25)
        map.setRegion(MKCoordinateRegion(center:center,span:.init(latitudeDelta:0.02,longitudeDelta:0.02)),animated:false)
        let before=map.region
        delegate.refreshNearby(map)
        let pin=try XCTUnwrap(map.annotations.first as? PoiPin)
        delegate.mapView(map,didSelect:MKAnnotationView(annotation:pin,reuseIdentifier:nil))
        XCTAssertEqual(selectedPlace,point.id);XCTAssertNil(selected)
        subject.nearbyPlaces=[];delegate.parent=subject;delegate.refreshNearby(map)
        XCTAssertTrue(map.annotations.isEmpty)
        XCTAssertEqual(map.region.center.latitude,before.center.latitude,accuracy:0.000001)
        XCTAssertEqual(map.region.center.longitude,before.center.longitude,accuracy:0.000001)
    }
    func testViewportMovementDebouncesAndUsesLatestCenterAndRadius() async throws {
        let model=PoiSearchModel();model.cameraDelay=30_000_000
        var centers:[Double]=[];var radii:[Double]=[]
        model.appleSearch={point,_,_,radius in centers.append(point.latitude);radii.append(radius ?? 0);return []}
        model.configure(.apple)
        try await Task.sleep(nanoseconds:10_000_000);centers=[]
        model.cameraChanged(.init(latitude:-33.88,longitude:151.22),zoom:15)
        model.cameraChanged(.init(latitude:-33.90,longitude:151.24),zoom:16)
        try await Task.sleep(nanoseconds:100_000_000)
        XCTAssertEqual(centers,[-33.90]);XCTAssertGreaterThan(radii.last ?? 0,500);XCTAssertLessThan(radii.last ?? 0,5000)
        model.cameraChanged(.init(latitude:-33.90,longitude:151.24),zoom:16)
        try await Task.sleep(nanoseconds:60_000_000);XCTAssertEqual(centers.count,1)
        model.stop()
    }
    func testViewportMoveCancelsOldResultsBeforeDebouncedRequest() async throws {
        let model=PoiSearchModel();model.cameraDelay=20_000_000
        let candidate=try fixture().schedule!.candidates[0]
        model.appleSearch={point,_,_,_ in
            if point.latitude > -33.89{try? await Task.sleep(nanoseconds:100_000_000);return [candidate]}
            return []
        }
        model.configure(.apple)
        model.cameraChanged(.init(latitude:-33.91,longitude:151.23),zoom:15)
        try await Task.sleep(nanoseconds:160_000_000)
        XCTAssertTrue(model.places.isEmpty);XCTAssertFalse(model.busy);XCTAssertNil(model.error);model.stop()
    }
    func testViewportStopPreventsPendingBackendQuery() async throws {
        let model=PoiSearchModel();model.cameraDelay=40_000_000
        var calls=0
        model.loader={request in calls+=1;return(Data("{\"places\":[]}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        model.configure(.osm);try await Task.sleep(nanoseconds:20_000_000);calls=0
        model.cameraChanged(.init(latitude:-33.9,longitude:151.25),zoom:15);model.stop()
        try await Task.sleep(nanoseconds:80_000_000);XCTAssertEqual(calls,0)
    }
    func makeEditor()->ScheduleEditorModel {
        let model=ScheduleEditorModel()
        model.roadLoader={days in
            var seen=Set<String>();var values:[WalkingObservation]=[]
            for day in days {let points=[day.start]+day.stops.map{Coordinate(name:$0.name,lat:$0.lat,lng:$0.lng)}+[day.end]
                for i in 0..<(points.count-1) where seen.insert(AppleWalkingRoads.key(points[i],points[i+1])).inserted {values.append(WalkingObservation(from:points[i],to:points[i+1],meters:65,seconds:60,fetchedAt:ISO8601DateFormatter().string(from:Date())))}
            };return values
        };return model
    }
    func testUnknownPlaceFactsAreNotDisplayedAsFreeOrVerifiedIndoor() throws {
        var stop=try fixture().days[0].stops[0];stop.costKnown=false;stop.indoorKnown=false;stop.cost=0
        XCTAssertEqual(stop.costLabel,"费用待确认");XCTAssertEqual(stop.indoorLabel,"室内外待确认")
        stop.cost=30;XCTAssertEqual(stop.costLabel,"每人预留 A$30")
    }
    func testOpeningDynamicTripKeepsSearchSourceForLaterEditing() throws {
        var trip=try fixture();trip.schedule=nil
        trip.days[0].stops[0].id="apple:real-place"
        let stop=trip.days[0].stops[0]
        trip.planningCandidates=[ScheduleCandidate(id:stop.id,name:stop.name,category:stop.category,indoor:false,visitMinutes:stop.visitMinutes,cost:stop.cost,lat:stop.lat,lng:stop.lng,reason:"真实搜索",source:"Apple Maps",poiKind:"attraction")]
        let state=ScheduleState.preserving(trip)
        XCTAssertEqual(state.candidates.first?.source,"Apple Maps")
        XCTAssertEqual(state.candidates.first?.id,"apple:real-place")
    }
    func testConcreteRestaurantKeepsMealIdentityWhenOpeningAndEncodingSchedule() throws {
        var trip=try fixture();trip.schedule=nil
        trip.days[0].stops[0].activityType="meal";trip.days[0].stops[0].category="美食"
        trip.days[0].stops[0].sourceURL="https://example.com/restaurant"
        let restored=try JSONDecoder().decode(Trip.self,from:JSONEncoder().encode(trip))
        let schedule=ScheduleState.preserving(restored)
        XCTAssertEqual(schedule.days[0].activities[0].kind,"meal")
        XCTAssertEqual(schedule.candidates.first?.activityKind,"meal")
        XCTAssertEqual(restored.days[0].stops[0].sourceURL,"https://example.com/restaurant")
    }
    func testNewTripDoesNotAdjustExistingTripAndPartialRecommendationNeedsReview() async throws {
        let restoreDefaults=preservePlanningDefaults();defer{restoreDefaults()}
        var result=try fixture();result.recommendationStatus="partial";result.schedule=nil
        result.profile.end=result.profile.start
        var existing=result;existing.id="existing-trip";existing.revision=2
        let store=AppStore();store.trip=existing;store.mapProvider = .apple;store.showOnboarding=true
        let response=try JSONEncoder().encode(result);var paths:[String]=[]
        let shortlist=try JSONSerialization.data(withJSONObject:["candidateDays":Array(repeating:[String](),count:result.profile.days)])
        store.loader={request in
            let path=request.url!.path;paths.append(path)
            let data=path=="/catalog" ? Data("[]".utf8):path=="/plan/prepare" ? shortlist:response
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await store.generateFromPet(result.profile,request:"新的美食旅行",saveImmediately:true)
        XCTAssertTrue(paths.contains("/plan/prepare"));XCTAssertTrue(paths.contains("/plan"))
        XCTAssertFalse(paths.contains("/plan/remaining"));XCTAssertFalse(paths.contains("/trips"))
        XCTAssertEqual(store.trip?.id,"existing-trip");XCTAssertEqual(store.proposal?.recommendationStatus,"partial")
    }
    func testAcceptingNewPartialDraftCreatesTripInsteadOfOverwritingCurrentTrip() async throws {
        let defaults=UserDefaults.standard
        let keys=["pendingTripFields","travelPlanningTask","onboarded","petSession.draft","petSession.new-reviewed-trip","petComposer.draft"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for (key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        var existing=try fixture();existing.id="keep-existing";existing.revision=7
        var draft=existing;draft.id=nil;draft.revision=nil;draft.recommendationStatus="partial"
        var accepted=draft;accepted.id="new-reviewed-trip";accepted.revision=1
        let store=AppStore();store.trip=existing;store.proposal=draft;store.proposalFromNewTrip=true
        let data=try JSONEncoder().encode(accepted);let list=try JSONEncoder().encode([existing,accepted])
        store.loader={request in
            if request.httpMethod=="POST" {
                let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
                XCTAssertEqual(body["expectedRevision"] as? Int,0)
                XCTAssertNil((body["trip"] as? [String:Any])?["id"])
                return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
            }
            let result=request.url!.path=="/trips" ? list:request.url!.path=="/health" ? Data(#"{"status":"ok","nvidiaConfigured":false,"googleConfigured":false,"iosMapConfigured":false,"model":"test"}"#.utf8):Data("[]".utf8)
            return(result,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        await store.acceptProposal();XCTAssertNil(store.errorMessage)
        XCTAssertNil(store.tripToOpen);XCTAssertEqual(store.trip?.id,"keep-existing");XCTAssertTrue(store.saved.contains{$0.id=="keep-existing"&&$0.revision==7})
    }
    func testInlineRestInsertionUsesPreviousPlaceAndCanBeUndone() throws {
        let trip=try fixture();let model=makeEditor();model.prepare(trip)
        let before=model.state!.days[0].activities
        model.loader={_ in throw URLError(.cancelled)}
        model.addFacility("rest",at:1)
        let rest=model.state!.days[0].activities[1]
        XCTAssertEqual(rest.kind,"rest");XCTAssertEqual(rest.lat,before[0].lat);XCTAssertEqual(rest.lng,before[0].lng)
        XCTAssertEqual(model.state!.days[0].activities.count,before.count+1)
        model.undo();XCTAssertEqual(model.state!.days[0].activities.map(\.id),before.map(\.id));model.stop()
    }
    func testInlineSaveDoesNotReplaceAnotherOpenTrip() async throws {
        var original=try fixture();original.id="background-save";original.revision=1
        var candidate=original;candidate.schedule?.days[0].activities[0].duration+=10
        var other=original;other.id="another-open-trip"
        var accepted=candidate;accepted.revision=2
        let store=AppStore();store.trip=other;store.saved=[original,other]
        let data=try JSONEncoder().encode(accepted)
        store.loader={request in (data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        let success=await store.saveAdjustment(candidate,original:original,inline:true)
        XCTAssertTrue(success);XCTAssertEqual(store.trip?.id,"another-open-trip");XCTAssertEqual(store.saved.first?.revision,2)
    }
    func testInlineDraftKeepsUneditedDaysAndAppleTimes() throws {
        let original=try fixture();var draft=original.schedule!
        draft.days[0].activities[0].duration+=15
        let changed=ScheduleInlineDraft.changedDays(original:original,draft:draft)
        XCTAssertEqual(changed,[0])
        var calculated=original;calculated.schedule=draft
        calculated.days[0].finishTime="18:15";calculated.days[1].finishTime="23:59";calculated.schedule?.days[1].finishTime="23:59"
        let merged=ScheduleInlineDraft.preserveUntouched(calculated,original:original,changed:changed)
        XCTAssertEqual(merged.days[0].finishTime,"18:15")
        let encoder=JSONEncoder();encoder.outputFormatting = .sortedKeys
        XCTAssertEqual(try encoder.encode(merged.days[1]),try encoder.encode(original.days[1]))
        XCTAssertEqual(merged.schedule?.days[1].finishTime,original.schedule?.days[1].finishTime)
    }
    func testInlineDraftIgnoresCalculatedTimesWhenDetectingUserEdits() throws {
        let original=try fixture();var draft=original.schedule!
        draft.days[0].activities[0].arrival="23:00";draft.days[0].activities[0].travelMinutes=123
        XCTAssertTrue(ScheduleInlineDraft.changedDays(original:original,draft:draft).isEmpty)
        draft.days[0].activities.reverse()
        XCTAssertEqual(ScheduleInlineDraft.changedDays(original:original,draft:draft),[0])
    }
    func testInlineSaveStaysInWorkspaceAndRetainsOriginalOnConflict() async throws {
        var original=try fixture();original.id="inline-save";original.revision=3
        let store=AppStore();store.trip=original;store.saved=[original];store.dayIndex=1
        var accepted=original;accepted.revision=4
        let data=try JSONEncoder().encode(accepted)
        store.loader={request in
            XCTAssertFalse(store.busy)
            let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            XCTAssertEqual(body["expectedRevision"] as? Int,3)
            XCTAssertEqual(body["adjustedDays"] as? [Int],[0])
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        var candidate=original;candidate.schedule?.days[0].activities[0].duration+=15
        let ok=await store.saveAdjustment(candidate,original:original,inline:true)
        XCTAssertTrue(ok);XCTAssertNil(store.proposal);XCTAssertEqual(store.dayIndex,1);XCTAssertEqual(store.trip?.revision,4)
        store.loader={request in (Data(#"{"error":"行程已被其他修改更新"}"#.utf8),HTTPURLResponse(url:request.url!,statusCode:409,httpVersion:nil,headerFields:nil)!)}
        let failure=await store.saveAdjustment(original,original:original,inline:true)
        XCTAssertFalse(failure);XCTAssertEqual(store.trip?.revision,4);XCTAssertNotNil(store.adjustmentError)
    }
    func testAppleTimeUpdateKeepsOriginalOnFailureAndAcceptsServerSnapshot() async throws {
        var original=try fixture();original.schedule=nil;original.id="walking-time-test";original.revision=3
        let store=AppStore();store.trip=original;store.saved=[original]
        let preview=applePreview(original.days[0])
        store.loader={request in (Data(#"{"error":"超过步数上限"}"#.utf8),HTTPURLResponse(url:request.url!,statusCode:422,httpVersion:nil,headerFields:nil)!)}
        let failure=await store.updateWalkingTimes(preview,original:original,dayIndex:0);XCTAssertFalse(failure)
        XCTAssertEqual(store.trip?.revision,3);XCTAssertNotNil(store.adjustmentError)
        var accepted=original;accepted.revision=4;accepted.days[0].finishTime="14:00"
        let data=try JSONEncoder().encode(accepted)
        store.loader={request in
            let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            XCTAssertEqual(body["walkingTimeDay"] as? Int,0);XCTAssertEqual(body["expectedRevision"] as? Int,3)
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let success=await store.updateWalkingTimes(preview,original:original,dayIndex:0);XCTAssertTrue(success)
        XCTAssertEqual(store.trip?.revision,4);XCTAssertEqual(store.saved.first?.days[0].finishTime,"14:00");XCTAssertNil(store.adjustmentError)
    }
    func testManualScheduleCanApplyRoadTimesThroughValidatedEditorAndPartialSave() async throws {
        var original=try fixture();original.id="manual-calibration";original.revision=2
        let store=AppStore();store.trip=original;store.saved=[original];store.roadLegs={_ in []}
        var candidate=original;candidate.schedule?.issues=[]
        var accepted=candidate;accepted.revision=3
        let preview=applePreview(original.days[0]);var paths:[String]=[]
        store.loader={request in
            paths.append(request.url!.path)
            let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            if request.url!.path=="/schedule/editor" {XCTAssertNotNil(body["walkingRoutes"]);return (try JSONEncoder().encode(candidate),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
            XCTAssertEqual(body["adjustedDay"] as? Int,0);XCTAssertEqual(body["expectedRevision"] as? Int,2)
            return (try JSONEncoder().encode(accepted),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let success=await store.updateWalkingTimes(preview,original:original,dayIndex:0)
        XCTAssertTrue(success);XCTAssertEqual(paths,["/schedule/editor","/trips"]);XCTAssertEqual(store.trip?.revision,3)
        XCTAssertEqual(store.trip?.days[1].finishTime,original.days[1].finishTime)
    }
    func testDeleteFailureKeepsAcceptedTripAndList() async throws {
        var trip=try fixture();trip.id="delete-failure-test"
        let store=AppStore();store.trip=trip;store.saved=[trip]
        store.loader={request in (Data("{\"error\":\"删除失败\"}".utf8),HTTPURLResponse(url:request.url!,statusCode:500,httpVersion:nil,headerFields:nil)!)}
        let success=await store.deleteTrip(trip)
        XCTAssertFalse(success);XCTAssertEqual(store.trip?.id,trip.id);XCTAssertEqual(store.saved.count,1);XCTAssertNotNil(store.errorMessage)
    }
    func testDeleteSuccessRemovesOnlyTargetAndClearsNavigationReferences() async throws {
        var target=try fixture();target.id="delete-success-test"
        var other=target;other.id="keep-trip-test"
        let store=AppStore();store.saved=[target,other];store.trip=target;store.tripToOpen=target;store.lastAdjustment=target
        store.loader={request in
            XCTAssertEqual(request.httpMethod,"DELETE");XCTAssertEqual(request.url!.path,"/trips/delete-success-test")
            return (Data("{\"ok\":true}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let success=await store.deleteTrip(target)
        XCTAssertTrue(success);XCTAssertEqual(store.saved.map(\.id),[other.id]);XCTAssertNil(store.trip);XCTAssertNil(store.tripToOpen);XCTAssertNil(store.lastAdjustment)
    }
    func testCancelledKokoPlanningCannotSaveOrCelebrate() async {
        let store=AppStore();store.showOnboarding=true
        var saved=false
        store.loader={request in
            if request.url!.path == "/catalog" {try await Task.sleep(nanoseconds:5_000_000_000)}
            if request.url!.path == "/trips",request.httpMethod == "POST" {saved=true}
            throw URLError(.cancelled)
        }
        let operation=Task {await store.generateFromPet(TripProfile(),request:"cancel",saveImmediately:true)}
        await Task.yield();XCTAssertEqual(store.koalaWork?.action,.map)
        operation.cancel();await operation.value
        XCTAssertFalse(saved);XCTAssertFalse(store.busy);XCTAssertNil(store.koalaWork);XCTAssertNil(store.koalaFeedback)
    }
    func testRejectedKokoSaveDoesNotCelebrate() async throws {
        let store=AppStore();store.proposal=try fixture()
        store.loader={request in (Data(#"{"error":"revision conflict"}"#.utf8),HTTPURLResponse(url:request.url!,statusCode:409,httpVersion:nil,headerFields:nil)!)}
        await store.acceptProposal()
        XCTAssertNotNil(store.errorMessage);XCTAssertNil(store.koalaFeedback);XCTAssertFalse(store.busy)
    }
    func testSuccessfulBackgroundGenerationDoesNotOpenOrReplaceTheCurrentWorkspace() async throws {
        let restoreDefaults=preservePlanningDefaults();defer{restoreDefaults()}
        let defaults=UserDefaults.standard
        let keys=["pendingTripFields","travelPlanningTask","onboarded","petSession.draft","petSession.nav-workspace-trip"]
        let previous=keys.map{defaults.object(forKey:$0)}
        defer{for (key,value) in zip(keys,previous){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
        var accepted=try fixture();accepted.id="nav-workspace-trip";accepted.revision=1
        let data=try JSONEncoder().encode(accepted)
        let list=try JSONEncoder().encode([accepted])
        let store=AppStore()
        store.pendingTripFields=["destination":"悉尼","date":accepted.profile.date,"days":String(accepted.profile.days),"budget":"100","budgetCurrency":"AUD","budgetBasis":"perPersonPerDay","travelers":"1","pace":"约10000步","interests":"自然"]
        store.loader={request in
            let body:Data
            switch request.url!.path {
            case "/plan/prepare":body=try JSONSerialization.data(withJSONObject:["candidateDays":Array(repeating:[String](),count:accepted.profile.days)])
            case "/plan":body=data
            case "/trips":body=request.httpMethod=="POST" ? data:list
            case "/health":body=Data(#"{"status":"ok","nvidiaConfigured":false,"googleConfigured":false,"iosMapConfigured":false,"model":"test"}"#.utf8)
            default:body=Data("[]".utf8)
            }
            return (body,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        var old=accepted;old.id="viewed-old-trip";store.trip=old;store.tab=0
        await store.generatePendingTrip()
        XCTAssertNil(store.errorMessage)
        XCTAssertNil(store.tripToOpen);XCTAssertEqual(store.trip?.id,"viewed-old-trip");XCTAssertEqual(store.tab,0)
        XCTAssertNil(store.koalaWork);XCTAssertNil(store.koalaFeedback)
        XCTAssertEqual(store.travelTask?.status,"completed")
        XCTAssertTrue(store.pendingTripFields.isEmpty)
    }
    private func preservePlanningDefaults()->()->Void {
        let keys=["travelPlanningTask","pendingTripFields","userTravelProfile","travelDataSource","mapProvider"];let defaults=UserDefaults.standard;let values=keys.map{defaults.object(forKey:$0)}
        // Existing background fixtures stub the direct-source catalog, not a warehouse POI response.
        defaults.set("direct",forKey:"travelDataSource")
        return {for (key,value) in zip(keys,values){if let value{defaults.set(value,forKey:key)}else{defaults.removeObject(forKey:key)}}}
    }
    func testBackgroundTaskAllowsNavigationAndKeepsPartialResultsInsideItsCard() async throws {
        let restore=preservePlanningDefaults();defer{restore()}
        var value=try fixture();value.recommendationStatus="partial";value.id=nil
        let response=try JSONEncoder().encode(value)
        let store=AppStore();store.mapProvider = .apple;store.travelTask=nil;store.saved=[];store.proposal=nil
        store.pendingTripFields=["destination":"悉尼","date":value.profile.date,"days":String(value.profile.days),"budget":"100","budgetCurrency":"AUD","budgetBasis":"perPersonPerDay","travelers":"1","pace":"约10000步","interests":"自然"]
        let fields=store.pendingTripFields
        let originalSource=store.travelDataSource;var usedSources:[String]=[]
        var release:CheckedContinuation<Void,Never>?
        store.loader={request in
            usedSources.append(request.value(forHTTPHeaderField:"X-Travel-Data-Source") ?? "")
            let data:Data
            if request.url!.path=="/plan/prepare" {
                await withCheckedContinuation {release=$0}
                try Task.checkCancellation()
                data=try JSONSerialization.data(withJSONObject:["candidateDays":Array(repeating:[String](),count:value.profile.days)])
            }else if request.url!.path=="/plan" {data=response}else{data=Data("[]".utf8)}
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let task=Task{await store.generatePendingTrip()}
        while release==nil{await Task.yield()}
        XCTAssertTrue(store.isGeneratingTrip);XCTAssertFalse(store.busy)
        XCTAssertEqual(store.travelTask?.progress?.phase,.places)
        store.tab=3;store.personalProfile.dailySteps=6000;store.travelDataSource=originalSource == .direct ? .zetaris:.direct;store.mapProvider = .osm
        store.newTrip();XCTAssertEqual(store.pendingTripFields,fields)
        release?.resume();await task.value
        XCTAssertTrue(usedSources.allSatisfy{$0==originalSource.rawValue})
        XCTAssertEqual(store.tab,3);XCTAssertFalse(store.busy);XCTAssertNil(store.proposal);XCTAssertNil(store.errorMessage)
        XCTAssertEqual(store.travelTask?.status,"review");XCTAssertNotNil(store.travelTask?.draft)
        store.openPlanningDraft();XCTAssertNotNil(store.proposal);XCTAssertTrue(store.proposalFromNewTrip)
    }
    func testInterruptedCardCanRestoreItsOwnFieldsForEditing() {
        let restore=preservePlanningDefaults();defer{restore()}
        let store=AppStore();store.travelTask=TravelPlanningTask(status:"draft",fields:["destination":"悉尼","days":"2"]);store.pendingTripFields=[:]
        store.resumePendingTrip()
        XCTAssertEqual(store.pendingTripFields,["destination":"悉尼","days":"2"]);XCTAssertTrue(store.showOnboarding)
    }
    func testBackgroundFailureStaysOnTheCardWithoutGlobalAlert() async {
        let restore=preservePlanningDefaults();defer{restore()}
        let store=AppStore();store.travelTask=nil;store.pendingTripFields=["destination":"悉尼"]
        store.errorMessage=nil;store.tab=3
        await store.generatePendingTrip()
        XCTAssertEqual(store.travelTask?.status,"failed");XCTAssertNotNil(store.travelTask?.message)
        XCTAssertNil(store.errorMessage);XCTAssertNil(store.proposal);XCTAssertNil(store.tripToOpen);XCTAssertEqual(store.tab,3)
    }
    func testPoiToolboxRemovalPersistsAndAllowsAddingAgain() throws {
        let toolbox=PoiToolbox.shared;let original=toolbox.places
        defer{toolbox.places=original;if let data=try? JSONEncoder().encode(original){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
        let point=ScheduleCandidate(id:"toolbox-toggle-test",name:"Test",category:"美食",indoor:false,visitMinutes:30,cost:0,lat:-33.86,lng:151.21,reason:"Test",source:"Overture Maps")
        toolbox.add(point);toolbox.add(point);XCTAssertEqual(toolbox.places.filter{$0.id==point.id}.count,1)
        toolbox.remove(point.id);XCTAssertFalse(toolbox.places.contains{$0.id==point.id})
        let saved=try JSONDecoder().decode([ScheduleCandidate].self,from:UserDefaults.standard.data(forKey:"poi-toolbox")!)
        XCTAssertFalse(saved.contains{$0.id==point.id});toolbox.add(point);XCTAssertTrue(toolbox.places.contains{$0.id==point.id})
    }
    func testAppStartsOnTripsAndLanguagePersistsWithoutChangingTrip() throws {
        let defaults=UserDefaults.standard;let previous=defaults.string(forKey:"appLanguage")
        defer{if let previous{defaults.set(previous,forKey:"appLanguage")}else{defaults.removeObject(forKey:"appLanguage")}}
        let store=AppStore();XCTAssertEqual(store.tab,1)
        store.trip=try fixture();let ids=store.trip!.days.flatMap(\.stops).map(\.id)
        store.language = .english
        XCTAssertEqual(AppStore().language,.english)
        XCTAssertEqual(AppLanguage.english.text("地图"),"Map")
        XCTAssertEqual(AppLanguage.traditional.text("地图"),"地圖")
        XCTAssertEqual(AppLanguage.simplified.text("地图"),"地图")
        XCTAssertEqual(AppLanguage.english.text("第 {0} 天","3"),"Day 3")
        XCTAssertEqual(AppLanguage.english.text("Custom place"),"Custom place")
        XCTAssertEqual(store.trip!.days.flatMap(\.stops).map(\.id),ids)
    }
    func testPoiEnglishDisplayUsesSourceLanguageFieldsAndPreservesSavedIdentity() throws {
        let defaults=UserDefaults.standard;let previous=defaults.string(forKey:"appLanguage")
        defer{if let previous{defaults.set(previous,forKey:"appLanguage")}else{defaults.removeObject(forKey:"appLanguage")}}
        defaults.set("en",forKey:"appLanguage")
        let data=Data(#"{"id":"osm:node:301","name":"博物館","nameEn":"Museum","category":"風景","indoor":true,"visitMinutes":60,"cost":0,"lat":-33.86,"lng":151.21,"reason":"test","description":"中文介紹","descriptionEn":"Harbour collection"}"#.utf8)
        let point=try JSONDecoder().decode(ScheduleCandidate.self,from:data)
        XCTAssertEqual(point.displayName,"Museum");XCTAssertEqual(point.displayDescription,"Harbour collection")
        XCTAssertEqual(point.displayName(language:.traditional),"博物館")
        XCTAssertEqual(point.name,"博物館");XCTAssertEqual(point.id,"osm:node:301")
        XCTAssertEqual(AppLanguage.english.display("悉尼歌剧院"),"Sydney Opera House")
        let roundTrip=try JSONDecoder().decode(ScheduleCandidate.self,from:JSONEncoder().encode(point))
        XCTAssertEqual(roundTrip.nameEn,"Museum")
    }
    func testVisibleCopyUsesSelectedScriptAndKeepsPlaceholdersLiteral() {
        XCTAssertEqual(AppLanguage.simplified.display("儲存行程後，開啟地圖。"),"储存行程后，开启地图。")
        XCTAssertEqual(AppLanguage.traditional.display("保存行程后，打开地图。"),"保存行程後，打開地圖。")
        XCTAssertEqual(AppLanguage.english.display("生成行程"),"Generate itinerary")
        XCTAssertEqual(AppLanguage.english.display("第 3 天"),"Day 3")
        XCTAssertEqual(AppLanguage.english.display("第 3 天 · 2026-10-26"),"Day 3 · 2026-10-26")
        XCTAssertEqual(AppLanguage.english.text("{0} · {1} 天","Hotel {1}","3"),"Hotel {1} · 3 days")
        XCTAssertEqual(AppLanguage.english.display("数据来源已连线 · 8 个行程景点"),"Connected · 8 itinerary places")
        XCTAssertEqual(AppLanguage.english.display("继续 · 测试行程"),"Continue · 测试行程")
        let budget=AppLanguage.english.display("收到！预算 120 AUD（每人每天），先收进这次的小背包。换算为每人每日活动预算约 120 AUD。")
        XCTAssertFalse(budget.unicodeScalars.contains(where:{(0x3400...0x9FFF).contains($0.value)}),budget)
        XCTAssertEqual(AppLanguage.english.text("{0} · {1} 天","2026-10-26","1"),"2026-10-26 · 1 day")
        XCTAssertEqual(AppLanguage.english.display("旅行伙伴 Koko，正在思考"),"Your travel companion Koko, Thinking")
        let assembled="好，我们直接聊旅行。你不用先整理好心情，想到什么说什么就好。这份心情只放进这次的小背包。\n这次想去哪个城市？目前可以生成悉尼路线，也能先保存其他城市的想法。"
        let translated=AppLanguage.english.display(assembled)
        XCTAssertFalse(translated.unicodeScalars.contains(where:{(0x3400...0x9FFF).contains($0.value)}),translated)
        XCTAssertNotEqual(AppLanguage.english.display("选悉尼"),"选悉尼")
        XCTAssertEqual(AppLanguage.english.display("Kittyhawk"),"Kittyhawk")
        XCTAssertEqual(AppLanguage.english.display("參考收費：7 AUD · 非即時報價"),"Reference charge: 7 AUD · Not a live quote")
        XCTAssertEqual(AppLanguage.english.display("https://example.com"),"https://example.com")
    }
    func testPreferenceSummaryLocalizesForDisplayWithoutMutatingProfile() {
        var profile=UserTravelProfile();profile.queueTolerance="尽量不排队";profile.dailySteps=6000;profile.energy="低精力";profile.rhythm="轻松随意";profile.interests=["艺术","美食","自然"]
        let original=profile.summary
        let rendered=AppLanguage.english.display(original)
        XCTAssertFalse(rendered.unicodeScalars.contains(where:{(0x3400...0x9FFF).contains($0.value)}),rendered)
        XCTAssertTrue(rendered.contains("6000"))
        XCTAssertEqual(profile.summary,original)
    }
    func testChatChoiceLabelsLocalizeWithoutTranslatingTypedMessagesOrEvidence() {
        let typed=PetMessage(id:"typed",role:"user",text:"我想去自然公园")
        XCTAssertEqual(typed.displayText(language:.english),typed.text)
        let evidence=PetChoiceEvidence(questionMessageId:"q",questionText:"想怎么开始",optionId:"skip",optionText:"先聊旅行")
        let choice=PetMessage(id:"choice",role:"user",text:"先聊旅行",choice:evidence)
        XCTAssertNotEqual(choice.displayText(language:.english),choice.text)
        XCTAssertEqual(choice.text,evidence.optionText)
    }
    private func applePreview(_ day:DayPlan)->JourneyPlan {
        // Synthetic geometry for state tests only; real MapKit is exercised on the simulator.
        let points=[day.start.location]+day.stops.map(\.coordinate)+[day.end.location]
        let names=[day.start.name]+day.stops.map(\.name)+[day.end.name]
        let legs=(0..<(points.count-1)).map{i -> JourneyLeg in
            let step=JourneyStep(mode:"walk",line:nil,headsign:nil,from:names[i],to:names[i+1],departureTime:day.date+"T10:00:00Z",arrivalTime:day.date+"T10:01:00Z",realtime:false,stopCount:nil,meters:100,seconds:60,waitSeconds:0,platform:nil,agency:nil,geometry:[points[i],points[i+1]].map{JourneyCoordinate(lat:$0.latitude,lng:$0.longitude)},walkingDirections:[WalkingDirection(id:0,text:"测试步行指引",meters:100)])
            return JourneyLeg(index:i,mode:"walk",from:names[i],to:names[i+1],departureTime:step.departureTime,arrivalTime:step.arrivalTime,staySeconds:0,steps:[step])
        }
        return JourneyPlan(departureTime:legs.first!.departureTime,arrivalTime:legs.last!.arrivalTime,travelSeconds:Double(legs.count*60),elapsedSeconds:Double(legs.count*60),meters:Double(legs.count*100),legs:legs,warnings:[],source:"Apple Maps / MapKit")
    }
    func testTimeOnlyChangesReuseAppleRouteButMovedStopsRequestAgain() async throws {
        let day=try fixture().days[0],plan=applePreview(try fixture().days[0]);var calls=0
        let overview=ItineraryOverview(applePlan:{_,_ in calls+=1;return plan})
        overview.loadHome(day,provider:.apple,contentSource:"open")
        for _ in 0..<100{if !overview.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        var calibrated=day;calibrated.finishTime="14:10";calibrated.stops[0].arrival="10:09";calibrated.legs[0].source="Apple Maps / MapKit"
        overview.loadHome(calibrated,provider:.apple,contentSource:"open")
        XCTAssertEqual(calls,1);XCTAssertEqual(overview.key,mapSceneKey(calibrated));XCTAssertNotNil(overview.route)
        calibrated.stops[0].lat+=0.001;overview.loadHome(calibrated,provider:.apple,contentSource:"open")
        for _ in 0..<100{if !overview.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(calls,2);overview.cancel()
    }
    func testAppleRouteFailureRetriesWithoutCallingBackendOrChangingTrip() async throws {
        let trip=try fixture(),day=try fixture().days[0],plan=applePreview(try fixture().days[0]);var calls=0,backend=0
        let overview=ItineraryOverview(applePlan:{_,_ in calls+=1;if calls==1{throw URLError(.notConnectedToInternet)};return plan})
        overview.loader={_ in backend+=1;throw URLError(.unsupportedURL)}
        overview.loadHome(day,provider:.apple,contentSource:"open")
        for _ in 0..<100{if !overview.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertNil(overview.route);XCTAssertTrue(overview.message?.contains("Apple")==true)
        overview.load(day,provider:.apple)
        for _ in 0..<100{if !overview.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(calls,2);XCTAssertEqual(backend,0);XCTAssertEqual(overview.route?.meters,plan.meters);XCTAssertEqual(overview.plan?.source,"Apple Maps / MapKit")
        XCTAssertEqual(trip.days[0].stops.map(\.id),day.stops.map(\.id));overview.cancel()
    }
    func testSwitchingDaysCancelsStaleAppleRouteAndNeverShowsPreviousDay() async throws {
        let first=try fixture().days[0];var second=first;second.date="2026-11-15";second.stops.removeLast()
        let a=applePreview(first),b=applePreview(second)
        let overview=ItineraryOverview(applePlan:{day,_ in
            if day.date==first.date{try? await Task.sleep(nanoseconds:150_000_000);return a}
            return b
        })
        overview.loadHome(first,provider:.apple,contentSource:"open")
        try await Task.sleep(nanoseconds:10_000_000)
        overview.loadHome(second,provider:.apple,contentSource:"open")
        try await Task.sleep(nanoseconds:200_000_000)
        XCTAssertEqual(overview.key,mapSceneKey(second));XCTAssertEqual(overview.plan?.legs.count,b.legs.count)
        XCTAssertEqual(overview.route?.meters,b.meters);XCTAssertFalse(overview.busy);overview.cancel()
    }
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
    func testOpeningLegacyRouteAndRestoringDoNotReselectOrReschedule() async throws {
        var trip=try fixture();trip.schedule=nil
        let before=try JSONSerialization.jsonObject(with:JSONEncoder().encode(trip)) as! NSDictionary
        let model=makeEditor();var calls=0
        model.loader={_ in calls+=1;throw URLError(.notConnectedToInternet)}
        model.prepare(trip)
        try await Task.sleep(nanoseconds:100_000_000)
        XCTAssertEqual(calls,0);XCTAssertFalse(model.busy)
        XCTAssertEqual(try JSONSerialization.jsonObject(with:JSONEncoder().encode(model.trip!)) as! NSDictionary,before)
        XCTAssertEqual(model.state!.days.flatMap(\.activities).map(\.name),trip.days.flatMap(\.stops).map(\.name))
        XCTAssertEqual(model.state!.days.map(\.estimatedCost),trip.days.map(\.estimatedCost))
        XCTAssertEqual(model.state!.days.flatMap(\.activities).map(\.arrival),trip.days.flatMap(\.stops).map(\.arrival))
        model.restore();XCTAssertEqual(calls,0)
        XCTAssertEqual(try JSONSerialization.jsonObject(with:JSONEncoder().encode(model.trip!)) as! NSDictionary,before)
        XCTAssertNil(model.trip!.schedule)
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
    func testOpenSearchUsesOvertureVersionAndPreservesContactFieldsForToolbox() async throws {
        let model=PoiSearchModel();var candidate=try fixture().schedule!.candidates[0]
        candidate.id="overture:00000000-0000-4000-8000-000000000001";candidate.source="Overture Maps";candidate.category="美食";candidate.poiKind="cafe";candidate.website="https://example.com";candidate.phone="test-phone";candidate.sourceRelease="2026-10-21.0";candidate.cost=0
        model.loader={request in
            let places=try JSONSerialization.jsonObject(with:JSONEncoder().encode([candidate]))
            let data=try JSONSerialization.data(withJSONObject:["places":places,"source":"Overture Maps / local snapshot","attribution":"Overture Maps Foundation","metadata":["release":"2026-10-21.0"]])
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        model.configure(.osm)
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        let result=try XCTUnwrap(model.places.first)
        XCTAssertEqual(model.source,"Overture Maps / local snapshot");XCTAssertEqual(model.dataVersion,"2026-10-21.0")
        XCTAssertEqual(result.website,"https://example.com");XCTAssertEqual(result.phone,"test-phone");XCTAssertTrue(result.importedPoi);XCTAssertEqual(result.activityKind,"meal");model.stop()
    }
    func testCommunityDiscoveryPreservesGuideAndUsesBackendOnAppleBaseMap() async throws {
        let model=PoiSearchModel();model.category="gems";var calls=0
        var point=try fixture().schedule!.candidates[0];point.id="gem:"+String(repeating:"a",count:24);point.source="Wikivoyage"
        point.guide=CommunityGuide(directions:"Public path by the wharf",sourceUrl:"https://en.wikivoyage.org/w/index.php?oldid=1",revision:"1",license:"CC BY-SA 4.0",licenseUrl:"https://creativecommons.org/licenses/by-sa/4.0/",attribution:"Wikivoyage contributors",retrievedAt:"now")
        point.photos=[PlacePhoto(url:"https://thumb.wikimedia.org/example.jpg",pageUrl:"https://commons.wikimedia.org/wiki/File:Example.jpg",title:"Example",artist:"Author",license:"CC BY-SA 4.0")];point.reviews=[]
        model.appleSearch={_,_,_,_ in XCTFail("Community collection must use backend");return []}
        model.loader={request in
            calls+=1;XCTAssertEqual(request.value(forHTTPHeaderField:"X-Travel-Data-Source"),"direct")
            let input=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];XCTAssertEqual(input["category"] as? String,"gems");XCTAssertEqual(input["radius"] as? Int,5000)
            let data=try JSONSerialization.data(withJSONObject:["places":JSONSerialization.jsonObject(with:JSONEncoder().encode([point]))])
            return(data,HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        model.configure(.apple,dataSource:.direct)
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(calls,1);let result=try XCTUnwrap(model.places.first);XCTAssertTrue(result.importedPoi);XCTAssertEqual(result.guide?.directions,"Public path by the wharf");XCTAssertEqual(result.photos?.first?.artist,"Author");XCTAssertEqual(result.reviews?.count,0);model.stop()
    }
    func testZetarisSearchUsesBackendEvenWithAppleMap() async throws {
        let model=PoiSearchModel();var calls=0
        model.appleSearch={_,_,_,_ in XCTFail("Zetaris must not call native Apple search");return []}
        model.loader={request in
            calls+=1;XCTAssertEqual(request.value(forHTTPHeaderField:"X-Travel-Data-Source"),"zetaris")
            return(Data("{\"places\":[],\"source\":\"Overture Maps / Zetaris\",\"attribution\":\"Overture Maps Foundation\"}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        model.configure(.apple,dataSource:.zetaris)
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(calls,1);XCTAssertNil(model.error);XCTAssertEqual(model.source,"Open travel data / Zetaris");model.stop()
        var request=URLRequest(url:URL(string:"http://localhost")!);TravelDataSource.direct.apply(to:&request)
        XCTAssertEqual(request.value(forHTTPHeaderField:"X-Travel-Data-Source"),"direct")
    }
    func testPoiSearchUsesAppleWithoutBackendAndClearsOnGoogleAndOfflineSwitch() async throws {
        let model=PoiSearchModel();var appleCalls=0;var backendCalls=0
        var candidate=try fixture().schedule!.candidates[0];candidate.id="apple:test";candidate.source="Apple Maps"
        model.appleSearch={_,kind,_,_ in appleCalls+=1;XCTAssertEqual(kind,"all");return [candidate]}
        model.loader={request in backendCalls+=1;let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];XCTAssertEqual(body["provider"] as? String,"google");return(Data("{\"places\":[],\"attribution\":\"Google Maps\"}".utf8),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        model.configure(.apple)
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(appleCalls,1);XCTAssertEqual(backendCalls,0);XCTAssertEqual(model.places.first?.source,"Apple Maps")
        model.configure(.google);XCTAssertTrue(model.places.isEmpty);XCTAssertFalse(model.hasSearched);XCTAssertEqual(backendCalls,0)
        model.search()
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(backendCalls,1);XCTAssertEqual(model.attribution,"Google Maps")
        model.loader={request in let data=try JSONEncoder().encode([candidate]);return(try JSONSerialization.data(withJSONObject:["places":JSONSerialization.jsonObject(with:data)]),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        model.configure(.google);model.search()
        for _ in 0..<100{if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertTrue(model.places.isEmpty);XCTAssertNotNil(model.error)
        model.configure(.schematic);model.search();XCTAssertEqual(backendCalls,1);XCTAssertNotNil(model.error)
        model.configure(.google,googleMapConfigured:false);model.search();XCTAssertEqual(backendCalls,1);XCTAssertNotNil(model.error)
        model.stop()
    }
    func testSlowApplePoiCannotOverwriteNewProviderResults() async throws {
        let model=PoiSearchModel();let candidate=try fixture().schedule!.candidates[0]
        model.appleSearch={_,_,_,_ in try? await Task.sleep(nanoseconds:100_000_000);return [candidate]}
        model.configure(.apple);try await Task.sleep(nanoseconds:10_000_000)
        model.configure(.google)
        try await Task.sleep(nanoseconds:160_000_000)
        XCTAssertTrue(model.places.isEmpty);XCTAssertFalse(model.busy);XCTAssertNil(model.error);model.stop()
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
        let map=try XCTUnwrap(findMap(window));XCTAssertEqual(map.overlays.filter{$0 is OsmTiles}.count,1);XCTAssertTrue(map.selectableMapFeatures.isEmpty);XCTAssertEqual(Set(map.annotations.compactMap{($0 as? PoiPin)?.point.id}),Set(points.map(\.id)))
        let center=map.centerCoordinate
        store.mapProvider = .apple;try await Task.sleep(nanoseconds:250_000_000)
        XCTAssertTrue(map.overlays.isEmpty);XCTAssertTrue(map.selectableMapFeatures.contains(.pointsOfInterest));XCTAssertEqual(Set(map.annotations.compactMap{($0 as? PoiPin)?.point.id}),Set(points.map(\.id)))
        XCTAssertEqual(map.centerCoordinate.latitude,center.latitude,accuracy:0.0001);XCTAssertEqual(map.centerCoordinate.longitude,center.longitude,accuracy:0.0001)
        store.mapProvider = .osm;try await Task.sleep(nanoseconds:250_000_000)
        XCTAssertEqual(map.overlays.filter{$0 is OsmTiles}.count,1);XCTAssertTrue(map.selectableMapFeatures.isEmpty);XCTAssertEqual(Set(map.annotations.compactMap{($0 as? PoiPin)?.point.id}),Set(points.map(\.id)))
    }
    func testPoiFocusMovesToVisibleAreaAndKeepsSelectionWithoutSearching() async throws {
        let points=try fixture().schedule!.candidates
        let state=PoiFocusTestState();let store=AppStore();let original=store.mapProvider;defer{store.mapProvider=original};store.mapProvider = .apple
        let scene=UIApplication.shared.connectedScenes.compactMap{$0 as? UIWindowScene}.first!
        let old=scene.windows.first{$0.isKeyWindow};let window=UIWindow(windowScene:scene);window.frame=scene.screen.bounds
        window.rootViewController=UIHostingController(rootView:PoiFocusTestHost(state:state,store:store,places:points).frame(height:500))
        window.makeKeyAndVisible();defer{window.isHidden=true;old?.makeKeyAndVisible()}
        try await Task.sleep(nanoseconds:500_000_000)
        func findMap(_ view:UIView)->MKMapView?{if let map=view as? MKMapView{return map};return view.subviews.compactMap{findMap($0)}.first}
        let map=try XCTUnwrap(findMap(window));let baseline=state.cameraChanges
        let target=points.last!;state.focus=PoiMapFocus(point:target)
        try await Task.sleep(nanoseconds:1_500_000_000)
        let pin=try XCTUnwrap(map.annotations.compactMap{$0 as? PoiPin}.first{$0.point.id==target.id})
        XCTAssertEqual(map.selectedAnnotations.compactMap{$0 as? PoiPin}.first?.point.id,target.id)
        XCTAssertEqual(state.cameraChanges,baseline,"Programmatic focus must not trigger a fresh search")
        XCTAssertEqual(map.annotations.compactMap{$0 as? PoiPin}.count,points.count)
        let position=map.convert(pin.coordinate,toPointTo:map)
        XCTAssertGreaterThan(position.y,map.bounds.height*0.29);XCTAssertLessThan(position.y,map.bounds.height*0.65)
        XCTAssertEqual((map.view(for:pin) as? MKMarkerAnnotationView)?.markerTintColor,UIColor.systemBlue)
        store.mapProvider = .osm;try await Task.sleep(nanoseconds:300_000_000)
        XCTAssertEqual(map.overlays.filter{$0 is OsmTiles}.count,1)
        state.focus=PoiMapFocus(point:points.first!)
        try await Task.sleep(nanoseconds:1_500_000_000)
        XCTAssertEqual(map.selectedAnnotations.compactMap{$0 as? PoiPin}.first?.point.id,points.first!.id)
        XCTAssertEqual(state.cameraChanges,baseline)
    }
    func testAppleFeatureItemUsesActualIdentityLocationAndContactWithoutInventingPrice() throws {
        let item=MKMapItem(placemark:MKPlacemark(coordinate:.init(latitude:-33.864,longitude:151.217)))
        item.name="Visitor Centre";item.phoneNumber="+61 2 0000 0000";item.url=URL(string:"https://example.com/visitor")
        let point=try XCTUnwrap(ApplePoiProvider.candidate(item))
        XCTAssertTrue(point.id.hasPrefix("apple:"));XCTAssertEqual(point.name,"Visitor Centre")
        XCTAssertEqual(point.lat,-33.864,accuracy:0.000001);XCTAssertEqual(point.lng,151.217,accuracy:0.000001)
        XCTAssertEqual(point.website,item.url?.absoluteString);XCTAssertEqual(point.phone,item.phoneNumber)
        XCTAssertEqual(point.cost,0);XCTAssertNil(point.openMinute);XCTAssertNotEqual(point.poiKind,"toilets")
        XCTAssertEqual(ApplePoiProvider.candidate(item)?.id,point.id)
        item.name="Other Centre";XCTAssertNotEqual(ApplePoiProvider.candidate(item)?.id,point.id)
    }
    func testLateAppleFeatureCannotReplaceNewSelectionAndFailureIsVisible() async throws {
        let points=try fixture().schedule!.candidates;let lookup=AppleFeatureLookup();var selected:String?;var message:String?
        lookup.load(operation:{try? await Task.sleep(nanoseconds:100_000_000);return points[0]},onResult:{selected=$0.id},onStatus:{_,error in message=error})
        lookup.load(operation:{points[1]},onResult:{selected=$0.id},onStatus:{_,error in message=error})
        try await Task.sleep(nanoseconds:150_000_000);XCTAssertEqual(selected,points[1].id);XCTAssertNil(message)
        lookup.load(operation:{throw URLError(.notConnectedToInternet)},onResult:{selected=$0.id},onStatus:{_,error in message=error})
        try await Task.sleep(nanoseconds:20_000_000);XCTAssertNotNil(message);XCTAssertEqual(selected,points[1].id)
        lookup.cancel()
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
        let trip=try fixture();let model=makeEditor();model.prepare(trip)
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
        let trip=try fixture();let model=makeEditor();model.prepare(trip)
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
        let model=makeEditor();model.prepare(trip)
        let locked=trip.schedule!.days[0].activities[1]
        let same=trip.schedule!.candidates.first{$0.id==locked.placeId}!
        model.place(same,at:720)
        XCTAssertFalse(model.busy);XCTAssertEqual(model.state!.days[0].activities[1].scheduledStart,locked.scheduledStart)
        let otherID=trip.schedule!.days[1].activities.first{$0.placeId != nil}!.placeId!
        model.place(trip.schedule!.candidates.first{$0.id==otherID}!,at:720)
        XCTAssertFalse(model.busy);XCTAssertFalse(model.state!.days[0].activities.contains{$0.placeId==otherID});model.stop()
    }
    func testDiscoveredRestaurantIsImportedAndAddedInOnePreviewWithMealAllowance() async throws {
        let trip=try fixture();let model=makeEditor();model.prepare(trip)
        var candidate=trip.schedule!.candidates[0];candidate.id="osm:node:456";candidate.source="OpenStreetMap";candidate.category="美食";candidate.name="Test Cafe";candidate.cost=0
        var calls=0
        model.loader={request in
            calls+=1
            let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            let candidates=body["candidates"] as! [[String:Any]]
            XCTAssertTrue(candidates.contains{$0["id"] as? String==candidate.id})
            let days=body["days"] as! [[String:Any]]
            let activities=days[0]["activities"] as! [[String:Any]]
            let meal=try XCTUnwrap(activities.first{$0["placeId"] as? String==candidate.id})
            XCTAssertEqual(meal["kind"] as? String,"meal")
            XCTAssertEqual(meal["cost"] as? Double,trip.schedule!.settings.mealCost)
            var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any];state["candidates"]=candidates;state["days"]=days;raw["schedule"]=state
            return (try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        model.importAndAdd(candidate)
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertEqual(calls,1);XCTAssertEqual(model.state!.days[0].activities.filter{$0.placeId==candidate.id}.count,1)
        model.restore();XCTAssertFalse(model.state!.days[0].activities.contains{$0.placeId==candidate.id});model.stop()
    }
    func testImportedMapPoiIsIncludedInSharedApiRequest() async throws {
        let trip=try fixture();let model=makeEditor();model.prepare(trip)
        var candidate=trip.schedule!.candidates[0];candidate.id="osm:node:123";candidate.source="OpenStreetMap";candidate.name="Test Cafe"
        var sent=false
        model.loader={request in let body=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any];let candidates=body["candidates"] as! [[String:Any]];sent=candidates.contains{$0["id"] as? String==candidate.id};var raw=body["trip"] as! [String:Any];var state=raw["schedule"] as! [String:Any];state["candidates"]=candidates;state["days"]=body["days"];raw["schedule"]=state;return (try JSONSerialization.data(withJSONObject:raw),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)}
        model.importPlaces([candidate])
        for _ in 0..<100 {if !model.busy{break};try await Task.sleep(nanoseconds:10_000_000)}
        XCTAssertTrue(sent);XCTAssertEqual(model.state!.candidates.filter{$0.id==candidate.id}.count,1)
        model.importPlaces([candidate]);XCTAssertFalse(model.busy);model.stop()
    }
    func testToolboxPlacementUsesSpecifiedTimeAndDoesNotDuplicateSelectedPlaces() async throws {
        let trip=try fixture();let model=makeEditor();model.prepare(trip)
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
        let trip=try fixture();let model=makeEditor();model.prepare(trip)
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
    func testRenamingPreservesRouteAndUsesExpectedRevision() async throws {
        let store=AppStore();var original=try fixture();original.id="rename-test";original.revision=3
        store.saved=[original];store.trip=original
        store.loader={request in
            let raw=try JSONSerialization.jsonObject(with:request.httpBody!) as! [String:Any]
            XCTAssertEqual(raw["expectedRevision"] as? Int,3)
            var renamed=try JSONDecoder().decode(Trip.self,from:JSONSerialization.data(withJSONObject:raw["trip"]!))
            XCTAssertEqual(raw["renameOnly"] as? Bool,true);XCTAssertEqual(renamed.createdAt,original.createdAt);XCTAssertEqual(renamed.profile.date,original.profile.date)
            XCTAssertEqual(renamed.title,"海风假期");XCTAssertEqual(renamed.days.count,original.days.count)
            XCTAssertEqual(renamed.days[0].stops.map(\.id),original.days[0].stops.map(\.id))
            renamed.revision=4
            return (try JSONEncoder().encode(renamed),HTTPURLResponse(url:request.url!,statusCode:200,httpVersion:nil,headerFields:nil)!)
        }
        let result=await store.renameTrip(original,to:"  海风假期  ")
        XCTAssertTrue(result);XCTAssertEqual(store.saved.first?.title,"海风假期");XCTAssertEqual(store.trip?.revision,4)
    }
    func testAdjustmentMergePreservesEveryOtherDayAndWeather() throws {
        let original=try fixture();var edited=original
        edited.days[0].stops=[];edited.days[1].estimatedSteps=99999;edited.days[2].stops=[]
        let merged=TripAdjustmentMerge.preservingOtherDays(edited,original:original,dayIndex:0)
        let encoder=JSONEncoder();encoder.outputFormatting=[.sortedKeys]
        XCTAssertTrue(merged.days[0].stops.isEmpty)
        XCTAssertEqual(try encoder.encode(merged.days[1]),try encoder.encode(original.days[1]))
        XCTAssertEqual(try encoder.encode(merged.days[2]),try encoder.encode(original.days[2]))
        XCTAssertEqual(merged.weather.summary,original.weather.summary)
    }
    func testFailedAdjustmentSaveLeavesAcceptedTripUntouched() async throws {
        let store=AppStore();var original=try fixture();original.id="adjustment-test";original.revision=2
        store.trip=original;store.saved=[original];var changed=original;changed.days[0].stops=[]
        store.loader={request in (Data(#"{"error":"版本已更新"}"#.utf8),HTTPURLResponse(url:request.url!,statusCode:409,httpVersion:nil,headerFields:nil)!)}
        let success=await store.saveAdjustment(changed,original:original)
        XCTAssertFalse(success);XCTAssertEqual(store.trip?.days[0].stops.count,original.days[0].stops.count);XCTAssertNil(store.lastAdjustment)
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
        let model=makeEditor();let trip=try fixture();model.prepare(trip)
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
        let model=makeEditor();let original=try fixture();model.prepare(original)
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
        let model=makeEditor();model.prepare(trip)
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

final class TravelInsightContractTests:XCTestCase {
    func testUnknownAmountsDoNotBecomeFreeAndPartialDataDecodes() throws {
        let data=Data(#"{"dataSource":"zetaris","fetchedAt":"now","scope":"current reports","traffic":{"status":"unavailable","summary":"unknown","source":"NSW","items":[]},"places":[{"id":"a","name":"Museum","price":{"status":"unknown","summary":"not included","source":"OSM","amount":null},"crowd":{"status":"unknown","summary":"unknown"},"googleFallback":"paid-services-disabled"}],"pricePlaces":[],"attribution":"OSM"}"#.utf8)
        let result=try JSONDecoder().decode(TravelInsights.self,from:data)
        XCTAssertEqual(result.places[0].price.status,"unknown");XCTAssertEqual(result.places[0].crowd.status,"unknown");XCTAssertTrue(result.traffic.items.isEmpty)
    }
}

@MainActor private final class PoiFocusTestState:ObservableObject {@Published var focus:PoiMapFocus?;var cameraChanges=0}
@MainActor private struct PoiFocusTestHost:View {
    @ObservedObject var state:PoiFocusTestState
    @ObservedObject var store:AppStore
    var places:[ScheduleCandidate]
    var body:some View{PoiNativeMap(places:places,osm:store.mapProvider == .osm,center:.init(latitude:-33.87,longitude:151.20),zoom:14,onCamera:{_,_ in state.cameraChanges+=1},onSelect:{_ in},focus:state.focus)}
}

final class ActivityReferenceContractTests:XCTestCase {
    func testUnknownAndFreePriceDecodeSeparately() throws {
        let fields:[String:Any] = ["id":"test","title":"Test admission","venueName":"Test venue","currency":"AUD","ticketType":"General entry","conditions":"Test","sourceName":"Official","sourceUrl":"https://example.com","observedOn":"2026-10-25","stale":false,"distanceMeters":0]
        var unknown=fields;unknown["amount"]=NSNull()
        XCTAssertNil(try JSONDecoder().decode(ActivityReference.self,from:JSONSerialization.data(withJSONObject:unknown)).amount)
        var free=fields;free["amount"]=0
        XCTAssertEqual(try JSONDecoder().decode(ActivityReference.self,from:JSONSerialization.data(withJSONObject:free)).amount,0)
    }
}

final class TicketSummaryLabelTests:XCTestCase {
    func testLabelsDistinguishAdultTourFromFreeAdmission() throws {
        let tour=try JSONDecoder().decode(TicketPriceSummary.self,from:Data(#"{"amount":50,"currency":"AUD","basis":"guided_tour"}"#.utf8))
        XCTAssertEqual(tour.label(.traditional),"成人導覽 A$50 起")
        let admission=try JSONDecoder().decode(TicketPriceSummary.self,from:Data(#"{"amount":0,"currency":"AUD","basis":"general_admission"}"#.utf8))
        XCTAssertEqual(admission.label(.english),"General admission free")
    }
}
