using Azure.Core;

namespace MigrationPlanner.Infrastructure.Model;

/// <summary>
/// Forces a fixed OAuth scope on every token request. The Azure AI Foundry
/// unified endpoint (`*.services.ai.azure.com`) rejects tokens issued for the
/// SDK's default scope; this wrapper requests
/// <c>https://cognitiveservices.azure.com/.default</c> instead.
/// </summary>
internal sealed class ScopedTokenCredential : TokenCredential
{
    private readonly TokenCredential _inner;
    private readonly string[] _scopes;

    public ScopedTokenCredential(TokenCredential inner, string scope)
    {
        _inner = inner;
        _scopes = new[] { scope };
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        _inner.GetToken(new TokenRequestContext(_scopes, requestContext.ParentRequestId), cancellationToken);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        _inner.GetTokenAsync(new TokenRequestContext(_scopes, requestContext.ParentRequestId), cancellationToken);
}
