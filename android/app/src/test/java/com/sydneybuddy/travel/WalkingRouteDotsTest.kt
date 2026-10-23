package com.sydneybuddy.travel

import org.junit.Assert.*
import org.junit.Test

class WalkingRouteDotsTest {
    @Test fun dotSizesAdaptToTheViewportWithoutBecomingTinyOrTouching() {
        val phone=walkingDotStyle(390f)
        val compact=walkingDotStyle(180f)
        val tablet=walkingDotStyle(1000f)
        assertTrue(compact.radius>=1.98f)
        assertTrue(tablet.radius>phone.radius)
        assertTrue(tablet.radius<=2.54f)
        listOf(compact,phone,tablet).forEach {assertTrue(it.spacing>2*(it.radius+it.halo))}
    }

    @Test fun dotsFollowTheBendRatherThanCutAcrossIt() {
        val start=0.0 to 0.0;val corner=0.001 to 0.0;val end=0.001 to 0.001
        val dots=walkingRouteDots(listOf(start,corner,end),20.0)
        assertEquals(start,dots.first());assertTrue(dots.size>8)
        assertTrue(dots.all{it.second==0.0 || it.first==0.001})
        assertTrue(dots.any{it.second>0.0})
    }
    @Test fun spacingChangesDotDensityWithoutChangingTheRoad() {
        val path=listOf(-33.86 to 151.21,-33.859 to 151.21)
        assertTrue(walkingRouteDots(path,8.0).size>walkingRouteDots(path,24.0).size)
        assertTrue(walkingRouteDots(path,8.0).all{it.second==151.21})
    }
    @Test fun missingInvalidAndZeroLengthShapesDoNotProduceAnInventedRoute() {
        assertTrue(walkingRouteDots(emptyList(),10.0).isEmpty())
        assertTrue(walkingRouteDots(listOf(0.0 to 0.0,Double.NaN to 1.0),10.0).isEmpty())
        assertTrue(walkingRouteDots(listOf(0.0 to 0.0,0.0 to 0.0),10.0).isEmpty())
    }
    @Test fun largeRoutesHaveBoundedDotCount() {
        assertTrue(walkingRouteDots(listOf(0.0 to 0.0,10.0 to 0.0),0.001).size<=8000)
    }
    @Test fun routeIdentityIncludesGeometryAndTransportModeEvenWithEqualPointCounts() {
        val a=JourneyMapSection("walk",listOf(0.0 to 0.0,0.001 to 0.0))
        val b=JourneyMapSection("walk",listOf(0.0 to 0.0,0.001 to 0.001))
        assertNotEquals(OsmRouteScene("day",null,listOf(a)),OsmRouteScene("day",null,listOf(b)))
        assertNotEquals(OsmRouteScene("day",null,listOf(a)),OsmRouteScene("day",null,listOf(a.copy(mode="bus"))))
    }
}
