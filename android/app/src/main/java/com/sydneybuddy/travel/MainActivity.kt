package com.sydneybuddy.travel

import android.app.DatePickerDialog
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import org.json.JSONObject
import java.time.LocalDate

val Green = Color(0xFF138564)
val Ink = Color(0xFF153E35)
val Cream = Color(0xFFF8F7EF)
val Mint = Color(0xFFE0F1E5)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            MaterialTheme(colorScheme = lightColorScheme(primary = Green, onPrimary = Color.White,
                background = Cream, surface = Color.White, onSurface = Ink, secondaryContainer = Mint)) {
                BuddyApp()
            }
        }
    }
}

@Composable fun BuddyApp(store: BuddyStore = viewModel()) {
    val tabs = listOf("探索", "行程", "问 AI", "账本", "我的")
    var delete by remember { mutableStateOf<Pair<String, JSONObject>?>(null) }
    Scaffold(containerColor = Cream,
        topBar = {
            Row(Modifier.fillMaxWidth().statusBarsPadding().padding(horizontal = 22.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                Buddy(44); Spacer(Modifier.width(12.dp))
                Column { Text("Sydney Buddy", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                    Text(if (store.onboarding) "按你的节奏，发现悉尼" else tabs[store.tab], style = MaterialTheme.typography.labelMedium) }
                Spacer(Modifier.weight(1f))
                if (!store.busy && !store.onboarding) TextButton(onClick = store::newTrip) { Text("新建") }
            }
        },
        bottomBar = {
            if (!store.onboarding && store.proposal == null) NavigationBar(containerColor = Color.White) {
                tabs.forEachIndexed { i, title -> NavigationBarItem(selected = store.tab == i,
                    onClick = { if (!store.busy) store.tab = i }, icon = { Text(listOf("⌖", "▤", "✦", "￥", "☺")[i]) }, label = { Text(title) }) }
            }
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
    if (store.proposal != null) BackHandler { if (!store.busy) store.cancelProposal() }
    BackHandler(enabled = store.busy) { /* Keep the current operation visible until it finishes. */ }
    store.error?.let { text -> AlertDialog(onDismissRequest = { store.error = null }, title = { Text("这次没有完成") }, text = { Text(text) },
        confirmButton = { TextButton(onClick = { store.error = null }) { Text("知道了") } }) }
    store.notice?.let { text -> AlertDialog(onDismissRequest = { store.notice = null }, text = { Text(text) },
        confirmButton = { TextButton(onClick = { store.notice = null }) { Text("好的") } }) }
    delete?.let { (type, value) -> AlertDialog(onDismissRequest = { delete = null }, title = { Text("确认删除？") },
        text = { Text(if (type == "memory") "偏好将从检索中撤回，电脑本机审计历史仍保留。" else "此操作将删除这条${if (type == "trip") "行程" else "消费记录"}。") },
        confirmButton = { TextButton(onClick = { when (type) { "trip" -> store.deleteTrip(value); "memory" -> store.deleteMemory(value); else -> store.deleteExpense(value) }; delete = null }) { Text("删除") } },
        dismissButton = { TextButton(onClick = { delete = null }) { Text("取消") } }) }
}

@Composable fun Page(content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(22.dp), verticalArrangement = Arrangement.spacedBy(16.dp), content = content)
}
@Composable fun Panel(content: @Composable ColumnScope.() -> Unit) {
    Surface(shape = RoundedCornerShape(22.dp), color = Color.White, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp), content = content)
    }
}
@Composable fun Heading(text: String) { Text(text, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold) }
@Composable fun Small(text: String) { Text(text, style = MaterialTheme.typography.bodySmall, color = Ink.copy(alpha = .75f)) }
@Composable fun Action(text: String, enabled: Boolean = true, onClick: () -> Unit) {
    Button(onClick = onClick, enabled = enabled, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp), shape = RoundedCornerShape(16.dp)) { Text(text) }
}
@Composable fun Choice(text: String, selected: Boolean, enabled: Boolean = true, onClick: () -> Unit) {
    OutlinedButton(onClick = onClick, enabled = enabled, modifier = Modifier.fillMaxWidth().heightIn(min = 54.dp),
        shape = RoundedCornerShape(16.dp), colors = ButtonDefaults.outlinedButtonColors(containerColor = if (selected) Mint else Color.White)) {
        Text((if (selected) "✓  " else "") + text)
    }
}
@Composable fun Buddy(size: Int) {
    Canvas(Modifier.size(size.dp)) {
        val w = this.size.width; val h = this.size.height
        drawRoundRect(Green, Offset(w*.08f,h*.18f), Size(w*.84f,h*.69f), CornerRadius(w*.22f))
        drawLine(Green, Offset(w*.5f,h*.18f),Offset(w*.5f,h*.05f), w*.045f)
        drawCircle(Green,w*.07f,Offset(w*.5f,h*.05f))
        drawRoundRect(Mint,Offset(w*.2f,h*.32f),Size(w*.6f,h*.33f),CornerRadius(w*.1f))
        drawCircle(Ink,w*.045f,Offset(w*.36f,h*.48f)); drawCircle(Ink,w*.045f,Offset(w*.64f,h*.48f))
        drawLine(Mint,Offset(w*.39f,h*.75f),Offset(w*.61f,h*.75f),w*.04f)
    }
}

@Composable fun Onboarding(store: BuddyStore) {
    var step by rememberSaveable { mutableIntStateOf(0) }
    val p = store.profile
    val context = LocalContext.current
    val titles = listOf("哪天去悉尼玩？", "这次玩几天？", "想用什么节奏？", "什么最让你心动？", "每天准备花多少？", "从哪里开始？", "几点出发返回？", "一起出发吧！")
    Page {
        Row(verticalAlignment = Alignment.CenterVertically) { Buddy(70); Spacer(Modifier.width(16.dp)); Column { Text("第 ${step+1} / 8 步", color = Green); Small("每个选择，都按你舒服的来。") } }
        LinearProgressIndicator(progress = { (step+1)/8f }, modifier = Modifier.fillMaxWidth())
        Heading(titles[step])
        when (step) {
            0 -> { Small("日期按悉尼当地时间安排。")
                Choice(p.date, true, !store.busy) {
                    val date = runCatching { LocalDate.parse(p.date) }.getOrDefault(LocalDate.now())
                    DatePickerDialog(context, { _, y, m, d -> store.profile = p.copy(date = LocalDate.of(y,m+1,d).toString()) }, date.year,date.monthValue-1,date.dayOfMonth).show()
                }
            }
            1 -> (1..3).forEach { n -> Choice("$n 天", p.days == n, !store.busy) { store.profile = p.copy(days = n) } }
            2 -> listOf(5000 to "轻松逛逛",10000 to "刚刚好",18000 to "活力满满").forEach { (n,t) -> Choice("$t · 约 $n 步以内",p.maxSteps == n,!store.busy) { store.profile=p.copy(maxSteps=n) } }
            3 -> { Small("可以多选，也可以随心探索。")
                listOf("风景","美食","艺术","文化","自然","城市").forEach { t -> Choice(t,t in p.interests,!store.busy) {
                    store.profile=p.copy(interests=if (t in p.interests) p.interests-t else p.interests+t)
                } }
            }
            4 -> { listOf(50,100,150).forEach { n -> Choice("A$$n / 天",p.budget==n,!store.busy) {store.profile=p.copy(budget=n)} }
                Text("自定：A$${p.budget}"); Slider(value=p.budget.toFloat(),onValueChange={store.profile=p.copy(budget=(it/10).toInt()*10)},valueRange=0f..300f,steps=29,enabled=!store.busy)
                Small("活动费用估算，不含住宿、机票及全部餐饮。") }
            5 -> { listOf(Endpoint.quay,Endpoint.central).forEach { e -> Choice(e.name,p.start==e,!store.busy){store.profile=p.copy(start=e,end=e)} }; Small("每天返回同一起点，可在“我的”设置下一次旅行的起终点。") }
            6 -> listOf(Triple(600,1080,"10:00–18:00 · 悠闲一天"),Triple(540,1020,"09:00–17:00 · 早一点出发"),Triple(780,1080,"13:00–18:00 · 午后小冒险")).forEach { (s,e,t) -> Choice(t,p.startMinute==s && p.endMinute==e,!store.busy){store.profile=p.copy(startMinute=s,endMinute=e)} }
            else -> { Panel { Text("悉尼 · ${p.days} 天 · ${p.maxSteps} 步以内 / 天",fontWeight=FontWeight.Bold)
                Text("${p.date} · 每日 A$${p.budget}"); Text("${p.start.name}出发 → ${p.end.name}返回"); Text(p.interests.joinToString(" · ").ifEmpty {"随心探索"}) }
                OnlineSwitch(store)
                Small("生成后先展示草案，由你确认后才保存。") }
        }
        Action(if(step==7) "让 Buddy 规划路线" else "选好了，继续",!store.busy) { store.rememberProfile(); if(step<7) step++ else store.generate() }
        if(step>0) TextButton(onClick={step--},enabled=!store.busy) {Text("返回上一步")}
        if(store.saved.isNotEmpty()) TextButton(onClick=store::resumeSaved,enabled=!store.busy) {Text("回到已保存行程")}
    }
    BackHandler(enabled=step>0 && !store.busy) {step--}
}

@Composable fun OnlineSwitch(store: BuddyStore) {
    Row(verticalAlignment=Alignment.CenterVertically) { Column(Modifier.weight(1f)) { Text("使用在线推荐",fontWeight=FontWeight.Bold); Small(if(store.online) "NVIDIA 推荐；有 Google 服务配置时查询在线数据" else "演示模式 · 示例数据，无 AI 调用") }
        Switch(checked=store.online,onCheckedChange={store.online=it},enabled=!store.busy) }
    if(store.online) Small("在线请求会将本次旅行的偏好、日期和起终点发送给 NVIDIA / Google。")
}

@Composable fun HomeScreen(store: BuddyStore) {
    var detail by rememberSaveable {mutableStateOf(false)}
    Page {
        if(store.trip==null) { Heading("你的小冒险，从这里开始"); Action("规划新行程",!store.busy,store::newTrip); Action("刷新连接",!store.busy,store::reload); return@Page }
        val trip = store.trip!!
        Heading(trip.optString("title"))
        Small(modeLabel(trip.optString("mode")))
        val days=trip.array("days").objects()
        Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(6.dp)) { days.forEachIndexed { i,d -> FilterChip(selected=store.dayIndex==i,onClick={store.dayIndex=i},label={Text("第 ${i+1} 天")}) } }
        RouteMap(store.day)
        WeatherPanel(store.weather ?: trip.optJSONObject("weather"),!store.busy,store::refreshWeather)
        val count=listOf("route","weather","bag").count(store::prepared)
        Panel {
            Text(if(count==3) "✓ 出发准备小达人 · 3/3" else "出发前的 3 个小任务 · $count/3",fontWeight=FontWeight.Bold)
            TextButton(onClick={detail=!detail;store.mark("route")}) {Text("${if(store.prepared("route")) "✓ " else ""}查看路线详情")}
            TextButton(onClick=store::refreshWeather,enabled=!store.busy) {Text("${if(store.prepared("weather")) "✓ " else ""}刷新天气")}
            Row(verticalAlignment=Alignment.CenterVertically) { Checkbox(checked=store.prepared("bag"),onCheckedChange={store.mark("bag",true)}); Text("我已备好行李") }
            Small("准备进度按行程保存，没有断签惩罚。")
        }
        if(detail) { days.forEach {DaySummary(it)}; Evidence(trip) }
        else store.day?.let {DaySummary(it)}
        Action("问 Buddy 调整行程",!store.busy){store.tab=2}
    }
}

fun modeLabel(mode:String) = when(mode) {"live"->"在线规划 · 以各项来源和警告为准";"assisted"->"Nemotron 推荐 · 示例地点、天气与路线";else->"演示行程 · 示例数据"}

@Composable fun WeatherPanel(weather:JSONObject?,enabled:Boolean,onRefresh:()->Unit) {
    Panel { Row(verticalAlignment=Alignment.CenterVertically){Text("悉尼天气",fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f));TextButton(onClick=onRefresh,enabled=enabled){Text("刷新天气")}}
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

@Composable fun TripsScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    Page {
        Heading("每次冒险都值得收藏")
        Action("规划新行程",!store.busy,store::newTrip)
        TextButton(onClick=store::reload,enabled=!store.busy){Text("刷新行程")}
        if(store.saved.isEmpty()) Small("还没有保存的行程。")
        store.saved.forEach { t -> Panel {
            Text(t.optString("title"),fontWeight=FontWeight.Bold);Text("${t.getJSONObject("profile").optString("date")} · ${t.array("days").length()} 天")
            Small(modeLabel(t.optString("mode")))
            Action("查看地图",!store.busy){store.select(t);store.tab=0}
            TextButton(onClick={onDelete(t)},enabled=!store.busy){Text("删除行程")}
        } }
    }
}

@Composable fun AgentScreen(store:BuddyStore) {
    var message by rememberSaveable {mutableStateOf("")}
    Page {
        Buddy(85); Heading("一起调整行程")
        OnlineSwitch(store)
        Panel {Text(store.trip?.optString("explanation") ?: "先创建并保存一份行程，Buddy 才能帮你调整。");Small("每次调整先生成草案，你确认后才替换原行程。")}
        Action("少走一点",!store.busy && store.trip!=null){store.generate("少走一点")}
        Action("下雨了 · 情景测试",!store.busy && store.trip!=null){store.generate("下雨了",true)}
        Small("雨天按钮是手动情景测试，不代表实时天气。")
        OutlinedTextField(value=message,onValueChange={message=it.take(1500)},label={Text("想怎么调整？")},modifier=Modifier.fillMaxWidth(),minLines=3,enabled=!store.busy)
        Action("帮我调整",!store.busy && store.trip!=null && message.isNotBlank()){store.generate(message)}
    }
}

@Composable fun LedgerScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    var title by rememberSaveable {mutableStateOf("午餐")}; var amount by rememberSaveable {mutableStateOf("")};var people by rememberSaveable {mutableIntStateOf(2)}
    Page {
        Heading("旅行小账本");Small("手机本机账本 · AUD · 记录与均分，不执行转账")
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
    Page {
        Heading("你好，旅行家")
        Panel {
            OnlineSwitch(store)
            Text("电脑规划服务：${if(store.health?.optString("status")=="ok") "已连接" else "未连接"}")
            Text("NVIDIA：${if(store.health?.optBoolean("nvidiaConfigured")==true) "密钥已配置" else "待配置"}")
            Text("Google 服务端：${if(store.health?.optBoolean("googleConfigured")==true) "密钥已配置" else "待配置"}")
            Text("Android 地图：${if(BuildConfig.MAPS_CONFIGURED) "密钥已配置" else "未配置，显示示意图"}")
            Small("已配置不代表在线调用成功；实际结果以来源和错误提示为准。")
            Action("刷新连接状态",!store.busy,store::reload)
        }
        Panel {
            Text("下一次旅行的默认设置",fontWeight=FontWeight.Bold)
            Text("出发地点")
            listOf(Endpoint.quay,Endpoint.central).forEach{e->Choice(e.name,store.profile.start==e){store.profile=store.profile.copy(start=e);store.rememberProfile()}}
            Text("返回地点")
            listOf(Endpoint.quay,Endpoint.central).forEach{e->Choice(e.name,store.profile.end==e){store.profile=store.profile.copy(end=e);store.rememberProfile()}}
            Text("步数偏好：${store.profile.maxSteps}")
            Slider(value=store.profile.maxSteps.coerceIn(1000,30000).toFloat(),onValueChange={store.profile=store.profile.copy(maxSteps=(it/1000).toInt()*1000);store.rememberProfile()},valueRange=1000f..30000f,steps=28)
            Small("当前已保存行程的调整请到“问 AI”。")
        }
        Panel {
            Text("我同意保存的偏好",fontWeight=FontWeight.Bold)
            Row(verticalAlignment=Alignment.CenterVertically){Checkbox(checked=consent,onCheckedChange={consent=it});Text("同意在电脑本机保存兴趣和步数偏好",Modifier.weight(1f))}
            Small("可撤回检索；本机审计历史仍保留。不保存位置轨迹、照片或私人聊天。")
            Action("记住当前偏好",consent && !store.busy){store.saveMemory(consent)}
            store.memories.forEach{m->
                HorizontalDivider();Text(m.optString("summary"))
                TextButton(onClick={val p=m.getJSONObject("profile");store.profile=store.profile.copy(interests=p.array("interests").strings(),maxSteps=p.optInt("maxSteps",10000));store.rememberProfile();store.notice="偏好将用于下一次新建行程"}){Text("用于下次规划")}
                TextButton(onClick={onDelete(m)},enabled=!store.busy){Text("撤回偏好")}
            }
        }
        Small("Sydney Buddy · Android MVP\n目前为电脑本机单用户服务。没有多人同步、支付、预订或后台 GPS。")
    }
}
