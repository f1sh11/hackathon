package com.sydneybuddy.travel

import android.app.Application
import android.graphics.Bitmap
import java.io.File
import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import com.stadiamaps.ferrostar.core.http.HttpClientProvider
import com.stadiamaps.ferrostar.core.http.IResponse
import kotlinx.coroutines.delay
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import uniffi.ferrostar.RouteRequest

class WalkingNavigationUiTest {
    @get:Rule val rule=createComposeRule()
    private fun click(text:String) {rule.onNode(hasText(text,substring=false) and hasClickAction()).performScrollTo().performClick()}
    @Test fun simulatePreviewArriveWaitAndAdvanceWithoutChangingTrip() {
        val instrumentation=InstrumentationRegistry.getInstrumentation()
        val context=instrumentation.targetContext
        val bytes=instrumentation.context.assets.open("walking-sydney.json").use{it.readBytes()}
        val day=JSONObject("""{"date":"2026-10-23","start":{"name":"Quay","lat":-33.8611,"lng":151.2126},"stops":[{"name":"Opera House","lat":-33.8568,"lng":151.2153,"visitMinutes":60}],"end":{"name":"Quay","lat":-33.8611,"lng":151.2126}}""")
        lateinit var nav:WalkingNavigator
        lateinit var store:BuddyStore
        val old=context.getSharedPreferences("buddy",0).getString("selectedTrip",null)
        instrumentation.runOnMainSync {
            store=BuddyStore(context.applicationContext as Application)
            nav=WalkingNavigator(context,day,"ui-${System.nanoTime()}","http://127.0.0.1/unused",object:HttpClientProvider {
                override suspend fun call(request:RouteRequest):IResponse {delay(100);return object:IResponse{
                    override val isSuccessful=true;override val code=200;override fun bodyBytes()=bytes
                }}
            })
        }
        try {
            rule.waitUntil(30000){!store.busy}
            assertTrue(store.health?.optBoolean("navigationConfigured")==true)
            instrumentation.runOnMainSync {store.select(JSONObject().put("id","navigation-ui-test").put("days",JSONArray().put(day)));store.chooseMapProvider(MapProvider.OSM)}
            val before=store.trip.toString()
            rule.setContent{MaterialTheme{WalkingNavigationDialog(store,suppliedNavigator=nav){}}}
            rule.onNodeWithText("前往 Opera House").assertIsDisplayed()
            rule.onNode(isToggleable()).performScrollTo().performClick()
            click("预览整天步行路线")
            rule.waitUntil(10000){!nav.busy}
            assertTrue(nav.geometry.size>10)
            click("开始模拟导航")
            rule.waitUntil(20000){nav.progress.arrived || nav.message!=null}
            assertNull(nav.message)
            rule.onNodeWithText("前往下一站").performScrollTo().assertIsDisplayed()
            assertEquals(0,nav.progress.leg)
            val screenshot=instrumentation.uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"navigation-android-arrival.png").outputStream().use{screenshot.compress(Bitmap.CompressFormat.PNG,100,it)}
            screenshot.recycle()
            click("前往下一站")
            assertEquals(1,nav.progress.leg)
            assertEquals(before,store.trip.toString())
            rule.runOnIdle {nav.pause()}
        } finally {
            instrumentation.runOnMainSync {nav.close()}
            context.getSharedPreferences("buddy",0).edit().apply{if(old==null)remove("selectedTrip")else putString("selectedTrip",old)}.commit()
        }
    }
}
