package com.sydneybuddy.travel
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.testTag
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
import org.json.JSONArray
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

/** Camera idle is deduplicated before the network debounce, so marker updates never re-query. */
internal data class PoiViewport(val lat:Double,val lng:Double,val zoom:Double) {
    val radius:Int get()=(156543.03392*kotlin.math.cos(Math.toRadians(lat))/Math.pow(2.0,zoom)*420).toInt().coerceIn(500,5000)
    fun differs(other:PoiViewport?):Boolean {
        if(other==null)return true
        val north=(lat-other.lat)*111320
        val east=(lng-other.lng)*111320*kotlin.math.cos(Math.toRadians(lat))
        return kotlin.math.hypot(north,east)>60 || kotlin.math.abs(zoom-other.zoom)>0.35
    }
}
internal class NearbyPoiState {
    var viewport by mutableStateOf<PoiViewport?>(null)
    var places by mutableStateOf<List<JSONObject>>(emptyList())
    var error by mutableStateOf<String?>(null)
    var busy by mutableStateOf(false)
    fun move(lat:Double,lng:Double,zoom:Double){
        val next=PoiViewport(lat,lng,zoom)
        if(lat.isFinite()&&lng.isFinite()&&zoom.isFinite()&&next.differs(viewport)){viewport=next;places=emptyList();error=null}
    }
}
@Composable internal fun rememberNearbyPoi(store:BuddyStore,requester:suspend(JSONObject)->JSONObject={store.searchPoi(it)}):NearbyPoiState {
    val state=remember(store.mapProvider,store.travelDataSource){NearbyPoiState()}
    val point=state.viewport
    LaunchedEffect(state,point){
        if(point==null||store.mapProvider==MapProvider.SCHEMATIC)return@LaunchedEffect
        delay(800);state.busy=true
        try {
            val result=withTimeout(30000){requester(JSONObject().put("lat",point.lat).put("lng",point.lng).put("radius",point.radius).put("category","all").put("query","").put("provider",if(store.mapProvider==MapProvider.GOOGLE&&store.travelDataSource=="direct")"google"else "osm"))}
            ensureActive();state.places=result.array("places").objects()
        }catch(e:CancellationException){throw e}catch(e:Exception){state.error=e.message ?: "地點搜尋失敗"}finally{state.busy=false}
    }
    return state
}
internal fun poiActivityKind(p:JSONObject):String=if(p.optString("source") !in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage"))"attraction"else when {p.optString("poiKind")=="toilets"->"restroom";p.optString("poiKind")=="drinking_water"->"rest";p.optString("category")=="美食"->"meal";else->"attraction"}
@Composable internal fun PoiDiscoveryDialog(store:BuddyStore,onDismiss:()->Unit,requester:suspend(JSONObject)->JSONObject={store.searchPoi(it)},onAdd:(JSONObject)->Unit={store.addPoi(it)},googleMapConfigured:Boolean=BuildConfig.MAPS_CONFIGURED) {
    var showInsights by remember{mutableStateOf(false)}
    var query by remember{mutableStateOf("")};var category by remember{mutableStateOf("all")}
    var places by remember{mutableStateOf<List<JSONObject>>(emptyList())};var selected by remember{mutableStateOf<String?>(null)}
    var center by remember{mutableStateOf(-33.865 to 151.212)};var busy by remember{mutableStateOf(false)};var error by remember{mutableStateOf<String?>(null)};var detail by remember{mutableStateOf<JSONObject?>(null)}
    var zoom by remember{mutableDoubleStateOf(14.0)}
    var cameraRequest by remember{mutableStateOf<PoiViewport?>(null)}

    val scope=rememberCoroutineScope();val uri=LocalUriHandler.current
    var searchJob by remember{mutableStateOf<Job?>(null)}
    var hasSearched by remember{mutableStateOf(false)}
    var resultDataSource by remember{mutableStateOf<String?>(null)}
    var resultProvider by remember{mutableStateOf<MapProvider?>(null)}
    var attribution by remember{mutableStateOf("")}
    var openSource by remember{mutableStateOf("Overture Maps / local snapshot")}
    var summary by remember{mutableStateOf<String?>(null)}
    var dataVersion by remember{mutableStateOf("")}
    val visiblePlaces=if(resultProvider==store.mapProvider&&resultDataSource==store.travelDataSource)places else emptyList()
    val source=if(store.travelDataSource=="zetaris")"Open travel data / Zetaris"else if(category=="gems")"Open travel data / local"else when(store.mapProvider){MapProvider.OSM->openSource;MapProvider.GOOGLE->"Google Places";MapProvider.SCHEMATIC->"離線示意 · 不搜尋地點"}
    fun search(){
        searchJob?.cancel();places=emptyList();selected=null;detail=null;error=null;hasSearched=true;busy=false;resultProvider=null
        summary=null;val provider=store.mapProvider;val route=store.travelDataSource
        if(provider==MapProvider.SCHEMATIC){error="離線示意圖不提供地點搜尋，請切換其他底圖。";return}
        if(category!="gems"&&route=="direct"&&provider==MapProvider.GOOGLE&&!googleMapConfigured){error="Google 地圖尚未設定金鑰，請選擇其他底圖。";return}
        val input=JSONObject().put("query",query).put("category",category).put("lat",center.first).put("lng",center.second).put("radius",cameraRequest?.radius ?: if(category in listOf("gems","all"))5000 else 1500).put("provider",if(category!="gems"&&provider==MapProvider.GOOGLE&&route=="direct")"google"else "osm").put("language",when(store.language){AppLanguage.ENGLISH->"en";AppLanguage.SIMPLIFIED->"zh-CN";else->"zh-TW"})
        busy=true
        searchJob=scope.launch{try{
            val result=withTimeout(30000){requester(input)}
            ensureActive();if(store.mapProvider!=provider||store.travelDataSource!=route)return@launch
            val found=result.array("places").objects()
            require(category=="gems"||route=="zetaris"||provider!=MapProvider.GOOGLE || found.all{it.optString("source")=="Google Places"}){"搜尋來源不一致，請更新後端後重試。"}
            summary=if(result.optBoolean("truncated"))"已顯示部分結果，請輸入名稱或縮小分類。"else result.optString("message").takeIf{it.isNotBlank()};places=found;resultProvider=provider;resultDataSource=route;attribution=result.optString("attribution",if(provider==MapProvider.GOOGLE)"Google Maps"else "Overture Maps Foundation")
            if(category=="gems"||provider==MapProvider.OSM||route=="zetaris"){openSource=result.optString("source","開放地點資料");dataVersion=result.optJSONObject("metadata")?.optString("release") ?: ""}
        }catch(e:CancellationException){if(e is TimeoutCancellationException)error="地點服務逾時，請重試。"else throw e}catch(e:Exception){error=e.message ?: "搜尋失敗"}finally{if(isActive)busy=false}}
    }
    fun moved(point:Pair<Double,Double>,value:Double){
        center=point;zoom=value
        val next=PoiViewport(point.first,point.second,value)
        if(next.differs(cameraRequest)){searchJob?.cancel();places=emptyList();selected=null;cameraRequest=next}
    }
    LaunchedEffect(cameraRequest){if(cameraRequest!=null){delay(800);search()}}
    LaunchedEffect(category,store.mapProvider,store.travelDataSource){
        cameraRequest=null
        searchJob?.cancel();places=emptyList();selected=null;detail=null;error=null;busy=false;hasSearched=false;resultProvider=null;attribution="";dataVersion=""
        if(category=="gems"||store.mapProvider==MapProvider.OSM||store.travelDataSource=="zetaris")search()
    }
    Dialog(onDismissRequest=onDismiss,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.fillMaxSize()) {Column(Modifier.fillMaxSize().systemBarsPadding().padding(12.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Text(ui("搜尋地圖地點"),Modifier.weight(1f),style=MaterialTheme.typography.titleMedium);TextButton(onClick=onDismiss){Text(ui("完成"))}}
            Row(verticalAlignment=Alignment.CenterVertically){OutlinedTextField(query,{query=it},Modifier.weight(1f),placeholder={Text(ui("地點名稱（可留空）"))},singleLine=true);TextButton(onClick=::search,enabled=!busy){Text(ui("搜尋"))}}
            Row(Modifier.horizontalScroll(rememberScrollState())){listOf("all" to "全部","sights" to "景點","gems" to "私房景點","food" to "餐飲","lodging" to "住宿","shops" to "店家","facilities" to "生活設施").forEach{(id,label)->TextButton(onClick={category=id}){Text((if(id==category)"✓ "else "")+ui(label))}}}
            Row{MapProvider.entries.filter{store.mapLayerIds?.contains(it.name.lowercase()) ?: true}.forEach{provider->FilterChip(selected=store.mapProvider==provider,onClick={store.chooseMapProvider(provider)},label={Text(provider.label,fontSize=10.sp)})}}
            Small(ui("地點資料：{0}",ui(source)))
            TextButton(onClick={
                val points=JSONArray().put(JSONObject().put("id","center").put("name","搜尋中心").put("lat",center.first).put("lng",center.second))
                visiblePlaces.take(20).forEach{p->points.put(JSONObject().put("id",p.optString("id")).put("name",p.optString("name")).put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng")))}
                store.refreshInsights(points);showInsights=true
            }){Text("交通・人潮・費用")}

            when(store.mapProvider) {
                MapProvider.OSM->PoiOsmMap(visiblePlaces,onCenter={moved(it,zoom)},onSelect={selected=it},Modifier.fillMaxWidth().height(230.dp),initialCenter=center,initialZoom=zoom,onZoom={moved(center,it)},insightMarkers=(store.insights?.optJSONObject("traffic")?.array("items")?.objects() ?: emptyList())+(store.insights?.array("pricePlaces")?.objects() ?: emptyList()))
                MapProvider.GOOGLE->if(BuildConfig.MAPS_CONFIGURED)PoiGoogleMap(visiblePlaces,center,zoom,onCamera={point,z->moved(point,z)},onSelect={selected=it},insightMarkers=(store.insights?.optJSONObject("traffic")?.array("items")?.objects() ?: emptyList())+(store.insights?.array("pricePlaces")?.objects() ?: emptyList()))else Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text(ui("Google 地圖尚未設定金鑰，請選擇其他底圖。"))}
                MapProvider.SCHEMATIC->Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text(ui("離線示意圖沒有街道底圖，請切換聯網底圖來移動搜尋範圍。"))}
            }
            Small(if(category=="all")"全部分類 · 附近 5 km · 資料限已匯入區域"else if(category=="gems")"社群探索候選 · 附近 5 km · 非熱門度評分"else if(store.mapProvider==MapProvider.GOOGLE&&store.travelDataSource=="direct")"Google Places · 移動地圖後自動搜尋 · 最遠 5 km"else "切換分類自動搜尋 · 移動地圖後自動搜尋 · 附近 1.5 km")
            summary?.let{Small(it)}
            if(busy){LinearProgressIndicator(Modifier.fillMaxWidth());Small("正在搜尋附近地點…")}
            error?.let{Text("地點資料暫時無法載入："+it,color=Color.Red,fontSize=12.sp);Small("地圖底圖仍可顯示；地點清單尚未取得，並非附近沒有地點。");TextButton(onClick=::search){Text(ui("重新搜尋"))}}
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                if(visiblePlaces.isEmpty()&&!busy&&error==null&&hasSearched)Small("此範圍沒有符合的地點。可清空名稱、切換分類，或移動地圖後重新搜尋。")
                visiblePlaces.sortedBy{if(it.optString("id")==selected)0 else 1}.forEach{p->Column(Modifier.fillMaxWidth().background(if(p.optString("id")==selected)Mint else Color.White).clickable{detail=p}.padding(8.dp)) {
                    Text((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else if(p.optString("category")=="住宿")"🛏 "else if(p.optString("category")=="店家")"🛍 "else "📍 ")+p.optString("name"),color=if(p.optString("category")=="美食")Color(0xffa65d12)else Green)
                    Small(ui(p.optString("category"))+" · "+(p.optJSONArray("sources")?.strings()?.joinToString(" / ") ?: p.optString("source")))
                    Text(p.optString("description"),fontSize=12.sp,maxLines=2)
                    Row{TextButton(onClick={detail=p}){Text(ui("詳細介紹"))};Spacer(Modifier.weight(1f));val added=store.poiToolbox.any{it.optString("id")==p.optString("id")};if(p.optString("source")=="Google Places")Small("Google 地點暫不匯入本機示例時間軸")else TextButton(onClick={if(added)store.removePoi(p.optString("id"))else{store.addPoi(p);onAdd(p)}}){Text(ui(if(added)"移出工具箱"else "＋工具箱"))}}
                }}
            }
            if(store.mapProvider==MapProvider.OSM)Column {
                Row {
                TextButton(onClick={uri.openUri("https://www.openstreetmap.org/copyright")},modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text(ui("地圖 © OpenStreetMap contributors · ODbL"),fontSize=10.sp)}
                TextButton(onClick={uri.openUri(if(category=="gems")"https://en.wikivoyage.org/wiki/Wikivoyage:Copyleft"else if(attribution.contains("OpenStreetMap"))"https://www.openstreetmap.org/copyright"else "https://docs.overturemaps.org/attribution/")},modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text(attribution.ifBlank{"Overture Maps Foundation"},fontSize=10.sp)}
                }
                if(dataVersion.isNotBlank())Text(ui("本機地點資料快照：{0}",dataVersion),fontSize=10.sp)
            }
            else if(store.travelDataSource=="zetaris") {Small(attribution.ifBlank{"Overture Maps Foundation"});if(dataVersion.isNotBlank())Small(ui("本機地點資料快照：{0}",dataVersion))}
            else if(store.mapProvider==MapProvider.GOOGLE)Small(attribution.ifBlank{"Google Maps"})
        }}
    }
    if(showInsights){
        val insightPoints=JSONArray().put(JSONObject().put("id","center").put("name","搜尋中心").put("lat",center.first).put("lng",center.second))
        visiblePlaces.take(20).forEach{p->insightPoints.put(JSONObject().put("id",p.optString("id")).put("name",p.optString("name")).put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng")))}
        TravelInsightsDialog(store,insightPoints){showInsights=false}
    }
    detail?.let{p->SchedulePlaceDetail(p,store){detail=null}}
}
@Composable private fun PoiOsmMap(places:List<JSONObject>,onCenter:(Pair<Double,Double>)->Unit,onSelect:(String)->Unit,modifier:Modifier,initialCenter:Pair<Double,Double>,initialZoom:Double,onZoom:(Double)->Unit,insightMarkers:List<JSONObject> = emptyList()) {
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
    LaunchedEffect(map,places,insightMarkers.toString()){map?.let{ready->ready.clear();insightMarkers.forEach{p->ready.addMarker(MarkerOptions().position(LatLng(p.getDouble("lat"),p.getDouble("lng"))).title(p.optString("title")).snippet(p.optString("summary")).icon(IconFactory.getInstance(context).fromBitmap(itineraryBadge(if(p.optString("kind")=="price")"$"else "!"))))};places.forEach{p->ready.addMarker(MarkerOptions().position(LatLng(p.getDouble("lat"),p.getDouble("lng"))).icon(IconFactory.getInstance(context).fromBitmap(itineraryBadge(when(p.optString("category")){"美食"->"餐";"設施"->"WC";"住宿"->"宿";"店家"->"店";else->"景"}))).title((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else if(p.optString("category")=="住宿")"🛏 "else if(p.optString("category")=="店家")"🛍 "else "📍 ")+p.optString("name")).snippet(p.getString("id")))}}}
    AndroidView(factory={view},modifier=modifier)
}

@Composable private fun PoiGoogleMap(places:List<JSONObject>,center:Pair<Double,Double>,zoom:Double,onCamera:(Pair<Double,Double>,Double)->Unit,onSelect:(String)->Unit,insightMarkers:List<JSONObject> = emptyList()) {
    val camera=rememberCameraPositionState()
    val currentCamera by rememberUpdatedState(onCamera)
    var loaded by remember{mutableStateOf(false)}
    LaunchedEffect(Unit){camera.move(GoogleCameraUpdate.newLatLngZoom(GoogleLatLng(center.first,center.second),zoom.toFloat()))}
    LaunchedEffect(camera,loaded){if(loaded)snapshotFlow{camera.isMoving}.collect{moving->if(!moving)currentCamera(camera.position.target.latitude to camera.position.target.longitude,camera.position.zoom.toDouble())}}
    GoogleMap(Modifier.fillMaxWidth().height(230.dp),cameraPositionState=camera,onMapLoaded={loaded=true}) {
        insightMarkers.forEach{p->Marker(state=MarkerState(position=GoogleLatLng(p.getDouble("lat"),p.getDouble("lng"))),title=p.optString("title"),snippet=p.optString("summary"))}
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
internal class PlaceMediaState(initial:List<JSONObject>) {
    var photos by mutableStateOf(initial)
    var loading by mutableStateOf(false)
    var message by mutableStateOf<String?>(null)
    suspend fun load(request:JSONObject,requester:suspend(JSONObject)->JSONObject){
        if(loading)return
        loading=true;message=null
        try{
            val found=requester(request).array("photos").objects()
            ensureActivePhotoRequest()
            if(found.isNotEmpty())photos=found
            if(photos.isEmpty())message="附近尚無可用的開放照片。"
        }catch(e:CancellationException){throw e}catch(e:Exception){message="照片暫時無法載入，請稍後重試。"}finally{loading=false}
    }
    private suspend fun ensureActivePhotoRequest(){kotlin.coroutines.coroutineContext.ensureActive()}
}
@Composable internal fun PlaceMedia(p:JSONObject,store:BuddyStore,requester:suspend(JSONObject)->JSONObject={store.poiMedia(it)}) {
    val scope=rememberCoroutineScope();val uri=LocalUriHandler.current
    val media=remember(p.optString("id"),store.travelDataSource){PlaceMediaState(p.array("photos").objects())}
    val request=remember(p.optString("id")){JSONObject().put("id",p.optString("id")).put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng"))}
    LaunchedEffect(media){if(media.photos.isEmpty())media.load(request,requester)}
    Text(ui("照片與街景"),style=MaterialTheme.typography.titleSmall)
    Small(ui(if(p.optBoolean("hiddenGem"))"社群照片保留作者與授權；附近照片不代表已確認拍攝此景點。"else "附近 100 公尺的實景照片，並非已核實的商家或店內照片。"))
    TextButton(onClick={scope.launch{media.load(request,requester)}},enabled=!media.loading,modifier=Modifier.testTag("place-media-retry")){Text(ui(if(media.photos.isEmpty())"載入附近照片"else "重新載入照片"))}
    if(media.loading)LinearProgressIndicator(Modifier.fillMaxWidth())
    media.message?.let{Small(ui(it))}
    media.photos.forEach{photo->
        CommonsPhoto(photo.optString("url"))
        Text(photo.optString("title"),fontSize=12.sp)
        Small(photo.optString("artist")+" · "+photo.optString("license"))
        if(photo.optString("matchKind")=="nearby-unverified")Small("附近照片，景點配對未確認")
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
            require(android.net.Uri.parse(url).let{it.scheme=="https"&&it.host in setOf("upload.wikimedia.org","thumb.wikimedia.org")})
            photoClient.newCall(okhttp3.Request.Builder().url(url).build()).execute().use{response->
                require(response.isSuccessful);val body=response.body ?: error("Empty photo")
                val input=body.source();input.request(5_000_001);require(input.buffer.size in 1..5_000_000);val bytes=input.buffer.readByteArray()
                android.graphics.BitmapFactory.decodeByteArray(bytes,0,bytes.size) ?: error("Invalid photo")
            }
        }}catch(e:CancellationException){throw e}catch(e:Exception){failed=true}
    }
    val photo=bitmap
    if(photo!=null)androidx.compose.foundation.Image(bitmap=photo.asImageBitmap(),contentDescription=ui("附近實景照片"),modifier=Modifier.fillMaxWidth().height(170.dp),contentScale=androidx.compose.ui.layout.ContentScale.Crop)
    else if(failed)Small(ui("照片無法載入"))else CircularProgressIndicator()
}
