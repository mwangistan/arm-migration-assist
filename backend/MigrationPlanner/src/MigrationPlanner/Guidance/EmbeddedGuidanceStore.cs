using System.Security.Cryptography;
using System.Text.Json;

namespace MigrationPlanner.Guidance;

public sealed class EmbeddedGuidanceStoreOptions
{
    public string CorpusRoot { get; set; } = string.Empty;

    public string ManifestFileName { get; set; } = "corpus.json";
}

/// <summary>
/// Loads the curated Windows on Arm guidance corpus from disk, verifies the
/// SHA-256 of every snippet file, and exposes read-only queries. Any hash
/// mismatch or missing file throws <see cref="CorpusIntegrityException"/>
/// during construction so the host refuses to start.
/// </summary>
public sealed class EmbeddedGuidanceStore
{
    private readonly IReadOnlyDictionary<string, GuidanceSnippet> _byId;
    private readonly IReadOnlyDictionary<Topic, IReadOnlyList<GuidanceSnippet>> _byTopic;

    public EmbeddedGuidanceStore(EmbeddedGuidanceStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.CorpusRoot))
        {
            throw new CorpusIntegrityException("CorpusRoot must be configured.");
        }

        var manifestPath = Path.Combine(options.CorpusRoot, options.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new CorpusIntegrityException($"Guidance manifest not found at '{manifestPath}'.");
        }

        GuidanceCorpusManifestV1? manifest;
        try
        {
            var manifestJson = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<GuidanceCorpusManifestV1>(manifestJson);
        }
        catch (JsonException ex)
        {
            throw new CorpusIntegrityException(
                $"Guidance manifest at '{manifestPath}' is not valid JSON.", ex);
        }

        if (manifest is null || manifest.Snippets.Count == 0)
        {
            throw new CorpusIntegrityException(
                $"Guidance manifest at '{manifestPath}' contains no snippets.");
        }

        CorpusVersion = manifest.CorpusVersion;

        var byId = new Dictionary<string, GuidanceSnippet>(StringComparer.Ordinal);
        var byTopic = new Dictionary<Topic, List<GuidanceSnippet>>();
        var seenRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var snippet in manifest.Snippets)
        {
            if (!byId.TryAdd(snippet.GuidanceId, snippet))
            {
                throw new CorpusIntegrityException(
                    $"Duplicate guidanceId '{snippet.GuidanceId}' in corpus manifest.");
            }

            if (!seenRelativePaths.Add(snippet.RelativePath))
            {
                throw new CorpusIntegrityException(
                    $"Duplicate relativePath '{snippet.RelativePath}' in corpus manifest.");
            }

            VerifySnippet(options.CorpusRoot, snippet);

            foreach (var topic in snippet.Topics)
            {
                if (!byTopic.TryGetValue(topic, out var list))
                {
                    list = new List<GuidanceSnippet>();
                    byTopic[topic] = list;
                }

                list.Add(snippet);
            }
        }

        _byId = byId;
        _byTopic = byTopic.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<GuidanceSnippet>)kv.Value.AsReadOnly());
    }

    public string CorpusVersion { get; }

    public IReadOnlyCollection<GuidanceSnippet> All() => _byId.Values.ToArray();

    public bool TryGet(string guidanceId, out GuidanceSnippet? snippet)
    {
        if (_byId.TryGetValue(guidanceId, out var found))
        {
            snippet = found;
            return true;
        }

        snippet = null;
        return false;
    }

    public IReadOnlyCollection<GuidanceSnippet> FindByTopic(Topic topic) =>
        _byTopic.TryGetValue(topic, out var list) ? list : Array.Empty<GuidanceSnippet>();

    private static void VerifySnippet(string corpusRoot, GuidanceSnippet snippet)
    {
        var snippetPath = Path.Combine(corpusRoot, snippet.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(snippetPath))
        {
            throw new CorpusIntegrityException(
                $"Snippet file '{snippet.RelativePath}' declared by guidance '{snippet.GuidanceId}' is missing.");
        }

        byte[] contents = File.ReadAllBytes(snippetPath);

        if (snippet.ByteLength is { } declaredLength && contents.LongLength != declaredLength)
        {
            throw new CorpusIntegrityException(
                $"Snippet '{snippet.GuidanceId}' byteLength mismatch: expected {declaredLength}, actual {contents.LongLength}.");
        }

        var hash = SHA256.HashData(contents);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(hex, snippet.Sha256, StringComparison.Ordinal))
        {
            throw new CorpusIntegrityException(
                $"Snippet '{snippet.GuidanceId}' SHA-256 mismatch: expected {snippet.Sha256}, actual {hex}.");
        }
    }
}
