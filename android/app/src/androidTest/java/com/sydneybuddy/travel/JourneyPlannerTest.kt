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
