using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MigrationPlanner.Tests.Integration.Fixtures;

/// <summary>
/// Points the guidance store at the real <c>knowledge/windows-on-arm</c> corpus
/// checked into the repository so integrity verification runs end-to-end.
/// </summary>
public sealed class PlannerWebApplicationFactory : WebApplicationFactory<MigrationPlanner.Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var corpusRoot = LocateCorpusRoot();

        Environment.SetEnvironmentVariable("MIGRATIONPLANNER_CORPUS_ROOT", corpusRoot);
        Environment.SetEnvironmentVariable("MIGRATIONPLANNER_MODEL_PROVIDER", "Fake");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Planner:CorpusRoot"] = corpusRoot,
                ["Planner:ModelProvider"] = "Fake",
            });
        });
    }

    private static string LocateCorpusRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "knowledge", "windows-on-arm");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "corpus.json")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate 'knowledge/windows-on-arm' corpus starting from " + AppContext.BaseDirectory);
    }
}
