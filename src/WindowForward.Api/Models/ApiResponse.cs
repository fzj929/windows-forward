namespace WindowForward.Api.Models;

public sealed record ApiResponse<T>(bool Success, string Message, T? Data)
{
    public static ApiResponse<T> Ok(T? data, string message = "操作成功。") => new(true, message, data);
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T? data, string message = "操作成功。") => new(true, message, data);
    public static ApiResponse<object> Fail(string message, object? data = null) => new(false, message, data);
}
