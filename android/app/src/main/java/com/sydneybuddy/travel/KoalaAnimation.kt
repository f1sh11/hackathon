package com.sydneybuddy.travel

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.provider.Settings
import android.util.LruCache
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import org.json.JSONObject

internal enum class KoalaEvent(val event:String,val action:String,val returnsToIdle:Boolean=false) {
    Welcome("app.welcome","wave"),ChatReady("chat.ready","idle"),DnaCompleted("profile.confirmed","celebrate"),PreferenceConfirmed("user.confirmed","thumbs-up"),
    InformationSubmitted("information.submitted","clap"),StepCompleted("step.completed","thumbs-up"),OptimizationProgress("optimization.progress","thumbs-up"),
    PlanningStarted("plan.started","map"),OptimizationStarted("optimization.started","map"),JourneyStarted("journey.started","departure"),SimulationStarted("simulation.started","departure"),
    WeatherRisk("risk.weather","weather"),BudgetRisk("risk.budget","budget"),FatigueRisk("risk.energy","tired"),TripSaved("trip.saved","celebrate"),
    ChoiceAccepted("conversation.choice","thumbs-up",true),RainConversation("conversation.rain","weather",true),PlayfulTired("conversation.long-walk","tired",true)
}

/** Presentation-only reactions from explicit current text, never inferred profile or engine risk. */
internal object KoalaConversationFeedback {
    fun event(text:String,scene:String=""):KoalaEvent? {
        val normalized=java.text.Normalizer.normalize(text,java.text.Normalizer.Form.NFKC).replace(",","").replace("，","").replace(" ","")
            .replace("一万五千","15000").replace("一万五","15000")
        val walkingScene=scene.contains("步") || scene.contains("步行") || scene.contains("走多少")
        val quantities=Regex("([0-9]+)步").findAll(normalized).map{it.groupValues[1]}.toList()
        val longWalk=quantities.lastOrNull()=="15000" || walkingScene && normalized=="15000"
        val negatedWalk=Regex("(?:不是|不走|不想走|不打算走|不愿走|不要超过|不要|不超过|不到|少于|不满|别走|取消|不选)15000").containsMatchIn(normalized)
        if(longWalk && !negatedWalk)return KoalaEvent.PlayfulTired
        val rainScene=scene.contains("雨") || scene.contains("伞") || normalized.contains("雨") || normalized.contains("伞")
        val declines=listOf("不要","不想","不去","不行","不愿","不可以","不喜欢","不用","不需要","先不","不打算","换","改","等","躲","避","室内","晴天","看天气","再定","再决定")
        val accepts=listOf("带伞","带上伞","撑伞","没关系","不怕","可以","愿意","都行","照样","照常","也能","喜欢","走吧","去吧")
        return if(rainScene && declines.none{normalized.contains(it)} && accepts.any{normalized.contains(it)})KoalaEvent.RainConversation else null
    }
}

internal data class KoalaClip(val id:String,val fps:Int,val frames:Int,val playback:String,val ending:String,val recovery:String,val framePath:String,val recoveryId:String?=null)
internal data class KoalaRecoveryClip(val id:String,val sourceAction:String,val fps:Int,val frames:Int,val framePath:String,val quality:String)

/** Finish complete gestures and supplied return cels; unavailable physical recoveries hold. */
internal class KoalaPlayback(private val clips:Map<String,KoalaClip>,private val recoveries:Map<String,KoalaRecoveryClip> = emptyMap()) {
    var action="idle";private set
    var frame=1;private set
    var reversing=false;private set
    var recovering=false;private set
    private var pending:String?=null
    private var pendingReturnToIdle=false
    private var releaseQueuedWork=false
    private var returnAfterPlayback=false
    private var lastToken=""
    val clip:KoalaClip? get()=clips[action]
    val recoveryClip:KoalaRecoveryClip? get()=clip?.recoveryId?.let{recoveries[it]}?.takeIf{it.sourceAction==action && it.quality=="source_frame_reversal"}
    val framePath:String? get()=(if(recovering)recoveryClip?.framePath else clip?.framePath)?.replace("{frame:03}",frame.toString().padStart(3,'0'))
    val fps:Int get()=if(recovering)recoveryClip?.fps ?: 12 else clip?.fps ?: 12
    fun request(action:String,token:String,returnToIdle:Boolean=false) {
        if(token==lastToken || action !in clips)return
        lastToken=token
        val queued=pending?.let{clips[it]}
        if(action=="idle" && this.action=="idle" && queued?.ending=="hold" && (queued.recovery=="reverse" || queued.recoveryId?.let{recoveries[it]}?.quality=="source_frame_reversal")){releaseQueuedWork=true;return}
        if(action=="idle" && pending!=null && pending!="idle" && pending!="map")return
        if(action==this.action && !recovering && !reversing){returnAfterPlayback=returnToIdle;pending=null;pendingReturnToIdle=false;releaseQueuedWork=false;return}
        pending=action;pendingReturnToIdle=returnToIdle;releaseQueuedWork=false
    }
    private fun finishAtIdle(){action="idle";frame=1;reversing=false;recovering=false;returnAfterPlayback=false;if(pending=="idle")pending=null}
    fun advance() {
        val current=clip ?: return
        if(action=="idle" && pending!=null){action=pending!!;pending=if(releaseQueuedWork)"idle"else null;releaseQueuedWork=false;frame=1;reversing=false;recovering=false;returnAfterPlayback=pendingReturnToIdle;return}
        if(recovering){if(frame<(recoveryClip?.frames ?: 0))frame++ else finishAtIdle();return}
        val nextRequested=pending!=null && pending!=action
        if(current.recovery=="reverse" && nextRequested && frame<current.frames)reversing=true
        when {
            reversing && frame>1 -> frame--
            reversing -> finishAtIdle()
            frame<current.frames -> frame++
            current.playback=="loop" -> frame=1
            current.ending=="idle" -> finishAtIdle()
            nextRequested || returnAfterPlayback -> {
                if(recoveryClip!=null){recovering=true;frame=1}
                else if(current.recovery=="reverse"){reversing=true;if(frame>1)frame--}
            }
        }
    }
}

private class KoalaAssets(private val context:Context) {
    val clips:Map<String,KoalaClip>
    val recoveries:Map<String,KoalaRecoveryClip>
    private val cache=object:LruCache<String,Bitmap>(12*1024*1024){override fun sizeOf(key:String,value:Bitmap)=value.byteCount}
    init {
        val json=context.assets.open("koala/manifest.json").bufferedReader().use{JSONObject(it.readText())}
        clips=json.getJSONArray("animations").let{items->(0 until items.length()).associate { i->
            val a=items.getJSONObject(i);val clip=KoalaClip(a.getString("id"),a.getInt("fps"),a.getInt("frames"),a.getString("playback"),a.getString("ending"),a.getString("recovery"),a.getString("framePath"),a.optString("recoveryId").takeIf{it.isNotBlank()});clip.id to clip
        }}
        recoveries=json.optJSONArray("recoveries")?.let{items->(0 until items.length()).associate {i->
            val r=items.getJSONObject(i);val clip=KoalaRecoveryClip(r.getString("id"),r.getString("sourceAction"),r.getInt("fps"),r.getInt("frames"),r.getString("framePath"),r.getString("quality"));clip.id to clip
        }} ?: emptyMap()
    }
    fun load(relativePath:String?):Bitmap? {
        val path="koala/"+(relativePath ?: return null)
        return cache.get(path) ?: context.assets.open(path).use { BitmapFactory.decodeStream(it) }?.also{cache.put(path,it)}
    }
}

@Composable internal fun KoalaAvatar(size:Int,action:String="idle",eventToken:String=action,returnToIdle:Boolean=false) {
    val context=LocalContext.current
    val owner=LocalLifecycleOwner.current
    val assets=remember(context){KoalaAssets(context)}
    val playback=remember(assets){KoalaPlayback(assets.clips,assets.recoveries)}
    var image by remember(assets){mutableStateOf(assets.load("animations/idle/frames/001.png"))}
    val reduceMotion=Settings.Global.getFloat(context.contentResolver,Settings.Global.ANIMATOR_DURATION_SCALE,1f)==0f
    LaunchedEffect(action,eventToken,returnToIdle){playback.request(action,eventToken,returnToIdle)}
    LaunchedEffect(assets,owner,reduceMotion) {
        if(!reduceMotion)owner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while(isActive){playback.advance();image=assets.load(playback.framePath);delay(1000L/playback.fps)}
        }
    }
    image?.let{Image(it.asImageBitmap(),contentDescription="旅行伙伴 Koko",modifier=Modifier.size(size.dp,(size*1.15f).dp))}
}
