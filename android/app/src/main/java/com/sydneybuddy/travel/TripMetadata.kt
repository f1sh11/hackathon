package com.sydneybuddy.travel

import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import org.json.JSONObject

internal fun tripCreatedDate(raw:String,zone:ZoneId=ZoneId.systemDefault()):String? = runCatching {
    OffsetDateTime.parse(raw).atZoneSameInstant(zone).format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm",Locale.ROOT))
}.getOrNull()

internal fun tripRenamePayload(original:JSONObject,value:String):JSONObject {
    val title=value.trim()
    require(title.isNotEmpty()&&title.length<=40){"Trip names must contain 1–40 characters."}
    require(original.optString("id").isNotBlank())
    return JSONObject().put("trip",JSONObject().put("id",original.getString("id")).put("title",title))
        .put("expectedRevision",original.optInt("revision",0)).put("renameOnly",true)
}
