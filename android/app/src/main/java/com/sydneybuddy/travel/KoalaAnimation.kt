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

internal enum class KoalaEvent(val event:String,val action:String) {
    Welcome("app.welcome","wave"),ChatReady("chat.ready","idle"),DnaCompleted("profile.confirmed","clap"),PreferenceConfirmed("user.confirmed","thumbs-up"),
    PlanningStarted("plan.started","map"),OptimizationStarted("optimization.started","map"),SimulationStarted("simulation.started","departure"),
    WeatherRisk("risk.weather","weather"),BudgetRisk("risk.budget","budget"),FatigueRisk("risk.energy","tired"),TripSaved("trip.saved","celebrate")
}

internal data class KoalaClip(val id:String,val fps:Int,val frames:Int,val playback:String,val ending:String,val recovery:String,val framePath:String)

/** Finish gestures and reverse props; missing physical recoveries hold their last frame. */
internal class KoalaPlayback(private val clips:Map<String,KoalaClip>) {
    var action="idle";private set
    var frame=1;private set
    var reversing=false;private set
    private var pending:String?=null
    private var lastToken=""
    val clip:KoalaClip? get()=clips[action]
    fun request(action:String,token:String) {
        if(token==lastToken || action !in clips)return
        lastToken=token
        if(action=="idle" && pending!=null && pending!="idle" && pending!="map")return
        if(action==this.action && pending==null)return
        pending=action
    }
    fun advance() {
        val current=clip ?: return
        if(action=="idle" && pending!=null){action=pending!!;pending=null;frame=1;reversing=false;return}
        if(current.recovery=="reverse" && pending!=null && pending!=action)reversing=true
        when {
            reversing && frame>1 -> frame--
            reversing -> {action="idle";frame=1;reversing=false}
            frame<current.frames -> frame++
            current.playback=="loop" -> frame=1
            current.ending=="idle" -> {action="idle";frame=1}
        }
    }
}

private class KoalaAssets(private val context:Context) {
    val clips:Map<String,KoalaClip>
    private val cache=object:LruCache<String,Bitmap>(12*1024*1024){override fun sizeOf(key:String,value:Bitmap)=value.byteCount}
    init {
        val json=context.assets.open("koala/manifest.json").bufferedReader().use{JSONObject(it.readText())}
        clips=json.getJSONArray("animations").let{items->(0 until items.length()).associate { i->
            val a=items.getJSONObject(i);val clip=KoalaClip(a.getString("id"),a.getInt("fps"),a.getInt("frames"),a.getString("playback"),a.getString("ending"),a.getString("recovery"),a.getString("framePath"));clip.id to clip
        }}
    }
    fun load(action:String,frame:Int):Bitmap? {
        val clip=clips[action] ?: return null
        val path="koala/"+clip.framePath.replace("{frame:03}",frame.toString().padStart(3,'0'))
        return cache.get(path) ?: context.assets.open(path).use { BitmapFactory.decodeStream(it) }?.also{cache.put(path,it)}
    }
}

@Composable internal fun KoalaAvatar(size:Int,action:String="idle",eventToken:String=action) {
    val context=LocalContext.current
    val owner=LocalLifecycleOwner.current
    val assets=remember(context){KoalaAssets(context)}
    val playback=remember(assets){KoalaPlayback(assets.clips)}
    var image by remember(assets){mutableStateOf(assets.load("idle",1))}
    val reduceMotion=Settings.Global.getFloat(context.contentResolver,Settings.Global.ANIMATOR_DURATION_SCALE,1f)==0f
    LaunchedEffect(action,eventToken){playback.request(action,eventToken)}
    LaunchedEffect(assets,owner,reduceMotion) {
        if(!reduceMotion)owner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while(isActive){playback.advance();image=assets.load(playback.action,playback.frame);delay(1000L/(playback.clip?.fps ?: 12))}
        }
    }
    image?.let{Image(it.asImageBitmap(),contentDescription="旅行伙伴 Buddy",modifier=Modifier.size(size.dp,(size*1.15f).dp))}
}
