using ArmMigrationAssist.Api.Assessment.RepositoryDiscovery;

namespace ArmMigrationAssist.Tests;

/// <summary>
/// Story 1.1 - Repository Ingestion. Covers the pure, deterministic ingestion logic:
/// public-GitHub URL validation, clone-error formatting, and offline license detection.
/// </summary>
public sealed class RepositoryIngestionTests
{
    [Theory]
    [InlineData("https://github.com/microsoft/WindowsAppSDK")]
    [InlineData("https://github.com/microsoft/WindowsAppSDK/")]
    [InlineData("https://GitHub.com/owner/repo")]
    [InlineData("https://github.com/owner/repo.git")]
    public void IsPublicGitHubRepository_accepts_valid_public_urls(string url) =>
        Assert.True(RepositoryIngestionService.IsPublicGitHubRepository(url));

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://github.com/owner/repo")]                 // not https
    [InlineData("ftp://github.com/owner/repo")]                  // wrong scheme
    [InlineData("https://gitlab.com/owner/repo")]                // wrong host
    [InlineData("https://raw.github.com/owner/repo")]            // wrong host
    [InlineData("https://github.com/owner")]                     // missing repo segment
    [InlineData("https://github.com/owner/repo/tree/main")]      // too many segments
    [InlineData("https://user:pass@github.com/owner/repo")]      // embedded credentials
    [InlineData("https://github.com:8443/owner/repo")]           // non-default port
    [InlineData("https://github.com/owner/repo?x=1")]            // query string
    [InlineData("https://github.com/owner/repo#frag")]           // fragment
    [InlineData("https://github.com/owner/..")]                  // path traversal
    public void IsPublicGitHubRepository_rejects_invalid_urls(string url) =>
        Assert.False(RepositoryIngestionService.IsPublicGitHubRepository(url));

    [Fact]
    public void FormatCloneError_explains_windows_long_path_failure()
    {
        var msg = RepositoryIngestionService.FormatCloneError(
            "error: unable to create file some/very/long/path: Filename too long");

        Assert.Contains("long repository paths", msg);
        Assert.Contains("LongPathsEnabled", msg);
    }

    [Fact]
    public void FormatCloneError_surfaces_fatal_git_lines()
    {
        var msg = RepositoryIngestionService.FormatCloneError(
            "Cloning into 'r'...\nfatal: repository 'https://github.com/owner/repo' not found\n");

        Assert.StartsWith("git clone failed:", msg);
        Assert.Contains("not found", msg);
    }

    [Fact]
    public void FormatCloneError_falls_back_when_no_useful_lines()
    {
        var msg = RepositoryIngestionService.FormatCloneError("some unrelated noise\nmore noise");
        Assert.Equal("git clone failed.", msg);
    }

    [Theory]
    [InlineData("Permission is hereby granted, free of charge, to any person obtaining a copy (MIT License)", "MIT")]
    [InlineData("Apache License\nVersion 2.0, January 2004", "Apache-2.0")]
    [InlineData("GNU GENERAL PUBLIC LICENSE\nVersion 3, 29 June 2007", "GPL-3.0")]
    [InlineData("Mozilla Public License Version 2.0", "MPL-2.0")]
    [InlineData("This is free and unencumbered software released into the public domain.", "Unlicense")]
    public void IdentifyLicense_maps_common_licenses(string text, string expected) =>
        Assert.Equal(expected, RepositoryIngestionService.IdentifyLicense(text));

    [Theory]
    [InlineData("")]
    [InlineData("Copyright the authors. All rights reserved.")]
    public void IdentifyLicense_returns_null_for_unknown_text(string text) =>
        Assert.Null(RepositoryIngestionService.IdentifyLicense(text));

    [Fact]
    public void DetectLicense_reads_repository_license_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arm-ma-lic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "LICENSE"),
                "Apache License\nVersion 2.0, January 2004\nhttp://www.apache.org/licenses/");

            Assert.Equal("Apache-2.0", RepositoryIngestionService.DetectLicense(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectLicense_returns_null_when_no_license_file_present()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arm-ma-nolic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(RepositoryIngestionService.DetectLicense(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectLicense_falls_back_to_file_name_for_unrecognized_text()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arm-ma-unk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "LICENSE.txt"), "Bespoke proprietary terms.");
            Assert.Equal("LICENSE.txt", RepositoryIngestionService.DetectLicense(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
