using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using WindowForward.Api.Data;
using WindowForward.Api.Models;
using WindowForward.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "Windows Forward Manager");

var applicationBasePath = AppContext.BaseDirectory;
var connectionString = builder.Configuration.GetConnectionString("Default") ??
    "Data Source=App_Data/window-forward.db";
var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
if (!Path.IsPathRooted(sqliteConnection.DataSource))
{
    sqliteConnection.DataSource = Path.Combine(applicationBasePath, sqliteConnection.DataSource);
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConnection.ConnectionString));
builder.Services.AddScoped<ForwardRuleValidator>();
builder.Services.AddScoped<ForwardCommandService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var app = builder.Build();

var dbDirectory = Path.GetDirectoryName(sqliteConnection.DataSource);
if (!string.IsNullOrWhiteSpace(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "CommandExecutionLogs" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_CommandExecutionLogs" PRIMARY KEY AUTOINCREMENT,
            "ForwardRuleId" INTEGER NULL,
            "RuleName" TEXT NULL,
            "Action" TEXT NOT NULL,
            "CommandText" TEXT NOT NULL,
            "Success" INTEGER NOT NULL,
            "ExitCode" INTEGER NULL,
            "Message" TEXT NOT NULL,
            "Output" TEXT NULL,
            "ExecutedAt" TEXT NOT NULL
        );
        """);
}

app.UseCors();

var webRoot = app.Environment.WebRootPath;
if (string.IsNullOrWhiteSpace(webRoot) || !Directory.Exists(webRoot))
{
    webRoot = Path.Combine(applicationBasePath, "wwwroot");
}
if (Directory.Exists(webRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/api/rules", async (AppDbContext db) =>
{
    var rules = await db.ForwardRules.AsNoTracking().ToListAsync();
    return rules.OrderByDescending(x => x.UpdatedAt).ToList();
});

app.MapGet("/api/rules/{id:int}", async (int id, AppDbContext db) =>
{
    var rule = await db.ForwardRules.FindAsync(id);
    return rule is null ? Results.NotFound(ApiResponse.Fail("规则不存在。")) : Results.Ok(rule);
});

app.MapGet("/api/command-logs", async (int? take, AppDbContext db) =>
{
    var count = Math.Clamp(take ?? 30, 1, 200);
    var logs = await db.CommandExecutionLogs.AsNoTracking().ToListAsync();
    return logs
        .OrderByDescending(x => x.ExecutedAt)
        .ThenByDescending(x => x.Id)
        .Take(count)
        .ToList();
});

app.MapGet("/api/portproxy", async (ForwardCommandService commandService) =>
{
    var result = await commandService.ShowPortProxyAsync();
    return result.Success
        ? Results.Ok(ApiResponse.Ok(result, "已读取系统 portproxy 配置。"))
        : Results.BadRequest(ApiResponse.Fail(result.Message, result));
});

app.MapGet("/api/portproxy/rules", async (ForwardCommandService commandService) =>
    await commandService.GetPortProxyRulesAsync());

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

    if (rule.Enabled && rule.Type != ForwardRuleType.PortProxy)
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

    if (!rule.Enabled && rule.Type != ForwardRuleType.PortProxy)
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

if (Directory.Exists(webRoot))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
