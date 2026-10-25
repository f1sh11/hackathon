namespace SydneyBuddy.Api;

/// <summary>
/// 定位共享地点数据、私有配置和 H-MEM 目录；测试可单独指定数据目录。
/// Locates shared assets, private settings and H-MEM state; tests can isolate mutable data.
/// </summary>
public sealed class ProjectPaths
{
    public string RepoRoot { get; }
    public string ServerDirectory => Path.Combine(RepoRoot, "server");
    public string DataDirectory { get; }
    public string HmemDirectory => Path.Combine(DataDirectory, ".hmem");
    public string SecretsFile { get; }

    public ProjectPaths(string repoRoot, string? dataDirectory = null)
    {
        RepoRoot = Path.GetFullPath(repoRoot);
        DataDirectory = Path.GetFullPath(dataDirectory ?? ServerDirectory);
        SecretsFile = Path.Combine(dataDirectory is null ? RepoRoot : DataDirectory, ".secrets.json");
    }

    public static string DiscoverRoot()
    {
        // dotnet run 与已发布 DLL 的工作目录不同。 / Support both dotnet run and published binaries.
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(candidate); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "server", "places.json")))
                    return directory.FullName;
        }
        throw new InvalidOperationException("找不到 server/places.json；请在仓库中运行或设置 SYDNEYBUDDY_ROOT。");
    }
}
