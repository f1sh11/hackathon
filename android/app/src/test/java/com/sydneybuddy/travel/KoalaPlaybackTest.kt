package com.sydneybuddy.travel

import org.junit.Assert.*
import org.junit.Test

class KoalaPlaybackTest {
    private fun player()=KoalaPlayback(listOf(
        KoalaClip("idle",12,36,"loop","loop","none",""),KoalaClip("clap",12,12,"once","idle","none",""),
        KoalaClip("map",12,36,"once","hold","reverse",""),KoalaClip("tired",12,36,"once","hold","hold","")
    ).associateBy{it.id})
    @Test fun repeatedEventDoesNotRestartGesture() {
        val p=player();p.request("clap","once");p.advance();repeat(5){p.advance()}
        p.request("clap","once");assertEquals(6,p.frame);repeat(10){p.advance()};assertEquals("idle",p.action)
    }
    @Test fun mapReversesFromCurrentFrameWhenThinkingEnds() {
        val p=player();p.request("map","thinking");p.advance();repeat(10){p.advance()}
        p.request("idle","reply");p.advance();assertEquals(10,p.frame);assertTrue(p.reversing)
        repeat(12){p.advance()};assertEquals("idle",p.action)
    }
    @Test fun heldLyingPoseHasNoFakeReturnToStanding() {
        val p=player();p.request("tired","tired");p.advance();repeat(40){p.advance()};p.request("idle","rested")
        repeat(40){p.advance()};assertEquals("tired",p.action);assertEquals(36,p.frame)
    }
}
