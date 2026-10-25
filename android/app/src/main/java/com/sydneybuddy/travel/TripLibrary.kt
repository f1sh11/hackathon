package com.sydneybuddy.travel

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.json.JSONObject
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

internal fun tripLibraryDates(trip:JSONObject):List<String> = trip.array("days").objects().map{it.optString("date")}.ifEmpty{listOf(trip.optJSONObject("profile")?.optString("date") ?: "")}
internal fun tripLibraryMatches(trip:JSONObject,query:String,date:String?):Boolean {
    if(date!=null && date !in tripLibraryDates(trip))return false
    val text=(listOf(trip.optString("title"),trip.optString("createdAt"),trip.optJSONObject("profile")?.optString("date") ?: "")+tripLibraryDates(trip)+trip.array("days").objects().flatMap{it.array("stops").objects().map{stop->stop.optString("name")}}).joinToString(" ")
    return query.isBlank()||text.contains(query.trim(),ignoreCase=true)
}
@Composable internal fun TripDateBadge(trip:JSONObject){
    val date=trip.optJSONObject("profile")?.optString("date") ?: ""
    Column(Modifier.width(76.dp).background(Mint,RoundedCornerShape(16.dp)).padding(vertical=14.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(4.dp)){
        Text(date.drop(5).replace('-','/'),fontWeight=FontWeight.Bold,color=Green,fontSize=16.sp)
        Text(ui("{0} 天",trip.array("days").length().toString()),color=Green,fontSize=12.sp)
        Text(date.take(4),color=Green,fontSize=11.sp)
    }
}
@Composable internal fun TripLibraryCalendar(selection:String?,marked:Set<String>,onSelect:(String)->Unit){
    var monthValue by rememberSaveable{mutableStateOf(YearMonth.now().toString())}
    val month=YearMonth.parse(monthValue)
    val language=LocalAppLanguage.current ?: AppLanguage.TRADITIONAL
    val locale=Locale.forLanguageTag(language.code)
    val first=month.atDay(1)
    val offset=first.dayOfWeek.value-1
    Surface(shape=RoundedCornerShape(20.dp),color=Color.White){Column(Modifier.padding(10.dp)){
        Row(verticalAlignment=Alignment.CenterVertically){
            IconButton(onClick={monthValue=month.minusMonths(1).toString()},modifier=Modifier.testTag("trip-month-previous")){Icon(Icons.Outlined.ChevronLeft,ui("上個月"))}
            Text(month.format(DateTimeFormatter.ofPattern("yyyy MMMM",locale)),Modifier.weight(1f),fontWeight=FontWeight.Bold)
            IconButton(onClick={monthValue=month.plusMonths(1).toString()}){Icon(Icons.Outlined.ChevronRight,ui("下個月"))}
        }
        Row{(0..6).forEach{i->Box(Modifier.weight(1f),contentAlignment=Alignment.Center){Text(java.time.DayOfWeek.of(i+1).getDisplayName(java.time.format.TextStyle.SHORT,locale),fontSize=10.sp,color=Muted)}}}
        repeat((offset+month.lengthOfMonth()+6)/7){row->Row{repeat(7){col->
            val day=row*7+col-offset+1
            Box(Modifier.weight(1f),contentAlignment=Alignment.Center){
                if(day in 1..month.lengthOfMonth()){
                    val date=month.atDay(day).toString();val active=selection==date
                    TextButton(onClick={onSelect(date)},modifier=Modifier.fillMaxWidth().height(44.dp).testTag("trip-date-$date").semantics{selected=active},shape=RoundedCornerShape(12.dp),colors=ButtonDefaults.textButtonColors(containerColor=if(active)Green else Color.Transparent,contentColor=if(active)Color.White else Ink),contentPadding=PaddingValues(0.dp)){
                        Column(horizontalAlignment=Alignment.CenterHorizontally){Text(day.toString(),fontSize=14.sp);Text(if(date in marked)"•" else " ",fontSize=10.sp,lineHeight=10.sp)}
                    }
                }
            }
        }}}
    }}
}
