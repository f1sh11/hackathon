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
                    Stepper("参与人数：\(people)",value:$people,in:1...10)
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
            }.scrollContentBackground(.hidden).background(Palette.cream).navigationTitle("旅行小账本")
        }
    }
}
struct SettingsView:View {
    @EnvironmentObject var store:AppStore
    @State var consent=false
    var body:some View {
        NavigationStack {
            Form {
                Section { HStack(spacing:18){Robot(size:65);VStack(alignment:.leading,spacing:6){Text("你好，旅行家").font(.title2.bold());Text("按自己的节奏，发现悉尼").font(.caption).foregroundStyle(Palette.muted)}}.padding(.vertical,10) }
                Section("地图底图") {
                    Picker("地图来源",selection:$store.mapProvider) {
                        ForEach(MapProvider.allCases) { source in Text(source.label).tag(source) }
                    }
                    Text("切换后，交通规划会预选对应路线服务，需点查询才取得新路线。NSW 资料可另选；Google 仍需启用服务。").font(.caption).foregroundStyle(Palette.muted)
                    if store.mapProvider == .google && !SydneyBuddyApp.mapConfigured {
                        Text("Google 地图未配置密钥，请选择 OpenStreetMap 或 Apple Maps。").font(.caption)
                    }
                }
                Section("服务连接") {
                    Toggle("使用在线服务",isOn:$store.live).tint(Palette.green).disabled(store.health?.paidServicesAllowed != true)
                    if store.health?.paidServicesAllowed != true { Text("后端已禁止付费服务，当前可使用演示模式。").font(.caption) }
                    status("本机规划服务",ready:store.health?.status=="ok")
                    status("NVIDIA Nemotron",ready:store.health?.nvidiaConfigured==true)
                    status("Google 地点与路线",ready:store.health?.googleConfigured==true)
                    status("Google iOS 地图",ready:SydneyBuddyApp.mapConfigured)
                    Text("在线模式会将本次旅行的偏好、起终点和日期发送给 Google、NVIDIA 及天气服务。不要输入私人住址或敏感信息。密钥在电脑本地设置页配置。").font(.caption).foregroundStyle(Palette.muted)
                    Button("刷新连接状态"){Task{await store.reload()}}
                }
                Section("下一次旅行的默认设置") {
                    Picker("出发地点",selection:$store.profile.start){Text("环形码头").tag(Coordinate.quay);Text("中央车站").tag(Coordinate.central)}
                    Picker("返回地点",selection:$store.profile.end){Text("环形码头").tag(Coordinate.quay);Text("中央车站").tag(Coordinate.central)}
                    Stepper("步数偏好：\(store.profile.maxSteps)",value:$store.profile.maxSteps,in:1000...30000,step:1000)
                    Text("当前行程的参数在“问 AI”中调整；这里用于新行程。").font(.caption).foregroundStyle(Palette.muted)
                }
                Section("我同意保存的偏好") {
                    Toggle("仅保存本机旅行偏好",isOn:$consent).tint(Palette.green)
                    Text("保存兴趣和步数偏好，供以后规划选择使用。可随时从检索中撤回；本机审计记录保留操作历史。不保存位置轨迹、照片或私人聊天。").font(.caption).foregroundStyle(Palette.muted)
                    Button("记住当前偏好"){Task{await store.saveMemory()}}.disabled(!consent)
                    ForEach(store.memories){memory in
                        VStack(alignment:.leading,spacing:10){Text(memory.summary).font(.subheadline);HStack{Button("用于下次规划"){store.profile.interests=memory.profile.interests;store.profile.maxSteps=memory.profile.maxSteps};Spacer();Button("删除",role:.destructive){Task{await store.deleteMemory(memory)}}}.font(.caption)}
                    }
                }
                Section("关于这个版本") {
                    Text("Sydney Buddy · iOS MVP").font(.headline)
                    Text("演示模式使用明确标记的示例数据；在线模式仅在服务成功返回后生成方案。尚未包含多人同步、自动预订、支付及后台定位。Cursor 与 Zetaris 按你的要求暂缓。").font(.caption).foregroundStyle(Palette.muted)
                }
            }.scrollContentBackground(.hidden).background(Palette.cream).navigationTitle("我的 Buddy")
        }
    }
    func status(_ name:String,ready:Bool)->some View {HStack{Text(name);Spacer();Label(ready ? "已配置" : "待配置",systemImage:ready ? "checkmark.circle.fill" : "circle.dashed").font(.caption).foregroundStyle(ready ? Palette.green : Palette.muted)}}
}
