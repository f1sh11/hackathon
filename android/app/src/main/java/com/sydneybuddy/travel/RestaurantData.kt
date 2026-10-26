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
            Text(ui("Import menus and restaurant data"),style=MaterialTheme.typography.titleLarge)
            Small(ui("Paste JSON with sourceType, sourceUrl, observedAt, license and menuItems, reportedSpends or recommendations."))
            OutlinedTextField(json,{json=it},label={Text("JSON")},minLines=7,modifier=Modifier.fillMaxWidth(),enabled=!busy)
            Small(ui("Menu prices, reported spending and budgets stay separate. Importing does not deduct your budget."))
            OutlinedTextField(query,{query=it},label={Text(ui("Place name"))},enabled=!busy)
            Row{OutlinedTextField(lat,{lat=it},label={Text(ui("Latitude"))},modifier=Modifier.weight(1f),enabled=!busy);OutlinedTextField(lng,{lng=it},label={Text(ui("Longitude"))},modifier=Modifier.weight(1f),enabled=!busy)}
            TextButton(onClick={run{places=store.searchPoi(JSONObject().put("provider","osm").put("category","food").put("query",query).put("lat",lat.toDouble()).put("lng",lng.toDouble()).put("radius",1500)).array("places").objects()}},enabled=!busy){Text(ui("Search places on the map"))}
            places.forEach{point->Text(point.optString("name"));Small(point.optString("address"));TextButton(onClick={run{
                require(json.toByteArray().size<=250000){"The JSON is too large."};val body=JSONObject(json).put("placeId",point.getString("id")).put("placeName",point.getString("name")).put("lat",point.getDouble("lat")).put("lng",point.getDouble("lng"));store.socialRequest("restaurants/import",body);message="Restaurant data imported. Search again to view it."
            }},enabled=!busy){Text(ui("Confirm branch and import"))}}
            if(busy)LinearProgressIndicator(Modifier.fillMaxWidth());message?.let{Text(ui(it))}
            TextButton(onClick=onDismiss,enabled=!busy){Text(ui("Done"))}
        }}
    }
}
@Composable private fun RestaurantSourceLine(source:JSONObject){
    val uri=LocalUriHandler.current
    if(source.optString("url").startsWith("https://"))TextButton(onClick={uri.openUri(source.getString("url"))}){Text(source.optString("name"))}
    Small(ui("Data date: {0}",source.optString("observedAt").take(10)))
}
@Composable internal fun RestaurantPricePanel(point:JSONObject,store:BuddyStore){
    var data by remember(point.toString()){mutableStateOf(point.getJSONObject("restaurantData"))}
    var quantities by remember{mutableStateOf<Map<String,Int>>(emptyMap())};var people by remember{mutableIntStateOf(1)}
    var busy by remember{mutableStateOf(false)};var message by remember{mutableStateOf<String?>(null)};val scope=rememberCoroutineScope()
    Text(ui("Menu prices"),style=MaterialTheme.typography.titleMedium)
    if(data.array("menuItems").length()==0)Small(ui("No menu prices provided by this source."))
    data.array("menuItems").objects().forEach{item->Column(verticalArrangement=Arrangement.spacedBy(4.dp)){
        Text(item.optString("name")+" · "+item.optString("currency")+" %.2f".format(item.optDouble("amount")))
        Small(ui(when(item.optString("channel")){"dine_in"->"Dine-in";"delivery"->"Delivery";else->"Takeaway"}));RestaurantSourceLine(item.getJSONObject("source"))
        if(item.optString("currency")=="AUD"&&item.optString("channel")=="dine_in"){
            val id=item.getString("id");val qty=quantities[id]?:0
            Row{TextButton(onClick={quantities=quantities+(id to qty-1)},enabled=qty>0&&!busy){Text("−")};Text(ui("Quantity: {0}",qty.toString()));TextButton(onClick={quantities=quantities+(id to qty+1)},enabled=qty<30&&!busy){Text("＋")}}
        }
    }}
    Text(ui("Reported spending"),style=MaterialTheme.typography.titleMedium)
    if(data.array("reportedSpends").length()==0)Small(ui("No reported spending provided."))
    data.array("reportedSpends").objects().forEach{row->
        if(row.isNull("amount"))Text(ui("Amount not structured. Original text is provided for reference."))else Text(row.optString("currency")+" %.2f".format(row.optDouble("amount")));Text(row.optString("quote"));Small(if(row.isNull("people"))ui("Number of diners unknown")else ui("Diners: {0}",row.getInt("people").toString()));Small(row.optString("author").ifBlank{ui("Author not provided")}+" · "+row.optString("date").takeIf{it.isNotBlank()&&it!="null"}?.take(10).orEmpty().ifBlank{ui("Post date not provided")});RestaurantSourceLine(row.getJSONObject("source"))
    }
    Text(ui("Estimated budget per person"),style=MaterialTheme.typography.titleMedium)
    data.optJSONObject("budgetEstimate")?.let{estimate->if(estimate.optString("status")=="ready"){Text(estimate.optString("currency")+" %.2f / person".format(estimate.optDouble("perPerson")));Small(estimate.optString("note"));Small(ui("Estimate covers {0} people",estimate.optInt("people").toString()));estimate.array("basis").objects().forEach{Small(it.optString("name")+" × "+it.optInt("quantity")+" · "+it.optDouble("unitPrice"))};Small(ui("Estimated on {0}",estimate.optString("calculatedAt").take(10)))}else Small(ui("Choose dishes to estimate your budget."))}
    Small(ui("Budget estimates currently use AUD dine-in prices."))
    Row{TextButton(onClick={people--},enabled=people>1&&!busy){Text("−")};Text(ui("People dining: {0}",people.toString()));TextButton(onClick={people++},enabled=people<100&&!busy){Text("＋")}}
    Button(onClick={scope.launch{busy=true;message=null;try{
        val items=JSONArray();quantities.filterValues{it>0}.forEach{(id,qty)->items.put(JSONObject().put("id",id).put("quantity",qty))}
        val updated=store.socialRequest("restaurants/estimate",JSONObject().put("placeId",point.getString("id")).put("people",people).put("currency","AUD").put("channel","dine_in").put("items",items))
        data=updated.getJSONObject("restaurantData");if(store.poiToolbox.any{it.optString("id")==updated.optString("id")}){store.removePoi(updated.getString("id"));store.addPoi(updated)}
        message="Estimate saved. Existing trips keep their original estimate; add the meal again to apply this one."
    }catch(e:Exception){message=e.message}finally{busy=false}}},enabled=!busy&&quantities.values.any{it>0}){Text(ui("Estimate selected dishes"))}
    Text(ui("Recommended dishes and reviews"),style=MaterialTheme.typography.titleMedium)
    (data.array("recommendations").objects()+data.array("reviews").objects()).forEach{row->
        if(row.optString("dish").isNotBlank())Text(row.getString("dish"));Text(row.optString("quote"));Small(row.optString("author").ifBlank{ui("Author not provided")}+" · "+row.optString("date").takeIf{it.isNotBlank()&&it!="null"}?.take(10).orEmpty().ifBlank{ui("Post date not provided")});RestaurantSourceLine(row.getJSONObject("source"))
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
            if(source==store.travelDataSource){require(result.optString("dataSource")==source);offers=result.array("offers").objects();if(offers.isEmpty())message="No confirmed activity price references for this place yet."}
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){if(source==store.travelDataSource)message="Activity references could not load. Please retry."}finally{if(source==store.travelDataSource)busy=false}
    }
    Text(ui("Activities & reference prices for this place"),style=MaterialTheme.typography.titleMedium)
    Small(ui("Only activities matched to this place are shown. Check official ticket types, dates and availability. Not added to your budget."))
    if(busy)LinearProgressIndicator(Modifier.fillMaxWidth())
    message?.let{Small(ui(it));TextButton(onClick={retry++}){Text(ui("Retry"))}}
    offers.forEach{offer->Card(Modifier.fillMaxWidth()){Column(Modifier.padding(12.dp),verticalArrangement=Arrangement.spacedBy(6.dp)){
        Text(offer.optString("title"),style=MaterialTheme.typography.titleSmall)
        Small(offer.optString("venueName")+" · "+offer.optInt("distanceMeters")+" m")
        Text((if(offer.isNull("amount"))ui("Price not confirmed")else if(offer.optDouble("amount")==0.0)ui("Free")else offer.optString("currency")+" %.2f".format(offer.optDouble("amount")))+" · "+offer.optString("ticketType"))
        Small(offer.optString("conditions"));Small(ui("Data date: {0}",offer.optString("observedOn")))
        if(offer.optBoolean("stale"))Small(ui("Price needs checking. See the official website."))
        if(offer.optString("sourceUrl").startsWith("https://"))TextButton(onClick={uri.openUri(offer.getString("sourceUrl"))}){Text(offer.optString("sourceName"))}
    }}}
}
