namespace Raider.Tests.Unit;

using System.Net;
using System.Net.Http;
using FluentAssertions;
using Raider.Core.Ports;
using Raider.Shared;
using Xunit;

public sealed class PluginConnectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "raider-instances-" + Guid.NewGuid().ToString("N"));
    private readonly CapturingHandler _inner = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private HttpClient CreateClient(PluginConnection connection) =>
        new(connection.CreateHandler(_inner)) { BaseAddress = PluginConnection.PlaceholderBaseUrl };

    [Fact]
    public async Task Requests_GoToTheRegisteredInstancesPort_WithTheToken()
    {
        new InstanceRegistration(1234, 50123, DateTimeOffset.UtcNow).WriteTo(_directory);
        var client = CreateClient(new PluginConnection(_directory) { Token = "secret" });

        await client.GetAsync("plugin-log?maxLines=5");

        _inner.Request!.RequestUri.Should().Be(new Uri("http://localhost:50123/raider/plugin-log?maxLines=5"));
        _inner.Request.Headers.GetValues("X-Raider-Token").Should().ContainSingle().Which.Should().Be("secret");
    }

    [Fact]
    public async Task Requests_GoToTheMostRecentlyStartedInstance()
    {
        new InstanceRegistration(1, 50001, DateTimeOffset.UtcNow.AddHours(-1)).WriteTo(_directory);
        new InstanceRegistration(2, 50002, DateTimeOffset.UtcNow).WriteTo(_directory);
        var client = CreateClient(new PluginConnection(_directory));

        await client.GetAsync("health");

        _inner.Request!.RequestUri!.Port.Should().Be(50002);
    }

    [Fact]
    public async Task Requests_Fail_WhenNoInstanceIsRegistered()
    {
        var client = CreateClient(new PluginConnection(_directory));

        var act = () => client.GetAsync("health");

        await act.Should().ThrowAsync<HttpRequestException>();
        _inner.Request.Should().BeNull();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
