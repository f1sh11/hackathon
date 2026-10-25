using System.Text.RegularExpressions;
namespace SydneyBuddy.Api;
public static class TravelAdjustmentIntent
{
    public static bool Lighter(string text)=>!Regex.IsMatch(text,@"不累|并不累|不想休息|不用少走|不需要少走|不想轻松|not tired|don't.*(?:rest|less walking)",RegexOptions.IgnoreCase)&&Regex.IsMatch(text,@"少走|有点累|好累|太累|走累|轻松|休息|\btired\b|less walking|take it easy",RegexOptions.IgnoreCase);
    public static bool Rain(string text)=>!Regex.IsMatch(text,@"不下雨|没有下雨|不用避雨|not raining",RegexOptions.IgnoreCase)&&Regex.IsMatch(text,@"下雨|避雨|\brain(?:ing)?\b|indoors",RegexOptions.IgnoreCase);
    public static int? RemainingMinutes(string text) {
        var match=Regex.Match(text,@"(?:只剩|只有|剩下?|还有)\s*([0-9]+|一|两|二|三|四)\s*(小时|分钟)|([0-9]+)\s*(hours?|minutes?)\s*(?:left|remaining)",RegexOptions.IgnoreCase);
        if(!match.Success)return null;
        var n=match.Groups[1].Success?match.Groups[1].Value:match.Groups[3].Value;
        if(!int.TryParse(n,out var count))count=n is "一"?1:n is "两" or "二"?2:n is "三"?3:4;
        var hours=match.Groups[2].Value=="小时"||match.Groups[4].Value.StartsWith("hour",StringComparison.OrdinalIgnoreCase);
        if(count<=0||count>1440)throw new ServiceException("请填写有效的剩余时间。");
        return Math.Min(1440,count*(hours?60:1));
    }
}
