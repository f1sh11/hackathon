using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
namespace SydneyBuddy.Api.Tests;
public sealed class ZetarisCloudTests {
    static TravelDataContext Select(string mode){var c=new DefaultHttpContext();c.Request.Headers[TravelDataContext.Header]=mode;return new(new HttpContextAccessor{HttpContext=c});}
    sealed class Transport:HttpMessageHandler {
        public List<string> Hosts=[];public List<string> Logins=[];public List<string> Statements=[];public bool EmptyPoi;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){
            Hosts.Add(r.RequestUri!.Host);
            if(r.RequestUri.AbsolutePath.EndsWith("/login")){Logins.Add(await r.Content!.ReadAsStringAsync(t));return new(HttpStatusCode.OK){Content=new StringContent("{\"idToken\":\"test-token\",\"orgId\":\"cloud-org\"}")};}
            var body=await r.Content!.ReadAsStringAsync(t);Statements.Add(body);
            if((JsonNode.Parse(body)?.Str("sql")??"").Contains("SELECT 'poi' AS dataset")){
                var rows=new JsonArray(new JsonArray("poi",new JsonObject{["id"]=r.RequestUri.Host}.ToJsonString()),new JsonArray("catalog","{\"id\":\"sample\"}"),new JsonArray("maps","{\"id\":\"osm\"}"),new JsonArray("metadata","{\"source\":\"Overture Maps\",\"bbox\":[151,-34,152,-33]}"));
                if(EmptyPoi)rows.RemoveAt(0);
                return new(HttpStatusCode.OK){Content=new StringContent(new JsonObject{["data"]=rows,["total"]=rows.Count}.ToJsonString())};
            }
            return new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[[\"sentinel\",\"{}\"]],\"total\":1}")};
        }
    }
    static IConfiguration Config(string api="https://sydney.example.test",string login="https://sydney.example.test/api/auth/login")=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["Zetaris:Username"]="local-user",["Zetaris:Password"]="local-secret",
        ["Zetaris:Cloud:BaseUrl"]=api,["Zetaris:Cloud:LoginUrl"]=login,
        ["Zetaris:Cloud:Username"]="cloud-user",["Zetaris:Cloud:Password"]="cloud-secret",["Zetaris:Cloud:OrgId"]="cloud-org"
    }).Build();
    [Fact]public async Task CloudSelectsOnlyCloudEndpointsAndCredentials(){
        var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        var selection=Select("zetaris-cloud");Assert.True(selection.Warehouse);
        var warehouse=new ZetarisTravelWarehouse(new(h),Config(),cache,selection);
        Assert.Empty(await warehouse.ActivityOffersAsync(default));Assert.Equal(2,h.Hosts.Count);
        Assert.All(h.Hosts,x=>Assert.Equal("sydney.example.test",x));Assert.Contains("cloud-user",h.Logins.Single());Assert.DoesNotContain("local-secret",h.Logins.Single());
    }
    [Theory][InlineData("http://cloud.example.test")][InlineData("https://localhost")][InlineData("https://name:pass@cloud.example.test")][InlineData("https://cloud.example.test?token=x")]
    public async Task CloudRejectsUnsafeEndpointsBeforeSendingCredentials(string endpoint){
        var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        await Assert.ThrowsAsync<ServiceException>(()=>new ZetarisTravelWarehouse(new(h),Config(endpoint),cache,Select("zetaris-cloud")).ActivityOffersAsync(default));Assert.Empty(h.Hosts);
    }
    [Fact]public async Task UnconfiguredCloudNeverFallsBackToLocal(){
        var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Zetaris:Username"]="local-user",["Zetaris:Password"]="local-secret"}).Build();
        await Assert.ThrowsAsync<ServiceException>(()=>new ZetarisTravelWarehouse(new(h),config,cache,Select("zetaris-cloud")).ActivityOffersAsync(default));Assert.Empty(h.Hosts);
    }
    [Fact]public async Task CloudOrganisationMismatchStopsBeforeSql(){
        var config=Config();config["Zetaris:Cloud:OrgId"]="different-org";var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        await Assert.ThrowsAsync<ServiceException>(()=>new ZetarisTravelWarehouse(new(h),config,cache,Select("zetaris-cloud")).ActivityOffersAsync(default));Assert.Single(h.Hosts);
    }
    [Fact]public async Task LocalRemainsCompatibleWithLegacyConfiguration(){
        var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        await new ZetarisTravelWarehouse(new(h),Config(),cache,Select("zetaris")).ActivityOffersAsync(default);
        Assert.All(h.Hosts,x=>Assert.Equal("127.0.0.1",x));Assert.Contains("local-user",h.Logins.Single());
    }
    [Fact]public async Task CachedLocalSnapshotCannotSatisfyCloudRequest(){
        var context=new DefaultHttpContext();context.Request.Headers[TravelDataContext.Header]="zetaris";
        var selection=new TravelDataContext(new HttpContextAccessor{HttpContext=context});var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        var warehouse=new ZetarisTravelWarehouse(new(h),Config(),cache,selection);
        var local=await warehouse.ReadAsync(default);context.Request.Headers[TravelDataContext.Header]="zetaris-cloud";
        var cloud=await warehouse.ReadAsync(default);context.Request.Headers[TravelDataContext.Header]="zetaris";
        var localAgain=await warehouse.ReadAsync(default);
        Assert.Equal("127.0.0.1",local.Poi.Array("places")![0]!.Str("id"));Assert.Equal("sydney.example.test",cloud.Poi.Array("places")![0]!.Str("id"));
        Assert.Same(local,localAgain);Assert.Equal(4,h.Hosts.Count);
    }

    [Fact]public async Task ApplicationApiKeyUsesProxyWithoutAdminLogin(){
        var config=Config();config["Zetaris:Cloud:ApiKey"]="read-only-test-key";config["Zetaris:Cloud:Transport"]="ui-proxy";
        var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        await new ZetarisTravelWarehouse(new(h),config,cache,Select("zetaris-cloud")).ActivityOffersAsync(default);
        Assert.Empty(h.Logins);Assert.Single(h.Hosts);
    }

    [Fact]public async Task SearchSnapshotFiltersBothLargeTablesAndSeparatesViewportCache(){
        var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        var warehouse=new ZetarisTravelWarehouse(new(h),Config(),cache,Select("zetaris-cloud"));
        var a=await warehouse.ReadForSearchAsync(-33.72,150.31,1500,default);
        var same=await warehouse.ReadForSearchAsync(-33.72,150.31,1500,default);
        await warehouse.ReadForSearchAsync(-33.86,151.21,1500,default);
        Assert.Same(a,same);Assert.Equal(2,h.Statements.Count);
        Assert.All(h.Statements,body=>{
            var sql=JsonNode.Parse(body)!.Str("sql");
            Assert.Equal(2,sql.Split("WHERE CAST(get_json_object").Length-1);
            Assert.Contains("travel_osm_places WHERE",sql);Assert.Contains("travel_poi WHERE",sql);
        });
        Assert.DoesNotContain("150.31",h.Statements[1]);
    }
    [Fact]public async Task SearchRejectsInvalidBoundsBeforeContactingZetaris(){
        var h=new Transport();using var cache=new MemoryCache(new MemoryCacheOptions());
        var warehouse=new ZetarisTravelWarehouse(new(h),Config(),cache,Select("zetaris-cloud"));
        await Assert.ThrowsAsync<ServiceException>(()=>warehouse.ReadForSearchAsync(double.NaN,150,1500,default));
        await Assert.ThrowsAsync<ServiceException>(()=>warehouse.ReadForSearchAsync(-33.7,150,5001,default));
        Assert.Empty(h.Hosts);await Task.CompletedTask;
    }
    [Fact]public async Task RegionalSearchCanUseOsmOutsideTheOvertureCitySnapshot(){
        var h=new Transport{EmptyPoi=true};using var cache=new MemoryCache(new MemoryCacheOptions());
        var warehouse=new ZetarisTravelWarehouse(new(h),Config(),cache,Select("zetaris-cloud"));
        var snapshot=await warehouse.ReadForSearchAsync(-33.72,150.31,1500,default);
        Assert.Empty(snapshot.Poi.Array("places")!);Assert.Single(snapshot.Catalog);
    }
}
