package com.sydneybuddy.travel

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.unit.dp
import com.google.android.gms.maps.CameraUpdateFactory
import com.google.android.gms.maps.model.LatLng
import com.google.android.gms.maps.model.LatLngBounds
import com.google.android.gms.maps.model.BitmapDescriptorFactory
import com.google.maps.android.compose.*
import org.json.JSONObject

@Composable fun RouteMap(day:JSONObject?,modifier:Modifier=Modifier,fullBleed:Boolean=false,
    provider:MapProvider=MapProvider.OSM,googleContent:Boolean=false,
    viewport:MapViewport?=null,onViewport:(MapViewport)->Unit={},overview:OverviewRoute?=null,expanded:Boolean=false,sections:List<JourneyMapSection> = emptyList(),insightMarkers:List<JSONObject> = emptyList()) {
    // Provider-owned live coordinates and paths must not be rendered on another provider's map.
    val visibleDay=mapDisplayDay(day,provider,googleContent)
    if(provider==MapProvider.OSM) {
        OsmRouteMap(visibleDay,modifier,fullBleed,viewport,onViewport,if(googleContent)null else overview,expanded,sections,insightMarkers)
        return
    }
    if(provider==MapProvider.GOOGLE && !BuildConfig.MAPS_CONFIGURED) {
        Box(modifier.background(Mint),contentAlignment=androidx.compose.ui.Alignment.Center) {
            Text("Google 地图未配置密钥\n请在“我的”切换到 OpenStreetMap",modifier=Modifier.padding(30.dp))
        }
        return
    }
    ProviderRouteMap(visibleDay,modifier,fullBleed,provider==MapProvider.GOOGLE,viewport,onViewport,if(googleContent)null else overview,expanded,sections,insightMarkers)
}

@Composable private fun ProviderRouteMap(day:JSONObject?,modifier:Modifier,fullBleed:Boolean,
    google:Boolean,viewport:MapViewport?,onViewport:(MapViewport)->Unit,overview:OverviewRoute?,expanded:Boolean,sections:List<JourneyMapSection>,insightMarkers:List<JSONObject>) {
    val config=androidx.compose.ui.platform.LocalConfiguration.current
    val dotStyle=walkingDotStyle(minOf(config.screenWidthDp,config.screenHeightDp).toFloat())
    val density=androidx.compose.ui.platform.LocalDensity.current.density
    val dotWidth=dotStyle.radius*2*density
    val dotGap=(dotStyle.spacing.toFloat()-dotStyle.radius*2)*density
    val stops=day?.array("stops")?.objects() ?: emptyList()
    val start=day?.getJSONObject("start") ?: Endpoint.quay.json();val end=day?.getJSONObject("end") ?: Endpoint.central.json()
    fun coordinate(j:JSONObject)=LatLng(j.getDouble("lat"),j.getDouble("lng"))
    val points=listOf(coordinate(start))+stops.map(::coordinate)+coordinate(end)
    val routes=day?.array("legs")?.objects()?.map {runCatching {decodePolyline(it.optString("polyline")).map{p->LatLng(p.first,p.second)}}.getOrDefault(emptyList())} ?: emptyList()
    val bounds=LatLngBounds.builder().apply{(points+(overview?.points?.map{LatLng(it.first,it.second)} ?: routes.flatten())).forEach{include(it)}}.build()
    if(google) {
        val camera=rememberCameraPositionState()
        var loaded by remember {mutableStateOf(false)}
        LaunchedEffect(day.toString(),loaded,overview){if(loaded) {
            val saved=viewport?.takeIf{it.sceneKey==day.toString() && overview==null}
            camera.move(if(saved!=null) CameraUpdateFactory.newLatLngZoom(LatLng(saved.lat,saved.lng),saved.zoom.toFloat()) else CameraUpdateFactory.newLatLngBounds(bounds,70))
        }}
        LaunchedEffect(camera,loaded) {
            snapshotFlow { camera.isMoving }.collect { moving ->
                if(loaded && !moving) onViewport(MapViewport(day.toString(),camera.position.target.latitude,camera.position.target.longitude,camera.position.zoom.toDouble()))
            }
        }
        GoogleMap(modifier=if(fullBleed)modifier else modifier.fillMaxWidth().height(310.dp).clip(RoundedCornerShape(22.dp)),cameraPositionState=camera,
            contentPadding=if(fullBleed)PaddingValues(top=if(expanded)100.dp else 280.dp,bottom=if(expanded)180.dp else 400.dp,start=12.dp,end=12.dp) else PaddingValues(0.dp),
            onMapLoaded={loaded=true},uiSettings=MapUiSettings(rotationGesturesEnabled=false,zoomControlsEnabled=false,myLocationButtonEnabled=false,mapToolbarEnabled=false)) {
            insightMarkers.forEach{item->key(item.optString("id")){Marker(state=MarkerState(position=LatLng(item.getDouble("lat"),item.getDouble("lng"))),title=(if(item.optString("kind")=="price")"💰 "else "⚠️ ")+item.optString("title"),snippet=item.optString("summary"),icon=BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_ORANGE))}}
            if(day!=null)key(day.toString(),overview) {
                val entries=listOf(start)+stops+listOf(end)
                val badges=itineraryBadges(points.map{it.latitude to it.longitude})
                itineraryPinGroups(points.map{it.latitude to it.longitude}).forEach {(i,group)->
                    val marker=remember {MarkerState(position=points[i])}
                    val badge=group.joinToString("/"){badges[it]}
                    val icon=remember {BitmapDescriptorFactory.fromBitmap(itineraryBadge(badge))}
                    Marker(state=marker,title=group.joinToString(" → "){j->"${badges[j]} · ${entries[j].optString("name")}"},snippet=entries[i].optString("arrival"),icon=icon,zIndex=2f,anchor=Offset(.5f,.5f))
                }
                sections.forEach {section->section.stops.forEach {(point,title)->key(point,title){
                    val marker=remember{MarkerState(position=LatLng(point.first,point.second))}
                    val icon=remember{BitmapDescriptorFactory.fromBitmap(itineraryBadge("●"))}
                    Marker(state=marker,title=title,snippet=section.line ?: transportLabel(section.mode),icon=icon,zIndex=3f,anchor=Offset(.5f,.5f))
                }}}
                val actual=overview!=null || (routes.size==points.size-1 && routes.all{it.size>1})
                val geometry=overview?.points?.map{LatLng(it.first,it.second)} ?: if(actual)routes.flatten() else emptyList()
                if(sections.isEmpty() && geometry.size>1){
                    Polyline(points=geometry,color=transportColor("walk"),width=dotWidth,pattern=listOf(com.google.android.gms.maps.model.Dot(),com.google.android.gms.maps.model.Gap(dotGap)))
                } else sections.filter{it.points.size>1}.forEach{section->
                    val path=section.points.map{LatLng(it.first,it.second)}
                    if(section.mode!="walk")Polyline(points=path,color=Color.White,width=10f)
                    Polyline(points=path,color=transportColor(section.mode),width=if(section.mode=="walk")dotWidth else 6f,pattern=if(section.mode=="walk")listOf(com.google.android.gms.maps.model.Dot(),com.google.android.gms.maps.model.Gap(dotGap))else null)
                }
                (if(sections.isEmpty())geometry else emptyList()).zipWithNext().forEachIndexed {i,(a,b)->
                    if(geometry.size>30 && i % (geometry.size/8).coerceAtLeast(1)!=0)return@forEachIndexed
                    if(a==b)return@forEachIndexed
                    val marker=remember {MarkerState(position=if(geometry.size>30)a else LatLng((a.latitude+b.latitude)/2,(a.longitude+b.longitude)/2))}
                    val icon=remember {BitmapDescriptorFactory.fromBitmap(itineraryBadge(itineraryDirection(a.latitude to a.longitude,b.latitude to b.longitude),true))}
                    Marker(state=marker,icon=icon,zIndex=1f,anchor=Offset(.5f,.5f))
                }
            }
        }
        if(!fullBleed)Small(if(routes.any{it.isNotEmpty()}) "步行路线；来源见规划记录" else "仅显示站点；查询后显示沿道路路线")
    } else {
        Column((if(fullBleed)modifier else modifier.fillMaxWidth().clip(RoundedCornerShape(22.dp))).background(Mint).padding(16.dp)) {
            Text("SYDNEY · 路线示意图")
            Canvas(if(fullBleed)Modifier.fillMaxWidth().weight(1f) else Modifier.fillMaxWidth().height(200.dp)) {
                val water=androidx.compose.ui.graphics.Path().apply{
                    moveTo(size.width*.63f,0f);cubicTo(size.width*.30f,size.height*.25f,size.width*.9f,size.height*.28f,size.width,size.height*.62f);lineTo(size.width,0f);close()
                }
                drawPath(water,Color(0xFFA6D6E6))
                repeat(11){i->val x=i*size.width/8-80;drawLine(Color.White.copy(alpha=.6f),Offset(x,0f),Offset(x+size.width*.5f,size.height),11f)}
                repeat(10){i->val y=i*size.height/8;drawLine(Color.White.copy(alpha=.6f),Offset(0f,y),Offset(size.width,y-70),8f)}
                val minLat=points.minOf{it.latitude};val maxLat=points.maxOf{it.latitude}
                val minLng=points.minOf{it.longitude};val maxLng=points.maxOf{it.longitude}
                fun pos(p:LatLng)=Offset(20f+((p.longitude-minLng)/(maxLng-minLng).coerceAtLeast(.005)).toFloat()*(size.width-40),
                    20f+((maxLat-p.latitude)/(maxLat-minLat).coerceAtLeast(.005)).toFloat()*(size.height-40))
                points.zipWithNext().forEach{(a,b)->drawLine(Green,pos(a),pos(b),4f,pathEffect=PathEffect.dashPathEffect(floatArrayOf(12f,10f)))}
                points.forEach{drawCircle(Color.White,14f,pos(it));drawCircle(Green,9f,pos(it))}
            }
            Small("离线示意图 · 不加载网络图砖。连线不是可导航路线。")
        }
    }
}
