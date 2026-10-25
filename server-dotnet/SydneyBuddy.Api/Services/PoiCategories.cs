using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>Discovery filters remain separate from the planner's broad activity categories.</summary>
public static class PoiCategories {
    static readonly Dictionary<string,string> Parents=new(StringComparer.Ordinal){
        ["restaurants"]="food",["cafes"]="food",["bakeries"]="food",["bars"]="food",
        ["museums"]="sights",["parks"]="sights",["beaches"]="sights",
        ["hotels"]="lodging",["hostels"]="lodging",["supermarkets"]="shops",["toilets"]="facilities",["drinking_water"]="facilities"};
    public static string Parent(string category)=>Parents.GetValueOrDefault(category,category);
    public static bool Valid(string category)=>category is "all" or "sights" or "gems" or "food" or "lodging" or "shops" or "facilities"||Parents.ContainsKey(category);
    public static bool Matches(JsonObject point,string category){
        if(category=="all"||category=="gems"&&point.Bool("socialHiddenGemClaim"))return true;
        var broad=point.Bool("hiddenGem")||point.Str("source")=="Wikivoyage"?"gems":point.Str("searchCategory",point.Str("category") switch{"美食"=>"food","設施"=>"facilities","住宿"=>"lodging","店家"=>"shops",_=>"sights"});
        if(!Parents.ContainsKey(category))return broad==category || category=="sights"&&broad=="gems" || category=="food"&&broad=="shops"&&Matches(point,"bakeries");
        var kind=point.Str("poiKind").ToLowerInvariant();
        return category switch{
            "restaurants"=>kind is "restaurant" or "fast_food" or "food_court" or "casual_eatery"||kind.EndsWith("_restaurant",StringComparison.Ordinal),
            "cafes"=>kind is "cafe" or "coffee_shop" or "tea_house",
            "bakeries"=>kind is "bakery" or "cake_shop" or "dessert_shop" or "pastry_shop" or "pastry" or "donut_shop",
            "bars"=>kind is "bar" or "pub" or "cocktail_bar" or "wine_bar" or "beer_garden" or "biergarten" or "night_club" or "nightclub",
            "museums"=>kind=="museum"||kind.EndsWith("_museum",StringComparison.Ordinal),
            "parks"=>kind is "park" or "national_park" or "state_park" or "city_park" or "botanical_garden" or "garden",
            "beaches"=>kind=="beach",
            "hotels"=>kind is "hotel" or "motel" or "guest_house" or "resort_hotel",
            "hostels"=>kind=="hostel",
            "supermarkets"=>kind is "supermarket" or "grocery_store" or "food_market" or "convenience" or "convenience_store",
            "toilets"=>kind is "toilets" or "public_restroom" or "public_bathroom",
            "drinking_water"=>kind is "drinking_water" or "water_fountain",
            _=>false};
    }
}
