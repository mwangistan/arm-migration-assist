using System.Security.Cryptography;
using System.Text;

namespace ArmMigrationAssist.RepositoryWorkspace;

// Identifies a materialized working copy uniquely across concurrent callers.
// Anonymous vs authenticated clones are cached separately because the anonymous
// path is required by the "no ambient Git credentials for anonymous cloud
// assessment" guarantee: sharing a clone across the boundary would leak the
// authenticated identity's fetch reach into the anonymous surface.
public sealed record RepositoryCloneKey(
    string RepositoryUrl,
    string CommitSha,
    bool Anonymous)
{
    // Same SHA can exist in unrelated repos (forks, mirrors) but `git fetch origin <sha>` binds
    // to a specific remote URL, so keeping URL+SHA distinct in the cache path avoids one caller's
    // clone leaking into another whose origin can't actually serve that SHA.
    public string DirectorySegment
    {
        get
        {
            var scope = Anonymous ? "anon" : "auth";
            var urlHash = ShortHash(RepositoryUrl);
            return $"{scope}-{urlHash}-{CommitSha}";
        }
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
