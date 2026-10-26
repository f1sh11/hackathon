package com.sydneybuddy.travel

import android.graphics.Bitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import java.io.File

/** Explicit opt-in: requires private SSH tunnel 8790 and a local backend 8787. */
class BackendSwitchLiveTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun switchesBetweenLocalAndOracleAndRendersConnectionStatus(){
        assumeTrue(InstrumentationRegistry.getArguments().getString("oracleBackendSmoke")=="true")
        rule.waitUntil(30000){var idle=false;rule.activityRule.scenario.onActivity{idle=!ViewModelProvider(it)[BuddyStore::class.java].busy};idle}
        rule.activityRule.scenario.onActivity{val s=ViewModelProvider(it)[BuddyStore::class.java];s.onboarding=false;s.tab=4;s.chooseTravelDataSource("direct")}
        rule.onNodeWithTag("backend-cloud").performScrollTo().performClick()
        rule.onNodeWithTag("backend-apply").performScrollTo().performClick()
        waitFor("http://127.0.0.1:8790")
        rule.onNodeWithTag("backend-status").performScrollTo().assertTextEquals("Selected backend connected")
        screenshot("android-oracle-connected.png")
        rule.onNodeWithTag("backend-local").performScrollTo().performClick()
        rule.onNodeWithTag("backend-apply").performScrollTo().performClick()
        waitFor("http://127.0.0.1:8787")
        rule.onNodeWithTag("backend-status").performScrollTo().assertTextEquals("Selected backend connected")
        screenshot("android-local-connected.png")
        rule.onNodeWithTag("backend-cloud").performScrollTo().performClick()
        rule.onNodeWithTag("backend-apply").performScrollTo().performClick()
        waitFor("http://127.0.0.1:8790")
    }
    private fun waitFor(base:String){rule.waitUntil(90000){var ready=false;rule.activityRule.scenario.onActivity{val s=ViewModelProvider(it)[BuddyStore::class.java];ready=s.base==base&&!s.busy&&s.health?.optString("status")=="ok"};ready}}
    private fun screenshot(name:String){val context=InstrumentationRegistry.getInstrumentation().targetContext;val bitmap=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot();File(context.getExternalFilesDir(null),name).outputStream().use{bitmap.compress(Bitmap.CompressFormat.PNG,100,it)};bitmap.recycle()}
}
