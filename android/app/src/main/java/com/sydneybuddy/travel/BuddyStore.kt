package com.sydneybuddy.travel

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID

class BuddyStore(app: Application) : AndroidViewModel(app) {
    private val prefs = app.getSharedPreferences("buddy", 0)
    var profile by mutableStateOf(runCatching { Profile.from(JSONObject(prefs.getString("profile", "")!!)) }.getOrDefault(Profile()))
    var trip by mutableStateOf<JSONObject?>(null); private set
    var proposal by mutableStateOf<Proposal?>(null); private set
    var saved by mutableStateOf<List<JSONObject>>(emptyList()); private set
    var memories by mutableStateOf<List<JSONObject>>(emptyList()); private set
    var health by mutableStateOf<JSONObject?>(null); private set
    var error by mutableStateOf<String?>(null)
    var notice by mutableStateOf<String?>(null)
    var busy by mutableStateOf(false); private set
    var weather by mutableStateOf<JSONObject?>(null); private set
    var online by mutableStateOf(false)
    var language by mutableStateOf(AppLanguage.fromStored(prefs.getString("appLanguage",null)));private set
    fun chooseLanguage(value:AppLanguage){language=value;prefs.edit().putString("appLanguage",value.code).apply()}
    var tab by mutableStateOf(1)
    var dayIndex by mutableStateOf(0)
    var onboarding by mutableStateOf(!prefs.getBoolean("onboarded", false))
    var preparations by mutableStateOf(prefs.getStringSet("preparations", emptySet())!!.toSet()); private set
    var expenses by mutableStateOf(runCatching { JSONArray(prefs.getString("expenses", "[]")).objects() }.getOrDefault(emptyList())); private set
    var mapProvider by mutableStateOf(MapProvider.fromStored(prefs.getString("mapProvider", null)))
        private set
    var mapViewport: MapViewport? = null
    fun chooseMapProvider(value: MapProvider) {
        mapProvider = value
        prefs.edit().putString("mapProvider", value.name).apply()
    }
    var travelDataSource by mutableStateOf(prefs.getString("travelDataSource","direct")?.takeIf{it in listOf("direct","zetaris")} ?: "direct");private set
    var dataConnection by mutableStateOf<String?>(null);private set
    var mapLayerIds by mutableStateOf<Set<String>?>(null);private set
    fun chooseTravelDataSource(value:String){require(value in listOf("direct","zetaris"));travelDataSource=value;prefs.edit().putString("travelDataSource",value).apply();weather=null;insights=null;dataConnection=null;mapLayerIds=null;reloadTravelData()}
    fun reloadTravelData(){val selected=travelDataSource;viewModelScope.launch{try{
        val maps=JSONObject(request("data/map-layers"));val status=JSONObject(request("data/status"))
        if(selected==travelDataSource){require(maps.optString("dataSource")==selected&&status.optString("dataSource")==selected);mapLayerIds=maps.array("layers").objects().map{it.getString("id")}.toSet();dataConnection=translated(JSONObject(getApplication<Application>().assets.open("ui-localizations.json").bufferedReader().use{it.readText()}),language,"資料來源已連線 · {0} 個行程景點",status.optInt("catalogCount").toString())}
    }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){if(selected==travelDataSource)dataConnection=e.message ?: "資料來源連線失敗"}}}
    var poiToolbox by mutableStateOf(runCatching{JSONArray(prefs.getString("poiToolbox","[]")).objects()}.getOrDefault(emptyList()));private set
    fun addPoi(p:JSONObject){if(poiToolbox.none{it.optString("id")==p.optString("id")}){poiToolbox=poiToolbox+p.copy();prefs.edit().putString("poiToolbox",JSONArray(poiToolbox).toString()).apply()}}
    fun removePoi(id:String){poiToolbox=poiToolbox.filterNot{it.optString("id")==id};prefs.edit().putString("poiToolbox",JSONArray(poiToolbox).toString()).apply()}
    suspend fun poiMedia(body:JSONObject)=JSONObject(request("poi/media","POST",body))
    suspend fun searchPoi(body:JSONObject)=JSONObject(request("poi/search","POST",body))
    val base = "http://127.0.0.1:8787"
    val day: JSONObject? get() = trip?.array("days")?.objects()?.getOrNull(dayIndex)

    init { reload() }

    private suspend fun request(path: String, method: String = "GET", body: JSONObject? = null): String = withContext(Dispatchers.IO) {
        val selectedSource=travelDataSource
        val conn = URL("$base/$path").openConnection() as HttpURLConnection
        try {
            conn.requestMethod = method; conn.connectTimeout = 10000; conn.readTimeout = 240000
            conn.instanceFollowRedirects = false;conn.useCaches=false
            conn.setRequestProperty("X-Travel-Data-Source",selectedSource)
            conn.setRequestProperty("Content-Type", "application/json")
            if (body != null) { conn.doOutput = true; conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) } }
            val status = conn.responseCode
            val result = (if (status in 200..299) conn.inputStream else conn.errorStream)?.bufferedReader(Charsets.UTF_8)?.use { it.readText() } ?: ""
            if (status !in 200..299) {
                val message = runCatching { JSONObject(result).optString("error") }.getOrDefault("")
                error(if (status == 409) "行程版本已变化。请取消草案，刷新行程后重新调整。" else message.ifBlank { "服务返回 HTTP $status" })
            }
            if(path in listOf("weather","plan","catalog","data/map-layers","data/status","data/insights")&&selectedSource!=travelDataSource)error("資料來源已變更，請重新查詢。")
            result
        } finally { conn.disconnect() }
    }

    private fun operation(action: suspend () -> Unit) {
        if (busy) return
        busy = true
        viewModelScope.launch {
            try { action() }
            catch (e: Exception) { error = when (e) {
                is java.net.ConnectException -> "无法连接电脑服务。请启动后端，并执行 adb reverse tcp:8787 tcp:8787。"
                is java.net.SocketTimeoutException -> "服务请求超时，请稍后重试。原行程未修改。"
                else -> e.message ?: "操作未完成，请重试。"
            } }
            finally { busy = false }
        }
    }
    private suspend fun load() {
        health = JSONObject(request("health"))
        val trips = JSONArray(request("trips")).objects().sortedByDescending { it.optString("createdAt") }
        memories = JSONArray(request("memories")).objects()
        saved = trips
        val selected = trip?.optString("id") ?: prefs.getString("selectedTrip", null)
        val current = trips.find { it.optString("id") == selected } ?: trips.firstOrNull()
        if (current != null) select(current)
        else { trip = null; weather = null }
    }
    fun reload() = operation { load() }
    fun select(value: JSONObject) {
        trip = value; weather = null; dayIndex = 0
        prefs.edit().putString("selectedTrip", value.getString("id")).apply()
    }
    fun rememberProfile() { prefs.edit().putString("profile", profile.json().toString()).apply() }
    fun resumeSaved() {
        onboarding = false
        prefs.edit().putBoolean("onboarded", true).apply()
        reload()
    }
    fun newTrip() {
        if (busy) return
        trip = null; proposal = null; weather = null; dayIndex = 0
        onboarding = true
    }
    fun generate(message: String = "", rain: Boolean = false) {
        val source = if (onboarding) profile.json() else trip?.getJSONObject("profile")?.copy() ?: profile.json()
        if (!Profile.from(source).valid()) { error = "请检查日期、天数、预算、步数和出发返回时间。"; return }
        val target = if (onboarding) null else trip?.optString("id")
        val revision = if (onboarding) 0 else trip?.optInt("revision", 0) ?: 0
        operation {
            source.put("request", message).put("mode", if (!online) "demo" else if (health?.optBoolean("googleConfigured") == true) "live" else "assisted")
            if (rain) source.put("weatherOverride", "rain") else source.remove("weatherOverride")
            val result = JSONObject(request("plan", "POST", source))
            proposal = Proposal(result, target, revision)
        }
    }
    suspend fun scheduleRequest(body:JSONObject)=JSONObject(request("schedule/editor","POST",body))
    fun previewSchedule(value:JSONObject) {proposal=Proposal(value.copy(),null,0)}
    fun applyScheduleDraft(value:JSONObject) {proposal=proposal?.copy(trip=value)}
    fun editSchedule(value:JSONObject) {select(value);proposal=Proposal(value.copy(),value.optString("id"),value.optInt("revision",0))}
    fun tryScheduleDemo()=operation {
        val source=Profile(days=3,budget=250,endMinute=1140).json().put("mode","demo")
        proposal=Proposal(JSONObject(request("plan","POST",source)),null,0)
    }
    fun cancelProposal() { if (!busy) proposal = null }
    fun accept() {
        val pending = proposal ?: return
        operation {
            val accepted = JSONObject(request("trips", "POST", savePayload(pending)))
            select(accepted); profile = Profile.from(accepted.getJSONObject("profile")); rememberProfile()
            proposal = null; onboarding = false; tab = 0
            prefs.edit().putBoolean("onboarded", true).apply()
            load()
        }
    }
    fun deleteTrip(value: JSONObject) = operation {
        request("trips/${value.getString("id")}", "DELETE")
        val prefix = value.getString("id") + ":"
        preparations = preparations.filterNot { it.startsWith(prefix) }.toSet()
        prefs.edit().putStringSet("preparations", preparations).apply()
        if (trip?.optString("id") == value.getString("id")) trip = null
        load()
    }
    fun prepared(key: String) = trip?.optString("id")?.let { "$it:$key" in preparations } ?: false
    fun mark(key: String, toggle: Boolean = false) {
        val id = trip?.optString("id") ?: return
        val full = "$id:$key"
        preparations = if (toggle && full in preparations) preparations - full else preparations + full
        prefs.edit().putStringSet("preparations", preparations).apply()
    }
    var insights by mutableStateOf<JSONObject?>(null);private set
    var insightsBusy by mutableStateOf(false);private set
    var insightsError by mutableStateOf<String?>(null);private set
    private var insightGeneration=0
    fun refreshInsights(requested:JSONArray?=null){
        val generation=++insightGeneration;val selected=travelDataSource;val provider=mapProvider;val scene=day.toString()
        insights=null;insightsBusy=true;insightsError=null
        val places=JSONArray();val start=day?.optJSONObject("start") ?: profile.json().optJSONObject("start") ?: Endpoint.quay.json()
        places.put(JSONObject().put("id","start").put("name",start.optString("name")).put("lat",start.optDouble("lat")).put("lng",start.optDouble("lng")))
        day?.array("stops")?.objects()?.take(28)?.forEach{p->places.put(JSONObject().put("id",p.optString("id")).put("name",p.optString("name")).put("lat",p.optDouble("lat")).put("lng",p.optDouble("lng")))}
        val body=JSONObject().put("places",requested ?: places).put("date",day?.optString("date") ?: profile.date).put("mapProvider",if(provider==MapProvider.GOOGLE)"google"else "open")
        viewModelScope.launch{try{
            val value=JSONObject(request("data/insights","POST",body))
            if(generation==insightGeneration&&selected==travelDataSource&&provider==mapProvider&&scene==day.toString())insights=value
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){if(generation==insightGeneration)insightsError="交通、人潮與費用暫時無法更新，請重試。"}finally{if(generation==insightGeneration)insightsBusy=false}}
    }
    var weatherBusy by mutableStateOf(false);private set
    fun refreshWeather() {
        if(weatherBusy)return
        weatherBusy=true;val selected=travelDataSource
        weather=JSONObject().put("summary","正在查詢即時天氣…").put("rainy",false).put("status","loading").put("source",if(selected=="zetaris")"Open-Meteo / Zetaris"else "Open-Meteo / direct")
        val source=trip?.getJSONObject("profile") ?: profile.json()
        viewModelScope.launch{try {
            val result=JSONObject(request("weather","POST",source));if(selected==travelDataSource){weather=result;if(result.optString("status")=="ok")mark("weather")}
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){
            if(selected==travelDataSource){weather=JSONObject().put("summary","即時天氣查詢失敗，請重試。").put("status","unavailable").put("rainy",false);error=e.message}
        }finally{weatherBusy=false}}
    }
    fun saveMemory(consent: Boolean) {
        if (!consent) return
        val source = trip?.getJSONObject("profile") ?: profile.json()
        operation {
            request("memories", "POST", JSONObject().put("consent", true).put("profile", source)
                .put("summary", "喜欢${source.array("interests").strings().joinToString("、")}，每天步数偏好约 ${source.optInt("maxSteps")} 步"))
            load(); notice = "偏好已保存到电脑本机"
        }
    }
    fun deleteMemory(value: JSONObject) = operation { request("memories/${value.getString("id")}", "DELETE"); load() }
    fun addExpense(title: String, amount: String, people: Int) {
        runCatching {
            val cents = Money.cents(amount); Money.shares(cents, people)
            expenses = expenses + JSONObject().put("id", UUID.randomUUID().toString()).put("title", title.trim().ifBlank { "消费" }).put("cents", cents).put("people", people)
            persistExpenses()
        }.onFailure { error = it.message }
    }
    fun deleteExpense(value: JSONObject) { expenses = expenses.filterNot { it.getString("id") == value.getString("id") }; persistExpenses() }
    private fun persistExpenses() { prefs.edit().putString("expenses", JSONArray(expenses).toString()).apply() }
}
