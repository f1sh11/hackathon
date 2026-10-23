package com.sydneybuddy.travel

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import androidx.compose.runtime.*
import kotlinx.coroutines.*
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.math.*

data class OverviewRoute(val points:List<Pair<Double,Double>>,val meters:Double,val seconds:Double,val legSeconds:List<Double>) {
    val segments get()=legSeconds.mapIndexed{i,seconds->"${if(i==0)"起" else "$i"}→${if(i==legSeconds.lastIndex)"终" else "${i+1}"} 步行 ${ceil(seconds/60).toInt()} 分"}.joinToString(" · ")
    companion object {
        fun parse(raw:String):OverviewRoute {
            val json=JSONObject(raw);require(json.optString("code")=="Ok")
            val route=json.getJSONArray("routes").getJSONObject(0)
            val points=decodePolyline(route.getString("geometry"),1e6)
            val meters=route.getDouble("distance");val seconds=route.getDouble("duration")
            require(points.size>1 && points.all{it.first in -90.0..90.0 && it.second in -180.0..180.0})
            require(meters.isFinite() && seconds.isFinite() && meters>=0 && seconds>=0)
            val legs=route.getJSONArray("legs").objects().map{it.getDouble("duration")}
            require(legs.isNotEmpty() && legs.all{it.isFinite() && it>=0})
            return OverviewRoute(points,meters,seconds,legs)
        }
    }
}
/** Preview has no location, speech, trip mutation or navigation-progress side effects. */
class ItineraryOverview {
    var route by mutableStateOf<OverviewRoute?>(null);private set
    var busy by mutableStateOf(false);private set
    var message by mutableStateOf<String?>(null);private set
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)
    private val client=OkHttpClient.Builder().callTimeout(25,TimeUnit.SECONDS).followRedirects(false).build()
    fun close(){scope.cancel()}
    fun load(day:JSONObject,base:String) {
        if(busy || route!=null)return
        busy=true;message=null
        scope.launch {
            try {
                val locations=JSONArray(walkingPoints(day).map{JSONObject().put("lat",it.lat).put("lon",it.lng)})
                val request=Request.Builder().url("$base/navigation/route").post(JSONObject().put("locations",locations).toString().toRequestBody("application/json".toMediaType())).build()
                val parsed=withContext(Dispatchers.IO) {
                    val call=client.newCall(request)
                    val response=suspendCancellableCoroutine<Response> { continuation ->
                        continuation.invokeOnCancellation {call.cancel()}
                        call.enqueue(object:Callback {
                            override fun onFailure(call:Call,e:java.io.IOException){if(!continuation.isCancelled)continuation.resumeWithException(e)}
                            override fun onResponse(call:Call,response:Response){continuation.resume(response){_,value,_->value.close()}}
                        })
                    }
                    response.use {require(it.isSuccessful);OverviewRoute.parse(it.body.string())}
                }
                ensureActive();route=parsed;busy=false
            } catch(e:CancellationException){throw e}
            catch(e:Exception){busy=false;message="暂时无法取得步行路线，仅显示站点；请重试。"}
        }
    }
}
fun itineraryBadge(text:String,direction:Boolean=false):Bitmap {
    val size=if(direction || text=="●")48 else 84
    val bitmap=Bitmap.createBitmap(size,size,Bitmap.Config.ARGB_8888)
    val canvas=Canvas(bitmap);val paint=Paint(Paint.ANTI_ALIAS_FLAG)
    paint.color=android.graphics.Color.WHITE;canvas.drawCircle(size/2f,size/2f,size/2f,paint)
    val start=text.contains("起")
    val end=text=="终"
    paint.color=when {start->android.graphics.Color.rgb(25,118,210);end->android.graphics.Color.rgb(215,48,48);direction->android.graphics.Color.rgb(13,130,94);else->android.graphics.Color.DKGRAY}
    canvas.drawCircle(size/2f,size/2f,size/2f-4,paint)
    if(!start && !end && !direction){paint.color=android.graphics.Color.WHITE;canvas.drawCircle(size/2f,size/2f,size/2f-8,paint)}
    paint.color=if(start || end || direction)android.graphics.Color.WHITE else android.graphics.Color.DKGRAY;paint.textAlign=Paint.Align.CENTER;paint.isFakeBoldText=true;paint.textSize=if(direction)32f else if(text.length>2)20f else 30f
    canvas.drawText(if(text=="●")"" else text,size/2f,size/2f-(paint.ascent()+paint.descent())/2,paint)
    return bitmap
}
fun itineraryBadges(points:List<Pair<Double,Double>>):List<String> = points.indices.map {i->
    when(i){0->if(points.first()==points.last())"起/终" else "起";points.lastIndex->"终";else->"$i"}
}
fun itineraryDirection(a:Pair<Double,Double>,b:Pair<Double,Double>):String {
    val angle=atan2((b.second-a.second)*cos(a.first*PI/180),b.first-a.first)
    return listOf("↑","↗","→","↘","↓","↙","←","↖")[(angle/(PI/4)).roundToInt().mod(8)]
}

/** Nearby attractions share a badge (e.g. 3/4) rather than obscuring each other at any zoom. */
fun itineraryPinGroups(points:List<Pair<Double,Double>>):Map<Int,List<Int>> {
    val groups=linkedMapOf<Int,List<Int>>()
    if(points.isEmpty())return groups
    groups[0]=listOf(0)
    val consumed=mutableSetOf<Int>()
    for(i in 1 until points.lastIndex) {
        if(i in consumed)continue
        val group=(i until points.lastIndex).filter{j->
            val dy=(points[i].first-points[j].first)*111320
            val dx=(points[i].second-points[j].second)*111320*cos(points[i].first*PI/180)
            j !in consumed && hypot(dx,dy)<20
        }
        consumed.addAll(group);groups[i]=group
    }
    if(points.size>1 && points.first()!=points.last())groups[points.lastIndex]=listOf(points.lastIndex)
    return groups
}
