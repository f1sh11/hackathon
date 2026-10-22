package com.sydneybuddy.travel

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class DomainTest {
    @Test fun moneyUsesDecimalRoundingAndExactShares() {
        assertEquals(1001, Money.cents("10.005"))
        assertEquals(101, Money.cents("1,01"))
        assertEquals(listOf(34,34,33), Money.shares(101,3))
        for (cents in 1..1000) for (people in 1..10) assertEquals(cents,Money.shares(cents,people).sum())
    }
    @Test fun invalidMoneyCannotBeSaved() {
        listOf("", "NaN", "-1", "0", "0.001", "100000").forEach { assertTrue(runCatching{Money.cents(it)}.isFailure) }
    }
    @Test fun profileRoundTripsAndRejectsInvalidDates() {
        val profile=Profile(date="2026-10-24",days=3,end=Endpoint.central)
        assertEquals(profile,Profile.from(profile.json()))
        assertFalse(profile.copy(date="2026-02-30").valid())
        assertFalse(profile.copy(endMinute=profile.startMinute).valid())
        assertFalse(profile.copy(days=4).valid())
    }
    @Test fun newTripDoesNotInheritServerIdentity() {
        val trip=JSONObject().put("id","generated-old-id").put("revision",17).put("title","new trip")
        val payload=savePayload(Proposal(trip,null,0))
        assertFalse(payload.getJSONObject("trip").has("id"))
        assertFalse(payload.getJSONObject("trip").has("revision"))
        assertEquals(0,payload.getInt("expectedRevision"))
        assertEquals("generated-old-id",trip.getString("id"))
    }
    @Test fun adjustmentPreservesCapturedRevision() {
        val payload=savePayload(Proposal(JSONObject(),"saved-trip",3))
        assertEquals("saved-trip",payload.getJSONObject("trip").getString("id"))
        assertEquals(3,payload.getInt("expectedRevision"))
    }
    @Test fun googlePolylineDecodesAndRejectsTruncatedInput() {
        val decoded=decodePolyline("_p~iF~ps|U_ulLnnqC_mqNvxq`@")
        assertEquals(3,decoded.size)
        assertEquals(38.5,decoded[0].first,0.00001)
        assertEquals(-120.95,decoded[1].second,0.00001)
        assertTrue(runCatching{decodePolyline("_")}.isFailure)
    }
}
