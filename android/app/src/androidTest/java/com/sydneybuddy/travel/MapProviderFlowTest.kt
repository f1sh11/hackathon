package com.sydneybuddy.travel

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.platform.app.InstrumentationRegistry
import android.graphics.Bitmap
import java.io.File
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

/** Tests provider settings without generating a trip or calling paid services. */
class MapProviderFlowTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    private fun click(text:String,scroll:Boolean=false) {
        val node=rule.onNode(hasText(text,substring=false) and hasClickAction())
        if(scroll) node.performScrollTo()
        node.performClick()
    }
    @Test fun travelDataSourcePersistsWithoutChangingMapOrTrip() {
        var original="direct";var provider=MapProvider.OSM;var trip:String?=null
        rule.activityRule.scenario.onActivity{activity->val store=ViewModelProvider(activity)[BuddyStore::class.java];original=store.travelDataSource;provider=store.mapProvider;trip=store.trip?.toString();store.chooseTravelDataSource("zetaris")}
        rule.activityRule.scenario.recreate()
        rule.activityRule.scenario.onActivity{activity->val store=ViewModelProvider(activity)[BuddyStore::class.java];assertEquals("zetaris",store.travelDataSource);assertEquals(provider,store.mapProvider);assertEquals(trip,store.trip?.toString());store.chooseTravelDataSource("direct")}
        rule.activityRule.scenario.recreate()
        rule.activityRule.scenario.onActivity{activity->val store=ViewModelProvider(activity)[BuddyStore::class.java];assertEquals("direct",store.travelDataSource);store.chooseTravelDataSource(original)}
    }
    @Test fun providerSwitchPersistsAndDoesNotChangeSavedTrip() {
        rule.waitUntil(30000) {rule.onAllNodesWithText("Koko is on it",substring=true).fetchSemanticsNodes().isEmpty()}
        var before:String?=null
        rule.activityRule.scenario.onActivity { activity ->
            val store=ViewModelProvider(activity)[BuddyStore::class.java]
            store.resumeSaved();store.chooseMapProvider(MapProvider.SCHEMATIC)
        }
        rule.waitUntil(30000) {rule.onAllNodesWithText("Koko is on it",substring=true).fetchSemanticsNodes().isEmpty()}
        rule.activityRule.scenario.onActivity {activity -> before=ViewModelProvider(activity)[BuddyStore::class.java].trip?.toString()}
        click("Me")
        click("OpenStreetMap",true)
        click("Map")
        rule.onNodeWithText("© OpenStreetMap contributors").assertIsDisplayed()
        rule.waitUntil(60000) {rule.onAllNodesWithText("Map loaded",substring=true).fetchSemanticsNodes().isNotEmpty()}
        rule.waitForIdle()
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val screenshot=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
        File(context.getExternalFilesDir(null),"map-provider-osm.png").outputStream().use {screenshot.compress(Bitmap.CompressFormat.PNG,100,it)}
        screenshot.recycle()
        click("Me")
        click("Google Maps",true)
        click("Map")
        if(!BuildConfig.MAPS_CONFIGURED) rule.onNodeWithText("Google Maps is not configured.\nChoose OpenStreetMap in Settings.").assertExists()
        rule.activityRule.scenario.recreate()
        rule.waitUntil(30000) {rule.onAllNodesWithText("Koko is on it",substring=true).fetchSemanticsNodes().isEmpty()}
        rule.activityRule.scenario.onActivity {activity ->
            val store=ViewModelProvider(activity)[BuddyStore::class.java]
            assertEquals(MapProvider.GOOGLE,store.mapProvider)
            assertEquals(before,store.trip?.toString())
            store.chooseMapProvider(MapProvider.OSM)
        }
    }
}
