namespace WindowForward.Api.Models;

public enum ForwardRuleType
{
    PortProxy = 0,
    Firewall = 1,
    Nat = 2,
    Route = 3,
    IpForwarding = 4,
    SshLocal = 5,
    SshRemote = 6,
    SshDynamic = 7
}
