package com.sydneybuddy.travel

import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.math.RoundingMode
import java.time.LocalDate
import java.time.ZoneId

fun JSONArray.objects(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }
fun JSONArray.strings(): List<String> = (0 until length()).map { getString(it) }
fun JSONObject.array(key: String): JSONArray = optJSONArray(key) ?: JSONArray()
fun JSONObject.copy(): JSONObject = JSONObject(toString())

data class Endpoint(val name: String, val lat: Double, val lng: Double) {
    fun json() = JSONObject().put("name", name).put("lat", lat).put("lng", lng)
    companion object {
        val quay = Endpoint("环形码头", -33.8611, 151.2126)
        val central = Endpoint("中央车站", -33.8830, 151.2065)
        fun from(json: JSONObject) = Endpoint(json.getString("name"), json.getDouble("lat"), json.getDouble("lng"))
    }
}

data class Profile(
    val date: String = LocalDate.now(ZoneId.of("Australia/Sydney")).toString(),
    val days: Int = 1,
    val budget: Int = 100,
    val maxSteps: Int = 10000,
    val startMinute: Int = 600,
    val endMinute: Int = 1080,
    val interests: List<String> = listOf("风景", "美食"),
    val start: Endpoint = Endpoint.quay,
    val end: Endpoint = Endpoint.quay
) {
    fun json() = JSONObject().put("date", date).put("days", days).put("budget", budget)
        .put("maxSteps", maxSteps).put("startMinute", startMinute).put("endMinute", endMinute)
        .put("interests", JSONArray(interests)).put("mustVisit", JSONArray()).put("start", start.json()).put("end", end.json())
    fun valid(): Boolean = runCatching { LocalDate.parse(date) }.isSuccess && days in 1..3 && budget in 0..100000 &&
        maxSteps in 500..60000 && startMinute in 0..1439 && endMinute in (startMinute + 1)..1440
    companion object {
        fun from(j: JSONObject) = Profile(j.getString("date"), j.getInt("days"), j.getDouble("budget").toInt(),
            j.getInt("maxSteps"), j.getInt("startMinute"), j.getInt("endMinute"), j.array("interests").strings(),
            Endpoint.from(j.getJSONObject("start")), Endpoint.from(j.getJSONObject("end")))
    }
}

object Money {
    fun cents(raw: String): Int {
        val value = raw.trim().replace(',', '.').toBigDecimalOrNull() ?: error("请输入有效金额")
        require(value > BigDecimal.ZERO && value < BigDecimal("100000")) { "金额须大于 0 且小于 100,000" }
        val result = value.movePointRight(2).setScale(0, RoundingMode.HALF_UP).intValueExact()
        require(result > 0) { "金额至少为 A$0.01" }
        return result
    }
    fun shares(cents: Int, people: Int): List<Int> {
        require(cents > 0 && people in 1..10)
        return List(people) { cents / people + if (it < cents % people) 1 else 0 }
    }
    fun display(cents: Int) = "A$" + BigDecimal(cents).movePointLeft(2).setScale(2).toPlainString()
}

data class Proposal(val trip: JSONObject, val targetId: String?, val expectedRevision: Int)

fun savePayload(proposal: Proposal): JSONObject {
    val trip = proposal.trip.copy().apply {
        remove("id"); remove("revision")
        proposal.targetId?.let { put("id", it) }
    }
    return JSONObject().put("trip", trip).put("expectedRevision", proposal.expectedRevision)
}

fun decodePolyline(encoded: String, scale: Double = 1e5): List<Pair<Double, Double>> {
    var index = 0; var lat = 0; var lng = 0
    fun next(): Int {
        var result = 0; var shift = 0
        while (true) {
            require(index < encoded.length && shift <= 30) { "Invalid route polyline" }
            val b = encoded[index++].code - 63
            require(b in 0..63)
            result = result or ((b and 31) shl shift); shift += 5
            if (b < 32) break
        }
        return if (result and 1 != 0) (result shr 1).inv() else result shr 1
    }
    return buildList {
        while (index < encoded.length) { lat += next(); lng += next(); add(lat / scale to lng / scale) }
    }
}
