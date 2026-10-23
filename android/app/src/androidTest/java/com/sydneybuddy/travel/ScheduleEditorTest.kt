package com.sydneybuddy.travel

import android.app.Application
import android.graphics.Bitmap
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.*
import org.json.JSONObject
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
    @Test fun timelineHandleLongDragChangesTheActualOrder() {
        val c=setup();val first=c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getString("id")
        rule.onAllNodesWithText("☰ 拖動排序")[1].performScrollTo()
        rule.onAllNodesWithText("☰ 拖動排序").onFirst().performTouchInput {
            down(center);advanceEventTime(800);moveBy(Offset(0f,650f),delayMillis=700);up()
        }
        rule.waitUntil(5000){!c.busy}
        assertNotEquals(first,c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getString("id"));c.stop()
    }
    @Test fun reorderResizeAndUndoSendUpdatedActivitiesToApi() {
        val c=setup();val original=c.state!!.array("days").getJSONObject(0).array("activities").objects().map{it.getString("id")}
        rule.onAllNodesWithText("↓").onFirst().performScrollTo().performClick()
        rule.waitUntil(5000){!c.busy}
        assertEquals(original[0],c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(1).getString("id"))
        val duration=c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getInt("duration")
        rule.onAllNodesWithText("拉下緣調整",substring=true).onFirst().performScrollTo().performTouchInput {
            down(center);advanceEventTime(32);moveTo(center+Offset(0f,90f),delayMillis=500);up()
        }
        rule.waitUntil(5000){!c.busy}
        assertTrue(c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getInt("duration")>duration)
        rule.runOnIdle{c.undo()};rule.waitUntil(5000){!c.busy}
        assertEquals(duration,c.state!!.array("days").getJSONObject(0).array("activities").getJSONObject(0).getInt("duration"));c.stop()
    }
}
