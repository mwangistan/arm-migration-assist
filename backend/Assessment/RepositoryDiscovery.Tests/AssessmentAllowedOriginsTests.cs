using System.Collections.Generic;
using ArmMigrationAssist.RepositoryDiscovery;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ArmMigrationAssist.RepositoryDiscovery.Tests;

public sealed class AssessmentAllowedOriginsTests
{
    [Fact]
    public void CommaSeparatedList_ProducesOneOriginPerEntry()
    {
        var configuration = BuildConfiguration(
            "http://localhost:5173,http://localhost:3000,https://orange-pebble-024f7ff0f.6.azurestaticapps.net");

        var origins = AssessmentApi.ReadAllowedOrigins(configuration);

        Assert.Equal(
            new[]
            {
                "http://localhost:5173",
                "http://localhost:3000",
                "https://orange-pebble-024f7ff0f.6.azurestaticapps.net",
            },
            origins);
    }

    [Fact]
    public void SemicolonSeparatedList_ProducesOneOriginPerEntry()
    {
        var configuration = BuildConfiguration(
            "http://localhost:5173;http://localhost:3000;https://orange-pebble-024f7ff0f.6.azurestaticapps.net");

        var origins = AssessmentApi.ReadAllowedOrigins(configuration);

        Assert.Equal(
            new[]
            {
                "http://localhost:5173",
                "http://localhost:3000",
                "https://orange-pebble-024f7ff0f.6.azurestaticapps.net",
            },
            origins);
    }

    [Fact]
    public void MixedDelimitersAndWhitespace_AreTrimmedAndSplit()
    {
        var configuration = BuildConfiguration(
            " http://localhost:5173 ; http://localhost:3000 , https://orange-pebble-024f7ff0f.6.azurestaticapps.net ");

        var origins = AssessmentApi.ReadAllowedOrigins(configuration);

        Assert.Equal(
            new[]
            {
                "http://localhost:5173",
                "http://localhost:3000",
                "https://orange-pebble-024f7ff0f.6.azurestaticapps.net",
            },
            origins);
    }

    [Fact]
    public void MissingOrEmpty_FallsBackToLocalhostDefaults()
    {
        var missing = AssessmentApi.ReadAllowedOrigins(BuildConfiguration(null));
        var empty = AssessmentApi.ReadAllowedOrigins(BuildConfiguration(string.Empty));
        var delimitersOnly = AssessmentApi.ReadAllowedOrigins(BuildConfiguration(" , ; , "));

        var expected = new[] { "http://localhost:5173", "http://127.0.0.1:5173" };
        Assert.Equal(expected, missing);
        Assert.Equal(expected, empty);
        Assert.Equal(expected, delimitersOnly);
    }

    private static IConfiguration BuildConfiguration(string? dashboardOrigins)
    {
        var entries = new Dictionary<string, string?>();
        if (dashboardOrigins is not null)
        {
            entries["DashboardOrigins"] = dashboardOrigins;
        }
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries)
            .Build();
    }
}
