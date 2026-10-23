import SwiftUI

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
            Text("拖動 ☰ 移動時段 · 上下把手調整停留時間").font(.caption).foregroundStyle(Palette.muted)
            HStack {Text("卡片長度＝停留時間 · 每格 5 分").font(.caption2);Spacer();Button("縮小"){scale=max(2,scale-1)};Button("放大"){scale=min(6,scale+1)}}.font(.caption)
            ZStack(alignment:.topLeading) {
                ForEach(Array(stride(from:first,through:last,by:30)),id:\.self){minute in
                    HStack(spacing:8){Text(clock(minute)).font(.caption2.monospacedDigit()).foregroundStyle(Palette.muted).frame(width:43,alignment:.leading);Rectangle().fill(Palette.green.opacity(0.16)).frame(height:1)}.offset(y:y(minute)-6).allowsHitTesting(false)
                }
                ForEach(Array(day.activities.enumerated()),id:\.element.id){index,a in
                    let previous=index==0 ? day.startMinute:day.activities[index-1].scheduledStart+(day.activities[index-1].queueMinutes ?? 0)+day.activities[index-1].duration
                    let travelEnd=previous+a.travelMinutes
                    if a.travelMinutes>0 {
                        band("步行交通約 \(a.travelMinutes) 分",start:previous,length:a.travelMinutes,color:.blue)
                    }
                    if a.scheduledStart>travelEnd {
                        let gap=a.scheduledStart-travelEnd
                        Button(action:onAdd){VStack(spacing:3){Text("＋ 空檔 \(gap) 分").font(.caption);if gap>=20{Text("可保留休息／彈性安排").font(.caption2)}}.frame(maxWidth:.infinity).frame(height:CGFloat(gap)*scale).background(.white.opacity(0.6),in:RoundedRectangle(cornerRadius:10)).overlay(RoundedRectangle(cornerRadius:10).strokeBorder(Palette.muted.opacity(0.35),style:StrokeStyle(lineWidth:1,dash:[4,4])))}.padding(.leading,58).offset(y:y(travelEnd))
                    }
                    if let queue=a.queueMinutes,queue>0 {band("排隊假設 \(queue) 分",start:a.scheduledStart,length:queue,color:.orange)}
                    block(a).padding(.leading,58).offset(y:y(edit(a).start+(a.queueMinutes ?? 0))).zIndex(dragging==a.id ? 10:1)
                }
            }.frame(height:CGFloat(last-first)*scale+20,alignment:.top)
            Text("交通與排隊獨立占用時間；空檔可保留。點卡片可編輯短活動、鎖定或跨日移動。").font(.caption2).foregroundStyle(Palette.muted)
        }
    }
    private func band(_ text:String,start:Int,length:Int,color:Color)->some View {
        HStack(spacing:4){Rectangle().fill(color.opacity(0.5)).frame(width:3);if CGFloat(length)*scale>=18{Text(text).font(.system(size:10)).foregroundStyle(color).lineLimit(1)};Spacer()}.frame(height:CGFloat(length)*scale).padding(.leading,60).offset(y:y(start)).allowsHitTesting(false)
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
                Text(a.name).font(.system(size:height<70 ? 11:15,weight:.bold)).lineLimit(height<70 ? 1:2)
                if height>=70 {Text("\(clock(value.start+(a.queueMinutes ?? 0)))–\(clock(value.start+(a.queueMinutes ?? 0)+value.duration)) · \(value.duration) 分").font(.caption.monospacedDigit())}
                if height>=120 {Label(a.label,systemImage:a.kind=="meal" ? "fork.knife":"mappin.and.ellipse").font(.caption);if a.kind=="attraction" && a.queueMinutes==nil{Text("排隊時間未知，尚未計入").font(.caption2)}}
            }.frame(maxWidth:.infinity,alignment:.leading).contentShape(Rectangle()).onTapGesture{onEdit(a)}
            Text(a.locked==true ? "🔒":"☰").frame(width:36,height:max(20,height-4)).contentShape(Rectangle()).highPriorityGesture(gesture(a,0)).accessibilityLabel("拖動活動排序").accessibilityIdentifier("schedule-move-\(a.id)")
        }.padding(.horizontal,10).frame(height:height).background(color.opacity(0.17),in:RoundedRectangle(cornerRadius:12)).overlay(RoundedRectangle(cornerRadius:12).stroke(color,lineWidth:dragging==a.id ? 2:1))
        .overlay(alignment:.topLeading){if a.locked != true{handle(a,part:-1,label:"拉上緣調整").padding(.leading,18).offset(y:-12)}}
        .overlay(alignment:.bottomTrailing){if a.locked != true{handle(a,part:1,label:"拉下緣調整").padding(.trailing,48).offset(y:12)}}
    }
    private func handle(_ a:ScheduleActivity,part:Int,label:String)->some View {
        Image(systemName:part<0 ? "chevron.up":"chevron.down").font(.system(size:11,weight:.bold)).foregroundStyle(Palette.green).frame(width:38,height:24).background(.white,in:Capsule()).overlay(Capsule().stroke(Palette.green.opacity(0.4))).contentShape(Rectangle()).highPriorityGesture(gesture(a,part)).accessibilityLabel(label).accessibilityIdentifier("schedule-\(part<0 ? "start":"end")-\(a.id)")
    }
}
