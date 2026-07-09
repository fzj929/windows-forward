namespace WindowForward.Api.Models;

public sealed record SystemFirewallRule(
    string DisplayName,
    string Enabled,
    string Direction,
    string Action,
    string Protocol,
    string LocalPort);
