using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class DailyPacing
{
    public static bool LongDay(JsonObject p)=>p.Str("dayStyle")!="halfDay"&&p.Int("endMinute")>=1140;
    public static bool Tired(JsonObject p)=>p.Str("energyFeedback")=="tired"||TravelAdjustmentIntent.Lighter(p.Str("request"));
    public static bool Energized(JsonObject p)=>p.Str("energyFeedback")=="energized"||TravelAdjustmentIntent.MoreEnergy(p.Str("request"));
    public static bool Low(JsonObject p)=>Tired(p)||!Energized(p)&&(p.Str("energy") is "低精力" or "low");
    public static int ReferenceSteps(JsonObject p)=>Low(p)?8000:Energized(p)||p.Str("energy") is "高精力" or "high"?15000:10000;
    public static int Target(JsonObject p)=>Math.Min(p.Int("endMinute"),p.Int("targetReturnMinute",1200));
    public static double Coverage(JsonObject day,JsonObject p)
    {
        if(!LongDay(p))return 0;
        var parts=day.Str("finishTime").Split(':');var finish=int.Parse(parts[0])*60+int.Parse(parts[1]);
        var productive=day.Array("stops")!.OfType<JsonObject>().Sum(s=>s.Int("visitMinutes"));
        // Long waits do not improve a day. Fatigue suppresses the incentive to extend it.
        return Low(p)?0:Math.Min(productive,Target(p)-p.Int("startMinute"))*.12-Math.Max(0,Target(p)-finish)*.12-day.Int("waitingMinutes")*.12-Math.Max(0,finish-Target(p))*.5;
    }
    public static void Annotate(JsonObject day,JsonObject p)
    {
        var parts=day.Str("finishTime").Split(':');var finish=int.Parse(parts[0])*60+int.Parse(parts[1]);
        var target=Target(p);var hotel=p.Object("hotel");
        var hotelEndpoint=hotel is not null&&RouteService.Distance(hotel,day.Object("end")!)<50;
        day["targetReturnTime"]=$"{target/60:00}:{target%60:00}";
        day["returnLocationStatus"]=hotel is null?"provisional_endpoint":!hotelEndpoint?"specified_endpoint":p.Str("accommodationStatus")=="booked"?"booked_hotel":"selected_unbooked_hotel";
        day["energyFeedback"]=p.Str("energyFeedback","normal");day["unfilledMinutes"]=Math.Max(0,target-finish);
        day["returnTargetStatus"]=finish>target?"later_than_target":target-finish<=60?"near_target":Low(p)?"early_for_energy":"early_constraints";
        if(hotel is null)day.Array("warnings")!.Add("飯店尚未指定：已計入返回暫定終點的路程，不能視為已驗證回到飯店；請選定住宿後重算。");
        else if(!hotelEndpoint)day.Array("warnings")!.Add("本日保留使用者指定終點，與飯店不同；返程時間不是抵達飯店時間，請核對抵離日安排。");
        else if(p.Str("accommodationStatus")!="booked")day.Array("warnings")!.Add("住宿為使用者選定但未預訂的候選；已計入返程，空房與房價仍待確認。");
        if(LongDay(p)&&target-finish>60)day.Array("warnings")!.Add(Low(p)?"依精力回饋提前結束並保留休息；20:00 為彈性目標，不超過體力上限。":"距離返程目標仍有空白：候選、營業、天氣、預算、交通或步數限制未能填滿；不虛增停留或等待時間，仍為待補充草稿。");
    }
}
