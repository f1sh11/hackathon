package com.sydneybuddy.travel

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import kotlinx.coroutines.delay
import org.json.JSONObject
import kotlin.math.*

internal fun toolboxMinute(y:Float,scale:Float,first:Int,start:Int,end:Int):Int? {
    if(y<0||scale<=0)return null
    val value=first+(y/scale/5).roundToInt()*5
    return value.takeIf{it>=start&&it<end}
}
@Composable internal fun ScheduleWorkbench(c:ScheduleController,onEdit:(JSONObject)->Unit,onAdd:()->Unit) {
    val state=c.state ?: return
    val day=state.array("days").getJSONObject(c.dayIndex)
    var detail by remember{mutableStateOf<JSONObject?>(null)}
    var discover by remember{mutableStateOf(false)}
    LaunchedEffect(c.store.poiToolbox,c.busy){if(!c.busy)c.importPlaces(c.store.poiToolbox)}
    var dragging by remember{mutableStateOf<JSONObject?>(null)}
    var pointer by remember{mutableStateOf(Offset.Zero)}
    var origin by remember{mutableStateOf(Offset.Zero)}
    var viewport by remember{mutableStateOf(Rect.Zero)}
    var canvasOrigin by remember{mutableStateOf(Offset.Zero)}
    var first by remember{mutableIntStateOf(0)}
    var scale by remember{mutableFloatStateOf(3f)}
    val dateFrames=remember{mutableStateMapOf<Int,Rect>()}
    val timelineScroll=rememberScrollState()
    val hoverDay=if(dragging!=null)dateFrames.entries.firstOrNull{it.value.contains(pointer)}?.key?.takeIf{it!=c.dayIndex}else null
    LaunchedEffect(hoverDay,dragging?.optString("id")) {
        if(hoverDay!=null){delay(450);c.dayIndex=hoverDay}
    }
    LaunchedEffect(c.dayIndex){timelineScroll.scrollTo(0)}
    val density=LocalDensity.current.density
    val selected=state.array("days").objects().flatMapIndexed{i,d->d.array("activities").objects().filter{it.optString("placeId").isNotBlank()}.map{it.optString("placeId") to i}}.toMap()
    val minute=if(viewport.contains(pointer))toolboxMinute((pointer.y-canvasOrigin.y)/density,scale,first,day.optInt("startMinute"),day.optInt("endMinute"))else null
    // Keep the pointer handler alive across day switches; resolve the drop with current layout.
    val dropCurrent by rememberUpdatedState<(JSONObject)->Unit>({p->
        val time=if(viewport.contains(pointer))toolboxMinute((pointer.y-canvasOrigin.y)/density,scale,first,day.optInt("startMinute"),day.optInt("endMinute"))else null
        if(time!=null)c.place(p,time,moveAcrossDays=true)
    })
    Box(Modifier.fillMaxWidth().height(600.dp).testTag("schedule-workbench").onGloballyPositioned{origin=it.positionInRoot()}) {
        Row(Modifier.fillMaxSize(),horizontalArrangement=Arrangement.spacedBy(8.dp)) {
            Column(Modifier.fillMaxWidth(.34f),verticalArrangement=Arrangement.spacedBy(6.dp)) {
                Text(ui("Place toolbox"),fontSize=13.sp,fontWeight=FontWeight.Bold)
                TextButton(onClick={discover=true},contentPadding=PaddingValues(0.dp)){Text("Find places on map",fontSize=11.sp)}
                Text("Sort by recommendation\nHold ◀ to switch dates\nDrag to right side when done",fontSize=10.sp,color=Color.Gray)
                Column(Modifier.verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                    state.array("candidates").objects().filter{p->p.optString("source") !in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation") || c.store.poiToolbox.any{it.optString("id")==p.optString("id")}}.forEachIndexed{i,p->key(p.getString("id")) {
                        val id=p.getString("id");val locked=state.array("days").objects().flatMap{it.array("activities").objects()}.any{it.optString("placeId")==id&&it.optBoolean("locked")};var grip by remember{mutableStateOf(Offset.Zero)}
                        Column(Modifier.fillMaxWidth().background(Color.White,RoundedCornerShape(13.dp)).border(1.dp,Green.copy(alpha=.12f),RoundedCornerShape(13.dp)).padding(8.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
                            Text("%02d".format(i+1),fontSize=10.sp,color=Green,fontWeight=FontWeight.Bold)
                            Text(p.optString("name"),fontSize=12.sp,fontWeight=FontWeight.SemiBold)
                            if(p.optString("ratingLabel").isNotBlank())Text(p.optString("ratingLabel"),fontSize=10.sp,color=Green,fontWeight=FontWeight.Medium)
                            Text(if(p.optString("source") in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation"))"${p.optInt("visitMinutes")} min · "+(p.optString("priceLabel").takeIf{it.isNotBlank()} ?: "Cost unknown")else "${p.optInt("visitMinutes")} min · A$ ${p.optInt("cost")}",fontSize=10.sp,color=Color.Gray)
                            selected[id]?.let{Text("Day ${it+1} scheduled",fontSize=10.sp,color=Green)}
                            if(locked)Text("Locked",fontSize=10.sp,color=Color.Gray)
                            Row(verticalAlignment=Alignment.CenterVertically){
                                TextButton(onClick={detail=p},modifier=Modifier.width(42.dp).testTag("toolbox-info-$id"),contentPadding=PaddingValues(0.dp)){Text("About",fontSize=11.sp)}
                                if(!c.busy&&!locked)Text("☰",Modifier.size(32.dp,36.dp).onGloballyPositioned{grip=it.positionInRoot()}.testTag("toolbox-drag-$id").pointerInput(id){detectDragGesturesAfterLongPress(onDragStart={dragging=p;pointer=grip+it},onDrag={change,amount->change.consume();pointer+=amount},onDragCancel={dragging=null},onDragEnd={
                                    dropCurrent(p);dragging=null
                                })},fontSize=18.sp,color=Green)
                            }
                        }
                    }}
                    Spacer(Modifier.height(20.dp))
                }
            }
            Box(Modifier.width(1.dp).fillMaxHeight().background(Green.copy(alpha=.12f)))
            Column(Modifier.weight(1f).fillMaxHeight().border(if(dragging!=null)1.dp else 0.dp,if(dragging!=null)Green else Color.Transparent,RoundedCornerShape(10.dp))) {
                Row(Modifier.fillMaxWidth()){state.array("days").objects().forEachIndexed{i,d->TextButton(onClick={c.dayIndex=i},modifier=Modifier.weight(1f).testTag("schedule-day-$i").onGloballyPositioned{val pos=it.positionInRoot();dateFrames[i]=Rect(pos,pos+Offset(it.size.width.toFloat(),it.size.height.toFloat()))}.border(if(hoverDay==i)2.dp else 0.dp,if(hoverDay==i)Green else Color.Transparent,RoundedCornerShape(10.dp)).background(if(i==c.dayIndex)Mint else Color.Transparent,RoundedCornerShape(10.dp)),contentPadding=PaddingValues(1.dp)){Text("Day ${i+1}\n${d.optString("date")}",fontSize=9.sp,color=if(i==c.dayIndex)Green else Color.Gray)}}}
                Column(Modifier.weight(1f).testTag("timeline-viewport").onGloballyPositioned{val pos=it.positionInRoot();viewport=Rect(pos,pos+Offset(it.size.width.toFloat(),it.size.height.toFloat()))}.verticalScroll(timelineScroll)) {
                key(c.dayIndex){ScheduleTimeCanvas(day,onChange={id,start,duration->c.retime(id,start,duration)},onEdit=onEdit,onAdd=onAdd,onCanvasPosition={point,start,zoom->canvasOrigin=point;first=start;scale=zoom})}
                }
            }
        }
        dragging?.let{p->Text(p.optString("name")+"\n"+(minute?.let{"Move to %02d:%02d".format(it/60,it%60)} ?: "Drag to right timeline"),Modifier.offset(x=((pointer.x-origin.x)/density-60).dp,y=((pointer.y-origin.y)/density-50).dp).width(140.dp).zIndex(20f).background(Mint,RoundedCornerShape(12.dp)).border(1.dp,Green,RoundedCornerShape(12.dp)).padding(10.dp),fontSize=11.sp)}
    }
    if(discover)PoiDiscoveryDialog(c.store,onDismiss={discover=false},onAdd={c.store.addPoi(it);c.importPlaces(listOf(it))})
    detail?.let{p->SchedulePlaceDetail(p,c.store){detail=null}}
}
@Composable internal fun SchedulePlaceDetail(p:JSONObject,store:BuddyStore,onDismiss:()->Unit) {
    fun time(n:Int)="%02d:%02d".format(n/60,n%60)
    AlertDialog(onDismissRequest=onDismiss,title={Text(ui("About this place"))},text={Column(Modifier.heightIn(max=480.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(14.dp)) {
        Text(p.optString("name"),fontSize=21.sp,fontWeight=FontWeight.Bold)
        Text(ui(p.optString("category"))+" · "+if(p.optBoolean("indoor"))"Indoor"else "Outdoor",color=Color.Gray)
        if(p.optString("ratingLabel").isNotBlank()||p.optString("reviewSummary").isNotBlank()){
            Column(Modifier.fillMaxWidth().background(Mint,RoundedCornerShape(12.dp)).padding(12.dp),verticalArrangement=Arrangement.spacedBy(4.dp)) {
                Text("Traveler rating · "+p.optString("ratingLabel","★ 4.4"),fontWeight=FontWeight.Bold,color=Green)
                if(p.optString("reviewSummary").isNotBlank())Text(p.optString("reviewSummary"),fontSize=13.sp)
                p.optJSONArray("reviewHighlights")?.strings()?.forEach{h->Small("• $h")}
            }
        }
        Text(p.optString("description").takeIf{it.isNotBlank()} ?: "This draft lacks attraction introductions; re-calculate to update data.")
        ActivityReferencesPanel(p,store)
        p.optJSONObject("restaurantData")?.let{RestaurantPricePanel(p,store)}
        p.array("socialRecommendations").objects().forEach{recommendation->Text(recommendation.optString("author").ifBlank{ui("Author not provided")});Text(recommendation.optJSONObject("claims")?.optString("quote") ?: "");Small(recommendation.optString("postedAt").ifBlank{ui("Post date not provided")})}
        p.optJSONObject("guide")?.let{guide->
            val uri=androidx.compose.ui.platform.LocalUriHandler.current
            Text(ui("Getting there"),fontWeight=FontWeight.Bold)
            Text(guide.optString("directions").ifBlank{ui("No access directions provided. Check navigation first.")})
            Small("Community directions are not live transit information. Verify before departure.")
            Small("Coordinates: ${p.optDouble("lat")}, ${p.optDouble("lng")}")
            if(guide.optString("sourceUrl").startsWith("https://"))TextButton(onClick={uri.openUri(guide.getString("sourceUrl"))}){Text(ui("Community guide and source revision"))}
            Small(guide.optString("attribution")+" · "+guide.optString("license"))
            if(guide.optString("licenseUrl").startsWith("https://"))TextButton(onClick={uri.openUri(guide.getString("licenseUrl"))}){Text(ui("Text license"))}
            Text(ui("Community guide and reviews"),fontWeight=FontWeight.Bold)
            val reviews=p.array("reviews").objects();if(reviews.isEmpty())Small("This source provides no traveller reviews or ratings.")else reviews.forEach{Text(it.optString("author")+" · "+it.optString("text"))}
        }
        if(p.optString("sourceUrl").startsWith("https://")){val uri=androidx.compose.ui.platform.LocalUriHandler.current;TextButton(onClick={uri.openUri(p.getString("sourceUrl"))}){Text(ui("Place data source"))}}
        PlaceMedia(p,store)
        PlaceContact(p)
        Text("Recommended for you",fontWeight=FontWeight.Bold);Text(p.optString("reason"))
        Text("Suggested stay: ${p.optInt("visitMinutes")} min\n"+if(p.optJSONObject("restaurantData")!=null)ui("See the meal estimate above. Opening hours and queues remain unconfirmed.")else if(p.optString("priceLabel").isNotBlank())"Price tier: ${p.optString("priceLabel")} · queue time unknown"else if(p.optString("source") in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation"))"Cost and queue time unknown"else "Planning reserve A$${p.optInt("cost")} · Queue time unknown")
        if(p.optString("openingHoursText").isNotBlank())Small("OSM opening hours: "+p.optString("openingHoursText"))
        if(!p.isNull("openMinute")&&!p.isNull("closeMinute"))Text("Demo open hours ${time(p.optInt("openMinute"))}–${time(p.optInt("closeMinute"))}")
        if(p.optString("note").isNotBlank())Small(p.optString("note"))
        Small(if(p.optString("source") in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation"))"Data source:"+p.optString("source")+"；Cost & business info unconfirmed."else "Local attraction data; fees and opening hours are planning examples. Please confirm daily info before departure.")
    }},confirmButton={TextButton(onClick=onDismiss){Text(ui("Done"))}})
}
