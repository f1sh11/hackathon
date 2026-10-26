package com.sydneybuddy.travel

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.ui.semantics.selected
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.*
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.time.LocalDate
import java.time.YearMonth
import java.time.ZoneId

val Muted = Color(0xFF6B7D73)

@Composable fun TripArtwork() {
    Box(Modifier.fillMaxWidth().height(145.dp).clip(RoundedCornerShape(20.dp)).background(Brush.linearGradient(listOf(Mint,Color(0xFFADD9E6))))) {
        Icon(Icons.Outlined.Sailing,null,Modifier.align(Alignment.CenterEnd).padding(20.dp).size(84.dp),tint=Color.White)
        Text("SYDNEY",Modifier.align(Alignment.BottomStart).padding(22.dp),fontSize=25.sp,fontWeight=FontWeight.Black,letterSpacing=4.sp,color=Green)
    }
}

@Composable fun IosNavigation(store: BuddyStore) {
    val items=listOf(Triple(1,"Trips",Icons.Outlined.Luggage),Triple(0,"Map",Icons.Outlined.Map),Triple(3,"Expenses",Icons.Outlined.CreditCard),Triple(4,"Me",Icons.Outlined.AccountCircle))
    Surface(color=Color.White.copy(alpha=.98f),shadowElevation=2.dp) {
        Row(Modifier.fillMaxWidth().navigationBarsPadding().height(66.dp),verticalAlignment=Alignment.CenterVertically) {
            items.forEach { (index,label,icon) ->
                Column(Modifier.weight(1f).fillMaxHeight().clickable(enabled=!store.busy){store.tab=index}.padding(top=10.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(4.dp)) {
                    Icon(icon,null,Modifier.size(24.dp),tint=if(store.tab==index) Green else Muted)
                    Text(ui(label),color=if(store.tab==index) Green else Muted,fontSize=11.sp,fontWeight=if(store.tab==index) FontWeight.Bold else FontWeight.Normal)
                }
            }
        }
    }
}

@Composable fun Buddy(size: Int) {
    Canvas(Modifier.size(size.dp,(size*1.15f).dp).semantics{contentDescription="Koko travel companion"}) {
        val u=this.size.width
        fun point(x:Float,y:Float)=Offset(u*x,u*y)
        fun round(color:Color,x:Float,y:Float,w:Float,h:Float,r:Float)=drawRoundRect(color,point(x,y),Size(u*w,u*h),CornerRadius(u*r))
        drawOval(Green.copy(alpha=.1f),point(.1f,.98f),Size(u*.8f,u*.1f))
        rotate(14f,point(.55f,.16f)){round(Green,.52f,.08f,.055f,.2f,.025f)}
        drawCircle(Mint,u*.06f,point(.58f,.055f));drawCircle(Green.copy(alpha=.4f),u*.06f,point(.58f,.055f),style=Stroke(u*.014f))
        drawRoundRect(Brush.linearGradient(listOf(Mint,Color.White)),point(.18f,.54f),Size(u*.64f,u*.43f),CornerRadius(u*.18f))
        rotate(24f,point(.3f,.94f)){round(Green,.235f,.83f,.13f,.22f,.06f)}
        rotate(-24f,point(.7f,.94f)){round(Green,.635f,.83f,.13f,.22f,.06f)}
        rotate(-28f,point(.13f,.6f)){round(Green.copy(alpha=.85f),.06f,.45f,.12f,.30f,.06f)}
        rotate(-55f,point(.89f,.5f)){round(Green.copy(alpha=.85f),.83f,.35f,.12f,.3f,.06f)}
        drawRoundRect(Brush.linearGradient(listOf(Color.White,Mint)),point(.09f,.20f),Size(u*.82f,u*.61f),CornerRadius(u*.25f))
        drawRoundRect(Green.copy(alpha=.28f),point(.09f,.20f),Size(u*.82f,u*.61f),CornerRadius(u*.25f),style=Stroke(u*.018f))
        round(Ink,.185f,.31f,.63f,.37f,.16f)
        round(Color.White,.325f,.425f,.055f,.105f,.025f);round(Color.White,.61f,.425f,.055f,.105f,.025f)
        drawArc(Color.White,0f,180f,false,point(.43f,.545f),Size(u*.14f,u*.07f),style=Stroke(u*.014f,cap=StrokeCap.Round))
        drawCircle(Color(0xFFFFBF6D),u*.045f,point(.5f,.87f))
    }
}

@Composable fun Action(text: String, enabled: Boolean = true, onClick: () -> Unit) {
    Box(Modifier.fillMaxWidth().padding(bottom=5.dp).background(Ink,RoundedCornerShape(22.dp))) {
        Button(onClick=onClick,enabled=enabled,modifier=Modifier.fillMaxWidth().offset(y=(-5).dp).heightIn(min=58.dp),shape=RoundedCornerShape(22.dp),contentPadding=PaddingValues(16.dp)) {
            Text(ui(text),fontSize=16.sp,fontWeight=FontWeight.Bold);Spacer(Modifier.width(12.dp));Icon(Icons.Outlined.ArrowForward,null,Modifier.size(21.dp))
        }
    }
}

fun choiceIcon(text:String):ImageVector = when {
    "轻松" in text || "Easy" in text -> Icons.Outlined.Coffee
    "步" in text || "活力" in text || "刚刚好" in text || "steps" in text || "energy" in text || "Just right" in text -> Icons.Outlined.DirectionsWalk
    "天" in text || "day" in text -> Icons.Outlined.WbSunny
    "A$" in text -> Icons.Outlined.AccountBalanceWallet
    "车站" in text || "Station" in text -> Icons.Outlined.Train
    "码头" in text || "Quay" in text -> Icons.Outlined.DirectionsBoat
    ":" in text -> Icons.Outlined.Schedule
    "美食" in text || "Food" in text -> Icons.Outlined.Restaurant
    "自然" in text || "Nature" in text -> Icons.Outlined.Park
    "艺术" in text || "Art" in text -> Icons.Outlined.Palette
    "文化" in text || "Culture" in text -> Icons.Outlined.AccountBalance
    "城市" in text || "City" in text -> Icons.Outlined.LocationCity
    else -> Icons.Outlined.AutoAwesome
}

@Composable fun Choice(text:String,selected:Boolean,enabled:Boolean=true,onClick:()->Unit) {
    val parts=text.split(" · ",limit=2)
    val detail=parts.getOrNull(1) ?: when(text){"1 day"->"A light city exploration";"2 days"->"Leave a little time to discover slowly";"3 days"->"Leave room for scenery and rest";"Circular Quay"->"Start from Sydney Harbour";"Central Station"->"Start from city transit hub";else->""}
    Column(Modifier.fillMaxWidth().padding(bottom=if(selected)4.dp else 0.dp).background(if(selected)Green.copy(alpha=.24f) else Color.Transparent,RoundedCornerShape(22.dp))) {
        Row(Modifier.fillMaxWidth().offset(y=if(selected)(-3).dp else 0.dp).clip(RoundedCornerShape(22.dp)).background(if(selected)Mint else Color.White)
            .border(if(selected)2.dp else 1.dp,if(selected)Green else Green.copy(alpha=.13f),RoundedCornerShape(22.dp))
            .clickable(enabled=enabled,onClick=onClick).padding(horizontal=18.dp,vertical=20.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(16.dp)) {
            Icon(choiceIcon(text),null,Modifier.size(28.dp),tint=Green)
            Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(6.dp)) {Text(ui(parts[0]),fontSize=17.sp,fontWeight=FontWeight.Bold);if(detail.isNotBlank())Text(ui(detail),fontSize=12.sp,color=Muted)}
            Icon(if(selected)Icons.Outlined.CheckCircle else Icons.Outlined.RadioButtonUnchecked,null,Modifier.size(23.dp),tint=if(selected)Green else Muted)
        }
    }
}

fun timeLabel(minute: Int): String {
    val value = minute.coerceIn(0, 1440)
    return "%02d:%02d".format(value / 60, value % 60)
}

fun durationLabel(startMinute: Int, endMinute: Int): String {
    val duration = (endMinute - startMinute).coerceAtLeast(0)
    return if (duration % 60 == 0) "${duration / 60} hr" else "${duration / 60} hr ${duration % 60} min"
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable fun TimeDropdown(label: String, minute: Int, options: List<Int>, enabled: Boolean, onSelected: (Int) -> Unit, modifier: Modifier = Modifier) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(expanded=expanded, onExpandedChange={ if(enabled) expanded=!expanded }, modifier=modifier) {
        OutlinedTextField(
            value=timeLabel(minute), onValueChange={}, readOnly=true, enabled=enabled, singleLine=true,
            label={Text(label)},
            trailingIcon={ExposedDropdownMenuDefaults.TrailingIcon(expanded=expanded)},
            modifier=Modifier.menuAnchor().fillMaxWidth(),
            colors=OutlinedTextFieldDefaults.colors(focusedBorderColor=Green,focusedLabelColor=Green,focusedTrailingIconColor=Green)
        )
        ExposedDropdownMenu(expanded=expanded,onDismissRequest={expanded=false}) {
            options.forEach { value ->
                DropdownMenuItem(text={Text(timeLabel(value))},onClick={onSelected(value);expanded=false})
            }
        }
    }
}

@Composable fun CustomTimeRange(startMinute: Int, endMinute: Int, enabled: Boolean, onRangeChange: (Int, Int) -> Unit) {
    val earliest = 6 * 60
    val latest = 22 * 60
    val interval = 30
    val start = startMinute.coerceIn(earliest, latest - interval)
    val end = endMinute.coerceIn(start + interval, latest)
    val slots = (earliest..latest step interval).toList()

    Surface(shape=RoundedCornerShape(22.dp), color=Color.White, border=BorderStroke(1.dp, Green.copy(alpha=.16f))) {
        Column(Modifier.fillMaxWidth().padding(18.dp), verticalArrangement=Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically) {
                Icon(Icons.Outlined.Tune, null, Modifier.size(22.dp), tint=Green)
                Spacer(Modifier.width(9.dp))
                Column(Modifier.weight(1f)) {
                    Text("Custom time range", fontWeight=FontWeight.Bold, fontSize=15.sp)
                    Text("30-minute intervals · allow at least 30 minutes", fontSize=11.sp, color=Muted)
                }
                Text("${durationLabel(start, end)}", fontSize=12.sp, color=Green, fontWeight=FontWeight.Bold)
            }
            Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                TimeDropdown("Departure time",start,slots.filter{it<=end-interval},enabled,{newStart->onRangeChange(newStart,end)},Modifier.weight(1f))
                Icon(Icons.Outlined.ArrowForward,null,Modifier.size(20.dp),tint=Green)
                TimeDropdown("Return time",end,slots.filter{it>=start+interval},enabled,{newEnd->onRangeChange(start,newEnd)},Modifier.weight(1f))
            }
            Text("Choose from 06:00–22:00 in 30-minute intervals.",fontSize=11.sp,color=Muted)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable fun HomeScreen(store:BuddyStore,suppliedOverview:ItineraryOverview?=null) {
    val uri=androidx.compose.ui.platform.LocalUriHandler.current
    val nearby=rememberNearbyPoi(store)
    var nearbyDetail by remember{mutableStateOf<org.json.JSONObject?>(null)}
    nearbyDetail?.let{point->SchedulePlaceDetail(point,store){nearbyDetail=null}}

    var poiSearch by remember{mutableStateOf(false)}
    if(poiSearch)PoiDiscoveryDialog(store,onDismiss={poiSearch=false})
    var detail by rememberSaveable {mutableStateOf(false)}
    var journey by rememberSaveable {mutableStateOf(false)}
    var expandedMap by rememberSaveable {mutableStateOf(false)}
    var navigation by rememberSaveable {mutableStateOf(false)}
    var chat by rememberSaveable {mutableStateOf(false)}
    var expanded by rememberSaveable {mutableStateOf(false)}
    var showInsights by rememberSaveable {mutableStateOf(false)}
    val trip=store.trip
    LaunchedEffect(trip?.optString("id"),store.travelDataSource){if(trip!=null)store.refreshWeather()}
    val day=store.day
    LaunchedEffect(day.toString(),store.travelDataSource,store.mapProvider){store.refreshInsights()}
    if(showInsights)TravelInsightsDialog(store){showInsights=false}
    val overview=remember(day.toString(),trip?.optString("mode"),store.mapProvider,suppliedOverview){suppliedOverview ?: ItineraryOverview()}
    DisposableEffect(overview){onDispose{overview.close()}}
    LaunchedEffect(overview){if(day!=null&&trip?.optString("mode")!="live"&&store.mapProvider==MapProvider.OSM)overview.load(day,store.base)}
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val compact=maxHeight<650.dp
        RouteMap(day,Modifier.fillMaxSize(),true,store.mapProvider,trip?.optString("mode")=="live",store.mapViewport,{store.mapViewport=it;nearby.move(it.lat,it.lng,it.zoom)},overview.route,sections=overview.plan?.sections ?: emptyList(),insightMarkers=(store.insights?.optJSONObject("traffic")?.array("items")?.objects() ?: emptyList())+(store.insights?.array("pricePlaces")?.objects() ?: emptyList()),nearbyPlaces=nearby.places,onPoiSelect={nearbyDetail=it})
        Column(Modifier.fillMaxSize().padding(horizontal=18.dp).padding(top=10.dp,bottom=10.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
            Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(8.dp),verticalAlignment=Alignment.CenterVertically){
                TextButton(onClick={poiSearch=true},modifier=Modifier.weight(1f).background(Color.White,CircleShape),contentPadding=PaddingValues(horizontal=16.dp,vertical=12.dp)){Icon(Icons.Outlined.Search,null,Modifier.size(20.dp));Spacer(Modifier.width(8.dp));Text(ui("Search places, food and facilities"),fontSize=13.sp);Spacer(Modifier.weight(1f))}
                var layers by remember{mutableStateOf(false)}
                Box{IconButton(onClick={layers=true},modifier=Modifier.size(46.dp).background(Color.White,CircleShape)){Icon(Icons.Outlined.Layers,"Change map",tint=Green)};DropdownMenu(expanded=layers,onDismissRequest={layers=false}){MapProvider.entries.forEach{provider->DropdownMenuItem(text={Text(ui(provider.label))},onClick={store.chooseMapProvider(provider);layers=false})}}}
            }
            if(trip!=null)MapDaySelector(trip.array("days").objects().map{it.optString("date")},store.dayIndex){store.dayIndex=it}
            if(nearby.busy)Text("Searching nearby locations…",Modifier.background(Color.White,CircleShape).padding(8.dp),fontSize=11.sp)
            nearby.error?.let{TextButton(onClick={poiSearch=true},modifier=Modifier.background(Color.White,CircleShape)){Text("Nearby places could not load · open search to retry",fontSize=11.sp)}}
            Spacer(Modifier.weight(1f))
            Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End,verticalAlignment=Alignment.CenterVertically){
                if(trip!=null)IconButton(onClick={expandedMap=true},modifier=Modifier.size(42.dp).background(Color.White,CircleShape)){Icon(Icons.Outlined.OpenInFull,"Expand map",tint=Green)}
                Spacer(Modifier.width(8.dp));TextButton(onClick={chat=true},modifier=Modifier.background(Color.White,CircleShape)){Icon(Icons.Outlined.AutoAwesome,null,Modifier.size(16.dp));Text(ui("Ask AI"),fontSize=12.sp)}
            }
            Surface(shape=RoundedCornerShape(24.dp),color=Color.White,shadowElevation=4.dp) {
                Column(Modifier.padding(16.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                    Row(verticalAlignment=Alignment.Top){Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(5.dp)){
                        Text(trip?.optString("title") ?: "Start your Sydney trip",fontSize=17.sp,fontWeight=FontWeight.Bold)
                        if(day!=null)Text(ui("{0} stops · {1} steps · A\${2} · return {3}",day.array("stops").length().toString(),"%,d".format(day.optInt("estimatedSteps")),day.optDouble("estimatedCost").toInt().toString(),day.optString("finishTime")),fontSize=11.sp,color=Muted)
                    };if(trip!=null)IconButton(onClick={expanded=!expanded},modifier=Modifier.size(28.dp).testTag("home-info-toggle")){Icon(if(expanded)Icons.Outlined.ExpandMore else Icons.Outlined.MoreHoriz,if(expanded)"Hide trip information"else "Show trip information",tint=Green)}}
                    TextButton(onClick={showInsights=true},contentPadding=PaddingValues(0.dp),modifier=Modifier.testTag("travel-insights-open")){Text(if(store.insightsBusy)"Updating travel information…"else store.insights?.optJSONObject("traffic")?.optString("summary") ?: "Transportation・Crowds・Costs",fontSize=12.sp,maxLines=1)}
                    if(day!=null)HomeTransportStrip(day,overview,onPlan={journey=true},onRetry={overview.load(day,store.base)})
                    if(expanded&&trip!=null)Column(Modifier.fillMaxWidth().heightIn(max=220.dp).verticalScroll(rememberScrollState()).testTag("home-extra-info"),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                        CompactWeather(store)
                        val count=listOf("route","weather","bag").count(store::prepared)
                        Text("3 things before you go · $count/3",fontSize=13.sp,fontWeight=FontWeight.Bold,color=Green)
                        Row(horizontalArrangement=Arrangement.spacedBy(7.dp)){
                            PreparationButton("Check route",Icons.Outlined.Map,store.prepared("route"),Modifier.weight(1f)){store.mark("route");detail=true}
                            PreparationButton("Check weather",Icons.Outlined.Cloud,store.prepared("weather"),Modifier.weight(1f),!store.busy,store::refreshWeather)
                            PreparationButton("Pack bags",Icons.Outlined.Backpack,store.prepared("bag"),Modifier.weight(1f)){store.mark("bag",true)}
                        }
                        TextButton(onClick={journey=true},contentPadding=PaddingValues(0.dp)){Text(ui("Plan transport"))}
                        if(day!=null){
                            Text("Start · ${day.getJSONObject("start").optString("name")}"+day.array("stops").objects().mapIndexed{i,s->" → ${i+1} · ${s.optString("name")} ${s.optString("arrival")}"}.joinToString("")+" → End · ${day.getJSONObject("end").optString("name")}",Modifier.horizontalScroll(rememberScrollState()).semantics{contentDescription="Stop order"},fontSize=11.sp,color=Green)
                            if(trip.optString("mode")!="live"&&store.mapProvider==MapProvider.OSM){TextButton(onClick={overview.load(day,store.base)},enabled=!overview.busy&&overview.route==null,contentPadding=PaddingValues(0.dp)){Text(if(overview.busy)"Loading walking route…"else overview.route?.let{"Walk about ${it.meters.toInt()} m"} ?: "Show walking route",fontSize=12.sp)};overview.message?.let{Small(it)}}
                        }
                    }
                    Row(horizontalArrangement=Arrangement.spacedBy(10.dp)) {
                        if(trip!=null)OutlinedButton(onClick={store.mark("route");detail=true},modifier=Modifier.weight(1f),shape=RoundedCornerShape(13.dp)){Text(ui("View route"))}
                        Button(onClick={if(trip==null)store.newTrip()else navigation=true},modifier=Modifier.weight(1f),shape=RoundedCornerShape(13.dp)){Icon(Icons.Outlined.Navigation,null,Modifier.size(16.dp));Text(ui(if(trip==null)"Plan a trip"else "Start navigation"))}
                    }
                    Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically){Text(ui(store.mapProvider.label),fontSize=9.sp,color=Muted);if(store.mapProvider==MapProvider.OSM)Text(" · © OpenStreetMap contributors",Modifier.clickable{uri.openUri("https://www.openstreetmap.org/copyright")},fontSize=9.sp,color=Green);Spacer(Modifier.weight(1f));Text(ui(if(trip?.optString("mode")=="live")"Google route"else if(store.mapProvider==MapProvider.SCHEMATIC)"Offline overview"else if(overview.route==null)"Stops only"else "Route ready"),fontSize=9.sp,color=Muted)}
                }
            }
        }
    }
    if(expandedMap) Dialog(onDismissRequest={expandedMap=false},properties=androidx.compose.ui.window.DialogProperties(usePlatformDefaultWidth=false)) {
        Box(Modifier.fillMaxSize().background(Color.White).testTag("expanded-itinerary-map")) {
            RouteMap(day,Modifier.fillMaxSize(),true,store.mapProvider,trip?.optString("mode")=="live",overview=overview.route,expanded=true,sections=overview.plan?.sections ?: emptyList())
            Column(Modifier.fillMaxSize().padding(18.dp),verticalArrangement=Arrangement.SpaceBetween) {
                Surface(shape=RoundedCornerShape(18.dp),color=Color.White) {
                    Row(Modifier.fillMaxWidth().padding(12.dp),verticalAlignment=Alignment.CenterVertically) {Text(ui("Full-day itinerary"),modifier=Modifier.weight(1f),fontWeight=FontWeight.Bold);TextButton(onClick={expandedMap=false}){Text(ui("Close map"))}}
                }
                Surface(shape=RoundedCornerShape(18.dp),color=Color.White) {
                    Column(Modifier.padding(16.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
                        Text(ui(if(overview.route==null || store.mapProvider==MapProvider.SCHEMATIC)"Road route not loaded · showing stops" else "Walking route · excludes time at stops"),fontSize=12.sp,color=Green)
                        if(day!=null) {
                            Text(walkingPoints(day).mapIndexed{i,p->"${if(i==0)"S" else if(i==walkingPoints(day).lastIndex)"E" else "$i"} · ${p.name}"}.joinToString(" → "),Modifier.horizontalScroll(rememberScrollState()),fontSize=12.sp)
                            if(trip?.optString("mode")!="live" && store.mapProvider==MapProvider.OSM) {
                                TextButton(onClick={overview.load(day,store.base)},enabled=!overview.busy && overview.route==null){Text(if(overview.busy)"Loading walking route…" else overview.route?.let{"Walk approx. ${it.meters.toInt()} meters · ${kotlin.math.ceil(it.seconds/60).toInt()} minutes"} ?: "Show walking route")}
                                overview.route?.let{Text(it.segments,Modifier.horizontalScroll(rememberScrollState()),fontSize=11.sp,color=Muted)}
                                overview.message?.let{Text(it,fontSize=12.sp)}
                            }
                        }
                    }
                }
            }
        }
    }
    if(journey && day!=null) {
        if(trip?.optString("mode")=="live" && store.mapProvider!=MapProvider.GOOGLE) AlertDialog(onDismissRequest={journey=false},title={Text("Choose Google Maps")},text={Text("Transport planning for this trip requires Google Maps and Google Routes.")},confirmButton={TextButton(onClick={journey=false}){Text(ui("Close"))}})
        else JourneyPlannerDialog(day,store.base,store.mapProvider,{journey=false},{journey=false;navigation=true},contentSource=if(trip?.optString("mode")=="live")"google" else "open",onPlan=overview::accept)
    }
    if(navigation) {
        if(trip?.optString("mode")=="live") AlertDialog(onDismissRequest={navigation=false},text={Text("This trip uses Google Places data. Create a demo trip to test open-source navigation.")},confirmButton={TextButton(onClick={navigation=false}){Text(ui("Close"))}})
        else WalkingNavigationDialog(store){navigation=false}
    }
    if(detail) ModalBottomSheet(onDismissRequest={detail=false},containerColor=Cream,sheetState=rememberModalBottomSheetState(skipPartiallyExpanded=true)) {
        Column(Modifier.fillMaxWidth().fillMaxHeight(.9f).verticalScroll(rememberScrollState()).padding(22.dp),verticalArrangement=Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Heading("Your adventure");Spacer(Modifier.weight(1f));TextButton(onClick={detail=false}){Text(ui("Done"))}}
            trip?.let{Text(it.optString("explanation"));WeatherPanel(store.weather,!store.busy,store::refreshWeather);it.array("days").objects().forEach{d->DaySummary(d)};Evidence(it)}
        }
    }
    if(chat) ModalBottomSheet(onDismissRequest={chat=false},containerColor=Cream,sheetState=rememberModalBottomSheetState(skipPartiallyExpanded=true)) {
        Box(Modifier.fillMaxHeight(.92f)){AgentScreen(store)}
    }
}

@Composable fun PreparationButton(label:String,icon:ImageVector,done:Boolean,modifier:Modifier=Modifier,enabled:Boolean=true,onClick:()->Unit) {
    Column(modifier.clip(RoundedCornerShape(14.dp)).background(if(done)Green else Mint).clickable(enabled=enabled,onClick=onClick).padding(vertical=10.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(4.dp)) {
        Icon(if(done)Icons.Outlined.CheckCircle else icon,null,Modifier.size(24.dp),tint=if(done)Color.White else Green)
        Text(label,fontSize=11.sp,fontWeight=FontWeight.Bold,color=if(done)Color.White else Green)
    }
}

@Composable fun CompactWeather(store:BuddyStore) {
    val weather=store.weather
    val daily=weather?.array("days")?.objects()?.find{it.optString("date")==store.day?.optString("date")}
    Surface(shape=RoundedCornerShape(20.dp),color=Color.White,shadowElevation=2.dp) {
        Column(Modifier.fillMaxWidth().padding(horizontal=15.dp,vertical=12.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically) {
                Icon(if(weather?.optBoolean("rainy")==true)Icons.Outlined.Umbrella else Icons.Outlined.Cloud,null,Modifier.size(21.dp));Spacer(Modifier.width(7.dp))
                Text(ui("Sydney weather"),fontWeight=FontWeight.Bold,fontSize=15.sp,modifier=Modifier.weight(1f))
                IconButton(onClick=store::refreshWeather,enabled=!store.busy,modifier=Modifier.size(26.dp)){Icon(Icons.Outlined.Refresh,"Refresh weather",tint=Green)}
            }
            Text(daily?.let{"${it.optString("date")} · ${it.optString("summary")}"} ?: weather?.optString("summary") ?: "Weather not checked yet",fontSize=12.sp,maxLines=2)
            Text(weather?.optString("source") ?: "",fontSize=10.sp,color=Muted,maxLines=1)
        }
    }
}

@Composable fun TravelCalendar(date:String,onDate:(String)->Unit) {
    val selected=runCatching{LocalDate.parse(date)}.getOrDefault(LocalDate.now(ZoneId.of("Australia/Sydney")))
    var month by remember(selected.year,selected.monthValue){mutableStateOf(YearMonth.from(selected))}
    val today=LocalDate.now(ZoneId.of("Australia/Sydney"))
    Surface(shape=RoundedCornerShape(24.dp),color=Color.White) {
        Column(Modifier.padding(14.dp),verticalArrangement=Arrangement.spacedBy(3.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Text("${month.year}-${month.monthValue}",fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f));IconButton(onClick={month=month.minusMonths(1)}){Icon(Icons.Outlined.ChevronLeft,"Previous month",tint=Green)};IconButton(onClick={month=month.plusMonths(1)}){Icon(Icons.Outlined.ChevronRight,"Next month",tint=Green)}}
            Row {listOf("Mon","Tue","Wed","Thu","Fri","Sat","Sun").forEach{Box(Modifier.weight(1f),contentAlignment=Alignment.Center){Text(it,fontSize=11.sp,color=Muted)}}}
            val offset=month.atDay(1).dayOfWeek.value-1
            val rows=(offset+month.lengthOfMonth()+6)/7
            repeat(rows){row->Row {repeat(7){col->
                val day=row*7+col-offset+1
                Box(Modifier.weight(1f).height(38.dp),contentAlignment=Alignment.Center){if(day in 1..month.lengthOfMonth()){
                    val value=month.atDay(day);val active=value==selected
                    Box(Modifier.size(34.dp).clip(CircleShape).background(if(active)Green else Color.Transparent).clickable(enabled=value>=today){onDate(value.toString())},contentAlignment=Alignment.Center){Text("$day",fontSize=14.sp,color=if(active)Color.White else if(value<today)Muted.copy(alpha=.35f) else Ink,fontWeight=if(active)FontWeight.Bold else FontWeight.Normal)}
                }}
            }}}
        }
    }
}

@Composable fun Onboarding(store:BuddyStore) {
    var step by rememberSaveable {mutableIntStateOf(0)}
    val p=store.profile
    val titles=listOf("Which day to go to Sydney?","How many days for this trip?","What pace would you like?","What moves you most?","How much to prepare daily?","Where to start?","What time would you like to depart and return?","Let's set off!")
    val subtitles=listOf("Pick a day, and we'll take it slow.","Leave time open—no need to plan everything at once.","Pick what's comfortable. No need to step-count challenge.","Can select multiple or temporarily not select.","Set a rough plan first, and you can always change it later.","Pick a convenient starting point.","Leave room for exploration and rest.","Glance at your choices; adjustments are possible after generation.")
    val messages=listOf("Every choice has no standard answer; what suits you is good.","Step 1 complete! How long do you want to stay in Sydney?","No need to rush, go at your own pace.","Let me learn more about your preferences.","Good experience, but also within a comfortable budget.","Halfway there! Just pick a departure point.","Almost done! Please set your departure and return times.","All set! Let me check the weather, distance, and operating hours.")
    Column(Modifier.fillMaxSize().background(Cream)) {
        Row(Modifier.fillMaxWidth().padding(horizontal=18.dp,vertical=16.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(14.dp)) {
            IconButton(onClick={if(step>0)step-- else if(store.saved.isNotEmpty())store.resumeSaved()},enabled=!store.busy,modifier=Modifier.size(24.dp)){Icon(Icons.Outlined.ChevronLeft,"Back one step",tint=Muted)}
            Box(Modifier.weight(1f).height(11.dp).clip(CircleShape).background(Green.copy(alpha=.12f))) {
                Box(Modifier.fillMaxWidth(step/8f).fillMaxHeight().background(Green,CircleShape))
            }
            Text("${step+1}/8",fontSize=12.sp,color=Green,fontWeight=FontWeight.Bold)
        }
        val scroll=rememberScrollState()
        LaunchedEffect(step){scroll.scrollTo(0)}
        Column(Modifier.weight(1f).verticalScroll(scroll).padding(horizontal=24.dp).padding(top=10.dp,bottom=20.dp),verticalArrangement=Arrangement.spacedBy(20.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(12.dp)) {
                Buddy(76)
                Surface(shape=RoundedCornerShape(20.dp),color=Color.White,border=BorderStroke(1.5.dp,Green.copy(alpha=.15f)),modifier=Modifier.weight(1f)){
                    Text(messages[step],Modifier.padding(16.dp),fontSize=14.sp,fontWeight=FontWeight.Bold,lineHeight=21.sp)
                }
            }
            Column(verticalArrangement=Arrangement.spacedBy(8.dp)) {
                Text("Mini adventure · Departure prep",fontSize=12.sp,fontWeight=FontWeight.Bold,color=Green)
                Text(titles[step],fontSize=29.sp,fontWeight=FontWeight.ExtraBold,color=Ink,lineHeight=37.sp)
                Text(subtitles[step],fontSize=14.sp,color=Muted,lineHeight=21.sp)
            }
        when (step) {
            0 -> {
                Surface(shape=RoundedCornerShape(24.dp),color=Mint) {
                    Row(Modifier.fillMaxWidth().padding(22.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(12.dp)) {
                        Icon(Icons.Outlined.Sailing,null,Modifier.size(29.dp),tint=Green)
                        Text("SYDNEY · Sydney",fontWeight=FontWeight.Bold,fontSize=20.sp,color=Green)
                    }
                }
                TravelCalendar(p.date){store.profile=p.copy(date=it)}
            }
            1 -> (1..3).forEach { n -> Choice(if(n==1)"1 day" else "$n days", p.days == n, !store.busy) { store.profile = p.copy(days = n) } }
            2 -> listOf(5000 to "Easy stroll",10000 to "Just right",18000 to "Full of energy").forEach { (n,t) -> Choice("$t · up to about $n steps",p.maxSteps == n,!store.busy) { store.profile=p.copy(maxSteps=n) } }
            3 -> {
                listOf("风景","美食","自然","艺术","文化","城市").chunked(2).forEach{row ->
                    Row(horizontalArrangement=Arrangement.spacedBy(14.dp)) {row.forEach{t ->
                        val selected=t in p.interests
                        Column(Modifier.weight(1f).height(126.dp).clip(RoundedCornerShape(22.dp)).background(if(selected)Mint else Color.White).border(if(selected)2.dp else 1.dp,if(selected)Green else Green.copy(alpha=.12f),RoundedCornerShape(22.dp)).clickable(enabled=!store.busy){store.profile=p.copy(interests=if(selected)p.interests-t else p.interests+t)},horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.Center) {
                            Icon(choiceIcon(t),null,Modifier.size(28.dp),tint=Green);Spacer(Modifier.height(10.dp));Text(ui(t),fontWeight=FontWeight.Bold);Spacer(Modifier.height(8.dp));Icon(if(selected)Icons.Outlined.CheckCircle else Icons.Outlined.RadioButtonUnchecked,null,Modifier.size(17.dp),tint=Green)
                        }
                    }}
                }
            }
            4 -> { listOf(50,100,150).forEach { n -> Choice("A$$n / day",p.budget==n,!store.busy) {store.profile=p.copy(budget=n)} }
                Text("Custom: A$${p.budget}"); Slider(value=p.budget.toFloat(),onValueChange={store.profile=p.copy(budget=(it/10).toInt()*10)},valueRange=0f..300f,steps=29,enabled=!store.busy)
                Small("Estimated activity costs exclude accommodation, flights and some meals.") }
            5 -> { listOf(Endpoint.quay,Endpoint.central).forEach { e -> Choice(e.name,p.start.sameLocation(e),!store.busy){store.profile=p.copy(start=e,end=e)} }; Small("Return to the same point each day. Set start and end points for your next trip in Settings.") }
            6 -> {
                listOf(Triple(600,1080,"10:00–18:00 · An easy day"),Triple(540,1020,"09:00–17:00 · An earlier start"),Triple(780,1080,"13:00–18:00 · Afternoon adventure")).forEach { (s,e,t) ->
                    Choice(t,p.startMinute==s && p.endMinute==e,!store.busy){store.profile=p.copy(startMinute=s,endMinute=e)}
                }
                CustomTimeRange(p.startMinute, p.endMinute, !store.busy) { start, end ->
                    store.profile = p.copy(startMinute=start, endMinute=end)
                }
            }
            else -> { Panel { Text("Sydney · ${p.days} days · up to ${p.maxSteps} steps/day",fontWeight=FontWeight.Bold)
                Text("${p.date} · A$${p.budget}/day"); Text("Depart ${ui(p.start.name)} → return ${ui(p.end.name)}"); Text(p.interests.map{ui(it)}.joinToString(" · ").ifEmpty {"Explore freely"}) }
                OnlineSwitch(store)
                Small("Review your draft first. It is saved when you confirm.") }
        }

        }
        Column(Modifier.fillMaxWidth().padding(horizontal=24.dp).padding(top=10.dp,bottom=16.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(10.dp)) {
            Text(if(step==7)"You decide, Koko helps" else "Can return and edit anytime",fontSize=11.sp,color=Muted)
            Action(if(step==7)"Let Koko plan the route" else "Selected. Let's continue.",!store.busy){store.rememberProfile();if(step<7)step++ else store.generate()}
            if(step==0 && store.saved.isNotEmpty())TextButton(onClick=store::resumeSaved,enabled=!store.busy){Text("Back to saved trips")}
        }
    }
    BackHandler(enabled=step>0 && !store.busy){step--}
}

@Composable internal fun HomeTransportStrip(day:org.json.JSONObject,overview:ItineraryOverview,onPlan:()->Unit,onRetry:()->Unit) {
    Column(verticalArrangement=Arrangement.spacedBy(6.dp)) {
        Row(verticalAlignment=Alignment.CenterVertically){TextButton(onClick=onPlan,contentPadding=PaddingValues(0.dp),modifier=Modifier.heightIn(min=28.dp)){Text(ui("Transport"),fontSize=12.sp,fontWeight=FontWeight.Bold)};Spacer(Modifier.weight(1f));overview.route?.let{Text(ui("{0} min · travel estimate",kotlin.math.ceil(it.seconds/60).toInt().toString()),fontSize=10.sp,color=Muted)}}
        when {
            overview.busy->Text(ui("Loading road route…"),fontSize=11.sp,color=Muted)
            overview.route!=null->Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).testTag("home-transport-legs"),horizontalArrangement=Arrangement.spacedBy(6.dp)){
                overviewTransportLegs(day,overview.route,overview.plan).forEach{leg->Column(Modifier.widthIn(max=220.dp).background(Mint.copy(alpha=.65f),RoundedCornerShape(10.dp)).padding(8.dp),verticalArrangement=Arrangement.spacedBy(4.dp)){Text(ui("{0} · {1} {2} min",leg.label,ui(leg.transport),leg.minutes.toString()),fontSize=11.sp,fontWeight=FontWeight.Bold,color=Green,maxLines=1);Text("${leg.from} → ${leg.to}",fontSize=10.sp,color=Muted,maxLines=1)}}
            }
            overview.message!=null->Row(Modifier.fillMaxWidth().testTag("home-route-error"),verticalAlignment=Alignment.Top){Text(overview.message!!,Modifier.weight(1f),fontSize=11.sp,color=MaterialTheme.colorScheme.error);TextButton(onClick=onRetry,contentPadding=PaddingValues(4.dp)){Text(ui("Retry"),fontSize=11.sp)}}
            else->Text(ui("Choose transport to plan a route"),fontSize=11.sp,color=Muted)
        }
    }
}

@Composable fun TravelInsightsDialog(store:BuddyStore,requested:org.json.JSONArray?=null,onDismiss:()->Unit){
    Dialog(onDismissRequest=onDismiss){Surface(shape=RoundedCornerShape(24.dp),color=Color.White){Column(Modifier.fillMaxWidth().heightIn(max=620.dp).padding(18.dp).verticalScroll(rememberScrollState()).testTag("travel-insights-sheet"),verticalArrangement=Arrangement.spacedBy(12.dp)){
        Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically){Text("Transportation・Crowds・Costs",fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f));TextButton(onClick=onDismiss){Text("Done")}}
        TextButton(onClick={store.refreshInsights(requested)},enabled=!store.insightsBusy){Text(if(store.insightsBusy)"Updating…"else "Retry")}
        store.insightsError?.let{Text(it,color=Color.Red)}
        store.insights?.let{report->
            val traffic=report.getJSONObject("traffic")
            Text(traffic.optString("summary"),fontWeight=FontWeight.Bold)
            Small(report.optString("scope"));Small("Source: ${report.optString("dataSource")} · ${report.optString("fetchedAt")}")
            traffic.array("items").objects().forEach{item->Column(Modifier.fillMaxWidth().background(Color(0xFFFFF3E0),RoundedCornerShape(12.dp)).padding(12.dp)){Text("⚠️ "+item.optString("title"));Text(item.optString("summary"));Small(item.optString("source"))}}
            if(report.array("pricePlaces").length()>0)Text("Nearby reference fares",fontWeight=FontWeight.Bold)
            report.array("pricePlaces").objects().forEach{place->Column(Modifier.fillMaxWidth().background(Mint,RoundedCornerShape(12.dp)).padding(12.dp)){Text(place.optString("title"),fontWeight=FontWeight.Bold);Text(place.optString("summary"));Small(place.optString("source"))}}
            report.array("places").objects().forEach{p->Column(Modifier.fillMaxWidth().background(Mint,RoundedCornerShape(12.dp)).padding(12.dp),verticalArrangement=Arrangement.spacedBy(5.dp)){
                Text(p.optString("name"),fontWeight=FontWeight.Bold);Text(p.getJSONObject("crowd").optString("summary"),color=Muted)
                val price=p.getJSONObject("price");Text(price.optString("summary"));Small(price.optString("source"))
                price.optJSONObject("range")?.let{range->
                    val low=range.optJSONObject("startPrice");val high=range.optJSONObject("endPrice")
                    Text("${low?.optString("currencyCode") ?: ""} ${low?.optString("units") ?: "?"} – ${high?.optString("currencyCode") ?: ""} ${high?.optString("units") ?: "No limit"}")
                }
                if(price.has("scope"))Small(price.optString("scope"))
            }}
            Small(report.optString("attribution"))
        }
    }}}
}

@Composable fun MapDaySelector(dates:List<String>,selection:Int,onSelect:(Int)->Unit) {
    val listState=rememberLazyListState()
    LaunchedEffect(selection,dates){if(selection in dates.indices)listState.animateScrollToItem(selection)}
    LazyRow(state=listState,modifier=Modifier.fillMaxWidth().testTag("map-day-selector"),horizontalArrangement=Arrangement.spacedBy(8.dp),contentPadding=PaddingValues(3.dp)) {
        itemsIndexed(dates){index,date->
            val active=index==selection
            val dayLabel=ui("Day {0}",(index+1).toString())
            Surface(onClick={onSelect(index)},shape=RoundedCornerShape(16.dp),color=if(active)Green else Color.White.copy(alpha=.96f),modifier=Modifier.widthIn(min=84.dp).testTag("map-day-$index").semantics{selected=active;contentDescription=dayLabel+", "+date}) {
                Column(Modifier.padding(horizontal=12.dp,vertical=10.dp).heightIn(min=40.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(4.dp)) {
                    Text(dayLabel,fontSize=14.sp,fontWeight=FontWeight.Bold,color=if(active)Color.White else Ink,softWrap=false)
                    Text(date.takeLast(5),fontSize=12.sp,color=if(active)Color.White else Muted,softWrap=false)
                }
            }
        }
    }
}
