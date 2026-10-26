package com.sydneybuddy.travel

import org.junit.Assert.*
import org.junit.Test
import org.json.JSONObject

class WalkingProgressTest {
    @Test fun cannotAdvanceBeforeArrivalOrSkipTwoStops() {
        val start=WalkingProgress()
        assertEquals(start,start.advance(3))
        val arrived=start.arrive()
        assertTrue(arrived.arrived)
        assertEquals(WalkingProgress(1),arrived.advance(3))
        assertEquals(WalkingProgress(1),arrived.advance(3).advance(3))
    }
    @Test fun completionIncludesReturnLegAndIsIdempotent() {
        val final=WalkingProgress(2).arrive().advance(3)
        assertTrue(final.finished)
        assertEquals(final,final.arrive().advance(3))
    }
    @Test fun corruptOrOutOfRangeStoredProgressCannotCrashNavigation() {
        assertEquals(WalkingProgress(),WalkingProgress.restore("invalid",3))
        assertEquals(WalkingProgress(),WalkingProgress.restore("{\"leg\":-1}",3))
        assertEquals(WalkingProgress(),WalkingProgress.restore("{\"leg\":3}",3))
        val arrived=WalkingProgress(1,true)
        assertEquals(arrived,WalkingProgress.restore(arrived.json().toString(),3))
    }
    @Test fun targetListIncludesEveryStopThenReturnAndScopesProgressToTripRevision() {
        val day=JSONObject("""{"date":"2026-10-23","start":{"name":"Hotel","lat":-33.86,"lng":151.21},"stops":[{"name":"Cafe","lat":-33.85,"lng":151.22,"visitMinutes":30}],"end":{"name":"Hotel","lat":-33.86,"lng":151.21}}""")
        assertEquals(listOf("Hotel","Cafe","Hotel (return)"),walkingPoints(day).map{it.name})
        assertEquals(30,walkingPoints(day)[1].stay)
        // Existing saved trips keep the same progress key when only display language changes.
        assertEquals("walking:8fd8c3b8ae0a8cc04d4ff8aea52d0c1a7b60ad792a799fb9d5271733c1c8959f",walkingKey("trip:1",day))
        assertNotEquals(walkingKey("trip:1",day),walkingKey("trip:2",day))
        assertEquals(walkingKey("trip:1",day),walkingKey("trip:1",JSONObject(day.toString())))
    }
}
