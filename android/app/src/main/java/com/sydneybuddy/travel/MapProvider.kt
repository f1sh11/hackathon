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
import androidx.compose.ui.layout.onSizeChanged
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import okhttp3.Cache
import okhttp3.OkHttpClient
import org.json.JSONObject
import org.maplibre.android.MapLibre
import org.maplibre.android.annotations.IconFactory
import org.maplibre.android.annotations.MarkerOptions
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

private fun walkingDotFeatures(map:MapLibreMap,points:List<Pair<Double,Double>>,dotStyle:WalkingDotStyle):org.maplibre.geojson.FeatureCollection {
    val latitude=map.cameraPosition.target?.latitude ?: points.firstOrNull()?.first ?: 0.0
    val spacing=(map.projection.getMetersPerPixelAtLatitude(latitude)*dotStyle.spacing).coerceIn(1.0,500.0)
    return org.maplibre.geojson.FeatureCollection.fromFeatures(walkingRouteDots(points,spacing).map {
        org.maplibre.geojson.Feature.fromGeometry(org.maplibre.geojson.Point.fromLngLat(it.second,it.first))
    })
}
private fun updateWalkingDots(map:MapLibreMap,sections:List<JourneyMapSection>,dotStyle:WalkingDotStyle) {
    val style=map.style ?: return
    sections.forEachIndexed {i,section->if(section.mode=="walk" && section.points.size>1) {
        style.getSourceAs<org.maplibre.android.style.sources.GeoJsonSource>("journey-$i")?.setGeoJson(walkingDotFeatures(map,section.points,dotStyle))
        style.getLayerAs<org.maplibre.android.style.layers.CircleLayer>("journey-$i-dots")?.setProperties(
            org.maplibre.android.style.layers.PropertyFactory.circleRadius(dotStyle.radius),
            org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(dotStyle.halo))
    }}
}

// No glyphs, sprites or cloud styles: only the visible OSM raster tiles are requested.
internal const val OSM_STYLE="""{
 "version":8,"sources":{"osm":{"type":"raster","tiles":["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],"tileSize":256,"maxzoom":19,"attribution":"© OpenStreetMap contributors"}},
 "layers":[{"id":"osm","type":"raster","source":"osm"}]
}"""

@Composable fun OsmRouteMap(day:JSONObject?,modifier:Modifier,fullBleed:Boolean,
    viewport:MapViewport?,onViewport:(MapViewport)->Unit,overview:OverviewRoute?=null,expanded:Boolean=false,sections:List<JourneyMapSection> = emptyList()) {
    val context=LocalContext.current
    val configuration=androidx.compose.ui.platform.LocalConfiguration.current
    val density=androidx.compose.ui.platform.LocalDensity.current.density
    var mapShortSide by remember {mutableStateOf(minOf(configuration.screenWidthDp,configuration.screenHeightDp).toFloat())}
    val dotStyle=walkingDotStyle(mapShortSide)
    val currentDotStyle by rememberUpdatedState(dotStyle)
    val lifecycle=LocalLifecycleOwner.current.lifecycle
    val currentViewport by rememberUpdatedState(viewport)
    val currentOnViewport by rememberUpdatedState(onViewport)
    val currentDay by rememberUpdatedState(day)
    var error by remember { mutableStateOf(false) }
    var rendered by remember {mutableStateOf(false)}
    var map by remember {mutableStateOf<MapLibreMap?>(null)}
    var sceneKey by remember {mutableStateOf<OsmRouteScene?>(null)}
    val paths=sections.ifEmpty{overview?.let{listOf(JourneyMapSection("walk",it.points))} ?: emptyList()}
    val currentPaths by rememberUpdatedState(paths)
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
                    if(sceneKey==null) return@addOnCameraIdleListener
                    updateWalkingDots(ready,currentPaths,currentDotStyle)
                    if(applyingCamera) return@addOnCameraIdleListener
                    val camera=ready.cameraPosition
                    camera.target?.let { target -> currentOnViewport(MapViewport(currentDay.toString(),target.latitude,target.longitude,camera.zoom)) }
                }
                ready.setStyle(Style.Builder().fromJson(OSM_STYLE)) { map=ready }
            }
        }
    }
    LaunchedEffect(map,dotStyle) {map?.let{updateWalkingDots(it,currentPaths,dotStyle)}}
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
        val key=OsmRouteScene(day.toString(),overview,sections)
        if(sceneKey==key) return@LaunchedEffect
        applyingCamera=true
        if(sceneKey?.day!=day.toString()) rendered=false
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
            ready.style?.let{style->
                style.layers.filter{it.id.startsWith("journey-")}.forEach{style.removeLayer(it.id)}
                style.sources.filter{it.id.startsWith("journey-")}.forEach{style.removeSource(it.id)}
                fun addRouteLayer(layer:org.maplibre.android.style.layers.Layer) {
                    if(style.getLayer("org.maplibre.annotations.points")!=null)style.addLayerBelow(layer,"org.maplibre.annotations.points")else style.addLayer(layer)
                }
                paths.forEachIndexed{i,section->
                    if(section.points.size<2)return@forEachIndexed
                    val id="journey-$i"
                    if(section.mode=="walk") {
                        // Small circles remain visible at every zoom without subpixel dash textures.
                        style.addSource(org.maplibre.android.style.sources.GeoJsonSource(id,walkingDotFeatures(ready,section.points,dotStyle)))
                        addRouteLayer(org.maplibre.android.style.layers.CircleLayer("$id-dots",id).withProperties(
                            org.maplibre.android.style.layers.PropertyFactory.circleColor(android.graphics.Color.rgb(25,118,210)),
                            org.maplibre.android.style.layers.PropertyFactory.circleRadius(dotStyle.radius),
                            org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor(android.graphics.Color.WHITE),
                            org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(dotStyle.halo)))
                    } else {
                        val shape=org.maplibre.geojson.LineString.fromLngLats(section.points.map{org.maplibre.geojson.Point.fromLngLat(it.second,it.first)})
                        style.addSource(org.maplibre.android.style.sources.GeoJsonSource(id,org.maplibre.geojson.Feature.fromGeometry(shape)))
                        addRouteLayer(org.maplibre.android.style.layers.LineLayer("$id-border",id).withProperties(org.maplibre.android.style.layers.PropertyFactory.lineColor(android.graphics.Color.WHITE),org.maplibre.android.style.layers.PropertyFactory.lineWidth(5f)))
                        val color=if(section.mode=="drive")android.graphics.Color.rgb(13,130,94)else android.graphics.Color.rgb(25,118,210)
                        addRouteLayer(org.maplibre.android.style.layers.LineLayer("$id-stroke",id).withProperties(org.maplibre.android.style.layers.PropertyFactory.lineColor(color),org.maplibre.android.style.layers.PropertyFactory.lineWidth(3f),org.maplibre.android.style.layers.PropertyFactory.lineCap("round")))
                    }
                }
            }
            val arrowPoints=overview?.points ?: emptyList()
            (if(sections.isEmpty())arrowPoints else emptyList()).zipWithNext().forEachIndexed {i,(a,b)->
                if(arrowPoints.size>30 && i % (arrowPoints.size/8).coerceAtLeast(1)!=0)return@forEachIndexed
                if(kotlin.math.abs(a.first-b.first)+kotlin.math.abs(a.second-b.second)<0.00001)return@forEachIndexed
                val position=if(arrowPoints.size>30)a else (a.first+b.first)/2 to (a.second+b.second)/2
                ready.addMarker(MarkerOptions().position(LatLng(position.first,position.second)).title("行程方向")
                    .icon(icons.fromBitmap(itineraryBadge(itineraryDirection(a,b),true))))
            }
            sections.forEach {section->section.stops.forEach {(point,title)->
                ready.addMarker(MarkerOptions().position(LatLng(point.first,point.second)).title(title).snippet(section.line ?: transportLabel(section.mode)).icon(icons.fromBitmap(itineraryBadge("●"))))
            }}
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
            updateWalkingDots(ready,paths,dotStyle)
            applyingCamera=false
            ready.cameraPosition.target?.let {target -> currentOnViewport(MapViewport(day.toString(),target.latitude,target.longitude,ready.cameraPosition.zoom))}
        }
    }
    Box(if(fullBleed) modifier else modifier.fillMaxWidth().height(310.dp)) {
        AndroidView(factory={view},modifier=Modifier.fillMaxSize().onSizeChanged {size->if(size.width>0 && size.height>0)mapShortSide=minOf(size.width,size.height)/density})
        val uri=LocalUriHandler.current
        Column(Modifier.align(Alignment.CenterEnd).background(Color.White.copy(alpha=.94f)).padding(6.dp)) {
            Text("© OpenStreetMap contributors",fontSize=10.sp,color=Ink,
                modifier=Modifier.clickable {uri.openUri("https://www.openstreetmap.org/copyright")})
            Text(if(rendered) if(sections.isNotEmpty()) "地图已加载 · 分段交通路线" else if(overview==null) "地图已加载 · 仅显示站点，尚未查询道路路线" else "地图已加载 · 沿道路步行路线" else if(error) "地图暂未加载，可选离线示意图" else "地图加载中…",fontSize=10.sp,color=Muted)
        }
    }
}
