package com.sydneybuddy.travel

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

class TripMetadataFlowTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun cardShowsCreationDateAndRenamesWithoutOpeningEditor() {
        var trip by mutableStateOf(JSONObject("{\"id\":\"metadata-test\",\"title\":\"Original\",\"createdAt\":\"2026-10-24T22:30:00Z\",\"profile\":{\"date\":\"2026-11-01\"},\"days\":[]}"))
        var editing=false;var renaming by mutableStateOf(false)
        rule.activityRule.scenario.onActivity{activity->activity.setContent{MaterialTheme{Column{
            SavedTripCard(trip,onEdit={editing=true},onNavigate={},onMap={},onDelete={},onRename={renaming=true})
            if(renaming)TripNameDialog(trip,false,onDismiss={renaming=false},onSave={name->trip=JSONObject(trip.toString()).put("title",name);true})
        }}}}
        rule.onNodeWithTag("trip-created-metadata-test",useUnmergedTree=true).assertExists()
        rule.onNode(hasText(tripCreatedDate("2026-10-24T22:30:00Z")!!,substring=true)).assertExists()
        rule.onNodeWithTag("rename-trip-metadata-test").performClick()
        rule.onNodeWithTag("trip-name-field").performTextReplacement("My Sydney holiday")
        rule.onNodeWithTag("trip-name-save").performClick()
        rule.onNodeWithText("My Sydney holiday").assertExists()
        rule.runOnIdle{assertFalse(editing);assertFalse(renaming);assertEquals("2026-11-01",trip.getJSONObject("profile").getString("date"));assertEquals("2026-10-24T22:30:00Z",trip.getString("createdAt"))}
    }
}
