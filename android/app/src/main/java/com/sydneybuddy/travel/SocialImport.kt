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
    fun run(action:suspend()->Unit){scope.launch{busy=true;problem=null;message=null;try{action()}catch(e:Exception){problem=e.message ?: "匯入失敗，請重試。"}finally{busy=false}}}
    fun reset(){preview=null;record=null;matching=null;places=emptyList();problem=null;message=null}
    val picker=rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()){uri->if(uri!=null)run{
        json=context.contentResolver.openInputStream(uri)?.bufferedReader()?.use{reader->val buffer=CharArray(250001);var length=0;while(length<buffer.size){val count=reader.read(buffer,length,buffer.size-length);if(count<0)break;length+=count};require(length<=250000){"JSON 太大"};String(buffer,0,length)} ?: error("匯入失敗，請重試。")
        jsonMode=true
    }}
    Dialog(onDismissRequest={if(!busy)onDismiss()},properties=DialogProperties(usePlatformDefaultWidth=false,dismissOnBackPress=!busy,dismissOnClickOutside=!busy)){
        Surface(Modifier.fillMaxSize(),color=Cream){Column(Modifier.fillMaxSize().systemBarsPadding().padding(18.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)){
            Row{Text(ui("匯入社群靈感"),modifier=Modifier.weight(1f),style=MaterialTheme.typography.titleLarge);TextButton(onClick=onDismiss,enabled=!busy){Text(ui("完成"))}}
            if(preview==null&&record==null){
                Row{FilterChip(selected=!jsonMode,onClick={jsonMode=false},enabled=!busy,label={Text(ui("貼上貼文"))});Spacer(Modifier.width(8.dp));FilterChip(selected=jsonMode,onClick={jsonMode=true},enabled=!busy,label={Text(ui("匯入 JSON"))},modifier=Modifier.testTag("social-json-mode"))}
                if(jsonMode){TextButton(onClick={picker.launch(arrayOf("application/json","text/plain"))},enabled=!busy){Text(ui("選擇 JSON 檔案"))};OutlinedTextField(json,{json=it},modifier=Modifier.fillMaxWidth().testTag("social-import-json"),minLines=6,enabled=!busy,label={Text("JSON")});Small(ui("JSON 需要貼文來源、text 與 mentions。"))}
                else{
                    var expanded by remember{mutableStateOf(false)}
                    Box{TextButton(onClick={expanded=true},enabled=!busy){Text(platform)};DropdownMenu(expanded=expanded,onDismissRequest={expanded=false}){listOf("instagram","tiktok","threads","xiaohongshu").forEach{value->DropdownMenuItem(text={Text(value)},onClick={platform=value;expanded=false})}}}
                    OutlinedTextField(url,{url=it},label={Text(ui("原貼文網址"))},singleLine=true,enabled=!busy,modifier=Modifier.fillMaxWidth().testTag("social-import-url"))
                    OutlinedTextField(text,{text=it},label={Text(ui("貼上你有權使用的旅遊貼文內容。"))},minLines=6,enabled=!busy,modifier=Modifier.fillMaxWidth().testTag("social-import-text"))
                }
                Button(onClick={run{
                    if(jsonMode){require(json.toByteArray().size<=250000){"JSON 太大"};val body=JSONObject(json);require(body.array("mentions").length()>0){"JSON 需要貼文來源、text 與 mentions。"};preview=body}
                    else{val body=JSONObject().put("platform",platform).put("postUrl",url).put("text",text);val result=requester("social/extract",body);if(result.array("mentions").length()==0)message="沒有找到可辨識的景點，請補充內容。" else preview=body.put("mentions",result.array("mentions"))}
                }},enabled=!busy&&(if(jsonMode)json.isNotBlank()else url.isNotBlank()&&text.isNotBlank()),modifier=Modifier.testTag("social-import-preview")){Text(ui(if(jsonMode)"預覽資料"else "萃取預覽"))}
            }else{
                Text(ui("萃取預覽"),style=MaterialTheme.typography.titleMedium);Small((record?:preview)!!.optString("postUrl"))
                (record?:preview)!!.array("mentions").objects().forEachIndexed{index,mention->Panel{
                    Text(mention.optString("name"));Text(mention.optString("quote"));listOf("dishes","prices","reviews").forEach{field->mention.array(field).objects().forEach{Small(it.optString("quote"))}}
                    if(record!=null)TextButton(onClick={matching=index;query=mention.optString("name");places=emptyList()},enabled=!busy){Text(ui("比對地圖地點"))}
                }}
                if(record==null)Button(onClick={run{record=requester("social/import",preview);message="已儲存，請逐一比對地圖地點。"}},enabled=!busy,modifier=Modifier.testTag("social-import-save")){Text(ui("確認儲存草稿"))}
                TextButton(onClick=::reset,enabled=!busy){Text(ui("開始新的匯入"))}
            }
            if(matching!=null){Panel{
                Text(ui("比對地圖地點"));OutlinedTextField(query,{query=it},enabled=!busy,label={Text(ui("地點名稱"))})
                Row{OutlinedTextField(lat,{lat=it},enabled=!busy,label={Text(ui("緯度"))},modifier=Modifier.weight(1f));OutlinedTextField(lng,{lng=it},enabled=!busy,label={Text(ui("經度"))},modifier=Modifier.weight(1f))}
                Small(ui("搜尋中心預設雪梨，可修改座標。"))
                TextButton(onClick={run{val latitude=lat.toDoubleOrNull();val longitude=lng.toDoubleOrNull();require(latitude!=null&&longitude!=null&&latitude.isFinite()&&longitude.isFinite()&&kotlin.math.abs(latitude)<=90&&kotlin.math.abs(longitude)<=180){"請輸入有效的搜尋中心座標。"};places=requester("poi/search",JSONObject().put("provider","osm").put("query",query).put("lat",latitude).put("lng",longitude).put("radius",1500).put("category","all")).array("places").objects();if(places.isEmpty())message="找不到地點，請調整名稱或搜尋中心。"}},enabled=!busy){Text(ui("搜尋地圖地點"))}
                places.forEach{place->Text(place.optString("name"));Small(place.optString("address"));TextButton(onClick={run{record=requester("social/${record!!.getString("id")}/publish",JSONObject().put("mentionIndex",matching).put("placeId",place.getString("id")).put("placeName",place.getString("name")).put("lat",place.getDouble("lat")).put("lng",place.getDouble("lng")));matching=null;places=emptyList();message="已發布至本機景點；Zetaris 資料尚待同步。"}},enabled=!busy){Text(ui("確認地點並發布"))}}
            }}
            Small(ui("內容與評論保留原文證據，不代表已核實的價格或評分。"));Small(ui("貼文萃取會使用已設定的 AI；JSON 匯入不需 AI。"))
            if(busy)LinearProgressIndicator(Modifier.fillMaxWidth())
            problem?.let{Text(ui(it),color=MaterialTheme.colorScheme.error,modifier=Modifier.testTag("social-import-error"))};message?.let{Text(ui(it),color=Green,modifier=Modifier.testTag("social-import-message"))}
            TextButton(onClick={run{history=requester("social/imports",null).array("imports").objects()}},enabled=!busy){Text(ui("讀取已匯入資料"))}
            history.forEach{item->TextButton(onClick={reset();record=item},enabled=!busy){Text(item.array("mentions").objects().firstOrNull()?.optString("name") ?: item.optString("postUrl"))}}
        }}
    }
}
