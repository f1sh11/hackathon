package com.sydneybuddy.travel

import androidx.activity.compose.setContent
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import java.time.YearMonth

class TripLibraryCalendarTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun canBrowseAndSelectPastTripDates(){
        val selected=mutableStateOf<String?>(null)
        val oldDate=YearMonth.now().minusMonths(1).atDay(15).toString()
        rule.activityRule.scenario.onActivity{activity->activity.setContent{TripLibraryCalendar(selected.value,setOf(oldDate)){selected.value=it}}}
        rule.onNodeWithTag("trip-month-previous").performClick()
        rule.onNodeWithTag("trip-date-$oldDate").assertIsDisplayed().performClick()
        rule.runOnIdle{assertEquals(oldDate,selected.value)}
        rule.onNodeWithTag("trip-date-$oldDate").assertIsSelected()
    }
}
