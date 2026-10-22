import SwiftUI
import GoogleMaps

struct GoogleRouteMap: UIViewRepresentable {
    var day: DayPlan?
    func makeUIView(context: Context) -> GMSMapView {
        let options=GMSMapViewOptions();options.camera=GMSCameraPosition(latitude:-33.865,longitude:151.212,zoom:14)
        let map=GMSMapView(options:options);map.settings.compassButton=true
        map.padding=UIEdgeInsets(top:100,left:20,bottom:260,right:20)
        return map
    }
    func updateUIView(_ map:GMSMapView,context:Context) {
        guard let day else {return}; map.clear()
        var bounds=GMSCoordinateBounds(coordinate:day.start.location,coordinate:day.end.location)
        for (i,stop) in day.stops.enumerated() {
            let pin=GMSMarker(position:stop.coordinate);pin.title="\(i+1). \(stop.name)";pin.snippet="\(stop.arrival)–\(stop.departure)"
            pin.icon=GMSMarker.markerImage(with:UIColor(Palette.green));pin.map=map
            bounds=bounds.includingCoordinate(stop.coordinate)
        }
        for leg in day.legs where !leg.polyline.isEmpty {
            guard let path=GMSPath(fromEncodedPath:leg.polyline) else {continue}
            let line=GMSPolyline(path:path);line.strokeColor=UIColor(Palette.green);line.strokeWidth=5;line.map=map
        }
        if day.legs.allSatisfy({$0.polyline.isEmpty}) {
            // Illustrative connectors are deliberately visually different from navigable routes.
            let path=GMSMutablePath();path.add(day.start.location);day.stops.forEach{path.add($0.coordinate)};path.add(day.end.location)
            let line=GMSPolyline(path:path);line.strokeColor=UIColor.gray;line.strokeWidth=2;line.map=map
        }
        map.moveCamera(GMSCameraUpdate.fit(bounds,withPadding:55))
    }
}
struct SchematicMap: View {
    var day: DayPlan?
    var body:some View {
        GeometryReader { geo in
            ZStack {
                Color(red:0.88,green:0.93,blue:0.86)
                Path { p in p.move(to:CGPoint(x:geo.size.width*0.63,y:0));p.addCurve(to:CGPoint(x:geo.size.width,y:geo.size.height*0.62),control1:CGPoint(x:geo.size.width*0.30,y:geo.size.height*0.25),control2:CGPoint(x:geo.size.width*0.9,y:geo.size.height*0.28));p.addLine(to:CGPoint(x:geo.size.width,y:0));p.closeSubpath() }.fill(Color(red:0.65,green:0.84,blue:0.90))
                Canvas { context,size in
                    for i in 0..<10 {
                        var path=Path();let x=CGFloat(i)*62-100
                        path.move(to:CGPoint(x:x,y:0));path.addLine(to:CGPoint(x:x+190,y:size.height))
                        context.stroke(path,with:.color(.white.opacity(0.65)),lineWidth:13)
                    }
                    for i in 0..<9 {
                        var path=Path();let y=CGFloat(i)*85
                        path.move(to:CGPoint(x:0,y:y));path.addLine(to:CGPoint(x:size.width,y:y-90))
                        context.stroke(path,with:.color(.white.opacity(0.60)),lineWidth:9)
                    }
                    let items=day?.stops ?? []
                    if items.count>1 {
                        var path=Path();path.move(to:point(items[0],size))
                        for s in items.dropFirst() {path.addLine(to:point(s,size))}
                        context.stroke(path,with:.color(Palette.green),style:StrokeStyle(lineWidth:4,lineCap:.round,dash:[7,8]))
                    }
                }
                Text("SYDNEY\nHARBOUR").font(.system(size:14,weight:.semibold,design:.rounded)).tracking(3).foregroundStyle(Color(red:0.22,green:0.48,blue:0.56)).position(x:geo.size.width*0.79,y:geo.size.height*0.18)
                Image(systemName:"sailboat.fill").font(.system(size:36)).foregroundStyle(.white).position(x:geo.size.width*0.80,y:geo.size.height*0.30)
                if let stops=day?.stops {
                    ForEach(Array(stops.enumerated()),id:\.element.id) { i,stop in
                        VStack(spacing:4) { Text("\(i+1)").font(.headline.bold()).foregroundStyle(.white).frame(width:34,height:34).background(Palette.green,in:Circle()).overlay(Circle().stroke(.white,lineWidth:3)); Text(stop.name).font(.system(size:10,weight:.bold)).padding(6).background(.white,in:Capsule()) }.position(point(stop,geo.size))
                    }
                } else { VStack(spacing:15) { Robot(size:110); Text("你的悉尼小冒险\n从这里开始").font(.title2.bold()).multilineTextAlignment(.center).foregroundStyle(Palette.ink) }.offset(y:-40) }
            }
        }
    }
    func point(_ stop:Stop,_ size:CGSize) -> CGPoint {
        let stops=day?.stops ?? [stop]
        let minLat=(stops.map(\.lat).min() ?? -33.88)-0.006,maxLat=(stops.map(\.lat).max() ?? -33.85)+0.006
        let minLng=(stops.map(\.lng).min() ?? 151.20)-0.006,maxLng=(stops.map(\.lng).max() ?? 151.22)+0.006
        return CGPoint(x:45+(stop.lng-minLng)/(maxLng-minLng)*max(1,size.width-110),y:140+(maxLat-stop.lat)/(maxLat-minLat)*max(1,size.height-420))
    }
}
struct HomeView: View {
    @EnvironmentObject var store:AppStore
    @State var chat=false
    @State var details=false
    var body:some View {
        ZStack(alignment:.top) {
            Group { if SydneyBuddyApp.mapReady {GoogleRouteMap(day:store.currentDay)} else {SchematicMap(day:store.currentDay)} }.ignoresSafeArea(edges:.top)
            VStack(spacing:10) {
                HStack { HStack(spacing:8) { Image(systemName:"mappin.circle.fill").foregroundStyle(Palette.green); VStack(alignment:.leading,spacing:3) { Text("悉尼 · 小冒险").font(.headline); Text(store.currentDay?.date ?? "准备出发").font(.caption).foregroundStyle(Palette.muted) } }.padding(.horizontal,18).padding(.vertical,12).background(.white,in:Capsule()); Spacer(); Button {store.tab=3} label:{Image(systemName:"person.crop.circle.fill").font(.system(size:42)).foregroundStyle(Palette.green).background(.white,in:Circle())} }.padding(.horizontal,20)
                if !SydneyBuddyApp.mapReady { Text("示意地图 · Google 地图待配置").font(.caption2.bold()).padding(.horizontal,12).padding(.vertical,6).background(.white.opacity(0.9),in:Capsule()) }
                if let t=store.trip,t.days.count>1 { Picker("天数",selection:$store.dayIndex) { ForEach(t.days.indices,id:\.self) { Text("第 \($0+1) 天").tag($0) } }.pickerStyle(.segmented).padding(.horizontal,24) }
                if let t=store.trip { WeatherCard(weather:store.refreshedWeather ?? t.weather,compact:true).padding(.horizontal,20) }
                Spacer()
                if store.trip != nil {PreparationTrail {details=true}.padding(.horizontal,20)}
                HStack(alignment:.bottom,spacing:12) {
                    VStack(alignment:.leading,spacing:12) {
                        Text(store.trip?.title ?? "悉尼一日游").font(.title3.bold()).foregroundStyle(Palette.ink)
                        if let day=store.currentDay {
                            Text("\(day.stops.count) 个地点 · 约 \(day.estimatedSteps) 步").font(.caption).foregroundStyle(Palette.muted)
                            HStack(spacing:8) { Label("A$\(Int(day.estimatedCost))",systemImage:"wallet.bifold"); Text("·");Text("\(day.finishTime) 返回") }.font(.caption.bold()).foregroundStyle(Palette.green)
                            Text(day.stops.map(\.name).joined(separator:" → ")).font(.caption).lineLimit(2).foregroundStyle(Palette.muted)
                        } else { Text("让 Buddy 帮你选出刚刚好的路线").font(.caption).foregroundStyle(Palette.muted) }
                        Button { if store.trip==nil {store.newTrip()} else {details=true} } label:{ HStack {Text(store.trip==nil ? "开始规划" : "查看路线").fontWeight(.bold);Spacer();Image(systemName:"arrow.up.right")}.padding(13).foregroundStyle(.white).background(Palette.green,in:RoundedRectangle(cornerRadius:14)) }
                    }.padding(18).frame(maxWidth:.infinity,alignment:.leading).background(.white,in:RoundedRectangle(cornerRadius:25)).shadow(color:Palette.ink.opacity(0.08),radius:12,y:4)
                    Button {chat=true} label:{ VStack(spacing:0) {Text("问 AI").font(.headline.bold()).padding(.horizontal,18).padding(.vertical,12).foregroundStyle(.white).background(Palette.green,in:Capsule());Robot(size:80)} }.buttonStyle(.plain).accessibilityLabel("问 AI")
                }.padding(.horizontal,20)
                Text(store.trip?.mode=="live" ? "Google 路线 · 费用与步数为估算" : "路线与营业时间为示例 · 非导航路径").font(.system(size:10)).foregroundStyle(Palette.ink.opacity(0.7)).padding(7).background(.white.opacity(0.85),in:Capsule())
            }.padding(.top,12).padding(.bottom,12)
        }.sheet(isPresented:$chat){AgentView()}.sheet(isPresented:$details){TripDetailView()}
    }
}
