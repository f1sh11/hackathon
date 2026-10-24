package com.sydneybuddy.travel

import org.junit.Assert.*
import org.junit.Test

class PoiViewportTest {
    @Test fun ignoresIdleCallbacksAndSmallJitterButAcceptsPanningAndZoom() {
        val point=PoiViewport(-33.865,151.212,15.0)
        assertFalse(point.differs(point))
        assertFalse(PoiViewport(-33.86501,151.21201,15.02).differs(point))
        assertTrue(PoiViewport(-33.88,151.23,15.0).differs(point))
        assertTrue(PoiViewport(-33.865,151.212,16.0).differs(point))
    }
    @Test fun boundsSearchRadiusAndShrinksItWhenZoomingIn() {
        assertEquals(5000,PoiViewport(-33.865,151.212,8.0).radius)
        assertEquals(500,PoiViewport(-33.865,151.212,20.0).radius)
        assertTrue(PoiViewport(-33.865,151.212,16.0).radius < PoiViewport(-33.865,151.212,15.0).radius)
    }
}
