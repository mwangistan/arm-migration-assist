using System.Text;
using AutomatedMigration.Api.Contracts;
using AutomatedMigration.Api.Jobs;
using AutomatedMigration.Api.Configuration;
using AutomatedMigration.Models;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomatedMigration.Api.Tests;

public sealed class MigrationJobStoreTests
{
    private static readonly MigrationPlan Plan = new(
        "1.0", "plan-test-1", "native-arm64",
        new[]
        {
            new WorkItem(
                "wi-1", 1, "P0", "Add ARM64 target", "obj",
                "build/add-arm64-target", new[] { "Dockerfile" }, new[] { "patch" },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<AcceptanceTest>(), true)
        });

    private static readonly RepositoryTarget Target = new(
        "https://github.com/example/repo",
        "0000000000000000000000000000000000000000");

    [Fact]
    public void Enqueue_returns_job_with_status_Queued_and_writes_to_channel()
    {
        var store = new MigrationJobStore(Options.Create(new AutomationOptions()));
        var job = store.Enqueue(Plan.PlanId, Target, Plan);

        job.JobId.Should().StartWith("job-");
        job.Status.Should().Be(MigrationJobStatus.Queued);
        job.PlanId.Should().Be("plan-test-1");
        job.Target.CommitSha.Should().Be(Target.CommitSha);

        store.TryGet(job.JobId, out var found).Should().BeTrue();
        found.JobId.Should().Be(job.JobId);
    }

    [Fact]
    public async Task Queue_is_readable_in_enqueue_order()
    {
        var store = new MigrationJobStore(Options.Create(new AutomationOptions()));
        var j1 = store.Enqueue(Plan.PlanId, Target, Plan);
        var j2 = store.Enqueue(Plan.PlanId, Target, Plan);

        var first = await store.Queue.ReadAsync();
        var second = await store.Queue.ReadAsync();
        first.Should().Be(j1.JobId);
        second.Should().Be(j2.JobId);
    }

    [Fact]
    public void TryGet_unknown_id_returns_false()
    {
        var store = new MigrationJobStore(Options.Create(new AutomationOptions()));
        store.TryGet("job-does-not-exist", out _).Should().BeFalse();
    }
}
