package com.sydneybuddy.travel

import android.graphics.Bitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.filters.LargeTest
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File

/** Opt-in smoke test against local Zetaris and free public feeds, never paid providers. */
@LargeTest
class TravelInsightLiveTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun mapDisplaysRealZetarisRoadAndFeeDataWithoutInventingCrowds(){
        var original="direct";var provider=MapProvider.OSM;var tab=1;var onboarding=false
        try {
            rule.activityRule.scenario.onActivity{activity->val store=ViewModelProvider(activity)[BuddyStore::class.java]
                original=store.travelDataSource;provider=store.mapProvider;tab=store.tab;onboarding=store.onboarding
                store.onboarding=false;store.chooseMapProvider(MapProvider.OSM);store.chooseTravelDataSource("zetaris");store.tab=0
            }
            rule.waitUntil(90000){var ready=false;rule.activityRule.scenario.onActivity{activity->val s=ViewModelProvider(activity)[BuddyStore::class.java];ready=!s.insightsBusy&&s.insights?.optString("dataSource")=="zetaris"};ready}
            rule.onNodeWithTag("travel-insights-open").performClick()
            rule.onNodeWithTag("travel-insights-sheet").assertExists()
            rule.onNodeWithText("來源：zetaris",substring=true).assertExists()
            rule.onNodeWithText("附近參考收費").assertExists()
            rule.onAllNodesWithText("人潮與排隊尚無可靠資料").onFirst().assertExists()
            rule.activityRule.scenario.onActivity{activity->val report=ViewModelProvider(activity)[BuddyStore::class.java].insights!!
                assertEquals("reported",report.getJSONObject("traffic").getString("status"));assertTrue(report.array("pricePlaces").length()>0)
                report.array("places").objects().forEach{assertEquals("unknown",it.getJSONObject("crowd").getString("status"));assertTrue(it.getJSONObject("crowd").isNull("people"))}
            }
            val context=InstrumentationRegistry.getInstrumentation().targetContext
            val screenshot=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
            File(context.getExternalFilesDir(null),"android-zetaris-insights.png").outputStream().use{screenshot.compress(Bitmap.CompressFormat.PNG,100,it)};screenshot.recycle()
        } finally {
            rule.activityRule.scenario.onActivity{activity->val s=ViewModelProvider(activity)[BuddyStore::class.java];s.chooseTravelDataSource(original);s.chooseMapProvider(provider);s.tab=tab;s.onboarding=onboarding}
        }
    }
}
