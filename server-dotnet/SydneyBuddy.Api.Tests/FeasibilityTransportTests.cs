using System.Net;
using System.Text;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;
public sealed class FeasibilityTransportTests {
    [Theory][InlineData(502,2)][InlineData(503,2)][InlineData(401,1)]
    public async Task TransientProviderErrorsRetryOnceButAuthenticationErrorsDoNot(int status,int expectedCalls) {
        var root=Path.Combine(Path.GetTempPath(),"feasibility-transport-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            var secrets=new SecretStore(new ProjectPaths(root));secrets.Save(new Dictionary<string,string>{{"NVIDIA_API_KEY","fixture-key"}});
            using var handler=new Handler(status);using var http=new HttpClient(handler);var reasoner=new FeasibilityReasoner(http,secrets);
            if(status==401)await Assert.ThrowsAsync<ServiceException>(()=>reasoner.NextAsync(new(),default));
            else {var result=await reasoner.NextAsync(new(),default);Assert.Equal("finish",result.Str("action"));}
            Assert.Equal(expectedCalls,handler.Calls);
        }finally{Directory.Delete(root,true);}
    }
    sealed class Handler(int status):HttpMessageHandler {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            Calls++;Assert.NotNull(request.Headers.Authorization);
            if(Calls==1)return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
            var payload=new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]="{\"action\":\"finish\",\"decision\":\"needs_info\",\"issueCodes\":[],\"summary\":\"Check details.\"}"}})};
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload.ToJsonString(),Encoding.UTF8,"application/json")});
        }
    }
}
