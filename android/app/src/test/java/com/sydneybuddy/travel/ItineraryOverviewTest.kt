package com.sydneybuddy.travel

import org.junit.Assert.*
import org.junit.Test

class ItineraryOverviewTest {
    @Test fun polyline6IsNotDecodedAsGooglePolyline5() {
        // Official encoded-polyline example, scaled to polyline6 coordinates.
        val encoded="_p~iF~ps|U_ulLnnqC_mqNvxq`@"
        val points=decodePolyline(encoded,1e6)
        assertEquals(3.85,points[0].first,0.000001)
        assertEquals(-12.02,points[0].second,0.000001)
        assertEquals(38.5,decodePolyline(encoded)[0].first,0.000001)
        assertThrows(IllegalArgumentException::class.java){decodePolyline("~",1e6)}
    }
    @Test fun loopUsesOneStartReturnBadgeAndNumbersFollowItinerary() {
        val a=-33.86 to 151.21;val b=-33.85 to 151.22
        assertEquals(listOf("起/终","1","终"),itineraryBadges(listOf(a,b,a)))
        assertEquals(listOf("起","1","终"),itineraryBadges(listOf(a,b,b)))
        assertEquals("↑",itineraryDirection(a,(-33.85 to 151.21)))
        assertEquals("→",itineraryDirection(a,(-33.86 to 151.22)))
    }
    @Test fun nearbyStopsKeepBothNumbersInOneBadge() {
        val groups=itineraryPinGroups(listOf(-33.8611 to 151.2126,-33.8599 to 151.209,-33.8599 to 151.2091,-33.8611 to 151.2126))
        assertEquals(listOf(1,2),groups[1]);assertEquals(2,groups.size)
    }
    @Test fun malformedResponsesNeverBecomeActualWalkingRoutes() {
        assertThrows(Exception::class.java){OverviewRoute.parse("""{"code":"Ok","routes":[{"geometry":"~","distance":5,"duration":8}]}""")}
        assertThrows(Exception::class.java){OverviewRoute.parse("""{"code":"Error"}""")}
    }
}
