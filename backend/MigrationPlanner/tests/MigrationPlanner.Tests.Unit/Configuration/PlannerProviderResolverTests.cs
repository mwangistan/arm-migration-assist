using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MigrationPlanner.Api.Configuration;
using MigrationPlanner.Infrastructure.DependencyInjection;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Configuration;

// Guards the fail-loud contract on planner provider selection. A missing
// selector must never silently fall back to Fake in production; this is the
// only test that keeps that invariant honest.
public sealed class PlannerProviderResolverTests
{
    [Fact]
    public void Resolve_env_var_wins_over_config()
    {
        var config = BuildConfig(("Planner:ModelProvider", "Phi"));
        var env = FakeEnv(("MIGRATIONPLANNER_MODEL_PROVIDER", "Hosted"));

        var result = PlannerProviderResolver.Resolve(config, env);

        result.Should().Be(PlannerModelProvider.Hosted);
    }

    [Theory]
    [InlineData("Fake", PlannerModelProvider.Fake)]
    [InlineData("Hosted", PlannerModelProvider.Hosted)]
    [InlineData("phi", PlannerModelProvider.Phi)]
    [InlineData("HOSTED", PlannerModelProvider.Hosted)]
    public void Resolve_env_var_is_case_insensitive(string envValue, PlannerModelProvider expected)
    {
        var config = BuildConfig();
        var env = FakeEnv(("MIGRATIONPLANNER_MODEL_PROVIDER", envValue));

        var result = PlannerProviderResolver.Resolve(config, env);

        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_falls_back_to_config_when_env_var_absent()
    {
        var config = BuildConfig(("Planner:ModelProvider", "Fake"));
        var env = FakeEnv();

        var result = PlannerProviderResolver.Resolve(config, env);

        result.Should().Be(PlannerModelProvider.Fake);
    }

    [Fact]
    public void Resolve_throws_when_neither_env_nor_config_set()
    {
        var config = BuildConfig();
        var env = FakeEnv();

        var act = () => PlannerProviderResolver.Resolve(config, env);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*refuses to start without an explicit model provider*")
            .Which.Message.Should().Contain("MIGRATIONPLANNER_MODEL_PROVIDER");
    }

    [Fact]
    public void Resolve_throws_when_env_var_is_unknown()
    {
        var config = BuildConfig();
        var env = FakeEnv(("MIGRATIONPLANNER_MODEL_PROVIDER", "NotAProvider"));

        var act = () => PlannerProviderResolver.Resolve(config, env);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NotAProvider*not a recognized provider*");
    }

    [Fact]
    public void Resolve_throws_when_config_is_unknown()
    {
        var config = BuildConfig(("Planner:ModelProvider", "GptBanana"));
        var env = FakeEnv();

        var act = () => PlannerProviderResolver.Resolve(config, env);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GptBanana*not a recognized provider*");
    }

    [Fact]
    public void Resolve_whitespace_env_var_is_treated_as_absent()
    {
        var config = BuildConfig(("Planner:ModelProvider", "Phi"));
        var env = FakeEnv(("MIGRATIONPLANNER_MODEL_PROVIDER", "   "));

        var result = PlannerProviderResolver.Resolve(config, env);

        result.Should().Be(PlannerModelProvider.Phi);
    }

    private static IConfiguration BuildConfig(params (string Key, string Value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Key, e => (string?)e.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static Func<string, string?> FakeEnv(params (string Key, string Value)[] entries)
    {
        var lookup = entries.ToDictionary(e => e.Key, e => (string?)e.Value, StringComparer.Ordinal);
        return name => lookup.TryGetValue(name, out var value) ? value : null;
    }
}
