package com.sydneybuddy.travel

import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import okhttp3.Cache
import okhttp3.OkHttpClient
import org.json.JSONObject
import org.maplibre.android.MapLibre
import org.maplibre.android.annotations.IconFactory
import org.maplibre.android.annotations.MarkerOptions
import org.maplibre.android.annotations.PolylineOptions
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.geometry.LatLngBounds
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.module.http.HttpRequestUtil
import java.io.File

/** App preferences and camera state do not depend on either map SDK. */
enum class MapProvider(val label:String) {
    OSM("OpenStreetMap"), GOOGLE("Google Maps"), SCHEMATIC("离线示意图");
    companion object {
        fun fromStored(value:String?):MapProvider = entries.find {it.name==value} ?: OSM
    }
}
fun mapDisplayDay(day:JSONObject?,provider:MapProvider,googleContent:Boolean):JSONObject? =
    if(googleContent && provider!=MapProvider.GOOGLE) null else day
data class MapViewport(val sceneKey:String,val lat:Double,val lng:Double,val zoom:Double)

internal object OsmNetwork {
    private var ready=false
    @Synchronized fun initialize(context:Context) {
        if(ready) return
        MapLibre.getInstance(context.applicationContext)
        HttpRequestUtil.setOkHttpClient(OkHttpClient.Builder()
            .cache(Cache(File(context.cacheDir,"osm-http"),64L*1024*1024))
            .addInterceptor { chain ->
                chain.proceed(chain.request().newBuilder()
                    .header("User-Agent","SydneyBuddy/1.0 (Android; https://github.com/f1sh11/hackathon)").build())
            }.build())
        ready=true
    }
}

// No glyphs, sprites or cloud styles: only the visible OSM raster tiles are requested.
internal const val OSM_STYLE="""{
 "version":8,"sources":{"osm":{"type":"raster","tiles":["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],"tileSize":256,"maxzoom":19,"attribution":"© OpenStreetMap contributors"}},
 "layers":[{"id":"osm","type":"raster","source":"osm"}]
}"""

@Composable fun OsmRouteMap(day:JSONObject?,modifier:Modifier,fullBleed:Boolean,
    viewport:MapViewport?,onViewport:(MapViewport)->Unit,overview:OverviewRoute?=null,expanded:Boolean=false,sections:List<JourneyMapSection> = emptyList()) {
    val context=LocalContext.current
    val lifecycle=LocalLifecycleOwner.current.lifecycle
    val currentViewport by rememberUpdatedState(viewport)
    val currentOnViewport by rememberUpdatedState(onViewport)
    val currentDay by rememberUpdatedState(day)
    var error by remember { mutableStateOf(false) }
    var rendered by remember {mutableStateOf(false)}
    var map by remember {mutableStateOf<MapLibreMap?>(null)}
    var sceneKey by remember {mutableStateOf<String?>(null)}
    var applyingCamera by remember {mutableStateOf(false)}
    val view=remember {
        OsmNetwork.initialize(context)
        MapView(context).apply {
            onCreate(null)
            addOnDidFailLoadingMapListener { error=true }
            addOnDidFinishRenderingMapListener { fully -> if(fully && sceneKey!=null) rendered=true }
            getMapAsync { ready ->
                ready.uiSettings.isLogoEnabled=false
                ready.uiSettings.isAttributionEnabled=false // Always-visible attribution is supplied below.
                ready.uiSettings.isCompassEnabled=true
                ready.uiSettings.isRotateGesturesEnabled=false
                ready.setPrefetchZoomDelta(0)
                ready.addOnCameraIdleListener {
                    if(sceneKey==null || applyingCamera) return@addOnCameraIdleListener
                    val camera=ready.cameraPosition
                    camera.target?.let { target -> currentOnViewport(MapViewport(currentDay.toString(),target.latitude,target.longitude,camera.zoom)) }
                }
                ready.setStyle(Style.Builder().fromJson(OSM_STYLE)) { map=ready }
            }
        }
    }
    LaunchedEffect(view) {
        kotlinx.coroutines.delay(20000)
        if(!rendered) error=true
    }
    DisposableEffect(view,lifecycle) {
        val observer=LifecycleEventObserver { _,event -> when(event) {
            Lifecycle.Event.ON_RESUME -> view.onResume()
            Lifecycle.Event.ON_PAUSE -> view.onPause()
            Lifecycle.Event.ON_START -> view.onStart()
            Lifecycle.Event.ON_STOP -> view.onStop()
            else -> Unit
        }}
        view.onStart(); view.onResume(); lifecycle.addObserver(observer)
        onDispose { lifecycle.removeObserver(observer); view.onPause();view.onStop();view.onDestroy() }
    }
    LaunchedEffect(map,day.toString(),overview,sections) {
        val ready=map ?: return@LaunchedEffect
        val key=day.toString()+"|"+(overview?.points?.size ?: 0)
        if(sceneKey==key) return@LaunchedEffect
        applyingCamera=true
        if(sceneKey?.substringBeforeLast("|")!=day.toString()) rendered=false
        sceneKey=key
        ready.clear()
        val start=day?.optJSONObject("start") ?: Endpoint.quay.json()
        val end=day?.optJSONObject("end") ?: Endpoint.central.json()
        val stops=day?.array("stops")?.objects() ?: emptyList()
        val entries=listOf(start)+stops+listOf(end)
        val points=entries.map {LatLng(it.getDouble("lat"),it.getDouble("lng"))}
        if(day!=null) {
            val coordinates=points.map{it.latitude to it.longitude}
            val badges=itineraryBadges(coordinates)
            val icons=IconFactory.getInstance(context)
            val geometry=overview?.points?.map{LatLng(it.first,it.second)} ?: points
            ready.style?.let{style->
                style.layers.filter{it.id.startsWith("journey-")}.forEach{style.removeLayer(it.id)}
                style.sources.filter{it.id.startsWith("journey-")}.forEach{style.removeSource(it.id)}
                sections.filter{it.points.size>1}.forEachIndexed{i,section->
                    val id="journey-$i"
                    val shape=org.maplibre.geojson.LineString.fromLngLats(section.points.map{org.maplibre.geojson.Point.fromLngLat(it.second,it.first)})
                    style.addSource(org.maplibre.android.style.sources.GeoJsonSource(id,org.maplibre.geojson.Feature.fromGeometry(shape)))
                    style.addLayer(org.maplibre.android.style.layers.LineLayer("$id-border",id).withProperties(org.maplibre.android.style.layers.PropertyFactory.lineColor(android.graphics.Color.WHITE),org.maplibre.android.style.layers.PropertyFactory.lineWidth(9f)))
                    val color=when(section.mode){"walk"->android.graphics.Color.rgb(25,118,210);"drive"->android.graphics.Color.rgb(13,130,94);else->android.graphics.Color.rgb(198,106,0)}
                    val stroke=org.maplibre.android.style.layers.LineLayer("$id-stroke",id).withProperties(org.maplibre.android.style.layers.PropertyFactory.lineColor(color),org.maplibre.android.style.layers.PropertyFactory.lineWidth(5f),org.maplibre.android.style.layers.PropertyFactory.lineCap("round"))
                    if(section.mode=="walk")stroke.setProperties(org.maplibre.android.style.layers.PropertyFactory.lineDasharray(arrayOf(0.2f,1.6f)))
                    style.addLayer(stroke)
                }
            }
            if(sections.isEmpty() && geometry.size>1){
                ready.addPolyline(PolylineOptions().addAll(geometry).color(android.graphics.Color.WHITE).width(9f))
                ready.addPolyline(PolylineOptions().addAll(geometry).color(android.graphics.Color.rgb(13,130,94)).width(5f))
            }
            val arrowPoints=overview?.points ?: coordinates
            (if(sections.isEmpty())arrowPoints else emptyList()).zipWithNext().forEachIndexed {i,(a,b)->
                if(arrowPoints.size>30 && i % (arrowPoints.size/8).coerceAtLeast(1)!=0)return@forEachIndexed
                if(kotlin.math.abs(a.first-b.first)+kotlin.math.abs(a.second-b.second)<0.00001)return@forEachIndexed
                val position=if(arrowPoints.size>30)a else (a.first+b.first)/2 to (a.second+b.second)/2
                ready.addMarker(MarkerOptions().position(LatLng(position.first,position.second)).title("行程方向")
                    .icon(icons.fromBitmap(itineraryBadge(itineraryDirection(a,b),true))))
            }
            itineraryPinGroups(coordinates).forEach {(index,group)->
                val badge=group.joinToString("/"){badges[it]}
                val title=group.joinToString(" → "){i->"${badges[i]} · ${entries[i].optString("name")}"}
                ready.addMarker(MarkerOptions().position(points[index]).title(title).icon(icons.fromBitmap(itineraryBadge(badge))))
            }
        }
        val saved=currentViewport?.takeIf {it.sceneKey==day.toString() && overview==null}
        view.post {
            ready.moveCamera(if(saved!=null) CameraUpdateFactory.newLatLngZoom(LatLng(saved.lat,saved.lng),saved.zoom)
                else CameraUpdateFactory.newLatLngBounds(LatLngBounds.Builder().includes(points+(overview?.points?.map{LatLng(it.first,it.second)} ?: emptyList())).build(),70,if(expanded)100 else if(fullBleed)280 else 30,70,if(expanded)180 else if(fullBleed)400 else 30))
            applyingCamera=false
            ready.cameraPosition.target?.let {target -> currentOnViewport(MapViewport(day.toString(),target.latitude,target.longitude,ready.cameraPosition.zoom))}
        }
    }
    Box(if(fullBleed) modifier else modifier.fillMaxWidth().height(310.dp)) {
        AndroidView(factory={view},modifier=Modifier.fillMaxSize())
        val uri=LocalUriHandler.current
        Column(Modifier.align(Alignment.CenterEnd).background(Color.White.copy(alpha=.94f)).padding(6.dp)) {
            Text("© OpenStreetMap contributors",fontSize=10.sp,color=Ink,
                modifier=Modifier.clickable {uri.openUri("https://www.openstreetmap.org/copyright")})
            Text(if(rendered) if(sections.isNotEmpty()) "地图已加载 · 分段交通路线" else if(overview==null) "地图已加载 · 顺序示意，非步行路径" else "地图已加载 · 沿道路步行路线" else if(error) "地图暂未加载，可选离线示意图" else "地图加载中… · 连线非导航",fontSize=10.sp,color=Muted)
        }
    }
}
