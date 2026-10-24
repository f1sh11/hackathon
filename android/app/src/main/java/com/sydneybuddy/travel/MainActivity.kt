package com.sydneybuddy.travel

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.ui.platform.testTag
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import org.json.JSONObject

val Coral = Color(0xFFBC3C55)
val Blush = Color(0xFFFCE4E8)
val Ink = Color(0xFF3D3138)
val Cream = Color(0xFFFFF8F5)
// Compatibility names keep the existing screens on the shared koala theme.
val Green = Coral
val Mint = Blush

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            MaterialTheme(colorScheme = lightColorScheme(primary = Coral, onPrimary = Color.White,
                primaryContainer = Blush, onPrimaryContainer = Ink,
                secondary = Coral, onSecondary = Color.White, secondaryContainer = Blush, onSecondaryContainer = Ink,
                background = Cream, onBackground = Ink, surface = Color.White, onSurface = Ink,
                surfaceVariant = Blush, onSurfaceVariant = Muted,
                outline = Color(0xFFD9BBC4), outlineVariant = Color(0xFFEFD9DE))) {
                BuddyApp()
            }
        }
    }
}

@Composable fun BuddyApp(store: BuddyStore = viewModel()) {
    CompositionLocalProvider(LocalAppLanguage provides store.language){BuddyAppContent(store)}
}
@Composable private fun BuddyAppContent(store:BuddyStore) {
    var delete by remember { mutableStateOf<Pair<String, JSONObject>?>(null) }
    Scaffold(containerColor = Cream,
        topBar = {
            if (!store.onboarding && store.tab != 0) Row(Modifier.fillMaxWidth().statusBarsPadding().padding(horizontal = 22.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(ui(when(store.tab){1->"我的行程";3->"旅行小账本";4->"我的 Buddy";else->"Buddy"}),style=MaterialTheme.typography.headlineMedium,fontWeight=FontWeight.Bold)
                Spacer(Modifier.weight(1f))
                if (store.tab==1) TextButton(onClick = store::newTrip,enabled=!store.busy) { Icon(Icons.Outlined.Add,null);Text(ui("新建")) }
            }
        },
        bottomBar = {
            if (!store.onboarding && store.proposal == null) IosNavigation(store)

        }
    ) { padding ->
        Column(Modifier.padding(padding).fillMaxSize().imePadding()) {
            if (store.busy) { LinearProgressIndicator(Modifier.fillMaxWidth()); Text("Buddy 正在处理，请稍候…", Modifier.padding(horizontal = 22.dp), style = MaterialTheme.typography.labelMedium) }
            if (store.proposal != null) ProposalScreen(store)
            else if (store.onboarding) Onboarding(store)
            else when (store.tab) {
                0 -> HomeScreen(store)
                1 -> TripsScreen(store) { delete = "trip" to it }
                2 -> AgentScreen(store)
                3 -> LedgerScreen(store) { delete = "expense" to it }
                else -> SettingsScreen(store) { delete = "memory" to it }
            }
        }
    }
    store.koalaEvent?.let { (event,token) ->
        LaunchedEffect(token){if(event !in listOf(KoalaEvent.PlanningStarted,KoalaEvent.OptimizationStarted,KoalaEvent.SimulationStarted,KoalaEvent.WeatherRisk,KoalaEvent.BudgetRisk,KoalaEvent.FatigueRisk)){kotlinx.coroutines.delay(if(event==KoalaEvent.TripSaved)2200 else 1700);if(store.koalaEvent?.second==token)store.koalaEvent=null}}
        AlertDialog(onDismissRequest={store.koalaEvent=null},title={Text(when(event){KoalaEvent.BudgetRisk->"预计费用超出预算";KoalaEvent.WeatherRisk->"有天气风险";KoalaEvent.TripSaved->"行程已保存";KoalaEvent.PreferenceConfirmed->"偏好已确认";else->"Buddy 陪着你"})},text={Box(Modifier.fillMaxWidth(),contentAlignment=Alignment.Center){KoalaAvatar(180,event.action,token)}},confirmButton={TextButton(onClick={store.koalaEvent=null}){Text("知道了")}})
    }
    if (store.proposal != null) BackHandler { if (!store.busy) store.cancelProposal() }
    BackHandler(enabled = store.busy) { /* Keep the current operation visible until it finishes. */ }
    store.error?.let { text -> AlertDialog(onDismissRequest = { store.error = null }, title = { Text(ui("这次没有完成")) }, text = { Text(text) },
        confirmButton = { TextButton(onClick = { store.error = null }) { Text(ui("知道了")) } }) }
    store.notice?.let { text -> AlertDialog(onDismissRequest = { store.notice = null }, text = { Text(text) },
        confirmButton = { TextButton(onClick = { store.notice = null }) { Text(ui("好的")) } }) }
    delete?.let { (type, value) -> AlertDialog(onDismissRequest = { delete = null }, title = { Text(ui("确认删除？")) },
        text = { Text(if (type == "memory") "偏好将从检索中撤回，电脑本机审计历史仍保留。" else "此操作将删除这条${if (type == "trip") "行程" else "消费记录"}。") },
        confirmButton = { TextButton(onClick = { when (type) { "trip" -> store.deleteTrip(value); "memory" -> store.deleteMemory(value); else -> store.deleteExpense(value) }; delete = null }) { Text(ui("删除")) } },
        dismissButton = { TextButton(onClick = { delete = null }) { Text(ui("取消")) } }) }
}

@Composable fun Page(content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(22.dp), verticalArrangement = Arrangement.spacedBy(16.dp), content = content)
}
@Composable fun Panel(content: @Composable ColumnScope.() -> Unit) {
    Surface(shape = RoundedCornerShape(24.dp), color = Color.White, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp), content = content)
    }
}
@Composable fun Heading(text: String) { Text(ui(text), style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold) }
@Composable fun Small(text: String) { Text(ui(text), style = MaterialTheme.typography.bodySmall, color = Ink.copy(alpha = .75f)) }
@Composable fun OnlineSwitch(store: BuddyStore) {
    Row(verticalAlignment=Alignment.CenterVertically) { Column(Modifier.weight(1f)) { Text(ui("使用在线推荐"),fontWeight=FontWeight.Bold); Small(if(store.online) "NVIDIA 推荐；有 Google 服务配置时查询在线数据" else "演示模式 · 示例数据，无 AI 调用") }
        Switch(checked=store.online,onCheckedChange={store.online=it},enabled=!store.busy && store.health?.optBoolean("paidServicesAllowed")==true) }
    if(store.health?.optBoolean("paidServicesAllowed")!=true) Small("后端已禁止付费服务，当前可使用演示模式。")
    if(store.online) Small("在线请求会将本次旅行的偏好、日期和起终点发送给 NVIDIA / Google。")
}

fun modeLabel(mode:String) = when(mode) {"live"->"在线规划 · 以各项来源和警告为准";"assisted"->"Nemotron 推荐 · 示例地点、天气与路线";else->"演示行程 · 示例数据"}

@Composable fun WeatherPanel(weather:JSONObject?,enabled:Boolean,onRefresh:()->Unit) {
    Panel { Row(verticalAlignment=Alignment.CenterVertically){Text(ui("悉尼天气"),fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f));TextButton(onClick=onRefresh,enabled=enabled){Text(ui("刷新天气"))}}
        if(weather==null) Small("尚未查询") else {
            val days=weather.array("days").objects()
            if(days.isEmpty()) Text(weather.optString("summary")) else days.forEach { Text("${it.optString("date")} · ${it.optString("summary")}",style=MaterialTheme.typography.bodySmall) }
            Small(weather.optString("source")); if(weather.has("fetchedAt")) Small("查询于 ${weather.optString("fetchedAt")}")
        }
        Small("刷新天气不会重排路线。需要改变行程时请问 Buddy。")
    }
}

@Composable fun DaySummary(day:JSONObject) {
    val context=LocalContext.current
    Panel {
        Text(day.optString("date"),fontWeight=FontWeight.Bold)
        Text("约 ${day.optInt("estimatedSteps")} 步 · A$${day.optDouble("estimatedCost")} · ${day.optString("finishTime")} 返回")
        Small("${day.optJSONObject("start")?.optString("name")} → ${day.optJSONObject("end")?.optString("name")}")
        day.array("stops").objects().forEachIndexed { i,s ->
            HorizontalDivider(color=Mint)
            Text("${i+1}. ${s.optString("name")}",fontWeight=FontWeight.Bold)
            Small("${s.optString("arrival")}–${s.optString("departure")} · ${if(s.optBoolean("indoor")) "室内" else "室外"} · 约 A$${s.optDouble("cost")}")
            Small(s.optString("note")); if(!s.optBoolean("hoursVerified")) Small("营业时间待核实")
            TextButton(onClick={
                val uri=Uri.parse(s.optString("mapsURL"))
                if(uri.scheme=="https" && uri.host=="www.google.com") runCatching {context.startActivity(Intent(Intent.ACTION_VIEW,uri))}
            }) {Text("在 Google 地图查看")}
        }
        day.array("warnings").strings().forEach {Small(it)}
    }
}

@Composable fun Evidence(trip:JSONObject) {
    var expanded by remember {mutableStateOf(false)}
    trip.array("warnings").strings().forEach {Small("• $it")}
    TextButton(onClick={expanded=!expanded}){Text(if(expanded) "收起规划依据" else "查看规划依据与验证记录")}
    if(expanded) Panel {trip.array("trace").objects().forEach {Text(it.optString("title"),fontWeight=FontWeight.Bold);Small(it.optString("detail"));Small(it.optString("source"))}}
}

@Composable fun ProposalScreen(store:BuddyStore) {
    val pending=store.proposal ?: return
    if(pending.trip.optString("mode")=="demo") {ScheduleEditorScreen(store);return}
    Page {
        Heading(if(pending.targetId==null) "你的路线准备好了" else "看看这次怎么调整")
        Small("确认之后，才会保存到你的行程。")
        Panel {Text(pending.trip.optString("explanation"));Small(modeLabel(pending.trip.optString("mode")))}
        if(pending.targetId!=null && store.trip!=null) {
            val before=store.trip!!.array("days").objects().flatMap {it.array("stops").objects()}.map{it.optString("name")}.toSet()
            val after=pending.trip.array("days").objects().flatMap{it.array("stops").objects()}.map{it.optString("name")}.toSet()
            Panel {Text("调整对比",fontWeight=FontWeight.Bold);Text("新增：${(after-before).joinToString("、").ifBlank{"无"}}");Text("移除：${(before-after).joinToString("、").ifBlank{"无"}}")
                Text("预计步数：${store.trip!!.array("days").objects().sumOf{it.optInt("estimatedSteps")}} → ${pending.trip.array("days").objects().sumOf{it.optInt("estimatedSteps")}}") }
        }
        WeatherPanel(pending.trip.optJSONObject("weather"),false,{})
        pending.trip.array("days").objects().forEach {DaySummary(it)}
        Evidence(pending.trip)
        Action("确认并保存",!store.busy,store::accept)
        TextButton(onClick=store::cancelProposal,enabled=!store.busy) {Text("取消草案")}
    }
}

internal fun savedTripNavigationDay(trip:JSONObject,today:String=java.time.LocalDate.now(java.time.ZoneId.of("Australia/Sydney")).toString()):Int? {
    val days=trip.array("days").objects();val index=days.indexOfFirst{it.optString("date")==today}
    return if(index>=0)index else if(days.size==1)0 else null
}
@Composable internal fun SavedTripCard(t:JSONObject,enabled:Boolean=true,onEdit:()->Unit,onNavigate:()->Unit,onMap:()->Unit,onDelete:()->Unit) {
    Surface(onClick=onEdit,enabled=enabled,shape=RoundedCornerShape(24.dp),color=Color.White,modifier=Modifier.fillMaxWidth().testTag("edit-trip-${t.optString("id")}")) {
        Column(Modifier.padding(18.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
            TripArtwork()
            Text(t.optString("title"),fontWeight=FontWeight.Bold);Text(ui("{0} · {1} 天",t.getJSONObject("profile").optString("date"),t.array("days").length().toString()))
            Small(modeLabel(t.optString("mode")))
            Row(horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                TextButton(onClick=onNavigate,enabled=enabled,modifier=Modifier.testTag("navigate-trip-${t.optString("id")}")){Icon(Icons.Outlined.Navigation,null,Modifier.size(18.dp));Text(ui("開始導航"))}
                TextButton(onClick=onMap,enabled=enabled){Text(ui("查看地图"),fontWeight=FontWeight.Bold)}
                TextButton(onClick=onDelete,enabled=enabled){Text(ui("删除行程"))}
            }
        }
    }
}
@Composable fun TripsScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    var choosing by remember{mutableStateOf<JSONObject?>(null)}
    var navigation by remember{mutableStateOf(false)}
    fun start(trip:JSONObject,index:Int){store.select(trip);store.dayIndex=index;choosing=null;navigation=true}
    Page {
        Small("下一站，去发现。每次冒险都值得收藏。")
        TextButton(onClick=store::reload,enabled=!store.busy){Text(ui("刷新行程"))}
        Action("試用 3 天時間軸",!store.busy,store::tryScheduleDemo)
        if(store.saved.isEmpty()) Small("还没有保存的行程。")
        store.saved.forEach { t -> SavedTripCard(t,!store.busy,onEdit={store.editSchedule(t)},onNavigate={val day=savedTripNavigationDay(t);if(day!=null)start(t,day)else choosing=t},onMap={store.select(t);store.tab=0},onDelete={onDelete(t)}) }
    }
    choosing?.let{trip->AlertDialog(onDismissRequest={choosing=null},title={Text("選擇導航日期")},text={Column(Modifier.verticalScroll(rememberScrollState())){
        Small("今天不在旅程日期內，請選擇要導航的行程")
        trip.array("days").objects().forEachIndexed{i,day->TextButton(onClick={start(trip,i)}){Text("第 ${i+1} 天 · ${day.optString("date")} · ${day.array("stops").length()} 個站點")}}
    }},confirmButton={TextButton(onClick={choosing=null}){Text(ui("取消"))}})}
    if(navigation) {
        if(store.trip?.optString("mode")=="live")AlertDialog(onDismissRequest={navigation=false},text={Text("這份行程使用 Google 地點資料，目前 App 內逐站步行導航僅支援開源行程。請從查看地圖使用 Google 交通路線規劃。")},confirmButton={TextButton(onClick={navigation=false}){Text("關閉")}})
        else WalkingNavigationDialog(store,autoStart=true){navigation=false}
    }
}

@Composable fun AgentScreen(store:BuddyStore) {
    var message by rememberSaveable {mutableStateOf("")}
    Column(Modifier.fillMaxSize().padding(24.dp).imePadding(),verticalArrangement=Arrangement.spacedBy(18.dp)) {
        Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(15.dp)) {
            Buddy(70)
            Column(verticalArrangement=Arrangement.spacedBy(6.dp)) {
                Text(ui("一起调整行程"),style=MaterialTheme.typography.titleLarge,fontWeight=FontWeight.Bold)
                Small(if(store.online)"NVIDIA Nemotron 提供候选建议" else "演示模式 · 本地规则，无 AI 调用")
            }
        }
        Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(18.dp)) {
            Panel {Text(store.trip?.optString("explanation") ?: "先创建并保存一份行程，Buddy 才能帮你调整。",style=MaterialTheme.typography.bodyMedium)}
            Row(horizontalArrangement=Arrangement.spacedBy(10.dp)) {
                OutlinedButton(onClick={store.generate("少走一点")},enabled=!store.busy && store.trip!=null,modifier=Modifier.weight(1f),shape=RoundedCornerShape(16.dp),colors=ButtonDefaults.outlinedButtonColors(containerColor=Mint),border=null) {
                    Icon(Icons.Outlined.DirectionsWalk,null,Modifier.size(19.dp));Spacer(Modifier.width(6.dp));Text(ui("少走一点"),fontWeight=FontWeight.Bold)
                }
                OutlinedButton(onClick={store.generate("下雨了",true)},enabled=!store.busy && store.trip!=null,modifier=Modifier.weight(1f),shape=RoundedCornerShape(16.dp),colors=ButtonDefaults.outlinedButtonColors(containerColor=Mint),border=null) {
                    Icon(Icons.Outlined.Umbrella,null,Modifier.size(19.dp));Spacer(Modifier.width(6.dp));Text(ui("下雨了"),fontWeight=FontWeight.Bold)
                }
            }
            Small("每次调整先生成草案，你确认后才替换原行程。雨天按钮是情景测试，不代表实时天气。")
        }
        OutlinedTextField(value=message,onValueChange={message=it.take(1500)},label={Text("例如：想多看艺术展，少走一点路")},modifier=Modifier.fillMaxWidth(),minLines=2,maxLines=4,enabled=!store.busy,shape=RoundedCornerShape(20.dp),colors=OutlinedTextFieldDefaults.colors(unfocusedContainerColor=Color.White,focusedContainerColor=Color.White,unfocusedBorderColor=Color.Transparent))
        Action("帮我调整",!store.busy && store.trip!=null && message.isNotBlank()){store.generate(message)}
    }
}

@Composable fun LedgerScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    var title by rememberSaveable {mutableStateOf("午餐")}; var amount by rememberSaveable {mutableStateOf("")};var people by rememberSaveable {mutableIntStateOf(2)}
    Page {
        Small("把花费也安排明白 · 本机账本 · AUD")
        Panel {
            OutlinedTextField(value=title,onValueChange={title=it.take(100)},label={Text("消费项目")},modifier=Modifier.fillMaxWidth())
            OutlinedTextField(value=amount,onValueChange={amount=it.take(20)},label={Text("金额 AUD")},singleLine=true,modifier=Modifier.fillMaxWidth(),keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Decimal))
            Row(verticalAlignment=Alignment.CenterVertically){TextButton(onClick={people--},enabled=people>1){Text("−")};Text("参与人数：$people");TextButton(onClick={people++},enabled=people<10){Text("＋")}}
            Action("保存消费"){store.addExpense(title,amount,people)}
        }
        Small("不能整除的分按成员顺序分配，合计始终等于原金额。")
        if(store.expenses.isEmpty()) Small("还没有消费记录。")
        store.expenses.asReversed().forEach {e->Panel {
            Text("${e.optString("title")} · ${Money.display(e.getInt("cents"))}",fontWeight=FontWeight.Bold)
            Small("${e.getInt("people")} 人均分："+Money.shares(e.getInt("cents"),e.getInt("people")).joinToString(" / "){Money.display(it)})
            TextButton(onClick={onDelete(e)}){Text("删除记录")}
        }}
    }
}

@Composable fun SettingsScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    var consent by rememberSaveable {mutableStateOf(false)}
    LaunchedEffect(Unit){store.reloadTravelData()}
    Page {
        Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(18.dp)){Buddy(65);Column(verticalArrangement=Arrangement.spacedBy(6.dp)){Text(ui("你好，旅行家"),style=MaterialTheme.typography.titleLarge,fontWeight=FontWeight.Bold);Small("按自己的节奏，发现悉尼")}}
        Panel {
            Text(ui("语言"),fontWeight=FontWeight.Bold)
            AppLanguage.entries.forEach { language ->
                Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically){
                    RadioButton(selected=store.language==language,onClick={store.chooseLanguage(language)},modifier=Modifier.testTag("language-${language.code}"))
                    TextButton(onClick={store.chooseLanguage(language)}){Text(language.label)}
                }
            }
            Small("語言切換後立即生效，並保存在這台裝置。")
            Small("景點名稱與已儲存的行程內容保留原始語言。")
        }
        Panel {
            Text(ui("旅遊資料來源"),fontWeight=FontWeight.Bold)
            listOf("direct" to "直接來源","zetaris" to "透過 Zetaris").forEach{(route,label)->TextButton(onClick={store.chooseTravelDataSource(route)},modifier=Modifier.testTag("travel-data-$route")){Text((if(store.travelDataSource==route)"✓ "else "")+ui(label))}}
            Small(ui("店家、景點、天氣與地圖設定可切換來源；底圖與導航仍由地圖服務提供。"))
            Small(ui("Zetaris 連線失敗時可手動切回直接來源。"))
            store.dataConnection?.let{Small(it)}
            TextButton(onClick={store.reloadTravelData()}){Text(ui("檢查資料連線"))}
        }
        Panel {
            Text(ui("地图底图"),fontWeight=FontWeight.Bold)
            MapProvider.entries.filter{store.mapLayerIds?.contains(it.name.lowercase()) ?: true}.forEach { provider ->
                Choice(provider.label,store.mapProvider==provider) {store.chooseMapProvider(provider)}
            }
            Small("底图切换不会重新规划或调用后端。OSM 需联网加载图砖；离线示意图不请求图砖。")
            if(store.mapProvider==MapProvider.GOOGLE && !BuildConfig.MAPS_CONFIGURED) Small("Google 地图尚未配置密钥，可选择 OpenStreetMap。")
        }
        Panel {
            OnlineSwitch(store)
            Text(ui("电脑规划服务：{0}",ui(if(store.health?.optString("status")=="ok") "已连接" else "未连接")))
            Text("NVIDIA: "+ui(if(store.health?.optBoolean("nvidiaConfigured")==true) "密钥已配置" else "待配置"))
            Text(ui("Google 服务端：{0}",ui(if(store.health?.optBoolean("googleConfigured")==true) "密钥已配置" else "待配置")))
            Text(ui("Android 地图：{0}",ui(if(BuildConfig.MAPS_CONFIGURED) "密钥已配置" else "未配置，选择后将提示配置")))
            Small("已配置不代表在线调用成功；实际结果以来源和错误提示为准。")
            Action("刷新连接状态",!store.busy,store::reload)
        }
        Panel {
            Text(ui("下一次旅行的默认设置"),fontWeight=FontWeight.Bold)
            Text(ui("出发地点"))
            listOf(Endpoint.quay,Endpoint.central).forEach{e->Choice(e.name,store.profile.start==e){store.profile=store.profile.copy(start=e);store.rememberProfile()}}
            Text(ui("返回地点"))
            listOf(Endpoint.quay,Endpoint.central).forEach{e->Choice(e.name,store.profile.end==e){store.profile=store.profile.copy(end=e);store.rememberProfile()}}
            Text(ui("步数偏好：{0}",store.profile.maxSteps.toString()))
            Slider(value=store.profile.maxSteps.coerceIn(1000,30000).toFloat(),onValueChange={store.profile=store.profile.copy(maxSteps=(it/1000).toInt()*1000);store.rememberProfile()},valueRange=1000f..30000f,steps=28)
            Small("当前已保存行程的调整请到“问 AI”。")
        }
        Panel {
            Text(ui("我同意保存的偏好"),fontWeight=FontWeight.Bold)
            Row(verticalAlignment=Alignment.CenterVertically){Checkbox(checked=consent,onCheckedChange={consent=it});Text(ui("同意在电脑本机保存兴趣和步数偏好"),Modifier.weight(1f))}
            Small("可撤回检索；本机审计历史仍保留。不保存位置轨迹、照片或私人聊天。")
            Action("记住当前偏好",consent && !store.busy){store.saveMemory(consent)}
            store.memories.forEach{m->
                HorizontalDivider();Text(m.optString("summary"))
                TextButton(onClick={val p=m.getJSONObject("profile");store.profile=store.profile.copy(interests=p.array("interests").strings(),maxSteps=p.optInt("maxSteps",10000));store.rememberProfile();store.notice="偏好将用于下一次新建行程"}){Text(ui("用于下次规划"))}
                TextButton(onClick={onDelete(m)},enabled=!store.busy){Text(ui("撤回偏好"))}
            }
        }
        Small("Sydney Buddy · Android MVP\n目前为电脑本机单用户服务。没有多人同步、支付、预订或后台 GPS。")
    }
}
