package com.sydneybuddy.travel

import android.app.Application
import android.graphics.Bitmap
import androidx.compose.runtime.*
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.*
import org.json.JSONObject
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File

class ScheduleEditorTest {
    @get:Rule val rule=createComposeRule()
    private fun fixture()=JSONObject(InstrumentationRegistry.getInstrumentation().context.assets.open("schedule-trip-test.json").bufferedReader().use{it.readText()})
    private fun setup():ScheduleController {
        val inst=InstrumentationRegistry.getInstrumentation()
        lateinit var controller:ScheduleController
        rule.runOnIdle {
            val store=BuddyStore(inst.targetContext.applicationContext as Application);store.previewSchedule(fixture())
            controller=ScheduleController(store,CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)){body->
                val result=body.getJSONObject("trip").copy();val schedule=result.getJSONObject("schedule").copy()
                schedule.put("days",body.getJSONArray("days")).put("settings",body.getJSONObject("settings")).put("rainy",body.optBoolean("rainy"))
                result.put("schedule",schedule)
            }
        }
        rule.setContent{MaterialTheme(colorScheme=lightColorScheme(primary=Green,onPrimary=Color.White,secondaryContainer=Mint)){ScheduleEditorScreen(controller.store,controller)}}
        return controller
    }
    @Test fun timelineShowsThreeDaysUnknownQueueAndCandidatesWithoutSaving() {
        val c=setup()
        rule.onNodeWithText("我的時間軸").assertIsDisplayed()
        rule.onNodeWithText("含未知排隊，剩餘僅供規劃參考").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("景點清單").performScrollTo().performClick()
        rule.onAllNodesWithText("☆ 設為必去").onFirst().performClick()
        rule.waitUntil(5000){!c.busy}
        assertTrue(c.trip.getJSONObject("profile").array("mustVisit").length()>0)
        rule.runOnIdle{c.undo()};rule.waitUntil(5000){!c.busy};assertEquals(0,c.trip.getJSONObject("profile").array("mustVisit").length())
        rule.onNodeWithText("完成").performClick()
        assertNull(c.store.proposal!!.targetId)
        val image=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
        File(InstrumentationRegistry.getInstrumentation().targetContext.getExternalFilesDir(null),"schedule-android-test.png").outputStream().use{image.compress(Bitmap.CompressFormat.PNG,100,it)};image.recycle()
        c.stop()
    }
    @Test fun languageSwitchUpdatesSettingsAndTabsAndPersistsWithoutChangingTrip() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application)}
        rule.waitUntil(15000){!store.busy}
        val original=store.language;val trip=fixture().put("id","language-test")
        assertEquals(1,store.tab)
        rule.runOnIdle{store.select(trip);store.chooseLanguage(AppLanguage.SIMPLIFIED)}
        val before=store.trip.toString()
        rule.setContent{MaterialTheme{CompositionLocalProvider(LocalAppLanguage provides store.language){
            androidx.compose.foundation.layout.Column {
                androidx.compose.foundation.layout.Box(androidx.compose.ui.Modifier.weight(1f)){SettingsScreen(store){}}
                IosNavigation(store)
            }
        }}}
        try {
            rule.onNodeWithTag("language-en").performScrollTo().performClick()
            rule.onNodeWithText("Language",useUnmergedTree=true).assertExists()
            val trips=rule.onNodeWithText("Trips").fetchSemanticsNode().boundsInRoot
            val map=rule.onNodeWithText("Map").fetchSemanticsNode().boundsInRoot
            assertTrue(trips.left<map.left)
            assertEquals("en",inst.targetContext.getSharedPreferences("buddy",0).getString("appLanguage",null))
            val catalog=JSONObject(inst.targetContext.assets.open("ui-localizations.json").bufferedReader().use{it.readText()})
            assertEquals("Day 3",translated(catalog,AppLanguage.ENGLISH,"第 {0} 天","3"))
            assertEquals("Custom place",translated(catalog,AppLanguage.ENGLISH,"Custom place"))
            rule.onNodeWithTag("language-zh-Hant").performScrollTo().performClick()
            rule.onNodeWithText("語言",useUnmergedTree=true).assertExists()
            assertEquals(before,store.trip.toString())
        }finally{rule.runOnIdle{store.chooseLanguage(original)}}
    }
    @Test fun mapHomeAutomaticallyLoadsRouteAndExposesActualModes() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore;var calls=0
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application)}
        rule.waitUntil(15000){!store.busy}
        val trip=fixture().put("id","local-home-route-test")
        val day=trip.array("days").getJSONObject(0)
        day.array("stops").getJSONObject(0).put("lat",day.getJSONObject("start").getDouble("lat")).put("lng",day.getJSONObject("start").getDouble("lng"))
        val original=store.mapProvider
        val recorded=inst.context.assets.open("walking-sydney.json").bufferedReader().use{it.readText()}
        val client=okhttp3.OkHttpClient.Builder().addInterceptor{chain->calls++;val buffer=okio.Buffer();chain.request().body!!.writeTo(buffer)
            val locations=JSONObject(buffer.readUtf8()).getJSONArray("locations")
            for(i in 1 until locations.length())assertNotEquals(locations.getJSONObject(i-1).toString(),locations.getJSONObject(i).toString())
            val response=JSONObject(recorded);val legs=org.json.JSONArray();repeat(locations.length()-1){legs.put(JSONObject().put("duration",60.0))};response.getJSONArray("routes").getJSONObject(0).put("legs",legs)
            okhttp3.Response.Builder().request(chain.request()).protocol(okhttp3.Protocol.HTTP_1_1).code(200).message("OK").body(response.toString().toResponseBody("application/json".toMediaType())).build()}.build()
        lateinit var overview:ItineraryOverview
        rule.runOnIdle{store.select(trip);store.chooseMapProvider(MapProvider.OSM);overview=ItineraryOverview(client)}
        rule.setContent{MaterialTheme{HomeScreen(store,overview)}}
        try {
            rule.waitUntil(5000){overview.route!=null}
            rule.onNodeWithText("交通方式").assertIsDisplayed()
            rule.onNodeWithTag("home-transport-legs").assertIsDisplayed()
            assertEquals(1,calls)
            assertEquals(day.array("stops").length()+1,overview.route!!.legSeconds.size)
            assertEquals(0.0,overview.route!!.legSeconds.first(),0.0)
            val plan=JourneyPlan.parse(inst.context.assets.open("journey-plan-test.json").bufferedReader().use{it.readText()})
            rule.runOnIdle{overview.accept(plan)}
            assertTrue(overviewTransportLegs(trip.array("days").getJSONObject(0),overview.route,overview.plan).any{it.transport.contains("TEST")&&it.transport.contains("公车")})
            assertEquals(1,calls)
        }finally{rule.runOnIdle{store.chooseMapProvider(original);overview.close()}}
    }
    @Test fun mapHomeKeepsSecondaryDetailsCollapsedAndCanExpandWithoutEditingTrip() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore
        val prefs=inst.targetContext.getSharedPreferences("buddy",0);val selected=prefs.getString("selectedTrip",null);val provider=prefs.getString("mapProvider",null)
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application)}
        rule.waitUntil(15000){!store.busy}
        val trip=fixture().put("id","local-home-clean-test")
        rule.runOnIdle{store.select(trip);store.chooseMapProvider(MapProvider.SCHEMATIC)}
        rule.setContent{MaterialTheme{HomeScreen(store)}}
        try {
            rule.onNodeWithTag("home-extra-info").assertDoesNotExist()
            rule.onNodeWithText("開始導航").assertIsDisplayed();rule.onNodeWithText("查看路线").assertIsDisplayed()
            rule.onNodeWithContentDescription("切換底圖").assertIsDisplayed()
            rule.onNodeWithTag("home-info-toggle").performClick()
            rule.onNodeWithTag("home-extra-info").assertExists()
            rule.onNodeWithText("悉尼天气").assertExists()
            rule.onNodeWithContentDescription("行程站点顺序").assertExists()
            rule.onNodeWithTag("home-info-toggle").performClick()
            rule.onNodeWithTag("home-extra-info").assertDoesNotExist()
            rule.runOnIdle{assertEquals(trip.toString(),store.trip.toString())}
        }finally{prefs.edit().apply{if(selected==null)remove("selectedTrip")else putString("selectedTrip",selected);if(provider==null)remove("mapProvider")else putString("mapProvider",provider)}.commit()}
    }
    @Test fun allowanceShowsNegativeRemainingAndClearsWarningsAfterRecovery() {
        val state=fixture().getJSONObject("schedule");val limits=state.getJSONObject("settings")
        val original=state.array("days").getJSONObject(0);var day by mutableStateOf(original.copy().put("estimatedSteps",limits.getInt("maxSteps")+1234).put("estimatedCost",limits.getDouble("dailyBudget")+25).put("timeUsed",limits.getInt("timeLimit")+15))
        rule.setContent{MaterialTheme{ScheduleAllowanceSummary(day,state,1)}}
        rule.onNodeWithText("剩餘 -1,234 步").assertIsDisplayed()
        rule.onNodeWithText("剩餘 A$-25.00").assertIsDisplayed()
        rule.onNodeWithText("剩餘 -15 分").assertIsDisplayed()
        for(id in listOf("steps","budget","time"))rule.onNodeWithTag("allowance-warning-$id",useUnmergedTree=true).assertExists()
        rule.runOnIdle{day=original.copy()}
        for(id in listOf("steps","budget","time"))rule.onNodeWithTag("allowance-warning-$id",useUnmergedTree=true).assertDoesNotExist()
        rule.onNodeWithText("預估步數").assertIsDisplayed();rule.onNodeWithText("當日花費").assertIsDisplayed();rule.onNodeWithText("交通＋排隊").assertIsDisplayed()
        assertEquals(-1234.0,ScheduleAllowance(10000.0,11234.0).remaining,0.0)
        assertEquals(0f,ScheduleAllowance(0.0,10.0).fraction,0f)
    }
    @Test fun poiMapFollowsSharedProviderAndRefreshesMatchingSource() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore;lateinit var original:MapProvider;var requests=0
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application);original=store.mapProvider;store.chooseMapProvider(MapProvider.OSM)}
        rule.setContent{MaterialTheme{PoiDiscoveryDialog(store,onDismiss={},requester={requests++;JSONObject().put("places",org.json.JSONArray())})}}
        try {
            rule.waitUntil(5000){requests==1}
            rule.onNodeWithText("离线示意图").performClick()
            rule.onNodeWithText("離線示意圖沒有街道底圖",substring=true).assertIsDisplayed()
            rule.runOnIdle{assertEquals(MapProvider.SCHEMATIC,store.mapProvider);assertEquals(1,requests)}
            if(!BuildConfig.MAPS_CONFIGURED){rule.onNodeWithText("Google Maps").performClick();rule.onNodeWithText("Google 地圖尚未設定金鑰",substring=true).assertIsDisplayed()}
            rule.onNodeWithText("OpenStreetMap",substring=false).performClick()
            rule.onNodeWithText("離線示意圖沒有街道底圖",substring=true).assertDoesNotExist()
            rule.waitUntil(5000){requests==2}
            rule.runOnIdle{assertEquals(MapProvider.OSM,store.mapProvider);assertEquals(2,requests)}
        }finally{rule.runOnIdle{store.chooseMapProvider(original)}}
    }
    @Test fun savedTripCardEditsOnTapAndNavigationButtonsDoNotOpenEditor() {
        val trip=fixture();var edit=0;var navigation=0;var map=0;var delete=0
        rule.setContent{MaterialTheme{SavedTripCard(trip,onEdit={edit++},onNavigate={navigation++},onMap={map++},onDelete={delete++})}}
        rule.onNodeWithText("編輯時間軸").assertDoesNotExist()
        rule.onNodeWithText("開始導航").performClick()
        rule.runOnIdle{assertEquals(1,navigation);assertEquals(0,edit)}
        rule.onNodeWithText("查看地图").performClick();rule.onNodeWithText("删除行程").performClick()
        rule.runOnIdle{assertEquals(1,map);assertEquals(1,delete);assertEquals(0,edit)}
        rule.onNodeWithTag("edit-trip-${trip.optString("id")}").performClick()
        rule.runOnIdle{assertEquals(1,edit);assertEquals(1,navigation)}
    }
    @Test fun savedTripNavigationSelectsTodayAndKeepsSavedStopOrder() {
        val trip=fixture();val days=trip.array("days").objects()
        assertEquals(1,savedTripNavigationDay(trip,days[1].getString("date")))
        assertNull(savedTripNavigationDay(trip,"2099-01-01"))
        val single=trip.copy().put("days",org.json.JSONArray().put(days[0]));assertEquals(0,savedTripNavigationDay(single,"2099-01-01"))
        assertNull(savedTripNavigationDay(trip.copy().put("days",org.json.JSONArray()),"2099-01-01"))
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val nav=WalkingNavigator(context,days[1],"card-order-test","http://127.0.0.1:8787/navigation/route")
        val expected=listOf(days[1].getJSONObject("start").getString("name"))+days[1].array("stops").objects().map{it.getString("name")}+listOf(days[1].getJSONObject("end").getString("name")+"（返回）")
        assertEquals(expected,nav.points.map{it.name});nav.close()
    }
    @Test fun poiEngineFollowsMapAndGoogleRequiresManualSearch() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore
        val requests=mutableListOf<String>()
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application)}
        rule.waitUntil(15000){!store.busy};val previous=store.mapProvider
        rule.runOnIdle{store.chooseMapProvider(MapProvider.OSM)}
        rule.setContent{MaterialTheme{PoiDiscoveryDialog(store,onDismiss={},googleMapConfigured=true,requester={input->
            requests.add(input.getString("provider"))
            JSONObject().put("places",org.json.JSONArray().put(JSONObject().put("id",input.getString("provider")+":test").put("name",input.getString("provider")+" result").put("lat",-33.86).put("lng",151.21).put("source",if(input.getString("provider")=="google")"Google Places"else "OpenStreetMap")))
        })}}
        try {
            rule.waitUntil(5000){requests.size==1}
            rule.runOnIdle{store.chooseMapProvider(MapProvider.GOOGLE)}
            rule.onNodeWithText("地點資料：Google Places").assertIsDisplayed()
            rule.onNodeWithText("osm result",substring=true).assertDoesNotExist()
            assertEquals(listOf("osm"),requests)
            rule.onNodeWithText("搜尋").performClick()
            rule.waitUntil(5000){requests.size==2}
            rule.onNodeWithText("google result",substring=true).assertExists()
            assertEquals(listOf("osm","google"),requests)
            rule.runOnIdle{store.chooseMapProvider(MapProvider.SCHEMATIC)}
            rule.onNodeWithText("google result",substring=true).assertDoesNotExist()
            rule.onNodeWithText("搜尋").performClick();assertEquals(2,requests.size)
        }finally{rule.runOnIdle{store.chooseMapProvider(previous)}}
    }
    @Test fun poiAutomaticallyLoadsCategoriesAndSeparatesFailureFromEmptyResults() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore
        val categories=mutableListOf<String>();var fail=false
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application)}
        rule.runOnIdle{store.chooseMapProvider(MapProvider.OSM)}
        rule.setContent{MaterialTheme{PoiDiscoveryDialog(store,onDismiss={},requester={input->
            categories.add(input.getString("category"));if(fail)throw IllegalStateException("測試連線中斷")
            JSONObject().put("places",org.json.JSONArray())
        })}}
        rule.waitUntil(5000){categories.contains("sights")}
        rule.onNodeWithText("此範圍沒有符合的地點。",substring=true).assertIsDisplayed()
        rule.runOnIdle{fail=true}
        rule.onNodeWithText("生活設施",substring=true).performClick()
        rule.waitUntil(5000){categories.contains("facilities")}
        rule.onNodeWithText("地點資料暫時無法載入：測試連線中斷").assertIsDisplayed()
        rule.onNodeWithText("此範圍沒有符合的地點。",substring=true).assertDoesNotExist()
        rule.runOnIdle{fail=false};rule.onNodeWithText("重新搜尋").performClick()
        rule.waitUntil(5000){categories.size>=3}
        rule.onNodeWithText("此範圍沒有符合的地點。",substring=true).assertIsDisplayed()
        assertEquals(listOf("sights","facilities","facilities"),categories)
    }
    @Test fun unifiedSearchStartsWithAllAndShowsLodgingInTheSameMapToolbox() {
        val inst=InstrumentationRegistry.getInstrumentation();val store=BuddyStore(inst.targetContext.applicationContext as Application)
        val original=store.travelDataSource;store.chooseTravelDataSource("zetaris");store.chooseMapProvider(MapProvider.OSM)
        val point=fixture().getJSONObject("schedule").getJSONArray("candidates").getJSONObject(0).copy().put("id","osm:node:888").put("source","OpenStreetMap").put("category","住宿").put("name","Unified test hotel").put("description","Public hotel candidate")
        var firstCategory:String?=null
        rule.setContent{MaterialTheme{PoiDiscoveryDialog(store,onDismiss={},requester={input->if(firstCategory==null)firstCategory=input.optString("category");JSONObject().put("places",org.json.JSONArray().put(point)).put("source","Open travel data / Zetaris")})}}
        rule.waitUntil(10000){rule.onAllNodesWithText("Unified test hotel",substring=true).fetchSemanticsNodes().isNotEmpty()}
        assertEquals("all",firstCategory)
        rule.onNodeWithText("住宿").performScrollTo().performClick()
        rule.waitUntil(10000){rule.onAllNodesWithText("Unified test hotel",substring=true).fetchSemanticsNodes().isNotEmpty()}
        rule.onNodeWithText("住宿 · OpenStreetMap").assertExists()
        rule.onNodeWithText("詳細介紹").performClick();rule.onNodeWithText("Unified test hotel").assertExists()
        rule.runOnIdle{store.chooseTravelDataSource(original)}
    }
    @Test fun communityDiscoveryShowsDirectionsAndEmptyReviewsOnAnyBaseMap() {
        val inst=InstrumentationRegistry.getInstrumentation();val store=BuddyStore(inst.targetContext.applicationContext as Application)
        val original=store.travelDataSource;store.chooseTravelDataSource("zetaris");store.chooseMapProvider(MapProvider.OSM)
        val point=fixture().getJSONObject("schedule").getJSONArray("candidates").getJSONObject(0).copy()
            .put("id","gem:"+"a".repeat(24)).put("source","Wikivoyage").put("name","Community test reserve")
            .put("description","Public community guide").put("photos",org.json.JSONArray()).put("reviews",org.json.JSONArray())
            .put("guide",JSONObject().put("directions","Public path by the wharf").put("sourceUrl","https://en.wikivoyage.org/w/index.php?oldid=1").put("attribution","Wikivoyage contributors").put("license","CC BY-SA 4.0"))
        rule.setContent{MaterialTheme{PoiDiscoveryDialog(store,onDismiss={},requester={input->JSONObject().put("places",if(input.optString("category")=="gems")org.json.JSONArray().put(point)else org.json.JSONArray()).put("source","Wikivoyage / Zetaris")})}}
        rule.onNodeWithText("私房景點").performClick()
        rule.waitUntil(10000){rule.onAllNodesWithText("Community test reserve",substring=true).fetchSemanticsNodes().isNotEmpty()}
        rule.onNodeWithText("詳細介紹").performClick()
        rule.onNodeWithText("Public path by the wharf").assertExists()
        rule.onNodeWithText("此來源未提供旅客評論或評分。").assertExists()
        rule.runOnIdle{store.chooseTravelDataSource(original)}
    }
    @Test fun mapSearchShowsPoiDetailsAndAddsToToolbox() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore;var added:JSONObject?=null
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application)}
        val point=JSONObject().put("id","overture:00000000-0000-4000-8000-000000000001").put("name","Test Cafe").put("lat",-33.86).put("lng",151.21).put("category","美食").put("description","Overture recorded cafe description").put("note","費用未知").put("source","Overture Maps").put("sourceRelease","2026-10-21.0").put("website","https://example.com")
        rule.runOnIdle{store.chooseMapProvider(MapProvider.OSM);store.removePoi(point.getString("id"))}
        rule.setContent{MaterialTheme{PoiDiscoveryDialog(store,onDismiss={},requester={JSONObject().put("places",org.json.JSONArray().put(point)).put("source","Overture Maps / local snapshot").put("attribution","Overture Maps Foundation").put("metadata",JSONObject().put("release","2026-10-21.0"))},onAdd={added=it})}}
        rule.onNodeWithText("搜尋").performClick()
        rule.waitUntil(5000){rule.onAllNodesWithText("Test Cafe",substring=true).fetchSemanticsNodes().isNotEmpty()}
        rule.onNodeWithText("地點資料：Overture Maps / local snapshot").assertIsDisplayed()
        rule.onNodeWithText("🍴 Test Cafe").performClick()
        rule.onAllNodesWithText("Overture recorded cafe description").onLast().assertIsDisplayed()
        rule.runOnIdle{assertNull(added);assertFalse(store.poiToolbox.any{it.optString("id")==point.optString("id")})}
        rule.onAllNodesWithText("完成").onLast().performClick()
        rule.onNodeWithText("＋工具箱").performClick()
        rule.runOnIdle{assertEquals("overture:00000000-0000-4000-8000-000000000001",added?.optString("id"))}
        rule.onNodeWithText("移出工具箱").performClick()
        rule.runOnIdle{assertFalse(store.poiToolbox.any{it.optString("id")==point.optString("id")})}
        rule.onNodeWithText("＋工具箱").assertExists()
    }
    @Test fun toolboxIntroductionsShowDescriptionWithoutChangingSchedule() {
        val c=setup();val before=c.state!!.array("days").toString()
        rule.onNodeWithTag("schedule-workbench").performScrollTo()
        rule.onNodeWithTag("toolbox-info-mca").performClick()
        rule.onNodeWithText("景點介紹").assertIsDisplayed()
        rule.onNodeWithText(c.state!!.array("candidates").objects().first{it.optString("id")=="mca"}.getString("description")).assertExists()
        rule.onNodeWithText("完成").performClick()
        assertEquals(before,c.state!!.array("days").toString());c.stop()
    }
    @Test fun toolboxMovesExistingWithoutDuplicateAndBlocksOtherDaysAndLocks() {
        val c=setup();val day=c.state!!.array("days").getJSONObject(0)
        val before=day.array("activities").objects().first{it.optString("placeId")=="mca"};val id=before.getString("id");val duration=before.getInt("duration")
        rule.onNodeWithTag("schedule-workbench").performScrollTo()
        rule.onNodeWithTag("toolbox-drag-gallery").assertExists()
        val source=rule.onNodeWithTag("toolbox-drag-mca").fetchSemanticsNode().boundsInRoot
        val canvas=rule.onNodeWithTag("timeline-canvas").fetchSemanticsNode().boundsInRoot
        val viewport=rule.onNodeWithTag("timeline-viewport").fetchSemanticsNode().boundsInRoot
        val target=Offset(viewport.right-30f,minOf(canvas.top+350f,viewport.bottom-50f))
        rule.onNodeWithTag("toolbox-drag-mca").performTouchInput{down(center);advanceEventTime(800);moveBy(target-source.center,delayMillis=700);up()}
        rule.waitUntil(5000){!c.busy}
        var items=c.state!!.array("days").getJSONObject(0).array("activities").objects()
        val moved=items.single{it.optString("placeId")=="mca"};assertEquals(id,moved.getString("id"));assertEquals(duration,moved.getInt("duration"));assertTrue(moved.has("fixedStart"))
        rule.runOnIdle{c.place(c.state!!.array("candidates").objects().first{it.optString("id")=="gallery"},720)}
        assertFalse(c.busy);assertFalse(c.state!!.array("days").getJSONObject(0).array("activities").objects().any{it.optString("placeId")=="gallery"})
        rule.runOnIdle{c.edit{s->s.array("days").getJSONObject(0).array("activities").objects().first{it.optString("placeId")=="mca"}.put("locked",true)}}
        rule.waitUntil(5000){!c.busy};rule.onNodeWithTag("toolbox-drag-mca").assertDoesNotExist()
        val start=c.state!!.array("days").getJSONObject(0).array("activities").objects().first{it.optString("placeId")=="mca"}.optInt("fixedStart")
        rule.runOnIdle{c.place(c.state!!.array("candidates").objects().first{it.optString("id")=="mca"},900)}
        assertFalse(c.busy);assertEquals(start,c.state!!.array("days").getJSONObject(0).array("activities").objects().first{it.optString("placeId")=="mca"}.optInt("fixedStart"));c.stop()
    }
    @Test fun hoveringDateSwitchesDayAndContinuedDragMovesExistingWithoutDuplicate() {
        val c=setup();rule.onNodeWithTag("schedule-workbench").performScrollTo()
        val before=c.state!!.array("days").toString()
        val original=c.state!!.array("days").getJSONObject(0).array("activities").objects().first{it.optString("placeId")=="mca"}
        val source=rule.onNodeWithTag("toolbox-drag-mca").fetchSemanticsNode().boundsInRoot.center
        val date=rule.onNodeWithTag("schedule-day-1").fetchSemanticsNode().boundsInRoot.center
        rule.onRoot().performTouchInput{down(source);advanceEventTime(800);moveTo(date,delayMillis=700)}
        rule.mainClock.advanceTimeBy(600)
        rule.waitUntil(5000){c.dayIndex==1}
        assertEquals(before,c.state!!.array("days").toString())
        val canvas=rule.onNodeWithTag("timeline-canvas").fetchSemanticsNode().boundsInRoot
        val viewport=rule.onNodeWithTag("timeline-viewport").fetchSemanticsNode().boundsInRoot
        rule.onRoot().performTouchInput{moveTo(Offset(viewport.right-30f,minOf(canvas.top+350f,viewport.bottom-50f)),delayMillis=700);up()}
        rule.waitUntil(5000){!c.busy}
        val days=c.state!!.array("days").objects()
        assertFalse(days[0].array("activities").objects().any{it.optString("placeId")=="mca"})
        val moved=days[1].array("activities").objects().single{it.optString("placeId")=="mca"}
        assertEquals(original.getString("id"),moved.getString("id"));assertEquals(original.getInt("duration"),moved.getInt("duration"))
        assertEquals(1,days.flatMap{it.array("activities").objects()}.count{it.optString("placeId")=="mca"})
        rule.runOnIdle{c.undo()};rule.waitUntil(5000){!c.busy}
        assertEquals(before,c.state!!.array("days").toString());c.stop()
    }
    @Test fun releasingOnDateOnlySwitchesDayWithoutAdding() {
        val c=setup();rule.onNodeWithTag("schedule-workbench").performScrollTo()
        val before=c.state!!.array("days").toString()
        val source=rule.onNodeWithTag("toolbox-drag-mca").fetchSemanticsNode().boundsInRoot.center
        val date=rule.onNodeWithTag("schedule-day-2").fetchSemanticsNode().boundsInRoot.center
        rule.onRoot().performTouchInput{down(source);advanceEventTime(800);moveTo(date,delayMillis=700)}
        rule.mainClock.advanceTimeBy(600);rule.waitUntil(5000){c.dayIndex==2}
        rule.onRoot().performTouchInput{up()}
        assertEquals(before,c.state!!.array("days").toString());assertFalse(c.busy);c.stop()
    }
    @Test fun toolboxDragDropsNewAttractionAtActualTime() {
        val c=setup()
        rule.onNodeWithTag("schedule-workbench").performScrollTo()
        rule.onNodeWithTag("toolbox-drag-museum").performScrollTo()
        val source=rule.onNodeWithTag("toolbox-drag-museum").fetchSemanticsNode().boundsInRoot
        val canvas=rule.onNodeWithTag("timeline-canvas").fetchSemanticsNode().boundsInRoot
        val viewport=rule.onNodeWithTag("timeline-viewport").fetchSemanticsNode().boundsInRoot
        val target=Offset(viewport.right-30f,minOf(canvas.top+350f,viewport.bottom-50f))
        rule.onNodeWithTag("toolbox-drag-museum").performTouchInput {
            down(center);advanceEventTime(800);moveBy(target-source.center,delayMillis=700);up()
        }
        rule.waitUntil(5000){!c.busy}
        val added=c.state!!.array("days").getJSONObject(0).array("activities").objects().filter{it.optString("placeId")=="museum"}
        assertEquals(1,added.size);assertTrue(added.first().getInt("fixedStart")>=600)
        val image=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
        File(InstrumentationRegistry.getInstrumentation().targetContext.getExternalFilesDir(null),"schedule-toolbox-android.png").outputStream().use{image.compress(Bitmap.CompressFormat.PNG,100,it)};image.recycle();c.stop()
    }
    @Test fun timelineHandleLongDragChangesTheActualOrder() {
        val c=setup();val first=c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getString("id")
        rule.onNodeWithTag("schedule-workbench").performScrollTo()
        rule.onNodeWithTag("schedule-move-$first").performScrollTo().performTouchInput {
            down(center);advanceEventTime(32);moveBy(Offset(0f,650f),delayMillis=700);up()
        }
        rule.waitUntil(5000){!c.busy}
        assertNotEquals(first,c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getString("id"));c.stop()
    }
    @Test fun shortActivityStillOpensDetailsAndCanBeRemovedAndUndone() {
        val c=setup();val before=c.state!!.array("days").getJSONObject(0).array("activities").objects();val id=before.first().getString("id")
        rule.onNodeWithTag("schedule-workbench").performScrollTo()
        rule.onNodeWithTag("schedule-edit-$id").performScrollTo().performClick()
        rule.onNodeWithText("移除此活動").performClick()
        rule.waitUntil(5000){!c.busy}
        assertEquals(before.size-1,c.state!!.array("days").getJSONObject(0).array("activities").length())
        rule.runOnIdle{c.undo()};rule.waitUntil(5000){!c.busy}
        assertEquals(id,c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getString("id"));c.stop()
    }
    @Test fun topHandleChangesStartAndKeepsFinish() {
        val c=setup();val activity=c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(1)
        val id=activity.getString("id");val start=activity.getInt("scheduledStart");val duration=activity.getInt("duration")
        rule.onNodeWithTag("schedule-workbench").performScrollTo()
        rule.onNodeWithTag("schedule-start-$id").performScrollTo().performTouchInput {
            down(center);advanceEventTime(32);moveBy(Offset(0f,100f),delayMillis=500);up()
        }
        rule.waitUntil(5000){!c.busy}
        val after=c.state!!.array("days").getJSONObject(0).array("activities").objects().first{it.getString("id")==id}
        assertTrue(after.getInt("fixedStart")>start)
        assertEquals(start+duration,after.getInt("fixedStart")+after.getInt("duration"));c.stop()
    }
    @Test fun reorderResizeAndUndoSendUpdatedActivitiesToApi() {
        val c=setup();val original=c.state!!.array("days").getJSONObject(0).array("activities").objects().map{it.getString("id")}
        rule.runOnIdle{c.retime(original[0],750,10)}
        rule.waitUntil(5000){!c.busy}
        assertNotEquals(original[0],c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getString("id"))
        val duration=c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getInt("duration")
        val id=c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getString("id")
        rule.onNodeWithTag("schedule-workbench").performScrollTo()
        rule.onNodeWithTag("schedule-end-$id").performScrollTo().performTouchInput {
            down(center);advanceEventTime(32);moveTo(center+Offset(0f,90f),delayMillis=500);up()
        }
        rule.waitUntil(5000){!c.busy}
        assertTrue(c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getInt("duration")>duration)
        rule.runOnIdle{c.undo()};rule.waitUntil(5000){!c.busy}
        assertEquals(duration,c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getInt("duration"));c.stop()
    }
}
