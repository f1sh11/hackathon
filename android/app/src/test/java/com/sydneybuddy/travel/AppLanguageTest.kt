package com.sydneybuddy.travel

import java.io.File
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class AppLanguageTest {
    private fun catalog():JSONObject {
        val path=listOf(File("src/main/assets/ui-localizations.json"),File("app/src/main/assets/ui-localizations.json"))
            .firstOrNull{it.isFile} ?: error("UI localization asset is missing")
        return JSONObject(path.readText())
    }

    @Test fun freshAndPreviouslyChineseDevicesUseEnglish() {
        for(value in listOf(null,"","zh-Hans","zh-Hant","en","unknown")) {
            assertEquals(AppLanguage.ENGLISH,AppLanguage.fromStored(value))
        }
        assertEquals(listOf(AppLanguage.ENGLISH),AppLanguage.available)
    }

    @Test fun legacyKeysAndOptionsDisplayEnglishWithoutChangingUserText() {
        val catalog=catalog()
        assertEquals("Food",translated(catalog,AppLanguage.SIMPLIFIED,"美食"))
        assertEquals("New trip",translated(catalog,AppLanguage.TRADITIONAL,"新建"))
        val original="我的旅程：悉尼和家人"
        assertEquals(original,translated(catalog,AppLanguage.ENGLISH,original))
        assertEquals("Created $original",translated(catalog,AppLanguage.ENGLISH,"Created {0}",original))
    }

    @Test fun generatedEnglishCatalogContainsNoUntranslatedChineseCopy() {
        val catalog=catalog()
        val chinese=Regex("[\\u3400-\\u9fff]")
        catalog.keys().forEach { key ->
            val english=catalog.getJSONObject(key).getString("en")
            assertFalse("Untranslated English entry: $key",chinese.containsMatchIn(english))
        }
        assertEquals(" %.2f / person",catalog.getJSONObject(" %.2f / 人").getString("en"))
    }
}
