package com.sydneybuddy.travel

import androidx.activity.compose.setContent
import androidx.compose.material3.Text
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.util.concurrent.CopyOnWriteArrayList

class PoiViewportFlowTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun cameraIdleSearchUsesLastViewportAndDoesNotRepeatForMarkerRefresh() {
        lateinit var nearby:NearbyPoiState
        lateinit var store:BuddyStore
        var originalProvider=MapProvider.OSM
        var originalSource="direct"
        val centers=CopyOnWriteArrayList<Double>()
        rule.activityRule.scenario.onActivity{activity->
            store=ViewModelProvider(activity)[BuddyStore::class.java]
            originalProvider=store.mapProvider;originalSource=store.travelDataSource
            store.chooseMapProvider(MapProvider.OSM);store.chooseTravelDataSource("direct")
            activity.setContent{
                nearby=rememberNearbyPoi(store){request->centers.add(request.getDouble("lat"));JSONObject("{\"places\":[{\"id\":\"test-poi\"}]}")}
                Text("points: "+nearby.places.size)
            }
        }
        try {
        rule.waitForIdle()
        rule.runOnIdle{nearby.move(-33.88,151.22,15.0);nearby.move(-33.90,151.24,16.0)}
        rule.waitUntil(5000){centers.size==1}
        rule.runOnIdle{assertEquals(-33.90,centers.single(),0.000001);assertEquals(1,nearby.places.size);nearby.move(-33.90,151.24,16.0)}
        Thread.sleep(1000)
        rule.runOnIdle{assertEquals(1,centers.size)}
        }finally{rule.runOnIdle{store.chooseMapProvider(originalProvider);store.chooseTravelDataSource(originalSource)}}
    }
}
