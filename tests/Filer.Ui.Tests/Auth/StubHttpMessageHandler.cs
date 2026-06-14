using System.Net;

namespace Filer.Ui.Tests.Auth;

/// <summary>
/// Test inner handler: returns queued responses in order and records what it received
/// (method, URI, bearer token, body) so assertions can inspect each call.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpMessageHandler Enqueue(HttpStatusCode status, string? jsonBody = null)
    {
        _responders.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (jsonBody is not null)
            {
                response.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }

            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.Parameter,
            body));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("StubHttpMessageHandler received an unexpected request.");
        }

        return _responders.Dequeue()(request);
    }

    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string? BearerToken,
        byte[]? Body);
}
