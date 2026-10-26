package com.sydneybuddy.travel

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.launch
import org.json.JSONObject

@Composable internal fun SocialImportDialog(store:BuddyStore,onDismiss:()->Unit,requester:suspend(String,JSONObject?)->JSONObject={path,body->store.socialRequest(path,body)}) {
    var jsonMode by remember{mutableStateOf(false)}
    var platform by remember{mutableStateOf("instagram")};var url by remember{mutableStateOf("")};var text by remember{mutableStateOf("")};var json by remember{mutableStateOf("")}
    var busy by remember{mutableStateOf(false)};var problem by remember{mutableStateOf<String?>(null)};var message by remember{mutableStateOf<String?>(null)}
    var preview by remember{mutableStateOf<JSONObject?>(null)};var record by remember{mutableStateOf<JSONObject?>(null)}
    var history by remember{mutableStateOf<List<JSONObject>>(emptyList())};var places by remember{mutableStateOf<List<JSONObject>>(emptyList())}
    var matching by remember{mutableStateOf<Int?>(null)};var query by remember{mutableStateOf("")};var lat by remember{mutableStateOf("-33.865")};var lng by remember{mutableStateOf("151.212")}
    val scope=rememberCoroutineScope();val context=LocalContext.current
    fun run(action:suspend()->Unit){scope.launch{busy=true;problem=null;message=null;try{action()}catch(e:Exception){problem=e.message ?: "Import failed. Please try again."}finally{busy=false}}}
    fun reset(){preview=null;record=null;matching=null;places=emptyList();problem=null;message=null}
    val picker=rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()){uri->if(uri!=null)run{
        json=context.contentResolver.openInputStream(uri)?.bufferedReader()?.use{reader->val buffer=CharArray(250001);var length=0;while(length<buffer.size){val count=reader.read(buffer,length,buffer.size-length);if(count<0)break;length+=count};require(length<=250000){"The JSON is too large."};String(buffer,0,length)} ?: error("Import failed. Please try again.")
        jsonMode=true
    }}
    Dialog(onDismissRequest={if(!busy)onDismiss()},properties=DialogProperties(usePlatformDefaultWidth=false,dismissOnBackPress=!busy,dismissOnClickOutside=!busy)){
        Surface(Modifier.fillMaxSize(),color=Cream){Column(Modifier.fillMaxSize().systemBarsPadding().padding(18.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)){
            Row{Text(ui("Import travel inspiration"),modifier=Modifier.weight(1f),style=MaterialTheme.typography.titleLarge);TextButton(onClick=onDismiss,enabled=!busy){Text(ui("Done"))}}
            if(preview==null&&record==null){
                Row{FilterChip(selected=!jsonMode,onClick={jsonMode=false},enabled=!busy,label={Text(ui("Paste post"))});Spacer(Modifier.width(8.dp));FilterChip(selected=jsonMode,onClick={jsonMode=true},enabled=!busy,label={Text(ui("Import JSON"))},modifier=Modifier.testTag("social-json-mode"))}
                if(jsonMode){TextButton(onClick={picker.launch(arrayOf("application/json","text/plain"))},enabled=!busy){Text(ui("Choose JSON file"))};OutlinedTextField(json,{json=it},modifier=Modifier.fillMaxWidth().testTag("social-import-json"),minLines=6,enabled=!busy,label={Text("JSON")});Small(ui("JSON requires a post source, text and mentions."))}
                else{
                    var expanded by remember{mutableStateOf(false)}
                    Box{TextButton(onClick={expanded=true},enabled=!busy){Text(platform)};DropdownMenu(expanded=expanded,onDismissRequest={expanded=false}){listOf("instagram","tiktok","threads","xiaohongshu").forEach{value->DropdownMenuItem(text={Text(value)},onClick={platform=value;expanded=false})}}}
                    OutlinedTextField(url,{url=it},label={Text(ui("Original post URL"))},singleLine=true,enabled=!busy,modifier=Modifier.fillMaxWidth().testTag("social-import-url"))
                    OutlinedTextField(text,{text=it},label={Text(ui("Paste travel content you have permission to use."))},minLines=6,enabled=!busy,modifier=Modifier.fillMaxWidth().testTag("social-import-text"))
                }
                Button(onClick={run{
                    if(jsonMode){require(json.toByteArray().size<=250000){"The JSON is too large."};val body=JSONObject(json);require(body.array("mentions").length()>0){"JSON requires a post source, text and mentions."};preview=body}
                    else{val body=JSONObject().put("platform",platform).put("postUrl",url).put("text",text);val result=requester("social/extract",body);if(result.array("mentions").length()==0)message="No places identified. Please add more detail." else preview=body.put("mentions",result.array("mentions"))}
                }},enabled=!busy&&(if(jsonMode)json.isNotBlank()else url.isNotBlank()&&text.isNotBlank()),modifier=Modifier.testTag("social-import-preview")){Text(ui(if(jsonMode)"Preview data"else "Extraction preview"))}
            }else{
                Text(ui("Extraction preview"),style=MaterialTheme.typography.titleMedium);Small((record?:preview)!!.optString("postUrl"))
                (record?:preview)!!.array("mentions").objects().forEachIndexed{index,mention->Panel{
                    Text(mention.optString("name"));Text(mention.optString("quote"));listOf("dishes","prices","reviews").forEach{field->mention.array(field).objects().forEach{Small(it.optString("quote"))}}
                    if(record!=null)TextButton(onClick={matching=index;query=mention.optString("name");places=emptyList()},enabled=!busy){Text(ui("Match a map place"))}
                }}
                if(record==null)Button(onClick={run{record=requester("social/import",preview);message="Saved. Match each place on the map."}},enabled=!busy,modifier=Modifier.testTag("social-import-save")){Text(ui("Save draft"))}
                TextButton(onClick=::reset,enabled=!busy){Text(ui("Start a new import"))}
            }
            if(matching!=null){Panel{
                Text(ui("Match a map place"));OutlinedTextField(query,{query=it},enabled=!busy,label={Text(ui("Place name"))})
                Row{OutlinedTextField(lat,{lat=it},enabled=!busy,label={Text(ui("Latitude"))},modifier=Modifier.weight(1f));OutlinedTextField(lng,{lng=it},enabled=!busy,label={Text(ui("Longitude"))},modifier=Modifier.weight(1f))}
                Small(ui("The search centre defaults to Sydney. You can edit the coordinates."))
                TextButton(onClick={run{val latitude=lat.toDoubleOrNull();val longitude=lng.toDoubleOrNull();require(latitude!=null&&longitude!=null&&latitude.isFinite()&&longitude.isFinite()&&kotlin.math.abs(latitude)<=90&&kotlin.math.abs(longitude)<=180){"Enter valid coordinates for the search centre."};places=requester("poi/search",JSONObject().put("provider","osm").put("query",query).put("lat",latitude).put("lng",longitude).put("radius",1500).put("category","all")).array("places").objects();if(places.isEmpty())message="No places found. Adjust the name or search centre."}},enabled=!busy){Text(ui("Search places on the map"))}
                places.forEach{place->Text(place.optString("name"));Small(place.optString("address"));TextButton(onClick={run{record=requester("social/${record!!.getString("id")}/publish",JSONObject().put("mentionIndex",matching).put("placeId",place.getString("id")).put("placeName",place.getString("name")).put("lat",place.getDouble("lat")).put("lng",place.getDouble("lng")));matching=null;places=emptyList();message="Published locally. Zetaris sync is pending."}},enabled=!busy){Text(ui("Confirm place and publish"))}}
            }}
            Small(ui("Quotes preserve the source. Prices and ratings are unverified."));Small(ui("Text extraction uses your configured AI. JSON import does not require AI."))
            if(busy)LinearProgressIndicator(Modifier.fillMaxWidth())
            problem?.let{Text(ui(it),color=MaterialTheme.colorScheme.error,modifier=Modifier.testTag("social-import-error"))};message?.let{Text(ui(it),color=Green,modifier=Modifier.testTag("social-import-message"))}
            TextButton(onClick={run{history=requester("social/imports",null).array("imports").objects()}},enabled=!busy){Text(ui("Load saved imports"))}
            history.forEach{item->TextButton(onClick={reset();record=item},enabled=!busy){Text(item.array("mentions").objects().firstOrNull()?.optString("name") ?: item.optString("postUrl"))}}
        }}
    }
}
