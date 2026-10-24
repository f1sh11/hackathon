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

class PoiCategoryFlowTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun detailedFilterSendsCafeCategoryAndRetainsItsGroup(){
        lateinit var store:BuddyStore
        var originalProvider=MapProvider.OSM;var originalSource="direct"
        val calls=CopyOnWriteArrayList<String>()
        rule.activityRule.scenario.onActivity{activity->
            store=ViewModelProvider(activity)[BuddyStore::class.java]
            originalProvider=store.mapProvider;originalSource=store.travelDataSource
            store.chooseMapProvider(MapProvider.OSM);store.chooseTravelDataSource("direct")
            activity.setContent{PoiDiscoveryDialog(store,onDismiss={},requester={request->calls.add(request.getString("category"));JSONObject("{\"places\":[]}")})}
        }
        try{
            rule.waitUntil(5000){calls.contains("all")}
            rule.onNodeWithTag("poi-category-cafes").performScrollTo().performClick()
            rule.waitUntil(5000){calls.contains("cafes")}
            rule.runOnIdle{assertEquals("food",PoiDiscoveryCategory.parent("cafes"))}
            rule.onNodeWithTag("poi-category-restaurants").assertExists()
        }finally{rule.runOnIdle{store.chooseMapProvider(originalProvider);store.chooseTravelDataSource(originalSource)}}
    }
}
