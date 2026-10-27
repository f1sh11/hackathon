import SwiftUI

struct TravelPreference:Codable,Identifiable,Equatable {
    var id:String;var key:String;var dimension:String;var value:String;var condition:String
    var scope:String;var source:String;var status:String;var confidence:Double
    var evidence:String;var sourceMessageId:String;var memoryId:String
    var choice:PetChoiceEvidence? = nil
    var conflictingMemoryIds:[String];var previousValues:[String]
    var label:String {Self.labels[key] ?? key}
    var summary:String {label+"："+value+(condition.isEmpty ? "":"（"+condition+"）")}
    var englishLabel:String {[
        "daily_steps":"Daily steps","queue_tolerance_minutes":"Queue tolerance","rhythm":"Trip pace","interests":"Interests","normal_start_time":"Usual start time","early_start_exception":"Early start exceptions","food_preferences":"Food preferences","dietary_restrictions":"Dietary needs","crowd_preference":"Crowds","transport_preference":"Transport","special_requirements":"Other needs"
    ][key] ?? key.replacingOccurrences(of:"_",with:" ").capitalized}
    var displaySummary:String {AppLanguage.current == .english ? englishLabel+": "+AppLanguage.current.display(value)+(condition.isEmpty ? "":" ("+AppLanguage.current.display(condition)+")"):summary}
    static let labels=["normal_start_time":"平时出发时间","early_start_exception":"早起例外","sleep_hours":"睡眠需要","daily_steps":"每日步数","walking_distance_km":"步行距离","hill_tolerance":"爬坡接受度","continuous_activity_minutes":"连续活动时间","rhythm":"旅行节奏","queue_tolerance_minutes":"排队容忍时间","queue_exception":"排队例外","crowd_preference":"人群偏好","interests":"兴趣","spending_priority":"花钱偏好","comfort_price_tradeoff":"舒适与价格","sun_tolerance":"日晒接受度","heat_tolerance":"炎热接受度","cold_tolerance":"寒冷接受度","rain_tolerance":"下雨接受度","food_preferences":"饮食偏好","dietary_restrictions":"饮食限制","meal_times":"吃饭时间","transport_preference":"交通偏好","transfer_tolerance_minutes":"换乘接受度","travel_style":"旅行方式","nap_preference":"午休习惯","free_time_preference":"留白习惯","special_requirements":"特殊需要"]
}

struct TravelPreferenceCard:View {
    @Environment(\.locale) private var interfaceLocale

    var personal=false
    let preference:TravelPreference
    let busy:Bool
    let decide:(Bool,Bool)->Void
    let correct:()->Void
    var body:some View {
        let _=interfaceLocale
        VStack(alignment:.leading,spacing:10) {
            Text(uiText(preference.source=="ai_inference" ? "这是我的猜测，理解对了吗？":"下次旅行也参考这一点吗？")).font(.subheadline.bold())
            Text(preference.displaySummary).font(.subheadline)
            if !preference.previousValues.isEmpty{Text(AppLanguage.current == .english ? "Previously: "+preference.previousValues.map{AppLanguage.current.display($0)}.joined(separator:", ")+". Confirm to replace this preference.":uiText("之前："+preference.previousValues.joined(separator:"、")+"。确认后会替换这一项。")).font(.caption).foregroundStyle(Palette.muted)}
            if !preference.evidence.isEmpty{Text(AppLanguage.current == .english ? "You said: “"+preference.evidence+"”":uiText("你说：「"+preference.evidence+"」")).font(.caption).foregroundStyle(Palette.muted)}
            HStack {
                Button(AppLanguage.current == .english ? (preference.previousValues.isEmpty ? "Remember this":"Update preference"):uiText(preference.previousValues.isEmpty ? "记住这点":"更新长期偏好")){decide(true,false)}.buttonStyle(KokoPrimaryStyle()).tint(Palette.green)
                Button(personal ? "Not now":uiText("仅限这次")){decide(false,false)}.buttonStyle(BuddySecondaryStyle())
            }.font(.caption)
            HStack{Button(uiText("我想纠正")){correct()};Spacer();Button(uiText("忽略")){decide(false,true)}}.font(.caption)
        }.padding(16).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:20)).disabled(busy)
    }
}
