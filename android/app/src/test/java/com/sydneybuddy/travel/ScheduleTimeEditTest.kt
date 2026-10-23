package com.sydneybuddy.travel
import org.junit.Assert.*
import org.junit.Test
class ScheduleTimeEditTest {
    @Test fun handlesSnapAndKeepOppositeEdge() {
        assertEquals(ScheduleTimeEdit(595,60),ScheduleTimeEdit.drag(600,60,-2.5f,0))
        assertEquals(ScheduleTimeEdit(615,60),ScheduleTimeEdit.drag(600,60,13f,0))
        assertEquals(ScheduleTimeEdit(615,45),ScheduleTimeEdit.drag(600,60,13f,-1))
        assertEquals(ScheduleTimeEdit(600,75),ScheduleTimeEdit.drag(600,60,13f,1))
        assertEquals(ScheduleTimeEdit(0,5),ScheduleTimeEdit.drag(0,5,-100f,-1))
        assertEquals(5,ScheduleTimeEdit.drag(600,60,1000f,-1).duration)
        assertEquals(240,ScheduleTimeEdit.drag(600,60,1000f,1).duration)
    }
}
