using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MigrationPlanner.Infrastructure.Guidance;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Guidance;

public sealed class EmbeddedGuidanceStoreTests : IDisposable
{
    private readonly string _corpusRoot;

    public EmbeddedGuidanceStoreTests()
    {
        _corpusRoot = Path.Combine(Path.GetTempPath(), "mp-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_corpusRoot, "snippets"));
    }

    [Fact]
    public void Ctor_ValidCorpus_LoadsSnippets()
    {
        var body = Encoding.UTF8.GetBytes("# Sample snippet\n");
        WriteSnippet("snippets/sample-01.md", body);
        var sha = Sha256Hex(body);
        WriteManifest(sha, body.LongLength);

        var store = new EmbeddedGuidanceStore(new EmbeddedGuidanceStoreOptions { CorpusRoot = _corpusRoot });

        store.CorpusVersion.Should().Be("2026-09-15.1");
        store.All().Should().HaveCount(1);
        store.TryGet("sample-01", out var snippet).Should().BeTrue();
        snippet.Should().NotBeNull();
        snippet!.GuidanceId.Should().Be("sample-01");
    }

    [Fact]
    public void Ctor_TamperedSnippet_ThrowsCorpusIntegrityException()
    {
        var body = Encoding.UTF8.GetBytes("# Original\n");
        WriteSnippet("snippets/sample-01.md", body);
        var declaredSha = Sha256Hex(body);
        WriteManifest(declaredSha, body.LongLength);

        // Tamper with the snippet after the manifest was written.
        File.WriteAllBytes(Path.Combine(_corpusRoot, "snippets", "sample-01.md"),
            Encoding.UTF8.GetBytes("# Tampered\n"));

        var act = () => new EmbeddedGuidanceStore(new EmbeddedGuidanceStoreOptions { CorpusRoot = _corpusRoot });

        act.Should().Throw<CorpusIntegrityException>()
            .WithMessage("*SHA-256 mismatch*");
    }

    [Fact]
    public void Ctor_MissingSnippetFile_ThrowsCorpusIntegrityException()
    {
        var body = Encoding.UTF8.GetBytes("# missing\n");
        var declaredSha = Sha256Hex(body);
        WriteManifest(declaredSha, body.LongLength);

        var act = () => new EmbeddedGuidanceStore(new EmbeddedGuidanceStoreOptions { CorpusRoot = _corpusRoot });

        act.Should().Throw<CorpusIntegrityException>()
            .WithMessage("*missing*");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_corpusRoot))
            {
                Directory.Delete(_corpusRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private void WriteSnippet(string relativePath, byte[] contents)
    {
        var fullPath = Path.Combine(_corpusRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, contents);
    }

    private void WriteManifest(string sha256, long byteLength)
    {
        var manifest = $$"""
        {
          "schemaVersion": "1.0",
          "corpusVersion": "2026-09-15.1",
          "generatedAt": "2026-09-15T00:00:00Z",
          "producer": {
            "name": "test",
            "version": "0.0.0",
            "sourceKind": "hand-authored-prototype"
          },
          "source": "microsoft-learn-windows-on-arm",
          "snippets": [
            {
              "guidanceId": "sample-01",
              "sourceUrl": "https://learn.microsoft.com/en-us/windows/arm/overview",
              "title": "Sample",
              "section": "Overview",
              "retrievedAt": "2026-09-14T00:00:00Z",
              "relativePath": "snippets/sample-01.md",
              "sha256": "{{sha256}}",
              "topics": ["woa-overview"],
              "summary": "unit-test",
              "byteLength": {{byteLength}}
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_corpusRoot, "corpus.json"), manifest);
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
