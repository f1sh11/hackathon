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
import com.google.maps.android.compose.*
import com.google.android.gms.maps.model.LatLng as GoogleLatLng
import com.google.android.gms.maps.CameraUpdateFactory as GoogleCameraUpdate
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
    var zoom by remember{mutableDoubleStateOf(14.0)}
    val scope=rememberCoroutineScope();val uri=LocalUriHandler.current
    var searchJob by remember{mutableStateOf<Job?>(null)}
    var hasSearched by remember{mutableStateOf(false)}
    fun search(){searchJob?.cancel();busy=true;error=null;hasSearched=true;places=emptyList();selected=null;searchJob=scope.launch{try{val result=withTimeout(30000){requester(JSONObject().put("query",query).put("category",category).put("lat",center.first).put("lng",center.second).put("radius",1500))};places=result.array("places").objects();selected=null}catch(e:CancellationException){if(e is TimeoutCancellationException)error="地點服務逾時，請重試。"else throw e}catch(e:Exception){error=e.message ?: "搜尋失敗"}finally{if(isActive)busy=false}}}
    LaunchedEffect(category){search()}
    Dialog(onDismissRequest=onDismiss,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.fillMaxSize()) {Column(Modifier.fillMaxSize().systemBarsPadding().padding(12.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Text("搜尋地圖地點",Modifier.weight(1f),style=MaterialTheme.typography.titleMedium);TextButton(onClick=onDismiss){Text("完成")}}
            Row(verticalAlignment=Alignment.CenterVertically){OutlinedTextField(query,{query=it},Modifier.weight(1f),placeholder={Text("地點名稱（可留空）")},singleLine=true);TextButton(onClick=::search,enabled=!busy){Text("搜尋")}}
            Row{listOf("sights" to "景點","food" to "餐飲","facilities" to "生活設施").forEach{(id,label)->TextButton(onClick={category=id}){Text((if(id==category)"✓ "else "")+label)}}}
            Row{MapProvider.entries.forEach{provider->FilterChip(selected=store.mapProvider==provider,onClick={store.chooseMapProvider(provider)},label={Text(provider.label,fontSize=10.sp)})}}
            Small("地點資料：OpenStreetMap")
            when(store.mapProvider) {
                MapProvider.OSM->PoiOsmMap(places,onCenter={center=it},onSelect={selected=it},Modifier.fillMaxWidth().height(230.dp),initialCenter=center,initialZoom=zoom,onZoom={zoom=it})
                MapProvider.GOOGLE->if(BuildConfig.MAPS_CONFIGURED)PoiGoogleMap(places,center,zoom,onCamera={point,z->center=point;zoom=z},onSelect={selected=it})else Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text("Google 地圖尚未設定金鑰，請選擇其他底圖。")}
                MapProvider.SCHEMATIC->Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text("離線示意圖沒有街道底圖，請切換聯網底圖來移動搜尋範圍。")}
            }
            Small("切換分類自動搜尋 · 移動地圖後按搜尋 · 附近 1.5 km")
            if(busy){LinearProgressIndicator(Modifier.fillMaxWidth());Small("正在搜尋附近地點…")}
            error?.let{Text("地點資料暫時無法載入："+it,color=Color.Red,fontSize=12.sp);Small("地圖底圖仍可顯示；地點清單尚未取得，並非附近沒有地點。");TextButton(onClick=::search){Text("重新搜尋")}}
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                if(places.isEmpty()&&!busy&&error==null&&hasSearched)Small("此範圍沒有符合的地點。可清空名稱、切換分類，或移動地圖後重新搜尋。")
                places.sortedBy{if(it.optString("id")==selected)0 else 1}.forEach{p->Column(Modifier.fillMaxWidth().background(if(p.optString("id")==selected)Mint else Color.White).padding(8.dp)) {
                    Text((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else "📍 ")+p.optString("name"),color=if(p.optString("category")=="美食")Color(0xffa65d12)else Green)
                    Text(p.optString("description"),fontSize=12.sp,maxLines=2)
                    Row{TextButton(onClick={detail=p}){Text("詳細介紹")};Spacer(Modifier.weight(1f));val added=store.poiToolbox.any{it.optString("id")==p.optString("id")};TextButton(onClick={onAdd(p)},enabled=!added){Text(if(added)"已加入"else "＋工具箱")}}
                }}
            }
            TextButton(onClick={uri.openUri("https://www.openstreetmap.org/copyright")}){Text("地點資料 © OpenStreetMap contributors · ODbL",fontSize=10.sp)}
        }}
    }
    detail?.let{p->AlertDialog(onDismissRequest={detail=null},title={Text(p.optString("name"))},text={Column(Modifier.verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)){Text(p.optString("description"));Text("OSM 營業時間標記："+p.optString("openingHoursText").ifBlank{"未知"});Text(p.optString("note"))}},confirmButton={TextButton(onClick={detail=null}){Text("完成")}})}
}
@Composable private fun PoiOsmMap(places:List<JSONObject>,onCenter:(Pair<Double,Double>)->Unit,onSelect:(String)->Unit,modifier:Modifier,initialCenter:Pair<Double,Double>,initialZoom:Double,onZoom:(Double)->Unit) {
    val context=LocalContext.current;val lifecycle=LocalLifecycleOwner.current.lifecycle
    var map by remember{mutableStateOf<MapLibreMap?>(null)}
    val currentCenter by rememberUpdatedState(onCenter);val currentSelect by rememberUpdatedState(onSelect);val currentZoom by rememberUpdatedState(onZoom)
    val view=remember{OsmNetwork.initialize(context);MapView(context).apply{onCreate(null);getMapAsync{ready->
        ready.uiSettings.isLogoEnabled=false;ready.uiSettings.isAttributionEnabled=false
        ready.moveCamera(CameraUpdateFactory.newLatLngZoom(LatLng(initialCenter.first,initialCenter.second),initialZoom))
        ready.addOnCameraIdleListener{ready.cameraPosition.target?.let{currentCenter(it.latitude to it.longitude)};currentZoom(ready.cameraPosition.zoom)}
        ready.setOnMarkerClickListener{marker->marker.snippet?.let{currentSelect(it)};false}
        ready.setStyle(Style.Builder().fromJson(OSM_STYLE)){map=ready}
    }}}
    DisposableEffect(view,lifecycle){val observer=LifecycleEventObserver{_,event->when(event){Lifecycle.Event.ON_START->view.onStart();Lifecycle.Event.ON_RESUME->view.onResume();Lifecycle.Event.ON_PAUSE->view.onPause();Lifecycle.Event.ON_STOP->view.onStop();else->Unit}};view.onStart();view.onResume();lifecycle.addObserver(observer);onDispose{lifecycle.removeObserver(observer);view.onPause();view.onStop();view.onDestroy()}}
    LaunchedEffect(map,places){map?.let{ready->ready.clear();places.forEach{p->ready.addMarker(MarkerOptions().position(LatLng(p.getDouble("lat"),p.getDouble("lng"))).icon(IconFactory.getInstance(context).fromBitmap(itineraryBadge(if(p.optString("category")=="美食")"餐"else if(p.optString("category")=="設施")"WC"else "景"))).title((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else "📍 ")+p.optString("name")).snippet(p.getString("id")))}}}
    AndroidView(factory={view},modifier=modifier)
}

@Composable private fun PoiGoogleMap(places:List<JSONObject>,center:Pair<Double,Double>,zoom:Double,onCamera:(Pair<Double,Double>,Double)->Unit,onSelect:(String)->Unit) {
    val camera=rememberCameraPositionState()
    val currentCamera by rememberUpdatedState(onCamera)
    var loaded by remember{mutableStateOf(false)}
    LaunchedEffect(Unit){camera.move(GoogleCameraUpdate.newLatLngZoom(GoogleLatLng(center.first,center.second),zoom.toFloat()))}
    LaunchedEffect(camera,loaded){if(loaded)snapshotFlow{camera.isMoving}.collect{moving->if(!moving)currentCamera(camera.position.target.latitude to camera.position.target.longitude,camera.position.zoom.toDouble())}}
    GoogleMap(Modifier.fillMaxWidth().height(230.dp),cameraPositionState=camera,onMapLoaded={loaded=true}) {
        places.forEach{p->Marker(state=MarkerState(position=GoogleLatLng(p.getDouble("lat"),p.getDouble("lng"))),title=p.getString("name"),onClick={onSelect(p.getString("id"));false})}
    }
}
