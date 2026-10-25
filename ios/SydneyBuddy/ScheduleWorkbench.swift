import SwiftUI

// Drag closures share current geometry instead of retaining an earlier view snapshot.
@MainActor final class ScheduleToolboxDragSession:ObservableObject {
    @Published var candidate:ScheduleCandidate?
    @Published var point=CGPoint.zero
    var day:Int?
    var dayFrames:[Int:CGRect]=[:]
    @Published var hoverDay:Int?
    private var hoverTask:Task<Void,Never>?
    func hover(at point:CGPoint,currentDay:Int,onSwitch:@escaping(Int)->Void) {
        let target=dayFrames.first{$0.value.contains(point)}?.key
        let next=target == currentDay ? nil:target
        guard next != hoverDay else{return}
        hoverTask?.cancel();hoverDay=next
        guard let next else{return}
        hoverTask=Task { [weak self] in
            do {try await Task.sleep(nanoseconds:450_000_000)} catch{return}
            guard let self,self.candidate != nil,self.hoverDay==next else{return}
            onSwitch(next)
        }
    }
    func finish(){hoverTask?.cancel();hoverTask=nil;hoverDay=nil;candidate=nil;day=nil}
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
        return DragGesture(minimumDistance:3,coordinateSpace:.named("scheduleWorkbench"))
            .onChanged {value in
                if dragging==nil{dragging=candidate;dragDay=model.dayIndex}
                pointer=value.location
                dragSession.hover(at:value.location,currentDay:model.dayIndex){index in model.dayIndex=index;dragDay=index}
            }
            .onEnded {value in
                pointer=value.location
                let day=model.state!.days[model.dayIndex]
                let minute=ScheduleDropTime.minute(at:value.location,viewport:dragSession.viewport,canvas:dragSession.canvas,start:day.startMinute,end:day.endMinute)
                dragSession.finish()
                if let minute{model.place(candidate,at:minute,moveAcrossDays:true)}
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
                        Text("依推薦排序\n按住 ☰ 拖到日期切換\n再拖入右側時段").font(.caption2).foregroundStyle(Palette.muted)
                        ScrollView {
                            VStack(spacing:10) {
                                ForEach(Array(state.candidates.filter{p in !p.importedPoi || toolbox.places.contains(where:{$0.id==p.id})}.enumerated()),id:\.element.id){index,c in
                                    VStack(alignment:.leading,spacing:7) {
                                        Text(String(format:"%02d",index+1)).font(.caption2.bold()).foregroundStyle(Palette.green)
                                        Text(c.name).font(.system(size:12,weight:.semibold)).fixedSize(horizontal:false,vertical:true)
                                        Text(c.importedPoi ? "\(c.visitMinutes) 分 · 費用未知":"\(c.visitMinutes) 分 · A$\(Int(c.cost))").font(.system(size:10)).foregroundStyle(Palette.muted)
                                        if let d=selected[c.id] {Text("第 \(d+1) 天已排").font(.system(size:10)).foregroundStyle(Palette.green)}
                                        let locked=state.days.flatMap(\.activities).contains{$0.placeId==c.id && $0.locked==true}
                                        if locked{Text("已鎖定").font(.system(size:10)).foregroundStyle(Palette.muted)}
                                        HStack(spacing:0) {
                                            Button{detail=c}label:{Image(systemName:"info.circle").frame(width:38,height:36)}.accessibilityLabel("介紹：\(c.name)")
                                            Spacer(minLength:0)
                                            if !model.busy && !locked {
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
                        ScrollView(.horizontal,showsIndicators:false){HStack(spacing:4){ForEach(Array(state.days.enumerated()),id:\.offset){i,d in Button{model.dayIndex=i}label:{VStack(spacing:3){Text("第 \(i+1) 天");Text(d.date).font(.system(size:9)).lineLimit(1).minimumScaleFactor(0.75)}.frame(width:76).padding(.vertical,7).background(i==model.dayIndex ? Palette.mint:Color.white,in:RoundedRectangle(cornerRadius:12))}.font(.system(size:10)).accessibilityIdentifier("schedule-day-\(i)")
                            .overlay(RoundedRectangle(cornerRadius:12).stroke(dragSession.hoverDay==i ? Palette.green:Color.clear,lineWidth:2))
                            .background(GeometryReader{proxy in
                                let frame=proxy.frame(in:.named("scheduleWorkbench"))
                                Color.clear.onAppear{dragSession.dayFrames[i]=frame}.onChange(of:frame){_,value in dragSession.dayFrames[i]=value}
                            })}}}
                        ScrollView {
                        ScheduleTimeCanvas(day:state.days[model.dayIndex],onChange:{id,start,duration in model.retime(id,start:start,duration:duration)},onEdit:onEdit,onAdd:onAdd,onPlace:{id,minute in if let c=model.state?.candidates.first(where:{$0.id==id}){model.place(c,at:minute)}},onGeometry:{canvas=$0},dropHint:dragging==nil ? nil:dropMinute().map{"放到 \(String(format:"%02d:%02d",$0/60,$0%60))"} ?? "拖至右側時段").padding(.top,4).padding(.bottom,20)
                    }.id(model.dayIndex).frame(maxWidth:.infinity).background(GeometryReader{proxy in
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
        .onChange(of:model.dayIndex){_,index in dragDay=index}
        .onDisappear{dragSession.finish()}
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
                    Text(candidate.category+" · "+(candidate.importedPoi ? (candidate.indoor ? "室內（來源標記）":"室內外未確認"):(candidate.indoor ? "室內":"戶外"))).font(.subheadline).foregroundStyle(Palette.muted)
                    Text(candidate.description ?? "此草案尚未附上景點介紹，可重新試算以更新資料。").lineSpacing(6)
                    if let data=candidate.restaurantData {RestaurantPriceView(placeId:candidate.id,initial:data)}
                    if let recommendations=candidate.socialRecommendations {ForEach(Array(recommendations.enumerated()),id:\.offset){_,recommendation in VStack(alignment:.leading,spacing:6){Text(recommendation.author ?? "來源未提供作者");Text(recommendation.claims.quote);Text(recommendation.postedAt ?? "發文日期未提供").font(.caption);if let url=URL(string:recommendation.sourceUrl){Link("查看原始推薦",destination:url)}}}}
                    if let guide=candidate.guide {
                        Text("如何到達").font(.headline)
                        Text(guide.directions.isEmpty ? "來源未提供到達方式，請先查詢導航。":guide.directions)
                        Text("社群到達說明並非即時交通班次，出發前請確認。").font(.caption).foregroundStyle(Palette.muted)
                        Text(String(format:"座標：%.6f, %.6f",candidate.lat,candidate.lng)).font(.caption)
                        if let url=URL(string:guide.sourceUrl){Link("社群指南與版本來源",destination:url)}
                        Text(guide.attribution+" · "+guide.license).font(.caption)
                        if let url=URL(string:guide.licenseUrl){Link("文字授權",destination:url).font(.caption)}
                        Text("社群介紹與評論").font(.headline)
                        if let reviews=candidate.reviews,!reviews.isEmpty {ForEach(reviews){review in Text(review.author+" · "+review.text)}}
                        else {Text("此來源未提供旅客評論或評分。").font(.caption)}
                    }
                    if let source=candidate.sourceUrl,let url=URL(string:source){Link("地點資料來源",destination:url).font(.caption)}
                    ActivityReferencesView(lat:candidate.lat,lng:candidate.lng,placeId:candidate.id,placeName:candidate.name)
                    PlaceMediaView(candidate:candidate)
                    Text("為你推薦").font(.headline)
                    Text(candidate.reason)
                    if let release=candidate.sourceRelease{Text("Overture · "+release).font(.caption)}
                    if let website=candidate.website,let url=URL(string:website),["http","https"].contains(url.scheme?.lowercased() ?? ""){Link("官方網站",destination:url)}
                    if let phone=candidate.phone,!phone.isEmpty{Text("電話："+phone)}
                    VStack(alignment:.leading,spacing:10) {
                        Text("建議停留 \(candidate.visitMinutes) 分鐘")
                        Text(candidate.restaurantData != nil ? "餐費見上方估算；營業與排隊仍待確認。":candidate.importedPoi ? "費用與排隊時間未知":"規劃預留 A$\(Int(candidate.cost)) · 排隊時間未知")
                        if let text=candidate.openingHoursText,!text.isEmpty{Text("營業時間標記："+text).font(.caption)}
                        if let open=candidate.openMinute,let close=candidate.closeMinute {Text("示範開放時段 \(time(open))–\(time(close))")}
                        if let note=candidate.note {Text(note).font(.caption)}
                    }.padding().frame(maxWidth:.infinity,alignment:.leading).background(Palette.mint,in:RoundedRectangle(cornerRadius:14))
                    Text(candidate.importedPoi ? "資料來源："+(candidate.source ?? "未知")+"；費用及營業資訊未確認。":"本機景點資料；費用與開放時間是規劃示例，出發前請確認當日資訊。").font(.caption).foregroundStyle(Palette.muted)
                }.padding(24)
            }.navigationTitle("景點介紹").navigationBarTitleDisplayMode(.inline).toolbar{Button("完成"){dismiss()}}
        }
    }
}
