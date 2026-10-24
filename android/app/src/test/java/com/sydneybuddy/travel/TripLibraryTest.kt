package com.sydneybuddy.travel

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class TripLibraryTest {
    @Test fun matchesEveryTravelDayNamesAndCombinedFilters(){
        val trip=JSONObject("""{"title":"Old Sydney adventure","profile":{"date":"2025-10-23"},"days":[{"date":"2025-10-23","stops":[{"name":"Opera House"}]},{"date":"2025-10-24","stops":[]}]}""")
        assertTrue(tripLibraryMatches(trip,"sYDney",null))
        assertTrue(tripLibraryMatches(trip," Opera ","2025-10-24"))
        assertFalse(tripLibraryMatches(trip,"Sydney","1999-01-01"))
        assertFalse(tripLibraryMatches(trip,"unknown",null))
        assertEquals(listOf("2025-10-23","2025-10-24"),tripLibraryDates(trip))
        trip.put("days",org.json.JSONArray())
        assertEquals(listOf("2025-10-23"),tripLibraryDates(trip))
    }
}
