import Foundation
import MapKit

/// An explicit, Debug-only exporter for a fixed public-landmark walking replay.
///
/// This deliberately bypasses the short-lived `AppleWalkingRoads` cache: every
/// export makes fresh `MKDirections` walking requests, then writes only the
/// normalized distance, ETA, public endpoints and capture time. It never reads
/// the device location, user defaults, account data or secrets.
enum MapKitReplayCapture {
    static let launchArgument = "--export-mapkit-walking-replay"

    private struct Segment {
        let id: String
        let from: String
        let to: String
        let fromCoordinate: Coordinate
        let toCoordinate: Coordinate
    }

    private struct RecordedLeg: Codable {
        let id: String
        let from: String
        let to: String
        let fromCoordinate: Coordinate
        let toCoordinate: Coordinate
        let meters: Double
        let seconds: Double
        let kind: String
        let capturedAt: String
    }

    private struct Replay: Codable {
        let schemaVersion: Int
        let kind: String
        let provider: String
        let recordedDate: String
        let capturedAt: String
        let captureClient: String
        let requestTransportType: String
        let timeZone: String
        let usesDeviceLocation: Bool
        let rerouteEvidence: String
        let notLiveNavigation: Bool
        let endpointBinding: String
        let note: String
        let legs: [RecordedLeg]
    }

    /// Public, fixed endpoints only. The sixth leg is a fresh direct query used
    /// when the fatigue replay removes the Opera House visit.
    private static let segments = [
        Segment(id: "start_to_garden", from: "start", to: "garden", fromCoordinate: .quay,
                toCoordinate: Coordinate(name: "皇家植物园", lat: -33.8642, lng: 151.2166)),
        Segment(id: "garden_to_opera", from: "garden", to: "opera",
                fromCoordinate: Coordinate(name: "皇家植物园", lat: -33.8642, lng: 151.2166),
                toCoordinate: Coordinate(name: "悉尼歌剧院", lat: -33.8568, lng: 151.2153)),
        Segment(id: "opera_to_rocks", from: "opera", to: "rocks",
                fromCoordinate: Coordinate(name: "悉尼歌剧院", lat: -33.8568, lng: 151.2153),
                toCoordinate: Coordinate(name: "岩石区", lat: -33.8599, lng: 151.2090)),
        Segment(id: "rocks_to_mca", from: "rocks", to: "mca",
                fromCoordinate: Coordinate(name: "岩石区", lat: -33.8599, lng: 151.2090),
                toCoordinate: Coordinate(name: "澳大利亚当代艺术博物馆", lat: -33.8599, lng: 151.2091)),
        Segment(id: "mca_to_end", from: "mca", to: "end",
                fromCoordinate: Coordinate(name: "澳大利亚当代艺术博物馆", lat: -33.8599, lng: 151.2091),
                toCoordinate: .quay),
        Segment(id: "garden_to_rocks", from: "garden", to: "rocks",
                fromCoordinate: Coordinate(name: "皇家植物园", lat: -33.8642, lng: 151.2166),
                toCoordinate: Coordinate(name: "岩石区", lat: -33.8599, lng: 151.2090))
    ]

    /// Called only by the Debug app launch hook. The result remains in the app
    /// Documents directory for manual review and explicit copying into the repo.
    @MainActor static func exportIfRequested() async {
        guard ProcessInfo.processInfo.arguments.contains(launchArgument) else { return }
        do {
            let url = try await exportToDocuments()
            print("MAPKIT_REPLAY_EXPORT=\(url.path)")
        } catch {
            print("MAPKIT_REPLAY_EXPORT_FAILED=\(error.localizedDescription)")
        }
    }

    @MainActor static func exportToDocuments(now: Date = Date()) async throws -> URL {
        let capturedAt = ISO8601DateFormatter().string(from: now)
        var legs: [RecordedLeg] = []
        for segment in segments {
            let route = try await walkingRoute(from: segment.fromCoordinate, to: segment.toCoordinate)
            guard route.distance > 0, route.expectedTravelTime > 0 else {
                throw NSError(domain: "MapKitReplayCapture", code: 1,
                              userInfo: [NSLocalizedDescriptionKey: "MapKit returned an empty walking route for \(segment.id)."])
            }
            legs.append(RecordedLeg(id: segment.id, from: segment.from, to: segment.to,
                                   fromCoordinate: segment.fromCoordinate, toCoordinate: segment.toCoordinate,
                                   meters: route.distance, seconds: route.expectedTravelTime,
                                   kind: "provider_recorded_leg", capturedAt: capturedAt))
        }

        let replay = Replay(
            schemaVersion: 1,
            kind: "historical-road-replay",
            provider: "Apple Maps / MapKit",
            recordedDate: TripProfile.dateString(now),
            capturedAt: capturedAt,
            captureClient: "SydneyBuddy iOS Debug MapKitReplayCapture",
            requestTransportType: "walking",
            timeZone: "Australia/Sydney",
            usesDeviceLocation: false,
            rerouteEvidence: "direct_provider_recorded_leg",
            notLiveNavigation: true,
            endpointBinding: "Six fixed public Sydney landmark coordinates; no current device location is captured.",
            note: "A fixed historical regression snapshot captured with MKDirections.Request.transportType = .walking. It is not live navigation and must remain labeled as a dated snapshot.",
            legs: legs
        )
        let documents = try FileManager.default.url(for: .documentDirectory, in: .userDomainMask,
                                                    appropriateFor: nil, create: true)
        let stamp = capturedAt.replacingOccurrences(of: ":", with: "-")
            .replacingOccurrences(of: ".", with: "-")
        let output = documents.appendingPathComponent("apple-mapkit-walking-replay-\(stamp).json")
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        try encoder.encode(replay).write(to: output, options: .atomic)
        return output
    }

    @MainActor private static func walkingRoute(from: Coordinate, to: Coordinate) async throws -> MKRoute {
        var lastError: Error?
        for attempt in 0..<3 {
            let request = MKDirections.Request()
            request.source = MKMapItem(placemark: MKPlacemark(coordinate: from.location))
            request.destination = MKMapItem(placemark: MKPlacemark(coordinate: to.location))
            request.transportType = .walking
            let directions = MKDirections(request: request)
            do {
                let response = try await directions.calculate()
                guard let route = response.routes.first else { throw MKError(.directionsNotFound) }
                return route
            } catch {
                lastError = error
                if attempt < 2 { try await Task.sleep(for: .seconds(Double(attempt + 1) * 2)) }
            }
        }
        throw NSError(domain: "MapKitReplayCapture", code: 2,
                      userInfo: [NSLocalizedDescriptionKey: "MapKit walking replay capture failed.", NSUnderlyingErrorKey: lastError as Any])
    }
}
