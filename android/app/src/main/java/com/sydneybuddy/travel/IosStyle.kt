package com.sydneybuddy.travel

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
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
    val items=listOf(Triple(0,"地图",Icons.Outlined.Map),Triple(1,"行程",Icons.Outlined.Luggage),Triple(3,"记账",Icons.Outlined.CreditCard),Triple(4,"我的",Icons.Outlined.AccountCircle))
    Surface(color=Color.White.copy(alpha=.98f),shadowElevation=2.dp) {
        Row(Modifier.fillMaxWidth().navigationBarsPadding().height(66.dp),verticalAlignment=Alignment.CenterVertically) {
            items.forEach { (index,label,icon) ->
                Column(Modifier.weight(1f).fillMaxHeight().clickable(enabled=!store.busy){store.tab=index}.padding(top=10.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(4.dp)) {
                    Icon(icon,null,Modifier.size(24.dp),tint=if(store.tab==index) Green else Muted)
                    Text(label,color=if(store.tab==index) Green else Muted,fontSize=11.sp,fontWeight=if(store.tab==index) FontWeight.Bold else FontWeight.Normal)
                }
            }
        }
    }
}

@Composable fun Buddy(size: Int) {
    Canvas(Modifier.size(size.dp,(size*1.15f).dp).semantics{contentDescription="旅行机器人 Buddy"}) {
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
            Text(text,fontSize=16.sp,fontWeight=FontWeight.Bold);Spacer(Modifier.width(12.dp));Icon(Icons.Outlined.ArrowForward,null,Modifier.size(21.dp))
        }
    }
}

fun choiceIcon(text:String):ImageVector = when {
    "轻松" in text -> Icons.Outlined.Coffee
    "步" in text || "活力" in text || "刚刚好" in text -> Icons.Outlined.DirectionsWalk
    "天" in text -> Icons.Outlined.WbSunny
    "A$" in text -> Icons.Outlined.AccountBalanceWallet
    "车站" in text -> Icons.Outlined.Train
    "码头" in text -> Icons.Outlined.DirectionsBoat
    ":" in text -> Icons.Outlined.Schedule
    "美食" in text -> Icons.Outlined.Restaurant
    "自然" in text -> Icons.Outlined.Park
    "艺术" in text -> Icons.Outlined.Palette
    "文化" in text -> Icons.Outlined.AccountBalance
    "城市" in text -> Icons.Outlined.LocationCity
    else -> Icons.Outlined.AutoAwesome
}

@Composable fun Choice(text:String,selected:Boolean,enabled:Boolean=true,onClick:()->Unit) {
    val parts=text.split(" · ",limit=2)
    val detail=parts.getOrNull(1) ?: when(text){"1 天"->"一次轻巧的城市探索";"2 天"->"多留一点时间，慢慢发现";"3 天"->"给风景和休息都留些空间";"环形码头"->"从悉尼海港开始";"中央车站"->"从城市交通枢纽开始";else->""}
    Column(Modifier.fillMaxWidth().padding(bottom=if(selected)4.dp else 0.dp).background(if(selected)Green.copy(alpha=.24f) else Color.Transparent,RoundedCornerShape(22.dp))) {
        Row(Modifier.fillMaxWidth().offset(y=if(selected)(-3).dp else 0.dp).clip(RoundedCornerShape(22.dp)).background(if(selected)Mint else Color.White)
            .border(if(selected)2.dp else 1.dp,if(selected)Green else Green.copy(alpha=.13f),RoundedCornerShape(22.dp))
            .clickable(enabled=enabled,onClick=onClick).padding(horizontal=18.dp,vertical=20.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(16.dp)) {
            Icon(choiceIcon(text),null,Modifier.size(28.dp),tint=Green)
            Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(6.dp)) {Text(parts[0],fontSize=17.sp,fontWeight=FontWeight.Bold);if(detail.isNotBlank())Text(detail,fontSize=12.sp,color=Muted)}
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
    return if (duration % 60 == 0) "${duration / 60} 小时" else "${duration / 60} 小时 ${duration % 60} 分钟"
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
                    Text("自定义时间区间", fontWeight=FontWeight.Bold, fontSize=15.sp)
                    Text("每次调整 30 分钟 · 至少保留 30 分钟", fontSize=11.sp, color=Muted)
                }
                Text("${durationLabel(start, end)}", fontSize=12.sp, color=Green, fontWeight=FontWeight.Bold)
            }
            Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                TimeDropdown("出发时间",start,slots.filter{it<=end-interval},enabled,{newStart->onRangeChange(newStart,end)},Modifier.weight(1f))
                Icon(Icons.Outlined.ArrowForward,null,Modifier.size(20.dp),tint=Green)
                TimeDropdown("返回时间",end,slots.filter{it>=start+interval},enabled,{newEnd->onRangeChange(start,newEnd)},Modifier.weight(1f))
            }
            Text("可选择 06:00–22:00，每次间隔 30 分钟。",fontSize=11.sp,color=Muted)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable fun HomeScreen(store:BuddyStore) {
    var poiSearch by remember{mutableStateOf(false)}
    if(poiSearch)PoiDiscoveryDialog(store,onDismiss={poiSearch=false})
    var detail by rememberSaveable {mutableStateOf(false)}
    var journey by rememberSaveable {mutableStateOf(false)}
    var expandedMap by rememberSaveable {mutableStateOf(false)}
    var navigation by rememberSaveable {mutableStateOf(false)}
    var chat by rememberSaveable {mutableStateOf(false)}
    var expanded by rememberSaveable {mutableStateOf(false)}
    val trip=store.trip
    val day=store.day
    val overview=remember(day.toString(),trip?.optString("mode"),store.mapProvider){ItineraryOverview()}
    DisposableEffect(overview){onDispose{overview.close()}}
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val compact=maxHeight<650.dp
        RouteMap(day,Modifier.fillMaxSize(),true,store.mapProvider,trip?.optString("mode")=="live",store.mapViewport,{store.mapViewport=it},overview.route)
        Column(Modifier.fillMaxSize().padding(horizontal=20.dp).padding(top=12.dp,bottom=10.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
            TextButton(onClick={poiSearch=true},modifier=Modifier.fillMaxWidth().background(Color.White,androidx.compose.foundation.shape.RoundedCornerShape(24.dp))){Text("⌕ 搜尋景點、餐廳與設施")}
            Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
                Surface(shape=CircleShape,color=Color.White,shadowElevation=2.dp) {
                    Row(Modifier.padding(horizontal=17.dp,vertical=12.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                        Icon(Icons.Outlined.LocationOn,null,Modifier.size(22.dp),tint=Green)
                        Column(verticalArrangement=Arrangement.spacedBy(2.dp)){Text("悉尼 · 小冒险",fontSize=16.sp,fontWeight=FontWeight.Bold);Text(day?.optString("date") ?: "准备出发",fontSize=12.sp,color=Muted)}
                    }
                }
                Spacer(Modifier.weight(1f))
                IconButton(onClick={store.tab=4},modifier=Modifier.size(46.dp).background(Color.White,CircleShape)) {Icon(Icons.Outlined.AccountCircle,"我的",Modifier.size(40.dp),tint=Green)}
            }
            Surface(shape=CircleShape,color=Color.White.copy(alpha=.9f),modifier=Modifier.align(Alignment.CenterHorizontally)) {
                Text(store.mapProvider.label + if(store.mapProvider==MapProvider.GOOGLE && !BuildConfig.MAPS_CONFIGURED) " · 未配置密钥" else "",Modifier.padding(horizontal=12.dp,vertical=5.dp),fontSize=10.sp,color=Muted)
            }
            if(trip!=null && trip.array("days").length()>1) {
                Row(Modifier.fillMaxWidth().clip(CircleShape).background(Color.White.copy(alpha=.7f))) {
                    trip.array("days").objects().forEachIndexed{i,_->
                        TextButton(onClick={store.dayIndex=i},modifier=Modifier.weight(1f).background(if(i==store.dayIndex)Color.White else Color.Transparent,CircleShape)){Text("第 ${i+1} 天",color=if(i==store.dayIndex)Ink else Muted,fontWeight=FontWeight.Bold)}
                    }
                }
            }
            if(trip!=null) CompactWeather(store)
            if(trip!=null) Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End) {
                Surface(shape=CircleShape,color=Color.White){TextButton(onClick={expandedMap=true}){Text("展开地图")}}
            }
            Spacer(Modifier.weight(1f))
            if(trip!=null) {
                val count=listOf("route","weather","bag").count(store::prepared)
                Surface(shape=RoundedCornerShape(20.dp),color=Color.White,shadowElevation=2.dp) {
                    Column(Modifier.padding(14.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
                        Row(Modifier.fillMaxWidth().clickable{expanded=!expanded},verticalAlignment=Alignment.CenterVertically) {
                            Icon(Icons.Outlined.Flag,null,Modifier.size(19.dp),tint=Green);Spacer(Modifier.width(7.dp))
                            Text(if(count==3)"出发准备小达人" else "出发前的 3 个小任务",fontSize=14.sp,fontWeight=FontWeight.Bold,color=Green,modifier=Modifier.weight(1f))
                            Text("$count/3",fontSize=12.sp,color=Muted);Icon(if(expanded)Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,null,Modifier.size(20.dp),tint=Green)
                        }
                        if(expanded) {
                            Row(horizontalArrangement=Arrangement.spacedBy(7.dp)) {
                                PreparationButton("看路线",Icons.Outlined.Map,store.prepared("route"),Modifier.weight(1f)){store.mark("route");detail=true}
                                PreparationButton("看天气",Icons.Outlined.Cloud,store.prepared("weather"),Modifier.weight(1f),!store.busy,store::refreshWeather)
                                PreparationButton("行李备好",Icons.Outlined.Backpack,store.prepared("bag"),Modifier.weight(1f)){store.mark("bag",true)}
                            }
                            Text("随时回来继续，旅行没有断签惩罚。",fontSize=10.sp,color=Muted)
                        }
                    }
                }
            }
            Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.Bottom,horizontalArrangement=Arrangement.spacedBy(12.dp)) {
                Surface(shape=RoundedCornerShape(25.dp),color=Color.White,shadowElevation=4.dp,modifier=Modifier.weight(1f)) {
                    Column(Modifier.padding(if(compact)15.dp else 18.dp),verticalArrangement=Arrangement.spacedBy(if(compact)8.dp else 11.dp)) {
                        Text(trip?.optString("title") ?: "悉尼一日游",fontSize=19.sp,fontWeight=FontWeight.Bold)
                        if(day!=null) {
                            Text("${day.array("stops").length()} 个地点 · 约 ${"%,d".format(day.optInt("estimatedSteps"))} 步",fontSize=12.sp,color=Muted)
                            Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(5.dp)){Icon(Icons.Outlined.AccountBalanceWallet,null,Modifier.size(16.dp),tint=Green);Text("A$${day.optDouble("estimatedCost").toInt()}  ·  ${day.optString("finishTime")} 返回",fontSize=12.sp,color=Green,fontWeight=FontWeight.Bold)}
                            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).semantics{contentDescription="行程站点顺序"}) {
                                Text("起 · ${day.getJSONObject("start").optString("name")}"+day.array("stops").objects().mapIndexed{i,s->" → ${i+1} · ${s.optString("name")} ${s.optString("arrival")}"}.joinToString("")+" → 终 · ${day.getJSONObject("end").optString("name")}",fontSize=12.sp,color=Green)
                            }
                            if(trip?.optString("mode")!="live" && store.mapProvider==MapProvider.OSM) {
                                TextButton(onClick={overview.load(day,store.base)},enabled=!overview.busy && overview.route==null) {
                                    Text(if(overview.busy)"正在取得步行路线…" else overview.route?.let{"步行约 ${it.meters.toInt()} 米 · ${kotlin.math.ceil(it.seconds/60).toInt()} 分钟"} ?: "显示步行路线",fontSize=12.sp)
                                }
                                overview.route?.let{Text(it.segments,Modifier.horizontalScroll(rememberScrollState()),fontSize=11.sp,color=Muted)}
                                overview.message?.let{Text(it,fontSize=11.sp,color=Muted)}
                            }
                        } else Text("让 Buddy 帮你选出刚刚好的路线",fontSize=12.sp,color=Muted)
                        if(trip!=null) Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(4.dp)) {
                            TextButton(onClick={journey=true},modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text("交通路线规划",fontSize=12.sp,maxLines=1)}
                            TextButton(onClick={navigation=true},enabled=store.mapProvider==MapProvider.OSM,modifier=Modifier.weight(1f),contentPadding=PaddingValues(0.dp)){Text("逐段步行导航",fontSize=12.sp,maxLines=1)}
                        }
                        Button(onClick={if(trip==null)store.newTrip() else {store.mark("route");detail=true}},enabled=!store.busy,modifier=Modifier.fillMaxWidth(),shape=RoundedCornerShape(14.dp),contentPadding=PaddingValues(horizontal=13.dp,vertical=12.dp)) {
                            Text(if(trip==null)"开始规划" else "查看路线",fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f));Icon(Icons.Outlined.NorthEast,null,Modifier.size(19.dp))
                        }
                    }
                }
                Column(Modifier.width(82.dp).clickable(enabled=!store.busy){chat=true}.semantics{contentDescription="问 AI"},horizontalAlignment=Alignment.CenterHorizontally) {
                    Surface(shape=CircleShape,color=Green){Text("问 AI",Modifier.padding(horizontal=17.dp,vertical=11.dp),fontWeight=FontWeight.Bold,color=Color.White,fontSize=15.sp)}
                    Buddy(80)
                }
            }
            Surface(shape=CircleShape,color=Color.White.copy(alpha=.9f),modifier=Modifier.align(Alignment.CenterHorizontally)) {
                Text(if(trip?.optString("mode")=="live" && store.mapProvider!=MapProvider.GOOGLE)"Google 行程图层已隐藏，请切换 Google 底图查看" else if(trip?.optString("mode")=="live")"Google 路线 · 费用与步数为估算" else if(overview.route!=null && store.mapProvider!=MapProvider.SCHEMATIC) "沿道路步行路线 · 不含景点停留时间" else if(store.mapProvider==MapProvider.SCHEMATIC)"离线顺序示意 · 非道路路线" else "尚未查询道路路线 · 仅显示站点",Modifier.padding(horizontal=10.dp,vertical=5.dp),fontSize=9.sp,color=Muted)
            }
        }
    }
    if(expandedMap) Dialog(onDismissRequest={expandedMap=false},properties=androidx.compose.ui.window.DialogProperties(usePlatformDefaultWidth=false)) {
        Box(Modifier.fillMaxSize().background(Color.White).testTag("expanded-itinerary-map")) {
            RouteMap(day,Modifier.fillMaxSize(),true,store.mapProvider,trip?.optString("mode")=="live",overview=overview.route,expanded=true)
            Column(Modifier.fillMaxSize().padding(18.dp),verticalArrangement=Arrangement.SpaceBetween) {
                Surface(shape=RoundedCornerShape(18.dp),color=Color.White) {
                    Row(Modifier.fillMaxWidth().padding(12.dp),verticalAlignment=Alignment.CenterVertically) {Text("全天行程顺序",modifier=Modifier.weight(1f),fontWeight=FontWeight.Bold);TextButton(onClick={expandedMap=false}){Text("关闭地图")}}
                }
                Surface(shape=RoundedCornerShape(18.dp),color=Color.White) {
                    Column(Modifier.padding(16.dp),verticalArrangement=Arrangement.spacedBy(8.dp)) {
                        Text(if(overview.route==null || store.mapProvider==MapProvider.SCHEMATIC)"尚未查询道路路线 · 仅显示站点" else "沿道路步行路线 · 不含景点停留时间",fontSize=12.sp,color=Green)
                        if(day!=null) {
                            Text(walkingPoints(day).mapIndexed{i,p->"${if(i==0)"起" else if(i==walkingPoints(day).lastIndex)"终" else "$i"} · ${p.name}"}.joinToString(" → "),Modifier.horizontalScroll(rememberScrollState()),fontSize=12.sp)
                            if(trip?.optString("mode")!="live" && store.mapProvider==MapProvider.OSM) {
                                TextButton(onClick={overview.load(day,store.base)},enabled=!overview.busy && overview.route==null){Text(if(overview.busy)"正在取得步行路线…" else overview.route?.let{"步行约 ${it.meters.toInt()} 米 · ${kotlin.math.ceil(it.seconds/60).toInt()} 分钟"} ?: "显示步行路线")}
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
        if(trip?.optString("mode")=="live" && store.mapProvider!=MapProvider.GOOGLE) AlertDialog(onDismissRequest={journey=false},title={Text("请切换 Google 地图")},text={Text("此行程的交通规划需搭配 Google 底图与 Google Routes。")},confirmButton={TextButton(onClick={journey=false}){Text("关闭")}})
        else JourneyPlannerDialog(day,store.base,store.mapProvider,{journey=false},{journey=false;navigation=true},contentSource=if(trip?.optString("mode")=="live")"google" else "open")
    }
    if(navigation) {
        if(trip?.optString("mode")=="live") AlertDialog(onDismissRequest={navigation=false},text={Text("此行程含 Google 地点资料。开源导航测试请先建立示例行程。")},confirmButton={TextButton(onClick={navigation=false}){Text("关闭")}})
        else WalkingNavigationDialog(store){navigation=false}
    }
    if(detail) ModalBottomSheet(onDismissRequest={detail=false},containerColor=Cream,sheetState=rememberModalBottomSheetState(skipPartiallyExpanded=true)) {
        Column(Modifier.fillMaxWidth().fillMaxHeight(.9f).verticalScroll(rememberScrollState()).padding(22.dp),verticalArrangement=Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){Heading("这次的小冒险");Spacer(Modifier.weight(1f));TextButton(onClick={detail=false}){Text("完成")}}
            trip?.let{Text(it.optString("explanation"));WeatherPanel(store.weather ?: it.optJSONObject("weather"),!store.busy,store::refreshWeather);it.array("days").objects().forEach{d->DaySummary(d)};Evidence(it)}
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
    val weather=store.weather ?: store.trip?.optJSONObject("weather")
    val daily=weather?.array("days")?.objects()?.find{it.optString("date")==store.day?.optString("date")}
    Surface(shape=RoundedCornerShape(20.dp),color=Color.White,shadowElevation=2.dp) {
        Column(Modifier.fillMaxWidth().padding(horizontal=15.dp,vertical=12.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically) {
                Icon(if(weather?.optBoolean("rainy")==true)Icons.Outlined.Umbrella else Icons.Outlined.Cloud,null,Modifier.size(21.dp));Spacer(Modifier.width(7.dp))
                Text("悉尼天气",fontWeight=FontWeight.Bold,fontSize=15.sp,modifier=Modifier.weight(1f))
                IconButton(onClick=store::refreshWeather,enabled=!store.busy,modifier=Modifier.size(26.dp)){Icon(Icons.Outlined.Refresh,"刷新天气",tint=Green)}
            }
            Text(daily?.let{"${it.optString("date")} · ${it.optString("summary")}"} ?: weather?.optString("summary") ?: "天气尚未查询",fontSize=12.sp,maxLines=2)
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
            Row(verticalAlignment=Alignment.CenterVertically){Text("${month.year}年 ${month.monthValue}月",fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f));IconButton(onClick={month=month.minusMonths(1)}){Icon(Icons.Outlined.ChevronLeft,"上个月",tint=Green)};IconButton(onClick={month=month.plusMonths(1)}){Icon(Icons.Outlined.ChevronRight,"下个月",tint=Green)}}
            Row {listOf("一","二","三","四","五","六","日").forEach{Box(Modifier.weight(1f),contentAlignment=Alignment.Center){Text(it,fontSize=11.sp,color=Muted)}}}
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
    val titles=listOf("哪天去悉尼玩？","这次玩几天？","想用什么节奏？","什么最让你心动？","每天准备花多少？","从哪里开始？","想几点出发返回？","一起出发吧！")
    val subtitles=listOf("选个日子，其他的慢慢来。","先留出时间，不必一次想好所有事。","选舒服的，不用挑战步数纪录。","可以多选，也可以暂时不选。","先定个大概，随时可以修改。","选一个方便的起点。","给探索和休息都留一点空间。","看一眼你的选择，生成后还可以调整。")
    val messages=listOf("每个选择都没有标准答案，适合你就好。","第一步完成！你想在悉尼留多久？","旅行不需要赶进度，按你的节奏来。","让我多了解一点你的喜好。","好体验，也要在舒服的预算里。","已经过半啦！选个出发点就好。","快好啦，最后留好出发和返程时间。","准备完成！我来检查天气、距离和营业时间。")
    Column(Modifier.fillMaxSize().background(Cream)) {
        Row(Modifier.fillMaxWidth().padding(horizontal=18.dp,vertical=16.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(14.dp)) {
            IconButton(onClick={if(step>0)step-- else if(store.saved.isNotEmpty())store.resumeSaved()},enabled=!store.busy,modifier=Modifier.size(24.dp)){Icon(Icons.Outlined.ChevronLeft,"返回上一步",tint=Muted)}
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
                Text("小冒险 · 出发准备",fontSize=12.sp,fontWeight=FontWeight.Bold,color=Green)
                Text(titles[step],fontSize=29.sp,fontWeight=FontWeight.ExtraBold,color=Ink,lineHeight=37.sp)
                Text(subtitles[step],fontSize=14.sp,color=Muted,lineHeight=21.sp)
            }
        when (step) {
            0 -> {
                Surface(shape=RoundedCornerShape(24.dp),color=Mint) {
                    Row(Modifier.fillMaxWidth().padding(22.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(12.dp)) {
                        Icon(Icons.Outlined.Sailing,null,Modifier.size(29.dp),tint=Green)
                        Text("SYDNEY · 悉尼",fontWeight=FontWeight.Bold,fontSize=20.sp,color=Green)
                    }
                }
                TravelCalendar(p.date){store.profile=p.copy(date=it)}
            }
            1 -> (1..3).forEach { n -> Choice("$n 天", p.days == n, !store.busy) { store.profile = p.copy(days = n) } }
            2 -> listOf(5000 to "轻松逛逛",10000 to "刚刚好",18000 to "活力满满").forEach { (n,t) -> Choice("$t · 约 $n 步以内",p.maxSteps == n,!store.busy) { store.profile=p.copy(maxSteps=n) } }
            3 -> {
                listOf("风景","美食","自然","艺术","文化","城市").chunked(2).forEach{row ->
                    Row(horizontalArrangement=Arrangement.spacedBy(14.dp)) {row.forEach{t ->
                        val selected=t in p.interests
                        Column(Modifier.weight(1f).height(126.dp).clip(RoundedCornerShape(22.dp)).background(if(selected)Mint else Color.White).border(if(selected)2.dp else 1.dp,if(selected)Green else Green.copy(alpha=.12f),RoundedCornerShape(22.dp)).clickable(enabled=!store.busy){store.profile=p.copy(interests=if(selected)p.interests-t else p.interests+t)},horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.Center) {
                            Icon(choiceIcon(t),null,Modifier.size(28.dp),tint=Green);Spacer(Modifier.height(10.dp));Text(t,fontWeight=FontWeight.Bold);Spacer(Modifier.height(8.dp));Icon(if(selected)Icons.Outlined.CheckCircle else Icons.Outlined.RadioButtonUnchecked,null,Modifier.size(17.dp),tint=Green)
                        }
                    }}
                }
            }
            4 -> { listOf(50,100,150).forEach { n -> Choice("A$$n / 天",p.budget==n,!store.busy) {store.profile=p.copy(budget=n)} }
                Text("自定：A$${p.budget}"); Slider(value=p.budget.toFloat(),onValueChange={store.profile=p.copy(budget=(it/10).toInt()*10)},valueRange=0f..300f,steps=29,enabled=!store.busy)
                Small("活动费用估算，不含住宿、机票及全部餐饮。") }
            5 -> { listOf(Endpoint.quay,Endpoint.central).forEach { e -> Choice(e.name,p.start==e,!store.busy){store.profile=p.copy(start=e,end=e)} }; Small("每天返回同一起点，可在“我的”设置下一次旅行的起终点。") }
            6 -> {
                listOf(Triple(600,1080,"10:00–18:00 · 悠闲一天"),Triple(540,1020,"09:00–17:00 · 早一点出发"),Triple(780,1080,"13:00–18:00 · 午后小冒险")).forEach { (s,e,t) ->
                    Choice(t,p.startMinute==s && p.endMinute==e,!store.busy){store.profile=p.copy(startMinute=s,endMinute=e)}
                }
                CustomTimeRange(p.startMinute, p.endMinute, !store.busy) { start, end ->
                    store.profile = p.copy(startMinute=start, endMinute=end)
                }
            }
            else -> { Panel { Text("悉尼 · ${p.days} 天 · ${p.maxSteps} 步以内 / 天",fontWeight=FontWeight.Bold)
                Text("${p.date} · 每日 A$${p.budget}"); Text("${p.start.name}出发 → ${p.end.name}返回"); Text(p.interests.joinToString(" · ").ifEmpty {"随心探索"}) }
                OnlineSwitch(store)
                Small("生成后先展示草案，由你确认后才保存。") }
        }

        }
        Column(Modifier.fillMaxWidth().padding(horizontal=24.dp).padding(top=10.dp,bottom=16.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(10.dp)) {
            Text(if(step==7)"你做主，Buddy 来帮忙" else "随时可以返回修改",fontSize=11.sp,color=Muted)
            Action(if(step==7)"让 Buddy 规划路线" else "选好了，继续",!store.busy){store.rememberProfile();if(step<7)step++ else store.generate()}
            if(step==0 && store.saved.isNotEmpty())TextButton(onClick=store::resumeSaved,enabled=!store.busy){Text("回到已保存行程")}
        }
    }
    BackHandler(enabled=step>0 && !store.busy){step--}
}
