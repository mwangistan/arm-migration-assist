using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MigrationPlanner.Api.Automation;
using MigrationPlanner.Domain.Assessment;
using MigrationPlanner.Domain.Plan;
using Xunit;

namespace MigrationPlanner.Tests.Unit.Automation;

public sealed class HttpAutomationDispatcherTests
{
    private static readonly Repository ValidRepo = new(
        "sample-repo",
        "https://github.com/example/repo",
        "0123456789abcdef0123456789abcdef01234567",
        "main");

    private static MigrationPlanV1 MinimalPlan() =>
        new()
        {
            AssessmentId = "assessment-test",
        };

    private static HttpAutomationDispatcher CreateDispatcher(FakeHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://f3.example/") };
        return new HttpAutomationDispatcher(client, NullLogger<HttpAutomationDispatcher>.Instance);
    }

    [Fact]
    public async Task Returns_null_when_repository_url_is_missing()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var dispatcher = CreateDispatcher(handler);
        var repo = ValidRepo with { Url = "" };

        var result = await dispatcher.DispatchAsync(MinimalPlan(), repo, CancellationToken.None);

        result.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_null_when_repository_commit_sha_is_missing()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var dispatcher = CreateDispatcher(handler);
        var repo = ValidRepo with { CommitSha = "" };

        var result = await dispatcher.DispatchAsync(MinimalPlan(), repo, CancellationToken.None);

        result.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_dispatch_on_202_with_jobId()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    """{"jobId":"job-abc","status":"queued","statusUrl":"https://f3.example/api/migration-actions/jobs/job-abc"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return response;
        });
        var dispatcher = CreateDispatcher(handler);

        var result = await dispatcher.DispatchAsync(MinimalPlan(), ValidRepo, CancellationToken.None);

        result.Should().NotBeNull();
        result!.JobId.Should().Be("job-abc");
        result.Status.Should().Be("queued");
        result.StatusUrl.Should().Be("https://f3.example/api/migration-actions/jobs/job-abc");
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/migration-actions");
    }

    [Fact]
    public async Task Returns_null_when_f3_responds_non_success()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var dispatcher = CreateDispatcher(handler);

        var result = await dispatcher.DispatchAsync(MinimalPlan(), ValidRepo, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_http_throws()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("boom"));
        var dispatcher = CreateDispatcher(handler);

        var result = await dispatcher.DispatchAsync(MinimalPlan(), ValidRepo, CancellationToken.None);

        result.Should().BeNull();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public List<HttpRequestMessage> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_factory(request));
        }
    }
}
