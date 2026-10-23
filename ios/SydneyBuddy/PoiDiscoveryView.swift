import SwiftUI
import MapKit

@MainActor final class PoiToolbox:ObservableObject {
    static let shared=PoiToolbox()
    @Published var places:[ScheduleCandidate]
    private init(){places=(UserDefaults.standard.data(forKey:"poi-toolbox").flatMap{try? JSONDecoder().decode([ScheduleCandidate].self,from:$0)}) ?? []}
    func add(_ p:ScheduleCandidate){guard !places.contains(where:{$0.id==p.id}) else{return};places.append(p);if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
}
@MainActor struct PoiDiscoveryView:View {
    var onAdd:(ScheduleCandidate)->Void = {PoiToolbox.shared.add($0)}
    @Environment(\.dismiss) var dismiss
    @State private var query=""
    @State private var category="sights"
    @State private var places:[ScheduleCandidate]=[]
    @State private var detail:ScheduleCandidate?
    @State private var center=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
    @State private var selected:String?
    @State private var busy=false
    @State private var error:String?
    @State private var added=Set<String>()
    @State private var job:Task<Void,Never>?
    func search(){job?.cancel();busy=true;error=nil;let lat=center.latitude,lng=center.longitude,text=query,kind=category
        job=Task {@MainActor in
            do {
                var request=URLRequest(url:URL(string:"http://127.0.0.1:8787/poi/search")!);request.httpMethod="POST";request.setValue("application/json",forHTTPHeaderField:"Content-Type");request.timeoutInterval=30
                request.httpBody=try JSONSerialization.data(withJSONObject:["lat":lat,"lng":lng,"radius":1500,"category":kind,"query":text])
                let (data,response)=try await URLSession.shared.data(for:request);try Task.checkCancellation()
                guard (response as? HTTPURLResponse)?.statusCode==200 else {let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any];throw NSError(domain:"POI",code:1,userInfo:[NSLocalizedDescriptionKey:raw?["error"] as? String ?? "地點搜尋失敗，請重試。"])}
                struct Result:Decodable{var places:[ScheduleCandidate]}
                places=try JSONDecoder().decode(Result.self,from:data).places;selected=nil;busy=false
            }catch is CancellationError {}catch{if !Task.isCancelled{self.error=error.localizedDescription;busy=false}}
        }
    }
    var body:some View {
        NavigationStack {
            VStack(spacing:10) {
                HStack {TextField("搜尋附近地點名稱（可留空）",text:$query).textFieldStyle(.roundedBorder).onSubmit{search()};Button("搜尋"){search()}.disabled(busy)}.padding(.horizontal)
                Picker("地點分類",selection:$category){Text("景點").tag("sights");Text("餐飲").tag("food");Text("生活設施").tag("facilities")}.pickerStyle(.segmented).padding(.horizontal)
                PoiOsmMap(places:places,onCenter:{center=$0},onSelect:{selected=$0}).frame(height:250)
                Text("移動地圖後按搜尋 · 中心附近 1.5 km · OSM 開源測試").font(.caption2).foregroundStyle(Palette.muted)
                if busy {ProgressView("查詢開源地點…")}
                if let error {Text(error).font(.caption).foregroundStyle(.red).padding(.horizontal)}
                List {
                    if places.isEmpty && !busy {Text("選擇分類並搜尋附近地點；地圖標記可點選。").font(.caption)}
                    ForEach(places.sorted{a,b in a.id==selected && b.id != selected}) {p in
                        VStack(alignment:.leading,spacing:7) {
                            Label(p.name,systemImage:p.category=="美食" ? "fork.knife":p.category=="設施" ? "figure.stand":"mappin.and.ellipse").font(.subheadline.bold()).foregroundStyle(p.category=="美食" ? .orange:Palette.green)
                            Text(p.description ?? "介紹未提供").font(.caption).lineLimit(2)
                            HStack {Button("詳細介紹"){detail=p};Spacer();Button(added.contains(p.id)||PoiToolbox.shared.places.contains(where:{$0.id==p.id}) ? "已加入":"＋工具箱"){onAdd(p);added.insert(p.id)}.disabled(added.contains(p.id)||PoiToolbox.shared.places.contains(where:{$0.id==p.id}))}.font(.caption)
                        }.listRowBackground(p.id==selected ? Palette.mint:Color.white)
                    }
                }.listStyle(.plain)
                Link("© OpenStreetMap contributors · ODbL",destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2)
            }.navigationTitle("搜尋地圖地點").navigationBarTitleDisplayMode(.inline).toolbar{Button("完成"){dismiss()}}
        }.sheet(item:$detail){SchedulePlaceDetail(candidate:$0)}.onDisappear{job?.cancel()}
    }
}
private final class PoiPin:NSObject,MKAnnotation {
    let point:ScheduleCandidate
    var coordinate:CLLocationCoordinate2D {CLLocationCoordinate2D(latitude:point.lat,longitude:point.lng)}
    var title:String? {point.name}
    init(_ p:ScheduleCandidate){point=p}
}
private struct PoiOsmMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var onCenter:(CLLocationCoordinate2D)->Void;var onSelect:(String)->Void
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:PoiOsmMap;var key=""
        init(_ p:PoiOsmMap){parent=p}
        func mapView(_ map:MKMapView,rendererFor overlay:MKOverlay)->MKOverlayRenderer{if let tiles=overlay as? MKTileOverlay{return MKTileOverlayRenderer(tileOverlay:tiles)};return MKOverlayRenderer(overlay:overlay)}
        func mapView(_ map:MKMapView,regionDidChangeAnimated animated:Bool){parent.onCenter(map.centerCoordinate)}
        func mapView(_ map:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            guard let pin=annotation as? PoiPin else{return nil}
            let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);view.markerTintColor=pin.point.category=="美食" ? .systemOrange:.systemTeal;view.glyphImage=UIImage(systemName:pin.point.category=="美食" ? "fork.knife":pin.point.category=="設施" ? "figure.stand":"mappin");view.canShowCallout=true;return view
        }
        func mapView(_ map:MKMapView,didSelect view:MKAnnotationView){if let pin=view.annotation as? PoiPin{parent.onSelect(pin.point.id)}}
    }
    func makeUIView(context:Context)->MKMapView{let map=MKMapView();map.delegate=context.coordinator;map.addOverlay(OsmTiles(),level:.aboveLabels);map.setRegion(MKCoordinateRegion(center:CLLocationCoordinate2D(latitude:-33.865,longitude:151.212),span:MKCoordinateSpan(latitudeDelta:0.025,longitudeDelta:0.025)),animated:false);return map}
    func updateUIView(_ map:MKMapView,context:Context){context.coordinator.parent=self;let key=places.map(\.id).joined(separator:",");guard context.coordinator.key != key else{return};context.coordinator.key=key;map.removeAnnotations(map.annotations);map.addAnnotations(places.map(PoiPin.init))}
}
