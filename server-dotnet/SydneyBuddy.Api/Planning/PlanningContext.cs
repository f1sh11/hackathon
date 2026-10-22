using System.Globalization;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 一次规划操作的状态：模式、配置快照、证据轨迹和路段复用；绝不跨请求共享。
/// Holds one operation's mode, configuration snapshot, trace and route reuse; never shared across requests.
/// </summary>
public sealed class PlanningContext(string mode, IReadOnlyDictionary<string, string> config)
{
    public string Mode { get; } = mode;
    public IReadOnlyDictionary<string, string> Config { get; } = new Dictionary<string, string>(config);
    public string Now { get; } = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    public JsonArray Trace { get; } = [];
    internal Dictionary<(double, double, double, double), JsonObject> RouteCache { get; } = [];

    public void Record(string title, string detail, string source) => Trace.Add(new JsonObject
    {
        ["title"] = title, ["detail"] = detail, ["source"] = source, ["fetchedAt"] = Now
    });
}
