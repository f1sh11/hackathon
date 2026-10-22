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

@Composable fun RouteMap(day:JSONObject?,modifier:Modifier=Modifier,fullBleed:Boolean=false) {
    val stops=day?.array("stops")?.objects() ?: emptyList()
    val start=day?.getJSONObject("start") ?: Endpoint.quay.json();val end=day?.getJSONObject("end") ?: Endpoint.central.json()
    fun coordinate(j:JSONObject)=LatLng(j.getDouble("lat"),j.getDouble("lng"))
    val points=listOf(coordinate(start))+stops.map(::coordinate)+coordinate(end)
    val routes=day?.array("legs")?.objects()?.map {runCatching {decodePolyline(it.optString("polyline")).map{p->LatLng(p.first,p.second)}}.getOrDefault(emptyList())} ?: emptyList()
    val bounds=LatLngBounds.builder().apply{points.forEach{include(it)}}.build()
    if(BuildConfig.MAPS_CONFIGURED) {
        val camera=rememberCameraPositionState()
        var loaded by remember {mutableStateOf(false)}
        LaunchedEffect(day.toString(),loaded){if(loaded) camera.move(CameraUpdateFactory.newLatLngBounds(bounds,70))}
        GoogleMap(modifier=if(fullBleed)modifier else modifier.fillMaxWidth().height(310.dp).clip(RoundedCornerShape(22.dp)),cameraPositionState=camera,
            contentPadding=if(fullBleed)PaddingValues(top=180.dp,bottom=290.dp,start=12.dp,end=12.dp) else PaddingValues(0.dp),
            onMapLoaded={loaded=true},uiSettings=MapUiSettings(zoomControlsEnabled=false,myLocationButtonEnabled=false,mapToolbarEnabled=false)) {
            stops.forEachIndexed {i,s->
                key(day.toString(),s.optString("id")) {
                    val marker=remember {MarkerState(position=coordinate(s))}
                    Marker(state=marker,title="${i+1}. ${s.optString("name")}",snippet="${s.optString("arrival")}–${s.optString("departure")}",icon=BitmapDescriptorFactory.defaultMarker(155f))
                }
            }
            if(day!=null)key(day.toString()) {
                val marker=remember {MarkerState(position=coordinate(start))}
                Marker(state=marker,title="${start.optString("name")} · 起点",icon=BitmapDescriptorFactory.defaultMarker(155f))
                val endMarker=remember {MarkerState(position=coordinate(end))}
                if(coordinate(end)!=coordinate(start))Marker(state=endMarker,title="${end.optString("name")} · 返回点",icon=BitmapDescriptorFactory.defaultMarker(155f))
            }
            routes.filter{it.isNotEmpty()}.forEach{Polyline(points=it,color=Green,width=10f)}
            if(day!=null && routes.all{it.isEmpty()}) Polyline(points=points,color=Color.Gray,width=4f)
        }
        if(!fullBleed)Small(if(routes.any{it.isNotEmpty()}) "步行路线；来源见规划记录" else "灰色连线为示意，不是导航路线")
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
            Small("未配置 Android 地图密钥。连线仅示意，不是可导航路线；地点顺序见下方。")
        }
    }
}
