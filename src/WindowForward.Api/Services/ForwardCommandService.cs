using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindowForward.Api.Data;
using WindowForward.Api.Models;

namespace WindowForward.Api.Services;

public sealed partial class ForwardCommandService(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string BuildPreview(ForwardRuleInput rule) => string.Join(Environment.NewLine, BuildCommands(rule, true));

    public async Task<CommandResult> EnableAsync(ForwardRule rule)
    {
        var input = FromEntity(rule);
        var result = await RunPowerShellAsync(BuildCommands(input, true));
        await LogAsync(rule, rule.Name, "启用", result);
        return result;
    }

    public async Task<CommandResult> DisableAsync(ForwardRule rule)
    {
        if (rule.Type is ForwardRuleType.SshLocal or ForwardRuleType.SshRemote or ForwardRuleType.SshDynamic &&
            rule.RuntimeProcessId is not null)
        {
            var sshResult = await RunPowerShellAsync(new[] { $"Stop-Process -Id {rule.RuntimeProcessId} -Force -ErrorAction Stop" });
            await LogAsync(rule, rule.Name, "禁用", sshResult);
            return sshResult;
        }

        var input = FromEntity(rule);
        var result = await RunPowerShellAsync(BuildCommands(input, false));
        await LogAsync(rule, rule.Name, "禁用", result);
        return result;
    }

    public async Task<CommandResult> ShowPortProxyAsync()
    {
        var result = await RunPowerShellAsync(new[] { "netsh interface portproxy show all" });
        await LogAsync(null, "系统 portproxy", "查看", result);
        return result;
    }

    public async Task<IReadOnlyList<SystemPortProxyRule>> GetPortProxyRulesAsync()
    {
        var result = await RunPowerShellAsync(new[] { "netsh interface portproxy show all" });
        if (!result.Success)
        {
            return Array.Empty<SystemPortProxyRule>();
        }

        return ParsePortProxyRules(result.Output);
    }

    public async Task<IReadOnlyList<SystemFirewallRule>> GetFirewallRulesAsync()
    {
        const string script = """
            $items = Get-NetFirewallRule -Direction Inbound -Action Allow -ErrorAction SilentlyContinue | ForEach-Object {
                $rule = $_
                $rule | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue | ForEach-Object {
                    [pscustomobject]@{
                        DisplayName = $rule.DisplayName
                        Enabled = $rule.Enabled.ToString()
                        Direction = $rule.Direction.ToString()
                        Action = $rule.Action.ToString()
                        Protocol = $_.Protocol.ToString()
                        LocalPort = $_.LocalPort.ToString()
                    }
                }
            }
            @($items) | ConvertTo-Json -Depth 4
            """;
        var result = await RunPowerShellAsync(new[] { script });
        return result.Success ? DeserializeJsonArray<SystemFirewallRule>(result.Output) : Array.Empty<SystemFirewallRule>();
    }

    public async Task<IReadOnlyList<SystemNatStaticMapping>> GetNatStaticMappingsAsync()
    {
        const string script = """
            @(Get-NetNatStaticMapping -ErrorAction SilentlyContinue |
                Select-Object NatName, Protocol, ExternalIPAddress, ExternalPort, InternalIPAddress, InternalPort) |
                ConvertTo-Json -Depth 4
            """;
        var result = await RunPowerShellAsync(new[] { script });
        return result.Success ? DeserializeJsonArray<SystemNatStaticMapping>(result.Output) : Array.Empty<SystemNatStaticMapping>();
    }

    public async Task<IReadOnlyList<SystemRouteRule>> GetRouteRulesAsync()
    {
        const string script = """
            @(Get-NetRoute -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object { $_.NextHop -and $_.NextHop -ne '0.0.0.0' } |
                Select-Object DestinationPrefix, NextHop, RouteMetric, InterfaceAlias, PolicyStore) |
                ConvertTo-Json -Depth 4
            """;
        var result = await RunPowerShellAsync(new[] { script });
        return result.Success ? DeserializeJsonArray<SystemRouteRule>(result.Output) : Array.Empty<SystemRouteRule>();
    }

    private static IReadOnlyList<SystemPortProxyRule> ParsePortProxyRules(string output)
    {
        var rules = new List<SystemPortProxyRule>();
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = WhitespaceRegex().Split(line.Trim());
            if (parts.Length < 4 ||
                !int.TryParse(parts[1], out var listenPort) ||
                !int.TryParse(parts[3], out var connectPort) ||
                !AddressRegex().IsMatch(parts[0]) ||
                !AddressRegex().IsMatch(parts[2]))
            {
                continue;
            }

            rules.Add(new SystemPortProxyRule(parts[0], listenPort, parts[2], connectPort));
        }

        return rules;
    }

    private static IEnumerable<string> BuildCommands(ForwardRuleInput rule, bool enable)
    {
        var protocol = rule.Protocol == ForwardProtocol.Udp ? "UDP" : "TCP";
        return rule.Type switch
        {
            ForwardRuleType.PortProxy when enable => new[]
            {
                $"netsh interface portproxy add v4tov4 listenaddress={Q(rule.ListenAddress)} listenport={rule.ListenPort} connectaddress={Q(rule.ConnectAddress)} connectport={rule.ConnectPort}"
            },
            ForwardRuleType.PortProxy => new[]
            {
                $"netsh interface portproxy delete v4tov4 listenaddress={Q(rule.ListenAddress)} listenport={rule.ListenPort}"
            },
            ForwardRuleType.Firewall when enable => new[]
            {
                $"New-NetFirewallRule -DisplayName {Ps(QName(rule.Name))} -Direction Inbound -Protocol {protocol} -LocalPort {rule.ListenPort} -Action Allow"
            },
            ForwardRuleType.Firewall => new[]
            {
                $"Get-NetFirewallRule -DisplayName {Ps(QName(rule.Name))} -ErrorAction SilentlyContinue | Remove-NetFirewallRule",
                $"Get-NetFirewallRule -DisplayName {Ps(rule.Name)} -ErrorAction SilentlyContinue | Remove-NetFirewallRule"
            },
            ForwardRuleType.Nat when enable => new[]
            {
                string.IsNullOrWhiteSpace(rule.Prefix)
                    ? $"if (-not (Get-NetNat -Name {Ps(rule.NatName)} -ErrorAction SilentlyContinue)) {{ throw 'NAT 不存在，请填写内部网段 Prefix 自动创建，或先手工创建 NAT。' }}"
                    : $"if (-not (Get-NetNat -Name {Ps(rule.NatName)} -ErrorAction SilentlyContinue)) {{ New-NetNat -Name {Ps(rule.NatName)} -InternalIPInterfaceAddressPrefix {Ps(rule.Prefix)} }}",
                $"Add-NetNatStaticMapping -NatName {Ps(rule.NatName)} -Protocol {protocol} -ExternalIPAddress 0.0.0.0 -ExternalPort {rule.ListenPort} -InternalIPAddress {rule.ConnectAddress} -InternalPort {rule.ConnectPort}"
            },
            ForwardRuleType.Nat => new[]
            {
                $"Get-NetNatStaticMapping -NatName {Ps(rule.NatName)} -ErrorAction SilentlyContinue | Where-Object {{$_.ExternalPort -eq {rule.ListenPort} -and $_.Protocol -eq '{protocol}'}} | Remove-NetNatStaticMapping -Confirm:$false"
            },
            ForwardRuleType.Route when enable => new[]
            {
                $"route add {rule.RouteDestination} mask {rule.RouteMask} {rule.RouteGateway} -p"
            },
            ForwardRuleType.Route => new[]
            {
                $"route delete {rule.RouteDestination}"
            },
            ForwardRuleType.IpForwarding when enable => new[]
            {
                "Set-ItemProperty -Path HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters -Name IPEnableRouter -Value 1"
            },
            ForwardRuleType.IpForwarding => new[]
            {
                "Set-ItemProperty -Path HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters -Name IPEnableRouter -Value 0"
            },
            ForwardRuleType.SshLocal when enable => new[]
            {
                $"(Start-Process ssh -ArgumentList {Ps($"-N -L {rule.ListenPort}:{rule.ConnectAddress}:{rule.ConnectPort} {SshTarget(rule)}")} -WindowStyle Hidden -PassThru).Id"
            },
            ForwardRuleType.SshRemote when enable => new[]
            {
                $"(Start-Process ssh -ArgumentList {Ps($"-N -R {rule.ListenPort}:{rule.ConnectAddress}:{rule.ConnectPort} {SshTarget(rule)}")} -WindowStyle Hidden -PassThru).Id"
            },
            ForwardRuleType.SshDynamic when enable => new[]
            {
                $"(Start-Process ssh -ArgumentList {Ps($"-N -D {rule.ListenPort} {SshTarget(rule)}")} -WindowStyle Hidden -PassThru).Id"
            },
            ForwardRuleType.SshLocal or ForwardRuleType.SshRemote or ForwardRuleType.SshDynamic => new[]
            {
                "# SSH 转发进程 ID 丢失，无法自动停止。"
            },
            _ => throw new InvalidOperationException("Unsupported forward rule type.")
        };
    }

    private static ForwardRuleInput FromEntity(ForwardRule rule) => new()
    {
        Name = rule.Name,
        Description = rule.Description,
        Type = rule.Type,
        Protocol = rule.Protocol,
        ListenAddress = rule.ListenAddress,
        ListenPort = rule.ListenPort,
        ConnectAddress = rule.ConnectAddress,
        ConnectPort = rule.ConnectPort,
        NatName = rule.NatName,
        Prefix = rule.Prefix,
        RouteDestination = rule.RouteDestination,
        RouteMask = rule.RouteMask,
        RouteGateway = rule.RouteGateway,
        SshHost = rule.SshHost,
        SshUser = rule.SshUser
    };

    private static IReadOnlyList<T> DeserializeJsonArray<T>(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<T>();
        }

        using var doc = JsonDocument.Parse(output);
        return doc.RootElement.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<T>>(doc.RootElement.GetRawText(), JsonOptions) ?? [],
            JsonValueKind.Object => JsonSerializer.Deserialize<T>(doc.RootElement.GetRawText(), JsonOptions) is { } item
                ? [item]
                : [],
            _ => []
        };
    }

    private static async Task<CommandResult> RunPowerShellAsync(IEnumerable<string> commands)
    {
        var commandList = commands.ToList();
        var script = string.Join("; ", commandList.Where(x => !x.TrimStart().StartsWith('#')));
        if (string.IsNullOrWhiteSpace(script))
        {
            return new(true, "无需执行系统命令。", string.Empty, null, null, string.Join(Environment.NewLine, commandList));
        }

        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new(false, "无法启动 PowerShell。", string.Empty, null, null, script);
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var processId = int.TryParse(output.Trim(), out var pid) ? pid : (int?)null;
        return process.ExitCode == 0
            ? new(true, "系统命令执行成功。", combined, processId, process.ExitCode, script)
            : new(false, "系统命令执行失败，请确认服务以管理员权限运行。", combined, null, process.ExitCode, script);
    }

    private async Task LogAsync(ForwardRule? rule, string ruleName, string action, CommandResult result)
    {
        db.CommandExecutionLogs.Add(new CommandExecutionLog
        {
            ForwardRuleId = rule?.Id,
            RuleName = ruleName,
            Action = action,
            CommandText = result.CommandText,
            Success = result.Success,
            ExitCode = result.ExitCode,
            Message = result.Message,
            Output = result.Output,
            ExecutedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static string SshTarget(ForwardRuleInput rule) =>
        string.IsNullOrWhiteSpace(rule.SshUser) ? rule.SshHost! : $"{rule.SshUser}@{rule.SshHost}";

    private static string QName(string value) => $"Windows Forward - {value}";

    private static string Q(string? value) => value ?? string.Empty;

    private static string Ps(string? value) => $"'{(value ?? string.Empty).Replace("'", "''")}'";

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^[0-9a-fA-F:.]+$")]
    private static partial Regex AddressRegex();
}

public sealed record CommandResult(
    bool Success,
    string Message,
    string Output,
    int? ProcessId,
    int? ExitCode,
    string CommandText);
