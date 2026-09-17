using System.Text.Json;
using System.Text.Json.Serialization;
using ArmMigrationAssist.RepositoryDiscovery.Models;

namespace ArmMigrationAssist.RepositoryDiscovery;

public static class RepositoryDiscoveryCommand
{
    private const string Usage =
        "Usage: repository-discovery <github-url|local-git-path> [--output <assessment.json>]";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default)
    {
        standardOutput ??= Console.Out;
        standardError ??= Console.Error;

        if (args.Any(argument => argument is "--help" or "-h"))
        {
            await standardOutput.WriteLineAsync(Usage);
            return 0;
        }

        try
        {
            var options = Parse(args);
            var assessment = await new RepositoryDiscoveryService()
                .DiscoverAsync(options.Source, cancellationToken);
            await WriteAssessmentAsync(assessment, options.OutputPath, cancellationToken);
            await standardOutput.WriteLineAsync($"Wrote repository assessment to {options.OutputPath}");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await standardError.WriteLineAsync("Repository discovery was canceled.");
            return 130;
        }
        catch (Exception exception) when (exception is RepositoryDiscoveryException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync($"Repository discovery failed: {exception.Message}");
            return 2;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Repository discovery failed unexpectedly.");
            return 1;
        }
    }

    private static CommandOptions Parse(IReadOnlyList<string> args)
    {
        string? source = null;
        var outputPath = Path.Combine("artifacts", "repository-assessment.json");

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument == "--output")
            {
                if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw new RepositoryDiscoveryException("--output requires a file path.");
                }

                outputPath = args[index];
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                throw new RepositoryDiscoveryException($"Unknown option: {argument}");
            }

            if (source is not null)
            {
                throw new RepositoryDiscoveryException("Provide exactly one repository source.");
            }

            source = argument;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new RepositoryDiscoveryException(Usage);
        }

        return new CommandOptions(source, outputPath);
    }

    private static async Task WriteAssessmentAsync(
        RepositoryAssessment assessment,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new RepositoryDiscoveryException("The output path must include a file name.");
        Directory.CreateDirectory(outputDirectory);

        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    assessment,
                    new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        WriteIndented = true,
                    },
                    cancellationToken);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record CommandOptions(string Source, string OutputPath);
}