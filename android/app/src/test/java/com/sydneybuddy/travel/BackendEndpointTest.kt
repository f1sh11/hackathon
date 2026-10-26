package com.sydneybuddy.travel
import org.junit.Assert.*
import org.junit.Test
class BackendEndpointTest {
    @Test fun onlyHttpsOrPrivateLoopbackAreAccepted(){
        assertNull(BackendEndpoint.validated("http://203.0.113.1:8787"))
        assertNull(BackendEndpoint.validated("https://user:secret@example.com"))
        assertNull(BackendEndpoint.validated("https://example.com?token=secret"))
        assertEquals(8790,BackendEndpoint.validated("http://127.0.0.1:8790")?.port)
        assertNotNull(BackendEndpoint.validated("https://api.example.com"))
    }
}
