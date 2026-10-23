package com.sydneybuddy.travel

import androidx.test.platform.app.InstrumentationRegistry
import com.stadiamaps.ferrostar.core.http.HttpClientProvider
import com.stadiamaps.ferrostar.core.http.IResponse
import kotlinx.coroutines.*
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import uniffi.ferrostar.*

/** Runs the real Rust parser and navigation engine; recorded OSM route avoids network-dependent tests. */
class WalkingNavigationTest {
    private val context get()=InstrumentationRegistry.getInstrumentation().targetContext
    private fun fixture()=InstrumentationRegistry.getInstrumentation().context.assets.open("walking-sydney.json").use{it.readBytes()}
    private fun day()=JSONObject("""{"date":"2026-10-23","start":{"name":"Quay","lat":-33.8611,"lng":151.2126},"stops":[{"name":"Opera House","lat":-33.8568,"lng":151.2153,"visitMinutes":60}],"end":{"name":"Quay","lat":-33.8611,"lng":151.2126}}""")
    private fun client(status:Int=200)=object:HttpClientProvider {
        override suspend fun call(request:RouteRequest):IResponse {
            delay(50)
            return object:IResponse {
                override val isSuccessful=status==200
                override val code=status
                override fun bodyBytes()=fixture()
            }
        }
    }
    @Test fun realFerrostarSimulationArrivesThenWaitsForNextStop()=runBlocking {
        val nav=withContext(Dispatchers.Main){WalkingNavigator(context,day(),"test-${System.nanoTime()}","http://127.0.0.1/unused",client()).also{it.chooseSimulation(true);it.start()}}
        try {
            withTimeout(20000){while(!withContext(Dispatchers.Main){nav.progress.arrived || nav.message!=null})delay(100)}
            withContext(Dispatchers.Main){
                assertNull(nav.message);assertTrue(nav.progress.arrived);assertFalse(nav.active)
                assertEquals(0,nav.progress.leg);assertEquals("Opera House",nav.target.name)
                assertTrue(nav.geometry.size>10)
            }
            delay(500)
            withContext(Dispatchers.Main){assertEquals(0,nav.progress.leg);nav.next();assertEquals(1,nav.progress.leg);nav.pause()}
        } finally {withContext(Dispatchers.Main){nav.close()}}
    }
    @Test fun zeroDistanceArrivesWithoutCallingRouteService()=runBlocking {
        val same=day().put("stops",org.json.JSONArray())
        var calls=0
        val nav=withContext(Dispatchers.Main){WalkingNavigator(context,same,"zero-${System.nanoTime()}","http://127.0.0.1/unused",object:HttpClientProvider{
            override suspend fun call(request:RouteRequest):IResponse {calls++;error("Must not request a zero-distance route")}
        }).also{it.chooseSimulation(true);it.start()}}
        try {
            withTimeout(5000){while(withContext(Dispatchers.Main){nav.busy})delay(20)}
            withContext(Dispatchers.Main){assertNull(nav.message);assertTrue(nav.progress.arrived);assertFalse(nav.active);assertEquals(0,calls)}
        } finally {withContext(Dispatchers.Main){nav.close()}}
    }
    @Test fun cancellationCannotStartNavigationAfterPause()=runBlocking {
        val nav=withContext(Dispatchers.Main){WalkingNavigator(context,day(),"cancel-${System.nanoTime()}","http://127.0.0.1/unused",object:HttpClientProvider{
            override suspend fun call(request:RouteRequest):IResponse {delay(1000);return client().call(request)}
        }).also{it.chooseSimulation(true);it.start()}}
        try {
            delay(100);withContext(Dispatchers.Main){nav.pause()};delay(1200)
            withContext(Dispatchers.Main){assertFalse(nav.active);assertFalse(nav.busy);assertFalse(nav.progress.arrived);assertEquals(0,nav.progress.leg)}
        } finally {withContext(Dispatchers.Main){nav.close()}}
    }
    @Test fun simulationAndFailedRequestsNeverOverwriteRealProgress()=runBlocking {
        val id="test-${System.nanoTime()}"
        val nav=withContext(Dispatchers.Main){WalkingNavigator(context,day(),id,"http://127.0.0.1/unused",client(503))}
        try {
            withContext(Dispatchers.Main){nav.chooseSimulation(true);nav.arrive();nav.chooseSimulation(false);assertFalse(nav.progress.arrived);nav.chooseSimulation(true);nav.start()}
            withTimeout(10000){while(withContext(Dispatchers.Main){nav.busy})delay(100)}
            withContext(Dispatchers.Main){
                assertNotNull(nav.message);assertEquals(0,nav.progress.leg);assertFalse(nav.active);assertFalse(nav.progress.arrived)
                nav.chooseSimulation(false);nav.arrive()
                val restored=WalkingNavigator(context,day(),id,"http://127.0.0.1/unused",client())
                assertTrue(restored.progress.arrived);assertEquals(0,restored.progress.leg)
                restored.close()
            }
        } finally {withContext(Dispatchers.Main){context.getSharedPreferences("walking",0).edit().remove(nav.key).commit();nav.close()}}
    }
}
