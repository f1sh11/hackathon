package com.sydneybuddy.travel

import androidx.compose.runtime.*
import androidx.compose.ui.platform.LocalContext
import org.json.JSONObject

enum class AppLanguage(val code:String,val label:String) {
    TRADITIONAL("zh-Hant","繁體中文"),SIMPLIFIED("zh-Hans","简体中文"),ENGLISH("en","English");
    companion object {fun fromStored(code:String?)=entries.firstOrNull{it.code==code} ?: TRADITIONAL}
}
// Standalone previews/tests keep their source language; BuddyApp supplies the saved preference.
val LocalAppLanguage=staticCompositionLocalOf<AppLanguage?>{null}
internal fun translated(catalog:JSONObject,language:AppLanguage,key:String,vararg args:String):String {
    var result=catalog.optJSONObject(key)?.optString(language.code,key) ?: key
    args.forEachIndexed{i,value->result=result.replace("{$i}",value)}
    return result
}
internal fun poiText(point:JSONObject,field:String,language:AppLanguage?,catalog:JSONObject):String {
    val preferred=if(language==AppLanguage.ENGLISH)point.optString(field+"En").takeIf{it.isNotBlank()} else null
    val original=preferred ?: point.optString(field)
    return if(language==null)original else translated(catalog,language,original)
}
@Composable fun ui(key:String,vararg args:String):String {
    val context=LocalContext.current
    val catalog=remember(context.applicationContext){JSONObject(context.assets.open("ui-localizations.json").bufferedReader().use{it.readText()})}
    val language=LocalAppLanguage.current
    if(language==null){var result=key;args.forEachIndexed{i,value->result=result.replace("{$i}",value)};return result}
    return translated(catalog,language,key,*args)
}
