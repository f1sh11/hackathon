package com.sydneybuddy.travel

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Test

class PoiLanguageTest {
    @Test fun sourceEnglishFieldsChangePresentationWithoutMutatingIdentity() {
        val point=JSONObject().put("id","osm:node:1").put("name","博物館").put("nameEn","Museum").put("description","中文介紹").put("descriptionEn","Harbour collection")
        val catalog=JSONObject()
        assertEquals("Museum",poiText(point,"name",AppLanguage.ENGLISH,catalog))
        assertEquals("Harbour collection",poiText(point,"description",AppLanguage.ENGLISH,catalog))
        assertEquals("博物館",poiText(point,"name",AppLanguage.TRADITIONAL,catalog))
        assertEquals("博物館",point.getString("name"))
        assertEquals("osm:node:1",point.getString("id"))
    }
    @Test fun olderSavedPlacesUseCatalogAndUnknownBusinessNamesStayIntact() {
        val catalog=JSONObject().put("悉尼歌剧院",JSONObject().put("en","Sydney Opera House"))
        assertEquals("Sydney Opera House",poiText(JSONObject().put("name","悉尼歌剧院"),"name",AppLanguage.ENGLISH,catalog))
        assertEquals("My café",poiText(JSONObject().put("name","My café"),"name",AppLanguage.ENGLISH,catalog))
    }
}
