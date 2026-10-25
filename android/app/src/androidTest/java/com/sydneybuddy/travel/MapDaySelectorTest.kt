package com.sydneybuddy.travel

import androidx.activity.compose.setContent
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class MapDaySelectorTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun sevenDayTripCanScrollAndSelectLastDay(){
        val selected=mutableIntStateOf(0)
        rule.activityRule.scenario.onActivity{activity->
            activity.setContent{MapDaySelector((25..31).map{"2026-10-$it"},selected.intValue){selected.intValue=it}}
        }
        rule.onNodeWithTag("map-day-0").assertIsDisplayed()
        rule.onNodeWithTag("map-day-selector").performScrollToIndex(6)
        rule.onNodeWithTag("map-day-6").assertIsDisplayed().performClick()
        rule.runOnIdle{assertEquals(6,selected.intValue)}
        rule.onNodeWithTag("map-day-6").assertIsSelected()
        rule.onNodeWithText("10-31",useUnmergedTree=true).assertIsDisplayed()
        rule.onNodeWithTag("map-day-selector").performScrollToIndex(0)
        rule.onNodeWithTag("map-day-0").performClick()
        rule.runOnIdle{assertEquals(0,selected.intValue)}
    }
}
