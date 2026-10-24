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
    private fun returningPlayer():KoalaPlayback {
        val actions=listOf(
            KoalaClip("idle",12,36,"loop","loop","none","idle/{frame:03}"),
            KoalaClip("clap",12,12,"once","idle","none","clap/{frame:03}"),
            KoalaClip("weather",12,30,"once","hold","reverse","weather/{frame:03}","weather-to-idle"),
            KoalaClip("tired",12,36,"once","hold","hold","tired/{frame:03}","tired-to-idle"),
            KoalaClip("departure",12,36,"once","hold","hold","departure/{frame:03}")
        ).associateBy{it.id}
        val returns=listOf("weather","tired").map{KoalaRecoveryClip(it+"-to-idle",it,12,6,"recoveries/"+it+"/{frame:03}","source_frame_reversal")}.associateBy{it.id}
        return KoalaPlayback(actions,returns)
    }
    @Test fun wholeProfileAndTripCelebrateButInformationSubmissionClaps() {
        assertEquals("wave",KoalaEvent.Welcome.action);assertEquals("clap",KoalaEvent.InformationSubmitted.action)
        listOf(KoalaEvent.ChoiceAccepted,KoalaEvent.StepCompleted,KoalaEvent.OptimizationProgress,KoalaEvent.PreferenceConfirmed).forEach{assertEquals("thumbs-up",it.action)}
        listOf(KoalaEvent.DnaCompleted,KoalaEvent.TripSaved).forEach{assertEquals("celebrate",it.action)}
        listOf(KoalaEvent.JourneyStarted,KoalaEvent.SimulationStarted).forEach{assertEquals("departure",it.action)}
        assertEquals("map",KoalaEvent.PlanningStarted.action);assertEquals("map",KoalaEvent.OptimizationStarted.action)
    }
    @Test fun conservativeConversationFeedbackDoesNotClaimActualFatigueOrWeatherRisk() {
        assertEquals(KoalaEvent.PlayfulTired,KoalaConversationFeedback.event("15,000 步 · 多探索"))
        assertEquals(KoalaEvent.PlayfulTired,KoalaConversationFeedback.event("15000","这趟走多少步？"))
        assertEquals(KoalaEvent.PlayfulTired,KoalaConversationFeedback.event("一万五千步也可以"))
        assertNull(KoalaConversationFeedback.event("预算15000澳元","想走多少步？"))
        assertNull(KoalaConversationFeedback.event("不是15000步，改成10000步"))
        assertNull(KoalaConversationFeedback.event("不想走15000步"))
        assertNull(KoalaConversationFeedback.event("我平时喜欢宅家"))
        assertEquals(KoalaEvent.RainConversation,KoalaConversationFeedback.event("走，带上伞","这趟要是下点小雨，撑伞逛逛？"))
        assertEquals(KoalaEvent.RainConversation,KoalaConversationFeedback.event("下雨也没关系"))
        assertNull(KoalaConversationFeedback.event("换个室内活动","下雨也出去？"))
        assertNull(KoalaConversationFeedback.event("我不喜欢下雨"))
        assertNull(KoalaConversationFeedback.event("看天气再决定","这趟下雨怎么办？"))
        assertTrue(KoalaEvent.PlayfulTired.returnsToIdle);assertTrue(KoalaEvent.RainConversation.returnsToIdle)
        assertFalse(KoalaEvent.FatigueRisk.returnsToIdle);assertFalse(KoalaEvent.WeatherRisk.returnsToIdle)
        assertNotEquals(KoalaEvent.PlayfulTired.event,KoalaEvent.FatigueRisk.event)
    }
    @Test fun temporaryReactionsPlayCompleteReturnsThenIdle() {
        listOf("tired","weather").forEach{action->
            val p=returningPlayer();p.request(action,action,true);p.advance();var sawRecovery=false
            repeat(100){if(p.recovering){sawRecovery=true;assertTrue(p.framePath!!.startsWith("recoveries/"))};p.advance()}
            assertTrue(sawRecovery);assertEquals("idle",p.action);assertFalse(p.recovering)
        }
    }
    @Test fun realRiskStillHoldsUntilAcknowledged() {
        val p=returningPlayer();p.request("tired","risk");p.advance();repeat(100){p.advance()}
        assertEquals("tired",p.action);assertEquals(36,p.frame);assertFalse(p.recovering)
        p.request("idle","acknowledged");p.advance();assertTrue(p.recovering);assertEquals(1,p.frame)
        repeat(6){p.advance()};assertEquals("idle",p.action)
    }
    @Test fun queuedChoiceWaitsForReturnAndOrdinaryIdleDoesNotDiscardIt() {
        val p=returningPlayer();p.request("tired","joke",true);p.advance();repeat(36){p.advance()};assertTrue(p.recovering)
        p.request("clap","choice");p.request("idle","ordinary-chat");repeat(6){p.advance()};assertEquals("idle",p.action)
        p.advance();assertEquals("clap",p.action);assertEquals(1,p.frame)
    }
    @Test fun suitcaseNeedsPhysicalReturnRatherThanDissolve() {
        val p=returningPlayer();p.request("departure","journey",true);p.advance();repeat(40){p.advance()};p.request("clap","later")
        repeat(100){p.advance()};assertEquals("departure",p.action);assertEquals(36,p.frame);assertFalse(p.recovering)
    }
    @Test fun newerRealRiskRevokesSameActionPlayfulReturn() {
        listOf("tired","weather").forEach{action->
            val p=returningPlayer();p.request(action,"playful",true);p.advance();repeat(5){p.advance()}
            p.request(action,"actual-risk",false);repeat(120){p.advance()}
            assertEquals(action,p.action);assertEquals(p.clip!!.frames,p.frame);assertFalse(p.recovering)
        }
    }
    @Test fun realRiskArrivingDuringReturnReplaysTheRiskAndHolds() {
        val p=returningPlayer();p.request("tired","joke",true);p.advance();repeat(36){p.advance()};assertTrue(p.recovering)
        p.request("tired","actual-risk",false);repeat(120){p.advance()}
        assertEquals("tired",p.action);assertEquals(36,p.frame);assertFalse(p.recovering)
    }
    @Test fun workFinishingBeforeFirstTickStillGoesThroughIdleTransition() {
        val p=player();p.request("map","fast-work");p.request("idle","finished-before-frame")
        p.advance();assertEquals("map",p.action);p.advance();assertEquals("idle",p.action)
        repeat(20){p.advance()};assertEquals("idle",p.action)
    }
}
