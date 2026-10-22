using System.Globalization;
using Roam.Api.Contracts;
using Roam.Core;

namespace Roam.Api.Features.Trips;

internal static class TripRequestValidation
{
    internal static TripRequestSnapshot Validate(CreateTripRequest request)
    {
        var title = request.Title?.Trim() ?? "";
        if (title.Length is < 1 or > 80) throw new ArgumentException("任务名称需要 1–80 个字符。");
        if (request.ProfileId == Guid.Empty) throw new ArgumentException("请先创建旅行画像。");
        if (request.IdempotencyKey == Guid.Empty) throw new ArgumentException("创建任务需要有效的请求编号。");
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("请选择有效的出行日期。");
        if (!TimeOnly.TryParseExact(request.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(request.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            throw new ArgumentException("时间格式应为 HH:mm。");
        if (end <= start) throw new ArgumentException("第一版仅支持同一天行程，最晚返回需要晚于出发时间。");
        if (request.Budget is < 0 or > 10000 || decimal.Round(request.Budget, 2) != request.Budget)
            throw new ArgumentException("预算需为 0–10000 澳元，最多两位小数。");
        if (request.Energy is < 10 or > 100) throw new ArgumentException("本次精力需要在 10–100 之间。");
        if (request.Theme is not ("harbour" or "culture" or "nature")) throw new ArgumentException("请选择海港、文化或自然主题。");
        if ((request.Notes?.Length ?? 0) > 500) throw new ArgumentException("备注最多 500 个字符。");

        return new(request.ProfileId, title, date.ToString("yyyy-MM-dd"), start.ToString("HH:mm"),
            end.ToString("HH:mm"), request.Budget, request.Energy, request.Theme, request.Notes?.Trim() ?? "");
    }

    internal static PlanningRequest ToPlanning(TripRequestSnapshot snapshot, bool easier) => new(
        DateOnly.ParseExact(snapshot.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(snapshot.StartTime, "HH:mm", CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(snapshot.EndTime, "HH:mm", CultureInfo.InvariantCulture),
        snapshot.Budget, snapshot.Energy, snapshot.Theme, snapshot.Notes, easier);
}
