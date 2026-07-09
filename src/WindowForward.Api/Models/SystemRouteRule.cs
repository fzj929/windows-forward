namespace WindowForward.Api.Models;

public sealed record SystemRouteRule(
    string DestinationPrefix,
    string NextHop,
    int RouteMetric,
    string InterfaceAlias,
    string PolicyStore);
