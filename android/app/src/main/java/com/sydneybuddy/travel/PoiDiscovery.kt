package com.sydneybuddy.travel
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import kotlinx.coroutines.*
import org.json.JSONObject
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.Style
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.annotations.MarkerOptions
import org.maplibre.android.annotations.IconFactory

internal fun poiActivityKind(p:JSONObject):String=if(p.optString("source")!="OpenStreetMap")"attraction"else when {p.optString("poiKind")=="toilets"->"restroom";p.optString("poiKind")=="drinking_water"->"rest";p.optString("category")=="美食"->"meal";else->"attraction"}
@Composable internal fun PoiDiscoveryDialog(store:BuddyStore,onDismiss:()->Unit,requester:suspend(JSONObject)->JSONObject={store.searchPoi(it)},onAdd:(JSONObject)->Unit={store.addPoi(it)}) {
    var query by remember{mutableStateOf("")};var category by remember{mutableStateOf("sights")}
    var places by remember{mutableStateOf<List<JSONObject>>(emptyList())};var selected by remember{mutableStateOf<String?>(null)}
    var center by remember{mutableStateOf(-33.865 to 151.212)};var busy by remember{mutableStateOf(false)};var error by remember{mutableStateOf<String?>(null)};var detail by remember{mutableStateOf<JSONObject?>(null)}
    val scope=rememberCoroutineScope();val uri=LocalUriHandler.current
    fun search(){if(busy)return;busy=true;error=null;scope.launch{try{val result=withTimeout(30000){requester(JSONObject().put("query",query).put("category",category).put("lat",center.first).put("lng",center.second).put("radius",1500))};places=result.array("places").objects();selected=null}catch(e:CancellationException){if(e is TimeoutCancellationException)error="地點服務逾時，請重試。"else throw e}catch(e:Exception){error=e.message ?: "搜尋失敗"}finally{busy=false}}}
    Dialog(onDismissRequest=onDismiss,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.fillMaxSize()) {Column(Modifier.fillMaxSize().systemBarsPadding().padding(12.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Text("搜尋地圖地點",Modifier.weight(1f),style=MaterialTheme.typography.titleMedium);TextButton(onClick=onDismiss){Text("完成")}}
            Row(verticalAlignment=Alignment.CenterVertically){OutlinedTextField(query,{query=it},Modifier.weight(1f),placeholder={Text("地點名稱（可留空）")},singleLine=true);TextButton(onClick=::search,enabled=!busy){Text("搜尋")}}
            Row{listOf("sights" to "景點","food" to "餐飲","facilities" to "生活設施").forEach{(id,label)->TextButton(onClick={category=id}){Text((if(id==category)"✓ "else "")+label)}}}
            PoiOsmMap(places,onCenter={center=it},onSelect={selected=it},Modifier.fillMaxWidth().height(230.dp))
            Small("移動地圖後按搜尋 · 中心附近 1.5 km · OSM 開源測試")
            if(busy)LinearProgressIndicator(Modifier.fillMaxWidth());error?.let{Text(it,color=Color.Red,fontSize=12.sp)}
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                if(places.isEmpty()&&!busy)Small("選擇分類並搜尋附近地點；點地圖標記可選擇。")
                places.sortedBy{if(it.optString("id")==selected)0 else 1}.forEach{p->Column(Modifier.fillMaxWidth().background(if(p.optString("id")==selected)Mint else Color.White).padding(8.dp)) {
                    Text((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else "📍 ")+p.optString("name"),color=if(p.optString("category")=="美食")Color(0xffa65d12)else Green)
                    Text(p.optString("description"),fontSize=12.sp,maxLines=2)
                    Row{TextButton(onClick={detail=p}){Text("詳細介紹")};Spacer(Modifier.weight(1f));val added=store.poiToolbox.any{it.optString("id")==p.optString("id")};TextButton(onClick={onAdd(p)},enabled=!added){Text(if(added)"已加入"else "＋工具箱")}}
                }}
            }
            TextButton(onClick={uri.openUri("https://www.openstreetmap.org/copyright")}){Text("© OpenStreetMap contributors · ODbL",fontSize=10.sp)}
        }}
    }
    detail?.let{p->AlertDialog(onDismissRequest={detail=null},title={Text(p.optString("name"))},text={Column(Modifier.verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)){Text(p.optString("description"));Text("OSM 營業時間標記："+p.optString("openingHoursText").ifBlank{"未知"});Text(p.optString("note"))}},confirmButton={TextButton(onClick={detail=null}){Text("完成")}})}
}
@Composable private fun PoiOsmMap(places:List<JSONObject>,onCenter:(Pair<Double,Double>)->Unit,onSelect:(String)->Unit,modifier:Modifier) {
    val context=LocalContext.current;val lifecycle=LocalLifecycleOwner.current.lifecycle
    var map by remember{mutableStateOf<MapLibreMap?>(null)}
    val currentCenter by rememberUpdatedState(onCenter);val currentSelect by rememberUpdatedState(onSelect)
    val view=remember{OsmNetwork.initialize(context);MapView(context).apply{onCreate(null);getMapAsync{ready->
        ready.uiSettings.isLogoEnabled=false;ready.uiSettings.isAttributionEnabled=false
        ready.moveCamera(CameraUpdateFactory.newLatLngZoom(LatLng(-33.865,151.212),14.0))
        ready.addOnCameraIdleListener{ready.cameraPosition.target?.let{currentCenter(it.latitude to it.longitude)}}
        ready.setOnMarkerClickListener{marker->marker.snippet?.let{currentSelect(it)};false}
        ready.setStyle(Style.Builder().fromJson(OSM_STYLE)){map=ready}
    }}}
    DisposableEffect(view,lifecycle){val observer=LifecycleEventObserver{_,event->when(event){Lifecycle.Event.ON_START->view.onStart();Lifecycle.Event.ON_RESUME->view.onResume();Lifecycle.Event.ON_PAUSE->view.onPause();Lifecycle.Event.ON_STOP->view.onStop();else->Unit}};view.onStart();view.onResume();lifecycle.addObserver(observer);onDispose{lifecycle.removeObserver(observer);view.onPause();view.onStop();view.onDestroy()}}
    LaunchedEffect(map,places){map?.let{ready->ready.clear();places.forEach{p->ready.addMarker(MarkerOptions().position(LatLng(p.getDouble("lat"),p.getDouble("lng"))).icon(IconFactory.getInstance(context).fromBitmap(itineraryBadge(if(p.optString("category")=="美食")"餐"else if(p.optString("category")=="設施")"WC"else "景"))).title((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else "📍 ")+p.optString("name")).snippet(p.getString("id")))}}}
    AndroidView(factory={view},modifier=modifier)
}
