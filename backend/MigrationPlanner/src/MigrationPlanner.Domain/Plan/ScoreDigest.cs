using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MigrationPlanner.Domain.Plan;

/// <summary>
/// Canonical SHA-256 digest of a deterministic <see cref="ReadinessScoreV1"/>.
/// The plan MUST embed this value byte-for-byte; the plan validator uses it to
/// prove the model did not alter the deterministic score.
/// </summary>
public static class ScoreDigest
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Compute(ReadinessScoreV1 score)
    {
        ArgumentNullException.ThrowIfNull(score);
        var json = JsonSerializer.Serialize(score, CanonicalOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
