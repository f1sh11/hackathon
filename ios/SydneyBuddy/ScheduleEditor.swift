import SwiftUI

struct ScheduleSettings:Codable {
    var pace:String;var timeLimit:Int;var restroomInterval:Int;var mealCost:Double
    var dailyBudget:Double;var totalBudget:Double;var maxSteps:Int;var luggage:Bool
}
struct ScheduleActivity:Codable,Identifiable {
    var id:String;var placeId:String?;var kind:String;var name:String
    var lat:Double;var lng:Double;var duration:Int;var cost:Double
    var confirmed:Bool?;var locked:Bool?;var fixedStart:Int?;var fixedDuration:Int?
    var openMinute:Int?;var closeMinute:Int?;var queueMinutes:Int?;var queueSource:String?
    var arrival:String;var departure:String;var scheduledStart:Int;var travelMinutes:Int
    var indoor:Bool?;var hoursVerified:Bool?
    var label:String {switch kind {case "meal":return "用餐";case "restroom":return "廁所";case "rest":return "休息";case "stay":return "入住";case "checkout":return "退房";case "bag-drop":return "寄放";case "bag-pickup":return "取件";default:return "景點"}}
}
struct ScheduleStay:Codable {var name:String;var lat:Double;var lng:Double;var cost:Double;var confirmed:Bool?;var checkIn:Int;var checkOut:Int}
struct ScheduleDay:Codable,Identifiable {
    var date:String;var startMinute:Int;var endMinute:Int;var start:Coordinate;var end:Coordinate;var stay:ScheduleStay
    var activities:[ScheduleActivity];var transportMinutes:Int;var queueMinutes:Int;var timeUsed:Int;var timeRemaining:Int
    var unknownQueue:Bool;var estimatedSteps:Int;var estimatedCost:Double;var finishTime:String
    var id:String {date}
}
struct ScheduleCandidate:Codable,Identifiable {var id:String;var name:String;var category:String;var indoor:Bool;var visitMinutes:Int;var cost:Double;var lat:Double;var lng:Double;var reason:String}
struct ScheduleIssue:Codable,Identifiable {var code:String;var message:String;var day:Int;var activityId:String?;var blocking:Bool;var id:String {code+message+(activityId ?? "")}}
struct ScheduleRadar:Codable {var name:String;var value:Double}
struct ScheduleState:Codable {
    var version:Int;var settings:ScheduleSettings;var days:[ScheduleDay];var candidates:[ScheduleCandidate];var issues:[ScheduleIssue]
    var totalCost:Double;var rainy:Bool;var source:String;var radar:[ScheduleRadar]
}
@MainActor final class ScheduleEditorModel:ObservableObject {
    @Published var trip:Trip?;@Published var state:ScheduleState?;@Published var busy=false;@Published var error:String?
    @Published var notice:String?;@Published var dayIndex=0;@Published var dirty=false
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
    private var pendingSnapshot:Trip?
    private var lastNotice:String?
    private var history:[Trip]=[];private var job:Task<Void,Never>?;private var generation=0
    var seed:Trip?
    func prepare(_ value:Trip) {guard seed == nil else{return};seed=value;trip=value;state=value.schedule;if state == nil {recompute(regenerate:true)}}
    func recompute(regenerate:Bool=false) {
        guard let trip else{return}
        job?.cancel();generation+=1;let token=generation;busy=true;dirty=true
        var body:[String:Any]=[:]
        do {
            body["trip"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(trip))
            if let state {
                body["settings"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(state.settings));body["version"]=state.version;body["rainy"]=state.rainy
                if !regenerate {body["days"]=try JSONSerialization.jsonObject(with:JSONEncoder().encode(state.days))}
            }
            let data=try JSONSerialization.data(withJSONObject:body)
            job=Task {
                do {
                    var request=URLRequest(url:URL(string:"http://127.0.0.1:8787/schedule/editor")!);request.httpMethod="POST";request.httpBody=data;request.timeoutInterval=30;request.setValue("application/json",forHTTPHeaderField:"Content-Type")
                    let (response,http)=try await loader(request)
                    guard (http as? HTTPURLResponse)?.statusCode == 200 else {let message=(try? JSONSerialization.jsonObject(with:response)) as? [String:Any];throw NSError(domain:"schedule",code:1,userInfo:[NSLocalizedDescriptionKey:message?["error"] as? String ?? "試算失敗，草案保留，可重試。"])}
                    let result=try JSONDecoder().decode(Trip.self,from:response)
                    guard !Task.isCancelled,token==generation else{return}
                    self.trip=result;self.state=result.schedule;self.busy=false;self.dirty=false
                    let limits=result.schedule?.issues.filter{["steps","budget","total-budget","time-budget"].contains($0.code)} ?? []
                    if !limits.isEmpty {let message=limits.map(\.message).joined(separator:"\n");if message != self.lastNotice {self.notice=message};self.lastNotice=message}else{self.lastNotice=nil}
                }catch {guard !Task.isCancelled,token==generation else{return};self.busy=false;self.error=error.localizedDescription}
            }
        }catch {busy=false;self.error=error.localizedDescription}
    }
    func edit(_ change:(inout ScheduleState)->Void) {
        guard var value=state else{return};if var snapshot=pendingSnapshot ?? trip {if pendingSnapshot == nil {snapshot.schedule=value};history.append(snapshot);if history.count>25{history.removeFirst()}};pendingSnapshot=nil
        change(&value);state=value;recompute()
    }
    func retime(_ id:String,start:Int,duration:Int) {
        guard let a=state?.days[dayIndex].activities.first(where:{$0.id==id}),a.locked != true else{return}
        edit {state in
            let i=state.days[dayIndex].activities.firstIndex(where:{$0.id==id})!
            state.days[dayIndex].activities[i].fixedStart=max(0,min(1439,start))
            state.days[dayIndex].activities[i].scheduledStart=max(0,min(1439,start))
            state.days[dayIndex].activities[i].duration=max(5,min(240,duration))
            state.days[dayIndex].activities=state.days[dayIndex].activities.enumerated().sorted {
                let l=$0.element.fixedStart ?? $0.element.scheduledStart,r=$1.element.fixedStart ?? $1.element.scheduledStart
                return l==r ? $0.offset<$1.offset:l<r
            }.map(\.element)
        }
    }
    func reorder(_ id:String,before target:String) {
        guard let day=state?.days[dayIndex],let source=day.activities.firstIndex(where:{$0.id==id}),let dest=day.activities.firstIndex(where:{$0.id==target}),source != dest else{return}
        edit {let a=$0.days[dayIndex].activities.remove(at:source);$0.days[dayIndex].activities.insert(a,at:min(dest,$0.days[dayIndex].activities.count))}
    }
    func move(_ id:String,by offset:Int) {guard let index=state?.days[dayIndex].activities.firstIndex(where:{$0.id==id}),let count=state?.days[dayIndex].activities.count,index+offset>=0,index+offset<count else{return};edit{$0.days[dayIndex].activities.swapAt(index,index+offset)}}
    func undo(){guard let previous=history.popLast() else{return};job?.cancel();trip=previous;state=previous.schedule;recompute()}
    func restore(){guard let seed else{return};job?.cancel();history=[];trip=seed;state=seed.schedule;recompute(regenerate:state == nil)}
    func stop(){job?.cancel();generation+=1;busy=false}
    func toggleMust(_ candidate:ScheduleCandidate) {
        guard var trip else{return}
        var before=trip;before.schedule=state;pendingSnapshot=before
        if trip.profile.mustVisit.contains(candidate.id){trip.profile.mustVisit.removeAll{$0==candidate.id}}else{trip.profile.mustVisit.append(candidate.id)}
        self.trip=trip
        if !(state?.days.flatMap(\.activities).contains{$0.placeId==candidate.id} ?? true){add(candidate)}else{edit{_ in}}
    }
    func addFacility(_ kind:String) {
        guard let day=state?.days[dayIndex] else{return}
        let labels=["meal":"用餐","restroom":"廁所","rest":"休息","bag-drop":"寄放","bag-pickup":"取件"]
        let a=ScheduleActivity(id:UUID().uuidString,kind:kind,name:(labels[kind] ?? kind)+" · 待確認",lat:day.start.lat,lng:day.start.lng,duration:kind=="meal" ? 45:10,cost:kind=="meal" ? (state?.settings.mealCost ?? 25):0,confirmed:false,openMinute:0,closeMinute:1440,arrival:"",departure:"",scheduledStart:day.startMinute,travelMinutes:0)
        edit{$0.days[dayIndex].activities.append(a)}
    }
    func add(_ candidate:ScheduleCandidate) {
        guard let day=state?.days[dayIndex],!(state?.days.flatMap(\.activities).contains(where:{$0.placeId==candidate.id}) ?? true) else{return}
        let a=ScheduleActivity(id:UUID().uuidString,placeId:candidate.id,kind:"attraction",name:candidate.name,lat:candidate.lat,lng:candidate.lng,duration:candidate.visitMinutes,cost:candidate.cost,arrival:"",departure:"",scheduledStart:day.startMinute,travelMinutes:0,indoor:candidate.indoor)
        edit {let i=$0.days[dayIndex].activities.firstIndex(where:{$0.kind=="stay" || $0.kind=="bag-pickup"}) ?? $0.days[dayIndex].activities.count;$0.days[dayIndex].activities.insert(a,at:i)}
    }
}

struct ScheduleRadarView:View {
    var axes:[ScheduleRadar]
    var body:some View {
        VStack(alignment:.leading) {
            Text("旅行偏好").font(.headline)
            Canvas {context,size in
                let center=CGPoint(x:size.width/2,y:size.height/2),radius=min(size.width,size.height)*0.32
                func point(_ i:Int,_ value:Double)->CGPoint {let angle=Double(i)*2 * .pi/Double(max(1,axes.count)) - .pi/2;return CGPoint(x:center.x+cos(angle)*radius*value,y:center.y+sin(angle)*radius*value)}
                for fraction in [0.25,0.5,0.75,1.0] {var path=Path();for i in axes.indices {let p=point(i,fraction);if i==0{path.move(to:p)}else{path.addLine(to:p)}};path.closeSubpath();context.stroke(path,with:.color(Palette.green.opacity(0.2)),lineWidth:1)}
                var shape=Path();for i in axes.indices {let p=point(i,axes[i].value);if i==0{shape.move(to:p)}else{shape.addLine(to:p)};context.draw(Text(axes[i].name).font(.system(size:10)),at:point(i,1.3))};shape.closeSubpath();context.fill(shape,with:.color(Palette.green.opacity(0.22)));context.stroke(shape,with:.color(Palette.green),lineWidth:2)
            }.frame(height:205).accessibilityLabel(axes.map{$0.name+" \(Int($0.value*100))"}.joined(separator:"，"))
        }
    }
}
@MainActor struct ScheduleEditorView:View {
    @EnvironmentObject var store:AppStore
    @StateObject private var model:ScheduleEditorModel
    init(model:ScheduleEditorModel? = nil){_model=StateObject(wrappedValue:model ?? ScheduleEditorModel())}
    @State private var candidateSheet=false;@State private var settingsSheet=false;@State private var editing:ScheduleActivity?
    @State private var saveWarning=false
    var body:some View {
        NavigationStack {
            ScrollView {
                VStack(alignment:.leading,spacing:16) {
                    if let state=model.state {
                        Text("先建議，再按你的節奏調整").font(.title2.bold())
                        Text(state.source).font(.caption).foregroundStyle(Palette.muted)
                        DisclosureGroup("旅行偏好雷達圖"){ScheduleRadarView(axes:state.radar)}.padding().background(.white,in:RoundedRectangle(cornerRadius:20))
                        HStack {Button("偏好與住宿"){settingsSheet=true};Spacer();Button("景點清單"){candidateSheet=true}}.font(.subheadline.bold())
                        ScrollView(.horizontal,showsIndicators:false){HStack {ForEach(Array(state.days.enumerated()),id:\.offset){i,d in Button("第 \(i+1) 天\n\(d.date)"){model.dayIndex=i}.buttonStyle(.bordered).tint(i==model.dayIndex ? Palette.green:.gray)}}}
                        let day=state.days[min(model.dayIndex,state.days.count-1)]
                        summary(day,state)
                        HStack {Button("還原上一步"){model.undo()};Spacer();Button("回到原建議"){model.restore()}}.font(.caption)
                        if model.busy {ProgressView("更新交通與等待時間…").font(.caption)}
                        ForEach(state.issues.filter{$0.day==model.dayIndex || $0.day == -1}) {issue in Label(issue.message,systemImage:issue.blocking ? "exclamationmark.triangle.fill":"info.circle").font(.caption).foregroundStyle(issue.blocking ? .red:.orange).padding(10).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:12))}
                        Label(day.start.name+" 出發",systemImage:"location.fill").font(.subheadline.bold())
                        ScheduleTimeCanvas(day:day,onChange:{id,start,duration in model.retime(id,start:start,duration:duration)},onEdit:{editing=$0},onAdd:{candidateSheet=true})
                        Label(day.end.name+" · "+day.finishTime,systemImage:"flag.checkered").font(.subheadline.bold())
                        if state.rainy {Text("下雨測試：戶外活動已標示，可從景點清單替換室內地點。").font(.caption)}
                        Button(state.rainy ? "切回晴天示例":"測試下雨影響"){model.edit{$0.rainy.toggle()}}
                        Button("依目前偏好重新建議（會重選活動）"){model.recompute(regenerate:true)}.font(.caption)
                    }else if model.busy {ProgressView("建立多日首版行程…")}else {Text(model.error ?? "準備行程編輯器");Button("重試"){model.recompute(regenerate:true)}}
                }.padding(20)
            }.background(Palette.cream).navigationTitle("我的時間軸").navigationBarTitleDisplayMode(.inline)
            .toolbar {ToolbarItem(placement:.topBarLeading){Button("取消"){model.stop();store.proposal=nil}}}
            .safeAreaInset(edge:.bottom){Button("確認並保存"){saveWarning=true}.buttonStyle(.borderedProminent).tint(Palette.green).disabled(model.busy || model.dirty || model.state == nil || model.state?.issues.contains(where:\.blocking)==true).padding().frame(maxWidth:.infinity).background(Palette.cream)}
        }
        .task {if let trip=store.proposal {model.prepare(trip)}}
        .onDisappear{model.stop()}
        .sheet(isPresented:$candidateSheet){candidates}
        .sheet(isPresented:$settingsSheet){ScheduleSettingsView(model:model)}
        .sheet(item:$editing){activity in ScheduleActivityView(activity:activity,days:model.state?.days.count ?? 1,currentDay:model.dayIndex,onSave:{updated,target in
            model.edit{state in let source=model.dayIndex;state.days[source].activities.removeAll{$0.id==updated.id};let dest=min(target,state.days.count-1);state.days[dest].activities.append(updated)}
        },onReplace:{updated in model.edit{state in if let index=state.days[model.dayIndex].activities.firstIndex(where:{$0.id==updated.id}){state.days[model.dayIndex].activities[index]=updated}}},onDelete:{model.edit{$0.days[model.dayIndex].activities.removeAll{$0.id==activity.id}}})}
        .alert("需要留意",isPresented:Binding(get:{model.notice != nil},set:{if !$0{model.notice=nil}})){Button("繼續調整"){}}message:{Text(model.notice ?? "")}
        .alert("試算未完成",isPresented:Binding(get:{model.error != nil},set:{if !$0{model.error=nil}})){Button("重試"){model.recompute()};Button("保留草案",role:.cancel){}}message:{Text(model.error ?? "")}
        .confirmationDialog("保存目前草案？估算與待確認資料會一併保存。",isPresented:$saveWarning,titleVisibility:.visible){Button("確認保存"){if let trip=model.trip {store.proposal=trip;Task{await store.acceptProposal()}}}}
    }
    func summary(_ day:ScheduleDay,_ state:ScheduleState)->some View {
        VStack(alignment:.leading,spacing:7){Text("完整行程額度").font(.headline);Text("步數 \(day.estimatedSteps) / \(state.settings.maxSteps)");Text(String(format:"花費 A$%.2f / %.2f · 全旅程 A$%.2f",day.estimatedCost,state.settings.dailyBudget,state.totalCost));Text("交通 \(day.transportMinutes)＋景點排隊 \(day.queueMinutes)＝\(day.timeUsed) / \(state.settings.timeLimit) 分");Text(day.unknownQueue ? "含未知排隊，剩餘僅供規劃參考":"剩餘 \(max(0,day.timeRemaining)) 分");Text("\(day.startMinute/60):\(String(format:"%02d",day.startMinute%60)) 出發 · \(day.finishTime) 結束")}.font(.caption).padding(16).frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:18))
    }
    var candidates:some View {NavigationStack{List{Section("新增生活安排（地點待確認）"){ForEach(["meal","restroom","rest","bag-drop","bag-pickup"],id:\.self){kind in Button(["meal":"＋用餐","restroom":"＋廁所","rest":"＋休息","bag-drop":"＋寄放","bag-pickup":"＋取件"][kind] ?? kind){model.addFacility(kind)}}};if let state=model.state {ForEach(state.candidates){c in let selected=state.days.flatMap(\.activities).contains{$0.placeId==c.id};Button{if selected {model.edit{$0.days=model.state!.days.map{day in var day=day;day.activities.removeAll{$0.placeId==c.id};return day}}}else{model.add(c)}}label:{HStack{Image(systemName:selected ? "checkmark.circle.fill":"circle");VStack(alignment:.leading){Text(c.name);Text(c.reason).font(.caption);Text("A$\(Int(c.cost)) · 遊覽 \(c.visitMinutes) 分 · 排隊未知").font(.caption2)}}};Button(model.trip?.profile.mustVisit.contains(c.id)==true ? "★ 必去":"☆ 設為必去"){model.toggleMust(c)}.font(.caption)}}}.navigationTitle("景點選擇").toolbar{Button("完成"){candidateSheet=false}}}}
}

struct ScheduleSettingsView:View {
    @ObservedObject var model:ScheduleEditorModel
    @Environment(\.dismiss) var dismiss
    @State private var settings=ScheduleSettings(pace:"balanced",timeLimit:180,restroomInterval:120,mealCost:25,dailyBudget:100,totalBudget:300,maxSteps:10000,luggage:false)
    @State private var stays:[ScheduleStay]=[]
    @State private var windows:[[Int]]=[]
    var body:some View {NavigationStack{Form {
        Section("偏好 · 沿用問答，可在這裡微調") {
            Picker("行程鬆緊",selection:$settings.pace){Text("悠閒").tag("relaxed");Text("適中").tag("balanced");Text("緊湊").tag("packed")}
            TextField("每日步數上限",value:$settings.maxSteps,format:.number)
            TextField("每日交通＋景點等待（分鐘）",value:$settings.timeLimit,format:.number)
            TextField("廁所間隔（分鐘）",value:$settings.restroomInterval,format:.number)
            TextField("每日預算 A$",value:$settings.dailyBudget,format:.number)
            TextField("全旅程預算 A$",value:$settings.totalBudget,format:.number)
            TextField("每餐預留 A$",value:$settings.mealCost,format:.number)
            Toggle("首版需要行李寄放／領取",isOn:$settings.luggage)
            Text("節奏／行李偏好儲存後，按重新建議可重選活動。現有手動順序不會自動覆蓋。餐飲與設施可逐項編輯。").font(.caption)
        }
        ForEach(stays.indices,id:\.self){i in Section("第 \(i+1) 晚住宿（最後一天不收住宿費）") {
            Stepper("當日開始 \(windows[i][0]/60):\(String(format:"%02d",windows[i][0]%60))",value:$windows[i][0],in:0...1380,step:15)
            Stepper("當日結束 \(windows[i][1]/60):\(String(format:"%02d",windows[i][1]%60))",value:$windows[i][1],in:15...1440,step:15)
            TextField("住宿名稱／暫定區域",text:$stays[i].name)
            TextField("緯度",value:$stays[i].lat,format:.number);TextField("經度",value:$stays[i].lng,format:.number)
            TextField("每晚房費 A$（本機單人試算）",value:$stays[i].cost,format:.number)
            Stepper("最早入住 \(stays[i].checkIn/60):\(String(format:"%02d",stays[i].checkIn%60))",value:$stays[i].checkIn,in:0...1380,step:15)
            Stepper("最晚退房 \(stays[i].checkOut/60):\(String(format:"%02d",stays[i].checkOut%60))",value:$stays[i].checkOut,in:0...1380,step:15)
            Text("座標與價格由你確認；此頁不訂房，也不代表可寄放行李。").font(.caption)
        }}
    }.navigationTitle("偏好與住宿").toolbar{ToolbarItem(placement:.topBarLeading){Button("取消"){dismiss()}};ToolbarItem(placement:.topBarTrailing){Button("套用"){model.edit{value in value.settings=settings;for i in stays.indices where i<value.days.count{value.days[i].stay=stays[i];value.days[i].startMinute=windows[i][0];value.days[i].endMinute=windows[i][1]}};dismiss()}}}
    .onAppear{if let state=model.state {settings=state.settings;stays=state.days.map(\.stay);windows=state.days.map{[$0.startMinute,$0.endMinute]}}}}}
}
struct ScheduleActivityView:View {
    @State var activity:ScheduleActivity
    var days:Int;var currentDay:Int=0
    var onSave:(ScheduleActivity,Int)->Void;var onReplace:(ScheduleActivity)->Void;var onDelete:()->Void = {}
    @State private var targetDay=0
    @State private var assumedQueue=false
    @State private var useStart=false
    @Environment(\.dismiss) var dismiss
    var body:some View {NavigationStack{Form {
        Section("活動") {
            Text(activity.name)
            Button("移除此活動",role:.destructive){onDelete();dismiss()}
            if activity.kind != "attraction" {TextField("名稱",text:$activity.name);TextField("緯度",value:$activity.lat,format:.number);TextField("經度",value:$activity.lng,format:.number);TextField("費用 A$",value:$activity.cost,format:.number)}
            Stepper("停留 \(activity.duration) 分",value:$activity.duration,in:5...240,step:5).disabled(activity.locked==true)
            Toggle("指定開始時間",isOn:$useStart)
            if useStart {Stepper("\((activity.fixedStart ?? activity.scheduledStart)/60):\(String(format:"%02d",(activity.fixedStart ?? activity.scheduledStart)%60))",value:Binding(get:{activity.fixedStart ?? activity.scheduledStart},set:{activity.fixedStart=$0}),in:0...1439,step:15)}
            Toggle("鎖定時段與停留",isOn:Binding(get:{activity.locked ?? false},set:{activity.locked=$0;if $0 {useStart=true;activity.fixedStart=activity.fixedStart ?? activity.scheduledStart;activity.fixedDuration=activity.duration}else{activity.fixedDuration=nil}}))
            Picker("日期",selection:$targetDay){ForEach(0..<days,id:\.self){Text("第 \($0+1) 天").tag($0)}}
        }
        if activity.kind=="attraction" {Section("排隊假設 · 不是即時資料") {Toggle("填入估算",isOn:$assumedQueue);if assumedQueue{Stepper("排隊 \(activity.queueMinutes ?? 0) 分",value:Binding(get:{activity.queueMinutes ?? 0},set:{activity.queueMinutes=$0}),in:0...360,step:5)}}}
        else {Section("服務窗口 · 請自行確認") {Stepper("開放 \((activity.openMinute ?? 0)/60):\(String(format:"%02d",(activity.openMinute ?? 0)%60))",value:Binding(get:{activity.openMinute ?? 0},set:{activity.openMinute=$0}),in:0...1440,step:15);Stepper("結束 \((activity.closeMinute ?? 1440)/60):\(String(format:"%02d",(activity.closeMinute ?? 1440)%60))",value:Binding(get:{activity.closeMinute ?? 1440},set:{activity.closeMinute=$0}),in:0...1440,step:15)}}
    }.navigationTitle("活動時間").toolbar{ToolbarItem(placement:.topBarLeading){Button("取消"){dismiss()}};ToolbarItem(placement:.topBarTrailing){Button("套用"){if !useStart {activity.fixedStart=nil;activity.locked=false};if !assumedQueue{activity.queueMinutes=nil};if targetDay==currentDay{onReplace(activity)}else{onSave(activity,targetDay)};dismiss()}}}.onAppear{targetDay=currentDay;assumedQueue=activity.queueMinutes != nil;useStart=activity.fixedStart != nil}}}
}
