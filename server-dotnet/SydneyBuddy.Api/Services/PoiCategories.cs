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
            "parks"=>kind is "park" or "national_park" or "state_park" or "city_park" or "botanical_garden" or "garden" or "nature_reserve",
            "beaches"=>kind=="beach",
            "hotels"=>kind is "hotel" or "motel" or "guest_house" or "resort_hotel",
            "hostels"=>kind=="hostel",
            "supermarkets"=>kind is "supermarket" or "grocery_store" or "food_market" or "convenience" or "convenience_store",
            "toilets"=>kind is "toilets" or "public_restroom" or "public_bathroom",
            "drinking_water"=>kind is "drinking_water" or "water_fountain",
            _=>false};
    }

    static readonly HashSet<string> Tier1Kinds=new(StringComparer.OrdinalIgnoreCase){
        "cafe","coffee_shop","bakery","pastry","pastry_shop","cake_shop","dessert_shop","ice_cream_shop",
        "bubble_tea_shop","juice_bar","smoothie_juice_bar","sandwich_shop","bagel_shop","donut_shop",
        "fast_food","fast_food_restaurant","food_court","food_stand","food_truck","kebab_shop",
        "fish_and_chips_restaurant","pie_shop","hot_dog_stand","burger_restaurant","chicken_restaurant",
        "noodle_restaurant","dumpling_restaurant","ramen_restaurant","pho_restaurant","snack_bar","deli","delicatessen","takeaway","cafeteria","tea_house"
    };
    static readonly HashSet<string> Tier3Kinds=new(StringComparer.OrdinalIgnoreCase){
        "steakhouse","seafood_restaurant","french_restaurant","fine_dining_restaurant","modern_australian_restaurant",
        "modern_european_restaurant","sushi_restaurant","japanese_bbq_restaurant","korean_bbq_restaurant",
        "wine_bar","cocktail_bar","tapas_bar","bistro","brasserie","oyster_bar","teppanyaki_restaurant"
    };
    static readonly HashSet<string> Tier4Kinds=new(StringComparer.OrdinalIgnoreCase){
        "fine_dining","haute_cuisine_restaurant","omakase_restaurant"
    };

    public static (int Tier,string Symbol,int PerPersonAud,int ForTwoAud)? ClassifyFoodPriceTier(string? poiKind,string? name,string? category){
        if(category!="美食"&&category!="food")return null;
        var kind=(poiKind??"restaurant").Trim().ToLowerInvariant().Replace(' ','_');
        var lowerName=(name??"").Trim().ToLowerInvariant();
        int tier;
        if(Tier4Kinds.Contains(kind)||lowerName.Contains("omakase")||lowerName.Contains("fine dining")||lowerName.Contains("sixpenny")||lowerName.Contains("quay restaurant")||lowerName.Contains("aria restaurant")||lowerName.Contains("bennelong")||lowerName.Contains("tetsuya")||lowerName.Contains("firedoor"))tier=4;
        else if(Tier3Kinds.Contains(kind)||lowerName.Contains("steakhouse")||lowerName.Contains("seafood")||lowerName.Contains("bistro")||lowerName.Contains("brasserie")||lowerName.Contains("oyster")||lowerName.Contains("grill & bar")||lowerName.Contains("6 head")||lowerName.Contains("harbourfront")||lowerName.Contains("rockpool")||lowerName.Contains("mr. wong")||lowerName.Contains("hubert")||lowerName.Contains("nomad")||lowerName.Contains("chin chin"))tier=3;
        else if(Tier1Kinds.Contains(kind)||lowerName.Contains("cafe")||lowerName.Contains("café")||lowerName.Contains("coffee")||lowerName.Contains("espresso")||lowerName.Contains("bakery")||lowerName.Contains("patisserie")||lowerName.Contains("pork roll")||lowerName.Contains("banh mi")||lowerName.Contains("kebab")||lowerName.Contains("mcdonald")||lowerName.Contains("kfc")||lowerName.Contains("subway")||lowerName.Contains("hungry jack")||lowerName.Contains("guzman")||lowerName.Contains("sushi hub")||lowerName.Contains("gong cha")||lowerName.Contains("chatime")||lowerName.Contains("gelato")||lowerName.Contains("messina")||lowerName.Contains("san churro")||lowerName.Contains("kurtos"))tier=1;
        else tier=2;
        return tier switch{
            1=>(1,"$",20,40),
            3=>(3,"$$$",75,150),
            4=>(4,"$$$$",120,240),
            _=>(2,"$$",40,80)
        };
    }

    static readonly string[] Tier1LodgingNames={"hostel","backpacker","yha","wake up!","mad monkey","urban nomad","podbed","tequila sunrise","billabong gardens","unilodge","student living","student accomodation","budget hotel","budget accomodation","ibis budget","cb hotel","central perk"};
    static readonly string[] Tier3LodgingNames={"meriton","skye suites","fraser suites","pullman","crowne plaza","hyatt regency","caption by hyatt","west hotel","radisson","amora","novotel","parkroyal","ovolo","ace hotel","crystalbrook","little national","harbour rocks","grace hotel","the sebel","adina","mantra","citadines","rydges","vibe hotel","larmont"};
    static readonly string[] Tier4LodgingNames={"park hyatt","crown sydney","crown towers","four seasons","capella","shangri-la","the langham","intercontinental","sofitel","w hotel","w sydney","the darling","fullerton","kimpton","pier one","pullman quay grand","sheraton","hilton sydney","marriott","swissotel","wildlife retreat at taronga","qt sydney","spicers"};

    public static (int Tier,string Symbol,int NightlyRateAud,int PerPersonAud)? ClassifyLodgingPriceTier(string? poiKind,string? name,string? category){
        if(category!="住宿"&&category!="lodging")return null;
        var kind=(poiKind??"hotel").Trim().ToLowerInvariant().Replace(' ','_');
        var lowerName=(name??"").Trim().ToLowerInvariant();
        int tier;
        if(kind is "resort_hotel" or "luxury_hotel"||Tier4LodgingNames.Any(lowerName.Contains))tier=4;
        else if(kind is "hostel" or "dormitory" or "youth_hostel" or "backpacker_hostel"||Tier1LodgingNames.Any(lowerName.Contains))tier=1;
        else if(Tier3LodgingNames.Any(lowerName.Contains))tier=3;
        else tier=2;
        return tier switch{
            1=>(1,"$",65,65),
            3=>(3,"$$$",295,148),
            4=>(4,"$$$$",620,310),
            _=>(2,"$$",185,95)
        };
    }

    public static void ApplyLodgingPriceMapping(JsonObject point){
        var category=point.Str("category");
        if(category!="住宿"&&point.Str("searchCategory")!="lodging")return;
        if(ClassifyLodgingPriceTier(point.Str("poiKind"),point.Str("name"),"住宿") is not {} mapped)return;
        if(point["priceRange"] is null)point["priceRange"]=mapped.Tier;
        if(point.Str("priceTier").Length==0)point["priceTier"]=mapped.Symbol;
        if(point["nightlyRateAud"] is null)point["nightlyRateAud"]=mapped.NightlyRateAud;
        if(point["estimatedSpendAud"] is null)point["estimatedSpendAud"]=mapped.NightlyRateAud;
        if(point.Str("priceLabel").Length==0)point["priceLabel"]=$"{mapped.Symbol} · 預估每晚 A${mapped.NightlyRateAud}/晚";
    }

    // A category, place name or deterministic hash is never evidence of a traveller review.
    public static void ApplyPlaceRatingMapping(JsonObject point){
        var supplied=point.Str("ratingSource").Length>0 && Uri.TryCreate(point.Str("ratingSourceUrl"),UriKind.Absolute,out var uri) && uri.Scheme=="https" && uri.UserInfo.Length==0;
        var rating=point.Number("aggregateRating",double.NaN);
        if(supplied && double.IsFinite(rating) && rating>=0 && rating<=5 && point.Int("reviewCount")>0){
            point["ratingStatus"]="source_provided";point.Remove("ratingLabel");return;
        }
        foreach(var key in new[]{"aggregateRating","reviewCount","ratingLabel","reviewSummary","reviewHighlights"})point.Remove(key);
        point["ratingStatus"]="unavailable";
    }

    public static void ApplyFoodPriceMapping(JsonObject point){
        ApplyPlaceRatingMapping(point);
        var category=point.Str("category");
        if(category=="住宿"||point.Str("searchCategory")=="lodging"){ApplyLodgingPriceMapping(point);return;}
        if(category!="美食"&&point.Str("searchCategory")!="food")return;
        if(ClassifyFoodPriceTier(point.Str("poiKind"),point.Str("name"),"美食") is not {} mapped)return;
        if(point["priceRange"] is null)point["priceRange"]=mapped.Tier;
        if(point.Str("priceTier").Length==0)point["priceTier"]=mapped.Symbol;
        if(point["estimatedSpendAud"] is null)point["estimatedSpendAud"]=mapped.PerPersonAud;
        if(point["avgCostForTwoAud"] is null)point["avgCostForTwoAud"]=mapped.ForTwoAud;
        if(point.Str("priceLabel").Length==0)point["priceLabel"]=$"{mapped.Symbol} · 預估均消 A${mapped.PerPersonAud}/人";
    }
}
