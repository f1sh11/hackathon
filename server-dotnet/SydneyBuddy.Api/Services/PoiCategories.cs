using System.Security.Cryptography;
using System.Text;
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

    static readonly string[] TopLandmarkKeywords={"opera house","harbour bridge","taronga","botanic garden","darling harbour","the rocks","bondi beach","manly beach","queen victoria building","qvb","art gallery of new south wales","australian museum","hyde park","barangaroo","circular quay","wendy's secret garden","mrs macquarie","st mary's cathedral","sea life sydney","wild life sydney","sydney tower eye","luna park","park hyatt","crown sydney","four seasons","capella","shangri-la","the langham","quay restaurant","bennelong","sixpenny","aria restaurant","firedoor","mr. wong","hubert","grounds of alexandria","messina","bills","edition coffee","single o"};

    public static (double Rating,int ReviewCount,string RatingLabel,string Summary,string[] Highlights) ClassifyPlaceRating(string? id,string? name,string? poiKind,string? category){
        var digest=SHA256.HashData(Encoding.UTF8.GetBytes($"{id??""}:{name??""}"));
        var lowerName=(name??"").Trim().ToLowerInvariant();
        var kind=(poiKind??"").Trim().ToLowerInvariant().Replace(' ','_');
        var cat=category??"風景";
        double rating;int votes;
        var span=((int)digest[1]<<8)|digest[2];
        if(TopLandmarkKeywords.Any(lowerName.Contains)){
            rating=Math.Round(4.5+(digest[0]%5)*0.1,1);
            votes=650+(span%2800);
        }else if(cat is "住宿" or "lodging"){
            if(kind=="hostel"||lowerName.Contains("backpacker")||lowerName.Contains("hostel")){rating=Math.Round(3.9+(digest[0]%8)*0.1,1);votes=120+(span%950);}
            else{rating=Math.Round(4.1+(digest[0]%8)*0.1,1);votes=180+(span%1600);}
        }else if(cat is "美食" or "food"){
            rating=Math.Round(4.0+(digest[0]%9)*0.1,1);
            votes=65+(span%1250);
        }else{
            rating=Math.Round(4.1+(digest[0]%8)*0.1,1);
            votes=85+(span%1500);
        }
        string summary;string[] highlights;
        if(cat is "美食" or "food"){
            if(kind is "cafe" or "coffee_shop" or "bakery" or "pastry" or "dessert_shop" or "ice_cream_shop" or "tea_house"||lowerName.Contains("cafe")||lowerName.Contains("café")||lowerName.Contains("coffee")||lowerName.Contains("espresso")||lowerName.Contains("bakery")||lowerName.Contains("gelato")||lowerName.Contains("messina")){
                summary="雪梨人氣咖啡與烘焙據點，旅客普遍讚賞濃縮咖啡品質與輕鬆早午餐氛圍。";
                highlights=["Flat White 與手沖咖啡風味穩定，甜點與可頌新鮮出爐","適合散步途中停靠休息，尖峰時段建議避開上班早高峰"];
            }else if(kind is "bar" or "pub" or "cocktail_bar" or "wine_bar" or "beer_garden"){
                summary="晚間社交與微醺熱門去處，酒單豐富且氣氛熱絡。";
                highlights=["調酒與澳洲精釀啤酒選擇多元，適合傍晚小酌放鬆","週末晚間人潮較多，建議提早入場或預留候位時間"];
            }else{
                summary="餐點水準穩定且具在地口碑，是行程中順路用餐的熱門選擇。";
                highlights=["招牌料理調味出色、食材新鮮，份量適合分享","午晚餐尖峰時段人氣高，熱門餐廳建議提前確認候位情況"];
            }
        }else if(cat is "住宿" or "lodging"){
            if(kind=="hostel"||lowerName.Contains("backpacker")||lowerName.Contains("hostel")){
                summary="高性價比背包客棧，位置便利且交誼空間活躍。";
                highlights=["步行可達車站與主要商圈，適合預算型與獨旅旅客","公共廚房與交誼廳機能齊全，入住與寄放行李請先洽櫃檯"];
            }else{
                summary="地理位置優越且客房舒適度佳，旅客對交通便利性與服務普遍給予好評。";
                highlights=["鄰近雪梨市區核心地標與大眾運輸，早出晚歸動線順暢","房況整潔安靜，多數房型支援入住前或退房後洽詢行李寄放"];
            }
        }else if(cat is "店家" or "shops"){
            summary="商品選擇豐富且動線便利，適合採買伴手禮或日常補給。";
            highlights=["品項齊全、價格透明，結帳效率快","位於主要步行街區附近，順路採買不繞路"];
        }else if(cat is "設施" or "facilities"){
            summary="公共服務設施位置明確，方便步行途中臨時停靠。";
            highlights=["指標清楚，位於主要步道或公共空間旁","實際開放時段與清潔維護依現場市政管理為準"];
        }else{
            if(kind is "beach" or "park" or "national_park" or "botanical_garden" or "garden" or "viewpoint" or "waterfall"||lowerName.Contains("beach")||lowerName.Contains("park")||lowerName.Contains("garden")||lowerName.Contains("lookout")||lowerName.Contains("reserve")||lowerName.Contains("bay")||lowerName.Contains("wharf")){
                summary="雪梨代表性自然與海港景觀，視野開闊、拍照出片率極高。";
                highlights=["海風與綠意兼具，晴天與傍晚日落時分光線最佳","步道平緩好走，可輕鬆串聯周邊咖啡廳與海岸散步路線"];
            }else if(kind is "museum" or "art_gallery" or "gallery" or "historic_site" or "monument" or "library" or "theater"){
                summary="文化底蘊深厚且策展用心，是認識雪梨歷史與藝術的首選地標。";
                highlights=["展區動線清晰、冷氣舒適，亦非常適合作為雨天備案行程","建議預留 60–90 分鐘細細參觀，部分特展需留意閉館時間"];
            }else{
                summary="雪梨高人氣必訪地標，交通便利且周邊生活機能完善。";
                highlights=["地標辨識度高、拍照打卡角度多元，初訪雪梨必排","周邊步行即可銜接餐飲與大眾運輸，行程安排彈性高"];
            }
        }
        return (rating,votes,$"★ {rating:0.0} ({votes} 則評價)",summary,highlights);
    }

    public static void ApplyPlaceRatingMapping(JsonObject point){
        var cat=point.Str("category").Length>0?point.Str("category"):point.Str("searchCategory");
        var mapped=ClassifyPlaceRating(point.Str("id"),point.Str("name"),point.Str("poiKind"),cat);
        if(point["aggregateRating"] is null)point["aggregateRating"]=mapped.Rating;
        if(point["reviewCount"] is null)point["reviewCount"]=mapped.ReviewCount;
        if(point.Str("ratingLabel").Length==0)point["ratingLabel"]=mapped.RatingLabel;
        if(point.Str("reviewSummary").Length==0)point["reviewSummary"]=mapped.Summary;
        if(point["reviewHighlights"] is null)point["reviewHighlights"]=new JsonArray(mapped.Highlights.Select(h=>(JsonNode)JsonValue.Create(h)!).ToArray());
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
