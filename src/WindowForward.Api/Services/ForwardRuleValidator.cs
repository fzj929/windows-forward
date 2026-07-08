using System.Net;
using System.Text.RegularExpressions;
using WindowForward.Api.Models;

namespace WindowForward.Api.Services;

public sealed partial class ForwardRuleValidator
{
    public ValidationResult Validate(ForwardRuleInput input)
    {
        var errors = new List<FieldError>();

        Required(errors, nameof(input.Name), input.Name, "规则名称不能为空。");
        if (input.Name.Trim().Length > 80)
        {
            errors.Add(new(nameof(input.Name), "规则名称不能超过 80 个字符。"));
        }

        switch (input.Type)
        {
            case ForwardRuleType.PortProxy:
                RequireAddress(errors, nameof(input.ListenAddress), input.ListenAddress, "监听地址不能为空，例如 0.0.0.0。");
                RequirePort(errors, nameof(input.ListenPort), input.ListenPort, "监听端口必须在 1-65535 之间。");
                RequireAddress(errors, nameof(input.ConnectAddress), input.ConnectAddress, "目标地址不能为空。");
                RequirePort(errors, nameof(input.ConnectPort), input.ConnectPort, "目标端口必须在 1-65535 之间。");
                if (input.Protocol != ForwardProtocol.Tcp)
                {
                    errors.Add(new(nameof(input.Protocol), "netsh portproxy 仅支持 TCP。"));
                }
                break;
            case ForwardRuleType.Firewall:
                RequirePort(errors, nameof(input.ListenPort), input.ListenPort, "放行端口必须在 1-65535 之间。");
                break;
            case ForwardRuleType.Nat:
                Required(errors, nameof(input.NatName), input.NatName, "NAT 名称不能为空。");
                SafeToken(errors, nameof(input.NatName), input.NatName, "NAT 名称只能包含字母、数字、点、下划线和短横线。");
                if (!string.IsNullOrWhiteSpace(input.Prefix) && !CidrRegex().IsMatch(input.Prefix))
                {
                    errors.Add(new(nameof(input.Prefix), "内部网段 Prefix 格式应类似 192.168.100.0/24。"));
                }
                RequirePort(errors, nameof(input.ListenPort), input.ListenPort, "外部端口必须在 1-65535 之间。");
                RequireAddress(errors, nameof(input.ConnectAddress), input.ConnectAddress, "内部地址不能为空。");
                RequirePort(errors, nameof(input.ConnectPort), input.ConnectPort, "内部端口必须在 1-65535 之间。");
                break;
            case ForwardRuleType.Route:
                RequireAddress(errors, nameof(input.RouteDestination), input.RouteDestination, "目标网段不能为空。");
                RequireAddress(errors, nameof(input.RouteMask), input.RouteMask, "子网掩码不能为空。");
                RequireAddress(errors, nameof(input.RouteGateway), input.RouteGateway, "网关不能为空。");
                break;
            case ForwardRuleType.IpForwarding:
                break;
            case ForwardRuleType.SshLocal:
            case ForwardRuleType.SshRemote:
                RequirePort(errors, nameof(input.ListenPort), input.ListenPort, "本地/远端监听端口必须在 1-65535 之间。");
                RequireAddress(errors, nameof(input.ConnectAddress), input.ConnectAddress, "目标地址不能为空。");
                RequirePort(errors, nameof(input.ConnectPort), input.ConnectPort, "目标端口必须在 1-65535 之间。");
                RequireAddress(errors, nameof(input.SshHost), input.SshHost, "SSH 主机不能为空。");
                break;
            case ForwardRuleType.SshDynamic:
                RequirePort(errors, nameof(input.ListenPort), input.ListenPort, "SOCKS 端口必须在 1-65535 之间。");
                RequireAddress(errors, nameof(input.SshHost), input.SshHost, "SSH 主机不能为空。");
                break;
            default:
                errors.Add(new(nameof(input.Type), "不支持的规则类型。"));
                break;
        }

        SafeToken(errors, nameof(input.SshUser), input.SshUser, "SSH 用户名只能包含字母、数字、点、下划线和短横线。");

        return new(errors.Count == 0, errors);
    }

    private static void Required(List<FieldError> errors, string field, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, message));
        }
    }

    private static void RequirePort(List<FieldError> errors, string field, int? port, string message)
    {
        if (port is null or < 1 or > 65535)
        {
            errors.Add(new(field, message));
        }
    }

    private static void RequireAddress(List<FieldError> errors, string field, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, message));
            return;
        }

        if (IPAddress.TryParse(value, out _) || HostRegex().IsMatch(value))
        {
            return;
        }

        errors.Add(new(field, "地址格式不正确，请填写 IP 或主机名。"));
    }

    private static void SafeToken(List<FieldError> errors, string field, string? value, string message)
    {
        if (!string.IsNullOrWhiteSpace(value) && !SafeTokenRegex().IsMatch(value))
        {
            errors.Add(new(field, message));
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9.-]{0,126}$")]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9._-]+$")]
    private static partial Regex SafeTokenRegex();

    [GeneratedRegex(@"^(\d{1,3}\.){3}\d{1,3}/([1-9]|[12]\d|3[0-2])$")]
    private static partial Regex CidrRegex();
}
