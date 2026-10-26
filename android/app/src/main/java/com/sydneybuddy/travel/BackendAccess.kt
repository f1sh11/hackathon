package com.sydneybuddy.travel

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import okhttp3.Interceptor
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

object BackendAccess {
    private var context:Context?=null
    fun initialize(value:Context){context=value.applicationContext}
    private fun key():SecretKey {
        val store=KeyStore.getInstance("AndroidKeyStore").apply{load(null)}
        (store.getKey("SydneyBuddy.backend",null) as? SecretKey)?.let{return it}
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore").apply{init(KeyGenParameterSpec.Builder("SydneyBuddy.backend",KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())}.generateKey()
    }
    fun save(base:String,code:String){
        require(BackendEndpoint.validated(base)?.scheme=="https" && code.length in 24..256 && code.none{it.isWhitespace()})
        val cipher=Cipher.getInstance("AES/GCM/NoPadding").apply{init(Cipher.ENCRYPT_MODE,key())}
        val encrypted=cipher.doFinal(code.toByteArray(Charsets.UTF_8))
        context!!.getSharedPreferences("backendAccess",0).edit().putString(base,Base64.encodeToString(cipher.iv+encrypted,Base64.NO_WRAP)).apply()
    }
    fun token(url:String):String? {
        val app=context ?: return null;val prefs=app.getSharedPreferences("buddy",0)
        val base=prefs.getString("cloudBackendURL",null) ?: return null
        val target=runCatching{java.net.URI(url)}.getOrNull() ?: return null;val expected=BackendEndpoint.validated(base) ?: return null
        if(target.scheme!="https" || target.host!=expected.host || target.port!=expected.port)return null
        return runCatching{val raw=Base64.decode(app.getSharedPreferences("backendAccess",0).getString(base,null) ?: return null,Base64.NO_WRAP);val cipher=Cipher.getInstance("AES/GCM/NoPadding").apply{init(Cipher.DECRYPT_MODE,key(),GCMParameterSpec(128,raw.copyOfRange(0,12)))};String(cipher.doFinal(raw.copyOfRange(12,raw.size)),Charsets.UTF_8)}.getOrNull()
    }
    val interceptor=Interceptor{chain->val original=chain.request();val token=token(original.url.toString());chain.proceed(if(token==null)original else original.newBuilder().header("Authorization","Bearer "+token).build())}
}
