namespace WindowForward.Api.Models;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<FieldError> Errors);

public sealed record FieldError(string Field, string Message);
