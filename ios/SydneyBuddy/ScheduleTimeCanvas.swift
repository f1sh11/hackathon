import SwiftUI
import UniformTypeIdentifiers

// Timeline coordinates are minutes, independent of zoom or screen density.
struct ScheduleTimeEdit:Equatable {
    var start:Int;var duration:Int
    static func drag(start:Int,duration:Int,queue:Int,delta:Double,edge:Int)->Self {
        let shift=Int((delta/5).rounded())*5
        if edge==0 {return Self(start:max(0,min(1439,start+shift)),duration:duration)}
        if edge<0 {
            let change=max(-start,min(duration-5,max(duration-240,shift)))
            return Self(start:start+change,duration:duration-change)
        }
        return Self(start:start,duration:max(5,min(240,duration+shift)))
    }
}

struct ScheduleTimeCanvas:View {
    var day:ScheduleDay
    var onChange:(String,Int,Int)->Void
    var onEdit:(ScheduleActivity)->Void
    var onAdd:()->Void
    var onPlace:(String,Int)->Void = {_,_ in}
    @State private var targeted=false
    @State private var scale:CGFloat=3
    @State private var dragging:String?
    @State private var edge=0
    @State private var delta:CGFloat=0
    private var first:Int {min(day.startMinute,day.activities.map(\.scheduledStart).min() ?? day.startMinute)/30*30}
    private var last:Int {let end=day.activities.map{$0.scheduledStart+($0.queueMinutes ?? 0)+$0.duration}.max() ?? day.endMinute;return ((max(day.endMinute,end)+29)/30)*30}
    private func clock(_ m:Int)->String {String(format:"%02d:%02d",m/60,m%60)}
    private func y(_ m:Int)->CGFloat {CGFloat(m-first)*scale}
    private func edit(_ a:ScheduleActivity)->ScheduleTimeEdit {
        ScheduleTimeEdit.drag(start:a.scheduledStart,duration:a.duration,queue:a.queueMinutes ?? 0,delta:Double(dragging==a.id ? delta/scale:0),edge:edge)
    }
    var body:some View {
        VStack(alignment:.leading,spacing:14) {
            Text(targeted ? "放開以加入這個時段":"時間軸 · 每格 5 分").font(.caption.bold()).foregroundStyle(Palette.green)
            HStack {Text("停留時間").font(.caption2);Spacer();Button("−"){scale=max(2,scale-1)}.accessibilityLabel("縮小時間軸");Button("＋"){scale=min(6,scale+1)}.accessibilityLabel("放大時間軸")}.font(.caption)

            ZStack(alignment:.topLeading) {
                ForEach(Array(stride(from:first,through:last,by:30)),id:\.self){minute in
                    HStack(spacing:4){Text(clock(minute)).font(.caption2.monospacedDigit()).foregroundStyle(Palette.muted).frame(width:32,alignment:.leading);Rectangle().fill(Palette.green.opacity(0.16)).frame(height:1)}.offset(y:y(minute)-6).allowsHitTesting(false)
                }
                ForEach(Array(day.activities.enumerated()),id:\.element.id){index,a in
                    let previous=index==0 ? day.startMinute:day.activities[index-1].scheduledStart+(day.activities[index-1].queueMinutes ?? 0)+day.activities[index-1].duration
                    let travelEnd=previous+a.travelMinutes
                    if a.travelMinutes>0 {
                        band("步行 \(a.travelMinutes) 分",start:previous,length:a.travelMinutes,color:.blue)
                    }
                    if a.scheduledStart>travelEnd {
                        let gap=a.scheduledStart-travelEnd
                        Button(action:onAdd){VStack(spacing:3){Text("＋ 空檔 \(gap) 分").font(.caption);if gap>=20{Text("彈性時間").font(.caption2)}}.frame(maxWidth:.infinity).frame(height:CGFloat(gap)*scale).background(.white.opacity(0.6),in:RoundedRectangle(cornerRadius:10)).overlay(RoundedRectangle(cornerRadius:10).strokeBorder(Palette.muted.opacity(0.35),style:StrokeStyle(lineWidth:1,dash:[4,4])))}.padding(.leading,40).offset(y:y(travelEnd))
                    }
                    if let queue=a.queueMinutes,queue>0 {band("排隊假設 \(queue) 分",start:a.scheduledStart,length:queue,color:.orange)}
                    block(a).padding(.leading,40).offset(y:y(edit(a).start+(a.queueMinutes ?? 0))).zIndex(dragging==a.id ? 10:1)
                }
            }.frame(height:CGFloat(last-first)*scale+20,alignment:.top)
            .background(targeted ? Palette.green.opacity(0.08):Color.clear)
            .contentShape(Rectangle())
            .onDrop(of:[UTType.text],isTargeted:$targeted){providers,location in
                let minute=first+Int((Double(location.y/scale)/5).rounded())*5
                guard location.x>=40,minute>=day.startMinute,minute<day.endMinute,let provider=providers.first else{return false}
                _=provider.loadObject(ofClass:NSString.self){value,_ in
                    guard let id=value as? String else{return}
                    Task {@MainActor in onPlace(id,minute)}
                }
                return true
            }
            Text("點卡片編輯 · 拖 ☰ 移動\n上下把手調整停留").font(.caption2).foregroundStyle(Palette.muted)
        }
    }
    private func band(_ text:String,start:Int,length:Int,color:Color)->some View {
        HStack(spacing:4){Rectangle().fill(color.opacity(0.5)).frame(width:3);if CGFloat(length)*scale>=18{Text(text).font(.system(size:10)).foregroundStyle(color).lineLimit(1)};Spacer()}.frame(height:CGFloat(length)*scale).padding(.leading,42).offset(y:y(start)).allowsHitTesting(false)
    }
    private func gesture(_ a:ScheduleActivity,_ part:Int)->some Gesture {
        DragGesture(minimumDistance:2).onChanged{value in guard a.locked != true else{return};dragging=a.id;edge=part;delta=value.translation.height}.onEnded{value in
            guard a.locked != true else{return}
            let result=ScheduleTimeEdit.drag(start:a.scheduledStart,duration:a.duration,queue:a.queueMinutes ?? 0,delta:Double(value.translation.height/scale),edge:part)
            dragging=nil;delta=0
            if result.start != a.scheduledStart || result.duration != a.duration {onChange(a.id,result.start,result.duration)}
        }
    }
    private func block(_ a:ScheduleActivity)->some View {
        let value=edit(a),height=CGFloat(value.duration)*scale
        let color:Color=a.kind=="meal" ? .blue:Palette.green
        return HStack(spacing:4) {
            VStack(alignment:.leading,spacing:5) {
                Text(a.name).font(.system(size:height<70 ? 10:12,weight:.bold)).lineLimit(height<70 ? 1:2)
                if height>=70 {Text("\(clock(value.start+(a.queueMinutes ?? 0)))–\(clock(value.start+(a.queueMinutes ?? 0)+value.duration)) · \(value.duration) 分").font(.caption.monospacedDigit())}
                if height>=120 {Label(a.label,systemImage:a.kind=="meal" ? "fork.knife":"mappin.and.ellipse").font(.caption);if a.kind=="attraction" && a.queueMinutes==nil{Text("排隊時間未知，尚未計入").font(.caption2)}}
            }.frame(maxWidth:.infinity,alignment:.leading).contentShape(Rectangle()).onTapGesture{onEdit(a)}
            Text(a.locked==true ? "🔒":"☰").frame(width:24,height:max(20,height-4)).contentShape(Rectangle()).highPriorityGesture(gesture(a,0)).accessibilityLabel("拖動活動排序").accessibilityIdentifier("schedule-move-\(a.id)")
        }.padding(.horizontal,7).frame(height:height).background(color.opacity(0.17),in:RoundedRectangle(cornerRadius:12)).overlay(RoundedRectangle(cornerRadius:12).stroke(color,lineWidth:dragging==a.id ? 2:1))
        .overlay(alignment:.topLeading){if a.locked != true{handle(a,part:-1,label:"拉上緣調整").padding(.leading,4).offset(y:-12)}}
        .overlay(alignment:.bottomTrailing){if a.locked != true{handle(a,part:1,label:"拉下緣調整").padding(.trailing,26).offset(y:12)}}
    }
    private func handle(_ a:ScheduleActivity,part:Int,label:String)->some View {
        Image(systemName:part<0 ? "chevron.up":"chevron.down").font(.system(size:11,weight:.bold)).foregroundStyle(Palette.green).frame(width:28,height:24).background(.white,in:Capsule()).overlay(Capsule().stroke(Palette.green.opacity(0.4))).contentShape(Rectangle()).highPriorityGesture(gesture(a,part)).accessibilityLabel(label).accessibilityIdentifier("schedule-\(part<0 ? "start":"end")-\(a.id)")
    }
}
