using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Ownership;

/// <summary>
/// Cross-owner access to a protected resource must return 404, not 403 or 200
/// (05-security.md) — the single most important behavioural guarantee in the
/// system, and one that must never regress (12-testing-strategy.md).
///
/// Exercised end to end against the first real owned resource — a Document via
/// <c>GET /api/v1/documents/{id}</c> (issue #35) — through the real pipeline:
/// JWT validation → ICurrentUser → owner-scoped store lookup → problem-details.
/// This test replaced the temporary ownership probe (OwnershipProbeEndpoints +
/// OwnershipProbeTests), deleted when this slice landed, per the removal trigger
/// in 12-testing-strategy.md.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class OwnershipTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CrossOwnerAccessToOwnedResource_Returns404()
    {
        // Owner A creates a document…
        HttpClient owner = _factory.CreateClient();
        AuthenticatedUser ownerUser = await owner.RegisterAndAuthenticateAsync();
        owner.WithBearer(ownerUser.AccessToken);
        Guid documentId = await UploadDocumentAsync(owner);

        // …and owner B requests it with B's bearer token.
        HttpClient intruder = _factory.CreateClient();
        AuthenticatedUser intruderUser = await intruder.RegisterAndAuthenticateAsync();
        intruder.WithBearer(intruderUser.AccessToken);

        HttpResponseMessage response = await intruder.GetAsync($"{DocumentsRoute}/{documentId}", Ct);

        // The whole point: not 403 (which would confirm the resource exists), not 200.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NonExistentResource_IsIndistinguishableFromNotOwned_Returns404()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> UploadDocumentAsync(HttpClient client)
    {
        // Unique bytes per call so tests sharing one database never collide on the
        // dedupe index.
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 ownership test content {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "ownership.pdf" } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }
}
