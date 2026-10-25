package com.sydneybuddy.travel

import androidx.activity.compose.setContent
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.util.concurrent.CopyOnWriteArrayList

class SocialImportFlowTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun jsonPreviewDoesNotCallAiAndOnlySavesAfterConfirmation(){
        val calls=CopyOnWriteArrayList<String>()
        rule.activityRule.scenario.onActivity{activity->val store=ViewModelProvider(activity)[BuddyStore::class.java]
            activity.setContent{SocialImportDialog(store,onDismiss={},requester={path,body->calls.add(path);JSONObject().put("id","social:test").put("status","draft").put("mentions",body!!.getJSONArray("mentions"))})}
        }
        rule.onNodeWithTag("social-json-mode").performClick()
        rule.onNodeWithTag("social-import-json").performTextInput("""{"platform":"threads","postUrl":"https://www.threads.com/@test/post/test","text":"Cafe","mentions":[{"name":"Cafe","kind":"restaurant","quote":"Cafe"}]}""")
        rule.onNodeWithTag("social-import-preview").performScrollTo().performClick()
        rule.onNodeWithTag("social-import-save").assertExists()
        assertTrue(calls.isEmpty())
        rule.onNodeWithTag("social-import-save").performScrollTo().performClick()
        rule.waitUntil(5000){calls.contains("social/import")}
        assertEquals(listOf("social/import"),calls.toList())
        rule.onNodeWithTag("social-import-message").performScrollTo().assertIsDisplayed()
    }
}
