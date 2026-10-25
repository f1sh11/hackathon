package com.sydneybuddy.travel

import java.time.ZoneId
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class TripMetadataTest {
    @Test fun creationDateUsesTimestampRatherThanTravelDate() {
        assertEquals("2026-10-25 09:30",tripCreatedDate("2026-10-24T22:30:00.1234567Z",ZoneId.of("Australia/Sydney")))
        assertEquals("2026-10-24 22:30",tripCreatedDate("2026-10-24T22:30:00+00:00",ZoneId.of("UTC")))
        assertNull(tripCreatedDate(""));assertNull(tripCreatedDate("unknown"))
    }
    @Test fun renamingOnlySendsNameIdentityAndRevisionAndKeepsOriginal() {
        val trip=JSONObject("{\"id\":\"trip-test\",\"title\":\"Original\",\"revision\":3,\"createdAt\":\"2026-10-24T22:30:00Z\",\"profile\":{\"date\":\"2026-11-01\"}}")
        val before=trip.toString();val payload=tripRenamePayload(trip,"  My holiday  ")
        assertTrue(payload.getBoolean("renameOnly"));assertEquals(3,payload.getInt("expectedRevision"))
        assertEquals("My holiday",payload.getJSONObject("trip").getString("title"));assertEquals(before,trip.toString())
        assertFalse(payload.getJSONObject("trip").has("createdAt"));assertFalse(payload.getJSONObject("trip").has("profile"))
        assertTrue(runCatching{tripRenamePayload(trip,"  ")}.isFailure)
        assertTrue(runCatching{tripRenamePayload(trip,"x".repeat(41))}.isFailure)
    }
}
