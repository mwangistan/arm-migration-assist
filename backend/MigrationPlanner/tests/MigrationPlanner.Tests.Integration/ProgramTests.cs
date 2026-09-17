using FluentAssertions;
using Xunit;

namespace MigrationPlanner.Tests.Integration;

public sealed class ProgramTests
{
    [Fact]
    public void ResolveDefaultCorpusRoot_FindsCorpusAboveApiProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"planner-root-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(root, "backend", "MigrationPlanner", "src", "MigrationPlanner.Api");
        var corpusRoot = Path.Combine(root, "knowledge", "windows-on-arm");

        try
        {
            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(corpusRoot);
            File.WriteAllText(Path.Combine(corpusRoot, "corpus.json"), "{}");

            var resolved = MigrationPlanner.Api.Program.ResolveDefaultCorpusRoot(contentRoot);

            resolved.Should().Be(corpusRoot);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
