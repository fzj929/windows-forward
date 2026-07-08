using Microsoft.EntityFrameworkCore;
using WindowForward.Api.Data;
using WindowForward.Api.Models;
using WindowForward.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "Windowx Forward Manager");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<ForwardRuleValidator>();
builder.Services.AddScoped<ForwardCommandService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();

app.MapGet("/api/rules", async (AppDbContext db) =>
    await db.ForwardRules.OrderByDescending(x => x.UpdatedAt).ToListAsync());

app.MapGet("/api/rules/{id:int}", async (int id, AppDbContext db) =>
{
    var rule = await db.ForwardRules.FindAsync(id);
    return rule is null ? Results.NotFound(ApiResponse.Fail("规则不存在。")) : Results.Ok(rule);
});

app.MapPost("/api/rules/validate", (ForwardRuleInput input, ForwardRuleValidator validator) =>
{
    var result = validator.Validate(input);
    return result.IsValid
        ? Results.Ok(ApiResponse.Ok(ForwardCommandService.BuildPreview(input), "验证通过。"))
        : Results.BadRequest(ApiResponse.Fail("规则配置有误，请按提示修正。", result.Errors));
});

app.MapPost("/api/rules", async (ForwardRuleInput input, AppDbContext db, ForwardRuleValidator validator) =>
{
    var validation = validator.Validate(input);
    if (!validation.IsValid)
    {
        return Results.BadRequest(ApiResponse.Fail("规则配置有误，请按提示修正。", validation.Errors));
    }

    var now = DateTimeOffset.UtcNow;
    var rule = new ForwardRule
    {
        Name = input.Name.Trim(),
        Description = input.Description?.Trim(),
        Type = input.Type,
        Protocol = input.Protocol,
        ListenAddress = input.ListenAddress?.Trim(),
        ListenPort = input.ListenPort,
        ConnectAddress = input.ConnectAddress?.Trim(),
        ConnectPort = input.ConnectPort,
        NatName = input.NatName?.Trim(),
        Prefix = input.Prefix?.Trim(),
        RouteDestination = input.RouteDestination?.Trim(),
        RouteMask = input.RouteMask?.Trim(),
        RouteGateway = input.RouteGateway?.Trim(),
        SshHost = input.SshHost?.Trim(),
        SshUser = input.SshUser?.Trim(),
        Enabled = false,
        CreatedAt = now,
        UpdatedAt = now
    };

    db.ForwardRules.Add(rule);
    await db.SaveChangesAsync();
    return Results.Created($"/api/rules/{rule.Id}", rule);
});

app.MapPut("/api/rules/{id:int}", async (int id, ForwardRuleInput input, AppDbContext db, ForwardRuleValidator validator) =>
{
    var rule = await db.ForwardRules.FindAsync(id);
    if (rule is null)
    {
        return Results.NotFound(ApiResponse.Fail("规则不存在。"));
    }

    if (rule.Enabled)
    {
        return Results.BadRequest(ApiResponse.Fail("请先禁用规则，再修改配置。"));
    }

    var validation = validator.Validate(input);
    if (!validation.IsValid)
    {
        return Results.BadRequest(ApiResponse.Fail("规则配置有误，请按提示修正。", validation.Errors));
    }

    rule.Name = input.Name.Trim();
    rule.Description = input.Description?.Trim();
    rule.Type = input.Type;
    rule.Protocol = input.Protocol;
    rule.ListenAddress = input.ListenAddress?.Trim();
    rule.ListenPort = input.ListenPort;
    rule.ConnectAddress = input.ConnectAddress?.Trim();
    rule.ConnectPort = input.ConnectPort;
    rule.NatName = input.NatName?.Trim();
    rule.Prefix = input.Prefix?.Trim();
    rule.RouteDestination = input.RouteDestination?.Trim();
    rule.RouteMask = input.RouteMask?.Trim();
    rule.RouteGateway = input.RouteGateway?.Trim();
    rule.SshHost = input.SshHost?.Trim();
    rule.SshUser = input.SshUser?.Trim();
    rule.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(rule);
});

app.MapPost("/api/rules/{id:int}/enable", async (int id, AppDbContext db, ForwardCommandService commandService) =>
{
    var rule = await db.ForwardRules.FindAsync(id);
    if (rule is null)
    {
        return Results.NotFound(ApiResponse.Fail("规则不存在。"));
    }

    if (rule.Enabled)
    {
        return Results.Ok(ApiResponse.Ok(rule, "规则已经启用。"));
    }

    var result = await commandService.EnableAsync(rule);
    if (!result.Success)
    {
        return Results.BadRequest(ApiResponse.Fail(result.Message, result.Output));
    }

    rule.Enabled = true;
    rule.RuntimeProcessId = result.ProcessId;
    rule.LastAppliedAt = DateTimeOffset.UtcNow;
    rule.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(ApiResponse.Ok(rule, "规则已启用。"));
});

app.MapPost("/api/rules/{id:int}/disable", async (int id, AppDbContext db, ForwardCommandService commandService) =>
{
    var rule = await db.ForwardRules.FindAsync(id);
    if (rule is null)
    {
        return Results.NotFound(ApiResponse.Fail("规则不存在。"));
    }

    if (!rule.Enabled)
    {
        return Results.Ok(ApiResponse.Ok(rule, "规则已经禁用。"));
    }

    var result = await commandService.DisableAsync(rule);
    if (!result.Success)
    {
        return Results.BadRequest(ApiResponse.Fail(result.Message, result.Output));
    }

    rule.Enabled = false;
    rule.RuntimeProcessId = null;
    rule.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(ApiResponse.Ok(rule, "规则已禁用。"));
});

app.MapDelete("/api/rules/{id:int}", async (int id, AppDbContext db) =>
{
    var rule = await db.ForwardRules.FindAsync(id);
    if (rule is null)
    {
        return Results.NotFound(ApiResponse.Fail("规则不存在。"));
    }

    if (rule.Enabled)
    {
        return Results.BadRequest(ApiResponse.Fail("请先禁用规则，再删除。"));
    }

    db.ForwardRules.Remove(rule);
    await db.SaveChangesAsync();
    return Results.Ok(ApiResponse.Ok<object?>(null, "规则已删除。"));
});

app.Run();
