package com.sydneybuddy.travel

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import org.junit.Rule
import org.junit.Test

/** Runs against a local development backend with adb reverse configured.
 * Creates demo trips and one local expense; never invokes paid providers.
 */
class BuddyFlowTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private fun click(text: String) {
        val node=rule.onNode(hasText(text,substring=false) and hasClickAction())
        if(text !in listOf("探索","行程","问 AI","账本","我的","新建")) node.performScrollTo()
        node.performClick()
    }
    private fun awaitText(text: String) {
        rule.waitUntil(30000) { rule.onAllNodesWithText(text,substring=true).fetchSemanticsNodes().isNotEmpty() }
    }
    private fun newDemoTrip() {
        if(rule.onAllNodesWithText("新建",substring=false).fetchSemanticsNodes().isNotEmpty()) click("新建")
        repeat(7) {click("选好了，继续")}
        click("让 Buddy 规划路线")
        awaitText("你的路线准备好了")
        click("确认并保存")
        awaitText("出发前的 3 个小任务")
    }

    @Test fun onboardingSaveAdjustmentCancelAndLocalPersistence() {
        val countBefore=org.json.JSONArray(java.net.URL("http://127.0.0.1:8787/trips").readText()).length()
        rule.waitUntil(30000) {rule.onAllNodesWithText("Buddy 正在处理",substring=true).fetchSemanticsNodes().isEmpty()}
        newDemoTrip()
        click("问 AI")
        click("少走一点")
        awaitText("看看这次怎么调整")
        click("取消草案")
        click("探索")
        click("查看路线详情")
        rule.onNodeWithText("我已备好行李").performScrollTo()
        rule.onNode(isToggleable() and hasAnySibling(hasText("我已备好行李"))).performClick()
        click("账本")
        rule.onNodeWithText("金额 AUD").performTextInput("10.01")
        click("＋")
        click("保存消费")
        val split="3 人均分：A$3.34 / A$3.34 / A$3.33"
        rule.onAllNodesWithText(split).onFirst().performScrollTo().assertIsDisplayed()
        rule.activityRule.scenario.recreate()
        awaitText("旅行小账本")
        rule.onAllNodesWithText(split).onFirst().performScrollTo().assertIsDisplayed()
        click("探索")
        newDemoTrip()
        click("行程")
        rule.onAllNodesWithText("查看地图").assertCountEquals(countBefore+2)
    }
}
