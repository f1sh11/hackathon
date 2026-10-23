package com.sydneybuddy.travel

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.zIndex
import org.json.JSONObject
import kotlin.math.*

internal data class ScheduleTimeEdit(val start:Int,val duration:Int) {
    companion object {
        fun drag(start:Int,duration:Int,delta:Float,edge:Int):ScheduleTimeEdit {
            val shift=(abs(delta)/5).roundToInt()*5*(if(delta<0)-1 else 1)
            if(edge==0)return ScheduleTimeEdit((start+shift).coerceIn(0,1439),duration)
            if(edge<0){val change=shift.coerceIn(max(-start,duration-240),duration-5);return ScheduleTimeEdit(start+change,duration-change)}
            return ScheduleTimeEdit(start,(duration+shift).coerceIn(5,240))
        }
    }
}
private fun timelineClock(minute:Int)="%02d:%02d".format(minute/60,minute%60)

@Composable internal fun ScheduleTimeCanvas(day:JSONObject,onChange:(String,Int,Int)->Unit,onEdit:(JSONObject)->Unit,onAdd:()->Unit) {
    var scale by remember{mutableFloatStateOf(3f)}
    var dragging by remember{mutableStateOf<String?>(null)}
    var edge by remember{mutableIntStateOf(0)}
    var delta by remember{mutableFloatStateOf(0f)}
    val density=LocalDensity.current.density
    val items=day.array("activities").objects()
    val first=min(day.optInt("startMinute"),items.minOfOrNull{it.optInt("scheduledStart")} ?: day.optInt("startMinute"))/30*30
    val last=(max(day.optInt("endMinute"),items.maxOfOrNull{it.optInt("scheduledStart")+it.optInt("queueMinutes")+it.optInt("duration")} ?: day.optInt("endMinute"))+29)/30*30
    fun timeEdit(a:JSONObject)=ScheduleTimeEdit.drag(a.optInt("scheduledStart"),a.optInt("duration"),if(dragging==a.optString("id"))delta/density/scale else 0f,edge)
    fun gesture(a:JSONObject,part:Int):Modifier=Modifier.pointerInput(a.toString(),part,scale,density){
        if(!a.optBoolean("locked"))detectVerticalDragGestures(onDragStart={dragging=a.getString("id");edge=part;delta=0f},onVerticalDrag={change,amount->change.consume();delta+=amount},onDragCancel={dragging=null;delta=0f},onDragEnd={
            val result=ScheduleTimeEdit.drag(a.optInt("scheduledStart"),a.optInt("duration"),delta/density/scale,part)
            dragging=null;delta=0f
            if(result.start!=a.optInt("scheduledStart")||result.duration!=a.optInt("duration"))onChange(a.getString("id"),result.start,result.duration)
        })
    }
    Column(verticalArrangement=Arrangement.spacedBy(12.dp)) {
        Small("拖動 ☰ 移動時段 · 上下把手調整停留時間")
        Row(verticalAlignment=Alignment.CenterVertically){Small("卡片長度＝停留時間 · 每格 5 分");Spacer(Modifier.weight(1f));TextButton(onClick={scale=max(2f,scale-1)}){Text("縮小")};TextButton(onClick={scale=min(6f,scale+1)}){Text("放大")}}
        Box(Modifier.fillMaxWidth().height(((last-first)*scale+20).dp)) {
            for(minute in first..last step 30)Row(Modifier.offset(y=((minute-first)*scale-6).dp).fillMaxWidth(),verticalAlignment=Alignment.CenterVertically){Text(timelineClock(minute),Modifier.width(51.dp),fontSize=10.sp,color=Color.Gray);HorizontalDivider(color=Green.copy(alpha=.16f))}
            items.forEachIndexed{index,a->key(a.getString("id")) {
                val id=a.getString("id");val value=timeEdit(a);val height=value.duration*scale
                val previous=if(index==0)day.optInt("startMinute")else items[index-1].let{it.optInt("scheduledStart")+it.optInt("queueMinutes")+it.optInt("duration")}
                val travel=a.optInt("travelMinutes");val travelEnd=previous+travel
                if(travel>0)TimelineBand("步行交通約 $travel 分",((previous-first)*scale),travel*scale,Color.Blue)
                if(a.optInt("scheduledStart")>travelEnd){val gap=a.optInt("scheduledStart")-travelEnd
                    Box(Modifier.offset(x=58.dp,y=((travelEnd-first)*scale).dp).fillMaxWidth().padding(end=58.dp).height((gap*scale).dp).background(Color.White.copy(alpha=.6f),RoundedCornerShape(10.dp)).drawBehind{drawRoundRect(Color.LightGray,cornerRadius=CornerRadius(10.dp.toPx()),style=Stroke(width=1.dp.toPx(),pathEffect=PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(),4.dp.toPx()))))}.clickable(onClick=onAdd),contentAlignment=Alignment.Center){Column(horizontalAlignment=Alignment.CenterHorizontally){Text("＋ 空檔 $gap 分",fontSize=11.sp,color=Green);if(gap>=20)Text("可保留休息／彈性安排",fontSize=10.sp,color=Color.Gray)}}
                }
                if(a.optInt("queueMinutes")>0)TimelineBand("排隊假設 ${a.optInt("queueMinutes")} 分",(a.optInt("scheduledStart")-first)*scale,a.optInt("queueMinutes")*scale,Color(0xffb36b00))
                val color=if(a.optString("kind")=="meal")Color(0xff287bd2)else Green
                Box(Modifier.offset(x=58.dp,y=((value.start+a.optInt("queueMinutes")-first)*scale).dp).fillMaxWidth().padding(end=58.dp).height(height.dp).zIndex(if(dragging==id)10f else 1f)) {
                    Row(Modifier.fillMaxSize().background(color.copy(alpha=.17f),RoundedCornerShape(12.dp)).border(if(dragging==id)2.dp else 1.dp,color,RoundedCornerShape(12.dp)).padding(horizontal=10.dp),verticalAlignment=Alignment.CenterVertically){
                        Column(Modifier.weight(1f).testTag("schedule-edit-$id").clickable{onEdit(a)},verticalArrangement=Arrangement.spacedBy(5.dp)){
                            Text(a.optString("name"),fontWeight=FontWeight.Bold,fontSize=if(height<70)11.sp else 15.sp,maxLines=if(height<70)1 else 2)
                            if(height>=70)Text("${timelineClock(value.start+a.optInt("queueMinutes"))}–${timelineClock(value.start+a.optInt("queueMinutes")+value.duration)} · ${value.duration} 分",fontSize=11.sp)
                            if(height>=120&&a.optString("kind")=="attraction"&&a.isNull("queueMinutes"))Text("排隊時間未知，尚未計入",fontSize=10.sp)
                        }
                        Text(if(a.optBoolean("locked"))"🔒"else "☰ 拖動排序",Modifier.width(40.dp).fillMaxHeight().then(gesture(a,0)).testTag("schedule-move-$id"),fontSize=10.sp)
                    }
                    if(!a.optBoolean("locked")){
                        Text("⌃",Modifier.align(Alignment.TopStart).offset(x=18.dp,y=(-12).dp).size(38.dp,24.dp).background(Color.White,RoundedCornerShape(12.dp)).border(1.dp,Green.copy(alpha=.4f),RoundedCornerShape(12.dp)).then(gesture(a,-1)).testTag("schedule-start-$id"),color=Green,textAlign=androidx.compose.ui.text.style.TextAlign.Center)
                        Text("⌄",Modifier.align(Alignment.BottomEnd).offset(x=(-48).dp,y=12.dp).size(38.dp,24.dp).background(Color.White,RoundedCornerShape(12.dp)).border(1.dp,Green.copy(alpha=.4f),RoundedCornerShape(12.dp)).then(gesture(a,1)).testTag("schedule-end-$id"),color=Green,textAlign=androidx.compose.ui.text.style.TextAlign.Center)
                    }
                }
            }}
        }
        Small("交通與排隊獨立占用時間；空檔可保留。點卡片可編輯短活動、鎖定或跨日移動。")
    }
}
@Composable private fun TimelineBand(text:String,y:Float,height:Float,color:Color) {
    Row(Modifier.offset(x=60.dp,y=y.dp).fillMaxWidth().padding(end=60.dp).height(height.dp),verticalAlignment=Alignment.CenterVertically){Box(Modifier.width(3.dp).fillMaxHeight().background(color.copy(alpha=.5f)));if(height>=18)Text(text,fontSize=10.sp,color=color,maxLines=1)}
}
