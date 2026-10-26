import SwiftUI

/// Small, explicitly confirmed groups; no defaults are stored merely by opening.
struct GuidedTripDetails: View {
    @Environment(\.locale) private var interfaceLocale

    let single:Bool
    let save: ([String:String]) async -> String?
    @Environment(\.dismiss) private var dismiss
    @State private var stage: Int
    @State private var destination: String
    @State private var date: Date
    @State private var days: Int
    @State private var people: Int
    @State private var budget: String
    @State private var basis: String
    @State private var extra:String
    @State private var busy = false
    @State private var error: String?
    init(fields:[String:String], startStage:Int?=nil, single:Bool=false, save:@escaping ([String:String]) async -> String?) {
        self.save=save;self.single=single
        let index=startStage ?? Self.firstMissing(fields)
        _extra=State(initialValue:fields[Self.extraKey(index)] ?? "")
        _stage=State(initialValue:index)
        _destination=State(initialValue:fields["destination"] ?? "")
        let formatter=DateFormatter();formatter.dateFormat="yyyy-MM-dd"
        _date=State(initialValue:formatter.date(from:fields["date"] ?? "") ?? Date())
        _days=State(initialValue:min(15,max(1,Int(fields["days"] ?? "") ?? 1)))
        _people=State(initialValue:min(30,max(1,Int(fields["travelers"] ?? "") ?? 1)))
        _budget=State(initialValue:fields["budgetCurrency"] == "AUD" ? fields["budget"] ?? "":"")
        _basis=State(initialValue:TravelRequirements.budgetBases.contains(where:{$0.0==fields["budgetBasis"]}) ? fields["budgetBasis"]!:"perPersonPerDay")
    }
    static func firstMissing(_ fields:[String:String])->Int {
        if !TravelRequirements.known(fields["destination"]){return 0}
        let formatter=DateFormatter();formatter.dateFormat="yyyy-MM-dd";formatter.isLenient=false
        guard let raw=fields["date"],let parsed=formatter.date(from:raw),formatter.string(from:parsed)==raw,(1...15).contains(Int(fields["days"] ?? "") ?? 0) else{return 1}
        if !(1...30).contains(Int(fields["travelers"] ?? "") ?? 0){return 2}
        return 3
    }
    static func correctionStage(_ fields:[String:String])->Int {
        if TravelRequirements.known(fields["destination"]),!TravelRequirements.supportedDestination(fields["destination"]){return 0}
        if TravelRequirements.known(fields["budgetCurrency"]),fields["budgetCurrency"] != "AUD"{return 3}
        if fields["budgetCoverage"]=="totalTravel"{return 3}
        return firstMissing(fields)
    }
    private static func extraKey(_ index:Int)->String {[4:"origin",5:"mustDo",6:"avoid",7:"companionNeeds"][index] ?? ""}
    private let detailsBlue=Color(red:0.20,green:0.36,blue:0.79)
    private var pageTitle:String {["Destination","Dates & duration","Travel companions","Activity budget","Starting point","Must-do plans","Things to avoid","Travel needs"][stage]}
    private var question:String {["Where shall we go?","When are we going?","Who’s coming along?","What feels right for activities?","Where shall we start?","What’s on your wish list?","Anything you’d rather skip?","What would make the trip easier?"][stage]}
    var body:some View {
        let _=interfaceLocale
        NavigationStack {
            ScrollView {VStack(alignment:.leading,spacing:20){
                if !single{ProgressView(value:Double(stage+1),total:4).tint(detailsBlue)}
                HStack(alignment:.center,spacing:12){PetAvatar(mood:.curious,size:64).frame(width:64,height:70);VStack(alignment:.leading,spacing:6){if !single{Text(uiText("第 \(stage+1) / 4 步")).font(.caption).foregroundStyle(Palette.muted)};Text(uiText(question)).font(.system(.title3,design:.rounded,weight:.bold)).fixedSize(horizontal:false,vertical:true)}}
                VStack(alignment:.leading,spacing:20) {
                    switch stage {
                    case 0:
                        TextField(uiText("城市或地区"),text:$destination).textFieldStyle(.roundedBorder).accessibilityIdentifier("details-destination")
                        Button(uiText("悉尼")){destination="悉尼"}.buttonStyle(.bordered)
                        Text(uiText("目前可生成悉尼路线；其他目的地可以先保存想法。")).font(.caption).foregroundStyle(Palette.muted)
                    case 1:
                        DatePicker(uiText("出发日期"),selection:$date,displayedComponents:.date).datePickerStyle(.compact)
                        Divider()
                        HStack{
                            VStack(alignment:.leading,spacing:4){Text(uiText("Trip length")).font(.subheadline.weight(.semibold));Text(uiText("Up to 15 days")).font(.caption).foregroundStyle(Palette.muted)}
                            Spacer()
                            Button{days=max(1,days-1)}label:{Image(systemName:"minus").frame(width:44,height:44).background(detailsBlue.opacity(0.07),in:Circle())}.disabled(days<=1).accessibilityLabel(uiText("Fewer days"))
                            Text(String(days)).font(.system(.title2,design:.rounded,weight:.bold)).monospacedDigit().frame(minWidth:32).accessibilityLabel(uiText("Trip length")+": "+String(days))
                            Button{days=min(15,days+1)}label:{Image(systemName:"plus").frame(width:44,height:44).background(detailsBlue.opacity(0.07),in:Circle())}.disabled(days>=15).accessibilityLabel(uiText("More days"))
                        }.accessibilityIdentifier("details-days")
                        Button(uiText("Decide the date later")){Task{busy=true;error=await save(["date":""]);busy=false;if error==nil{if single{dismiss()}else{stage+=1}}}}.font(.subheadline).frame(minHeight:44).disabled(busy)
                    case 2:
                        Stepper(uiText("一共 \(people) 人"),value:$people,in:1...30)
                        Text(uiText("包括你自己。特别的照顾需求可以继续告诉 Koko。")).font(.subheadline).foregroundStyle(Palette.muted)
                    case 3:
                        HStack {TextField(uiText("金额"),text:$budget).keyboardType(.decimalPad).textFieldStyle(.roundedBorder).accessibilityIdentifier("details-budget");Text(uiText("AUD")).fontWeight(.bold)}
                        Picker(uiText("预算范围"),selection:$basis){ForEach(TravelRequirements.budgetBases,id:\.0){Text(uiText($0.1)).tag($0.0)}}
                        Text(uiText("这是活动预算，不包含住宿、交通及全部餐饮。选择每人或全体、每天或全程。")).font(.caption).foregroundStyle(Palette.muted)
                    default:
                        TextField(uiText("暂未确定，可以留空"),text:$extra,axis:.vertical).lineLimit(3...6).textFieldStyle(.roundedBorder)
                        if stage==4{Text(uiText("暂未确定时，生成路线会明确提示采用的临时起点。")).font(.caption).foregroundStyle(Palette.muted)}
                    }
                }.padding(16).background(Color.gray.opacity(0.035),in:RoundedRectangle(cornerRadius:16))
                if let error {Text(uiText(error)).font(.subheadline).foregroundStyle(.red)}
                Text(uiText("Just for this trip. Your other details stay as they are.")).font(.caption).foregroundStyle(Palette.muted)
                if stage>0 && !single{Button(uiText("上一步")){error=nil;stage-=1}.frame(maxWidth:.infinity).disabled(busy)}
            }.padding(20)}.background(Color.white).foregroundStyle(Color(red:0.09,green:0.15,blue:0.24))
                .navigationTitle(uiText(pageTitle)).navigationBarTitleDisplayMode(.inline)
                .toolbar{ToolbarItem(placement:.topBarTrailing){Button(uiText(single ? "取消":"稍后继续")){dismiss()}.disabled(busy)}}
                .safeAreaInset(edge:.bottom){
                    Button{Task{await advance()}}label:{HStack(spacing:8){if busy{ProgressView().tint(.white)};Text(uiText(busy ? "正在收好…":single ? "保存修改":stage==3 ? "完成，回到聊天":"确认，下一步"));if !busy{Image(systemName:"checkmark")}}.font(.headline).frame(maxWidth:.infinity,minHeight:52)}.buttonStyle(TripsCreateButtonStyle()).disabled(busy).padding(.horizontal,20).padding(.top,12).padding(.bottom,12).background(.white).accessibilityIdentifier("details-save")
                }
        }.tint(detailsBlue).interactiveDismissDisabled(busy)
            .presentationDetents(single ? [.height(520),.large]:[.large])
            .presentationDragIndicator(.visible)
    }
    @MainActor private func advance() async {
        error=nil
        let delta:[String:String]
        switch stage {
        case 0:
            let city=destination.trimmingCharacters(in:.whitespacesAndNewlines)
            guard TravelRequirements.known(city) else{error="哼唧，还没告诉我去哪儿呢～先选个城市，我才能带路呀。";return}
            delta=["destination":city]
        case 1:delta=["date":TripProfile.dateString(date),"days":String(days)]
        case 2:delta=["travelers":String(people)]
        case 3:
            guard let amount=Double(budget.trimmingCharacters(in:.whitespacesAndNewlines)),amount.isFinite,amount>=0 else{error="活动预算不能为负数，免费活动可以填 0～";return}
            let daily=amount / (basis.hasPrefix("group") ? Double(people):1) / (basis.hasSuffix("Trip") ? Double(days):1)
            guard amount<=1000000,daily<=10000 else{error="金额超出当前范围：每人每天活动预算最多 10,000 AUD，请调整金额或预算范围。";return}
            delta=["budget":budget.trimmingCharacters(in:.whitespacesAndNewlines),"budgetCurrency":"AUD","budgetBasis":basis,"budgetCoverage":"activities"]
        default:delta=[Self.extraKey(stage):extra.trimmingCharacters(in:.whitespacesAndNewlines)]
        }
        busy=true;error=await save(delta);busy=false
        if error==nil{if single || stage==3{dismiss()}else{stage+=1}}
    }
}

private struct TripDetailSelection:Identifiable {let id:Int}
struct TripDetailsSummary:View {
    @Environment(\.locale) private var interfaceLocale

    let fields:[String:String]
    let save:([String:String]) async -> String?
    @Environment(\.dismiss) private var dismiss
    @State private var editing:TripDetailSelection?
    @State private var continuing=false
    private func value(_ key:String)->String {TravelRequirements.known(fields[key]) ? fields[key]!:"待补充"}
    var body:some View {
        let _=interfaceLocale
        NavigationStack {
            List {
                Section(uiText("本次旅行 · 点哪项就改哪项")) {
                    row("目的地",value("destination"),0)
                    row("日期与天数",(TravelRequirements.known(fields["date"]) ? value("date"):"日期未定")+(TravelRequirements.known(fields["days"]) ? " · "+value("days")+" 天":" · 天数未定"),1)
                    row("同行人数",TravelRequirements.known(fields["travelers"]) ? value("travelers")+" 人":"人数待确认",2)
                    row("活动预算",TravelRequirements.known(fields["budget"]) ? value("budget")+" "+(fields["budgetCurrency"] ?? "币种待确认")+" · "+TravelRequirements.optionTitle(fields["budgetBasis"] ?? "范围待确认",field:"budgetBasis"):"金额待确认",3)
                }
                Section(uiText("可选补充")) {
                    row("住宿 / 每日起点",value("origin"),4)
                    row("特别想去或想做",value("mustDo"),5)
                    row("需要避开",value("avoid"),6)
                    row("同行者需要",value("companionNeeds"),7)
                }
                if !TravelRequirements.missingFields(fields).isEmpty {
                    Button(uiText("按步骤补齐剩余资料")){continuing=true}
                }
                Text(uiText("只修改这次旅行。保存后 Koko 会告诉你还缺什么；取消不会保存当前修改。")).font(.caption).foregroundStyle(Palette.muted)
            }.scrollContentBackground(.hidden).background(.white)
                .navigationTitle(uiText("旅行资料")).navigationBarTitleDisplayMode(.inline).toolbar{Button(uiText("回到聊天")){dismiss()}}
                .sheet(item:$editing){item in GuidedTripDetails(fields:fields,startStage:item.id,single:true,save:save)}
                .sheet(isPresented:$continuing){GuidedTripDetails(fields:fields,save:save)}
        }.tint(Color(red:0.20,green:0.36,blue:0.79))
    }
    private func row(_ title:String,_ detail:String,_ stage:Int)->some View {
        Button{editing=TripDetailSelection(id:stage)}label:{HStack {VStack(alignment:.leading,spacing:5){Text(uiText(title)).font(.subheadline.bold());Text(uiText(detail)).font(.caption).foregroundStyle(Palette.muted)};Spacer();Image(systemName:"chevron.right").font(.caption)}}.foregroundStyle(Palette.ink).accessibilityIdentifier("trip-detail-\(stage)")
    }
}
