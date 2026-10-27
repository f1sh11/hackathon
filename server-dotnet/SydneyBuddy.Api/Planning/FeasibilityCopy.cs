using System.Globalization;
namespace SydneyBuddy.Api;

/// English presentation only. Stable issue codes remain the integration contract.
public static class FeasibilityCopy {
    public static string Message(string code,int day,double? actual,double? limit) {
        string N(double? value)=>value?.ToString("0.##",CultureInfo.InvariantCulture) ?? "unknown";
        var prefix=day>=0?$"Day {day+1}: ":"";
        var message=code switch {
            "daily_budget_exceeded"=>$"estimated activity costs are A${N(actual)} per person, above your A${N(limit)} daily budget.",
            "daily_steps_exceeded" or "walking_distance_exceeds_steps"=>$"the plan needs about {N(actual)} steps, above your {N(limit)}-step daily limit.",
            "date_requires_confirmation"=>"please confirm an exact departure date before we check the plan.",
            "budget_basis_requires_normalization"=>"please confirm the activity budget in AUD per person per day. We haven't assumed an exchange rate.",
            "daily_time_window_conflict"=>"the end time needs to be later than the start time.",
            "travel_date_mismatch" or "travel_day_count_mismatch"=>"the itinerary dates don't match your confirmed travel dates.",
            "duplicate_day"=>"the same travel date appears more than once.",
            "duplicate_place"=>"the same place has been scheduled more than once.",
            "avoided_place"=>"the plan includes a place you asked to avoid.",
            "must_visit_missing"=>"a must-visit place is missing from the plan.",
            "time_overlap_or_travel_gap"=>"the schedule doesn't leave enough time for travel or the planned visit.",
            "return_time_exceeded"=>"the planned return is later than your confirmed finish time.",
            "return_time_underreported"=>"the displayed return time is earlier than the route allows.",
            "steps_underreported"=>"the displayed step estimate is lower than the walking routes require.",
            "invalid_activity_value"=>"an activity has an invalid cost or visit duration.",
            "outside_opening_hours"=>"a visit falls outside the supplied opening hours.",
            "opening_hours_unknown"=>"opening hours haven't been verified. Please check before visiting.",
            "opening_evidence_missing"=>"we couldn't find the evidence behind the claimed opening hours.",
            "price_not_verified"=>"some prices are planning estimates or unknown. They aren't confirmed quotes or proof that entry is free.",
            "pace_density"=>"this day may feel busy for your relaxed pace. Consider leaving more breathing room.",
            "transfer_density"=>"this day has several transfers. A simpler route may feel easier.",
            "transport_preference_violated"=>"the transport choices don't match your confirmed preference.",
            "route_unreachable"=>"a planned connection isn't reachable using the available route evidence.",
            "walking_evidence_missing"=>"a verified walking connection is missing. Ask the planner to refresh the Apple Maps routes.",
            "walking_evidence_invalid_or_expired"=>"the walking evidence is invalid or out of date. Please refresh it.",
            "route_evidence_mismatch"=>"the displayed route doesn't match its walking evidence.",
            "transit_refresh_required"=>"the public transport connections still need a timetable check.",
            "transit_unavailable"=>"we couldn't verify a usable public transport connection for this departure.",
            "transit_timing_changed"=>"the refreshed public transport connection takes longer than the plan allows.",
            "transit_query_limit"=>"this review reached its transit-check limit. Please check the remaining connections separately.",
            "unsupported_transport_evidence"=>"this transport mode needs evidence that the current checker can't verify.",
            "place_identity_unverified"=>"we couldn't match a planned stop to its source place record.",
            "place_identity_coordinate_conflict"=>"a place's identity and map coordinates don't agree.",
            "source_price_mismatch"=>"a planned cost doesn't match the supplied source price.",
            "schedule_conflict" or "schedule_snapshot_mismatch"=>"the saved timeline and the itinerary disagree. Ask the planner to rebuild the affected day.",
            "accessibility_needs_unverified"=>"your accessibility needs haven't been verified for this plan. We can't promise step-free access.",
            "dietary_needs_unverified"=>"your dietary or allergy requirements haven't been verified. Please confirm them with the venue.",
            _ when code.EndsWith("_timeout",StringComparison.Ordinal)=>"a verification tool timed out. Your draft is kept; please try again.",
            _ when code.EndsWith("_unavailable",StringComparison.Ordinal)=>"a verification source is temporarily unavailable. We haven't marked it as checked.",
            _=>"this part of the plan needs another check before you confirm."
        };
        return prefix+(prefix.Length>0?message:char.ToUpperInvariant(message[0])+message[1..]);
    }
    public static string Action(string action)=>action switch {
        "replace_optional_paid_stop"=>"Replace an optional paid stop",
        "request_mapkit_evidence"=>"Refresh walking routes",
        "refresh_or_change_transport"=>"Check another transport connection",
        "include_required_place"=>"Include the must-visit place",
        "reorder_or_shorten_optional_stops"=>"Adjust the order or visit times",
        _=>"Review the affected details"
    };
}
