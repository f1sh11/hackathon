package com.sydneybuddy.travel

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.horizontalScroll
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.window.DialogWindowProvider
import android.view.WindowManager
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.foundation.clickable
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.stadiamaps.ferrostar.core.FerrostarCore
import com.stadiamaps.ferrostar.core.AndroidTtsObserver
import com.stadiamaps.ferrostar.core.http.HttpClientProvider
import com.stadiamaps.ferrostar.core.http.OkHttpClientProvider.Companion.toOkHttpClientProvider
import com.stadiamaps.ferrostar.core.location.AndroidLocationProvider
import com.stadiamaps.ferrostar.core.location.SimulatedLocationProvider
import com.stadiamaps.ferrostar.core.location.NavigationLocationProviding
import com.stadiamaps.ferrostar.core.location.toUserLocation
import com.stadiamaps.ferrostar.core.extensions.preferredUserLocation
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first
import okhttp3.OkHttpClient
import org.json.JSONObject
import org.json.JSONArray
import uniffi.ferrostar.*
import java.security.MessageDigest
import java.util.concurrent.TimeUnit

data class WalkingPoint(val name:String,val lat:Double,val lng:Double,val stay:Int=0) {
    fun location()=Location("itinerary").apply { latitude=lat;longitude=lng;accuracy=5f;time=System.currentTimeMillis() }
}
data class WalkingProgress(val leg:Int=0,val arrived:Boolean=false,val finished:Boolean=false) {
    fun arrive()=if(finished) this else copy(arrived=true)
    fun advance(count:Int)=if(!arrived || finished) this else if(leg+1>=count) copy(finished=true) else WalkingProgress(leg+1)
    fun json()=JSONObject().put("leg",leg).put("arrived",arrived).put("finished",finished)
    companion object {
        fun restore(raw:String?,count:Int):WalkingProgress = runCatching {
            val o=JSONObject(raw ?: "{}");WalkingProgress(o.optInt("leg"),o.optBoolean("arrived"),o.optBoolean("finished"))
        }.getOrDefault(WalkingProgress()).takeIf{it.leg in 0 until count} ?: WalkingProgress()
    }
}
fun walkingPoints(day:JSONObject):List<WalkingPoint> {
    fun point(o:JSONObject,stay:Int=0,suffix:String="")=WalkingPoint(o.getString("name")+suffix,o.getDouble("lat"),o.getDouble("lng"),stay)
    return listOf(point(day.getJSONObject("start")))+day.array("stops").objects().map{point(it,it.optInt("visitMinutes"))}+point(day.getJSONObject("end"),suffix=" (return)")
}
// Preserve the old key spelling so English labels do not reset saved navigation progress.
fun walkingKey(tripIdentity:String,day:JSONObject):String {
    val points=walkingPoints(day)
    val canonical=points.mapIndexed{index,point->
        val name=if(index==points.lastIndex)point.name.removeSuffix(" (return)")+"（返回）" else point.name
        "$name:${point.lat}:${point.lng}:${point.stay}"
    }.joinToString("|")
    return "walking:"+MessageDigest.getInstance("SHA-256").digest((tripIdentity+":"+day.optString("date")+":"+canonical).toByteArray()).joinToString(""){"%02x".format(it)}
}

private class LocationFixException(message:String):Exception(message)

/** Itinerary stop progress is independent of SDK state and map renderer. */
class WalkingNavigator(private val context:Context,day:JSONObject,tripIdentity:String,private val endpoint:String, private val httpClient:HttpClientProvider = OkHttpClient.Builder().addInterceptor(BackendAccess.interceptor).callTimeout(25,TimeUnit.SECONDS).followRedirects(false).build().toOkHttpClientProvider()) {
    val points=walkingPoints(day)
    val key=walkingKey(tripIdentity,day)
    private val prefs=context.getSharedPreferences("walking",0)
    var progress by mutableStateOf(WalkingProgress.restore(prefs.getString(key,null),points.size-1));private set
    private var realProgress=progress
    var simulation by mutableStateOf(false);private set
    var busy by mutableStateOf(false);private set
    var active by mutableStateOf(false);private set
    var message by mutableStateOf<String?>(null);private set
    var instruction by mutableStateOf("Ready to go");private set
    var remaining by mutableStateOf("");private set
    var geometry by mutableStateOf<List<GeographicCoordinate>>(emptyList());private set
    var position by mutableStateOf<GeographicCoordinate?>(null);private set
    var muted by mutableStateOf(false);private set
    private var core:FerrostarCore?=null
    private var speech:AndroidTtsObserver?=null
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)
    private var request:Job?=null
    private var updates:Job?=null
    private var generation=0
    val count get()=points.size-1
    val target get()=points[progress.leg+1]
    private fun save() { if(!simulation) {realProgress=progress;prefs.edit().putString(key,progress.json().toString()).apply()} }
    fun chooseSimulation(value:Boolean) { pause();simulation=value;progress=if(value) WalkingProgress() else realProgress;geometry=emptyList();position=null;message=null }
    fun pause() { generation++;request?.cancel();request=null;updates?.cancel();updates=null;core?.stopNavigation();core=null;speech?.shutdown();speech=null;busy=false;active=false }
    fun close() { pause();scope.cancel() }
    fun arrive() { pause();progress=progress.arrive();instruction="Arrived at ${target.name}";remaining="";save() }
    fun next() { if(!progress.arrived) return;progress=progress.advance(count);geometry=emptyList();position=null;save();if(!progress.finished) start() }
    fun reset() { pause();progress=WalkingProgress();geometry=emptyList();position=null;message=null;save() }
    fun toggleMute() {muted=!muted;speech?.setMuted(muted)}
    fun preview() {
        if(busy || active) return
        pause();busy=true;message=null
        val token=generation
        request=scope.launch {
            try {
                val source=SimulatedLocationProvider(scope=scope,initialLocation=points[0].location())
                val engine=FerrostarCore(wellKnownRouteProvider=WellKnownRouteProvider.Valhalla(endpoint,"pedestrian"),
                    httpClient=httpClient,
                    locationProvider=source,navigationControllerConfig=walkingConfig())
                // Ferrostar's HTTP adapter reads the response body synchronously after headers arrive.
                // Keep both body reading and route parsing off Android's UI thread.
                val routes=withContext(Dispatchers.IO){engine.getRoutes(points[0].location().toUserLocation(),points.drop(1).map{Waypoint(GeographicCoordinate(it.lat,it.lng),WaypointKind.BREAK)})}
                ensureActive();if(token!=generation) return@launch
                val route=routes.firstOrNull() ?: error("No walking route was found.")
                geometry=route.geometry;position=null;remaining="Walk about ${route.distance.toInt()} meters (excluding stops)";busy=false
            } catch(e:CancellationException){throw e}
            catch(e:Exception){if(token==generation){pause();message="Unable to preview the walking route. Please try again shortly."}}
        }
    }
    fun start() {
        if(busy || active || progress.arrived || progress.finished) return
        pause();busy=true;message=null
        val token=generation
        request=scope.launch {
            try {
                val source:NavigationLocationProviding=if(simulation) SimulatedLocationProvider(scope=scope,warpFactor=10u,initialLocation=points[progress.leg].location())
                    else AndroidLocationProvider(context)
                val initial=if(simulation) points[progress.leg].location() else withTimeoutOrNull(30000) {
                    source.locationUpdates().first{it.hasAccuracy() && it.accuracy in 0f..50f && kotlin.math.abs(System.currentTimeMillis()-it.time)<30000}
                } ?: throw LocationFixException("A precise location is not available. Enable precise location and retry outdoors, or choose simulated navigation.")
                ensureActive();if(token!=generation) return@launch
                if(initial.accuracy<=20 && initial.distanceTo(target.location())<=10) {
                    position=GeographicCoordinate(initial.latitude,initial.longitude);geometry=emptyList();arrive();return@launch
                }
                val config=walkingConfig()
                val engine=FerrostarCore(wellKnownRouteProvider=WellKnownRouteProvider.Valhalla(endpoint,"pedestrian"),
                    httpClient=httpClient,
                    locationProvider=source,navigationControllerConfig=config)
                core=engine
                speech=AndroidTtsObserver(context).also{it.setMuted(muted);engine.spokenInstructionObserver=it}
                val routes=withContext(Dispatchers.IO){engine.getRoutes(initial.toUserLocation(),listOf(Waypoint(GeographicCoordinate(target.lat,target.lng),WaypointKind.BREAK)))}
                ensureActive();if(token!=generation) return@launch
                val route=routes.firstOrNull() ?: error("No walkable route found, please try again.")
                geometry=route.geometry
                if(source is SimulatedLocationProvider) source.setRoute(route)
                active=true;busy=false
                engine.startNavigation(route)
                updates=scope.launch {
                    engine.state.collect { state ->
                        if(token!=generation) return@collect
                        geometry=state.routeGeometry
                        position=state.tripState.preferredUserLocation()?.coordinates
                        when(val trip=state.tripState) {
                            is TripState.Navigating -> {
                                instruction=if(state.isCalculatingNewRoute) "Deviated from route, re-planning…" else trip.visualInstruction?.primaryContent?.text ?: trip.remainingSteps.firstOrNull()?.roadName ?: "Walk along route"
                                remaining="Remaining ${trip.progress.distanceRemaining.toInt()} meters · Approx ${maxOf(1,kotlin.math.ceil(trip.progress.durationRemaining/60).toInt())} minutes"
                            }
                            is TripState.Complete -> arrive()
                            else -> Unit
                        }
                    }
                }
            } catch(e:CancellationException) { throw e }
            catch(e:Exception) { if(token==generation) {pause();message=when(e) { is SecurityException -> "Allow precise location, or choose simulated navigation."; is LocationFixException -> e.message; else -> "Walking route temporarily unavailable. Please try again later. Arrival progress is preserved." }} }
        }
    }
}

internal fun walkingConfig()=NavigationControllerConfig(WaypointAdvanceMode.WaypointWithinRange(15.0),
    stepAdvanceDistanceEntryAndExit(15u,5u,50u),stepAdvanceDistanceToEndOfStep(15u,50u),
    RouteDeviationTracking.StaticThreshold(50u,40.0),CourseFiltering.RAW)

@OptIn(ExperimentalMaterial3Api::class)
@Composable fun WalkingNavigationDialog(store:BuddyStore,suppliedNavigator:WalkingNavigator?=null,autoStart:Boolean=false,onClose:()->Unit) {
    val day=store.day ?: return
    val context=LocalContext.current
    val uri=LocalUriHandler.current
    val identity=store.trip?.optString("id")+":"+store.trip?.optInt("revision")
    val key=walkingKey(identity,day)
    val nav=suppliedNavigator ?: remember(key){WalkingNavigator(context.applicationContext,day,identity,store.base+"/navigation/route")}
    val lifecycle=LocalLifecycleOwner.current.lifecycle
    var reset by remember{mutableStateOf(false)}
    val permission=rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        if(result[Manifest.permission.ACCESS_FINE_LOCATION]==true) nav.start()
        else store.error="Allow precise location, or choose simulated navigation."
    }
    var autoStarted by remember(key){mutableStateOf(false)}
    LaunchedEffect(autoStart,store.health?.optBoolean("navigationConfigured")) {
        if(autoStart && !autoStarted && store.health?.optBoolean("navigationConfigured")==true){
            autoStarted=true
            if(ContextCompat.checkSelfPermission(context,Manifest.permission.ACCESS_FINE_LOCATION)==PackageManager.PERMISSION_GRANTED)nav.start()
            else permission.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION,Manifest.permission.ACCESS_COARSE_LOCATION))
        }
    }
    DisposableEffect(nav,lifecycle) {
        val observer=LifecycleEventObserver{_,event->if(event==Lifecycle.Event.ON_STOP)nav.pause()}
        lifecycle.addObserver(observer)
        onDispose {lifecycle.removeObserver(observer);nav.close()}
    }
    Dialog(onDismissRequest=onClose,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        val view=LocalView.current
        DisposableEffect(view,nav.active) {
            val window=(view.parent as? DialogWindowProvider)?.window
            if(nav.active) window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            onDispose {window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)}
        }
        Scaffold(topBar={TopAppBar(title={Text("Step-by-step navigation")},actions={TextButton(onClick=onClose){Text(ui("Close"))}})}) { padding ->
            Column(Modifier.padding(padding).fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(14.dp)) {
                Small("Navigation date:"+day.optString("date"))
                Text("Segment ${nav.progress.leg+1} of ${nav.count} · Walking",style=MaterialTheme.typography.titleMedium)
                Heading(if(nav.progress.finished) "Today's itinerary completed" else "Head to ${nav.target.name}")
                Row(Modifier.horizontalScroll(rememberScrollState())) {MapProvider.entries.forEach{p->FilterChip(selected=store.mapProvider==p,onClick={store.chooseMapProvider(p)},label={Text(p.label)})}}
                WalkingMap(nav.points,nav.geometry,nav.position,store.mapProvider)
                Row {Switch(checked=nav.simulation,onCheckedChange=nav::chooseSimulation,enabled=!nav.busy && !nav.active);Text("Simulate navigation (without device location)",Modifier.padding(12.dp))}
                if(!nav.simulation) Small("Your actual location will be sent via the device backend to the configured walking route service for route calculation.")
                if(nav.simulation) Text("Simulated location · 10× speed · real trip progress is unchanged",color=androidx.compose.ui.graphics.Color(0xFFAE6300))
                if(!nav.active) {
                    TextButton(onClick=nav::preview,enabled=!nav.busy && store.health?.optBoolean("navigationConfigured")==true){Text("Preview daily walking route")}
                    if(nav.remaining.isNotBlank()) Small(nav.remaining)
                }
                Small("Walking route. You can stop after arriving at a station, then proceed to the next.")
                Text("Route data © OpenStreetMap contributors",style=MaterialTheme.typography.labelSmall,
                    modifier=Modifier.clickable{uri.openUri("https://www.openstreetmap.org/copyright")})
                if(nav.busy){LinearProgressIndicator(Modifier.fillMaxWidth());Text("Getting location and walking route…")}
                nav.message?.let{Text(it,color=MaterialTheme.colorScheme.error)}
                when {
                    nav.active -> {
                        Text(nav.instruction,style=MaterialTheme.typography.titleMedium);Text(nav.remaining)
                        Row {TextButton(onClick=nav::toggleMute){Text(if(nav.muted) "Enable voice" else "Mute")};TextButton(onClick=nav::pause){Text("Pause navigation")}}
                        OutlinedButton(onClick=nav::arrive){Text("I have arrived")}
                    }
                    nav.progress.finished -> Text("All segments completed",color=Green)
                    nav.progress.arrived -> {
                        Text("Arrived. ${if(nav.target.stay>0) "Suggested stay: ${nav.target.stay} min." else ""} ready to continue?")
                        Action(if(nav.progress.leg+1==nav.count) "Complete today's itinerary" else "Go to next stop",!nav.busy,nav::next)
                    }
                    else -> Action(if(nav.simulation) "Start navigation simulation" else "Start segment navigation",!nav.busy && store.health?.optBoolean("navigationConfigured")==true) {
                        if(nav.simulation || ContextCompat.checkSelfPermission(context,Manifest.permission.ACCESS_FINE_LOCATION)==PackageManager.PERMISSION_GRANTED)nav.start()
                        else permission.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION,Manifest.permission.ACCESS_COARSE_LOCATION))
                    }
                }
                if(store.health?.optBoolean("navigationConfigured")!=true) Small("Walking route service not enabled. Navigation unavailable. Please contact the development team.")
                Heading("Daily route & progress")
                nav.points.forEachIndexed { index,p -> Text("${if(index<=nav.progress.leg || nav.progress.finished || index==nav.progress.leg+1 && nav.progress.arrived) "✓" else "○"} $index. ${p.name}${if(p.stay>0) " · stay ${p.stay} min" else ""}") }
                Small("Keep this page open for navigation. Leaving the app pauses it; resume from your current location when you return. Directions and voice guidance are in English. Walking ETA excludes stops.")
                TextButton(onClick={reset=true}){Text("Reset today's navigation progress")}
            }
        }
    }
    if(reset) AlertDialog(onDismissRequest={reset=false},title={Text("Reset today's navigation progress?")},confirmButton={TextButton(onClick={nav.reset();reset=false}){Text("Reset")}},dismissButton={TextButton(onClick={reset=false}){Text(ui("Cancel"))}})
}
