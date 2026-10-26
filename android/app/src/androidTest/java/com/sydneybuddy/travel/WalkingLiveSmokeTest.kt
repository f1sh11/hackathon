package com.sydneybuddy.travel

import android.graphics.Bitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.filters.LargeTest
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/** One explicit smoke test against the configured development router; all other navigation tests use recorded routes. */
@LargeTest
class WalkingLiveSmokeTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    private fun api(path:String,method:String,body:JSONObject?=null):JSONObject {
        val connection=URL("http://127.0.0.1:8787/$path").openConnection() as HttpURLConnection
        try {
            connection.requestMethod=method;connection.connectTimeout=5000;connection.readTimeout=30000
            connection.setRequestProperty("Content-Type","application/json")
            if(body!=null){connection.doOutput=true;connection.outputStream.use{it.write(body.toString().toByteArray())}}
            assertTrue("API HTTP ${connection.responseCode}",connection.responseCode in 200..299)
            return JSONObject(connection.inputStream.bufferedReader().use{it.readText()})
        } finally {connection.disconnect()}
    }
    private fun click(text:String,scroll:Boolean=true){val node=rule.onNode(hasText(text,substring=false) and hasClickAction());if(scroll)node.performScrollTo();node.performClick()}
    private fun canonical(value:Any?):Any?=when(value){
        is JSONObject -> value.keys().asSequence().toList().sorted().associateWith{canonical(value.get(it))}
        is org.json.JSONArray -> (0 until value.length()).map{canonical(value.get(it))}
        else -> value
    }
    @Test fun homeEntryCanNavigateAnActualOsmRouteWithoutGoogleCalls() {
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val prefs=context.getSharedPreferences("buddy",0)
        val old=prefs.getString("selectedTrip",null)
        val draft=api("plan","POST",Profile().json().put("mode","demo"))
        val trip=api("trips","POST",JSONObject().put("trip",draft).put("expectedRevision",0))
        try {
            rule.waitUntil(30000){rule.onAllNodesWithText("Koko is on it",substring=true).fetchSemanticsNodes().isEmpty()}
            rule.activityRule.scenario.onActivity { activity ->
                val store=ViewModelProvider(activity)[BuddyStore::class.java]
                assertTrue(store.health?.optBoolean("navigationConfigured")==true)
                store.select(trip);store.resumeSaved();store.chooseMapProvider(MapProvider.OSM);store.tab=0
            }
            rule.waitUntil(30000){rule.onAllNodesWithText("Koko is on it",substring=true).fetchSemanticsNodes().isEmpty()}
            click("Start navigation",false)
            rule.onNode(isToggleable()).performScrollTo().performClick()
            click("Preview daily walking route")
            rule.waitUntil(30000){rule.onAllNodesWithText("Walk about",substring=true).fetchSemanticsNodes().isNotEmpty()}
            click("Start navigation simulation")
            rule.waitUntil(60000){rule.onAllNodesWithText("Go to next stop").fetchSemanticsNodes().isNotEmpty()}
            rule.onNodeWithText("Go to next stop").performScrollTo().assertIsDisplayed()
            val screenshot=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"navigation-android-live-arrival.png").outputStream().use{screenshot.compress(Bitmap.CompressFormat.PNG,100,it)}
            screenshot.recycle()
            click("Go to next stop")
            rule.onNodeWithText("Segment 2 of",substring=true).assertExists()
            click("Close",false)
            rule.activityRule.scenario.onActivity { activity ->
                assertEquals(canonical(trip),canonical(ViewModelProvider(activity)[BuddyStore::class.java].trip))
            }
        } catch(error:Throwable) {
            val roots=rule.onAllNodes(isRoot(),useUnmergedTree=true)
            roots.fetchSemanticsNodes().indices.forEach{println(roots[it].printToString())}
            val failed=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"navigation-live-failure.png").outputStream().use{failed.compress(Bitmap.CompressFormat.PNG,100,it)}
            failed.recycle()
            throw error
        } finally {
            api("trips/${trip.getString("id")}","DELETE")
            prefs.edit().apply{if(old==null)remove("selectedTrip")else putString("selectedTrip",old)}.commit()
        }
    }
}
