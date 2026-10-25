using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
namespace SydneyBuddy.Api.Tests;
public sealed class ActivityOfferTests {
    static JsonObject Offer()=>new(){["id"]="synthetic",["title"]="Test activity",["venueName"]="Test venue",["lat"]=-33.8568,["lng"]=151.2153,["amount"]=50,["currency"]="AUD",["ticketType"]="Adult advance",["conditions"]="Test reference",["sourceName"]="Test official",["sourceUrl"]="https://example.com/tickets",["observedOn"]="2026-10-25",["reviewBy"]="2026-11-24"};
    [Fact]public void ReferenceNeverChangesPlannerBudgetAndKeepsCurrency(){var row=Offer();row["budgetEligible"]=true;var result=ActivityOfferService.Search(new JsonArray(row),-33.8568,151.2153,1000,"","zetaris",DateTimeOffset.Parse("2026-10-25T01:00:00Z"),"test-venue","Test venue");var found=result.Array("offers")![0]!;Assert.False(found.Bool("budgetEligible"));Assert.False(found.Bool("stale"));Assert.Equal("AUD",found.Str("currency"));Assert.Equal("zetaris",result.Str("dataSource"));Assert.Null(found["cost"]);}
    [Fact]public void SearchExcludesDistantAndNonmatchingActivities(){var rows=new JsonArray(Offer());Assert.Empty(ActivityOfferService.Search(rows,-33.90,151.21,1000,"","direct",DateTimeOffset.UtcNow,"test-venue","Test venue").Array("offers")!);Assert.Empty(ActivityOfferService.Search(rows,-33.8568,151.2153,1000,"unrelated","direct",DateTimeOffset.UtcNow,"test-venue","Test venue").Array("offers")!);}
    [Fact]public void OutdatedPriceIsMarkedForReview(){var result=ActivityOfferService.Search(new JsonArray(Offer()),-33.8568,151.2153,1000,"activity","direct",DateTimeOffset.Parse("2026-12-25T00:00:00Z"),"test-venue","Test venue");Assert.True(result.Array("offers")![0]!.Bool("stale"));}
    [Fact]public void UnknownCurrencyAndUnsafeLinksAreRejected(){var row=Offer();row["currency"]="";Assert.Throws<ServiceException>(()=>ActivityOfferService.Validate(row));row=Offer();row["sourceUrl"]="javascript:alert(1)";Assert.Throws<ServiceException>(()=>ActivityOfferService.Validate(row));}
    [Fact]public void NearbyRestaurantNeverReceivesAnotherVenuesTickets(){
        var rows=new JsonArray(Offer());var now=DateTimeOffset.Parse("2026-10-25T01:00:00Z");
        Assert.Empty(ActivityOfferService.Search(rows,-33.8568,151.2153,1000,"","direct",now,"restaurant:123","Luke Mangan & Company").Array("offers")!);
        Assert.Empty(ActivityOfferService.Search(rows,-33.8568,151.2153,1000,"","direct",now).Array("offers")!);
        Assert.Empty(ActivityOfferService.Search(rows,-33.8568,151.2153,1000,"","direct",now,"other","Test venue cafe").Array("offers")!);
    }
    [Fact]public void ExactReviewedAliasesMatchButDistantSameNameDoesNot(){
        var offer=Offer();offer["venueAliases"]=new JsonArray("測試場館");
        Assert.True(ActivityOfferService.MatchesVenue(offer,"apple:123","測試場館",80));
        Assert.False(ActivityOfferService.MatchesVenue(offer,"apple:123","測試場館",600));
        Assert.False(ActivityOfferService.MatchesVenue(offer,"apple:123","Other",0));
    }
    [Fact]public void UnknownAndFreeAreDifferentAndNeitherDeductsBudget(){
        var unknown=Offer();unknown["amount"]=null;unknown["pricingStatus"]="unknown";
        var checkedUnknown=ActivityOfferService.Validate(unknown);Assert.Null(checkedUnknown["amount"]);Assert.Equal("unknown",checkedUnknown.Str("pricingStatus"));Assert.False(checkedUnknown.Bool("budgetEligible"));
        var free=Offer();free["amount"]=0;free["pricingStatus"]="free";var checkedFree=ActivityOfferService.Validate(free);Assert.Equal(0,checkedFree.Number("amount"));Assert.Equal("free",checkedFree.Str("pricingStatus"));
        free["amount"]=25;Assert.Throws<ServiceException>(()=>ActivityOfferService.Validate(free));
        unknown["amount"]=0;Assert.Throws<ServiceException>(()=>ActivityOfferService.Validate(unknown));
    }
    static JsonObject SummaryOffer(double amount,string audience="adult",string experience="guided_tour",string currency="AUD",bool stale=false)=>new(){["id"]=Guid.NewGuid().ToString(),["amount"]=amount,["audience"]=audience,["experience"]=experience,["currency"]=currency,["pricingStatus"]=amount==0?"free":"known",["stale"]=stale,["sourceUrl"]="https://example.com",["observedOn"]="2026-10-25"};
    [Fact]public void SummaryExcludesChildConcessionFamilyAndUnlikePackages(){
        var summary=ActivityOfferService.Summarize(new JsonArray(SummaryOffer(0,"child"),SummaryOffer(20,"concession"),SummaryOffer(130,"family"),SummaryOffer(50),SummaryOffer(55),SummaryOffer(84,"adult","combo")));
        Assert.Equal(50,summary!.Number("amount"));Assert.Equal("guided_tour",summary.Str("basis"));Assert.False(summary.Bool("budgetEligible"));
    }
    [Fact]public void GeneralAdmissionPrecedesTourAndStaleOrMixedCurrenciesAreNotSummarized(){
        var free=ActivityOfferService.Summarize(new JsonArray(SummaryOffer(0,"general","general_admission"),SummaryOffer(25)));
        Assert.Equal(0,free!.Number("amount"));Assert.Equal("general_admission",free.Str("basis"));
        Assert.Null(ActivityOfferService.Summarize(new JsonArray(SummaryOffer(50,stale:true))));
        Assert.Null(ActivityOfferService.Summarize(new JsonArray(SummaryOffer(50),SummaryOffer(20,currency:"USD"))));
        Assert.Null(ActivityOfferService.Summarize(new JsonArray(SummaryOffer(0,"child"))));
    }
    sealed class Warehouse: ITravelWarehouse {
        public bool Fail;public int Calls;
        public Task<TravelSnapshot> ReadAsync(CancellationToken token)=>throw new NotImplementedException();
        public Task<JsonArray> ActivityOffersAsync(CancellationToken token){Calls++;if(Fail)throw new ServiceException("offline");return Task.FromResult(new JsonArray(Offer()));}
    }
    [Fact]public async Task SelectedWarehouseIsUsedWithoutDirectFallback(){
        var context=new DefaultHttpContext();context.Request.Headers[TravelDataContext.Header]="zetaris";
        var warehouse=new Warehouse();var service=new ActivityOfferService(new ProjectPaths("/unused"),new TravelDataContext(new HttpContextAccessor{HttpContext=context}),warehouse);
        var request=new JsonObject{["lat"]=-33.8568,["lng"]=151.2153,["placeId"]="test-venue",["placeName"]="Test venue"};
        var result=await service.SearchAsync(request,CancellationToken.None);Assert.Single(result.Array("offers")!);Assert.Equal(1,warehouse.Calls);
        warehouse.Fail=true;await Assert.ThrowsAsync<ServiceException>(()=>service.SearchAsync(request,CancellationToken.None));Assert.Equal(2,warehouse.Calls);
    }
    [Fact]public async Task BatchReadsWarehouseOnceAndRejectsUnboundedRequests(){
        var context=new DefaultHttpContext();context.Request.Headers[TravelDataContext.Header]="zetaris";var warehouse=new Warehouse();
        var service=new ActivityOfferService(new ProjectPaths("/unused"),new TravelDataContext(new HttpContextAccessor{HttpContext=context}),warehouse);
        var points=new JsonArray();for(var i=0;i<80;i++)points.Add(new JsonObject{["id"]="test:"+i,["name"]="Test venue",["lat"]=-33.8568,["lng"]=151.2153});
        await service.SummariesAsync(new(){["places"]=points},CancellationToken.None);Assert.Equal(1,warehouse.Calls);
        points.Add(points[0]!.DeepClone());await Assert.ThrowsAsync<ServiceException>(()=>service.SummariesAsync(new(){["places"]=points.DeepClone()},CancellationToken.None));Assert.Equal(1,warehouse.Calls);
    }
    [Fact]public async Task InvalidLocationIsRejectedBeforeDataQuery(){
        var context=new DefaultHttpContext();context.Request.Headers[TravelDataContext.Header]="zetaris";var warehouse=new Warehouse();
        var service=new ActivityOfferService(new ProjectPaths("/unused"),new TravelDataContext(new HttpContextAccessor{HttpContext=context}),warehouse);
        await Assert.ThrowsAsync<ServiceException>(()=>service.SearchAsync(new JsonObject{["lat"]=100,["lng"]=151},CancellationToken.None));Assert.Equal(0,warehouse.Calls);
    }
}
