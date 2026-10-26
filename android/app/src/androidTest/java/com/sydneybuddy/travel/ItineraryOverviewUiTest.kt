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
            rule.waitUntil(30000){rule.onAllNodesWithText("Koko is on it",substring=true).fetchSemanticsNodes().isEmpty()}
            rule.activityRule.scenario.onActivity {activity ->
                store=ViewModelProvider(activity)[BuddyStore::class.java]
                store.onboarding=false;store.tab=0;store.chooseMapProvider(MapProvider.OSM);store.select(trip)
            }
            rule.waitUntil(30000){rule.onAllNodesWithTag("home-transport-legs").fetchSemanticsNodes().isNotEmpty() || rule.onAllNodesWithTag("home-route-error").fetchSemanticsNodes().isNotEmpty()}
            rule.onNodeWithTag("home-transport-legs").assertIsDisplayed()
            rule.onNodeWithText("Transport").assertIsDisplayed()
            val home=instrumentation.uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"itinerary-android-home-route.png").outputStream().use{home.compress(Bitmap.CompressFormat.PNG,100,it)}
            home.recycle()
            rule.onNodeWithTag("home-info-toggle").performClick()
            rule.onNodeWithContentDescription("Stop order").assertExists()
            rule.onNodeWithContentDescription("Expand map").performClick()
            rule.onNodeWithText("Full-day itinerary").assertIsDisplayed()
            rule.waitUntil(30000){rule.onAllNodes(hasText("Map loaded",substring=true) and hasAnyAncestor(hasTestTag("expanded-itinerary-map"))).fetchSemanticsNodes().isNotEmpty()}
            val screenshot=instrumentation.uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"itinerary-android-overview.png").outputStream().use{screenshot.compress(Bitmap.CompressFormat.PNG,100,it)}
            screenshot.recycle()
            rule.waitUntil(30000){rule.onAllNodesWithText("Walk approx.",substring=true).fetchSemanticsNodes().isNotEmpty() || rule.onAllNodesWithText("Could not load",substring=true).fetchSemanticsNodes().isNotEmpty()}
            rule.onNode(hasText("Walking route · excludes time at stops") and hasAnyAncestor(hasTestTag("expanded-itinerary-map"))).assertIsDisplayed()
            rule.waitUntil(30000){rule.onAllNodes(hasText("Map loaded · walking route along roads") and hasAnyAncestor(hasTestTag("expanded-itinerary-map"))).fetchSemanticsNodes().isNotEmpty()}
            val actual=instrumentation.uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"itinerary-android-walking.png").outputStream().use{actual.compress(Bitmap.CompressFormat.PNG,100,it)}
            actual.recycle()
            rule.onNodeWithText("Close map").performClick()
            assertEquals(trip.toString(),store.trip.toString())
            val recorded=instrumentation.context.assets.open("walking-sydney.json").bufferedReader().use{it.readText()}
            val route=OverviewRoute.parse(recorded)
            assertEquals(-33.861,route.points.first().first,0.003)
            assertTrue(route.meters>500);assertTrue(route.legSeconds.size==1)
            rule.onNodeWithContentDescription("Expand map").assertIsDisplayed()
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
