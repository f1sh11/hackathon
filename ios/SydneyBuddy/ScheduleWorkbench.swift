import SwiftUI

// Drag closures share current geometry instead of retaining an earlier view snapshot.
@MainActor final class ScheduleToolboxDragSession:ObservableObject {
    @Published var candidate:ScheduleCandidate?
    @Published var point=CGPoint.zero
    var day:Int?
    var canvas=ScheduleCanvasGeometry()
    var viewport=CGRect.zero
}

@MainActor struct ScheduleWorkbench:View {
    @ObservedObject var model:ScheduleEditorModel
    var onEdit:(ScheduleActivity)->Void
    var onAdd:()->Void
    @State private var detail:ScheduleCandidate?
    @State private var discover=false
    @StateObject private var dragSession=ScheduleToolboxDragSession()
    private var dragging:ScheduleCandidate? {get{dragSession.candidate} nonmutating set{dragSession.candidate=newValue}}
    private var pointer:CGPoint {get{dragSession.point} nonmutating set{dragSession.point=newValue}}
    private var dragDay:Int? {get{dragSession.day} nonmutating set{dragSession.day=newValue}}
    private var canvas:ScheduleCanvasGeometry {get{dragSession.canvas} nonmutating set{dragSession.canvas=newValue}}
    private var viewport:CGRect {get{dragSession.viewport} nonmutating set{dragSession.viewport=newValue}}
    private func dropMinute()->Int? {
        guard let day=model.state?.days[model.dayIndex],dragDay==model.dayIndex else{return nil}
        return ScheduleDropTime.minute(at:pointer,viewport:viewport,canvas:canvas,start:day.startMinute,end:day.endMinute)
    }
    private func toolboxDrag(_ candidate:ScheduleCandidate)->some Gesture {
        let sourceDay=model.dayIndex
        return DragGesture(minimumDistance:3,coordinateSpace:.named("scheduleWorkbench"))
            .onChanged {value in if dragging==nil{dragging=candidate;dragDay=model.dayIndex};pointer=value.location}
            .onEnded {value in
                pointer=value.location
                let day=model.state!.days[model.dayIndex]
                let minute=sourceDay==model.dayIndex ? ScheduleDropTime.minute(at:value.location,viewport:dragSession.viewport,canvas:dragSession.canvas,start:day.startMinute,end:day.endMinute):nil
                dragging=nil;dragDay=nil
                if let minute{model.place(candidate,at:minute)}
            }
    }
    @ObservedObject private var toolbox=PoiToolbox.shared
    var body:some View {
        GeometryReader {geometry in
            if let state=model.state {
                let selected=Dictionary(state.days.enumerated().flatMap {i,day in day.activities.compactMap {a in a.placeId.map{($0,i)}}},uniquingKeysWith:{first,_ in first})
                ZStack(alignment:.topLeading) {
                HStack(alignment:.top,spacing:10) {
                    VStack(alignment:.leading,spacing:8) {
                        Text("景點工具箱").font(.subheadline.bold())
                        Button("地圖找地點"){discover=true}.font(.caption.bold())
                        Text("依推薦排序\n按住 ☰ 拖到右側").font(.caption2).foregroundStyle(Palette.muted)
                        ScrollView {
                            VStack(spacing:10) {
                                ForEach(Array(state.candidates.enumerated()),id:\.element.id){index,c in
                                    VStack(alignment:.leading,spacing:7) {
                                        Text(String(format:"%02d",index+1)).font(.caption2.bold()).foregroundStyle(Palette.green)
                                        Text(c.name).font(.system(size:12,weight:.semibold)).fixedSize(horizontal:false,vertical:true)
                                        Text(c.source=="OpenStreetMap" ? "\(c.visitMinutes) 分 · 費用未知":"\(c.visitMinutes) 分 · A$\(Int(c.cost))").font(.system(size:10)).foregroundStyle(Palette.muted)
                                        if let d=selected[c.id] {Text("第 \(d+1) 天已排").font(.system(size:10)).foregroundStyle(Palette.green)}
                                        let locked=state.days[model.dayIndex].activities.contains{$0.placeId==c.id && $0.locked==true}
                                        if locked{Text("已鎖定").font(.system(size:10)).foregroundStyle(Palette.muted)}
                                        HStack(spacing:0) {
                                            Button{detail=c}label:{Image(systemName:"info.circle").frame(width:38,height:36)}.accessibilityLabel("介紹：\(c.name)")
                                            Spacer(minLength:0)
                                            if !model.busy && !locked && (selected[c.id]==nil || selected[c.id]==model.dayIndex) {
                                                Image(systemName:"line.3.horizontal").frame(width:32,height:36).contentShape(Rectangle()).highPriorityGesture(toolboxDrag(c)).accessibilityIdentifier("toolbox-drag-\(c.id)").accessibilityLabel("拖入時間軸：\(c.name)")
                                            }
                                        }
                                    }.padding(9).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:13)).overlay(RoundedRectangle(cornerRadius:13).stroke(Palette.green.opacity(0.12)))
                                }
                            }.padding(.bottom,20)
                        }
                    }.frame(width:min(150,geometry.size.width*0.34))
                    Divider()
                    VStack(alignment:.leading,spacing:8) {
                        HStack(spacing:4){ForEach(Array(state.days.enumerated()),id:\.offset){i,d in Button{model.dayIndex=i}label:{VStack(spacing:3){Text("第 \(i+1) 天");Text(d.date).font(.system(size:9)).lineLimit(1).minimumScaleFactor(0.75)}.frame(maxWidth:.infinity).padding(.vertical,7).background(i==model.dayIndex ? Palette.mint:Color.white,in:RoundedRectangle(cornerRadius:12))}.font(.system(size:10))}}
                        ScrollView {
                        ScheduleTimeCanvas(day:state.days[model.dayIndex],onChange:{id,start,duration in model.retime(id,start:start,duration:duration)},onEdit:onEdit,onAdd:onAdd,onPlace:{id,minute in if let c=model.state?.candidates.first(where:{$0.id==id}){model.place(c,at:minute)}},onGeometry:{canvas=$0},dropHint:dragging==nil ? nil:dropMinute().map{"放到 \(String(format:"%02d:%02d",$0/60,$0%60))"} ?? "拖至右側時段").padding(.top,4).padding(.bottom,20)
                    }.frame(maxWidth:.infinity).background(GeometryReader{proxy in
                        let frame=proxy.frame(in:.named("scheduleWorkbench"))
                        Color.clear.onAppear{viewport=frame}.onChange(of:frame){_,value in viewport=value}
                    })
                    }
                }
                if let dragging {
                    VStack(alignment:.leading,spacing:4){Text(dragging.name).font(.caption.bold());Text(dropMinute().map{"放到 \(String(format:"%02d:%02d",$0/60,$0%60))"} ?? "拖至右側時間軸").font(.caption2)}
                        .padding(10).frame(width:140).background(Palette.mint,in:RoundedRectangle(cornerRadius:12)).overlay(RoundedRectangle(cornerRadius:12).stroke(Palette.green)).position(x:pointer.x,y:pointer.y-42).allowsHitTesting(false).zIndex(20)
                }
                }
            }
        }.frame(height:600).coordinateSpace(name:"scheduleWorkbench")
        .onChange(of:model.dayIndex){_,_ in dragging=nil;dragDay=nil}
        .onReceive(toolbox.$places){model.importPlaces($0)}
        .onChange(of:model.busy){_,busy in if !busy{model.importPlaces(toolbox.places)}}
        .sheet(isPresented:$discover){PoiDiscoveryView{p in toolbox.add(p);model.importPlaces([p])}}
        .sheet(item:$detail){c in SchedulePlaceDetail(candidate:c)}
    }
}
struct SchedulePlaceDetail:View {
    var candidate:ScheduleCandidate
    @Environment(\.dismiss) var dismiss
    private func time(_ n:Int)->String {String(format:"%02d:%02d",n/60,n%60)}
    var body:some View {
        NavigationStack {
            ScrollView {
                VStack(alignment:.leading,spacing:20) {
                    Image(systemName:candidate.indoor ? "building.2.crop.circle.fill":"leaf.circle.fill").font(.system(size:54)).foregroundStyle(Palette.green)
                    Text(candidate.name).font(.title2.bold())
                    Text(candidate.category+" · "+(candidate.source=="OpenStreetMap" ? (candidate.indoor ? "室內（OSM 標記）":"室內外未確認"):(candidate.indoor ? "室內":"戶外"))).font(.subheadline).foregroundStyle(Palette.muted)
                    Text(candidate.description ?? "此草案尚未附上景點介紹，可重新試算以更新資料。").lineSpacing(6)
                    Text("為你推薦").font(.headline)
                    Text(candidate.reason)
                    VStack(alignment:.leading,spacing:10) {
                        Text("建議停留 \(candidate.visitMinutes) 分鐘")
                        Text(candidate.source=="OpenStreetMap" ? "費用與排隊時間未知":"規劃預留 A$\(Int(candidate.cost)) · 排隊時間未知")
                        if let text=candidate.openingHoursText,!text.isEmpty{Text("OSM 營業時間標記："+text).font(.caption)}
                        if let open=candidate.openMinute,let close=candidate.closeMinute {Text("示範開放時段 \(time(open))–\(time(close))")}
                        if let note=candidate.note {Text(note).font(.caption)}
                    }.padding().frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:14))
                    Text(candidate.source=="OpenStreetMap" ? "資料來源：© OpenStreetMap contributors · 社群資料，費用及營業資訊未確認。":"本機景點資料；費用與開放時間是規劃示例，出發前請確認當日資訊。").font(.caption).foregroundStyle(Palette.muted)
                }.padding(24)
            }.navigationTitle("景點介紹").navigationBarTitleDisplayMode(.inline).toolbar{Button("完成"){dismiss()}}
        }
    }
}
