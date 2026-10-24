package com.sydneybuddy.travel
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
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

internal fun poiActivityKind(p:JSONObject):String=if(p.optString("source") !in listOf("OpenStreetMap","Apple Maps","Overture Maps"))"attraction"else when {p.optString("poiKind")=="toilets"->"restroom";p.optString("poiKind")=="drinking_water"->"rest";p.optString("category")=="美食"->"meal";else->"attraction"}
@Composable internal fun PoiDiscoveryDialog(store:BuddyStore,onDismiss:()->Unit,requester:suspend(JSONObject)->JSONObject={store.searchPoi(it)},onAdd:(JSONObject)->Unit={store.addPoi(it)},googleMapConfigured:Boolean=BuildConfig.MAPS_CONFIGURED) {
    var query by remember{mutableStateOf("")};var category by remember{mutableStateOf("sights")}
    var places by remember{mutableStateOf<List<JSONObject>>(emptyList())};var selected by remember{mutableStateOf<String?>(null)}
    var center by remember{mutableStateOf(-33.865 to 151.212)};var busy by remember{mutableStateOf(false)};var error by remember{mutableStateOf<String?>(null)};var detail by remember{mutableStateOf<JSONObject?>(null)}
    var zoom by remember{mutableDoubleStateOf(14.0)}
    val scope=rememberCoroutineScope();val uri=LocalUriHandler.current
    var searchJob by remember{mutableStateOf<Job?>(null)}
    var hasSearched by remember{mutableStateOf(false)}
    var resultProvider by remember{mutableStateOf<MapProvider?>(null)}
    var attribution by remember{mutableStateOf("")}
    var openSource by remember{mutableStateOf("Overture Maps / local snapshot")}
    var dataVersion by remember{mutableStateOf("")}
    val visiblePlaces=if(resultProvider==store.mapProvider)places else emptyList()
    val source=when(store.mapProvider){MapProvider.OSM->openSource;MapProvider.GOOGLE->"Google Places";MapProvider.SCHEMATIC->"離線示意 · 不搜尋地點"}
    fun search(){
        searchJob?.cancel();places=emptyList();selected=null;detail=null;error=null;hasSearched=true;busy=false;resultProvider=null
        val provider=store.mapProvider
        if(provider==MapProvider.SCHEMATIC){error="離線示意圖不提供地點搜尋，請切換其他底圖。";return}
        if(provider==MapProvider.GOOGLE&&!googleMapConfigured){error="Google 地圖尚未設定金鑰，請選擇其他底圖。";return}
        val input=JSONObject().put("query",query).put("category",category).put("lat",center.first).put("lng",center.second).put("radius",1500).put("provider",if(provider==MapProvider.GOOGLE)"google"else "osm").put("language",when(store.language){AppLanguage.ENGLISH->"en";AppLanguage.SIMPLIFIED->"zh-CN";else->"zh-TW"})
        busy=true
        searchJob=scope.launch{try{
            val result=withTimeout(30000){requester(input)}
            ensureActive();if(store.mapProvider!=provider)return@launch
            val found=result.array("places").objects()
            require(provider!=MapProvider.GOOGLE || found.all{it.optString("source")=="Google Places"}){"搜尋來源不一致，請更新後端後重試。"}
            places=found;resultProvider=provider;attribution=result.optString("attribution",if(provider==MapProvider.GOOGLE)"Google Maps"else "Overture Maps Foundation")
            if(provider==MapProvider.OSM){openSource=result.optString("source","開放地點資料");dataVersion=result.optJSONObject("metadata")?.optString("release") ?: ""}
        }catch(e:CancellationException){if(e is TimeoutCancellationException)error="地點服務逾時，請重試。"else throw e}catch(e:Exception){error=e.message ?: "搜尋失敗"}finally{if(isActive)busy=false}}
    }
    LaunchedEffect(category,store.mapProvider){
        searchJob?.cancel();places=emptyList();selected=null;detail=null;error=null;busy=false;hasSearched=false;resultProvider=null;attribution="";dataVersion=""
        if(store.mapProvider==MapProvider.OSM)search()
    }
    Dialog(onDismissRequest=onDismiss,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.fillMaxSize()) {Column(Modifier.fillMaxSize().systemBarsPadding().padding(12.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Text(ui("搜尋地圖地點"),Modifier.weight(1f),style=MaterialTheme.typography.titleMedium);TextButton(onClick=onDismiss){Text(ui("完成"))}}
            Row(verticalAlignment=Alignment.CenterVertically){OutlinedTextField(query,{query=it},Modifier.weight(1f),placeholder={Text(ui("地點名稱（可留空）"))},singleLine=true);TextButton(onClick=::search,enabled=!busy){Text(ui("搜尋"))}}
            Row{listOf("sights" to "景點","food" to "餐飲","facilities" to "生活設施").forEach{(id,label)->TextButton(onClick={category=id}){Text((if(id==category)"✓ "else "")+ui(label))}}}
            Row{MapProvider.entries.forEach{provider->FilterChip(selected=store.mapProvider==provider,onClick={store.chooseMapProvider(provider)},label={Text(provider.label,fontSize=10.sp)})}}
            Small(ui("地點資料：{0}",ui(source)))
            when(store.mapProvider) {
                MapProvider.OSM->PoiOsmMap(visiblePlaces,onCenter={center=it},onSelect={selected=it},Modifier.fillMaxWidth().height(230.dp),initialCenter=center,initialZoom=zoom,onZoom={zoom=it})
                MapProvider.GOOGLE->if(BuildConfig.MAPS_CONFIGURED)PoiGoogleMap(visiblePlaces,center,zoom,onCamera={point,z->center=point;zoom=z},onSelect={selected=it})else Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text(ui("Google 地圖尚未設定金鑰，請選擇其他底圖。"))}
                MapProvider.SCHEMATIC->Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text(ui("離線示意圖沒有街道底圖，請切換聯網底圖來移動搜尋範圍。"))}
            }
            Small(if(store.mapProvider==MapProvider.GOOGLE)"Google Places · 按搜尋才查詢 · 附近 1.5 km"else "切換分類自動搜尋 · 移動地圖後按搜尋 · 附近 1.5 km")
            if(busy){LinearProgressIndicator(Modifier.fillMaxWidth());Small("正在搜尋附近地點…")}
            error?.let{Text("地點資料暫時無法載入："+it,color=Color.Red,fontSize=12.sp);Small("地圖底圖仍可顯示；地點清單尚未取得，並非附近沒有地點。");TextButton(onClick=::search){Text(ui("重新搜尋"))}}
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                if(visiblePlaces.isEmpty()&&!busy&&error==null&&hasSearched)Small("此範圍沒有符合的地點。可清空名稱、切換分類，或移動地圖後重新搜尋。")
                visiblePlaces.sortedBy{if(it.optString("id")==selected)0 else 1}.forEach{p->Column(Modifier.fillMaxWidth().background(if(p.optString("id")==selected)Mint else Color.White).clickable{detail=p}.padding(8.dp)) {
                    Text((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else "📍 ")+p.optString("name"),color=if(p.optString("category")=="美食")Color(0xffa65d12)else Green)
                    Text(p.optString("description"),fontSize=12.sp,maxLines=2)
                    Row{TextButton(onClick={detail=p}){Text(ui("詳細介紹"))};Spacer(Modifier.weight(1f));val added=store.poiToolbox.any{it.optString("id")==p.optString("id")};if(p.optString("source")=="Google Places")Small("Google 地點暫不匯入本機示例時間軸")else TextButton(onClick={if(added)store.removePoi(p.optString("id"))else{store.addPoi(p);onAdd(p)}}){Text(ui(if(added)"移出工具箱"else "＋工具箱"))}}
                }}
            }
            if(store.mapProvider==MapProvider.OSM)Column {
                Row {
                TextButton(onClick={uri.openUri("https://www.openstreetmap.org/copyright")},modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text(ui("地圖 © OpenStreetMap contributors · ODbL"),fontSize=10.sp)}
                TextButton(onClick={uri.openUri(if(attribution.contains("OpenStreetMap"))"https://www.openstreetmap.org/copyright"else "https://docs.overturemaps.org/attribution/")},modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text(attribution.ifBlank{"Overture Maps Foundation"},fontSize=10.sp)}
                }
                if(dataVersion.isNotBlank())Text(ui("本機地點資料快照：{0}",dataVersion),fontSize=10.sp)
            }
            else if(store.mapProvider==MapProvider.GOOGLE)Small(attribution.ifBlank{"Google Maps"})
        }}
    }
    detail?.let{p->SchedulePlaceDetail(p,store){detail=null}}
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

@Composable internal fun PlaceContact(p:JSONObject) {
    val uri=LocalUriHandler.current
    if(p.optString("phone").isNotBlank())Text(ui("電話：")+p.optString("phone"))
    val website=p.optString("website")
    if(website.startsWith("https://")||website.startsWith("http://"))TextButton(onClick={uri.openUri(website)}){Text(ui("官方網站"))}
    if(p.optString("sourceRelease").isNotBlank())Small("Overture · "+p.optString("sourceRelease"))
}
@Composable internal fun PlaceMedia(p:JSONObject,store:BuddyStore) {
    val scope=rememberCoroutineScope();val uri=LocalUriHandler.current
    var photos by remember(p.optString("id")){mutableStateOf<List<JSONObject>>(emptyList())}
    var loading by remember{mutableStateOf(false)};var message by remember{mutableStateOf<String?>(null)}
    Text(ui("照片與街景"),style=MaterialTheme.typography.titleSmall)
    Small(ui("附近 100 公尺的實景照片，並非已核實的商家或店內照片。"))
    TextButton(onClick={scope.launch{
        loading=true;message=null
        try{photos=store.poiMedia(JSONObject().put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng"))).array("photos").objects();if(photos.isEmpty())message="附近尚無可用的開放照片。"}
        catch(e:CancellationException){throw e}catch(e:Exception){message="照片暫時無法載入，請稍後重試。"}finally{loading=false}
    }},enabled=!loading){Text(ui(if(photos.isEmpty())"載入附近照片"else "重新載入照片"))}
    if(loading)LinearProgressIndicator(Modifier.fillMaxWidth())
    message?.let{Small(ui(it))}
    photos.forEach{photo->
        CommonsPhoto(photo.optString("url"))
        Text(photo.optString("title"),fontSize=12.sp)
        Small(photo.optString("artist")+" · "+photo.optString("license"))
        TextButton(onClick={uri.openUri(photo.getString("pageUrl"))}){Text(ui("照片來源與授權"))}
    }
    TextButton(onClick={uri.openUri("https://www.mapillary.com/app/?lat=${p.optDouble("lat")}&lng=${p.optDouble("lng")}&z=17")}){Text(ui("開啟附近街景（外部地圖）"))}
    Small(ui("街景覆蓋由服務提供者決定；未必有此商家的全景。"))
}
private val photoClient=okhttp3.OkHttpClient.Builder().callTimeout(15,java.util.concurrent.TimeUnit.SECONDS).followRedirects(false).build()
@Composable private fun CommonsPhoto(url:String) {
    var bitmap by remember(url){mutableStateOf<android.graphics.Bitmap?>(null)};var failed by remember(url){mutableStateOf(false)}
    LaunchedEffect(url){
        try{bitmap=withContext(Dispatchers.IO){
            require(android.net.Uri.parse(url).let{it.scheme=="https"&&it.host=="upload.wikimedia.org"})
            photoClient.newCall(okhttp3.Request.Builder().url(url).build()).execute().use{response->
                require(response.isSuccessful);val body=response.body ?: error("Empty photo")
                require(body.contentLength() in 1..5_000_000)
                android.graphics.BitmapFactory.decodeStream(body.byteStream()) ?: error("Invalid photo")
            }
        }}catch(e:CancellationException){throw e}catch(e:Exception){failed=true}
    }
    val photo=bitmap
    if(photo!=null)androidx.compose.foundation.Image(bitmap=photo.asImageBitmap(),contentDescription=ui("附近實景照片"),modifier=Modifier.fillMaxWidth().height(170.dp),contentScale=androidx.compose.ui.layout.ContentScale.Crop)
    else if(failed)Small(ui("照片無法載入"))else CircularProgressIndicator()
}
