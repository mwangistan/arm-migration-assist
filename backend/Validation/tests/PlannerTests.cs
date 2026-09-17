using Validation.BuildValidation;
using Xunit;

namespace Validation.Tests;

public sealed class PlannerTests
{
    [Fact]
    public void DiscoversSupportedBuildsButNeverInventsSmokeChecksOrProseMappings()
    {
        using var workspace = new TestWorkspace();
        var repository = workspace.Context(
            ("src/App.csproj", "<Project />"),
            ("tests/App.Tests.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>"),
            ("native/App.vcxproj", "<Project><ItemGroup><ProjectConfiguration Include=\"Release|ARM64\" /></ItemGroup></Project>"),
            ("Dockerfile", "FROM --platform=linux/amd64 example"));
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), repository, new(workspace.Evidence));

        Assert.Equal(2, plan.Commands.Count(command => command.Kind == CommandKind.DotNetBuild));
        var test = Assert.Single(plan.Commands, command => command.Kind == CommandKind.DotNetTest);
        Assert.Contains("win-arm64", test.Arguments);
        Assert.Equal(ExecutionSurface.WindowsArm64Runtime, test.Surface);
        Assert.Equal(ProofKind.Trx, test.Proof);
        Assert.StartsWith(workspace.Evidence, test.ProofPath);
        Assert.Contains("/p:Platform=ARM64", Assert.Single(plan.Commands, c => c.Kind == CommandKind.NativeBuild).Arguments);
        Assert.Contains("linux/arm64", Assert.Single(plan.Commands, c => c.Kind == CommandKind.ContainerBuild).Arguments);
        Assert.Single(plan.Commands, c => c.Kind == CommandKind.ContainerInspect);
        Assert.DoesNotContain(plan.Commands, c => c.Kind is CommandKind.NativeSmoke or CommandKind.ContainerSmoke);
        Assert.DoesNotContain(plan.Commands, c => c.CriterionKeys.Contains("validation:vc-build"));
        Assert.Contains(plan.ManualChecks, c => c.CriterionKey == "validation:vc-build");
        Assert.Contains(plan.Criteria, c => c.Key == "acceptance:wi-one:at-one");
        Assert.Contains(plan.Criteria, c => c.Key == "acceptance:wi-two:at-one");
        Assert.Contains(plan.Criteria, c => c.Key == "discovered:target-arm64-vm");
    }

    [Fact]
    public void DetectsTestSdkWithXmlNamespaceAndLeavesUnsupportedNativeConfigManual()
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(
            ("Test.csproj", "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>"),
            ("App.vcxproj", "<Project><ProjectConfiguration Include=\"Release|x64\" /></Project>")),
            new(workspace.Evidence));
        Assert.Single(plan.Commands, command => command.Kind == CommandKind.DotNetTest);
        Assert.DoesNotContain(plan.Commands, command => command.Kind == CommandKind.NativeBuild);
        Assert.Contains(plan.Notices, notice => notice.Message.Contains("no explicit ARM64"));
    }

    [Fact]
    public void ExplicitSmokeInputsAndMappingsAreIncludedInTheReviewablePlan()
    {
        using var workspace = new TestWorkspace();
        var repo = workspace.Context(("App.vcxproj", "<Project><ProjectConfiguration Include=\"Debug|ARM64\" /></Project>"),
            ("Dockerfile", "FROM example"));
        var seed = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), repo, new(workspace.Evidence));
        string buildId = seed.Commands.Single(c => c.Kind == CommandKind.NativeBuild).Id;
        var options = new PlanningOptions(workspace.Evidence,
            [new(buildId, ["validation:vc-build", "acceptance:wi-one:at-one"])],
            [new("App.vcxproj", Path.Combine("bin", "App.exe"), ["--smoke"], ["validation:vc-function"])],
            [new("Dockerfile", ["app", "--smoke"], ["validation:vc-function"])]);
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), repo, options);
        Assert.Single(plan.Commands, c => c.Kind == CommandKind.NativeSmoke);
        var smoke = Assert.Single(plan.Commands, c => c.Kind == CommandKind.ContainerSmoke);
        Assert.Equal(["run", "--rm", "--platform", "linux/arm64"], smoke.Arguments.Take(4));
        Assert.Contains("validation:vc-build", plan.Commands.Single(c => c.Id == buildId).CriterionKeys);
        Assert.DoesNotContain(plan.ManualChecks, c => c.CriterionKey == "validation:vc-build");
    }

    [Fact]
    public void MissingArtifactsAndMalformedXmlRemainHonestGaps()
    {
        using var workspace = new TestWorkspace();
        var empty = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(), new(workspace.Evidence));
        Assert.Empty(empty.Commands);
        Assert.Contains(empty.Criteria, c => c.Key == "discovered:supported-build");
        var malformed = new DeterministicPlanner().Prepare(TestWorkspace.Migration(),
            workspace.Context(("App.csproj", "<Project>")), new(workspace.Evidence));
        Assert.DoesNotContain(malformed.Commands, c => c.Kind == CommandKind.DotNetTest);
        Assert.Contains(malformed.Notices, n => n.Message.Contains("Cannot inspect"));
    }

    [Fact]
    public void RejectsUnsupportedVersionAndUnknownMappings()
    {
        using var workspace = new TestWorkspace();
        Assert.Throws<InvalidDataException>(() => new DeterministicPlanner().Prepare(
            TestWorkspace.Migration() with { SchemaVersion = "2.0" }, workspace.Context(), new(workspace.Evidence)));
        Assert.Throws<InvalidDataException>(() => new DeterministicPlanner().Prepare(
            TestWorkspace.Migration(), workspace.Context(), new(workspace.Evidence, [new("invented", ["validation:vc-build"])])));
    }

    [Fact]
    public void AllMigrationCategoriesAndEvidenceSurviveDeserialization()
    {
        using var workspace = new TestWorkspace();
        var migration = TestWorkspace.Migration();
        migration = migration with
        {
            ValidationPlan = migration.ValidationPlan with
            {
                ReliabilityChecks = [new("vc-rel", "Reliability", "Reliable")],
                PerformanceChecks = [new("vc-perf", "Performance", "Fast")],
                PowerChecks = [new("vc-power", "Power", "Efficient")],
                OfflineChecks = [new("vc-offline", "Offline", "Offline")],
                AccessibilityChecks = [new("vc-a11y", "Accessibility", "Accessible")],
                WindowsExperienceChecks = [new("vc-win", "Windows", "Native UX")]
            }
        };
        var parsed = ValidationJson.Deserialize<MigrationPlan>(ValidationJson.Serialize(migration));
        var plan = new DeterministicPlanner().Prepare(parsed, workspace.Context(), new(workspace.Evidence));
        Assert.Equal(8, plan.Criteria.Count(c => c.Source == CheckSource.ValidationCheck));
        var build = plan.Criteria.Single(c => c.SourceId == "vc-build");
        Assert.Equal(["ev-build"], build.SourceEvidenceIds);
        Assert.Equal(["guide-build"], build.GuidanceIds);
    }

    [Fact]
    public void MultiTargetTestsHaveSeparateCommandsProofsAndSharedProjectCoverage()
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(
            ("Tests.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject><TargetFrameworks>net8.0; net9.0</TargetFrameworks></PropertyGroup></Project>")),
            new(workspace.Evidence));
        var tests = plan.Commands.Where(command => command.Kind == CommandKind.DotNetTest).ToArray();
        Assert.Equal(2, tests.Length);
        Assert.Equal(2, tests.Select(command => command.Id).Distinct().Count());
        Assert.Equal(2, tests.Select(command => command.ProofPath).Distinct().Count());
        Assert.Equal(["net8.0", "net9.0"], tests.Select(command =>
            command.Arguments[command.Arguments.ToList().IndexOf("--framework") + 1]));
        Assert.All(tests, command => Assert.Contains(Path.GetDirectoryName(command.ProofPath!)!, command.Arguments));
        Assert.All(tests, command => Assert.Equal([plan.Commands[0].Id], command.DependsOn));
        Assert.Equal(2, tests[0].CriterionKeys.Intersect(tests[1].CriterionKeys).Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("<TargetFrameworks>$(TestFrameworks)</TargetFrameworks>")]
    [InlineData("<TargetFrameworks>net8.0;</TargetFrameworks>")]
    [InlineData("<TargetFrameworks>net8.0;NET8.0</TargetFrameworks>")]
    [InlineData("<TargetFrameworks Condition=\"'$(Mode)' == 'test'\">net8.0;net9.0</TargetFrameworks>")]
    [InlineData("<PropertyGroup Condition=\"'$(Mode)' == 'test'\"><TargetFramework>net8.0</TargetFramework></PropertyGroup>")]
    [InlineData("<TargetFramework>net8.0</TargetFramework><TargetFrameworks>net8.0;net9.0</TargetFrameworks>")]
    public void UnresolvedOrAmbiguousFrameworksStayManual(string metadata)
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(
            ("Tests.csproj", $"<Project><IsTestProject>true</IsTestProject>{metadata}</Project>")), new(workspace.Evidence));
        Assert.DoesNotContain(plan.Commands, command => command.Kind == CommandKind.DotNetTest);
        Assert.Contains(plan.ManualChecks, check => check.CriterionKey.StartsWith("discovered:dotnet-test-"));
        Assert.Contains(plan.Notices, notice => notice.Message.Contains("per-framework proof"));
    }
}
