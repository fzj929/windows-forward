namespace WindowForward.Api.Models;

public sealed record SystemPortProxyRule(
    string ListenAddress,
    int ListenPort,
    string ConnectAddress,
    int ConnectPort);
