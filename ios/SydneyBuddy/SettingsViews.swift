import SwiftUI

struct JournalView:View {
    @EnvironmentObject var store:AppStore
    @State var title="午餐"
    @State var amount=""
    @State var people=2
    var body:some View {
        NavigationStack {
            Form {
                Section { VStack(alignment:.leading,spacing:10){Text("把花费也安排明白").font(.title2.bold());Text("本机账本 · AUD · 记录与均分，不执行转账").font(.caption).foregroundStyle(Palette.muted)}.padding(.vertical,10) }
                Section("记一笔") {
                    TextField("项目",text:$title)
                    TextField("金额（AUD）",text:$amount).keyboardType(.decimalPad)
                    Stepper(store.language.text("参与人数：{0}",String(people)),value:$people,in:1...10)
                    Button("保存消费") {
                        let normalized=amount.replacingOccurrences(of:",",with:".")
                        guard let decimal=Decimal(string:normalized),decimal>0,decimal<100000 else {store.errorMessage="请输入有效金额";return}
                        var raw=decimal*100,rounded=Decimal();NSDecimalRound(&rounded,&raw,0,.plain)
                        let cents=NSDecimalNumber(decimal:rounded).intValue
                        store.expenses.append(Expense(title:title.isEmpty ? "消费" : title,cents:cents,participants:people));amount=""
                    }.tint(Palette.green)
                }
                Section("消费记录") {
                    ForEach(store.expenses){e in VStack(alignment:.leading,spacing:8){HStack{Text(e.title).font(.headline);Spacer();Text(String(format:"A$%.2f",Double(e.cents)/100)).bold()};Text("\(e.participants) 人均分："+e.shares.map{String(format:"A$%.2f",Double($0)/100)}.joined(separator:" / ")).font(.caption).foregroundStyle(Palette.muted)}}.onDelete{store.expenses.remove(atOffsets:$0)}
                    if store.expenses.isEmpty {Text("还没有记录，先享受这段旅程吧。").foregroundStyle(Palette.muted)}
                }
                Section {Text("遇到不能整除的金额，余下的分按成员顺序分配，合计始终等于原始金额。此版本未关联成员账户或多人同步。").font(.caption).foregroundStyle(Palette.muted)}
            }.scrollContentBackground(.hidden).background(Palette.cream).navigationTitle(store.language.text("旅行小账本"))
        }
    }
}
struct SettingsView:View {
    @EnvironmentObject var store:AppStore
    @State private var deletingMemory:Memory?
    var body:some View {
        NavigationStack {
            Form {
                Section { HStack(spacing:18){Robot(size:65);VStack(alignment:.leading,spacing:6){Text("你好，旅行家").font(.title2.bold());Text("按自己的节奏，发现悉尼").font(.caption).foregroundStyle(Palette.muted)}}.padding(.vertical,10) }
                Section("语言") {
                    Picker("介面語言",selection:$store.language) {
                        ForEach(AppLanguage.allCases){language in Text(verbatim:language.label).tag(language)}
                    }.accessibilityIdentifier("app-language-picker")
                    Text("語言切換後立即生效，並保存在這台裝置。").font(.caption)
                    Text("景點名稱與已儲存的行程內容保留原始語言。").font(.caption).foregroundStyle(Palette.muted)
                }
                Section("Buddy 记住的偏好") {
                    Text("新旅行会参考你同意保存的兴趣与节奏，本次选择始终优先。长期记忆可在开场摘要中保存。").font(.caption).foregroundStyle(Palette.muted)
                    if store.memories.isEmpty{Text("还没有长期偏好，和 Buddy 聊聊吧。").foregroundStyle(Palette.muted)}
                    ForEach(store.memories){memory in HStack(alignment:.top){Text(memory.summary).font(.subheadline);Spacer();Button("删除",role:.destructive){deletingMemory=memory}.font(.subheadline).frame(minHeight:44)}}
                }
                Section("体验与帮助") {
                    Button("重新体验开场提问"){store.newTrip()}
                    Text("开启新的旅行草稿，历史行程和长期偏好保留。").font(.caption).foregroundStyle(Palette.muted)
                }
                Section {
                    DisclosureGroup("高级设置与开发测试") {
                        Group {
                    Picker("地图来源",selection:$store.mapProvider) {
                        ForEach(MapProvider.allCases) { source in Text(LocalizedStringKey(source.label)).tag(source) }
                    }
                    Text("切换后，交通规划会预选对应路线服务，需点查询才取得新路线。NSW 资料可另选；Google 仍需启用服务。").font(.caption).foregroundStyle(Palette.muted)
                    if store.mapProvider == .google && !SydneyBuddyApp.mapConfigured {
                        Text("Google 地图未配置密钥，请选择 OpenStreetMap 或 Apple Maps。").font(.caption)
                    }
                }
                        Group {
                    Toggle("使用在线服务",isOn:$store.live).tint(Palette.green).disabled(store.health?.paidServicesAllowed != true)
                    if store.health?.paidServicesAllowed != true { Text("后端已禁止付费服务，当前可使用演示模式。").font(.caption) }
                    status("本机规划服务",ready:store.health?.status=="ok")
                    status("NVIDIA Nemotron",ready:store.health?.nvidiaConfigured==true)
                    status("Google 地点与路线",ready:store.health?.googleConfigured==true)
                    status("Google iOS 地图",ready:SydneyBuddyApp.mapConfigured)
                    Text("在线模式会将本次旅行的偏好、起终点和日期发送给 Google、NVIDIA 及天气服务。不要输入私人住址或敏感信息。密钥在电脑本地设置页配置。").font(.caption).foregroundStyle(Palette.muted)
                    Button("刷新连接状态"){Task{await store.reload()}}
                }
                        Button("试用 3 天时间轴"){Task{await store.tryScheduleDemo()}}
                        Text("Sydney Buddy · iOS 测试版本。路线来源和待确认信息可在行程详情中查看。").font(.caption).foregroundStyle(Palette.muted)
                    }
                }
            }.scrollContentBackground(.hidden).background(Palette.cream).navigationTitle(store.language.text("我的 Buddy"))
            .confirmationDialog("删除这条长期偏好？以后的旅行将不再参考它。",isPresented:Binding(get:{deletingMemory != nil},set:{if !$0{deletingMemory=nil}}),titleVisibility:.visible){Button("删除偏好",role:.destructive){if let memory=deletingMemory{Task{await store.deleteMemory(memory);deletingMemory=nil}}}}
        }
    }
    func status(_ name:String,ready:Bool)->some View {HStack{Text(LocalizedStringKey(name));Spacer();Label(LocalizedStringKey(ready ? "已配置" : "待配置"),systemImage:ready ? "checkmark.circle.fill" : "circle.dashed").font(.caption).foregroundStyle(ready ? Palette.green : Palette.muted)}}
}
