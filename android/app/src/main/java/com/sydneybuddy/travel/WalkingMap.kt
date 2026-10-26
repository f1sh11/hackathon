package com.sydneybuddy.travel

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.foundation.clickable
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.android.gms.maps.model.LatLng as GoogleLatLng
import com.google.android.gms.maps.CameraUpdateFactory as GoogleCameraUpdate
import com.google.android.gms.maps.model.LatLngBounds as GoogleBounds
import com.google.maps.android.compose.*
import org.maplibre.android.annotations.MarkerOptions
import org.maplibre.android.annotations.PolylineOptions
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.geometry.LatLngBounds
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.Style
import uniffi.ferrostar.GeographicCoordinate

/** SDK-neutral map inputs. No Google route content is passed into this renderer. */
@Composable fun WalkingMap(points:List<WalkingPoint>,geometry:List<GeographicCoordinate>,position:GeographicCoordinate?,provider:MapProvider) {
    when(provider) {
        MapProvider.SCHEMATIC -> Text("Offline map legend does not display navigation routes. Please select an online base map.")
        MapProvider.GOOGLE -> if(!BuildConfig.MAPS_CONFIGURED) Text("Google Maps is not configured. Choose OpenStreetMap.") else {
            val camera=rememberCameraPositionState()
            LaunchedEffect(points,position){if(position!=null)camera.move(GoogleCameraUpdate.newLatLngZoom(GoogleLatLng(position.lat,position.lng),16f))
                else camera.move(GoogleCameraUpdate.newLatLngBounds(GoogleBounds.builder().apply{points.forEach{include(GoogleLatLng(it.lat,it.lng))}}.build(),50))}
            GoogleMap(Modifier.fillMaxWidth().height(300.dp),cameraPositionState=camera) {
                points.forEachIndexed {i,p->Marker(state=MarkerState(position=GoogleLatLng(p.lat,p.lng)),title="$i. ${p.name}")}
                if(geometry.size>1) Polyline(points=geometry.map{GoogleLatLng(it.lat,it.lng)},color=Green,width=8f)
                position?.let{Marker(state=MarkerState(position=GoogleLatLng(it.lat,it.lng)),title="Current location")}
            }
        }
        MapProvider.OSM -> WalkingOsmMap(points,geometry,position)
    }
}
@Composable private fun WalkingOsmMap(points:List<WalkingPoint>,geometry:List<GeographicCoordinate>,position:GeographicCoordinate?) {
    val context=LocalContext.current
    val lifecycle=LocalLifecycleOwner.current.lifecycle
    var map by remember{mutableStateOf<MapLibreMap?>(null)}
    val view=remember {
        OsmNetwork.initialize(context)
        MapView(context).apply {onCreate(null);getMapAsync{ready->ready.uiSettings.isAttributionEnabled=false;ready.uiSettings.isLogoEnabled=false;ready.setPrefetchZoomDelta(0);ready.setStyle(Style.Builder().fromJson(OSM_STYLE)){map=ready}}}
    }
    DisposableEffect(view,lifecycle) {
        val observer=LifecycleEventObserver{_,event->when(event){Lifecycle.Event.ON_START->view.onStart();Lifecycle.Event.ON_RESUME->view.onResume();Lifecycle.Event.ON_PAUSE->view.onPause();Lifecycle.Event.ON_STOP->view.onStop();else->Unit}}
        view.onStart();view.onResume();lifecycle.addObserver(observer)
        onDispose {lifecycle.removeObserver(observer);view.onPause();view.onStop();view.onDestroy()}
    }
    var userPin by remember{mutableStateOf<org.maplibre.android.annotations.Marker?>(null)}
    LaunchedEffect(map,points,geometry) {
        val ready=map ?: return@LaunchedEffect
        ready.clear();userPin=null
        points.forEachIndexed{i,p->ready.addMarker(MarkerOptions().position(LatLng(p.lat,p.lng)).title("$i. ${p.name}"))}
        if(geometry.size>1)ready.addPolyline(PolylineOptions().addAll(geometry.map{LatLng(it.lat,it.lng)}).color(android.graphics.Color.rgb(13,130,94)).width(5f))
        if(position==null)view.post {ready.moveCamera(CameraUpdateFactory.newLatLngBounds(LatLngBounds.Builder().includes(points.map{LatLng(it.lat,it.lng)}).build(),45))}
    }
    LaunchedEffect(map,position,geometry) {
        val ready=map ?: return@LaunchedEffect
        position?.let{p->
            val coord=LatLng(p.lat,p.lng)
            if(userPin==null)userPin=ready.addMarker(MarkerOptions().position(coord).title("Current location"))
            else userPin?.position=coord
            ready.moveCamera(CameraUpdateFactory.newLatLngZoom(coord,16.0))
        }
    }
    Column {
        AndroidView(factory={view},modifier=Modifier.fillMaxWidth().height(300.dp))
        val uri=LocalUriHandler.current
        Text("© OpenStreetMap contributors",style=androidx.compose.material3.MaterialTheme.typography.labelSmall,modifier=Modifier.clickable{uri.openUri("https://www.openstreetmap.org/copyright")})
    }
}
