using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using Roam.Api;
using Roam.Core;

var builder=WebApplication.CreateBuilder(args);
builder.Services.Configure<FormOptions>(o=>o.MultipartBodyLengthLimit=64*1024);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=64*1024);
builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.PropertyNamingPolicy=JsonNamingPolicy.CamelCase);
var connectionString=builder.Configuration.GetConnectionString("Roam")
    ?? Environment.GetEnvironmentVariable("ROAM_DATABASE_URL")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Roam with a PostgreSQL connection string. No SQLite or in-memory fallback is used.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<TripStore>();
builder.Services.AddSingleton<Planner>();
var app=builder.Build();
app.Use(async(context,next)=>
{
    if(context.Request.Path.StartsWithSegments("/api"))context.Response.Headers.CacheControl="no-store";
    context.Response.Headers.XContentTypeOptions="nosniff";
    try{await next(context);}
    catch(Exception ex) when(!context.Response.HasStarted)
    {
        var (code,message)=ex switch
        {
            ConflictException=>(409,ex.Message),
            MissingException=>(404,ex.Message),
            ArgumentException=>(400,ex.Message),
            BadHttpRequestException=>(400,"请求格式不正确，请检查输入。"),
            JsonException=>(400,"请求内容不是有效的 JSON。"),
            NpgsqlException=>(503,"数据库暂时不可用，请稍后重试。"),
            _=>(500,"生成时出现问题，请重试；你的已保存任务不会被覆盖。")
        };
        if(code>=500)app.Logger.LogError(ex,"Request failed: {Path}",context.Request.Path);
        context.Response.StatusCode=code;
        await context.Response.WriteAsJsonAsync(new{message});
    }
});
var store=app.Services.GetRequiredService<TripStore>();
var schema=await File.ReadAllTextAsync(Path.Combine(app.Environment.ContentRootPath,"Database","001_initial.sql"));
for(var attempt=0;;attempt++)
{
    try{await store.InitializeAsync(schema,CancellationToken.None);break;}
    catch(NpgsqlException) when(attempt<19){app.Logger.LogWarning("Waiting for PostgreSQL ({Attempt}/20)",attempt+1);await Task.Delay(500);}
}
app.UseDefaultFiles();app.UseStaticFiles();
app.MapGet("/health",async(TripStore db,CancellationToken ct)=>
{
    try{return await db.IsHealthyAsync(ct)?Results.Ok(new{status="healthy",database="PostgreSQL",backend=".NET 10",demoData=true}):Results.StatusCode(503);}
    catch(NpgsqlException){return Results.Json(new{status="unhealthy",database="PostgreSQL"},statusCode:503);}
});
app.MapGet("/api/bootstrap",async(TripStore db,CancellationToken ct)=>Results.Ok(new{
    profile=await db.GetProfileAsync(ct),trips=await db.ListTripsAsync(ct),demoData=true,
    dataNotice="示例地点、费用、营业时间、交通与 HP 均用于产品测试，未连接外部旅行 API。"
}));
app.MapPost("/api/profiles",async(CreateProfileRequest request,TripStore db,CancellationToken ct)=>
{
    if(request.Answers is null)throw new ArgumentException("请回答全部六个问题。");
    return Results.Ok(await db.CreateProfileAsync(request.Answers,ct));
});
app.MapPost("/api/trips",async(CreateTripRequest input,TripStore db,Planner planner,CancellationToken ct)=>
{
    var snapshot=Validate(input);
    var profile=await db.GetProfileAsync(ct)??throw new ConflictException("请先创建旅行角色。");
    if(profile.Id!=snapshot.ProfileId)throw new ConflictException("画像编号不匹配，请刷新后重试。");
    var result=planner.Generate(profile,ToPlanning(snapshot,false));
    return Results.Ok(await db.CreateTripAsync(snapshot,input.IdempotencyKey,result,ct));
});
app.MapGet("/api/trips/{id:guid}",async(Guid id,TripStore db,CancellationToken ct)=>Results.Ok(await db.GetTripAsync(id,ct)));
app.MapPost("/api/trips/{id:guid}/replan",async(Guid id,ReplanRequest input,TripStore db,Planner planner,CancellationToken ct)=>
{
    if(input.IdempotencyKey==Guid.Empty)throw new ArgumentException("重新规划需要有效的请求编号。");
    var trip=await db.GetTripAsync(id,ct);
    var profile=await db.GetProfileAsync(ct)??throw new MissingException("没有找到旅行画像。");
    var result=planner.Generate(profile,ToPlanning(trip.Request,input.Easier));
    return Results.Ok(await db.ReplanAsync(id,input.IdempotencyKey,input.Easier,result,ct));
});
app.Map("/api/{**path}",()=>Results.NotFound(new{message="没有这个接口。"}));
app.MapFallbackToFile("index.html");
app.Run();

static TripRequestSnapshot Validate(CreateTripRequest r)
{
    var title=r.Title?.Trim()??"";
    if(title.Length is <1 or >80)throw new ArgumentException("任务名称需要 1–80 个字符。");
    if(r.ProfileId==Guid.Empty)throw new ArgumentException("请先创建旅行画像。");
    if(r.IdempotencyKey==Guid.Empty)throw new ArgumentException("创建任务需要有效的请求编号。");
    if(!DateOnly.TryParseExact(r.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
        throw new ArgumentException("请选择有效的出行日期。");
    if(!TimeOnly.TryParseExact(r.StartTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var start)
       ||!TimeOnly.TryParseExact(r.EndTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var end))
        throw new ArgumentException("时间格式应为 HH:mm。");
    if(end<=start)throw new ArgumentException("第一版仅支持同一天行程，最晚返回需要晚于出发时间。");
    if(r.Budget is <0 or >10000 || decimal.Round(r.Budget,2)!=r.Budget)throw new ArgumentException("预算需为 0–10000 澳元，最多两位小数。");
    if(r.Energy is <10 or >100)throw new ArgumentException("本次精力需要在 10–100 之间。");
    if(r.Theme is not ("harbour" or "culture" or "nature"))throw new ArgumentException("请选择海港、文化或自然主题。");
    if((r.Notes?.Length??0)>500)throw new ArgumentException("备注最多 500 个字符。");
    return new(r.ProfileId,title,date.ToString("yyyy-MM-dd"),start.ToString("HH:mm"),end.ToString("HH:mm"),r.Budget,r.Energy,r.Theme,r.Notes?.Trim()??"");
}
static PlanningRequest ToPlanning(TripRequestSnapshot s,bool easier)=>new(
    DateOnly.ParseExact(s.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture),
    TimeOnly.ParseExact(s.StartTime,"HH:mm",CultureInfo.InvariantCulture),
    TimeOnly.ParseExact(s.EndTime,"HH:mm",CultureInfo.InvariantCulture),s.Budget,s.Energy,s.Theme,s.Notes,easier);
