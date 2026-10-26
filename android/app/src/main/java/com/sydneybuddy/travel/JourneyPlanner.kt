package com.sydneybuddy.travel

import android.app.DatePickerDialog
import android.app.TimePickerDialog
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.*
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.time.*
import java.time.format.DateTimeFormatter
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.math.ceil

fun transportLabel(mode:String)=when(mode){"walk"->"步行";"drive"->"开车";"transit"->"大众运输";"bus"->"公车";"rail"->"火车";"metro"->"地铁";"tram"->"轻轨";"ferry"->"渡轮";else->"交通"}
fun transportGlyph(mode:String)=when(mode){"walk"->"🚶";"drive"->"🚗";"bus"->"🚌";"ferry"->"⛴";else->"🚆"}
fun transportColor(mode:String)=when(mode){"walk"->Color(0xFF1976D2);"drive"->Green;else->Color(0xFF1976D2)}
fun journeyTime(raw:String)=runCatching{OffsetDateTime.parse(raw).atZoneSameInstant(ZoneId.of("Australia/Sydney")).format(DateTimeFormatter.ofPattern("HH:mm"))}.getOrDefault("时间未提供")
data class JourneyMapSection(val mode:String,val points:List<Pair<Double,Double>>,val boarding:String="",val alighting:String="",val line:String?=null) {
    val stops get()=if(mode in listOf("walk","drive") || points.size<2)emptyList()else listOf(points.first() to "上车 · $boarding",points.last() to "下车 · $alighting")
}
data class JourneyStep(val mode:String,val line:String?,val headsign:String?,val from:String,val to:String,val departureTime:String,val arrivalTime:String,val realtime:Boolean,val stopCount:Int?,val meters:Double,val seconds:Double,val waitSeconds:Double,val platform:String?,val agency:String?,val geometry:List<Pair<Double,Double>>) {
    val title get()="${transportGlyph(mode)} ${transportLabel(mode)}"+(if(mode in listOf("walk","drive"))"" else " · "+line?.takeIf{it.isNotBlank()}.orEmpty().ifEmpty{"路线号码未提供"})
    companion object {
        fun parse(o:JSONObject):JourneyStep {
            fun nullable(key:String)=if(o.isNull(key))null else o.optString(key)
            val points=o.getJSONArray("geometry").objects().map{it.getDouble("lat") to it.getDouble("lng")}
            require(points.all{it.first in -90.0..90.0 && it.second in -180.0..180.0})
            return JourneyStep(o.getString("mode"),nullable("line"),nullable("headsign"),o.getString("from"),o.getString("to"),o.getString("departureTime"),o.getString("arrivalTime"),o.getBoolean("realtime"),if(o.isNull("stopCount"))null else o.getInt("stopCount"),o.getDouble("meters"),o.getDouble("seconds"),o.getDouble("waitSeconds"),nullable("platform"),nullable("agency"),points)
        }
    }
}
data class JourneyLeg(val index:Int,val mode:String,val from:String,val to:String,val departureTime:String,val arrivalTime:String,val staySeconds:Double,val steps:List<JourneyStep>)
data class JourneyPlan(val departureTime:String,val arrivalTime:String,val travelSeconds:Double,val elapsedSeconds:Double,val meters:Double,val legs:List<JourneyLeg>,val warnings:List<String>,val source:String) {
    val sections get()=legs.flatMap{it.steps}.map{JourneyMapSection(it.mode,it.geometry,it.from,it.to,it.line)}
    val overview get()=OverviewRoute(sections.flatMap{it.points},meters,travelSeconds,legs.map{it.steps.sumOf{step->step.seconds+step.waitSeconds}})
    companion object {
        fun parse(raw:String):JourneyPlan {
            val o=JSONObject(raw)
            val legs=o.getJSONArray("legs").objects().map{l->JourneyLeg(l.getInt("index"),l.getString("mode"),l.getString("from"),l.getString("to"),l.getString("departureTime"),l.getString("arrivalTime"),l.getDouble("staySeconds"),l.getJSONArray("steps").objects().map(JourneyStep::parse))}
            require(legs.isNotEmpty() && legs.all{it.steps.isNotEmpty()})
            val warnings=o.getJSONArray("warnings");return JourneyPlan(o.getString("departureTime"),o.getString("arrivalTime"),o.getDouble("travelSeconds"),o.getDouble("elapsedSeconds"),o.getDouble("meters"),legs,(0 until warnings.length()).map{warnings.getString(it)},o.getString("source"))
        }
    }
}
class JourneyModel(private val client:OkHttpClient=OkHttpClient.Builder().addInterceptor(BackendAccess.interceptor).callTimeout(95,TimeUnit.SECONDS).followRedirects(false).build()) {
    var plan by mutableStateOf<JourneyPlan?>(null);private set
    var busy by mutableStateOf(false);private set
    var error by mutableStateOf<String?>(null);private set
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)
    private var job:Job?=null
    fun invalidate(){job?.cancel();job=null;busy=false;plan=null;error=null}
    fun close(){invalidate();scope.cancel()}
    fun load(day:JSONObject,modes:List<String>,departure:OffsetDateTime,base:String,routingProvider:String="open",mapProvider:String="osm",contentSource:String="open"){
        invalidate();busy=true
        val points=JSONArray(walkingPoints(day).mapIndexed{i,p->JSONObject().put("name",p.name).put("lat",p.lat).put("lng",p.lng).put("stayMinutes",if(i==0)0 else p.stay)})
        val payload=JSONObject().put("points",points).put("modes",JSONArray(modes)).put("departureTime",departure.toString()).put("contentSource",contentSource).put("routingProvider",routingProvider).put("mapProvider",mapProvider)
        job=scope.launch {
            try {
                val result=withContext(Dispatchers.IO){
                    val call=client.newCall(Request.Builder().url("$base/journey/plan").post(payload.toString().toRequestBody("application/json".toMediaType())).build())
                    val response=suspendCancellableCoroutine<Response>{c->
                        c.invokeOnCancellation{call.cancel()};call.enqueue(object:Callback{
                            override fun onFailure(call:Call,e:java.io.IOException){if(!c.isCancelled)c.resumeWithException(e)}
                            override fun onResponse(call:Call,response:Response){c.resume(response){_,value,_->value.close()}}
                        })
                    }
                    response.use{val body=it.body.string();if(!it.isSuccessful)error(runCatching{JSONObject(body).optString("error")}.getOrDefault("").ifBlank{"交通规划服务暂不可用。"});JourneyPlan.parse(body)}
                }
                ensureActive();plan=result;busy=false
            }catch(e:CancellationException){throw e}
            catch(e:Exception){busy=false;error=e.message ?: "交通查询失败，请稍后重试。"}
        }
    }
}
@Composable fun JourneyPlannerDialog(day:JSONObject,base:String,provider:MapProvider,onClose:()->Unit,onWalkNavigation:()->Unit,fixture:JourneyPlan?=null,suppliedModel:JourneyModel?=null,contentSource:String="open",onPlan:(JourneyPlan)->Unit={}) {
    val context=LocalContext.current
    val model=remember(day.toString(),suppliedModel){suppliedModel ?: JourneyModel()}
    DisposableEffect(model){onDispose{model.close()}}
    var modes by remember(day.toString()){mutableStateOf(fixture?.legs?.map{it.mode} ?: List(day.array("stops").length()+1){"walk"})}
    var departure by remember{mutableStateOf(ZonedDateTime.now(ZoneId.of("Australia/Sydney")))}
    var routingProvider by remember(provider){mutableStateOf(if(provider==MapProvider.GOOGLE)"google" else "open")}
    LaunchedEffect(provider){model.invalidate()}
    var details by remember{mutableStateOf(true)}
    val result=fixture ?: model.plan
    val currentOnPlan by rememberUpdatedState(onPlan)
    LaunchedEffect(model.plan){if(fixture==null)model.plan?.let{currentOnPlan(it)}}
    val points=walkingPoints(day)
    fun setModes(value:List<String>){model.invalidate();modes=value}
    Dialog(onDismissRequest=onClose,properties=DialogProperties(usePlatformDefaultWidth=false)) {
        Box(Modifier.fillMaxSize().background(Mint).testTag("journey-planner")) {
            RouteMap(day,Modifier.fillMaxSize(),true,provider,overview=result?.overview,expanded=true,sections=result?.sections ?: emptyList())
            Column(Modifier.fillMaxSize().padding(horizontal=16.dp,vertical=12.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                Surface(shape=RoundedCornerShape(20.dp),color=Color.White){Row(Modifier.fillMaxWidth().padding(14.dp),verticalAlignment=Alignment.CenterVertically){Text("交通路线",modifier=Modifier.weight(1f),style=MaterialTheme.typography.titleMedium);TextButton(onClick=onClose){Text(ui("关闭"))}}}
                if(fixture!=null)Text("测试资料 · TEST 公车非真实班次",Modifier.background(Color.Yellow).padding(8.dp),fontSize=12.sp)
                if(!details)Surface(shape=RoundedCornerShape(18.dp),color=Color.White){Column(Modifier.fillMaxWidth().padding(14.dp)){Text("起 · ${points.first().name}");Text("↓ ${points.size-2} 个停留点",fontSize=12.sp);Text("终 · ${points.last().name}")}}
                Spacer(Modifier.weight(1f))
                Surface(shape=RoundedCornerShape(24.dp),color=Color.White,shadowElevation=6.dp){
                    Column(Modifier.padding(16.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                        Row(verticalAlignment=Alignment.CenterVertically){Text(result?.let{"${ceil(it.travelSeconds/60).toInt()} 分钟 · ${it.meters.toInt()} 米"} ?: "选择每段交通方式",Modifier.weight(1f),style=MaterialTheme.typography.titleMedium);TextButton(onClick={details=!details}){Text(if(details)"收起" else "路线详情")}}
                        result?.let{Text(it.source,fontSize=11.sp,color=Muted)}
                        if(details)Column(Modifier.fillMaxWidth().heightIn(max=350.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                            if(result==null) {
                            Text("路线资料来源",fontSize=12.sp)
                            Row(Modifier.horizontalScroll(rememberScrollState()),horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                                (if(contentSource=="google")listOf("google") else listOf("open","nsw")+if(provider==MapProvider.GOOGLE)listOf("google")else emptyList()).forEach { source ->
                                    FilterChip(selected=routingProvider==source,onClick={model.invalidate();routingProvider=source},label={Text(when(source){"open"->"开源 Valhalla / OTP";"nsw"->"NSW + Valhalla";else->"Google Routes"},fontSize=11.sp)})
                                }
                            }
                            TextButton(onClick={
                                DatePickerDialog(context,{_,year,month,date->
                                    val chosen=LocalDate.of(year,month+1,date)
                                    TimePickerDialog(context,{_,hour,minute->model.invalidate();departure=ZonedDateTime.of(chosen,LocalTime.of(hour,minute),ZoneId.of("Australia/Sydney"))},departure.hour,departure.minute,true).show()
                                },departure.year,departure.monthValue-1,departure.dayOfMonth).show()
                            }){Text("出发（悉尼） ${departure.format(DateTimeFormatter.ofPattern("MM-dd HH:mm"))}",fontSize=12.sp)}
                            Row(Modifier.horizontalScroll(rememberScrollState()),horizontalArrangement=Arrangement.spacedBy(8.dp)){listOf("walk","drive","transit").forEach{mode->FilterChip(selected=modes.all{it==mode},onClick={setModes(List(modes.size){mode})},label={Text("${transportGlyph(mode)} ${transportLabel(mode)}")})}}
                            } else Row {Text("出发 ${journeyTime(result.departureTime)} · 悉尼",Modifier.weight(1f),fontSize=12.sp);if(fixture==null)TextButton(onClick=model::invalidate){Text("调整交通")}}
                            points.forEachIndexed{i,p->
                                Row(horizontalArrangement=Arrangement.spacedBy(10.dp),verticalAlignment=Alignment.CenterVertically){Text(if(i==0)"起" else if(i==points.lastIndex)"终" else "$i",Modifier.background(Mint,CircleShape).padding(8.dp));Text(p.name,style=MaterialTheme.typography.titleSmall)}
                                if(i<modes.size){
                                    if(result==null) Row(horizontalArrangement=Arrangement.spacedBy(6.dp)){listOf("walk","drive","transit").forEach{mode->FilterChip(selected=modes[i]==mode,onClick={setModes(modes.toMutableList().also{it[i]=mode})},label={Text(transportLabel(mode),fontSize=12.sp)},modifier=Modifier.testTag("journey-mode-$i-$mode"))}}
                                    result?.legs?.firstOrNull{it.index==i}?.let{leg->
                                        Text("${journeyTime(leg.departureTime)} → ${journeyTime(leg.arrivalTime)}"+if(leg.staySeconds>0)" · 到站停留 ${leg.staySeconds.toInt()/60} 分" else "",fontSize=12.sp,color=Muted)
                                        leg.steps.forEach{JourneyStepRow(it)}
                                    }
                                }
                            }
                            result?.warnings?.forEach{Text(it,fontSize=11.sp,color=Muted)}
                            Text(result?.source ?: "步行：蓝色密集小圆点 · 开车：绿色细实线 · 公共交通：蓝色细实线。尚未查询时仅显示站点，不画直线；路线沿服务回传的道路线形。",fontSize=11.sp,color=Muted)
                        }
                        model.error?.let{Text(it,color=Color.Red,fontSize=12.sp,modifier=Modifier.testTag("journey-error"))}
                        Row(horizontalArrangement=Arrangement.spacedBy(8.dp),verticalAlignment=Alignment.CenterVertically){
                            Button(onClick={model.load(day,modes,departure.toOffsetDateTime(),base,routingProvider,provider.name.lowercase(),contentSource)},enabled=!model.busy && fixture==null){Text(if(model.busy)"正在查询交通…" else if(result==null)"规划交通路线" else "重新查询")}
                            if(model.busy)TextButton(onClick=model::invalidate){Text(ui("取消"))}
                            if(fixture==null && routingProvider=="open" && result!=null && modes.all{it=="walk"})TextButton(onClick=onWalkNavigation){Text("开始步行导航")}
                        }
                    }
                }
            }
        }
    }
}
@Composable private fun JourneyStepRow(step:JourneyStep) {
    Column(Modifier.fillMaxWidth().background(transportColor(step.mode).copy(alpha=.08f),RoundedCornerShape(12.dp)).padding(10.dp),verticalArrangement=Arrangement.spacedBy(5.dp)) {
        Row{Text(step.title,color=transportColor(step.mode),style=MaterialTheme.typography.titleSmall,modifier=Modifier.weight(1f));Text("${ceil(step.seconds/60).toInt()} 分",fontSize=12.sp)}
        if(step.waitSeconds>0)Text("候车／衔接约 ${ceil(step.waitSeconds/60).toInt()} 分",fontSize=12.sp)
        Text("${step.from} → ${step.to}",fontSize=12.sp)
        step.headsign?.takeIf{it.isNotBlank()}?.let{Text("往 $it",fontSize=12.sp)}
        if(step.mode !in listOf("walk","drive")){
            Text("${journeyTime(step.departureTime)} 上车 · ${journeyTime(step.arrivalTime)} 下车"+(step.stopCount?.let{" · $it 站"} ?: "")+(step.platform?.takeIf{it.isNotBlank()}?.let{" · 站台 $it"} ?: ""),fontSize=12.sp)
            Text(if(step.realtime)"实时更新" else "时刻表班次 · 非实时",fontSize=11.sp,color=Muted)
        }
        if(step.geometry.size<2)Text("此段未提供路线线形，请按站名确认。",fontSize=11.sp,color=Muted)
    }
}
