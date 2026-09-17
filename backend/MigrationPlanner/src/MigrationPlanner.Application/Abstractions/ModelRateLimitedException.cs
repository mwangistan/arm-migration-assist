namespace MigrationPlanner.Application.Abstractions;

/// <summary>
/// Thrown by an <see cref="IPlannerModel"/> when the underlying model provider
/// returns HTTP 429 Too Many Requests. Carries the provider's suggested
/// retry delay so the API layer can pass it back to the caller as an HTTP
/// <c>Retry-After</c> header.
/// </summary>
public sealed class ModelRateLimitedException : Exception
{
    public ModelRateLimitedException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
