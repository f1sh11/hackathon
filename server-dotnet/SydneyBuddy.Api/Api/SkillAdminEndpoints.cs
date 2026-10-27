using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class SkillAdminEndpoints
{
    public static void MapSkillAdminEndpoints(this RouteGroupBuilder routes)
    {
        var admin = routes.MapGroup("/admin/planning/skills");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            var token = http.Request.Headers["X-Skill-Admin-Token"];
            if (token.Count != 1 || !http.RequestServices.GetRequiredService<SkillAdministration>().Authorized(token.ToString()))
                return Results.Json(new { error = "Skill administrator authentication required." }, statusCode: 401);
            return await next(context);
        });
        admin.MapGet("/status", async (SkillAdministration service) => Results.Json(await service.StatusAsync()));
        admin.MapGet("/versions", async (SkillAdministration service) => Results.Json(await service.ListAsync()));
        admin.MapGet("/template", async (SkillAdministration service) => Results.Bytes(
            System.Text.Encoding.UTF8.GetBytes((await service.TemplateAsync()).ToJsonString()), "application/json", "skill-bundle.json"));
        admin.MapPost("/versions", async (HttpContext http, SkillAdministration service) =>
            Results.Json(await service.UploadAsync(await PlanningTestEndpoints.ReadInputAsync(http))));
        admin.MapPost("/documents", async (HttpContext http, SkillAdministration service) =>
        {
            if (!http.Request.HasFormContentType) return Results.Json(new { error = "Use multipart/form-data." }, statusCode: 415);
            IFormCollection form;
            try { form = await http.Request.ReadFormAsync(new Microsoft.AspNetCore.Http.Features.FormOptions { MultipartBodyLengthLimit = 220_000, ValueLengthLimit = 200, ValueCountLimit = 4 }, http.RequestAborted); }
            catch (InvalidDataException) { throw new BadHttpRequestException("Invalid multipart upload", 400); }
            if (form.Count != 2 || !form.ContainsKey("version") || !form.ContainsKey("baseVersion") || form["version"].Count != 1 || form["baseVersion"].Count != 1 ||
                form.Files.Count != 2 || form.Files.Count(f => f.Name == "en") != 1 || form.Files.Count(f => f.Name == "zhHant") != 1)
                throw new ServiceException("Provide version, baseVersion and exactly two Markdown files named en and zhHant.");
            var baseline = ItinerarySkill.Load(null);
            async Task<string> Appendix(string field, string basis)
            {
                var file = form.Files.GetFile(field)!;
                if (!file.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || file.Length > 110_000) throw new ServiceException("Use Markdown files of at most 110000 bytes each.");
                await using var input = file.OpenReadStream(); using var memory = new MemoryStream();
                var buffer = new byte[8192]; int count;
                while ((count = await input.ReadAsync(buffer, http.RequestAborted)) > 0) { if (memory.Length + count > 110_000) throw new ServiceException("Markdown is too large."); memory.Write(buffer, 0, count); }
                string text;
                try { text = new System.Text.UTF8Encoding(false, true).GetString(memory.ToArray()).TrimStart('\uFEFF'); }
                catch (System.Text.DecoderFallbackException) { throw new ServiceException("Markdown must use UTF-8."); }
                if (!text.StartsWith(basis, StringComparison.Ordinal)) throw new ServiceException("Do not remove or edit embedded rules. Download the current full Skill and append rules at its end.");
                var suffix = text[basis.Length..];
                return suffix.StartsWith("\n\n", StringComparison.Ordinal) ? suffix[2..] : suffix;
            }
            var bundle = new JsonObject { ["version"] = form["version"].ToString(), ["baseVersion"] = form["baseVersion"].ToString(),
                ["enAppendix"] = await Appendix("en", baseline.English), ["zhHantAppendix"] = await Appendix("zhHant", baseline.Chinese) };
            return Results.Json(await service.UploadAsync(bundle));
        });
        admin.MapGet("/versions/{version}", async (string version, SkillAdministration service) => Results.Json(await service.GetAsync(version)));
        admin.MapPost("/versions/{version}/activate", async (string version, HttpContext http, SkillAdministration service) =>
        {
            var input = await RequestJson.ReadAsync(http);
            return Results.Json(await service.ActivateAsync(version, input.Str("expectedActiveFingerprint"), false, false));
        });
        admin.MapPost("/rollback", async (HttpContext http, SkillAdministration service) =>
        {
            var input = await RequestJson.ReadAsync(http);
            return Results.Json(await service.ActivateAsync(input.Str("version"), input.Str("expectedActiveFingerprint"), true, input.Bool("confirmRemoval")));
        });
    }
}
