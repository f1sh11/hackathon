package com.sydneybuddy.travel

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import org.junit.Rule
import org.junit.Test

/** Runs against a local development backend with adb reverse configured.
 * Creates demo trips and one local expense; never invokes paid providers.
 */
class BuddyFlowTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private fun click(text: String) {
        val node=rule.onNode(hasText(text,substring=false) and hasClickAction())
        if(text !in listOf("Map","Trips","Ask AI","Expenses","Me","New trip","Selected. Let's continue.","Let Koko plan the route","View route","3 quick tasks before departure","Pack bags")) node.performScrollTo()
        node.performClick()
    }
    private fun awaitText(text: String) {
        rule.waitUntil(30000) { rule.onAllNodesWithText(text,substring=true).fetchSemanticsNodes().isNotEmpty() }
    }
    private fun newDemoTrip() {
        if(rule.onAllNodesWithText("Which day to go to Sydney?",substring=false).fetchSemanticsNodes().isEmpty()) { click("Trips");click("New trip") }
        repeat(7) {click("Selected. Let's continue.")}
        click("Let Koko plan the route")
        awaitText("My timeline")
        awaitText("Trip Remaining Balance")
        rule.waitUntil(30000){rule.onAllNodes(hasText("Confirm and save") and isEnabled()).fetchSemanticsNodes().isNotEmpty()}
        click("Confirm and save")
        rule.onNodeWithText("Confirm save").performClick()
        awaitText("Start navigation")
    }

    @Test fun onboardingSaveAdjustmentCancelAndLocalPersistence() {
        val countBefore=org.json.JSONArray(java.net.URL("http://127.0.0.1:8787/trips").readText()).length()
        rule.waitUntil(30000) {rule.onAllNodesWithText("Koko is on it",substring=true).fetchSemanticsNodes().isEmpty()}
        newDemoTrip()
        click("Ask AI")
        click("Less walking")
        awaitText("My timeline")
        click("Discard draft")
        click("Map")
        rule.onNodeWithTag("home-info-toggle").performClick()
        rule.onNodeWithText("Pack bags").performScrollTo().performClick()
        click("View route")
        click("Done")
        click("Expenses")
        rule.onNodeWithText("Amount (AUD)").performTextInput("10.01")
        click("＋")
        click("Save expense")
        val split="Split between 3 people: A$3.34 / A$3.34 / A$3.33"
        rule.onAllNodesWithText(split).onFirst().performScrollTo().assertIsDisplayed()
        rule.activityRule.scenario.recreate()
        awaitText("Travel expenses")
        rule.onAllNodesWithText(split).onFirst().performScrollTo().assertIsDisplayed()
        click("Map")
        newDemoTrip()
        click("Trips")
        rule.onAllNodesWithText("View map").assertCountEquals(countBefore+2)
    }
}
