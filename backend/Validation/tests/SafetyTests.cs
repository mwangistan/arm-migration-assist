using Validation.BuildValidation;
using Xunit;

namespace Validation.Tests;

public sealed class SafetyTests
{
    [Fact]
    public void FingerprintIsStableAcrossJsonWhitespaceAndDictionaryOrdering()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan(TestWorkspace.Command() with
        {
            Environment = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" }
        });
        var reordered = plan with
        {
            Commands = [plan.Commands[0] with { Environment = new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" } }]
        };
        Assert.Equal(PlanSafety.Fingerprint(plan), PlanSafety.Fingerprint(reordered));
        Assert.Equal(PlanSafety.Fingerprint(plan), PlanSafety.Fingerprint(ValidationJson.Deserialize<PreparedValidation>(ValidationJson.Serialize(plan))));
    }

    [Fact]
    public void RejectsEscapingCwdUnknownCriteriaCyclesAndProofPaths()
    {
        using var workspace = new TestWorkspace();
        var variants = new[]
        {
            TestWorkspace.Command() with { WorkingDirectory = ".." },
            TestWorkspace.Command() with { WorkingDirectory = workspace.Repo },
            TestWorkspace.Command() with { CriterionKeys = ["nonexistent"] },
            TestWorkspace.Command() with { DependsOn = ["build"] },
            TestWorkspace.Command() with { DependsOn = ["later"] },
            TestWorkspace.Command() with { TimeoutSeconds = 0 },
            TestWorkspace.Command() with { Proof = ProofKind.Trx, ProofPath = Path.Combine(workspace.Root, "escape.trx") },
            TestWorkspace.Command() with { Surface = ExecutionSurface.WindowsArm64Runtime }
        };
        foreach (var command in variants)
            Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(workspace.Plan(command)));
    }

    [Fact]
    public void RejectsDuplicateCriteriaAndCommands()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(plan with { Criteria = [plan.Criteria[0], plan.Criteria[0]] }));
        Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(plan with { Commands = [plan.Commands[0], plan.Commands[0]] }));
    }

    [Fact]
    public void RejectsMissingCollectionsRatherThanAccidentallyPassing()
    {
        using var workspace = new TestWorkspace();
        var plan = workspace.Plan();
        Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(plan with { Commands = null! }));
        Assert.Throws<InvalidDataException>(() => new DeterministicPlanner().Prepare(
            TestWorkspace.Migration() with { ValidationPlan = null! }, workspace.Context(), new(workspace.Evidence)));
    }

    [Fact]
    public void BuiltInTestAndContainerProofCannotBeDowngraded()
    {
        using var workspace = new TestWorkspace();
        Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(workspace.Plan(TestWorkspace.Command() with
        {
            Kind = CommandKind.DotNetTest, Surface = ExecutionSurface.WindowsArm64Runtime, Proof = ProofKind.ExitCode
        })));
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(("Dockerfile", "FROM example")),
            new(workspace.Evidence));
        Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(plan with
        {
            Commands = plan.Commands.Where(command => command.Kind != CommandKind.ContainerInspect).ToArray()
        }));
    }

    [Fact]
    public void RuntimeCoverageCannotBeMappedToCustomCommands()
    {
        using var workspace = new TestWorkspace();
        var plan = new DeterministicPlanner().Prepare(TestWorkspace.Migration(), workspace.Context(("App.csproj", "<Project />")),
            new(workspace.Evidence));
        Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(plan with
        {
            Commands = plan.Commands.Append(TestWorkspace.Command("custom-runtime") with
            {
                CriterionKeys = ["discovered:windows-arm64-runtime"]
            }).ToArray()
        }));
    }

    [Theory]
    [InlineData("arm64-vm")]
    [InlineData("snapdragon-x-elite")]
    [InlineData("x64-baseline-for-comparison")]
    public void DeviceAndBaselineCriteriaCannotBeSatisfiedByBuildOrCustomMappings(string device)
    {
        using var workspace = new TestWorkspace();
        var migration = TestWorkspace.Migration();
        migration = migration with { ValidationPlan = migration.ValidationPlan with { TargetDevices = [device] } };
        var repository = workspace.Context(("App.csproj", "<Project />"));
        var plan = new DeterministicPlanner().Prepare(migration, repository, new(workspace.Evidence));
        string key = "discovered:target-" + device;
        var commands = new[] { plan.Commands[0], TestWorkspace.Command("custom") };
        foreach (var command in commands)
        {
            var error = Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(plan with
            {
                Commands = [command with { CriterionKeys = command.CriterionKeys.Append(key).ToArray() }]
            }));
            Assert.Contains("typed device evidence", error.Message);
        }
        Assert.Throws<InvalidDataException>(() => new DeterministicPlanner().Prepare(migration, repository,
            new(workspace.Evidence, [new(plan.Commands[0].Id, [key])])));
        Assert.Contains(plan.ManualChecks, check => check.CriterionKey == key);
        var score = ScorecardBuilder.Build(plan, plan.Commands.Select(command =>
            new CommandResult(command.Id, ResultStatus.Passed, "Passed build", [])).ToArray());
        Assert.Equal(ResultStatus.NotRun, score.Criteria.Single(result => result.Criterion.Key == key).Status);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("multiple")]
    [InlineData("expression")]
    public void TestsCannotUseUnscopedOrAmbiguousFrameworkProof(string mode)
    {
        using var workspace = new TestWorkspace();
        string[] arguments = mode switch
        {
            "multiple" => ["test", "--framework", "net8.0", "--framework", "net9.0"],
            "expression" => ["test", "--framework", "$(Framework)"],
            _ => ["test"]
        };
        var plan = workspace.Plan(TestWorkspace.Command() with
        {
            Kind = CommandKind.DotNetTest, Surface = ExecutionSurface.WindowsArm64Runtime,
            Proof = ProofKind.Trx, ProofPath = Path.Combine(workspace.Evidence, "results.trx"), Arguments = arguments
        });
        Assert.Throws<InvalidDataException>(() => PlanSafety.Validate(plan));
    }
}
