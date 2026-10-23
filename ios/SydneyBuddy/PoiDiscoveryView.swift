import SwiftUI
import MapKit
import GoogleMaps

@MainActor final class PoiToolbox:ObservableObject {
    static let shared=PoiToolbox()
    @Published var places:[ScheduleCandidate]
    private init(){places=(UserDefaults.standard.data(forKey:"poi-toolbox").flatMap{try? JSONDecoder().decode([ScheduleCandidate].self,from:$0)}) ?? []}
    func add(_ p:ScheduleCandidate){guard !places.contains(where:{$0.id==p.id}) else{return};places.append(p);if let data=try? JSONEncoder().encode(places){UserDefaults.standard.set(data,forKey:"poi-toolbox")}}
}
@MainActor final class PoiSearchModel:ObservableObject {
    @Published var query=""
    @Published var category="sights"
    @Published var places:[ScheduleCandidate]=[]
    @Published var selected:String?
    @Published var busy=false
    @Published var error:String?
    @Published var hasSearched=false
    var center=CLLocationCoordinate2D(latitude:-33.865,longitude:151.212)
    var loader:(URLRequest) async throws -> (Data,URLResponse) = {try await URLSession.shared.data(for:$0)}
    private var job:Task<Void,Never>?
    func search(){
        job?.cancel();busy=true;error=nil;hasSearched=true;places=[];selected=nil
        let lat=center.latitude,lng=center.longitude,text=query,kind=category
        job=Task {@MainActor in
            do {
                var request=URLRequest(url:URL(string:"http://127.0.0.1:8787/poi/search")!);request.httpMethod="POST";request.setValue("application/json",forHTTPHeaderField:"Content-Type");request.timeoutInterval=30
                request.httpBody=try JSONSerialization.data(withJSONObject:["lat":lat,"lng":lng,"radius":1500,"category":kind,"query":text])
                let (data,response)=try await loader(request);try Task.checkCancellation()
                guard (response as? HTTPURLResponse)?.statusCode==200 else {let raw=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any];throw NSError(domain:"POI",code:1,userInfo:[NSLocalizedDescriptionKey:raw?["error"] as? String ?? "地點搜尋失敗，請重試。"])}
                struct Result:Decodable{var places:[ScheduleCandidate]}
                places=try JSONDecoder().decode(Result.self,from:data).places;busy=false
            }catch {if !Task.isCancelled{self.error="地點資料暫時無法載入："+error.localizedDescription;busy=false}}
        }
    }
    func stop(){job?.cancel();job=nil;busy=false}
}
@MainActor struct PoiDiscoveryView:View {
    var onAdd:(ScheduleCandidate)->Void = {PoiToolbox.shared.add($0)}
    @EnvironmentObject var store:AppStore
    @Environment(\.dismiss) var dismiss
    @StateObject private var model=PoiSearchModel()
    @State private var detail:ScheduleCandidate?
    @State private var added=Set<String>()
    @State private var zoom:Double=14
    var body:some View {
        NavigationStack {
            VStack(spacing:10) {
                HStack {TextField("搜尋附近地點名稱（可留空）",text:$model.query).textFieldStyle(.roundedBorder).onSubmit{model.search()};Button("搜尋"){model.search()}.disabled(model.busy)}.padding(.horizontal)
                Picker("地點分類",selection:$model.category){Text("景點").tag("sights");Text("餐飲").tag("food");Text("生活設施").tag("facilities")}.pickerStyle(.segmented).padding(.horizontal)
                HStack{Picker("底圖",selection:$store.mapProvider){ForEach(MapProvider.allCases){Text($0.label).tag($0)}}.pickerStyle(.menu);Spacer();Text("地點資料：OpenStreetMap").font(.caption2).foregroundStyle(Palette.muted)}.padding(.horizontal)
                Group {
                    switch store.mapProvider {
                    case .apple,.osm:
                        PoiNativeMap(places:model.places,osm:store.mapProvider == .osm,center:model.center,zoom:zoom,onCamera:{model.center=$0;zoom=$1},onSelect:{model.selected=$0})
                    case .google:
                        if SydneyBuddyApp.mapReady{PoiGoogleMap(places:model.places,center:model.center,zoom:zoom,onCamera:{model.center=$0;zoom=$1},onSelect:{model.selected=$0})}
                        else{Text("Google 地圖尚未設定金鑰，請選擇其他底圖。").font(.caption).padding()}
                    case .schematic:
                        Text("離線示意圖沒有街道底圖，請切換聯網底圖來移動搜尋範圍。").font(.caption).padding()
                    }
                }.frame(height:250).accessibilityIdentifier("poi-map-"+store.mapProvider.rawValue)
                Text("切換分類自動搜尋 · 移動地圖後按搜尋 · 附近 1.5 km").font(.caption2).foregroundStyle(Palette.muted)
                if model.busy {ProgressView("正在搜尋附近地點…")}
                if let error=model.error {
                    VStack(spacing:6){Text(error).font(.caption).foregroundStyle(.red);Text("地圖底圖仍可顯示；地點清單尚未取得，並非附近沒有地點。").font(.caption2).foregroundStyle(Palette.muted);Button("重新搜尋"){model.search()}}
                        .padding(.horizontal).accessibilityIdentifier("poi-search-error")
                }
                List {
                    if model.places.isEmpty && !model.busy && model.error==nil && model.hasSearched {Text("此範圍沒有符合的地點。可清空名稱、切換分類，或移動地圖後重新搜尋。").font(.caption)}
                    ForEach(model.places.sorted{a,b in a.id==model.selected && b.id != model.selected}) {p in
                        VStack(alignment:.leading,spacing:7) {
                            Label(p.name,systemImage:p.category=="美食" ? "fork.knife":p.category=="設施" ? "figure.stand":"mappin.and.ellipse").font(.subheadline.bold()).foregroundStyle(p.category=="美食" ? .orange:Palette.green)
                            Text(p.description ?? "介紹未提供").font(.caption).lineLimit(2)
                            HStack {Button("詳細介紹"){detail=p};Spacer();Button(added.contains(p.id)||PoiToolbox.shared.places.contains(where:{$0.id==p.id}) ? "已加入":"＋工具箱"){onAdd(p);added.insert(p.id)}.disabled(added.contains(p.id)||PoiToolbox.shared.places.contains(where:{$0.id==p.id}))}.font(.caption)
                        }.listRowBackground(p.id==model.selected ? Palette.mint:Color.white)
                    }
                }.listStyle(.plain)
                Link("地點資料 © OpenStreetMap contributors · ODbL",destination:URL(string:"https://www.openstreetmap.org/copyright")!).font(.caption2)
            }.navigationTitle("搜尋地圖地點").navigationBarTitleDisplayMode(.inline).toolbar{Button("完成"){dismiss()}}
        }.sheet(item:$detail){SchedulePlaceDetail(candidate:$0)}
            .onAppear{if !model.hasSearched{model.search()}}
            .onChange(of:model.category){_,_ in model.search()}
            .onDisappear{model.stop()}
    }
}
private final class PoiPin:NSObject,MKAnnotation {
    let point:ScheduleCandidate
    var coordinate:CLLocationCoordinate2D {CLLocationCoordinate2D(latitude:point.lat,longitude:point.lng)}
    var title:String? {point.name}
    init(_ p:ScheduleCandidate){point=p}
}
struct PoiNativeMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var osm:Bool
    var center:CLLocationCoordinate2D;var zoom:Double
    var onCamera:(CLLocationCoordinate2D,Double)->Void;var onSelect:(String)->Void
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,MKMapViewDelegate {
        var parent:PoiNativeMap;var key=""
        init(_ p:PoiNativeMap){parent=p}
        func mapView(_ map:MKMapView,rendererFor overlay:MKOverlay)->MKOverlayRenderer{if let tiles=overlay as? MKTileOverlay{return MKTileOverlayRenderer(tileOverlay:tiles)};return MKOverlayRenderer(overlay:overlay)}
        func mapView(_ map:MKMapView,regionDidChangeAnimated animated:Bool){parent.onCamera(map.centerCoordinate,log2(360/max(0.00001,map.region.span.longitudeDelta)))}
        func mapView(_ map:MKMapView,viewFor annotation:MKAnnotation)->MKAnnotationView? {
            guard let pin=annotation as? PoiPin else{return nil}
            let view=MKMarkerAnnotationView(annotation:pin,reuseIdentifier:nil);view.markerTintColor=pin.point.category=="美食" ? .systemOrange:.systemTeal;view.glyphImage=UIImage(systemName:pin.point.category=="美食" ? "fork.knife":pin.point.category=="設施" ? "figure.stand":"mappin");view.canShowCallout=true;return view
        }
        func mapView(_ map:MKMapView,didSelect view:MKAnnotationView){if let pin=view.annotation as? PoiPin{parent.onSelect(pin.point.id)}}
    }
    func makeUIView(context:Context)->MKMapView{let map=MKMapView();map.delegate=context.coordinator;if osm{map.addOverlay(OsmTiles(),level:.aboveLabels)};let delta=360/pow(2,zoom);map.setRegion(MKCoordinateRegion(center:center,span:MKCoordinateSpan(latitudeDelta:delta,longitudeDelta:delta)),animated:false);return map}
    func updateUIView(_ map:MKMapView,context:Context){context.coordinator.parent=self
        let tiles=map.overlays.filter{$0 is OsmTiles}
        if osm && tiles.isEmpty{map.addOverlay(OsmTiles(),level:.aboveLabels)}
        if !osm{map.removeOverlays(tiles)}
        let key=places.map(\.id).joined(separator:",");guard context.coordinator.key != key else{return};context.coordinator.key=key;map.removeAnnotations(map.annotations);map.addAnnotations(places.map(PoiPin.init))}
}

private struct PoiGoogleMap:UIViewRepresentable {
    var places:[ScheduleCandidate];var center:CLLocationCoordinate2D;var zoom:Double
    var onCamera:(CLLocationCoordinate2D,Double)->Void;var onSelect:(String)->Void
    func makeCoordinator()->Coordinator{Coordinator(self)}
    final class Coordinator:NSObject,GMSMapViewDelegate {
        var parent:PoiGoogleMap;var key=""
        init(_ parent:PoiGoogleMap){self.parent=parent}
        func mapView(_ map:GMSMapView,idleAt camera:GMSCameraPosition){parent.onCamera(camera.target,Double(camera.zoom))}
        func mapView(_ map:GMSMapView,didTap marker:GMSMarker)->Bool{if let id=marker.userData as? String{parent.onSelect(id)};return false}
    }
    func makeUIView(context:Context)->GMSMapView {
        let options=GMSMapViewOptions();options.camera=GMSCameraPosition(target:center,zoom:Float(zoom))
        let map=GMSMapView(options:options);map.delegate=context.coordinator;return map
    }
    func updateUIView(_ map:GMSMapView,context:Context){
        context.coordinator.parent=self;let key=places.map(\.id).joined(separator:",")
        guard key != context.coordinator.key else{return};context.coordinator.key=key;map.clear()
        for point in places{let marker=GMSMarker(position:CLLocationCoordinate2D(latitude:point.lat,longitude:point.lng));marker.title=point.name;marker.userData=point.id;marker.icon=GMSMarker.markerImage(with:point.category=="美食" ? .systemOrange:.systemTeal);marker.map=map}
    }
}
