package com.sydneybuddy.travel

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class MapProviderPolicyTest {
    @Test fun missingOrUnknownPreferenceDefaultsToOsm() {
        assertEquals(MapProvider.OSM,MapProvider.fromStored(null))
        assertEquals(MapProvider.OSM,MapProvider.fromStored("removed-provider"))
        MapProvider.entries.forEach {assertEquals(it,MapProvider.fromStored(it.name))}
    }
    @Test fun liveGoogleContentCannotLeakToOtherBaseMaps() {
        val day=JSONObject("""{"stops":[{"id":"example"}],"legs":[{"polyline":"provider-owned"}]}""")
        assertNull(mapDisplayDay(day,MapProvider.OSM,true))
        assertNull(mapDisplayDay(day,MapProvider.SCHEMATIC,true))
        assertSame(day,mapDisplayDay(day,MapProvider.GOOGLE,true))
        assertEquals("provider-owned",day.getJSONArray("legs").getJSONObject(0).getString("polyline"))
    }
    @Test fun demoTripRemainsAvailableAcrossProviders() {
        val day=JSONObject("""{"stops":[{"id":"example"}]}""")
        MapProvider.entries.forEach {assertSame(day,mapDisplayDay(day,it,false))}
    }
}
