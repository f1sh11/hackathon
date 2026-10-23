package com.sydneybuddy.travel
import org.junit.Assert.*
import org.junit.Test
class JourneyFormatTest {
    @Test fun sydneyClockHandlesSummerAndWinterOffsets(){
        assertEquals("10:10",journeyTime("2026-10-22T23:10:00Z"))
        assertEquals("09:10",journeyTime("2026-06-22T23:10:00Z"))
        assertEquals("时间未提供",journeyTime("invalid"))
    }
    @Test fun missingBusNumberIsNotReplacedByAGuess(){
        val step=JourneyStep("bus",null,null,"A","B","2026-10-23T10:00:00+11:00","2026-10-23T10:15:00+11:00",false,null,100.0,900.0,0.0,null,null,emptyList())
        assertTrue(step.title.contains("路线号码未提供"))
    }
}
