namespace Validation.Api;

public sealed class ValidationApiOptions
{
    public string? StorageRoot { get; set; }
    public int QueueCapacity { get; set; } = 100;
    public bool AllowNonLoopback { get; set; }
}
