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
                Text(ui("景點工具箱"),fontSize=13.sp,fontWeight=FontWeight.Bold)
                TextButton(onClick={discover=true},contentPadding=PaddingValues(0.dp)){Text("地圖找地點",fontSize=11.sp)}
                Text("依推薦排序\n按住 ☰ 拖到日期切換\n再拖入右側時段",fontSize=10.sp,color=Color.Gray)
                Column(Modifier.verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                    state.array("candidates").objects().filter{p->p.optString("source") !in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation") || c.store.poiToolbox.any{it.optString("id")==p.optString("id")}}.forEachIndexed{i,p->key(p.getString("id")) {
                        val id=p.getString("id");val locked=state.array("days").objects().flatMap{it.array("activities").objects()}.any{it.optString("placeId")==id&&it.optBoolean("locked")};var grip by remember{mutableStateOf(Offset.Zero)}
                        Column(Modifier.fillMaxWidth().background(Color.White,RoundedCornerShape(13.dp)).border(1.dp,Green.copy(alpha=.12f),RoundedCornerShape(13.dp)).padding(8.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
                            Text("%02d".format(i+1),fontSize=10.sp,color=Green,fontWeight=FontWeight.Bold)
                            Text(p.optString("name"),fontSize=12.sp,fontWeight=FontWeight.SemiBold)
                            if(p.optString("ratingLabel").isNotBlank())Text(p.optString("ratingLabel"),fontSize=10.sp,color=Green,fontWeight=FontWeight.Medium)
                            Text(if(p.optString("source") in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation"))"${p.optInt("visitMinutes")} 分 · "+(p.optString("priceLabel").takeIf{it.isNotBlank()} ?: "費用未知")else "${p.optInt("visitMinutes")} 分 · A$${p.optInt("cost")}",fontSize=10.sp,color=Color.Gray)
                            selected[id]?.let{Text("第 ${it+1} 天已排",fontSize=10.sp,color=Green)}
                            if(locked)Text("已鎖定",fontSize=10.sp,color=Color.Gray)
                            Row(verticalAlignment=Alignment.CenterVertically){
                                TextButton(onClick={detail=p},modifier=Modifier.width(42.dp).testTag("toolbox-info-$id"),contentPadding=PaddingValues(0.dp)){Text("介紹",fontSize=11.sp)}
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
                Row(Modifier.fillMaxWidth()){state.array("days").objects().forEachIndexed{i,d->TextButton(onClick={c.dayIndex=i},modifier=Modifier.weight(1f).testTag("schedule-day-$i").onGloballyPositioned{val pos=it.positionInRoot();dateFrames[i]=Rect(pos,pos+Offset(it.size.width.toFloat(),it.size.height.toFloat()))}.border(if(hoverDay==i)2.dp else 0.dp,if(hoverDay==i)Green else Color.Transparent,RoundedCornerShape(10.dp)).background(if(i==c.dayIndex)Mint else Color.Transparent,RoundedCornerShape(10.dp)),contentPadding=PaddingValues(1.dp)){Text("第 ${i+1} 天\n${d.optString("date")}",fontSize=9.sp,color=if(i==c.dayIndex)Green else Color.Gray)}}}
                Column(Modifier.weight(1f).testTag("timeline-viewport").onGloballyPositioned{val pos=it.positionInRoot();viewport=Rect(pos,pos+Offset(it.size.width.toFloat(),it.size.height.toFloat()))}.verticalScroll(timelineScroll)) {
                key(c.dayIndex){ScheduleTimeCanvas(day,onChange={id,start,duration->c.retime(id,start,duration)},onEdit=onEdit,onAdd=onAdd,onCanvasPosition={point,start,zoom->canvasOrigin=point;first=start;scale=zoom})}
                }
            }
        }
        dragging?.let{p->Text(p.optString("name")+"\n"+(minute?.let{"放到 %02d:%02d".format(it/60,it%60)} ?: "拖至右側時間軸"),Modifier.offset(x=((pointer.x-origin.x)/density-60).dp,y=((pointer.y-origin.y)/density-50).dp).width(140.dp).zIndex(20f).background(Mint,RoundedCornerShape(12.dp)).border(1.dp,Green,RoundedCornerShape(12.dp)).padding(10.dp),fontSize=11.sp)}
    }
    if(discover)PoiDiscoveryDialog(c.store,onDismiss={discover=false},onAdd={c.store.addPoi(it);c.importPlaces(listOf(it))})
    detail?.let{p->SchedulePlaceDetail(p,c.store){detail=null}}
}
@Composable internal fun SchedulePlaceDetail(p:JSONObject,store:BuddyStore,onDismiss:()->Unit) {
    fun time(n:Int)="%02d:%02d".format(n/60,n%60)
    val language=LocalAppLanguage.current
    val context=androidx.compose.ui.platform.LocalContext.current
    val catalog=remember(context.applicationContext){JSONObject(context.assets.open("ui-localizations.json").bufferedReader().use{it.readText()})}
    AlertDialog(onDismissRequest=onDismiss,title={Text(ui("景點介紹"))},text={Column(Modifier.heightIn(max=480.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(14.dp)) {
        Text(poiText(p,"name",language,catalog),fontSize=21.sp,fontWeight=FontWeight.Bold)
        Text(ui(p.optString("category"))+" · "+ui(if(p.optBoolean("indoor"))"室內"else "戶外"),color=Color.Gray)
        if(p.optString("ratingLabel").isNotBlank()||p.optString("reviewSummary").isNotBlank()){
            Column(Modifier.fillMaxWidth().background(Mint,RoundedCornerShape(12.dp)).padding(12.dp),verticalArrangement=Arrangement.spacedBy(4.dp)) {
                Text("旅客綜合評價 · "+p.optString("ratingLabel","★ 4.4"),fontWeight=FontWeight.Bold,color=Green)
                if(p.optString("reviewSummary").isNotBlank())Text(p.optString("reviewSummary"),fontSize=13.sp)
                p.optJSONArray("reviewHighlights")?.strings()?.forEach{h->Small("• $h")}
            }
        }
        Text(poiText(p,"description",language,catalog).takeIf{it.isNotBlank()} ?: ui("此草案尚未附上景點介紹，可重新試算以更新資料。"))
        ActivityReferencesPanel(p,store)
        p.optJSONObject("restaurantData")?.let{RestaurantPricePanel(p,store)}
        p.array("socialRecommendations").objects().forEach{recommendation->Text(recommendation.optString("author").ifBlank{ui("來源未提供作者")});Text(recommendation.optJSONObject("claims")?.optString("quote") ?: "");Small(recommendation.optString("postedAt").ifBlank{ui("發文日期未提供")})}
        p.optJSONObject("guide")?.let{guide->
            val uri=androidx.compose.ui.platform.LocalUriHandler.current
            Text(ui("如何到達"),fontWeight=FontWeight.Bold)
            Text(guide.optString("directions").ifBlank{ui("來源未提供到達方式，請先查詢導航。")})
            Small("社群到達說明並非即時交通班次，出發前請確認。")
            Small("座標：${p.optDouble("lat")}, ${p.optDouble("lng")}")
            if(guide.optString("sourceUrl").startsWith("https://"))TextButton(onClick={uri.openUri(guide.getString("sourceUrl"))}){Text(ui("社群指南與版本來源"))}
            Small(guide.optString("attribution")+" · "+guide.optString("license"))
            if(guide.optString("licenseUrl").startsWith("https://"))TextButton(onClick={uri.openUri(guide.getString("licenseUrl"))}){Text(ui("文字授權"))}
            Text(ui("社群介紹與評論"),fontWeight=FontWeight.Bold)
            val reviews=p.array("reviews").objects();if(reviews.isEmpty())Small("此來源未提供旅客評論或評分。")else reviews.forEach{Text(it.optString("author")+" · "+it.optString("text"))}
        }
        if(p.optString("sourceUrl").startsWith("https://")){val uri=androidx.compose.ui.platform.LocalUriHandler.current;TextButton(onClick={uri.openUri(p.getString("sourceUrl"))}){Text(ui("地點資料來源"))}}
        PlaceMedia(p,store)
        PlaceContact(p)
        Text(ui("為你推薦"),fontWeight=FontWeight.Bold);Text(ui(p.optString("reason")))
        Text("建議停留 ${p.optInt("visitMinutes")} 分鐘\n"+if(p.optJSONObject("restaurantData")!=null)ui("餐費見上方估算；營業與排隊仍待確認。")else if(p.optString("priceLabel").isNotBlank())"價格等級映射：${p.optString("priceLabel")} · 排隊時間未知"else if(p.optString("source") in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation"))"費用與排隊時間未知"else "規劃預留 A$${p.optInt("cost")} · 排隊時間未知")
        if(p.optString("openingHoursText").isNotBlank())Small("OSM 營業時間標記："+p.optString("openingHoursText"))
        if(!p.isNull("openMinute")&&!p.isNull("closeMinute"))Text("示範開放時段 ${time(p.optInt("openMinute"))}–${time(p.optInt("closeMinute"))}")
        if(p.optString("note").isNotBlank())Small(ui(p.optString("note")))
        Small(if(p.optString("source") in listOf("OpenStreetMap","Apple Maps","Overture Maps","Wikivoyage","Restaurant evidence","Community social recommendation"))"資料來源："+p.optString("source")+"；費用及營業資訊未確認。"else "本機景點資料；費用與開放時間是規劃示例，出發前請確認當日資訊。")
    }},confirmButton={TextButton(onClick=onDismiss){Text(ui("完成"))}})
}
