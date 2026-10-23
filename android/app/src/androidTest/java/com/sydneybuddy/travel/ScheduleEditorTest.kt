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
    @Test fun mapSearchShowsPoiDetailsAndAddsToToolbox() {
        val inst=InstrumentationRegistry.getInstrumentation();lateinit var store:BuddyStore;var added:JSONObject?=null
        rule.runOnIdle{store=BuddyStore(inst.targetContext.applicationContext as Application)}
        val point=JSONObject().put("id","osm:node:123").put("name","Test Cafe").put("lat",-33.86).put("lng",151.21).put("category","美食").put("description","OSM recorded cafe description").put("note","費用未知")
        rule.setContent{MaterialTheme{PoiDiscoveryDialog(store,onDismiss={},requester={JSONObject().put("places",org.json.JSONArray().put(point))},onAdd={added=it})}}
        rule.onNodeWithText("搜尋").performClick()
        rule.waitUntil(5000){rule.onAllNodesWithText("Test Cafe",substring=true).fetchSemanticsNodes().isNotEmpty()}
        rule.onNodeWithText("詳細介紹").performClick()
        rule.onAllNodesWithText("OSM recorded cafe description").onLast().assertIsDisplayed()
        rule.onAllNodesWithText("完成").onLast().performClick()
        rule.onNodeWithText("＋工具箱").performClick()
        rule.runOnIdle{assertEquals("osm:node:123",added?.optString("id"))}
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
        rule.onNodeWithTag("toolbox-drag-gallery").assertDoesNotExist()
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
