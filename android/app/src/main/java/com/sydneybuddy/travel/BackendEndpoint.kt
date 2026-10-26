package com.sydneybuddy.travel

object BackendEndpoint {
    fun validated(address:String):java.net.URI? {
        val uri=runCatching{java.net.URI(address.trim())}.getOrNull() ?: return null
        if(uri.host.isNullOrBlank() || uri.userInfo!=null || uri.query!=null || uri.fragment!=null)return null
        return uri.takeIf{it.scheme=="https" || it.scheme=="http" && it.host in listOf("127.0.0.1","localhost")}
    }
}
