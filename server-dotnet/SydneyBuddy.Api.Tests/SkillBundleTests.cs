using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;
public sealed class SkillBundleTests:IDisposable
{
    readonly string path=Path.Combine(Path.GetTempPath(),"skill-bundle-"+Guid.NewGuid()+".json");
    void Write(string version="test.1",string? basis=null)=>File.WriteAllText(path,new JsonObject{["baseVersion"]=basis??ItinerarySkill.BaseVersion,["version"]=version,["enAppendix"]="Additional test rule.",["zhHantAppendix"]="新增測試規則。"}.ToJsonString());
    [Fact]public void OverrideAlwaysPreservesFullEmbeddedRulesInBothLanguages(){
        var baseline=ItinerarySkill.Load(null);Write();var updated=ItinerarySkill.Load(path);
        Assert.StartsWith(baseline.English,updated.English);Assert.StartsWith(baseline.Chinese,updated.Chinese);
        Assert.EndsWith("Additional test rule.",updated.Read("en"));Assert.EndsWith("新增測試規則。",updated.Read("zh-Hant"));
    }
    [Fact]public async Task ActiveRequestSnapshotAndVersionDoNotChangeMidRequest(){
        Write();using var first=ItinerarySkill.Use(ItinerarySkill.Load(path));var oldHash=ItinerarySkill.Hash("en");
        Write("test.2");await Task.Yield();Assert.Equal("test.1",ItinerarySkill.Version);Assert.Equal(oldHash,ItinerarySkill.Hash("en"));
        using(ItinerarySkill.Use(ItinerarySkill.Load(path)))Assert.Equal("test.2",ItinerarySkill.Version);
        Assert.Equal("test.1",ItinerarySkill.Version);
    }
    [Theory][InlineData("test.1","wrong-base")][InlineData("bad/version","2026-10-27.5")][InlineData("2026-10-27.5","2026-10-27.5")]
    public void InvalidOrIncompatibleBundleCannotSilentlyReplaceTheSkill(string version,string basis){Write(version,basis);Assert.Throws<ServiceException>(()=>ItinerarySkill.Load(path));}
    [Fact]public void MissingAndMalformedBundlesAreDistinguished(){
        Assert.Equal("embedded",ItinerarySkill.Load(path).Source);File.WriteAllText(path,"{");Assert.Throws<ServiceException>(()=>ItinerarySkill.Load(path));
    }
    public void Dispose(){if(File.Exists(path))File.Delete(path);}
}
