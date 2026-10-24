package com.sydneybuddy.travel

import kotlin.math.*

/** Uniform dots follow each supplied road polyline; no connections are invented between sections. */
internal fun walkingRouteDots(points:List<Pair<Double,Double>>,spacingMeters:Double):List<Pair<Double,Double>> {
    if(points.size<2 || !spacingMeters.isFinite() || spacingMeters<=0)return emptyList()
    if(points.any{!it.first.isFinite() || !it.second.isFinite() || it.first !in -90.0..90.0 || it.second !in -180.0..180.0})return emptyList()
    fun distance(a:Pair<Double,Double>,b:Pair<Double,Double>):Double {
        val latA=Math.toRadians(a.first);val latB=Math.toRadians(b.first)
        val h=sin((latB-latA)/2).pow(2)+cos(latA)*cos(latB)*sin(Math.toRadians(b.second-a.second)/2).pow(2)
        return 6371008.8*2*asin(sqrt(h.coerceIn(0.0,1.0)))
    }
    val lengths=points.zipWithNext().map{(a,b)->distance(a,b)}
    val total=lengths.sum()
    if(total<0.01)return emptyList()
    // Bound work even for unusually long routes viewed at street-level zoom.
    val spacing=max(spacingMeters,total/7999)
    val dots=mutableListOf(points.first())
    var traveled=0.0;var next=spacing
    points.zipWithNext().forEachIndexed {index,(a,b)->
        val length=lengths[index]
        if(length>0)while(next<=traveled+length && dots.size<8000) {
            val t=((next-traveled)/length).coerceIn(0.0,1.0)
            dots.add(a.first+(b.first-a.first)*t to a.second+(b.second-a.second)*t)
            next+=spacing
        }
        traveled+=length
    }
    return dots
}

/** Geometry and mode are part of identity: equal vertex counts do not imply the same route. */
internal data class OsmRouteScene(val day:String,val overview:OverviewRoute?,val sections:List<JourneyMapSection>,val insights:String="")

/** Logical screen units keep the route legible across density and device sizes. */
internal data class WalkingDotStyle(val radius:Float,val halo:Float,val spacing:Double)
internal fun walkingDotStyle(shortSideDp:Float):WalkingDotStyle {
    val scale=(shortSideDp/390f).coerceIn(0.9f,1.15f)
    return WalkingDotStyle(2.2f*scale,0.65f*scale,10.0*scale)
}
