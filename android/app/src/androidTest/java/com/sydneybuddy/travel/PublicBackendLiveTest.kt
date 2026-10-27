package com.sydneybuddy.travel

import android.graphics.Bitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.platform.app.InstrumentationRegistry
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/** Opt-in: credentials arrive through a private app file, never test arguments. */
class PublicBackendLiveTest {
    @get:Rule val rule=createAndroidComposeRule<MainActivity>()
    @Test fun cloudColocationUsesManagedDataThroughPathPrefix(){
        assumeTrue(InstrumentationRegistry.getArguments().getString("cloudColocationSmoke")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val file=File(context.filesDir,"backend-public-smoke.json")
        val config=JSONObject(file.readText());file.delete()
        val base=config.getString("url");val code=config.getString("accessCode")
        assertTrue(base.startsWith("https://") && base.endsWith("/sydneybuddy"))
        val anonymous=URL("$base/health").openConnection() as HttpURLConnection
        try{anonymous.connectTimeout=15000;anonymous.readTimeout=15000;assertEquals(401,anonymous.responseCode)}finally{anonymous.disconnect()}
        rule.waitUntil(45000){var idle=false;rule.activityRule.scenario.onActivity{idle=!ViewModelProvider(it)[BuddyStore::class.java].busy};idle}
        rule.activityRule.scenario.onActivity{val store=ViewModelProvider(it)[BuddyStore::class.java];store.onboarding=false;store.tab=4;store.chooseBackend("cloud",base,code)}
        rule.waitUntil(120000){var ready=false;rule.activityRule.scenario.onActivity{val store=ViewModelProvider(it)[BuddyStore::class.java];ready=store.base==base&&!store.busy&&store.health?.optString("status")=="ok"&&store.travelDataSource=="zetaris-cloud"&&!store.mapLayerIds.isNullOrEmpty()};ready}
        rule.activityRule.scenario.onActivity{val store=ViewModelProvider(it)[BuddyStore::class.java];assertTrue(store.health!!.optBoolean("nvidiaConfigured"));assertNull(store.error)}
    }

    @Test fun publicHttpsAutomaticallyUsesManagedCloudData(){
        assumeTrue(InstrumentationRegistry.getArguments().getString("publicBackendSmoke")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val configFile=File(context.filesDir,"backend-public-smoke.json")
        val config=JSONObject(configFile.readText());configFile.delete()
        val base=config.getString("url");val code=config.getString("accessCode")
        assertTrue(base.startsWith("https://"))
        val anonymous=URL("$base/health").openConnection() as HttpURLConnection
        try {anonymous.connectTimeout=15000;anonymous.readTimeout=15000;assertEquals(401,anonymous.responseCode)}finally{anonymous.disconnect()}
        rule.waitUntil(45000){var idle=false;rule.activityRule.scenario.onActivity{idle=!ViewModelProvider(it)[BuddyStore::class.java].busy};idle}
        rule.activityRule.scenario.onActivity{val s=ViewModelProvider(it)[BuddyStore::class.java];s.onboarding=false;s.tab=4}
        rule.onNodeWithTag("backend-cloud").performScrollTo().performClick()
        rule.onNodeWithTag("backend-address").performScrollTo().performTextReplacement(base)
        rule.onNodeWithTag("backend-access-code").performScrollTo().performTextInput(code)
        rule.onNodeWithTag("backend-apply").performScrollTo().performClick()
        rule.waitUntil(120000){var ready=false;rule.activityRule.scenario.onActivity{val s=ViewModelProvider(it)[BuddyStore::class.java];ready=s.base==base&&!s.busy&&s.health?.optString("managedTravelDataSource")=="zetaris-cloud"&&s.travelDataSource=="zetaris-cloud"&&s.dataConnection?.contains("8")==true};ready}
        rule.onNodeWithTag("backend-status").performScrollTo().assertTextEquals("所選後端已連線")
        val bitmap=InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
        File(context.getExternalFilesDir(null),"android-public-https-connected.png").outputStream().use{bitmap.compress(Bitmap.CompressFormat.PNG,100,it)};bitmap.recycle()
        rule.activityRule.scenario.onActivity{ViewModelProvider(it)[BuddyStore::class.java].refreshWeather()}
        rule.waitUntil(120000){var ready=false;rule.activityRule.scenario.onActivity{val s=ViewModelProvider(it)[BuddyStore::class.java];ready=!s.weatherBusy&&s.weather?.optString("status")=="ok"&&s.weather?.optString("dataSource")=="zetaris-cloud"};ready}
        rule.activityRule.scenario.onActivity{val s=ViewModelProvider(it)[BuddyStore::class.java];assertFalse(s.weather!!.optBoolean("cached",true))}
        rule.onNodeWithTag("backend-local").performScrollTo().performClick()
        rule.onNodeWithTag("backend-apply").performScrollTo().performClick()
        waitFor("http://127.0.0.1:8787","direct")
        rule.onNodeWithTag("backend-cloud").performScrollTo().performClick()
        // An empty field reuses the encrypted access code already saved for this URL.
        rule.onNodeWithTag("backend-apply").performScrollTo().performClick()
        waitFor(base,"zetaris-cloud")
    }
    private fun waitFor(base:String,source:String){rule.waitUntil(120000){var ready=false;rule.activityRule.scenario.onActivity{val s=ViewModelProvider(it)[BuddyStore::class.java];ready=s.base==base&&!s.busy&&s.health?.optString("status")=="ok"&&s.travelDataSource==source&&!s.mapLayerIds.isNullOrEmpty()};ready}}
}
