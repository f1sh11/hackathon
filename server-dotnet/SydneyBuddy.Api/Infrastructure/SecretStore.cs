using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SydneyBuddy.Api;

/// <summary>
/// 读取环境变量和私有 JSON，原子保存配置；从不向客户端返回密钥。
/// Reads environment/private JSON configuration and saves atomically, without exposing secrets.
/// </summary>
public sealed partial class SecretStore(ProjectPaths paths)
{
    public const string DefaultModel = "nvidia/nemotron-3.5-lightning-30b-a3b";
    private static readonly string[] Keys = ["NVIDIA_API_KEY", "GOOGLE_SERVER_KEY", "GOOGLE_IOS_KEY", "NVIDIA_MODEL"];
    private readonly object gate = new();
    public string SetupToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public Dictionary<string, string> Read()
    {
        lock (gate)
        {
            var local = File.Exists(paths.SecretsFile)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(paths.SecretsFile)) ?? [] : [];
            return Keys.ToDictionary(key => key, key => Environment.GetEnvironmentVariable(key) ?? local.GetValueOrDefault(key, ""));
        }
    }

    public void Save(IReadOnlyDictionary<string, string> updates)
    {
        lock (gate)
        {
            var current = Read();
            foreach (var key in Keys.Where(key => key != "NVIDIA_MODEL"))
            {
                var value = updates.GetValueOrDefault(key, "").Trim();
                if (value.Length == 0) continue; // 留空保留已有值。 / Empty fields preserve existing settings.
                if (!KeyFormat().IsMatch(value)) throw new ServiceException("密钥格式不合法");
                current[key] = value;
            }
            WritePrivate(paths.SecretsFile, JsonSerializer.Serialize(current));
            var ios = Path.Combine(paths.RepoRoot, "ios", "Config");
            if (Directory.Exists(ios) && current["GOOGLE_IOS_KEY"].Length > 0)
                WritePrivate(Path.Combine(ios, "Secrets.xcconfig"), "GOOGLE_MAPS_API_KEY = " + current["GOOGLE_IOS_KEY"] + "\n");
        }
    }

    private static void WritePrivate(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            using (var writer = new StreamWriter(stream)) writer.Write(content);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex KeyFormat();
}
