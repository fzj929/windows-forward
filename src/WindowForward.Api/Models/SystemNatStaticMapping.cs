namespace WindowForward.Api.Models;

public sealed record SystemNatStaticMapping(
    string NatName,
    string Protocol,
    string ExternalIPAddress,
    int ExternalPort,
    string InternalIPAddress,
    int InternalPort);
