using SydneyBuddy.Api;
namespace SydneyBuddy.Api.Tests;

public sealed class DeploymentPathsTests
{
    [Fact]
    public void PrivateStateCanPersistOutsideTheReadOnlySourceTree()
    {
        var state = Path.Combine(Path.GetTempPath(), "oracle-state-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(Path.GetTempPath(), "source-does-not-exist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);
        try
        {
            var paths = new ProjectPaths(root, state);
            new SecretStore(paths).Save(new Dictionary<string,string>{{"NVIDIA_API_KEY", "synthetic-test-key"}});
            Assert.False(Directory.Exists(root));
            Assert.Equal("synthetic-test-key", new SecretStore(new ProjectPaths(root, state)).Read()["NVIDIA_API_KEY"]);
            Assert.StartsWith(state, paths.HmemDirectory);
        }
        finally { Directory.Delete(state, true); }
    }

    [Fact]
    public void DefaultLocalSettingsKeepTheirExistingLocation()
    {
        var root = Path.GetTempPath();
        Assert.Equal(Path.Combine(root, ".secrets.json"), new ProjectPaths(root).SecretsFile);
    }
}
