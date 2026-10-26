package com.sydneybuddy.travel

import androidx.compose.runtime.*
import androidx.compose.ui.platform.LocalContext
import org.json.JSONObject

// Keep legacy codes readable without allowing old device settings to change product copy.
enum class AppLanguage(val code:String,val label:String) {
    TRADITIONAL("zh-Hant","Traditional Chinese"),SIMPLIFIED("zh-Hans","Simplified Chinese"),ENGLISH("en","English");
    companion object {
        val available = listOf(ENGLISH)
        fun fromStored(@Suppress("UNUSED_PARAMETER") code:String?)=ENGLISH
    }
}
val LocalAppLanguage=staticCompositionLocalOf{AppLanguage.ENGLISH}
internal fun translated(catalog:JSONObject,language:AppLanguage,key:String,vararg args:String):String {
    var result=catalog.optJSONObject(key)?.optString(AppLanguage.fromStored(language.code).code,key) ?: key
    args.forEachIndexed{i,value->result=result.replace("{$i}",value)}
    return result
}
@Composable fun ui(key:String,vararg args:String):String {
    val context=LocalContext.current
    val catalog=remember(context.applicationContext){JSONObject(context.assets.open("ui-localizations.json").bufferedReader().use{it.readText()})}
    return translated(catalog,LocalAppLanguage.current,key,*args)
}
