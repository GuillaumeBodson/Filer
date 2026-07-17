using System.Net;

namespace Filer.Ui.Tests.Auth;

/// <summary>
/// Test inner handler: returns queued responses in order and records what it received
/// (method, URI, bearer token, body) so assertions can inspect each call.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responders = new();

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

            return Task.FromResult(response);
        });
        return this;
    }

    /// <summary>Queues an async responder, e.g. one gated on a TaskCompletionSource.</summary>
    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _responders.Enqueue(responder);
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
            body,
            request.Content?.Headers.ContentType?.ToString()));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("StubHttpMessageHandler received an unexpected request.");
        }

        return await _responders.Dequeue()(request).ConfigureAwait(false);
    }

    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string? BearerToken,
        byte[]? Body,
        string? ContentType);
}
