package com.sydneybuddy.travel

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject

@Composable internal fun RestaurantImportDialog(store:BuddyStore,onDismiss:()->Unit){
    var json by remember{mutableStateOf("")};var query by remember{mutableStateOf("")};var lat by remember{mutableStateOf("-33.865")};var lng by remember{mutableStateOf("151.212")}
    var places by remember{mutableStateOf<List<JSONObject>>(emptyList())};var busy by remember{mutableStateOf(false)};var message by remember{mutableStateOf<String?>(null)}
    val scope=rememberCoroutineScope()
    fun run(action:suspend()->Unit){scope.launch{busy=true;message=null;try{action()}catch(e:Exception){message=e.message}finally{busy=false}}}
    Dialog(onDismissRequest={if(!busy)onDismiss()},properties=DialogProperties(usePlatformDefaultWidth=false,dismissOnBackPress=!busy,dismissOnClickOutside=!busy)){
        Surface(Modifier.fillMaxSize(),color=Cream){Column(Modifier.fillMaxSize().systemBarsPadding().padding(18.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)){
            Text(ui("匯入菜單與餐廳資料"),style=MaterialTheme.typography.titleLarge)
            Small(ui("貼上含 sourceType、sourceUrl、observedAt、license 及 menuItems／reportedSpends／recommendations 的 JSON。"))
            OutlinedTextField(json,{json=it},label={Text("JSON")},minLines=7,modifier=Modifier.fillMaxWidth(),enabled=!busy)
            Small(ui("菜單、單次消費與預算分開保存；匯入後不會自動扣預算。"))
            OutlinedTextField(query,{query=it},label={Text(ui("地點名稱"))},enabled=!busy)
            Row{OutlinedTextField(lat,{lat=it},label={Text(ui("緯度"))},modifier=Modifier.weight(1f),enabled=!busy);OutlinedTextField(lng,{lng=it},label={Text(ui("經度"))},modifier=Modifier.weight(1f),enabled=!busy)}
            TextButton(onClick={run{places=store.searchPoi(JSONObject().put("provider","osm").put("category","food").put("query",query).put("lat",lat.toDouble()).put("lng",lng.toDouble()).put("radius",1500)).array("places").objects()}},enabled=!busy){Text(ui("搜尋地圖地點"))}
            places.forEach{point->Text(point.optString("name"));Small(point.optString("address"));TextButton(onClick={run{
                require(json.toByteArray().size<=250000){"JSON 太大"};val body=JSONObject(json).put("placeId",point.getString("id")).put("placeName",point.getString("name")).put("lat",point.getDouble("lat")).put("lng",point.getDouble("lng"));store.socialRequest("restaurants/import",body);message="餐廳資料已匯入，請重新搜尋查看。"
            }},enabled=!busy){Text(ui("確認分店並匯入"))}}
            if(busy)LinearProgressIndicator(Modifier.fillMaxWidth());message?.let{Text(ui(it))}
            TextButton(onClick=onDismiss,enabled=!busy){Text(ui("完成"))}
        }}
    }
}
@Composable private fun RestaurantSourceLine(source:JSONObject){
    val uri=LocalUriHandler.current
    if(source.optString("url").startsWith("https://"))TextButton(onClick={uri.openUri(source.getString("url"))}){Text(source.optString("name"))}
    Small(ui("資料日期：{0}",source.optString("observedAt").take(10)))
}
@Composable internal fun RestaurantPricePanel(point:JSONObject,store:BuddyStore){
    var data by remember(point.toString()){mutableStateOf(point.getJSONObject("restaurantData"))}
    var quantities by remember{mutableStateOf<Map<String,Int>>(emptyMap())};var people by remember{mutableIntStateOf(1)}
    var busy by remember{mutableStateOf(false)};var message by remember{mutableStateOf<String?>(null)};val scope=rememberCoroutineScope()
    Text(ui("菜單標價"),style=MaterialTheme.typography.titleMedium)
    if(data.array("menuItems").length()==0)Small(ui("來源未提供菜單價格。"))
    data.array("menuItems").objects().forEach{item->Column(verticalArrangement=Arrangement.spacedBy(4.dp)){
        Text(item.optString("name")+" · "+item.optString("currency")+" %.2f".format(item.optDouble("amount")))
        Small(ui(when(item.optString("channel")){"dine_in"->"內用";"delivery"->"外送";else->"外帶"}));RestaurantSourceLine(item.getJSONObject("source"))
        if(item.optString("currency")=="AUD"&&item.optString("channel")=="dine_in"){
            val id=item.getString("id");val qty=quantities[id]?:0
            Row{TextButton(onClick={quantities=quantities+(id to qty-1)},enabled=qty>0&&!busy){Text("−")};Text(ui("份數：{0}",qty.toString()));TextButton(onClick={quantities=quantities+(id to qty+1)},enabled=qty<30&&!busy){Text("＋")}}
        }
    }}
    Text(ui("網友單次消費"),style=MaterialTheme.typography.titleMedium)
    if(data.array("reportedSpends").length()==0)Small(ui("來源未提供單次消費。"))
    data.array("reportedSpends").objects().forEach{row->
        if(row.isNull("amount"))Text(ui("金額未結構化，保留原文供參考。"))else Text(row.optString("currency")+" %.2f".format(row.optDouble("amount")));Text(row.optString("quote"));Small(if(row.isNull("people"))ui("消費人數未知")else ui("消費人數：{0}",row.getInt("people").toString()));Small(row.optString("author").ifBlank{ui("來源未提供作者")}+" · "+row.optString("date").takeIf{it.isNotBlank()&&it!="null"}?.take(10).orEmpty().ifBlank{ui("發文日期未提供")});RestaurantSourceLine(row.getJSONObject("source"))
    }
    Text(ui("每人預估預算"),style=MaterialTheme.typography.titleMedium)
    data.optJSONObject("budgetEstimate")?.let{estimate->if(estimate.optString("status")=="ready"){Text(estimate.optString("currency")+" %.2f / 人".format(estimate.optDouble("perPerson")));Small(estimate.optString("note"));Small(ui("估算人數：{0}",estimate.optInt("people").toString()));estimate.array("basis").objects().forEach{Small(it.optString("name")+" × "+it.optInt("quantity")+" · "+it.optDouble("unitPrice"))};Small(ui("估算日期：{0}",estimate.optString("calculatedAt").take(10)))}else Small(ui("尚未選餐，預算待確認。"))}
    Small(ui("目前預算以 AUD 內用價格估算。"))
    Row{TextButton(onClick={people--},enabled=people>1&&!busy){Text("−")};Text(ui("用餐人數：{0}",people.toString()));TextButton(onClick={people++},enabled=people<100&&!busy){Text("＋")}}
    Button(onClick={scope.launch{busy=true;message=null;try{
        val items=JSONArray();quantities.filterValues{it>0}.forEach{(id,qty)->items.put(JSONObject().put("id",id).put("quantity",qty))}
        val updated=store.socialRequest("restaurants/estimate",JSONObject().put("placeId",point.getString("id")).put("people",people).put("currency","AUD").put("channel","dine_in").put("items",items))
        data=updated.getJSONObject("restaurantData");if(store.poiToolbox.any{it.optString("id")==updated.optString("id")}){store.removePoi(updated.getString("id"));store.addPoi(updated)}
        message="估算已儲存；既有行程保留原估算，重新加入可套用。"
    }catch(e:Exception){message=e.message}finally{busy=false}}},enabled=!busy&&quantities.values.any{it>0}){Text(ui("依所選餐點估算"))}
    Text(ui("推薦菜色與評論"),style=MaterialTheme.typography.titleMedium)
    (data.array("recommendations").objects()+data.array("reviews").objects()).forEach{row->
        if(row.optString("dish").isNotBlank())Text(row.getString("dish"));Text(row.optString("quote"));Small(row.optString("author").ifBlank{ui("來源未提供作者")}+" · "+row.optString("date").takeIf{it.isNotBlank()&&it!="null"}?.take(10).orEmpty().ifBlank{ui("發文日期未提供")});RestaurantSourceLine(row.getJSONObject("source"))
    }
    if(busy)LinearProgressIndicator(Modifier.fillMaxWidth());message?.let{Text(ui(it))}
}

@Composable internal fun ActivityReferencesPanel(point:JSONObject,store:BuddyStore){
    val lat=point.optDouble("lat");val lng=point.optDouble("lng");val source=store.travelDataSource;val placeId=point.optString("id");val placeName=point.optString("name")
    var offers by remember{mutableStateOf<List<JSONObject>>(emptyList())};var busy by remember{mutableStateOf(false)}
    var message by remember{mutableStateOf<String?>(null)};var retry by remember{mutableStateOf(0)}
    val uri=LocalUriHandler.current
    LaunchedEffect(placeId,lat,lng,source,retry){
        busy=true;message=null;offers=emptyList()
        try{
            val result=store.socialRequest("activities/search",JSONObject().put("lat",lat).put("lng",lng).put("radius",1000).put("placeId",placeId).put("placeName",placeName))
            if(source==store.travelDataSource){require(result.optString("dataSource")==source);offers=result.array("offers").objects();if(offers.isEmpty())message="此景點尚無已確認的活動票價資料。"}
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){if(source==store.travelDataSource)message="活動參考暫時無法載入，請重試。"}finally{if(source==store.travelDataSource)busy=false}
    }
    Text(ui("此景點的活動與參考票價"),style=MaterialTheme.typography.titleMedium)
    Small(ui("只顯示已配對此景點的活動；票種、日期與名額以官網為準，不自動扣預算。"))
    if(busy)LinearProgressIndicator(Modifier.fillMaxWidth())
    message?.let{Small(ui(it));TextButton(onClick={retry++}){Text(ui("重新查詢"))}}
    offers.forEach{offer->Card(Modifier.fillMaxWidth()){Column(Modifier.padding(12.dp),verticalArrangement=Arrangement.spacedBy(6.dp)){
        Text(offer.optString("title"),style=MaterialTheme.typography.titleSmall)
        Small(offer.optString("venueName")+" · "+offer.optInt("distanceMeters")+" m")
        Text((if(offer.isNull("amount"))ui("票價待確認")else if(offer.optDouble("amount")==0.0)ui("免費")else offer.optString("currency")+" %.2f".format(offer.optDouble("amount")))+" · "+offer.optString("ticketType"))
        Small(offer.optString("conditions"));Small(ui("資料日期：{0}",offer.optString("observedOn")))
        if(offer.optBoolean("stale"))Small(ui("票價待重新確認，請查看官網。"))
        if(offer.optString("sourceUrl").startsWith("https://"))TextButton(onClick={uri.openUri(offer.getString("sourceUrl"))}){Text(offer.optString("sourceName"))}
    }}}
}
