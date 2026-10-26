using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

public sealed record JourneyCoordinate(double Lat, double Lng);
public sealed record JourneyPoint(string Name, double Lat, double Lng, int StayMinutes);
public sealed record JourneyStep(string Mode, string? Line, string? Headsign, string From, string To,
    DateTimeOffset DepartureTime, DateTimeOffset ArrivalTime, bool Realtime, int? StopCount,
    double Meters, double Seconds, double WaitSeconds, string? Platform, string? Agency,
    IReadOnlyList<JourneyCoordinate> Geometry);
public sealed record JourneyLeg(int Index, string Mode, string From, string To, DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime, double StaySeconds, IReadOnlyList<JourneyStep> Steps);
public sealed record JourneyPlan(DateTimeOffset DepartureTime, DateTimeOffset ArrivalTime, double TravelSeconds,
    double ElapsedSeconds, double Meters, IReadOnlyList<JourneyLeg> Legs, string[] Warnings, string Source);

/// <summary>Transport-neutral itinerary boundary. OTP owns GTFS transit; Valhalla owns street routing.</summary>
public sealed class JourneyService(HttpClient client, IConfiguration configuration, NavigationService roads, IEnumerable<IJourneyProvider>? providers = null)
{
    private const string Query = """
        query Journey($origin: PlanLabeledLocationInput!, $destination: PlanLabeledLocationInput!, $dateTime: PlanDateTimeInput!) {
          planConnection(origin: $origin, destination: $destination, dateTime: $dateTime, first: 1, modes: {transitOnly: true}) {
            edges { node { legs {
              mode startTime endTime distance realTime headsign
              from { name lat lon stop { platformCode } }
              to { name lat lon }
              route { shortName longName agency { name } }
              legGeometry { points }
              intermediateStops { name }
              pickupType dropoffType
            } } }
          }
        }
        """;
    public bool TransitConfigured => OtpEndpoint is not null;
    private Uri? OtpEndpoint => Uri.TryCreate(configuration["Transit:OtpGraphqlUrl"], UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)
        && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;

    public async Task<JourneyPlan> PlanAsync(JsonObject input, CancellationToken ct)
    {
        var providerId = input.Str("routingProvider", "open");
        if (input.Str("contentSource") == "google" && providerId != "google")
            throw new ServiceException("Google itineraries must use Google routes.");
        if (providerId == "google" && input.Str("mapProvider") != "google")
            throw new ServiceException("Google routes can only be displayed on Google Maps.");
        var provider = providerId == "open" ? null : (providers?.SingleOrDefault(x => x.Id == providerId)
            ?? throw new ServiceException("This route provider is unavailable. Apple routes must be requested on an iOS device."));
        if (input["points"] is not JsonArray raw || raw.Count is < 2 or > 12) throw new ServiceException("Journey planning requires 2–12 stops.");
        var points = raw.Select(ParsePoint).ToArray();
        if (input["modes"] is not JsonArray modes || modes.Count != points.Length - 1) throw new ServiceException("Choose a transport mode for each leg.");
        var choices = modes.Select(x => x is JsonValue v && v.TryGetValue<string>(out var value) ? value : "").ToArray();
        if (choices.Any(x => x is not ("walk" or "drive" or "transit"))) throw new ServiceException("Supported transport modes are walking, driving and public transport.");
        var timeText = input.Str("departureTime");
        if (!System.Text.RegularExpressions.Regex.IsMatch(timeText, @"(Z|[+-]\d{2}:\d{2})$")
            || !DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var departure))
            throw new ServiceException("The departure time must include a time zone, such as +11:00.");
        if (departure.Year is < 2000 or > 2100) throw new ServiceException("The departure date is outside the supported range.");
        // Fail before making any street calls if a later transit leg cannot be planned.
        if (provider is null && choices.Contains("transit") && !TransitConfigured) throw new ServiceException("Public transport is not enabled, so service times are unavailable. Choose walking or driving, or contact the development team.");
        if (provider is null && choices.Any(x => x != "transit") && !roads.Configured) throw new ServiceException("Walking and driving routes are not enabled. Please contact the development team.");
        provider?.EnsureAvailable(choices);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var cursor = departure; var legs = new List<JourneyLeg>();
        try
        {
            for (var i = 0; i < choices.Length; i++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                IReadOnlyList<JourneyStep> steps = provider is not null
                    ? await provider.RouteAsync(points[i], points[i + 1], choices[i], cursor, deadline.Token)
                    : choices[i] == "transit"
                    ? await TransitAsync(points[i], points[i + 1], cursor, deadline.Token)
                    : await StreetAsync(points[i], points[i + 1], choices[i], cursor, deadline.Token);
                var arrival = steps[^1].ArrivalTime;
                var stay = i == choices.Length - 1 ? 0 : points[i + 1].StayMinutes * 60;
                legs.Add(new(i, choices[i], points[i].Name, points[i + 1].Name, cursor, arrival, stay, steps));
                // Timetable queries for the next leg start after actual arrival plus the attraction visit.
                cursor = arrival.AddSeconds(stay);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServiceException("Journey planning timed out. Try again with fewer stops. Your original itinerary is unchanged."); }
        var warnings = new List<string> { "Planning does not change your original itinerary. Search again if the departure time or transport mode changes." };
        if (choices.Contains("drive")) warnings.Add("Driving times exclude parking and live traffic. Arrange your vehicle separately when using mixed transport.");
        if (provider is null && choices.Contains("transit")) warnings.Add("Services come from imported GTFS data. They are marked live only when GTFS-RT is configured and an update exists for that service. Fares are unavailable.");
        if (provider is not null) warnings.Add(provider.Notice);
        if (legs.SelectMany(x => x.Steps).Any(x => x.Geometry.Count < 2 && x.Meters > 0)) warnings.Add("Some legs have no route geometry and show stop names only. Missing geometry is not replaced with straight lines.");
        var all = legs.SelectMany(x => x.Steps).ToArray();
        return new(departure, cursor, legs.Sum(x => (x.ArrivalTime - x.DepartureTime).TotalSeconds),
            (cursor - departure).TotalSeconds, all.Sum(x => x.Meters), legs, warnings.ToArray(), provider?.Label ?? (choices.All(x => x == "transit") ? "OpenTripPlanner / OSM + GTFS" : choices.Contains("transit") ? "OSM / Valhalla / OpenTripPlanner + GTFS" : "OSM / Valhalla"));
    }

    private async Task<IReadOnlyList<JourneyStep>> StreetAsync(JourneyPoint from, JourneyPoint to, string mode, DateTimeOffset departure, CancellationToken ct)
    {
        if (Math.Abs(from.Lat - to.Lat) + Math.Abs(from.Lng - to.Lng) < 0.000001)
            return [new(mode, null, null, from.Name, to.Name, departure, departure, false, null, 0, 0, 0, null, null, [new(from.Lat, from.Lng)])];
        var response = await roads.PreviewAsync(new JsonObject { ["locations"] = new JsonArray(
            new JsonObject { ["lat"] = from.Lat, ["lon"] = from.Lng }, new JsonObject { ["lat"] = to.Lat, ["lon"] = to.Lng }) }, mode, ct);
        var route = response.Array("routes")![0]!;
        var seconds = ReadNonnegative(route, "duration"); var meters = ReadNonnegative(route, "distance");
        var geometry = Decode(route.Str("geometry"), 1e6);
        if (geometry.Count < 2) throw new ServiceException("Road route data is incomplete.");
        return [new(mode, null, null, from.Name, to.Name, departure, departure.AddSeconds(seconds), false, null, meters, seconds, 0, null, null, geometry)];
    }

    private async Task<IReadOnlyList<JourneyStep>> TransitAsync(JourneyPoint from, JourneyPoint to, DateTimeOffset departure, CancellationToken ct)
    {
        JsonObject Location(JourneyPoint p) => new() { ["label"] = p.Name, ["location"] = new JsonObject { ["coordinate"] = new JsonObject { ["latitude"] = p.Lat, ["longitude"] = p.Lng } } };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await client.PostAsJsonAsync(OtpEndpoint, new JsonObject { ["query"] = Query, ["variables"] = new JsonObject {
                ["origin"] = Location(from), ["destination"] = Location(to), ["dateTime"] = new JsonObject { ["earliestDeparture"] = departure.ToString("O", CultureInfo.InvariantCulture) } } }, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new ServiceException("Could not connect to the open transport service. Check the OTP service and GTFS data.");
            var body = await response.Content.ReadFromJsonAsync<JsonObject>(timeout.Token);
            if (body?.Array("errors") is { Count: > 0 }) throw new ServiceException("The transport query failed. Check the OTP version and data coverage.");
            var node = body?.Object("data")?.Object("planConnection")?.Array("edges")?.FirstOrDefault()?.Object("node");
            return ParseTransit(node?.Array("legs"), departure);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServiceException("The transport query timed out. Please try again later."); }
        catch (HttpRequestException) { throw new ServiceException("Could not connect to the open transport service. Check the OTP configuration."); }
        catch (JsonException) { throw new ServiceException("The open transport service returned invalid data."); }
    }

    public static IReadOnlyList<JourneyStep> ParseTransit(JsonArray? raw, DateTimeOffset departure)
    {
        if (raw is null || raw.Count == 0) throw new ServiceException("No services are available for this date or journey. Check timetable coverage or choose another transport mode.");
        var result = new List<JourneyStep>(); var cursor = departure; var transit = false;
        foreach (var leg in raw)
        {
            if (leg is not JsonObject item) throw new ServiceException("Transport leg data is incomplete.");
            var mode = item.Str("mode") switch { "WALK" => "walk", "BUS" => "bus", "RAIL" => "rail", "SUBWAY" => "metro", "TRAM" => "tram", "FERRY" => "ferry", _ => "" };
            if (mode.Length == 0) throw new ServiceException("This route contains an unsupported transport mode. Please choose another route.");
            // Demand-responsive trips cannot be presented as a normal scheduled bus service.
            if (item.Str("pickupType") is "CALL_AGENCY" or "COORDINATE_WITH_DRIVER" || item.Str("dropoffType") is "CALL_AGENCY" or "COORDINATE_WITH_DRIVER")
                throw new ServiceException("This route includes a service that requires a booking. Confirm with the operator and arrange it separately.");
            var start = ReadTime(item, "startTime"); var end = ReadTime(item, "endTime");
            if (start < cursor || end < start) throw new ServiceException("Transport service times are inconsistent. Please search again.");
            var origin = item.Object("from"); var destination = item.Object("to");
            if (string.IsNullOrWhiteSpace(origin.Str("name")) || string.IsNullOrWhiteSpace(destination.Str("name"))) throw new ServiceException("Boarding or arrival stop details are incomplete.");
            var route = item.Object("route"); var line = route.Str("shortName", route.Str("longName"));
            if (mode != "walk") transit = true;
            var geometry = Decode(item.Object("legGeometry").Str("points"), 1e5);
            // Missing geometry stays empty: never invent a straight navigable path over a gap.
            result.Add(new(mode, string.IsNullOrWhiteSpace(line) ? null : line, item.Str("headsign"), origin.Str("name"), destination.Str("name"),
                start, end, item.Bool("realTime"), mode == "walk" ? null : item.Array("intermediateStops")?.Count + 1,
                ReadNonnegative(item, "distance"), (end - start).TotalSeconds, (start - cursor).TotalSeconds,
                origin.Object("stop").Str("platformCode"), route.Object("agency").Str("name"), geometry));
            cursor = end;
        }
        if (!transit) throw new ServiceException("No public transport services were found. You can switch to walking.");
        return result;
    }
    private static JourneyPoint ParsePoint(JsonNode? node)
    {
        if (node is not JsonObject p || p["lat"] is not JsonValue lat || !lat.TryGetValue<double>(out var a)
            || p["lng"] is not JsonValue lng || !lng.TryGetValue<double>(out var b)
            || !double.IsFinite(a) || !double.IsFinite(b) || a is < -90 or > 90 || b is < -180 or > 180
            || string.IsNullOrWhiteSpace(p.Str("name")) || p.Str("name").Length > 160) throw new ServiceException("The stop name or coordinates are invalid.");
        if (p.ContainsKey("stayMinutes") && (p["stayMinutes"] is not JsonValue stayValue || !stayValue.TryGetValue<int>(out _))) throw new ServiceException("The stop duration must be a whole number of minutes.");
        var stay = p.Int("stayMinutes");
        if (stay is < 0 or > 1440) throw new ServiceException("The stop duration is invalid.");
        return new(p.Str("name"), a, b, stay);
    }
    private static double ReadNonnegative(JsonNode node, string key)
    {
        if (node[key] is not JsonValue v || !v.TryGetValue<double>(out var value) || !double.IsFinite(value) || value is < 0 or > 100_000_000)
            throw new ServiceException("Transport distance or duration data is incomplete.");
        return value;
    }
    private static DateTimeOffset ReadTime(JsonNode node, string key)
    {
        if (node[key] is not JsonValue value || !value.TryGetValue<long>(out var milliseconds) || milliseconds is < 0 or > 32_503_680_000_000)
            throw new ServiceException("Service time data is incomplete.");
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
    public static IReadOnlyList<JourneyCoordinate> Decode(string encoded, double scale)
    {
        if (encoded.Length > 200_000) throw new ServiceException("The route data is too large.");
        var result = new List<JourneyCoordinate>(); var index = 0; long lat = 0, lng = 0;
        long Next()
        {
            long value = 0; var shift = 0;
            while (true)
            {
                if (index >= encoded.Length || shift > 30) throw new ServiceException("The route encoding is incomplete.");
                var b = encoded[index++] - 63; if (b is < 0 or > 63) throw new ServiceException("The route encoding is invalid.");
                value |= (long)(b & 31) << shift; shift += 5; if (b < 32) break;
            }
            return (value & 1) == 0 ? value >> 1 : ~(value >> 1);
        }
        while (index < encoded.Length)
        {
            lat += Next(); lng += Next(); var a = lat / scale; var b = lng / scale;
            if (a is < -90 or > 90 || b is < -180 or > 180) throw new ServiceException("Route coordinates are invalid.");
            result.Add(new(a, b));
        }
        return result;
    }
}
