import SwiftUI
import UniformTypeIdentifiers

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
    @State private var editingPersonalPreferences=false
    @State private var socialImport=false
    var body:some View {
        NavigationStack {
            Form {
                Section { HStack(spacing:18){Robot(size:65);VStack(alignment:.leading,spacing:6){Text("你好，旅行家").font(.title2.bold());Text("按自己的节奏，发现悉尼").font(.caption).foregroundStyle(Palette.muted)}}.padding(.vertical,10) }
                Section("我的旅行偏好") {
                    Text(store.personalProfile.summary.isEmpty ? "还没认识你的旅行习惯，来告诉 Buddy 吧。":store.personalProfile.summary).font(.subheadline)
                    Button { editingPersonalPreferences=true } label:{Label("修改旅行习惯",systemImage:"slider.horizontal.3")}
                    Text("新旅行会参考这些习惯；这次旅行的要求始终优先。修改不会改变已有行程或当前草稿。").font(.caption).foregroundStyle(Palette.muted)
                    DisclosureGroup("聊天中另外记住的偏好（\(store.memories.count)）") {
                        Text("这里只显示你同意长期保存的聊天补充，可以逐条删除。").font(.caption).foregroundStyle(Palette.muted)
                        if store.memories.isEmpty{Text("还没有额外保存的偏好。").foregroundStyle(Palette.muted)}
                        ForEach(store.memories){memory in HStack(alignment:.top){Text(memory.summary).font(.subheadline);Spacer();Button("删除",role:.destructive){deletingMemory=memory}.font(.subheadline).frame(minHeight:44)}}
                    }
                }
                travelDataSection
                Section {Button("匯入社群靈感",systemImage:"square.and.arrow.down"){socialImport=true}.accessibilityIdentifier("social-import-open").sheet(isPresented:$socialImport){SocialImportView()}}
                Section("设置") {
                    Picker("界面语言",selection:$store.language){ForEach(AppLanguage.allCases){language in Text(verbatim:language.label).tag(language)}}.accessibilityIdentifier("app-language-picker")
                }
                Section("体验与帮助") {
                    Button("创建一次新的旅行"){store.newTrip()}
                    Text("询问这次的日期、天数、同行人和预算，个人偏好会自动带入。").font(.caption).foregroundStyle(Palette.muted)
                }
                Section {
                    DisclosureGroup("高级设置与开发测试") {
                        Group {
                    Picker("地图来源",selection:$store.mapProvider) {
                        ForEach(store.availableMapProviders) { source in Text(LocalizedStringKey(source.label)).tag(source) }
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
            }.scrollContentBackground(.hidden).background(Palette.cream).navigationTitle(store.language.text("我的 Buddy")).tint(Palette.green)
            .sheet(isPresented:$editingPersonalPreferences){PersonalPreferencesView(editing:true)}
            .confirmationDialog("删除这条长期偏好？以后的旅行将不再参考它。",isPresented:Binding(get:{deletingMemory != nil},set:{if !$0{deletingMemory=nil}}),titleVisibility:.visible){Button("删除偏好",role:.destructive){if let memory=deletingMemory{Task{await store.deleteMemory(memory);deletingMemory=nil}}}}
        }
    }
    private var travelDataSection:some View {
        Section("旅遊資料來源") {
            Picker("資料取得方式",selection:$store.travelDataSource){ForEach(TravelDataSource.allCases){route in Text(LocalizedStringKey(route.label)).tag(route)}}.accessibilityIdentifier("travel-data-source")
            Text("店家、景點、天氣與地圖設定可切換來源；底圖與導航仍由地圖服務提供。").font(.caption).foregroundStyle(Palette.muted)
            Text("Zetaris 連線失敗時可手動切回直接來源。").font(.caption)
            if let status=store.dataConnection{Text(status).font(.caption).accessibilityIdentifier("travel-data-status")}
            Button("檢查資料連線"){Task{await store.reloadTravelData()}}
        }.task(id:store.travelDataSource){await store.reloadTravelData()}
    }
    func status(_ name:String,ready:Bool)->some View {HStack{Text(LocalizedStringKey(name));Spacer();Label(LocalizedStringKey(ready ? "已配置" : "待配置"),systemImage:ready ? "checkmark.circle.fill" : "circle.dashed").font(.caption).foregroundStyle(ready ? Palette.green : Palette.muted)}}
}

@MainActor final class SocialImportModel:ObservableObject {
    @Published var busy=false
    @Published var error:String?
    @Published var message:String?
    @Published var preview:[String:Any]?
    @Published var record:[String:Any]?
    @Published var history:[[String:Any]]=[]
    @Published var places:[[String:Any]]=[]
    @Published var matching:Int?
    func call(_ path:String,body:[String:Any]?=nil,store:AppStore) async throws -> [String:Any] {
        var request=URLRequest(url:store.base.appendingPathComponent(path));request.httpMethod=body == nil ? "GET":"POST"
        store.travelDataSource.apply(to:&request);request.setValue("application/json",forHTTPHeaderField:"Content-Type");request.timeoutInterval=100
        if let body{request.httpBody=try JSONSerialization.data(withJSONObject:body)}
        let (data,response)=try await store.loader(request)
        let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any] ?? [:]
        guard let http=response as? HTTPURLResponse,(200..<300).contains(http.statusCode) else{throw NSError(domain:"SocialImport",code:1,userInfo:[NSLocalizedDescriptionKey:raw["error"] as? String ?? store.language.text("匯入失敗，請重試。")])}
        return raw
    }
    func run(_ action:() async throws -> Void) async {busy=true;error=nil;message=nil;defer{busy=false};do{try await action()}catch{self.error=error.localizedDescription}}
    func prepare(platform:String,url:String,text:String,json:String,jsonMode:Bool,store:AppStore) async {
        await run {
            if jsonMode {
                guard json.utf8.count<=250000,let body=try JSONSerialization.jsonObject(with:Data(json.utf8)) as? [String:Any],let mentions=body["mentions"] as? [[String:Any]],!mentions.isEmpty else{throw NSError(domain:"SocialImport",code:2,userInfo:[NSLocalizedDescriptionKey:store.language.text("JSON 需要貼文來源、text 與 mentions。")])}
                preview=body
            }else{
                var body:[String:Any]=["platform":platform,"postUrl":url,"text":text]
                let result=try await call("social/extract",body:body,store:store)
                guard let mentions=result["mentions"] as? [[String:Any]],!mentions.isEmpty else{message=store.language.text("沒有找到可辨識的景點，請補充內容。");return}
                body["mentions"]=mentions;preview=body
            }
        }
    }
    func save(store:AppStore) async {guard let preview else{return};await run{record=try await call("social/import",body:preview,store:store);message=store.language.text("已儲存，請逐一比對地圖地點。")}}
    func load(store:AppStore) async {await run{history=(try await call("social/imports",store:store))["imports"] as? [[String:Any]] ?? []}}
    func search(_ query:String,lat:String,lng:String,store:AppStore) async {
        await run{guard let lat=Double(lat),let lng=Double(lng),abs(lat)<=90,abs(lng)<=180 else{throw NSError(domain:"SocialImport",code:3,userInfo:[NSLocalizedDescriptionKey:store.language.text("請輸入有效的搜尋中心座標。")])}
            places=(try await call("poi/search",body:["provider":"osm","query":query,"lat":lat,"lng":lng,"radius":1500,"category":"all"],store:store))["places"] as? [[String:Any]] ?? []
            if places.isEmpty{message=store.language.text("找不到地點，請調整名稱或搜尋中心。")} }
    }
    func publish(_ point:[String:Any],store:AppStore) async {guard let id=record?["id"] as? String,let matching else{return};await run{
        record=try await call("social/\(id)/publish",body:["mentionIndex":matching,"placeId":point["id"] ?? "","placeName":point["name"] ?? "","lat":point["lat"] ?? 0,"lng":point["lng"] ?? 0],store:store)
        self.matching=nil;places=[];message=store.language.text("已發布至本機景點；Zetaris 資料尚待同步。")
    }}
    var mentions:[[String:Any]] {(record ?? preview)?["mentions"] as? [[String:Any]] ?? []}
    func reset(){preview=nil;record=nil;matching=nil;places=[];error=nil;message=nil}
}
struct SocialImportView:View {
    @EnvironmentObject private var store:AppStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model=SocialImportModel()
    @State private var jsonMode=false
    @State private var platform="instagram"
    @State private var url=""
    @State private var text=""
    @State private var json=""
    @State private var filePicker=false
    @State private var query=""
    @State private var lat="-33.865"
    @State private var lng="151.212"
    var body:some View {
        NavigationStack {
            Form {
                if model.preview == nil && model.record == nil {inputSection}
                else {
                    Section("萃取預覽") {
                        Text((model.record ?? model.preview)?["postUrl"] as? String ?? "").font(.caption).textSelection(.enabled)
                        ForEach(Array(model.mentions.enumerated()),id:\.offset){index,mention in
                            VStack(alignment:.leading,spacing:8){
                                Text(mention["name"] as? String ?? "").font(.headline)
                                Text(mention["quote"] as? String ?? "").font(.subheadline)
                                ForEach(["dishes","prices","reviews"],id:\.self){field in
                                    ForEach(Array(((mention[field] as? [[String:Any]]) ?? []).enumerated()),id:\.offset){_,item in Text(item["quote"] as? String ?? "").font(.caption).foregroundStyle(Palette.muted)}
                                }
                                if model.record != nil {Button("比對地圖地點"){model.matching=index;query=mention["name"] as? String ?? "";model.places=[]}.accessibilityIdentifier("social-match-\(index)")}
                            }.padding(.vertical,5)
                        }
                        if model.record == nil {Button("確認儲存草稿"){Task{await model.save(store:store)}}.accessibilityIdentifier("social-import-save")}
                        Button("開始新的匯入"){model.reset()}
                    }
                }
                if model.matching != nil {matchingSection}
                Section {
                    Text("內容與評論保留原文證據，不代表已核實的價格或評分。").font(.caption).foregroundStyle(Palette.muted)
                    Text("貼文萃取會使用已設定的 AI；JSON 匯入不需 AI。").font(.caption).foregroundStyle(Palette.muted)
                    if model.busy {ProgressView()}
                    if let error=model.error {Text(error).foregroundStyle(.red).accessibilityIdentifier("social-import-error")}
                    if let message=model.message {Text(message).foregroundStyle(Palette.green).accessibilityIdentifier("social-import-message")}
                    Button("讀取已匯入資料"){Task{await model.load(store:store)}}
                    ForEach(Array(model.history.enumerated()),id:\.offset){_,record in
                        Button{model.reset();model.record=record}label:{VStack(alignment:.leading){Text((record["mentions"] as? [[String:Any]])?.first?["name"] as? String ?? "");Text(record["postUrl"] as? String ?? "").font(.caption).lineLimit(1)}}
                    }
                }
            }.disabled(model.busy)
            .navigationTitle("匯入社群靈感").navigationBarTitleDisplayMode(.inline)
            .toolbar{Button("完成"){dismiss()}.disabled(model.busy)}
            .interactiveDismissDisabled(model.busy)
            .fileImporter(isPresented:$filePicker,allowedContentTypes:[.json]){result in
                do {let file=try result.get();let scoped=file.startAccessingSecurityScopedResource();defer{if scoped{file.stopAccessingSecurityScopedResource()}}
                    let size=try file.resourceValues(forKeys:[.fileSizeKey]).fileSize ?? 0;guard size<=250000 else{throw CocoaError(.fileReadTooLarge)}
                    let data=try Data(contentsOf:file);guard data.count<=250000,let value=String(data:data,encoding:.utf8) else{throw CocoaError(.fileReadCorruptFile)}
                    json=value;jsonMode=true
                }catch{model.error=error.localizedDescription}
            }
        }
    }
    private var inputSection:some View {
        Section {
            Picker("匯入方式",selection:$jsonMode){Text("貼上貼文").tag(false);Text("匯入 JSON").tag(true)}.pickerStyle(.segmented)
            if jsonMode {
                Button("選擇 JSON 檔案"){filePicker=true}
                TextEditor(text:$json).frame(minHeight:180).accessibilityIdentifier("social-import-json")
                Text("JSON 需要貼文來源、text 與 mentions。").font(.caption).foregroundStyle(Palette.muted)
            }else{
                Picker("平台",selection:$platform){ForEach([("instagram","Instagram"),("tiktok","TikTok"),("threads","Threads"),("xiaohongshu","小紅書")],id:\.0){Text($0.1).tag($0.0)}}
                TextField("原貼文網址",text:$url).textInputAutocapitalization(.never).keyboardType(.URL).accessibilityIdentifier("social-import-url")
                TextEditor(text:$text).frame(minHeight:180).accessibilityIdentifier("social-import-text")
                Text("貼上你有權使用的旅遊貼文內容。").font(.caption).foregroundStyle(Palette.muted)
            }
            Button(jsonMode ? "預覽資料":"萃取預覽"){Task{await model.prepare(platform:platform,url:url,text:text,json:json,jsonMode:jsonMode,store:store)}}.disabled(jsonMode ? json.isEmpty:url.isEmpty || text.isEmpty).accessibilityIdentifier("social-import-preview")
        }
    }
    private var matchingSection:some View {
        Section("比對地圖地點") {
            TextField("地點名稱",text:$query)
            HStack{TextField("緯度",text:$lat);TextField("經度",text:$lng)}.textInputAutocapitalization(.never)
            Text("搜尋中心預設雪梨，可修改座標。").font(.caption).foregroundStyle(Palette.muted)
            Button("搜尋地圖地點"){Task{await model.search(query,lat:lat,lng:lng,store:store)}}
            ForEach(Array(model.places.enumerated()),id:\.offset){_,point in
                VStack(alignment:.leading){Text(point["name"] as? String ?? "").font(.headline);Text(point["address"] as? String ?? "").font(.caption);Button("確認地點並發布"){Task{await model.publish(point,store:store)}}}
            }
        }
    }
}
