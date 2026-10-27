using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class MealTiming
{
    public sealed record Slot(string Name,int Open,int Close,int Target);
    public static Slot[] Slots(JsonObject profile)
    {
        var custom=profile.Object("mealTimes");
        var defaults=new[]{new Slot("breakfast",480,630,540),new Slot("lunch",690,870,720),new Slot("dinner",1020,1170,1080)};
        return defaults.Select(s=>custom?[s.Name] is not null?new Slot(s.Name,Math.Max(0,custom.Int(s.Name)-45),Math.Min(1440,custom.Int(s.Name)+90),custom.Int(s.Name)):s)
            .Where(s=>profile.Int("startMinute")+45<=s.Close&&profile.Int("endMinute")>=s.Open+45).ToArray();
    }
    public static void Validate(JsonObject profile)
    {
        if(profile["mealTimes"] is null)return;
        var times=profile.Object("mealTimes")??throw new ServiceException("用餐時間格式無效。");
        foreach(var item in times){var n=times.Number(item.Key,double.NaN);if(item.Key is not ("breakfast" or "lunch" or "dinner")||!double.IsFinite(n)||n!=Math.Truncate(n)||n is <0 or >1439)throw new ServiceException("請用當地午夜後的分鐘數設定早餐、午餐或晚餐時間。");}
    }
    public static void Annotate(JsonObject day,JsonObject profile)
    {
        int Minute(string time){var parts=time.Split(':');return int.Parse(parts[0])*60+int.Parse(parts[1]);}
        var slots=new JsonArray();var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();
        foreach(var slot in Slots(profile)){
            var meal=stops.FirstOrDefault(s=>RecommendationQuality.IsMeal(s)&&Minute(s.Str("arrival"))>=slot.Open&&Minute(s.Str("departure"))<=slot.Close);
            var status=meal is not null?"restaurant_planned":Minute(day.Str("finishTime"))<=slot.Open?"after_return_unselected":"time_slot_unselected";
            slots.Add(new JsonObject{["meal"]=slot.Name,["targetMinute"]=slot.Target,["windowStart"]=slot.Open,["windowEnd"]=slot.Close,["status"]=status,["placeId"]=meal?.Str("id"),["reservationConfirmed"]=false});
            if(status=="time_slot_unselected")day.Array("warnings")!.Add("準時用餐尚待補齊（"+slot.Name+"）：未找到符合時段、交通與預算的餐廳，不能把景點或寄放任務當成用餐。");
        }
        day["mealSlots"]=slots;
    }
}
