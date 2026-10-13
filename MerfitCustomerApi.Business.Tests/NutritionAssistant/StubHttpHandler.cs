using System.Net;
using System.Text;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>Saglayici testleri icin sahte HTTP: gelen istekleri kaydeder, sirayla hazir yanit dondurur.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<Task<HttpResponseMessage>>> _responses = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    public StubHttpHandler Respond(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null, TimeSpan? delay = null)
    {
        _responses.Enqueue(async () =>
        {
            if (delay is { } d) await Task.Delay(d);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_responses.Count == 0) throw new InvalidOperationException("Beklenmeyen istek.");
        return await _responses.Dequeue()().WaitAsync(cancellationToken);
    }

    public HttpClient Client(string baseUrl) => new(this) { BaseAddress = new Uri(baseUrl) };
}
