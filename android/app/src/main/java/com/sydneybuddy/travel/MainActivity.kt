package com.sydneybuddy.travel

import android.content.Context
import android.content.res.Configuration
import java.util.Locale
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.ui.platform.testTag
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import kotlinx.coroutines.launch
import org.json.JSONObject

val Green = Color(0xFF0D825E)
val Ink = Color(0xFF193830)
val Cream = Color(0xFFFAF7F0)
val Mint = Color(0xFFDBF2E0)

class MainActivity : ComponentActivity() {
    override fun attachBaseContext(newBase:Context) {
        val configuration=Configuration(newBase.resources.configuration)
        configuration.setLocale(Locale.ENGLISH)
        configuration.setLayoutDirection(Locale.ENGLISH)
        super.attachBaseContext(newBase.createConfigurationContext(configuration))
    }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            MaterialTheme(colorScheme = lightColorScheme(primary = Green, onPrimary = Color.White,
                background = Cream, surface = Color.White, onSurface = Ink, secondaryContainer = Mint)) {
                BuddyApp()
            }
        }
    }
}

@Composable fun BuddyApp(store: BuddyStore = viewModel()) {
    CompositionLocalProvider(LocalAppLanguage provides store.language){BuddyAppContent(store)}
}
@Composable private fun BuddyAppContent(store:BuddyStore) {
    var delete by remember { mutableStateOf<Pair<String, JSONObject>?>(null) }
    Scaffold(containerColor = Cream,
        topBar = {
            if (!store.onboarding && store.tab != 0) Row(Modifier.fillMaxWidth().statusBarsPadding().padding(horizontal = 22.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(ui(when(store.tab){1->"My trips";3->"Travel expenses";4->"Me & Koko";else->"Buddy"}),style=MaterialTheme.typography.headlineMedium,fontWeight=FontWeight.Bold)
                Spacer(Modifier.weight(1f))
                if (store.tab==1) TextButton(onClick = store::newTrip,enabled=!store.busy) { Icon(Icons.Outlined.Add,null);Text(ui("New trip")) }
            }
        },
        bottomBar = {
            if (!store.onboarding && store.proposal == null) IosNavigation(store)

        }
    ) { padding ->
        Column(Modifier.padding(padding).fillMaxSize().imePadding()) {
            if (store.busy) { LinearProgressIndicator(Modifier.fillMaxWidth()); Text("Koko is on it. One moment…", Modifier.padding(horizontal = 22.dp), style = MaterialTheme.typography.labelMedium) }
            if (store.proposal != null) ProposalScreen(store)
            else if (store.onboarding) Onboarding(store)
            else when (store.tab) {
                0 -> HomeScreen(store)
                1 -> TripsScreen(store) { delete = "trip" to it }
                2 -> AgentScreen(store)
                3 -> LedgerScreen(store) { delete = "expense" to it }
                else -> SettingsScreen(store) { delete = "memory" to it }
            }
        }
    }
    if (store.proposal != null) BackHandler { if (!store.busy) store.cancelProposal() }
    BackHandler(enabled = store.busy) { /* Keep the current operation visible until it finishes. */ }
    store.error?.let { text -> AlertDialog(onDismissRequest = { store.error = null }, title = { Text(ui("Could not complete")) }, text = { Text(text) },
        confirmButton = { TextButton(onClick = { store.error = null }) { Text(ui("Got it")) } }) }
    store.notice?.let { text -> AlertDialog(onDismissRequest = { store.notice = null }, text = { Text(text) },
        confirmButton = { TextButton(onClick = { store.notice = null }) { Text(ui("OK")) } }) }
    delete?.let { (type, value) -> AlertDialog(onDismissRequest = { delete = null }, title = { Text(ui("Delete this item?")) },
        text = { Text(if (type == "memory") "This preference will no longer be retrieved. The local audit history is retained." else "Delete this ${if (type == "trip") "trip" else "expense"}?") },
        confirmButton = { TextButton(onClick = { when (type) { "trip" -> store.deleteTrip(value); "memory" -> store.deleteMemory(value); else -> store.deleteExpense(value) }; delete = null }) { Text(ui("Delete")) } },
        dismissButton = { TextButton(onClick = { delete = null }) { Text(ui("Cancel")) } }) }
}

@Composable fun Page(content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(22.dp), verticalArrangement = Arrangement.spacedBy(16.dp), content = content)
}
@Composable fun Panel(content: @Composable ColumnScope.() -> Unit) {
    Surface(shape = RoundedCornerShape(24.dp), color = Color.White, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp), content = content)
    }
}
@Composable fun Heading(text: String) { Text(ui(text), style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold) }
@Composable fun Small(text: String) { Text(ui(text), style = MaterialTheme.typography.bodySmall, color = Ink.copy(alpha = .75f)) }
@Composable fun OnlineSwitch(store: BuddyStore) {
    Row(verticalAlignment=Alignment.CenterVertically) { Column(Modifier.weight(1f)) { Text(ui("Use online recommendations"),fontWeight=FontWeight.Bold); Small(if(store.online) "NVIDIA suggestions; live data when Google services are configured" else "Demo mode · sample data, no AI calls") }
        Switch(checked=store.online,onCheckedChange={store.online=it},enabled=!store.busy && store.health?.optBoolean("paidServicesAllowed")==true) }
    if(store.health?.optBoolean("paidServicesAllowed")!=true) Small("Paid services are disabled on the server. Demo mode is available.")
    if(store.online) Small("Online requests send this trip's preferences, dates and start/end points to NVIDIA / Google.")
}

fun modeLabel(mode:String) = when(mode) {"live"->"Online plan · check sources and warnings";"assisted"->"Nemotron suggestions · sample places, weather and routes";else->"Demo trip · sample data"}

@Composable fun WeatherPanel(weather:JSONObject?,enabled:Boolean,onRefresh:()->Unit) {
    Panel { Row(verticalAlignment=Alignment.CenterVertically){Text(ui("Sydney weather"),fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f));TextButton(onClick=onRefresh,enabled=enabled){Text(ui("Refresh weather"))}}
        if(weather==null) Small("Not loaded") else {
            val days=weather.array("days").objects()
            if(days.isEmpty()) Text(weather.optString("summary")) else days.forEach { Text("${it.optString("date")} · ${it.optString("summary")}",style=MaterialTheme.typography.bodySmall) }
            Small(weather.optString("source")); if(weather.has("fetchedAt")) Small("Checked ${weather.optString("fetchedAt")}")
        }
        Small("Refreshing weather keeps your route. Ask Koko when you want to change the plan.")
    }
}

@Composable fun DaySummary(day:JSONObject) {
    val context=LocalContext.current
    Panel {
        Text(day.optString("date"),fontWeight=FontWeight.Bold)
        Text("About ${day.optInt("estimatedSteps")} steps · A$${day.optDouble("estimatedCost")} · return ${day.optString("finishTime")}")
        Small("${day.optJSONObject("start")?.optString("name")} → ${day.optJSONObject("end")?.optString("name")}")
        day.array("stops").objects().forEachIndexed { i,s ->
            HorizontalDivider(color=Mint)
            Text("${i+1}. ${s.optString("name")}",fontWeight=FontWeight.Bold)
            Small("${s.optString("arrival")}–${s.optString("departure")} · ${if(s.optBoolean("indoor")) "Indoor" else "Outdoor"} · about A$${s.optDouble("cost")}")
            Small(s.optString("note")); if(!s.optBoolean("hoursVerified")) Small("Hours Pending Verification")
            TextButton(onClick={
                val uri=Uri.parse(s.optString("mapsURL"))
                if(uri.scheme=="https" && uri.host=="www.google.com") runCatching {context.startActivity(Intent(Intent.ACTION_VIEW,uri))}
            }) {Text("View in Google Maps")}
        }
        day.array("warnings").strings().forEach {Small(it)}
    }
}

@Composable fun Evidence(trip:JSONObject) {
    var expanded by remember {mutableStateOf(false)}
    trip.array("warnings").strings().forEach {Small("• $it")}
    TextButton(onClick={expanded=!expanded}){Text(if(expanded) "Hide planning notes" else "View planning basis & verification records")}
    if(expanded) Panel {trip.array("trace").objects().forEach {Text(it.optString("title"),fontWeight=FontWeight.Bold);Small(it.optString("detail"));Small(it.optString("source"))}}
}

@Composable fun ProposalScreen(store:BuddyStore) {
    val pending=store.proposal ?: return
    if(pending.trip.optString("mode")=="demo") {ScheduleEditorScreen(store);return}
    Page {
        Heading(if(pending.targetId==null) "Your route is ready" else "Review the changes")
        Small("Your trip is saved after you confirm.")
        Panel {Text(pending.trip.optString("explanation"));Small(modeLabel(pending.trip.optString("mode")))}
        if(pending.targetId!=null && store.trip!=null) {
            val before=store.trip!!.array("days").objects().flatMap {it.array("stops").objects()}.map{it.optString("name")}.toSet()
            val after=pending.trip.array("days").objects().flatMap{it.array("stops").objects()}.map{it.optString("name")}.toSet()
            Panel {Text("Adjust comparison",fontWeight=FontWeight.Bold);Text("Added: ${(after-before).joinToString(", ").ifBlank{"None"}}");Text("Removed: ${(before-after).joinToString(", ").ifBlank{"None"}}")
                Text("Estimated steps: ${store.trip!!.array("days").objects().sumOf{it.optInt("estimatedSteps")}} → ${pending.trip.array("days").objects().sumOf{it.optInt("estimatedSteps")}}") }
        }
        WeatherPanel(pending.trip.optJSONObject("weather"),false,{})
        pending.trip.array("days").objects().forEach {DaySummary(it)}
        Evidence(pending.trip)
        Action("Confirm and save",!store.busy,store::accept)
        TextButton(onClick=store::cancelProposal,enabled=!store.busy) {Text("Discard draft")}
    }
}

internal fun savedTripNavigationDay(trip:JSONObject,today:String=java.time.LocalDate.now(java.time.ZoneId.of("Australia/Sydney")).toString()):Int? {
    val days=trip.array("days").objects();val index=days.indexOfFirst{it.optString("date")==today}
    return if(index>=0)index else if(days.size==1)0 else null
}
@Composable internal fun SavedTripCard(t:JSONObject,enabled:Boolean=true,onEdit:()->Unit,onNavigate:()->Unit,onMap:()->Unit,onDelete:()->Unit,onRename:()->Unit={}) {
    Surface(onClick=onEdit,enabled=enabled,shape=RoundedCornerShape(24.dp),color=Color.White,modifier=Modifier.fillMaxWidth().testTag("edit-trip-${t.optString("id")}")) {
        Column(Modifier.padding(18.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
            Row(horizontalArrangement=Arrangement.spacedBy(14.dp),verticalAlignment=Alignment.CenterVertically){
                TripDateBadge(t)
                Column(Modifier.weight(1f)){Text(t.optString("title"),fontWeight=FontWeight.Bold);Text(ui("{0} · {1} days",t.getJSONObject("profile").optString("date"),t.array("days").length().toString()))}
            }
            val created=tripCreatedDate(t.optString("createdAt"))
            Box(Modifier.testTag("trip-created-${t.optString("id")}")){Small(if(created!=null)ui("Created {0}",created)else ui("Creation date unavailable"))}
            Small(modeLabel(t.optString("mode")))
            Row(horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                TextButton(onClick=onNavigate,enabled=enabled,modifier=Modifier.testTag("navigate-trip-${t.optString("id")}")){Icon(Icons.Outlined.Navigation,null,Modifier.size(18.dp));Text(ui("Start navigation"))}
                TextButton(onClick=onMap,enabled=enabled){Text(ui("View map"),fontWeight=FontWeight.Bold)}
            }
            Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically){
                TextButton(onClick=onRename,enabled=enabled,modifier=Modifier.testTag("rename-trip-${t.optString("id")}")){Icon(Icons.Outlined.Edit,null,Modifier.size(18.dp));Text(ui("Rename"))}
                Spacer(Modifier.weight(1f))
                TextButton(onClick=onDelete,enabled=enabled){Text(ui("Delete trip"))}
            }
        }
    }
}
@Composable internal fun TripNameDialog(trip:JSONObject,busy:Boolean,onDismiss:()->Unit,onSave:suspend(String)->Boolean) {
    var title by remember(trip.optString("id")){mutableStateOf(trip.optString("title"))}
    var problem by remember{mutableStateOf(false)}
    var saving by remember{mutableStateOf(false)}
    val scope=rememberCoroutineScope()
    AlertDialog(onDismissRequest={if(!busy&&!saving)onDismiss()},title={Text(ui("Name your trip"))},text={Column{
        OutlinedTextField(title,{title=it;problem=false},label={Text(ui("Trip name"))},singleLine=true,modifier=Modifier.testTag("trip-name-field"),enabled=!busy&&!saving)
        Small(ui("Up to 40 characters. Places and travel dates stay the same."))
        if(problem)Text(ui("Name was not saved. Please try again."),color=MaterialTheme.colorScheme.error)
    }},confirmButton={TextButton(onClick={scope.launch{saving=true;try{if(onSave(title)){onDismiss()}else{problem=true}}finally{saving=false}}},enabled=!busy&&!saving&&title.trim().isNotEmpty()&&title.trim().length<=40,modifier=Modifier.testTag("trip-name-save")){Text(ui("Save name"))}},dismissButton={TextButton(onClick=onDismiss,enabled=!busy&&!saving){Text(ui("Cancel"))}})
}
@Composable fun TripsScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    var search by rememberSaveable{mutableStateOf("")}
    var selectedDate by rememberSaveable{mutableStateOf<String?>(null)}
    var calendarExpanded by rememberSaveable{mutableStateOf(false)}
    var renaming by remember{mutableStateOf<JSONObject?>(null)}
    var choosing by remember{mutableStateOf<JSONObject?>(null)}
    var navigation by remember{mutableStateOf(false)}
    fun start(trip:JSONObject,index:Int){store.select(trip);store.dayIndex=index;choosing=null;navigation=true}
    Page {
        Small("Discover your next adventure. Keep every trip.")
        TextButton(onClick=store::reload,enabled=!store.busy){Text(ui("Refresh trips"))}
        Action("Try a 3-day timeline",!store.busy,store::tryScheduleDemo)
        OutlinedTextField(search,{search=it},placeholder={Text(ui("Search trips, places or dates"))},singleLine=true,leadingIcon={Icon(Icons.Outlined.Search,null)},modifier=Modifier.fillMaxWidth().testTag("trip-library-search"))
        Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically){
            TextButton(onClick={calendarExpanded=!calendarExpanded},modifier=Modifier.testTag("trip-library-calendar")){Icon(Icons.Outlined.CalendarMonth,null);Text(ui("Trip calendar"));Icon(if(calendarExpanded)Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,null)}
            Spacer(Modifier.weight(1f))
            if(selectedDate!=null||search.isNotEmpty())TextButton(onClick={selectedDate=null;search=""},modifier=Modifier.testTag("trip-library-reset")){Text(ui("Show all"))}
        }
        if(calendarExpanded){TripLibraryCalendar(selectedDate,store.saved.flatMap{tripLibraryDates(it)}.toSet()){selectedDate=if(selectedDate==it)null else it};Small(ui("Dots mark trip dates. Tap a date to filter."))}
        selectedDate?.let{Small(it)}
        val filtered=store.saved.filter{tripLibraryMatches(it,search,selectedDate)}
        Small("${filtered.size} / ${store.saved.size}")
        if(store.saved.isEmpty()) Small("No saved trips yet.")
        else if(filtered.isEmpty())Small(ui("No matching trips"))
        filtered.forEach { t -> SavedTripCard(t,!store.busy,onEdit={store.editSchedule(t)},onNavigate={val day=savedTripNavigationDay(t);if(day!=null)start(t,day)else choosing=t},onMap={store.select(t);store.tab=0},onDelete={onDelete(t)},onRename={renaming=t}) }
    }
    renaming?.let{trip->TripNameDialog(trip,store.busy,onDismiss={renaming=null},onSave={store.renameTrip(trip,it)})}
    choosing?.let{trip->AlertDialog(onDismissRequest={choosing=null},title={Text("Choose navigation date")},text={Column(Modifier.verticalScroll(rememberScrollState())){
        Small("Today is outside trip dates. Please select the trip to navigate.")
        trip.array("days").objects().forEachIndexed{i,day->TextButton(onClick={start(trip,i)}){Text("Day ${i+1} · ${day.optString("date")} · ${day.array("stops").length()} stops")}}
    }},confirmButton={TextButton(onClick={choosing=null}){Text(ui("Cancel"))}})}
    if(navigation) {
        if(store.trip?.optString("mode")=="live")AlertDialog(onDismissRequest={navigation=false},text={Text("This itinerary uses Google place data. Step-by-step walking navigation in-app only supports open-source itineraries. Use Google transit routing from the map view.")},confirmButton={TextButton(onClick={navigation=false}){Text("Close")}})
        else WalkingNavigationDialog(store,autoStart=true){navigation=false}
    }
}

@Composable fun AgentScreen(store:BuddyStore) {
    var message by rememberSaveable {mutableStateOf("")}
    Column(Modifier.fillMaxSize().padding(24.dp).imePadding(),verticalArrangement=Arrangement.spacedBy(18.dp)) {
        Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(15.dp)) {
            Buddy(70)
            Column(verticalArrangement=Arrangement.spacedBy(6.dp)) {
                Text(ui("Adjust your trip"),style=MaterialTheme.typography.titleLarge,fontWeight=FontWeight.Bold)
                Small(if(store.online)"Suggestions powered by NVIDIA Nemotron" else "Demo mode · local rules, no AI calls")
            }
        }
        Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(18.dp)) {
            Panel {Text(store.trip?.optString("explanation") ?: "Create and save a trip so Koko can help you adjust it.",style=MaterialTheme.typography.bodyMedium)}
            Row(horizontalArrangement=Arrangement.spacedBy(10.dp)) {
                OutlinedButton(onClick={store.generate("Less walking")},enabled=!store.busy && store.trip!=null,modifier=Modifier.weight(1f),shape=RoundedCornerShape(16.dp),colors=ButtonDefaults.outlinedButtonColors(containerColor=Mint),border=null) {
                    Icon(Icons.Outlined.DirectionsWalk,null,Modifier.size(19.dp));Spacer(Modifier.width(6.dp));Text(ui("Less walking"),fontWeight=FontWeight.Bold)
                }
                OutlinedButton(onClick={store.generate("It's raining",true)},enabled=!store.busy && store.trip!=null,modifier=Modifier.weight(1f),shape=RoundedCornerShape(16.dp),colors=ButtonDefaults.outlinedButtonColors(containerColor=Mint),border=null) {
                    Icon(Icons.Outlined.Umbrella,null,Modifier.size(19.dp));Spacer(Modifier.width(6.dp));Text(ui("It's raining"),fontWeight=FontWeight.Bold)
                }
            }
            Small("Changes create a draft first. Confirm it to replace your trip. The rain button tests a scenario and does not show live weather.")
        }
        OutlinedTextField(value=message,onValueChange={message=it.take(1500)},label={Text("For example: more art galleries, less walking")},modifier=Modifier.fillMaxWidth(),minLines=2,maxLines=4,enabled=!store.busy,shape=RoundedCornerShape(20.dp),colors=OutlinedTextFieldDefaults.colors(unfocusedContainerColor=Color.White,focusedContainerColor=Color.White,unfocusedBorderColor=Color.Transparent))
        Action("Adjust my trip",!store.busy && store.trip!=null && message.isNotBlank()){store.generate(message)}
    }
}

@Composable fun LedgerScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    var title by rememberSaveable {mutableStateOf("Lunch")}; var amount by rememberSaveable {mutableStateOf("")};var people by rememberSaveable {mutableIntStateOf(2)}
    Page {
        Small("Keep track of spending · local expense book · AUD")
        Panel {
            OutlinedTextField(value=title,onValueChange={title=it.take(100)},label={Text("Expense name")},modifier=Modifier.fillMaxWidth())
            OutlinedTextField(value=amount,onValueChange={amount=it.take(20)},label={Text("Amount (AUD)")},singleLine=true,modifier=Modifier.fillMaxWidth(),keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Decimal))
            Row(verticalAlignment=Alignment.CenterVertically){TextButton(onClick={people--},enabled=people>1){Text("−")};Text("People: $people");TextButton(onClick={people++},enabled=people<10){Text("＋")}}
            Action("Save expense"){store.addExpense(title,amount,people)}
        }
        Small("Any remaining cents are assigned in member order, so shares always add up to the original amount.")
        if(store.expenses.isEmpty()) Small("No expenses yet.")
        store.expenses.asReversed().forEach {e->Panel {
            Text("${e.optString("title")} · ${Money.display(e.getInt("cents"))}",fontWeight=FontWeight.Bold)
            Small("Split between ${e.getInt("people")} people: "+Money.shares(e.getInt("cents"),e.getInt("people")).joinToString(" / "){Money.display(it)})
            TextButton(onClick={onDelete(e)}){Text("Delete expense")}
        }}
    }
}

@Composable fun SettingsScreen(store:BuddyStore,onDelete:(JSONObject)->Unit) {
    var restaurantImport by remember{mutableStateOf(false)}
    if(restaurantImport)RestaurantImportDialog(store){restaurantImport=false}
    var socialImport by remember{mutableStateOf(false)}
    if(socialImport)SocialImportDialog(store,onDismiss={socialImport=false})
    var consent by rememberSaveable {mutableStateOf(false)}
    LaunchedEffect(Unit){store.reloadTravelData()}
    Page {
        Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(18.dp)){Buddy(65);Column(verticalArrangement=Arrangement.spacedBy(6.dp)){Text(ui("Hello, traveller"),style=MaterialTheme.typography.titleLarge,fontWeight=FontWeight.Bold);Small("Discover Sydney at your own pace")}}
        Panel {
            Text(ui("Language"),fontWeight=FontWeight.Bold)
            AppLanguage.available.forEach { language ->
                Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically){
                    RadioButton(selected=store.language==language,onClick={store.chooseLanguage(language)},modifier=Modifier.testTag("language-${language.code}"))
                    TextButton(onClick={store.chooseLanguage(language)}){Text(language.label)}
                }
            }
            Small("Koko speaks English.")
            Small("Place names and saved trip content keep their original language.")
        }
        Panel {
            var backendAccessCode by remember{mutableStateOf("")}
            var backendMode by remember{mutableStateOf(store.backendMode)}
            var backendAddress by remember{mutableStateOf(store.backendAddress(backendMode))}
            Text("Backend connection",fontWeight=FontWeight.Bold)
            listOf("local" to "Local backend","cloud" to "Cloud backend").forEach{(mode,label)->TextButton(onClick={backendMode=mode;backendAddress=store.backendAddress(mode)},modifier=Modifier.testTag("backend-$mode")){Text((if(backendMode==mode)"✓ "else "")+label)}}
            OutlinedTextField(value=backendAddress,onValueChange={backendAddress=it},label={Text("Backend URL")},singleLine=true,modifier=Modifier.fillMaxWidth().testTag("backend-address"))
            if(backendMode=="cloud")OutlinedTextField(value=backendAccessCode,onValueChange={backendAccessCode=it},label={Text("Cloud connection code (leave empty to keep saved value)")},visualTransformation=androidx.compose.ui.text.input.PasswordVisualTransformation(),singleLine=true,modifier=Modifier.fillMaxWidth().testTag("backend-access-code"))
            TextButton(onClick={store.chooseBackend(backendMode,backendAddress,backendAccessCode);backendAccessCode=""},enabled=!store.busy,modifier=Modifier.testTag("backend-apply")){Text("Apply and test connection")}
            Small("Currently: ${store.backendMode} · ${store.base}")
            Text(if(store.health?.optString("status")=="ok")"Selected backend connected" else "Selected backend not connected",modifier=Modifier.testTag("backend-status"))
            Small("Cloud: Please fill in HTTPS URL and connection code. Localhost defaults to 8787. Trips are saved separately on each end; switching will not move data.")
        }
        TextButton(onClick={restaurantImport=true}){Text(ui("Import menus and restaurant data"))}
        TextButton(onClick={socialImport=true},modifier=Modifier.testTag("social-import-open")){Icon(Icons.Outlined.FileDownload,null);Text(ui("Import travel inspiration"))}
        Panel {
            Text(ui("Travel data source"),fontWeight=FontWeight.Bold)
            listOf("direct" to "Direct sources","zetaris" to "Local Zetaris","zetaris-cloud" to "Cloud Zetaris (Sydney)").forEach{(route,label)->TextButton(onClick={store.chooseTravelDataSource(route)},enabled=store.health?.optString("managedTravelDataSource").isNullOrBlank(),modifier=Modifier.testTag("travel-data-$route")){Text((if(store.travelDataSource==route)"✓ "else "")+ui(label))}}
            if(!store.health?.optString("managedTravelDataSource").isNullOrBlank())Small("Data source is managed uniformly by the backend; no need to set Zetaris URL or account on phone.")
            Small(ui("Switch sources for places, weather and map settings. Map services still render maps and navigation."))
            Small(ui("Recheck connection; if source is managed by backend, switch backend to use other sources."))
            store.dataConnection?.let{Small(it)}
            TextButton(onClick={store.reloadTravelData()}){Text(ui("Check data connection"))}
        }
        Panel {
            Text(ui("Map provider"),fontWeight=FontWeight.Bold)
            MapProvider.entries.filter{store.mapLayerIds?.contains(it.name.lowercase()) ?: true}.forEach { provider ->
                Choice(provider.label,store.mapProvider==provider) {store.chooseMapProvider(provider)}
            }
            Small("Switching maps loads supported walking previews. OSM tiles require a connection; the offline overview does not request tiles.")
            if(store.mapProvider==MapProvider.GOOGLE && !BuildConfig.MAPS_CONFIGURED) Small("Google Maps has no API key. Choose OpenStreetMap.")
        }
        Panel {
            OnlineSwitch(store)
            Text(ui("Local planning service: {0}",ui(if(store.health?.optString("status")=="ok") "Connected" else "Disconnected")))
            Text("NVIDIA: "+ui(if(store.health?.optBoolean("nvidiaConfigured")==true) "API key configured" else "Not configured"))
            Text(ui("Google server: {0}",ui(if(store.health?.optBoolean("googleConfigured")==true) "API key configured" else "Not configured")))
            Text(ui("Android map: {0}",ui(if(BuildConfig.MAPS_CONFIGURED) "API key configured" else "Not configured; select for setup details")))
            Small("Configuration does not guarantee a successful request. Check the returned source and any error.")
            Action("Refresh connection",!store.busy,store::reload)
        }
        Panel {
            Text(ui("Defaults for your next trip"),fontWeight=FontWeight.Bold)
            Text(ui("Starting point"))
            listOf(Endpoint.quay,Endpoint.central).forEach{e->Choice(e.name,store.profile.start.sameLocation(e)){store.profile=store.profile.copy(start=e);store.rememberProfile()}}
            Text(ui("Return point"))
            listOf(Endpoint.quay,Endpoint.central).forEach{e->Choice(e.name,store.profile.end.sameLocation(e)){store.profile=store.profile.copy(end=e);store.rememberProfile()}}
            Text(ui("Preferred daily steps: {0}",store.profile.maxSteps.toString()))
            Slider(value=store.profile.maxSteps.coerceIn(1000,30000).toFloat(),onValueChange={store.profile=store.profile.copy(maxSteps=(it/1000).toInt()*1000);store.rememberProfile()},valueRange=1000f..30000f,steps=28)
            Small("Use Ask AI to adjust saved trips.")
        }
        Panel {
            Text(ui("Saved preferences"),fontWeight=FontWeight.Bold)
            Row(verticalAlignment=Alignment.CenterVertically){Checkbox(checked=consent,onCheckedChange={consent=it});Text(ui("Allow interests and step preferences to be saved on the local server"),Modifier.weight(1f))}
            Small("Retrieval consent can be withdrawn; local audit history remains. Location tracks, photos and private chats are not saved.")
            Action("Remember preferences",consent && !store.busy){store.saveMemory(consent)}
            store.memories.forEach{m->
                HorizontalDivider();Text(m.optString("summary"))
                TextButton(onClick={val p=m.getJSONObject("profile");store.profile=store.profile.copy(interests=p.array("interests").strings(),maxSteps=p.optInt("maxSteps",10000));store.rememberProfile();store.notice="This preference will be used for your next trip."}){Text(ui("Use for the next trip"))}
                TextButton(onClick={onDelete(m)},enabled=!store.busy){Text(ui("Withdraw preferences"))}
            }
        }
        Small("Sydney Buddy · Android MVP\nA local, single-user service. Group sync, payments, bookings and background GPS are not available.")
    }
}
