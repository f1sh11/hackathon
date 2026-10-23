package com.sydneybuddy.travel

import android.graphics.Bitmap
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import okhttp3.*
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File
import java.time.OffsetDateTime

class JourneyPlannerTest {
    @get:Rule val rule=createComposeRule()
    private val day=JSONObject("""{"date":"2026-10-23","start":{"name":"测试起点","lat":-33.8611,"lng":151.2126},"stops":[],"end":{"name":"测试终点","lat":-33.8568,"lng":151.2153}}""")
    private fun fixture()=InstrumentationRegistry.getInstrumentation().context.assets.open("journey-plan-test.json").bufferedReader().use{it.readText()}
    @Test fun busDetailsAndTrafficSheetCanCollapseWithoutStartingNavigation() {
        val result=JourneyPlan.parse(fixture());var started=false;var closed=false
        rule.setContent{MaterialTheme(colorScheme=lightColorScheme(primary=Green,onPrimary=Color.White,secondaryContainer=Mint)){JourneyPlannerDialog(day,"http://127.0.0.1:8787",MapProvider.OSM,{closed=true},{started=true},result)}}
        rule.onNodeWithText("测试资料 · TEST 公车非真实班次").assertIsDisplayed()
        rule.waitUntil(30000){rule.onAllNodesWithText("地图已加载",substring=true).fetchSemanticsNodes().isNotEmpty()}
        rule.onNodeWithText("🚌 公车 · TEST-01").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("往 测试目的地方向").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("测试上车站 A → 测试下车站 B").assertExists()
        rule.onNodeWithText("10:10 上车 · 10:25 下车 · 4 站 · 站台 A").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("时刻表班次 · 非实时").assertExists()
        val inst=InstrumentationRegistry.getInstrumentation();val image=inst.uiAutomation.takeScreenshot()
        File(inst.targetContext.getExternalFilesDir(null),"journey-android-fixture.png").outputStream().use{image.compress(Bitmap.CompressFormat.PNG,100,it)};image.recycle()
        rule.onNodeWithText("收起").performClick();rule.onNodeWithText("路线详情").assertIsDisplayed()
        rule.onNodeWithText("开始步行导航").assertDoesNotExist();assertFalse(started)
        rule.onNodeWithText("关闭").performClick();assertTrue(closed)
    }
    @Test fun actualClientSendsModesAndCancelsOldResultWhenInputsChange() {
        val response=fixture();var sent:JSONObject?=null
        val client=OkHttpClient.Builder().addInterceptor{chain->
            val buffer=okio.Buffer();chain.request().body!!.writeTo(buffer);sent=JSONObject(buffer.readUtf8())
            Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(200).message("OK").body(ResponseBody.create(null,response)).build()
        }.build()
        lateinit var model:JourneyModel
        rule.runOnIdle{model=JourneyModel(client);model.load(day,listOf("transit"),OffsetDateTime.parse("2026-10-23T10:00:00+11:00"),"http://127.0.0.1:8787","nsw","osm")}
        rule.waitUntil(10000){!model.busy}
        assertNull(model.error);assertEquals("TEST-01",model.plan!!.legs[0].steps[1].line)
        assertEquals("transit",sent!!.getJSONArray("modes").getString(0));assertEquals(2,sent!!.getJSONArray("points").length())
        assertEquals("open",sent!!.getString("contentSource"));assertEquals("nsw",sent!!.getString("routingProvider"));assertEquals("osm",sent!!.getString("mapProvider"))
        rule.runOnIdle{model.invalidate()};assertNull(model.plan);rule.runOnIdle{model.close()}
    }
    @androidx.test.filters.SdkSuppress(minSdkVersion=29)
    @Test fun walkingLoopIsVisibleAfterPlanningOnAnAlreadyLoadedMap() {
        val inst=InstrumentationRegistry.getInstrumentation()
        fun asset(name:String)=inst.context.assets.open(name).bufferedReader().use{it.readText()}
        val loop=JSONObject(asset("walking-itinerary-day.json"))
        val response=asset("walking-itinerary-plan.json")
        val client=OkHttpClient.Builder().addInterceptor{chain->Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(200).message("OK").body(ResponseBody.create(null,response)).build()}.build()
        lateinit var model:JourneyModel
        rule.runOnIdle{model=JourneyModel(client)}
        rule.setContent{MaterialTheme(colorScheme=lightColorScheme(primary=Green,onPrimary=Color.White,secondaryContainer=Mint)){JourneyPlannerDialog(loop,"http://127.0.0.1:8787",MapProvider.OSM,{},{},suppliedModel=model)}}
        rule.waitUntil(30000){rule.onAllNodesWithText("地图已加载",substring=true).fetchSemanticsNodes().isNotEmpty()}
        rule.onNodeWithText("规划交通路线").performClick()
        rule.waitUntil(10000){!model.busy && model.plan!=null}
        assertNull(model.error);assertEquals(5,model.plan!!.sections.size)
        rule.onNodeWithText("收起").performClick()
        rule.onNodeWithText("路线详情").assertIsDisplayed()
        rule.waitUntil(30000){rule.onAllNodesWithText("分段交通路线",substring=true).fetchSemanticsNodes().isNotEmpty()}
        fun visibleDotPixels():Int {
            val bitmap=inst.uiAutomation.takeScreenshot()
            var count=0
            for(y in (bitmap.height*.28).toInt() until (bitmap.height*.76).toInt() step 2)for(x in 0 until bitmap.width step 2) {
                val color=bitmap.getPixel(x,y)
                if(kotlin.math.abs(android.graphics.Color.red(color)-25)<=8 && kotlin.math.abs(android.graphics.Color.green(color)-118)<=8 && kotlin.math.abs(android.graphics.Color.blue(color)-210)<=8)count++
            }
            bitmap.recycle();return count
        }
        fun save(name:String) {
            val image=inst.uiAutomation.takeScreenshot()
            File(inst.targetContext.getExternalFilesDir(null),name).outputStream().use{image.compress(Bitmap.CompressFormat.PNG,100,it)};image.recycle()
        }
        // Check pixels on the rendered map, rather than trusting the "map loaded" label.
        rule.waitUntil(15000){visibleDotPixels()>100}
        save("journey-android-walking-loop.png")
        var nativeMap:org.maplibre.android.maps.MapLibreMap?=null
        val renderedFrames=java.util.concurrent.atomic.AtomicInteger()
        fun findMap(view:android.view.View):org.maplibre.android.maps.MapView? {
            if(view is org.maplibre.android.maps.MapView)return view
            if(view is android.view.ViewGroup)for(i in 0 until view.childCount)findMap(view.getChildAt(i))?.let{return it}
            return null
        }
        rule.runOnIdle {
            val mapView=android.view.inspector.WindowInspector.getGlobalWindowViews().asReversed().firstNotNullOf{if(it.hasWindowFocus())findMap(it)else null}
            mapView.getMapAsync{nativeMap=it}
            mapView.addOnDidFinishRenderingFrameListener{_,_,_->renderedFrames.incrementAndGet()}
        }
        rule.waitUntil(10000){nativeMap!=null}
        var initialZoom=0.0
        var beforeZoom=0
        rule.runOnIdle {
            initialZoom=nativeMap!!.cameraPosition.zoom;beforeZoom=renderedFrames.get()
            nativeMap!!.moveCamera(org.maplibre.android.camera.CameraUpdateFactory.zoomBy(1.0))
            assertEquals(initialZoom+1,nativeMap!!.cameraPosition.zoom,0.05)
        }
        rule.waitUntil(30000){renderedFrames.get()>beforeZoom}
        rule.waitUntil(15000){visibleDotPixels()>100}
        save("journey-android-walking-zoom.png")
        rule.runOnIdle {
            beforeZoom=renderedFrames.get()
            nativeMap!!.moveCamera(org.maplibre.android.camera.CameraUpdateFactory.zoomTo(initialZoom))
            assertEquals(initialZoom,nativeMap!!.cameraPosition.zoom,0.05)
        }
        rule.waitUntil(30000){renderedFrames.get()>beforeZoom}
        rule.waitUntil(15000){visibleDotPixels()>100}
        rule.onNodeWithText("重新查询").performClick()
        rule.waitUntil(10000){!model.busy && model.plan!=null}
        rule.waitUntil(15000){visibleDotPixels()>100}
        rule.runOnIdle{model.close()}
    }
    @Test fun missingTransitServiceShowsServerMessageWithoutInventedBus() {
        val client=OkHttpClient.Builder().addInterceptor{chain->Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(422).message("Not configured").body(ResponseBody.create(null,"""{"error":"大众运输服务尚未启用。"}""")).build()}.build()
        lateinit var model:JourneyModel
        rule.runOnIdle{model=JourneyModel(client)}
        rule.setContent{MaterialTheme{JourneyPlannerDialog(day,"http://127.0.0.1:8787",MapProvider.OSM,{},{},suppliedModel=model)}}
        rule.onNodeWithTag("journey-mode-0-transit").performScrollTo().performClick()
        rule.onNodeWithText("规划交通路线").performClick()
        rule.waitUntil(10000){!model.busy}
        assertNull(model.plan);assertTrue(model.error!!.contains("尚未启用"))
        rule.onNodeWithTag("journey-error").assertIsDisplayed()
        rule.onAllNodesWithText("TEST-01",substring=true).assertCountEquals(0)
        rule.runOnIdle{model.close()}
    }
}
