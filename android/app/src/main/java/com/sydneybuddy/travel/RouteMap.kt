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
import com.google.maps.android.compose.*
import org.json.JSONObject

@Composable fun RouteMap(day:JSONObject?) {
    if(day==null) return
    val stops=day.array("stops").objects()
    val start=day.getJSONObject("start");val end=day.getJSONObject("end")
    fun coordinate(j:JSONObject)=LatLng(j.getDouble("lat"),j.getDouble("lng"))
    val points=listOf(coordinate(start))+stops.map(::coordinate)+coordinate(end)
    val routes=day.array("legs").objects().map {runCatching {decodePolyline(it.optString("polyline")).map{p->LatLng(p.first,p.second)}}.getOrDefault(emptyList())}
    val bounds=LatLngBounds.builder().apply{points.forEach{include(it)}}.build()
    if(BuildConfig.MAPS_CONFIGURED) {
        val camera=rememberCameraPositionState()
        var loaded by remember {mutableStateOf(false)}
        LaunchedEffect(day.toString(),loaded){if(loaded) camera.move(CameraUpdateFactory.newLatLngBounds(bounds,70))}
        GoogleMap(modifier=Modifier.fillMaxWidth().height(310.dp).clip(RoundedCornerShape(22.dp)),cameraPositionState=camera,
            onMapLoaded={loaded=true},uiSettings=MapUiSettings(zoomControlsEnabled=true,myLocationButtonEnabled=false)) {
            stops.forEachIndexed {i,s->
                key(day.toString(),s.optString("id")) {
                    val marker=remember {MarkerState(position=coordinate(s))}
                    Marker(state=marker,title="${i+1}. ${s.optString("name")}",snippet="${s.optString("arrival")}–${s.optString("departure")}")
                }
            }
            key(day.toString()) {
                val marker=remember {MarkerState(position=coordinate(start))}
                Marker(state=marker,title="${start.optString("name")} · 起点")
                val endMarker=remember {MarkerState(position=coordinate(end))}
                Marker(state=endMarker,title="${end.optString("name")} · 返回点")
            }
            routes.filter{it.isNotEmpty()}.forEach{Polyline(points=it,color=Green,width=10f)}
            if(routes.all{it.isEmpty()}) Polyline(points=points,color=Color.Gray,width=4f)
        }
        Small(if(routes.any{it.isNotEmpty()}) "步行路线；来源见规划记录" else "灰色连线为示意，不是导航路线")
    } else {
        Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(22.dp)).background(Mint).padding(16.dp)) {
            Text("SYDNEY · 路线示意图")
            Canvas(Modifier.fillMaxWidth().height(200.dp)) {
                val minLat=points.minOf{it.latitude};val maxLat=points.maxOf{it.latitude}
                val minLng=points.minOf{it.longitude};val maxLng=points.maxOf{it.longitude}
                fun pos(p:LatLng)=Offset(20f+((p.longitude-minLng)/(maxLng-minLng).coerceAtLeast(.005)).toFloat()*(size.width-40),
                    20f+((maxLat-p.latitude)/(maxLat-minLat).coerceAtLeast(.005)).toFloat()*(size.height-40))
                points.zipWithNext().forEach{(a,b)->drawLine(Green,pos(a),pos(b),4f,pathEffect=PathEffect.dashPathEffect(floatArrayOf(12f,10f)))}
                points.forEach{drawCircle(Color.White,14f,pos(it));drawCircle(Green,9f,pos(it))}
            }
            Small("未配置 Android 地图密钥。连线仅示意，不是可导航路线；地点顺序见下方。")
        }
    }
}
