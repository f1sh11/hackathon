package com.sydneybuddy.travel

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Column
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.util.concurrent.atomic.AtomicInteger

class PlaceMediaFlowTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun opensWithAutomaticPhotoRequestAndAllowsRetryAfterFailure() {
        val calls=AtomicInteger(0)
        rule.activityRule.scenario.onActivity{activity->
            val store=ViewModelProvider(activity)[BuddyStore::class.java]
            activity.setContent{Column{PlaceMedia(JSONObject("{\"id\":\"test-place\",\"lat\":-33.865,\"lng\":151.212}"),store){
                if(calls.incrementAndGet()==1)throw IllegalStateException("Test failure")
                JSONObject("{\"photos\":[]}")
            }}}
        }
        rule.waitUntil(5000){calls.get()==1}
        rule.onNodeWithTag("place-media-retry").assertIsEnabled().performClick()
        rule.waitUntil(5000){calls.get()==2}
        rule.onNodeWithTag("place-media-retry").assertIsEnabled()
        assertEquals(2,calls.get())
    }
}
