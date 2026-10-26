package com.sydneybuddy.travel

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.border
import androidx.compose.animation.core.*
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.*
import androidx.compose.ui.unit.sp
import android.animation.ValueAnimator
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp
import androidx.compose.ui.text.font.FontWeight
import kotlinx.coroutines.*
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID
import kotlin.math.*

internal class ScheduleController(val store:BuddyStore,val scope:CoroutineScope,val requester:suspend (JSONObject)->JSONObject = {store.scheduleRequest(it)}) {
    var trip by mutableStateOf(store.proposal!!.trip.copy())
    var state by mutableStateOf(trip.optJSONObject("schedule")?.copy())
    var dayIndex by mutableStateOf(0)
    var busy by mutableStateOf(false)
    var dirty by mutableStateOf(false)
    var error by mutableStateOf<String?>(null)
    var notice by mutableStateOf<String?>(null)
    val seed=trip.copy()
    private val history=ArrayDeque<JSONObject>()
    private var job:Job?=null
    private var generation=0
    private var pendingSnapshot:JSONObject?=null
    private var lastNotice:String?=null
    fun request(regenerate:Boolean=false) {
        job?.cancel();val token=++generation;busy=true;dirty=true
        val value=state?.copy()
        val body=JSONObject().put("trip",trip)
        value?.let {body.put("candidates",it.array("candidates")).put("settings",it.getJSONObject("settings")).put("version",it.optInt("version")).put("rainy",it.optBoolean("rainy"));if(!regenerate)body.put("days",it.getJSONArray("days"))}
        job=scope.launch {
            try {
                val result=requester(body)
                if(token!=generation)return@launch
                trip=result;state=result.getJSONObject("schedule");busy=false;dirty=false
                val warnings=state!!.array("issues").objects().filter{it.optString("code") in listOf("steps","budget","total-budget","time-budget")}
                if(warnings.isNotEmpty()){val text=warnings.joinToString("\n"){it.optString("message")};if(text!=lastNotice)notice=text;lastNotice=text}else lastNotice=null
            }catch(e:CancellationException){throw e}catch(e:Exception){if(token==generation){busy=false;error=e.message ?: "Could not calculate the plan. Your draft has been kept."}}
        }
    }
    fun edit(change:(JSONObject)->Unit) {
        val value=state?.copy() ?: return
        history.addLast(pendingSnapshot ?: trip.copy().put("schedule",value.copy()));pendingSnapshot=null;if(history.size>25)history.removeFirst()
        change(value);state=value;request()
    }
    fun retime(id:String,start:Int,duration:Int) {
        if(state!!.array("days").getJSONObject(dayIndex).array("activities").objects().firstOrNull{it.optString("id")==id}?.optBoolean("locked")!=false)return
        edit {s->val day=s.array("days").getJSONObject(dayIndex);val items=day.array("activities").objects()
            items.first{it.optString("id")==id}.put("fixedStart",start.coerceIn(0,1439)).put("scheduledStart",start.coerceIn(0,1439)).put("duration",duration.coerceIn(5,240))
            day.put("activities",JSONArray(items.sortedBy{if(it.isNull("fixedStart"))it.optInt("scheduledStart")else it.optInt("fixedStart")}))
        }
    }
    fun reorder(id:String,target:Int) {edit {s->val day=s.array("days").getJSONObject(dayIndex);val items=day.array("activities").objects().toMutableList();val i=items.indexOfFirst{it.optString("id")==id};if(i>=0){val a=items.removeAt(i);items.add(target.coerceIn(0,items.size),a);day.put("activities",JSONArray(items))}}}
    fun undo(){if(history.isNotEmpty()){trip=history.removeLast();state=trip.getJSONObject("schedule");request()}}
    fun restore(){trip=seed.copy();state=trip.optJSONObject("schedule");request(state==null)}
    fun toggleMust(candidate:JSONObject){
        pendingSnapshot=trip.copy().put("schedule",state!!.copy())
        val profile=trip.getJSONObject("profile");val id=candidate.getString("id");val list=profile.array("mustVisit").strings().toMutableList()
        if(id in list)list.remove(id)else list.add(id)
        trip=trip.copy().put("profile",profile.copy().put("mustVisit",JSONArray(list)))
        if(state!!.array("days").objects().flatMap{it.array("activities").objects()}.none{it.optString("placeId")==id})add(candidate)else edit{}
    }
    fun importPlaces(points:List<JSONObject>) {
        if(busy||state==null)return
        val existing=state!!.array("candidates").objects().map{it.optString("id")}.toSet()
        val additions=points.filter{it.optString("id") !in existing};if(additions.isEmpty())return
        edit{s->s.put("candidates",JSONArray(s.array("candidates").objects()+additions.map{it.copy()}))}
    }
    fun place(candidate:JSONObject,minute:Int,moveAcrossDays:Boolean=false) {
        val s=state ?: return;val day=s.array("days").getJSONObject(dayIndex)
        if(busy||minute<day.optInt("startMinute")||minute>=day.optInt("endMinute"))return
        val id=candidate.getString("id");val existing=day.array("activities").objects().firstOrNull{it.optString("placeId")==id}
        if(existing!=null){retime(existing.getString("id"),minute,existing.optInt("duration"));return}
        val source=s.array("days").objects().firstOrNull{d->d.array("activities").objects().any{it.optString("placeId")==id}}
        if(source!=null){
            val original=source.array("activities").objects().first{it.optString("placeId")==id}
            if(!moveAcrossDays||original.optBoolean("locked"))return
            val sourceIndex=s.array("days").objects().indexOf(source)
            edit {value->
                val from=value.array("days").getJSONObject(sourceIndex)
                from.put("activities",JSONArray(from.array("activities").objects().filter{it.optString("id")!=original.optString("id")}))
                val target=value.array("days").getJSONObject(dayIndex);val items=target.array("activities").objects().toMutableList()
                val moved=original.copy().put("fixedStart",minute).put("scheduledStart",minute)
                val index=items.indexOfFirst{(if(it.isNull("fixedStart"))it.optInt("scheduledStart")else it.optInt("fixedStart"))>minute}
                items.add(if(index<0)items.size else index,moved);target.put("activities",JSONArray(items))
            };return
        }
        edit {value->val d=value.array("days").getJSONObject(dayIndex);val items=d.array("activities").objects().toMutableList()
            val a=JSONObject().put("id",UUID.randomUUID().toString()).put("kind",poiActivityKind(candidate)).put("placeId",id).put("name",candidate.getString("name")).put("duration",candidate.optInt("visitMinutes",60)).put("fixedStart",minute).put("scheduledStart",minute)
            val index=items.indexOfFirst{(if(it.isNull("fixedStart"))it.optInt("scheduledStart")else it.optInt("fixedStart"))>minute}
            items.add(if(index<0)items.size else index,a);d.put("activities",JSONArray(items))
        }
    }
    fun add(candidate:JSONObject){edit{s->val day=s.array("days").getJSONObject(dayIndex);val items=day.array("activities").objects().toMutableList();val a=JSONObject().put("id",UUID.randomUUID().toString()).put("kind",poiActivityKind(candidate)).put("placeId",candidate.getString("id")).put("name",candidate.getString("name")).put("duration",candidate.optInt("visitMinutes",60));val i=items.indexOfFirst{it.optString("kind") in listOf("stay","bag-pickup")};items.add(if(i<0)items.size else i,a);day.put("activities",JSONArray(items))}}
    fun stop(){job?.cancel();generation++}
}
private fun scheduleTimeLabel(minute:Int)="%02d:%02d".format(minute/60,minute%60)
private fun scheduleTimeMinute(value:String):Int {val parts=value.trim().split(":");require(parts.size==2);val hour=parts[0].toInt();val minute=parts[1].toInt();require(hour in 0..24 && minute in 0..59 && (hour<24 || minute==0));return hour*60+minute}
private fun kindLabel(kind:String)=when(kind){"meal"->"Dine";"restroom"->"Toilets";"rest"->"Rest";"stay"->"Check in";"checkout"->"Check-out";"bag-drop"->"Store";"bag-pickup"->"Pickup";else->"Sights"}
@Composable private fun Radar(state:JSONObject) {
    val axes=state.array("radar").objects()
    Panel {
        Text("Travel preferences",fontWeight=FontWeight.Bold)
        Canvas(Modifier.fillMaxWidth().height(155.dp)) {
            val center=Offset(size.width/2,size.height/2);val radius=min(size.width,size.height)*.43f
            fun point(i:Int,v:Float):Offset {val a=i*2*PI/axes.size-PI/2;return center+Offset(cos(a).toFloat()*radius*v,sin(a).toFloat()*radius*v)}
            fun polygon(values:List<Float>)=Path().apply{values.forEachIndexed{i,v->val p=point(i,v);if(i==0)moveTo(p.x,p.y)else lineTo(p.x,p.y)};close()}
            for(v in listOf(.25f,.5f,.75f,1f))drawPath(polygon(List(axes.size){v}),Green.copy(alpha=.2f),style=Stroke(1f))
            val shape=polygon(axes.map{it.optDouble("value").toFloat()});drawPath(shape,Green.copy(alpha=.25f));drawPath(shape,Green,style=Stroke(3f))
        }
        Small(axes.joinToString(" · "){it.optString("name")+" "+(it.optDouble("value")*100).roundToInt()})
    }
}
@Composable internal fun ScheduleEditorScreen(store:BuddyStore,suppliedController:ScheduleController?=null) {
    val scope=rememberCoroutineScope();val controller=remember{suppliedController ?: ScheduleController(store,scope)}
    var candidates by remember{mutableStateOf(false)};var settings by remember{mutableStateOf(false)};var editing by remember{mutableStateOf<JSONObject?>(null)};var confirm by remember{mutableStateOf(false)};var showRadar by remember{mutableStateOf(false)}
    LaunchedEffect(Unit){if(controller.state==null)controller.request(true)}
    DisposableEffect(Unit){onDispose{controller.stop()}}
    val state=controller.state
    Page {
        Heading("My timeline")
        Small("Start with a suggestion, then adjust it to your pace. Save when you're ready.")
        if(state!=null){
            Small(state.optString("source"));TextButton(onClick={showRadar=!showRadar}){Text(if(showRadar)"Hide travel preference chart"else "Travel preference chart")};if(showRadar)Radar(state)
            Row {TextButton(onClick={settings=true}){Text("Preferences & Accommodation")};Spacer(Modifier.weight(1f));TextButton(onClick={candidates=true}){Text("Places")}}
            val day=state.array("days").getJSONObject(controller.dayIndex)
            ScheduleAllowanceSummary(day,state,controller.dayIndex+1)
            Row{TextButton(onClick=controller::undo){Text("Undo")};TextButton(onClick=controller::restore){Text("Back to original suggestion")}}
            if(controller.busy)LinearProgressIndicator(Modifier.fillMaxWidth())
            state.array("issues").objects().filter{it.optInt("day")==controller.dayIndex||it.optInt("day")==-1}.forEach{Small((if(it.optBoolean("blocking"))"⚠ " else "· ")+it.optString("message"))}
            Text("Depart ${day.getJSONObject("start").optString("name")}",fontWeight=FontWeight.Bold)
            ScheduleWorkbench(controller,onEdit={editing=it.copy()},onAdd={candidates=true})
            Text("${day.getJSONObject("end").optString("name")} · ${day.optString("finishTime")}")
            TextButton(onClick={controller.edit{it.put("rainy",!it.optBoolean("rainy"))}}){Text(if(state.optBoolean("rainy"))"Switch to sunny example" else "Test rain impact")}
            TextButton(onClick={controller.request(true)}){Text("Suggest new activities using my preferences")}
            Action("Confirm and save",!controller.busy&&!controller.dirty&&!state.array("issues").objects().any{it.optBoolean("blocking")}){confirm=true}
        }else {if(controller.busy)Text("Creating your first multi-day plan…")else TextButton(onClick={controller.request(true)}){Text(ui("Retry"))}}
        TextButton(onClick={controller.stop();store.cancelProposal()}){Text("Discard draft")}
    }
    controller.notice?.let{text->AlertDialog(onDismissRequest={controller.notice=null},title={Text(ui("Please note"))},text={Text(text)},confirmButton={TextButton(onClick={controller.notice=null}){Text("Continue adjusting")}})}
    controller.error?.let{text->AlertDialog(onDismissRequest={controller.error=null},title={Text("Calculation Incomplete")},text={Text(text)},confirmButton={TextButton(onClick={controller.error=null;controller.request()}){Text(ui("Retry"))}},dismissButton={TextButton(onClick={controller.error=null}){Text("Keep the draft")}})}
    if(confirm)AlertDialog(onDismissRequest={confirm=false},text={Text("Save this draft? Estimates and details awaiting confirmation will be included.")},confirmButton={TextButton(onClick={confirm=false;store.applyScheduleDraft(controller.trip);store.accept()}){Text("Confirm save")}},dismissButton={TextButton(onClick={confirm=false}){Text(ui("Cancel"))}})
    if(candidates&&state!=null)ScheduleCandidates(controller){candidates=false}
    if(settings&&state!=null)ScheduleSettingsDialog(controller){settings=false}
    editing?.let{a->ScheduleActivityDialog(a,state!!.array("days").length(),controller.dayIndex,onDismiss={editing=null},onDelete={controller.edit{s->val d=s.array("days").getJSONObject(controller.dayIndex);d.put("activities",JSONArray(d.array("activities").objects().filterNot{it.optString("id")==a.optString("id")}))};editing=null}){updated,target->controller.edit{s->val source=s.array("days").getJSONObject(controller.dayIndex);val list=source.array("activities").objects();if(target==controller.dayIndex)source.put("activities",JSONArray(list.map{if(it.optString("id")==updated.optString("id"))updated else it}))else{source.put("activities",JSONArray(list.filterNot{it.optString("id")==updated.optString("id")}));s.array("days").getJSONObject(target).array("activities").put(updated)}};editing=null}}
}

@Composable private fun ScheduleCandidates(c:ScheduleController,onDismiss:()->Unit) {
    val state=c.state ?: return
    val selected=state.array("days").objects().flatMap{it.array("activities").objects()}.map{it.optString("placeId")}.toSet()
    AlertDialog(onDismissRequest=onDismiss,title={Text("Places and practical stops")},text={Column(Modifier.heightIn(max=480.dp).verticalScroll(rememberScrollState())){
        Small("Places are ranked by interests, weather and area. Adding one recalculates travel and queue time.")
        state.array("candidates").objects().forEach{p->Row(verticalAlignment=Alignment.CenterVertically){Checkbox(checked=p.optString("id") in selected,onCheckedChange={checked->if(checked)c.add(p)else c.edit{s->s.array("days").objects().forEach{d->d.put("activities",JSONArray(d.array("activities").objects().filterNot{it.optString("placeId")==p.optString("id")}))}}});Column{Text(p.optString("name"));Small(p.optString("reason"));Small("A$${p.optDouble("cost")} · Tour ${p.optInt("visitMinutes")} min · Queue unknown");TextButton(onClick={c.toggleMust(p)}){Text(if(p.optString("id") in c.trip.getJSONObject("profile").array("mustVisit").strings())"★ Must-go"else "☆ Set as Must-go")}}}}
        HorizontalDivider();Text("Add a practical stop (location to confirm)")
        listOf("meal","restroom","rest","bag-drop","bag-pickup").forEach{kind->TextButton(onClick={c.edit{s->val day=s.array("days").getJSONObject(c.dayIndex);val point=day.getJSONObject("start");day.array("activities").put(JSONObject().put("id",UUID.randomUUID().toString()).put("kind",kind).put("name",kindLabel(kind)+" · To confirm").put("lat",point.getDouble("lat")).put("lng",point.getDouble("lng")).put("duration",if(kind=="meal")45 else 10).put("cost",if(kind=="meal")s.getJSONObject("settings").optDouble("mealCost")else 0))}}){Text("＋${kindLabel(kind)}")}}
    }},confirmButton={TextButton(onClick=onDismiss){Text(ui("Done"))}})
}
@Composable private fun ScheduleSettingsDialog(c:ScheduleController,onDismiss:()->Unit) {
    val original=c.state!!
    val values=remember{original.getJSONObject("settings").copy()}
    val stays=remember{original.array("days").objects().map{it.getJSONObject("stay").copy()}}
    var pace by remember{mutableStateOf(values.optString("pace"))};var luggage by remember{mutableStateOf(values.optBoolean("luggage"))}
    val fields=remember{mutableStateMapOf<String,String>().apply{for(k in listOf("maxSteps","timeLimit","restroomInterval","mealCost","dailyBudget","totalBudget"))put(k,values.get(k).toString());stays.forEachIndexed{i,s->for(k in listOf("name","lat","lng","cost","checkIn","checkOut"))put("$i:$k",if(k in listOf("checkIn","checkOut"))scheduleTimeLabel(s.optInt(k))else s.optString(k));put("$i:startMinute",scheduleTimeLabel(original.array("days").getJSONObject(i).optInt("startMinute")));put("$i:endMinute",scheduleTimeLabel(original.array("days").getJSONObject(i).optInt("endMinute")))}}}
    var invalid by remember{mutableStateOf(false)}
    AlertDialog(onDismissRequest=onDismiss,title={Text("Preferences & Accommodation")},text={Column(Modifier.heightIn(max=500.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(8.dp)){
        Small("Fine-tune your preferences here while keeping your conversation.")
        Row{listOf("relaxed" to "Relaxed","balanced" to "Moderate","packed" to "Compact").forEach{(value,label)->TextButton(onClick={pace=value}){Text((if(pace==value)"✓ "else "")+label)}}}
        listOf("maxSteps" to "Daily step upper limit","timeLimit" to "Daily travel and queue time (min)","restroomInterval" to "Restroom interval (min)","mealCost" to "Per meal reserved A$","dailyBudget" to "Daily budget A$","totalBudget" to "Trip budget (AUD)").forEach{(key,label)->OutlinedTextField(value=fields[key] ?: "",onValueChange={fields[key]=it},label={Text(label)},singleLine=true)}
        Row(verticalAlignment=Alignment.CenterVertically){Checkbox(luggage,{luggage=it});Text("Include bag drop-off and pickup in new suggestions")}
        val lodgingOptions=(c.store.poiToolbox+original.array("candidates").objects()).filter{it.optString("category")=="住宿"}.distinctBy{it.optString("id")}
        stays.forEachIndexed{i,_->Text("Night ${i+1} stay (no accommodation charge on the final day)",fontWeight=FontWeight.Bold);if(lodgingOptions.isNotEmpty()){Small("Choose a stay from your toolbox to use its estimated nightly rate:");Row(Modifier.horizontalScroll(rememberScrollState()),horizontalArrangement=Arrangement.spacedBy(6.dp)){lodgingOptions.forEach{item->val rate=item.optInt("nightlyRateAud",item.optInt("estimatedSpendAud",185));OutlinedButton(onClick={fields["$i:name"]=item.optString("name");fields["$i:lat"]=item.optDouble("lat").toString();fields["$i:lng"]=item.optDouble("lng").toString();fields["$i:cost"]=rate.toString()}){Text("${item.optString("name")} (A$$rate/night)",fontSize=11.sp)}}}};listOf("startMinute" to "Day starts HH:mm","endMinute" to "Day ends HH:mm","name" to "Accommodation name / Tentative area","lat" to "Latitude","lng" to "Longitude","cost" to "Nightly accommodation (AUD, one person)","checkIn" to "Earliest check-in HH:mm","checkOut" to "Latest check-out HH:mm").forEach{(key,label)->OutlinedTextField(value=fields["$i:$key"] ?: "",onValueChange={fields["$i:$key"]=it},label={Text(label)},singleLine=true)}}
        Small("After changing pace or luggage preferences, request new suggestions. Confirm accommodation and facilities yourself; this does not book a room.")
        if(invalid)Text("Enter valid numbers.",color=Color.Red)
    }},confirmButton={TextButton(onClick={runCatching{
        for(key in listOf("maxSteps","timeLimit","restroomInterval"))values.put(key,fields[key]!!.toInt())
        for(key in listOf("mealCost","dailyBudget","totalBudget"))values.put(key,fields[key]!!.toDouble())
        values.put("pace",pace).put("luggage",luggage)
        stays.forEachIndexed{i,stay->for(key in listOf("name","lat","lng","cost","checkIn","checkOut")){val v=fields["$i:$key"]!!;stay.put(key,if(key=="name")v else if(key in listOf("checkIn","checkOut"))scheduleTimeMinute(v)else v.toDouble())}}
        c.edit{s->s.put("settings",values);stays.forEachIndexed{i,stay->s.array("days").getJSONObject(i).put("stay",stay).put("startMinute",scheduleTimeMinute(fields["$i:startMinute"]!!)).put("endMinute",scheduleTimeMinute(fields["$i:endMinute"]!!))}};onDismiss()
    }.onFailure{invalid=true}}){Text("Apply")}},dismissButton={TextButton(onClick=onDismiss){Text(ui("Cancel"))}})
}
@Composable private fun ScheduleActivityDialog(original:JSONObject,days:Int,currentDay:Int,onDismiss:()->Unit,onDelete:()->Unit={},onSave:(JSONObject,Int)->Unit) {
    val fields=remember{mutableStateMapOf<String,String>().apply{for(key in listOf("name","duration","lat","lng","cost","queueMinutes","openMinute","closeMinute"))put(key,if(key=="queueMinutes"&&original.isNull(key))"" else if(key in listOf("openMinute","closeMinute"))scheduleTimeLabel(original.optInt(key,if(key=="closeMinute")1440 else 0)) else original.optString(key))}}
    var target by remember{mutableIntStateOf(currentDay)};var fixed by remember{mutableStateOf(!original.isNull("fixedStart"))};var locked by remember{mutableStateOf(original.optBoolean("locked"))};var start by remember{mutableStateOf(scheduleTimeLabel(if(original.isNull("fixedStart"))original.optInt("scheduledStart")else original.optInt("fixedStart")))}
    var invalid by remember{mutableStateOf(false)}
    AlertDialog(onDismissRequest=onDismiss,title={Text("Activity time")},text={Column(Modifier.heightIn(max=490.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(8.dp)){
        Text(original.optString("name"))
        TextButton(onClick=onDelete){Text("Remove this activity",color=Color.Red)}
        OutlinedTextField(fields["duration"] ?: "",{fields["duration"]=it},enabled=!locked,label={Text("Stay duration (min)")},singleLine=true)
        Row(verticalAlignment=Alignment.CenterVertically){Checkbox(fixed,{fixed=it});Text("Set start time")}
        if(fixed)OutlinedTextField(start,{start=it},label={Text("Start time HH:mm")},singleLine=true)
        Row(verticalAlignment=Alignment.CenterVertically){Checkbox(locked,{locked=it;if(it)fixed=true});Text("Lock time slots and stays")}
        Row{Text("Move to date:");(0 until days).forEach{i->TextButton(onClick={target=i}){Text((if(i==target)"✓ "else "")+"${i+1}")}}}
        if(original.optString("kind")=="attraction")OutlinedTextField(fields["queueMinutes"] ?: "",{fields["queueMinutes"]=it},label={Text("Assumed queue time (min; blank means unknown)")},singleLine=true)
        else {listOf("name" to "Name","lat" to "Latitude","lng" to "Longitude","cost" to "Cost A$","openMinute" to "Opens HH:mm","closeMinute" to "Closes HH:mm").forEach{(key,label)->OutlinedTextField(fields[key] ?: "",{fields[key]=it},label={Text(label)},singleLine=true)}}
        if(invalid)Text("Enter valid numbers.",color=Color.Red)
    }},confirmButton={TextButton(onClick={runCatching{
        val a=original.copy().put("duration",fields["duration"]!!.toInt()).put("locked",locked)
        if(fixed)a.put("fixedStart",scheduleTimeMinute(start))else a.remove("fixedStart")
        if(locked)a.put("fixedDuration",a.getInt("duration"))else a.remove("fixedDuration")
        if(original.optString("kind")=="attraction"){if(fields["queueMinutes"].isNullOrBlank())a.remove("queueMinutes")else a.put("queueMinutes",fields["queueMinutes"]!!.toInt())}
        else for(key in listOf("name","lat","lng","cost","openMinute","closeMinute")){val v=fields[key]!!;a.put(key,if(key=="name")v else if(key in listOf("openMinute","closeMinute"))scheduleTimeMinute(v)else v.toDouble())}
        onSave(a,target)
    }.onFailure{invalid=true}}){Text("Apply")}},dismissButton={TextButton(onClick=onDismiss){Text(ui("Cancel"))}})
}


internal data class ScheduleAllowance(val limit:Double,val used:Double) {
    val remaining:Double get()=limit-used
    val fraction:Float get()=if(limit>0)(remaining/limit).coerceIn(0.0,1.0).toFloat()else 0f
    val exceeded:Boolean get()=remaining<0
}
@Composable internal fun ScheduleAllowanceSummary(day:JSONObject,state:JSONObject,dayNumber:Int) {
    val limits=state.getJSONObject("settings")
    val steps=ScheduleAllowance(limits.optDouble("maxSteps"),day.optDouble("estimatedSteps"))
    val budget=ScheduleAllowance(limits.optDouble("dailyBudget"),day.optDouble("estimatedCost"))
    val time=ScheduleAllowance(limits.optDouble("timeLimit"),day.optDouble("timeUsed"))
    val total=ScheduleAllowance(limits.optDouble("totalBudget"),state.optDouble("totalCost"))
    val overdrawn=listOf(steps,budget,time,total).any{it.exceeded}
    val pulse=if(overdrawn&&ValueAnimator.areAnimatorsEnabled()){
        val transition=rememberInfiniteTransition(label="allowance warning")
        val alpha by transition.animateFloat(initialValue=.55f,targetValue=1f,animationSpec=infiniteRepeatable(tween(1000),RepeatMode.Reverse),label="warning pulse");alpha
    }else 1f
    fun integer(n:Double)=String.format(java.util.Locale.US,"%,d",n.toInt())
    fun money(n:Double)=String.format(java.util.Locale.US,"A$%.2f",n)
    Column(Modifier.fillMaxWidth().background(Mint,RoundedCornerShape(20.dp)).padding(16.dp),verticalArrangement=Arrangement.spacedBy(12.dp)) {
        Row(verticalAlignment=Alignment.CenterVertically){Column(Modifier.weight(1f)){Text("Trip Remaining Balance",fontWeight=FontWeight.Bold);Text("Day $dayNumber · ${day.optString("date")}",fontSize=10.sp,color=Color.Gray)};if(overdrawn)Text("⚠",color=Color.Red.copy(alpha=pulse),fontSize=20.sp)}
        AllowanceBar("Walking energy","steps",steps,integer(steps.remaining)+" steps",Green,pulse)
        AllowanceBar("Daily budget","budget",budget,money(budget.remaining),Color(0xff008b8b),pulse)
        AllowanceBar("Transportation＋Queue Time","time",time,integer(time.remaining)+" min",Color(0xff287fd6),pulse)
        Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(6.dp)) {
            AllowanceTile("Estimated steps",integer(day.optDouble("estimatedSteps")),"Step limit ${integer(steps.limit)}",Modifier.weight(1f))
            AllowanceTile("Day's cost",money(day.optDouble("estimatedCost")),"Whole trip: ${money(state.optDouble("totalCost"))}",Modifier.weight(1f))
            AllowanceTile("Transportation＋Queue","${day.optInt("timeUsed")} min","Transportation ${day.optInt("transportMinutes")}＋Queue ${day.optInt("queueMinutes")}",Modifier.weight(1f))
        }
        Text((if(total.exceeded)"⚠ "else "")+"Trip budget remaining: ${money(total.remaining)} / ${money(total.limit)}",fontSize=10.sp,color=if(total.exceeded)Color.Red else Color.Gray)
        if(day.optBoolean("unknownQueue"))Small("Includes unknown wait times, remaining for planning reference only")
        Small("Depart %02d:%02d · finish ${day.optString("finishTime")}".format(day.optInt("startMinute")/60,day.optInt("startMinute")%60))
    }
}
@Composable private fun AllowanceBar(title:String,id:String,value:ScheduleAllowance,remaining:String,color:Color,pulse:Float) {
    val tint=if(value.exceeded)Color.Red else if(value.fraction<=.2f)Color(0xffbf750b)else color
    Column(Modifier.testTag("allowance-$id").semantics(mergeDescendants=true){contentDescription="$title, $remaining remaining"+(if(value.exceeded)", over budget"else "")},verticalArrangement=Arrangement.spacedBy(6.dp)) {
        Row(verticalAlignment=Alignment.CenterVertically){Text(title,fontSize=12.sp,modifier=Modifier.weight(1f));Text("$remaining remaining",fontSize=13.sp,fontWeight=FontWeight.Bold,color=tint);if(value.exceeded)Text(" ⚠",color=Color.Red.copy(alpha=pulse),modifier=Modifier.testTag("allowance-warning-$id"))}
        Row(Modifier.fillMaxWidth().border(1.dp,tint.copy(alpha=if(value.exceeded)pulse else .25f),RoundedCornerShape(5.dp)).padding(3.dp),horizontalArrangement=Arrangement.spacedBy(3.dp)) {
            repeat(16){index->Box(Modifier.weight(1f).height(12.dp).background(tint.copy(alpha=.13f),RoundedCornerShape(2.dp))){Box(Modifier.fillMaxHeight().fillMaxWidth((value.fraction*16-index).coerceIn(0f,1f)).background(tint,RoundedCornerShape(2.dp)))}}
        }
    }
}
@Composable private fun AllowanceTile(title:String,value:String,detail:String,modifier:Modifier) {
    Column(modifier.background(Color.White.copy(alpha=.85f),RoundedCornerShape(12.dp)).padding(8.dp).heightIn(min=68.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
        Text(title,fontSize=10.sp,color=Color.Gray,maxLines=1)
        Text(value,fontSize=14.sp,fontWeight=FontWeight.Bold,maxLines=1)
        Text(detail,fontSize=9.sp,color=Color.Gray,maxLines=2)
    }
}
