using Validation.BuildValidation;
using Xunit;

namespace Validation.Tests;

public sealed class RunSettingsEnvironmentTests
{
    [Fact]
    public void LoadsOnlyFoundryConfiguration()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "validation.runsettings");
        File.WriteAllText(path,
            """
            <RunSettings>
              <RunConfiguration>
                <EnvironmentVariables>
                  <ARM_MIGRATION_FOUNDRY_ENDPOINT>https://example.test/mai/v1</ARM_MIGRATION_FOUNDRY_ENDPOINT>
                  <ARM_MIGRATION_FOUNDRY_MODEL>demo-model</ARM_MIGRATION_FOUNDRY_MODEL>
                </EnvironmentVariables>
              </RunConfiguration>
            </RunSettings>
            """);
        string? priorEndpoint = Environment.GetEnvironmentVariable(
            FoundryValidationAiOptions.EndpointEnvironmentVariable);
        string? priorModel = Environment.GetEnvironmentVariable(
            FoundryValidationAiOptions.DeploymentEnvironmentVariable);
        try
        {
            RunSettingsEnvironment.Load(path);

            Assert.Equal("https://example.test/mai/v1", Environment.GetEnvironmentVariable(
                FoundryValidationAiOptions.EndpointEnvironmentVariable));
            Assert.Equal("demo-model", Environment.GetEnvironmentVariable(
                FoundryValidationAiOptions.DeploymentEnvironmentVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                FoundryValidationAiOptions.EndpointEnvironmentVariable, priorEndpoint);
            Environment.SetEnvironmentVariable(
                FoundryValidationAiOptions.DeploymentEnvironmentVariable, priorModel);
        }
    }

    [Fact]
    public void RejectsUnrelatedEnvironmentVariables()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "validation.runsettings");
        File.WriteAllText(path,
            """
            <RunSettings>
              <RunConfiguration>
                <EnvironmentVariables>
                  <PATH>untrusted</PATH>
                </EnvironmentVariables>
              </RunConfiguration>
            </RunSettings>
            """);

        var error = Assert.Throws<InvalidDataException>(() => RunSettingsEnvironment.Load(path));

        Assert.Contains("Unsupported", error.Message);
    }
}
