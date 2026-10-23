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
    return listOf(point(day.getJSONObject("start")))+day.array("stops").objects().map{point(it,it.optInt("visitMinutes"))}+point(day.getJSONObject("end"),suffix="（返回）")
}
fun walkingKey(tripIdentity:String,day:JSONObject)="walking:"+MessageDigest.getInstance("SHA-256").digest((tripIdentity+":"+day.optString("date")+":"+walkingPoints(day).joinToString("|"){"${it.name}:${it.lat}:${it.lng}:${it.stay}"}).toByteArray()).joinToString(""){"%02x".format(it)}

private class LocationFixException(message:String):Exception(message)

/** Itinerary stop progress is independent of SDK state and map renderer. */
class WalkingNavigator(private val context:Context,day:JSONObject,tripIdentity:String,private val endpoint:String, private val httpClient:HttpClientProvider = OkHttpClient.Builder().callTimeout(25,TimeUnit.SECONDS).followRedirects(false).build().toOkHttpClientProvider()) {
    val points=walkingPoints(day)
    val key=walkingKey(tripIdentity,day)
    private val prefs=context.getSharedPreferences("walking",0)
    var progress by mutableStateOf(WalkingProgress.restore(prefs.getString(key,null),points.size-1));private set
    private var realProgress=progress
    var simulation by mutableStateOf(false);private set
    var busy by mutableStateOf(false);private set
    var active by mutableStateOf(false);private set
    var message by mutableStateOf<String?>(null);private set
    var instruction by mutableStateOf("准备出发");private set
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
    fun arrive() { pause();progress=progress.arrive();instruction="已到达 ${target.name}";remaining="";save() }
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
                val route=routes.firstOrNull() ?: error("找不到步行路线")
                geometry=route.geometry;position=null;remaining="全天步行约 ${route.distance.toInt()} 米（不含停留）";busy=false
            } catch(e:CancellationException){throw e}
            catch(e:Exception){if(token==generation){pause();message="暂时无法预览步行路线，请稍后重试。"}}
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
                } ?: throw LocationFixException("尚未取得准确位置，请开启精确定位并到开阔处重试，或选择模拟导航。")
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
                val route=routes.firstOrNull() ?: error("找不到可步行的路线，请重试。")
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
                                instruction=if(state.isCalculatingNewRoute) "偏离路线，正在重新规划…" else trip.visualInstruction?.primaryContent?.text ?: trip.remainingSteps.firstOrNull()?.roadName ?: "沿路线步行"
                                remaining="剩余 ${trip.progress.distanceRemaining.toInt()} 米 · 约 ${maxOf(1,kotlin.math.ceil(trip.progress.durationRemaining/60).toInt())} 分钟"
                            }
                            is TripState.Complete -> arrive()
                            else -> Unit
                        }
                    }
                }
            } catch(e:CancellationException) { throw e }
            catch(e:Exception) { if(token==generation) {pause();message=when(e) { is SecurityException -> "定位权限未开启，请允许精确定位或选择模拟导航。"; is LocationFixException -> e.message; else -> "步行路线暂不可用，请稍后重试。到站进度已保留。" }} }
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
        else store.error="请允许精确定位，或选择模拟导航。"
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
        Scaffold(topBar={TopAppBar(title={Text("逐段导航")},actions={TextButton(onClick=onClose){Text("关闭")}})}) { padding ->
            Column(Modifier.padding(padding).fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),verticalArrangement=Arrangement.spacedBy(14.dp)) {
                Small("導航日期："+day.optString("date"))
                Text("第 ${nav.progress.leg+1} / ${nav.count} 段 · 步行",style=MaterialTheme.typography.titleMedium)
                Heading(if(nav.progress.finished) "今天的行程已完成" else "前往 ${nav.target.name}")
                Row(Modifier.horizontalScroll(rememberScrollState())) {MapProvider.entries.forEach{p->FilterChip(selected=store.mapProvider==p,onClick={store.chooseMapProvider(p)},label={Text(p.label)})}}
                WalkingMap(nav.points,nav.geometry,nav.position,store.mapProvider)
                Row {Switch(checked=nav.simulation,onCheckedChange=nav::chooseSimulation,enabled=!nav.busy && !nav.active);Text("模拟导航（不使用设备定位）",Modifier.padding(12.dp))}
                if(!nav.simulation) Small("实际位置会经本机后端发送至已配置的步行路线服务，用于计算路线。")
                if(nav.simulation) Text("模拟位置 · 10 倍速度 · 不记录实际行程进度",color=androidx.compose.ui.graphics.Color(0xFFAE6300))
                if(!nav.active) {
                    TextButton(onClick=nav::preview,enabled=!nav.busy && store.health?.optBoolean("navigationConfigured")==true){Text("预览整天步行路线")}
                    if(nav.remaining.isNotBlank()) Small(nav.remaining)
                }
                Small("步行路线 · 到站后可停留，再前往下一站")
                Text("路线数据 © OpenStreetMap contributors",style=MaterialTheme.typography.labelSmall,
                    modifier=Modifier.clickable{uri.openUri("https://www.openstreetmap.org/copyright")})
                if(nav.busy){LinearProgressIndicator(Modifier.fillMaxWidth());Text("正在获取位置与步行路线…")}
                nav.message?.let{Text(it,color=MaterialTheme.colorScheme.error)}
                when {
                    nav.active -> {
                        Text(nav.instruction,style=MaterialTheme.typography.titleMedium);Text(nav.remaining)
                        Row {TextButton(onClick=nav::toggleMute){Text(if(nav.muted) "开启语音" else "静音")};TextButton(onClick=nav::pause){Text("暂停导航")}}
                        OutlinedButton(onClick=nav::arrive){Text("我已到达")}
                    }
                    nav.progress.finished -> Text("全部路段已完成",color=Green)
                    nav.progress.arrived -> {
                        Text("已到达。${if(nav.target.stay>0) "建议停留 ${nav.target.stay} 分钟。" else ""}准备好再继续。")
                        Action(if(nav.progress.leg+1==nav.count) "完成今天行程" else "前往下一站",!nav.busy,nav::next)
                    }
                    else -> Action(if(nav.simulation) "开始模拟导航" else "开始本段导航",!nav.busy && store.health?.optBoolean("navigationConfigured")==true) {
                        if(nav.simulation || ContextCompat.checkSelfPermission(context,Manifest.permission.ACCESS_FINE_LOCATION)==PackageManager.PERMISSION_GRANTED)nav.start()
                        else permission.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION,Manifest.permission.ACCESS_COARSE_LOCATION))
                    }
                }
                if(store.health?.optBoolean("navigationConfigured")!=true) Small("步行路线服务尚未开启，暂时无法导航。请联系开发团队。")
                Heading("整天路线与完成进度")
                nav.points.forEachIndexed { index,p -> Text("${if(index<=nav.progress.leg || nav.progress.finished || index==nav.progress.leg+1 && nav.progress.arrived) "✓" else "○"} $index. ${p.name}${if(p.stay>0) " · 停留 ${p.stay} 分钟" else ""}") }
                Small("保持本页开启进行导航；离开 App 会暂停，回来后可从当前位置继续。路线与语音为英文，停留时间不计入步行 ETA。")
                TextButton(onClick={reset=true}){Text("重置今天导航进度")}
            }
        }
    }
    if(reset) AlertDialog(onDismissRequest={reset=false},title={Text("重置今天的导航进度？")},confirmButton={TextButton(onClick={nav.reset();reset=false}){Text("重置")}},dismissButton={TextButton(onClick={reset=false}){Text("取消")}})
}
