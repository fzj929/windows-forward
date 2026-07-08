namespace WindowForward.Api.Models;

public sealed class ForwardRule
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ForwardRuleType Type { get; set; }
    public ForwardProtocol Protocol { get; set; } = ForwardProtocol.Tcp;
    public string? ListenAddress { get; set; }
    public int? ListenPort { get; set; }
    public string? ConnectAddress { get; set; }
    public int? ConnectPort { get; set; }
    public string? NatName { get; set; }
    public string? Prefix { get; set; }
    public string? RouteDestination { get; set; }
    public string? RouteMask { get; set; }
    public string? RouteGateway { get; set; }
    public string? SshHost { get; set; }
    public string? SshUser { get; set; }
    public bool Enabled { get; set; }
    public int? RuntimeProcessId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastAppliedAt { get; set; }
}
