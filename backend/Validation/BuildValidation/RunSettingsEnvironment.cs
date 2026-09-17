using System.Xml.Linq;

namespace Validation.BuildValidation;

public static class RunSettingsEnvironment
{
    private static readonly HashSet<string> AllowedVariables =
    [
        FoundryValidationAiOptions.EndpointEnvironmentVariable,
        FoundryValidationAiOptions.DeploymentEnvironmentVariable
    ];

    public static void Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        RepositoryPaths.RejectLinks(fullPath);
        if (!File.Exists(fullPath))
            throw new InvalidDataException($"Runsettings file does not exist: {fullPath}");
        if (new FileInfo(fullPath).Length > 262_144)
            throw new InvalidDataException("Runsettings file exceeds the 256 KiB limit.");

        var document = SafeXml.Parse(File.ReadAllText(fullPath));
        var environmentVariables = document.Root?
            .Elements().SingleOrDefault(element => element.Name.LocalName == "RunConfiguration")?
            .Elements().SingleOrDefault(element => element.Name.LocalName == "EnvironmentVariables");
        if (environmentVariables is null)
            throw new InvalidDataException("Runsettings must contain RunConfiguration/EnvironmentVariables.");

        var values = environmentVariables.Elements().ToDictionary(
            element => element.Name.LocalName,
            element => element.Value,
            StringComparer.Ordinal);
        var unsupported = values.Keys.Where(key => !AllowedVariables.Contains(key)).ToArray();
        if (unsupported.Length > 0)
            throw new InvalidDataException(
                $"Unsupported validation runsettings variables: {string.Join(", ", unsupported)}");
        foreach (string variable in AllowedVariables)
            if (values.TryGetValue(variable, out string? value))
                Environment.SetEnvironmentVariable(variable, value);
    }
}
