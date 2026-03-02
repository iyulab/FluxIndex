using System.Net;
using System.Text;

namespace FluxIndex.Providers.OpenAI.Tests;

/// <summary>
/// A mock HTTP message handler for testing HTTP-based services.
/// Captures request details and returns configurable responses.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<string> _responseFactory;
    private readonly HttpStatusCode _statusCode;

    public HttpRequestMessage? LastRequest { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public string? LastRequestBody { get; private set; }

    public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode)
        : this(() => responseContent, statusCode)
    {
    }

    public MockHttpMessageHandler(Func<string> responseFactory, HttpStatusCode statusCode)
    {
        _responseFactory = responseFactory;
        _statusCode = statusCode;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestUri = request.RequestUri;

        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseFactory(), Encoding.UTF8, "application/json")
        };
    }
}
