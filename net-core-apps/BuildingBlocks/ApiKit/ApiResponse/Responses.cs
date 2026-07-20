public class Response<T>
{
    public Response()
    {
    }
    public Response(T data)
    {
        Succeeded = true;
        Message = null;
        Errors = null;
        Data = data;
    }

    public T? Data { get; set; }
    public bool? Succeeded { get; set; }
    public string[]? Errors { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Finite request outcomes used at the API boundary. Values are deliberately bounded so the
/// outcome can be exported as a metric label without accepting caller-controlled cardinality.
/// </summary>
public enum HttpRequestOutcome
{
    Success,
    InvalidRequest,
    AuthenticationRequired,
    AccessForbidden,
    NotFound,
    Conflict,
    BusinessRejection,
    ClientRateLimited,
    LoadShed,
    ServerTimeout,
    DependencyUnavailable,
    ServerError,
    ClientCancelled,
}

/// <summary>
/// Shared, process-local key used by boundary components to attach an exact outcome to the
/// current request without adding an HTTP header or exposing implementation details to clients.
/// </summary>
public static class HttpRequestOutcomeContext
{
    public static object ItemKey { get; } = new();
}


