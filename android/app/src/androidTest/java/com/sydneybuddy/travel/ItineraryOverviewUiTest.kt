package com.sydneybuddy.travel

import android.graphics.Bitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.platform.app.InstrumentationRegistry
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File

/** Local UI test with one explicit call to the configured development router. No saved trip is changed. */
@androidx.test.filters.LargeTest
class ItineraryOverviewUiTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun numberedOverviewCanExpandAndCloseWithoutChangingTrip() {
        val instrumentation=InstrumentationRegistry.getInstrumentation()
        val context=instrumentation.targetContext
        val prefs=context.getSharedPreferences("buddy",0)
        val selected=prefs.getString("selectedTrip",null)
        val provider=prefs.getString("mapProvider",null)
        val trip=JSONObject("""{"id":"local-overview-test","title":"地图顺序测试","mode":"demo","days":[{"date":"2026-10-23","estimatedSteps":1800,"estimatedCost":0,"finishTime":"12:00","start":{"name":"Quay","lat":-33.8611,"lng":151.2126},"stops":[{"name":"Opera House","lat":-33.8568,"lng":151.2153,"arrival":"10:15","departure":"11:15"},{"name":"Rocks","lat":-33.8598,"lng":151.209,"arrival":"11:30","departure":"12:00"}],"end":{"name":"Quay","lat":-33.8611,"lng":151.2126},"legs":[]}]}""")
        lateinit var store:BuddyStore
        try {
            rule.waitUntil(30000){rule.onAllNodesWithText("Buddy 正在处理",substring=true).fetchSemanticsNodes().isEmpty()}
            rule.activityRule.scenario.onActivity {activity ->
                store=ViewModelProvider(activity)[BuddyStore::class.java]
                store.onboarding=false;store.tab=0;store.chooseMapProvider(MapProvider.OSM);store.select(trip)
            }
            rule.onNodeWithContentDescription("行程站点顺序").assertExists()
            rule.onNodeWithText("显示步行路线").assertIsDisplayed()
            rule.onNodeWithText("展开地图").performClick()
            rule.onNodeWithText("全天行程顺序").assertIsDisplayed()
            rule.onNodeWithText("尚未查询道路路线 · 仅显示站点").assertIsDisplayed()
            rule.waitUntil(30000){rule.onAllNodes(hasText("地图已加载",substring=true) and hasAnyAncestor(hasTestTag("expanded-itinerary-map"))).fetchSemanticsNodes().isNotEmpty()}
            val screenshot=instrumentation.uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"itinerary-android-overview.png").outputStream().use{screenshot.compress(Bitmap.CompressFormat.PNG,100,it)}
            screenshot.recycle()
            rule.onNode(hasText("显示步行路线") and hasAnyAncestor(hasTestTag("expanded-itinerary-map"))).performClick()
            rule.waitUntil(30000){rule.onAllNodesWithText("步行约",substring=true).fetchSemanticsNodes().isNotEmpty() || rule.onAllNodesWithText("暂时无法取得",substring=true).fetchSemanticsNodes().isNotEmpty()}
            rule.onNode(hasText("沿道路步行路线 · 不含景点停留时间") and hasAnyAncestor(hasTestTag("expanded-itinerary-map"))).assertIsDisplayed()
            rule.waitUntil(30000){rule.onAllNodes(hasText("地图已加载 · 沿道路步行路线") and hasAnyAncestor(hasTestTag("expanded-itinerary-map"))).fetchSemanticsNodes().isNotEmpty()}
            val actual=instrumentation.uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"itinerary-android-walking.png").outputStream().use{actual.compress(Bitmap.CompressFormat.PNG,100,it)}
            actual.recycle()
            rule.onNodeWithText("关闭地图").performClick()
            assertEquals(trip.toString(),store.trip.toString())
            val recorded=instrumentation.context.assets.open("walking-sydney.json").bufferedReader().use{it.readText()}
            val route=OverviewRoute.parse(recorded)
            assertEquals(-33.861,route.points.first().first,0.003)
            assertTrue(route.meters>500);assertTrue(route.legSeconds.size==1)
            rule.onNodeWithText("展开地图").assertIsDisplayed()
            lateinit var unavailable:ItineraryOverview
            rule.runOnIdle {unavailable=ItineraryOverview();unavailable.load(trip.getJSONArray("days").getJSONObject(0),"http://127.0.0.1:1")}
            rule.waitUntil(10000){!unavailable.busy}
            assertNull(unavailable.route);assertNotNull(unavailable.message)
            rule.runOnIdle{unavailable.close()}
        } finally {
            prefs.edit().apply{if(selected==null)remove("selectedTrip")else putString("selectedTrip",selected);if(provider==null)remove("mapProvider")else putString("mapProvider",provider)}.commit()
        }
    }
}
