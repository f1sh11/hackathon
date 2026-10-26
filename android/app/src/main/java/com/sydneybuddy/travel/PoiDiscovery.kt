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
import com.google.android.gms.maps.model.BitmapDescriptorFactory
import com.google.android.gms.maps.CameraUpdateFactory as GoogleCameraUpdate
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.Style
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.annotations.MarkerOptions
import org.maplibre.android.annotations.IconFactory

internal object PoiDiscoveryCategory {
    val groups=listOf("all" to "All","sights" to "Sights","gems" to "Hidden gems","food" to "Food","lodging" to "Stays","shops" to "Shops","facilities" to "Facilities")
    val details=listOf(Triple("restaurants","Restaurants","food"),Triple("cafes","Cafes","food"),Triple("bakeries","Bakeries & desserts","food"),Triple("bars","Bars & nightlife","food"),Triple("museums","Museums","sights"),Triple("parks","Parks & gardens","sights"),Triple("beaches","Beaches","sights"),Triple("hotels","Hotels","lodging"),Triple("hostels","Hostels","lodging"),Triple("supermarkets","Groceries","shops"),Triple("toilets","Toilets","facilities"),Triple("drinking_water","Drinking water","facilities"))
    fun parent(id:String)=details.firstOrNull{it.first==id}?.third ?: id
    fun filters(id:String)=details.filter{parent(id)=="all"||it.third==parent(id)}
}
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
        }catch(e:CancellationException){throw e}catch(e:Exception){state.error=e.message ?: "Could not search places."}finally{state.busy=false}
    }
    return state
}
internal fun poiActivityKind(p:JSONObject):String=if(p.optString("source") !in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation"))"attraction"else when {p.optString("poiKind")=="toilets"->"restroom";p.optString("poiKind")=="drinking_water"->"rest";p.optString("category")=="美食"->"meal";else->"attraction"}
private fun focusedPoiIcon():android.graphics.Bitmap {
    val image=android.graphics.Bitmap.createBitmap(100,100,android.graphics.Bitmap.Config.ARGB_8888)
    val canvas=android.graphics.Canvas(image);val paint=android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG)
    paint.color=android.graphics.Color.WHITE;canvas.drawCircle(50f,50f,48f,paint)
    paint.color=android.graphics.Color.rgb(0,122,255);canvas.drawCircle(50f,50f,40f,paint)
    paint.color=android.graphics.Color.WHITE;canvas.drawCircle(50f,50f,13f,paint)
    return image
}
internal data class PoiFocus(val id:String,val lat:Double,val lng:Double,val name:String,val token:Long=System.nanoTime())

@Composable internal fun PoiDiscoveryDialog(store:BuddyStore,onDismiss:()->Unit,requester:suspend(JSONObject)->JSONObject={store.searchPoi(it)},onAdd:(JSONObject)->Unit={store.addPoi(it)},googleMapConfigured:Boolean=BuildConfig.MAPS_CONFIGURED) {
    var showInsights by remember{mutableStateOf(false)}
    var query by remember{mutableStateOf("")};var category by remember{mutableStateOf("all")}
    var ticketSummaries by remember{mutableStateOf(JSONObject())}
    var focus by remember{mutableStateOf<PoiFocus?>(null)}
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
    LaunchedEffect(visiblePlaces.map{it.optString("id")},store.travelDataSource,store.mapProvider){
        ticketSummaries=JSONObject();val route=store.travelDataSource;val provider=store.mapProvider
        if(visiblePlaces.isNotEmpty())try{
            val points=JSONArray();visiblePlaces.take(80).forEach{points.put(JSONObject().put("id",it.getString("id")).put("name",it.getString("name")).put("lat",it.getDouble("lat")).put("lng",it.getDouble("lng")))}
            val response=store.socialRequest("activities/summaries",JSONObject().put("places",points))
            ensureActive();if(route==store.travelDataSource&&provider==store.mapProvider&&response.optString("dataSource")==route)ticketSummaries=response.optJSONObject("summaries") ?: JSONObject()
        }catch(e:CancellationException){throw e}catch(e:Exception){/* Optional reference unavailable; no fabricated price. */}
    }
    val source=if(store.travelDataSource!="direct")"Open travel data / Zetaris"else if(category=="gems")"Open travel data / local"else when(store.mapProvider){MapProvider.OSM->openSource;MapProvider.GOOGLE->"Google Places";MapProvider.SCHEMATIC->"Offline overview · place search unavailable"}
    fun search(){
        searchJob?.cancel();places=emptyList();selected=null;focus=null;detail=null;error=null;hasSearched=true;busy=false;resultProvider=null
        summary=null;val provider=store.mapProvider;val route=store.travelDataSource
        if(provider==MapProvider.SCHEMATIC){error="Offline schematic does not provide location search, please switch to another base map.";return}
        if(category!="gems"&&route=="direct"&&provider==MapProvider.GOOGLE&&!googleMapConfigured){error="Google Maps has no API key. Please choose another map.";return}
        val input=JSONObject().put("query",query).put("category",category).put("lat",center.first).put("lng",center.second).put("radius",cameraRequest?.radius ?: if(category in listOf("gems","all"))5000 else 1500).put("provider",if(category!="gems"&&provider==MapProvider.GOOGLE&&route=="direct")"google"else "osm").put("language",AppLanguage.ENGLISH.code)
        busy=true
        searchJob=scope.launch{try{
            val result=withTimeout(30000){requester(input)}
            ensureActive();if(store.mapProvider!=provider||store.travelDataSource!=route)return@launch
            val found=result.array("places").objects()
            require(category=="gems"||route!="direct"||provider!=MapProvider.GOOGLE || found.all{it.optString("source")=="Google Places"}){"Search source inconsistency. Please update backend and retry."}
            summary=if(result.optBoolean("truncated"))"Showing some results. Enter a name or choose a category."else result.optString("message").takeIf{it.isNotBlank()};places=found;resultProvider=provider;resultDataSource=route;attribution=result.optString("attribution",if(provider==MapProvider.GOOGLE)"Google Maps"else "Overture Maps Foundation")
            if(category=="gems"||provider==MapProvider.OSM||route!="direct"){openSource=result.optString("source","Open location data");dataVersion=result.optJSONObject("metadata")?.optString("release") ?: ""}
        }catch(e:CancellationException){if(e is TimeoutCancellationException)error="The places service timed out. Please retry."else throw e}catch(e:Exception){error=e.message ?: "Search failed."}finally{if(isActive)busy=false}}
    }
    fun focusPlace(p:JSONObject){center=p.getDouble("lat") to p.getDouble("lng");zoom=17.0;selected=p.getString("id");focus=PoiFocus(selected!!,p.getDouble("lat"),p.getDouble("lng"),p.optString("name"))}
    fun moved(point:Pair<Double,Double>,value:Double){
        center=point;zoom=value
        val next=PoiViewport(point.first,point.second,value)
        if(next.differs(cameraRequest)){searchJob?.cancel();places=emptyList();selected=null;focus=null;cameraRequest=next}
    }
    LaunchedEffect(cameraRequest){if(cameraRequest!=null){delay(800);search()}}
    LaunchedEffect(category,store.mapProvider,store.travelDataSource){
        cameraRequest=null
        searchJob?.cancel();places=emptyList();selected=null;focus=null;detail=null;error=null;busy=false;hasSearched=false;resultProvider=null;attribution="";dataVersion=""
        if(store.mapProvider!=MapProvider.SCHEMATIC)search()
    }
    Dialog(onDismissRequest=onDismiss,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Surface(Modifier.fillMaxSize()) {Column(Modifier.fillMaxSize().systemBarsPadding().padding(12.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Text(ui("Search places on the map"),Modifier.weight(1f),style=MaterialTheme.typography.titleMedium);TextButton(onClick=onDismiss){Text(ui("Done"))}}
            Row(verticalAlignment=Alignment.CenterVertically){OutlinedTextField(query,{query=it},Modifier.weight(1f),placeholder={Text(ui("Place name (optional)"))},singleLine=true);TextButton(onClick=::search,enabled=!busy){Text(ui("Search"))}}
            Row(Modifier.horizontalScroll(rememberScrollState())){PoiDiscoveryCategory.groups.forEach{(id,label)->TextButton(onClick={category=id}){Text((if(id==PoiDiscoveryCategory.parent(category))"✓ "else "")+ui(label))}}}
            if(PoiDiscoveryCategory.filters(category).isNotEmpty())Row(verticalAlignment=Alignment.CenterVertically){
                Small(ui("Subcategories"))
                Row(Modifier.horizontalScroll(rememberScrollState())){
                    if(PoiDiscoveryCategory.parent(category)!="all")FilterChip(selected=category==PoiDiscoveryCategory.parent(category),onClick={category=PoiDiscoveryCategory.parent(category)},label={Text(ui("All"))})
                    PoiDiscoveryCategory.filters(category).forEach{(id,label,_)->FilterChip(selected=category==id,onClick={category=id},label={Text(ui(label))},modifier=Modifier.testTag("poi-category-"+id))}
                }
            }
            Row{MapProvider.entries.filter{store.mapLayerIds?.contains(it.name.lowercase()) ?: true}.forEach{provider->FilterChip(selected=store.mapProvider==provider,onClick={store.chooseMapProvider(provider)},label={Text(provider.label,fontSize=10.sp)})}}
            Small(ui("Places: {0}",ui(source)))
            TextButton(onClick={
                val points=JSONArray().put(JSONObject().put("id","center").put("name","Search center").put("lat",center.first).put("lng",center.second))
                visiblePlaces.take(20).forEach{p->points.put(JSONObject().put("id",p.optString("id")).put("name",p.optString("name")).put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng")))}
                store.refreshInsights(points);showInsights=true
            }){Text("Transportation・Crowds・Costs")}

            when(store.mapProvider) {
                MapProvider.OSM->PoiOsmMap(visiblePlaces,onCenter={moved(it,zoom)},onSelect={id->visiblePlaces.firstOrNull{it.optString("id")==id}?.let{focusPlace(it)}},Modifier.fillMaxWidth().height(230.dp),initialCenter=center,initialZoom=zoom,focus=focus,onZoom={moved(center,it)},insightMarkers=(store.insights?.optJSONObject("traffic")?.array("items")?.objects() ?: emptyList())+(store.insights?.array("pricePlaces")?.objects() ?: emptyList()))
                MapProvider.GOOGLE->if(BuildConfig.MAPS_CONFIGURED)PoiGoogleMap(visiblePlaces,center,zoom,focus=focus,onCamera={point,z->moved(point,z)},onSelect={id->visiblePlaces.firstOrNull{it.optString("id")==id}?.let{focusPlace(it)}},insightMarkers=(store.insights?.optJSONObject("traffic")?.array("items")?.objects() ?: emptyList())+(store.insights?.array("pricePlaces")?.objects() ?: emptyList()))else Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text(ui("Google Maps has no API key. Please choose another map."))}
                MapProvider.SCHEMATIC->Box(Modifier.fillMaxWidth().height(230.dp),contentAlignment=Alignment.Center){Text(ui("Choose an online map to move the search area."))}
            }
            Small(if(category=="all")"All categories · within 5 km · imported areas only"else if(category=="gems")"Community discoveries · within 5 km · popularity unverified"else if(store.mapProvider==MapProvider.GOOGLE&&store.travelDataSource=="direct")"Google Places · search updates as you move the map · up to 5 km"else "Search updates with the category and map · within 1.5 km")
            summary?.let{Small(ui(it))}
            if(busy){LinearProgressIndicator(Modifier.fillMaxWidth());Small("Searching nearby places…")}
            error?.let{Text("Place data temporarily unavailable:"+it,color=Color.Red,fontSize=12.sp);Small("The map is available, but places could not be loaded. This does not mean there are no places nearby.");TextButton(onClick=::search){Text(ui("Search again"))}}
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                if(visiblePlaces.isEmpty()&&!busy&&error==null&&hasSearched)Small("No matching places here. Clear the name, change category, or move the map and search again.")
                visiblePlaces.sortedBy{if(it.optString("id")==selected)0 else 1}.forEach{p->Column(Modifier.fillMaxWidth().background(if(p.optString("id")==selected)Mint else Color.White).clickable{focusPlace(p)}.padding(8.dp)) {
                    Text((if(p.optString("category")=="美食")"🍴 "else if(p.optString("category")=="設施")"🚻 "else if(p.optString("category")=="住宿")"🛏 "else if(p.optString("category")=="店家")"🛍 "else "📍 ")+p.optString("name"),color=if(p.optString("category")=="美食")Color(0xffa65d12)else Green)
                    Small((p.optString("ratingLabel").takeIf{it.isNotBlank()}?.plus(" · ") ?: "")+(ticketSummaries.optJSONObject(p.optString("id"))?.let{ticketSummaryLabel(it)} ?: p.optString("priceLabel").takeIf{it.isNotBlank()} ?: ui("Cost unknown")))
                    Small(ui(p.optString("category"))+" · "+(p.optJSONArray("sources")?.strings()?.joinToString(" / ") ?: p.optString("source")))
                    Text(p.optString("description"),fontSize=12.sp,maxLines=2)
                    Row{TextButton(onClick={focusPlace(p);detail=p}){Text(ui("Details"))};Spacer(Modifier.weight(1f));val added=store.poiToolbox.any{it.optString("id")==p.optString("id")};if(p.optString("source")=="Google Places")Small("Google places are not imported into the local demo timeline")else TextButton(onClick={if(added)store.removePoi(p.optString("id"))else{store.addPoi(p);onAdd(p)}}){Text(ui(if(added)"Remove from toolbox"else "+ Toolbox"))}}
                }}
            }
            if(store.mapProvider==MapProvider.OSM)Column {
                Row {
                TextButton(onClick={uri.openUri("https://www.openstreetmap.org/copyright")},modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text(ui("Map © OpenStreetMap contributors · ODbL"),fontSize=10.sp)}
                TextButton(onClick={uri.openUri(if(category=="gems")"https://en.wikivoyage.org/wiki/Wikivoyage:Copyleft"else if(attribution.contains("OpenStreetMap"))"https://www.openstreetmap.org/copyright"else "https://docs.overturemaps.org/attribution/")},modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text(attribution.ifBlank{"Overture Maps Foundation"},fontSize=10.sp)}
                }
                if(dataVersion.isNotBlank())Text(ui("Local place snapshot: {0}",dataVersion),fontSize=10.sp)
            }
            else if(store.travelDataSource!="direct") {Small(attribution.ifBlank{"Overture Maps Foundation"});if(dataVersion.isNotBlank())Small(ui("Local place snapshot: {0}",dataVersion))}
            else if(store.mapProvider==MapProvider.GOOGLE)Small(attribution.ifBlank{"Google Maps"})
        }}
    }
    if(showInsights){
        val insightPoints=JSONArray().put(JSONObject().put("id","center").put("name","Search center").put("lat",center.first).put("lng",center.second))
        visiblePlaces.take(20).forEach{p->insightPoints.put(JSONObject().put("id",p.optString("id")).put("name",p.optString("name")).put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng")))}
        TravelInsightsDialog(store,insightPoints){showInsights=false}
    }
    detail?.let{p->SchedulePlaceDetail(p,store){detail=null}}
}
@Composable private fun PoiOsmMap(places:List<JSONObject>,onCenter:(Pair<Double,Double>)->Unit,onSelect:(String)->Unit,modifier:Modifier,initialCenter:Pair<Double,Double>,initialZoom:Double,onZoom:(Double)->Unit,focus:PoiFocus?=null,insightMarkers:List<JSONObject> = emptyList()) {
    val context=LocalContext.current;val lifecycle=LocalLifecycleOwner.current.lifecycle
    var map by remember{mutableStateOf<MapLibreMap?>(null)}
    var userGesture by remember{mutableStateOf(false)}
    var appliedFocus by remember{mutableStateOf<Long?>(null)}
    val currentCenter by rememberUpdatedState(onCenter);val currentSelect by rememberUpdatedState(onSelect);val currentZoom by rememberUpdatedState(onZoom)
    val view=remember{OsmNetwork.initialize(context);MapView(context).apply{onCreate(null);getMapAsync{ready->
        ready.uiSettings.isLogoEnabled=false;ready.uiSettings.isAttributionEnabled=false
        ready.moveCamera(CameraUpdateFactory.newLatLngZoom(LatLng(initialCenter.first,initialCenter.second),initialZoom))
        ready.addOnCameraMoveStartedListener{reason->userGesture=reason==1}
        ready.addOnCameraIdleListener{if(userGesture){userGesture=false;ready.cameraPosition.target?.let{currentCenter(it.latitude to it.longitude)};currentZoom(ready.cameraPosition.zoom)}}
        ready.setStyle(Style.Builder().fromJson(OSM_STYLE)){map=ready}
    }}}
    DisposableEffect(view,lifecycle){val observer=LifecycleEventObserver{_,event->when(event){Lifecycle.Event.ON_START->view.onStart();Lifecycle.Event.ON_RESUME->view.onResume();Lifecycle.Event.ON_PAUSE->view.onPause();Lifecycle.Event.ON_STOP->view.onStop();else->Unit}};view.onStart();view.onResume();lifecycle.addObserver(observer);onDispose{lifecycle.removeObserver(observer);view.onPause();view.onStop();view.onDestroy()}}
    LaunchedEffect(map,places,focus,insightMarkers.toString()){map?.let{ready->
        val markerIds=mutableMapOf<Long,String>()
        ready.setOnMarkerClickListener{marker->markerIds[marker.id]?.let{currentSelect(it)};true}
        ready.clear()
        insightMarkers.forEach{p->ready.addMarker(MarkerOptions().position(LatLng(p.getDouble("lat"),p.getDouble("lng"))).title(p.optString("title")).icon(IconFactory.getInstance(context).fromBitmap(itineraryBadge(if(p.optString("kind")=="price")"$"else "!"))))}
        var selectedMarker:org.maplibre.android.annotations.Marker?=null
        places.forEach{p->val selected=p.optString("id")==focus?.id
            val marker=ready.addMarker(MarkerOptions().position(LatLng(p.getDouble("lat"),p.getDouble("lng"))).icon(IconFactory.getInstance(context).fromBitmap(if(selected)focusedPoiIcon()else itineraryBadge(when(p.optString("category")){"美食","Food"->"F";"設施","Facilities"->"WC";"住宿","Stays"->"H";"店家","Shops"->"Sh";else->"P"}))).title(p.optString("name")))
            markerIds[marker.id]=p.getString("id")
            if(selected)selectedMarker=marker
        }
        if(focus!=null&&selectedMarker!=null&&appliedFocus!=focus.token){
            appliedFocus=focus.token;userGesture=false
            ready.moveCamera(CameraUpdateFactory.newLatLngZoom(LatLng(focus.lat,focus.lng),17.0))
            ready.selectMarker(selectedMarker!!)
        }
    }}
    AndroidView(factory={view},modifier=modifier)
}

@Composable private fun PoiGoogleMap(places:List<JSONObject>,center:Pair<Double,Double>,zoom:Double,onCamera:(Pair<Double,Double>,Double)->Unit,onSelect:(String)->Unit,focus:PoiFocus?=null,insightMarkers:List<JSONObject> = emptyList()) {
    val camera=rememberCameraPositionState()
    val currentCamera by rememberUpdatedState(onCamera)
    var loaded by remember{mutableStateOf(false)}
    LaunchedEffect(Unit){camera.move(GoogleCameraUpdate.newLatLngZoom(GoogleLatLng(center.first,center.second),zoom.toFloat()))}
    LaunchedEffect(focus?.token,loaded){if(loaded&&focus!=null)camera.move(GoogleCameraUpdate.newLatLngZoom(GoogleLatLng(focus.lat,focus.lng),17f))}
    LaunchedEffect(camera,loaded){if(loaded)snapshotFlow{camera.isMoving}.collect{moving->if(!moving&&camera.cameraMoveStartedReason==CameraMoveStartedReason.GESTURE)currentCamera(camera.position.target.latitude to camera.position.target.longitude,camera.position.zoom.toDouble())}}
    GoogleMap(Modifier.fillMaxWidth().height(230.dp),cameraPositionState=camera,onMapLoaded={loaded=true}) {
        insightMarkers.forEach{p->Marker(state=MarkerState(position=GoogleLatLng(p.getDouble("lat"),p.getDouble("lng"))),title=p.optString("title"),snippet=p.optString("summary"))}
        places.forEach{p->key(p.getString("id")){
            val state=rememberMarkerState(position=GoogleLatLng(p.getDouble("lat"),p.getDouble("lng")))
            val selected=p.getString("id")==focus?.id
            LaunchedEffect(selected,focus?.token){if(selected)state.showInfoWindow()else state.hideInfoWindow()}
            Marker(state=state,title=p.getString("name"),zIndex=if(selected)1000f else 0f,icon=BitmapDescriptorFactory.defaultMarker(if(selected)BitmapDescriptorFactory.HUE_AZURE else BitmapDescriptorFactory.HUE_ORANGE),onClick={onSelect(p.getString("id"));true})
        }}
    }
}

@Composable internal fun PlaceContact(p:JSONObject) {
    val uri=LocalUriHandler.current
    if(p.optString("phone").isNotBlank())Text(ui("Phone: ")+p.optString("phone"))
    val website=p.optString("website")
    if(website.startsWith("https://")||website.startsWith("http://"))TextButton(onClick={uri.openUri(website)}){Text(ui("Official website"))}
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
            if(photos.isEmpty())message="No open photos available nearby."
        }catch(e:CancellationException){throw e}catch(e:Exception){message="Photos could not load. Please try again."}finally{loading=false}
    }
    private suspend fun ensureActivePhotoRequest(){kotlin.coroutines.coroutineContext.ensureActive()}
}
@Composable internal fun PlaceMedia(p:JSONObject,store:BuddyStore,requester:suspend(JSONObject)->JSONObject={store.poiMedia(it)}) {
    val scope=rememberCoroutineScope();val uri=LocalUriHandler.current
    val media=remember(p.optString("id"),store.travelDataSource){PlaceMediaState(p.array("photos").objects())}
    val request=remember(p.optString("id")){JSONObject().put("id",p.optString("id")).put("name",p.optString("name")).put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng")).put("wikidata",p.optString("wikidata")).put("poiKind",p.optString("poiKind")).put("category",p.optString("category"))}
    LaunchedEffect(media){if(media.photos.isEmpty())media.load(request,requester)}
    Text(ui("Photos & street view"),style=MaterialTheme.typography.titleSmall)
    Small(ui(if(p.optBoolean("hiddenGem"))"Photos retain author credits and licenses. Nearby photos may show a different place."else "Photos within 100 m; not verified photos of this business or its interior."))
    TextButton(onClick={scope.launch{media.load(request,requester)}},enabled=!media.loading,modifier=Modifier.testTag("place-media-retry")){Text(ui(if(media.photos.isEmpty())"Load nearby photos"else "Reload photos"))}
    if(media.loading)LinearProgressIndicator(Modifier.fillMaxWidth())
    media.message?.let{Small(ui(it))}
    media.photos.forEach{photo->
        CommonsPhoto(photo.optString("url"))
        Text(photo.optString("title"),fontSize=12.sp)
        Small(photo.optString("artist")+" · "+photo.optString("license"))
        if(photo.optString("matchKind")=="exact-place"||photo.optString("matchKind")=="guide-file")Small("✓ Matched spot real photos")
        else if(photo.optString("matchKind")=="nearby-unverified")Small("Nearby photo; place match unverified")
        TextButton(onClick={uri.openUri(photo.getString("pageUrl"))}){Text(ui("Photo source & license"))}
    }
    TextButton(onClick={uri.openUri("https://www.mapillary.com/app/?lat=${p.optDouble("lat")}&lng=${p.optDouble("lng")}&z=17")}){Text(ui("Open nearby street view (external map)"))}
    Small(ui("Street view coverage depends on the provider; a panorama of this business may not be available."))
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
    if(photo!=null)androidx.compose.foundation.Image(bitmap=photo.asImageBitmap(),contentDescription=ui("Nearby location photo"),modifier=Modifier.fillMaxWidth().height(170.dp),contentScale=androidx.compose.ui.layout.ContentScale.Crop)
    else if(failed)Small(ui("Photo unavailable"))else CircularProgressIndicator()
}

@Composable private fun ticketSummaryLabel(summary:JSONObject):String {
    val amount=summary.getDouble("amount");val basis=summary.optString("basis")
    if(amount==0.0)return ui(if(basis=="general_admission")"General admission free"else "Activity free")
    val money=(if(summary.optString("currency")=="AUD")"A$"else summary.optString("currency")+" ")+(if(amount%1.0==0.0)"%.0f"else "%.2f").format(amount)
    return ui(if(basis=="guided_tour")"Adult tour from {0}"else if(basis=="combo")"Adult package from {0}"else "Adult ticket from {0}",money)
}
