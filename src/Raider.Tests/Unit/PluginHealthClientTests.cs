namespace Raider.Tests.Unit;

using Raider.Core.Models;
using Raider.Core.Ports;
using Xunit;
using FluentAssertions;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Text.Json;

/// <summary>
/// Unit tests for PluginHealthClient.
/// </summary>
public sealed class PluginHealthClientTests : IDisposable
{
    private readonly string _instancesDirectory = Path.Combine(Path.GetTempPath(), "raider-instances-" + Guid.NewGuid().ToString("N"));
    private readonly HttpMessageHandlerMock _handlerMock;
    private readonly PluginHealthClient _client;

    public PluginHealthClientTests()
    {
        _handlerMock = new HttpMessageHandlerMock();
        new Raider.Shared.InstanceRegistration(1234, 50123, DateTimeOffset.UtcNow).WriteTo(_instancesDirectory);
        _client = new PluginHealthClient(new PluginConnection(_instancesDirectory), _handlerMock);
    }

    public void Dispose()
    {
        if (Directory.Exists(_instancesDirectory))
        {
            Directory.Delete(_instancesDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsHealth_WhenEndpointRespondsWithValidToken()
    {
        var healthJson = JsonSerializer.Serialize(new
        {
            isInGame = true,
            gameBuild = "7.20.0",
            pluginVersion = "1.0.0"
        });

        _handlerMock.SetupResponse(HttpStatusCode.OK, healthJson, "application/json");

        var health = await _client.GetHealthAsync("test-token");

        health.Should().NotBeNull();
        health!.IsInGame.Should().BeTrue();
        health.GameBuild.Version.Should().Be("7.20.0");
        health.PluginVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsNull_WhenEndpointReturns401()
    {
        _handlerMock.SetupResponse(HttpStatusCode.Unauthorized, "{\"error\":\"Invalid token\"}", "application/json");

        var health = await _client.GetHealthAsync("wrong-token");

        health.Should().BeNull();
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsNull_WhenEndpointReturns404()
    {
        _handlerMock.SetupResponse(HttpStatusCode.NotFound, "Not found", "text/plain");

        var health = await _client.GetHealthAsync("test-token");

        health.Should().BeNull();
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsNull_WhenRequestThrows()
    {
        _handlerMock.SetupException(new HttpRequestException("Connection refused"));

        var health = await _client.GetHealthAsync("test-token");

        health.Should().BeNull();
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenHealthEndpointResponds()
    {
        var healthJson = JsonSerializer.Serialize(new
        {
            isInGame = false,
            gameBuild = "7.20.0",
            pluginVersion = "1.0.0"
        });

        _handlerMock.SetupResponse(HttpStatusCode.OK, healthJson, "application/json");

        var healthy = await _client.IsHealthyAsync("test-token");

        healthy.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenHealthEndpointFails()
    {
        _handlerMock.SetupResponse(HttpStatusCode.Unauthorized, "Unauthorized", "application/json");

        var healthy = await _client.IsHealthyAsync("test-token");

        healthy.Should().BeFalse();
    }

    /// <summary>
    /// Mock HttpMessageHandler for testing.
    /// </summary>
    private sealed class HttpMessageHandlerMock : HttpMessageHandler
    {
        private HttpResponseMessage? _response;
        private Exception? _exception;

        public void SetupResponse(HttpStatusCode statusCode, string content, string mediaType)
        {
            _response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
            };
            _exception = null;
        }

        public void SetupException(Exception exception)
        {
            _exception = exception;
            _response = null;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception != null)
            {
                throw _exception;
            }
            return Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}