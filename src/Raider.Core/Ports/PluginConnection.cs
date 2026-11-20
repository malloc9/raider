namespace Raider.Core.Ports;

using System.Net.Http;
using Raider.Shared;

/// <summary>
/// Where the plugin clients send their requests: the most recently started registered Instance, with the
/// per-install token on every request. A stop-gap until Raider drives every Instance (#33).
/// </summary>
public sealed class PluginConnection
{
    /// <summary>Base address the clients use; the handler swaps it for the Instance's real one.</summary>
    public static readonly Uri PlaceholderBaseUrl = new("http://raider-instance/raider/");

    private const string TokenHeader = "X-Raider-Token";
    private readonly string _instancesDirectory;

    public PluginConnection(string? instancesDirectory = null)
    {
        _instancesDirectory = instancesDirectory ?? InstanceRegistration.DefaultDirectory;
    }

    /// <summary>The per-install token; null until the desktop app has read it.</summary>
    public string? Token { get; set; }

    /// <summary>The most recently started registered Instance, or null when none is registered.</summary>
    public InstanceRegistration? CurrentInstance() =>
        InstanceRegistration.ReadAll(_instancesDirectory).MaxBy(r => r.ProcessStart);

    /// <summary>A handler that sends each request to <see cref="CurrentInstance"/>.</summary>
    public HttpMessageHandler CreateHandler(HttpMessageHandler? innerHandler = null) =>
        new RoutingHandler(this, innerHandler ?? new HttpClientHandler());

    private sealed class RoutingHandler : DelegatingHandler
    {
        private readonly PluginConnection _connection;

        public RoutingHandler(PluginConnection connection, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            _connection = connection;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var instance = _connection.CurrentInstance()
                ?? throw new HttpRequestException("No running Instance has registered with Raider");

            var relative = PlaceholderBaseUrl.MakeRelativeUri(request.RequestUri!);
            request.RequestUri = new Uri(instance.BaseUrl, relative);

            if (_connection.Token is { Length: > 0 } token && !request.Headers.Contains(TokenHeader))
            {
                request.Headers.Add(TokenHeader, token);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
