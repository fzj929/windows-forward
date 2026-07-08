namespace WindowForward.Api.Models;

public sealed class CommandExecutionLog
{
    public int Id { get; set; }
    public int? ForwardRuleId { get; set; }
    public string? RuleName { get; set; }
    public required string Action { get; set; }
    public required string CommandText { get; set; }
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public required string Message { get; set; }
    public string? Output { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
}
