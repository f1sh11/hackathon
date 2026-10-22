using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 在兼容原客户端的 JSON 边界读取标量；业务规则由对应服务验证。
/// Reads scalar values at the legacy JSON boundary; services own domain validation.
/// </summary>
public static class JsonExtensions
{
    public static string Str(this JsonNode? node, string name, string fallback = "") =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var result) ? result : fallback;

    public static int Int(this JsonNode? node, string name, int fallback = 0) =>
        node?[name] is { } value && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;

    public static double Number(this JsonNode? node, string name, double fallback = 0) =>
        node?[name] is { } value && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            && double.IsFinite(result) ? result : fallback;

    public static bool Bool(this JsonNode? node, string name, bool fallback = false) =>
        node?[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    public static JsonObject? Object(this JsonNode? node, string name) => node?[name] as JsonObject;
    public static JsonArray? Array(this JsonNode? node, string name) => node?[name] as JsonArray;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
