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
    private val uiCatalog by lazy {JSONObject(app.assets.open("ui-localizations.json").bufferedReader().use{it.readText()})}
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
    fun chooseLanguage(value:AppLanguage){language=AppLanguage.fromStored(value.code);prefs.edit().putString("appLanguage",language.code).apply()}
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
    var travelDataSource by mutableStateOf(prefs.getString("travelDataSource","direct")?.takeIf{it in listOf("direct","zetaris","zetaris-cloud")} ?: "direct");private set
    var dataConnection by mutableStateOf<String?>(null);private set
    var mapLayerIds by mutableStateOf<Set<String>?>(null);private set
    fun chooseTravelDataSource(value:String){require(value in listOf("direct","zetaris","zetaris-cloud"));travelDataSource=value;prefs.edit().putString("travelDataSource",value).apply();weather=null;insights=null;dataConnection=null;mapLayerIds=null;reloadTravelData()}
    fun reloadTravelData(){val selected=travelDataSource;viewModelScope.launch{try{
        val maps=JSONObject(request("data/map-layers"));val status=JSONObject(request("data/status"))
        if(selected==travelDataSource){require(maps.optString("dataSource")==selected&&status.optString("dataSource")==selected);mapLayerIds=maps.array("layers").objects().map{it.getString("id")}.toSet();dataConnection=translated(uiCatalog,language,"Connected · {0} itinerary places",status.optInt("catalogCount").toString())}
    }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){if(selected==travelDataSource)dataConnection=e.message ?: "Could not connect to the data source."}}}
    var poiToolbox by mutableStateOf(runCatching{JSONArray(prefs.getString("poiToolbox","[]")).objects()}.getOrDefault(emptyList()));private set
    fun addPoi(p:JSONObject){if(poiToolbox.none{it.optString("id")==p.optString("id")}){poiToolbox=poiToolbox+p.copy();prefs.edit().putString("poiToolbox",JSONArray(poiToolbox).toString()).apply()}}
    fun removePoi(id:String){poiToolbox=poiToolbox.filterNot{it.optString("id")==id};prefs.edit().putString("poiToolbox",JSONArray(poiToolbox).toString()).apply()}
    suspend fun socialRequest(path:String,body:JSONObject?=null)=JSONObject(request(path,if(body==null)"GET"else "POST",body))
    suspend fun poiMedia(body:JSONObject)=JSONObject(request("poi/media","POST",body))
    suspend fun searchPoi(body:JSONObject)=JSONObject(request("poi/search","POST",body))
    private var backendRevision=0L
    var backendMode by mutableStateOf(prefs.getString("backendMode","local") ?: "local");private set
    var base by mutableStateOf(backendAddress(backendMode));private set
    fun backendAddress(mode:String):String=prefs.getString(if(mode=="cloud")"cloudBackendURL" else "localBackendURL",if(mode=="cloud")"http://127.0.0.1:8790" else BuildConfig.BACKEND_URL)!!
    fun chooseBackend(mode:String,address:String,accessCode:String="") {
        if(busy)return
        val uri=BackendEndpoint.validated(address)
        if(mode !in listOf("local","cloud") || uri==null){error="Please enter an HTTPS URL, or a local HTTP URL for 127.0.0.1.";return}
        if(mode=="cloud" && accessCode.isNotBlank()){try{BackendAccess.save(uri.toString().trimEnd('/'),accessCode)}catch(_:Exception){error="Could not save connection code securely. Settings unchanged.";return}}
        base=uri.toString().trimEnd('/');backendMode=mode
        prefs.edit().putString("backendMode",mode).putString(if(mode=="cloud")"cloudBackendURL" else "localBackendURL",base).apply()
        backendRevision++;travelDataSource="direct";prefs.edit().putString("travelDataSource","direct").apply()
        health=null;saved=emptyList();memories=emptyList();trip=null;proposal=null;weather=null;insights=null;dataConnection=null;mapLayerIds=null;mapViewport=null;dayIndex=0;error=null;online=false
        reload()
    }
    val day: JSONObject? get() = trip?.array("days")?.objects()?.getOrNull(dayIndex)

    init { prefs.edit().putString("appLanguage",language.code).apply();BackendAccess.initialize(app);reload() }

    private suspend fun request(path: String, method: String = "GET", body: JSONObject? = null): String = withContext(Dispatchers.IO) {
        val selectedSource=travelDataSource
        val requestBase=base;val requestRevision=backendRevision
        val conn = URL("$requestBase/$path").openConnection() as HttpURLConnection
        try {
            BackendAccess.token(conn.url.toString())?.let{conn.setRequestProperty("Authorization","Bearer "+it)}
            conn.requestMethod = method; conn.connectTimeout = 10000; conn.readTimeout = 240000
            conn.instanceFollowRedirects = false;conn.useCaches=false
            conn.setRequestProperty("X-Travel-Data-Source",selectedSource)
            conn.setRequestProperty("Content-Type", "application/json")
            if (body != null) { conn.doOutput = true; conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) } }
            val status = conn.responseCode
            if(requestBase!=base || requestRevision!=backendRevision)throw kotlinx.coroutines.CancellationException("Backend changed")
            val result = (if (status in 200..299) conn.inputStream else conn.errorStream)?.bufferedReader(Charsets.UTF_8)?.use { it.readText() } ?: ""
            if(requestBase!=base || requestRevision!=backendRevision)throw kotlinx.coroutines.CancellationException("Backend changed")
            if (status !in 200..299) {
                val message = runCatching { JSONObject(result).optString("error") }.getOrDefault("")
                error(if (status == 409) "This trip has changed. Discard the draft, refresh the trip and try again." else message.ifBlank { "The service returned HTTP $status." })
            }
            if(path in listOf("weather","plan","catalog","data/map-layers","data/status","data/insights")&&selectedSource!=travelDataSource)error("The data source changed. Please search again.")
            result
        } finally { conn.disconnect() }
    }

    private fun operation(action: suspend () -> Unit) {
        if (busy) return
        busy = true
        viewModelScope.launch {
            try { action() }
            catch (e: Exception) { error = when (e) {
                is java.net.ConnectException -> "Could not connect to the local service. Start the backend and run adb reverse tcp:8787 tcp:8787."
                is java.net.SocketTimeoutException -> "The request timed out. Please retry. Your original trip is unchanged."
                else -> e.message ?: "Could not complete this action. Please retry."
            } }
            finally { busy = false }
        }
    }
    private suspend fun load() {
        health = JSONObject(request("health"))
        health?.optString("managedTravelDataSource")?.takeIf{it in listOf("direct","zetaris","zetaris-cloud")}?.let{selected->travelDataSource=selected;prefs.edit().putString("travelDataSource",selected).apply()}
        val trips = JSONArray(request("trips")).objects().sortedByDescending { it.optString("createdAt") }
        memories = JSONArray(request("memories")).objects()
        saved = trips
        val selected = trip?.optString("id") ?: prefs.getString("selectedTrip", null)
        val current = trips.find { it.optString("id") == selected } ?: trips.firstOrNull()
        if (current != null) select(current)
        else { trip = null; weather = null }
        reloadTravelData()
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
        if (!Profile.from(source).valid()) { error = "Check the date, trip length, budget, steps and departure/return times."; return }
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
    suspend fun renameTrip(original:JSONObject,value:String):Boolean {
        if(busy)return false
        busy=true
        try{
            val accepted=JSONObject(request("trips","POST",tripRenamePayload(original,value)))
            saved=saved.map{if(it.optString("id")==accepted.optString("id"))accepted else it}
            if(trip?.optString("id")==accepted.optString("id"))trip=accepted
            return true
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){error=e.message ?: "Name was not saved. Please try again.";return false}finally{busy=false}
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
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){if(generation==insightGeneration)insightsError="Transportation, Crowds & Costs Currently Unavailable, Please Try Again."}finally{if(generation==insightGeneration)insightsBusy=false}}
    }
    var weatherBusy by mutableStateOf(false);private set
    fun refreshWeather() {
        if(weatherBusy)return
        weatherBusy=true;val selected=travelDataSource
        weather=JSONObject().put("summary","Checking live weather…").put("rainy",false).put("status","loading").put("source",if(selected!="direct")"Open-Meteo / Zetaris"else "Open-Meteo / direct")
        val source=trip?.getJSONObject("profile") ?: profile.json()
        viewModelScope.launch{try {
            val result=JSONObject(request("weather","POST",source));if(selected==travelDataSource){weather=result;if(result.optString("status")=="ok")mark("weather")}
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){
            if(selected==travelDataSource){weather=JSONObject().put("summary","Live weather unavailable. Please retry.").put("status","unavailable").put("rainy",false);error=e.message}
        }finally{weatherBusy=false}}
    }
    fun saveMemory(consent: Boolean) {
        if (!consent) return
        val source = trip?.getJSONObject("profile") ?: profile.json()
        operation {
            request("memories", "POST", JSONObject().put("consent", true).put("profile", source)
                .put("summary", "Interests: ${source.array("interests").strings().map{translated(uiCatalog,language,it)}.joinToString(", ")}; preferred daily walking limit: about ${source.optInt("maxSteps")} steps"))
            load(); notice = "Preference saved locally."
        }
    }
    fun deleteMemory(value: JSONObject) = operation { request("memories/${value.getString("id")}", "DELETE"); load() }
    fun addExpense(title: String, amount: String, people: Int) {
        runCatching {
            val cents = Money.cents(amount); Money.shares(cents, people)
            expenses = expenses + JSONObject().put("id", UUID.randomUUID().toString()).put("title", title.trim().ifBlank { "Expense" }).put("cents", cents).put("people", people)
            persistExpenses()
        }.onFailure { error = it.message }
    }
    fun deleteExpense(value: JSONObject) { expenses = expenses.filterNot { it.getString("id") == value.getString("id") }; persistExpenses() }
    private fun persistExpenses() { prefs.edit().putString("expenses", JSONArray(expenses).toString()).apply() }
}
