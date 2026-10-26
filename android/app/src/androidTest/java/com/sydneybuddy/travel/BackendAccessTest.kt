package com.sydneybuddy.travel
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
class BackendAccessTest {
    @Test fun accessCodeUsesKeystoreAndOnlyMatchesConfiguredHttpsHost(){
        val app=InstrumentationRegistry.getInstrumentation().targetContext
        val prefs=app.getSharedPreferences("buddy",0);val old=prefs.getString("cloudBackendURL",null)
        val base="https://backend-test.example.invalid";val code="synthetic-test-access-code-123456789"
        BackendAccess.initialize(app)
        try {
            BackendAccess.save(base,code);prefs.edit().putString("cloudBackendURL",base).commit()
            assertEquals(code,BackendAccess.token("$base/health"))
            assertNull(BackendAccess.token("https://third-party.example.invalid/health"))
            assertNull(BackendAccess.token("http://backend-test.example.invalid/health"))
            assertFalse(app.getSharedPreferences("backendAccess",0).getString(base,"")!!.contains(code))
        } finally {prefs.edit().apply{if(old==null)remove("cloudBackendURL")else putString("cloudBackendURL",old)}.commit();app.getSharedPreferences("backendAccess",0).edit().remove(base).commit()}
    }
}
