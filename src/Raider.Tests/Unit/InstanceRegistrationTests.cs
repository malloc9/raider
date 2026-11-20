namespace Raider.Tests.Unit;

using FluentAssertions;
using Raider.Shared;
using Xunit;

public sealed class InstanceRegistrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "raider-instances-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Json_UsesCamelCaseFieldNames()
    {
        var json = new InstanceRegistration(1234, 50123, DateTimeOffset.Parse("2026-11-20T08:00:00Z")).ToJson();

        json.Should().Contain("\"pid\": 1234").And.Contain("\"port\": 50123").And.Contain("\"processStart\"");
        json.Should().NotContain("baseUrl");
    }

    [Fact]
    public void WriteTo_ThenReadAll_RoundTrips()
    {
        var registration = new InstanceRegistration(1234, 50123, DateTimeOffset.Parse("2026-11-20T08:00:00Z"));

        registration.WriteTo(_directory);

        File.Exists(Path.Combine(_directory, "1234.json")).Should().BeTrue();
        InstanceRegistration.ReadAll(_directory).Should().ContainSingle().Which.Should().Be(registration);
    }

    [Fact]
    public void WriteTo_ReplacesTheEarlierFileForTheSamePid()
    {
        new InstanceRegistration(1234, 50123, DateTimeOffset.UnixEpoch).WriteTo(_directory);
        new InstanceRegistration(1234, 50999, DateTimeOffset.UnixEpoch).WriteTo(_directory);

        InstanceRegistration.ReadAll(_directory).Should().ContainSingle().Which.Port.Should().Be(50999);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public void ReadAll_ReturnsOneEntryPerInstance()
    {
        new InstanceRegistration(1, 50001, DateTimeOffset.UnixEpoch).WriteTo(_directory);
        new InstanceRegistration(2, 50002, DateTimeOffset.UnixEpoch).WriteTo(_directory);

        InstanceRegistration.ReadAll(_directory).Select(r => r.Pid).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void ReadAll_SkipsFilesThatAreNotRegistrations()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "garbage.json"), "not json");
        File.WriteAllText(Path.Combine(_directory, "empty.json"), "{}");
        new InstanceRegistration(7, 50007, DateTimeOffset.UnixEpoch).WriteTo(_directory);

        InstanceRegistration.ReadAll(_directory).Should().ContainSingle().Which.Pid.Should().Be(7);
    }

    [Fact]
    public void ReadAll_ReturnsNothing_WhenTheFolderDoesNotExist()
    {
        InstanceRegistration.ReadAll(_directory).Should().BeEmpty();
    }

    [Fact]
    public void Delete_RemovesTheFile_AndIgnoresAMissingOne()
    {
        new InstanceRegistration(1234, 50123, DateTimeOffset.UnixEpoch).WriteTo(_directory);

        InstanceRegistration.Delete(_directory, 1234);
        InstanceRegistration.Delete(_directory, 1234);

        InstanceRegistration.ReadAll(_directory).Should().BeEmpty();
    }

    [Fact]
    public void BaseUrl_PointsAtTheRaiderPathOnLocalhost()
    {
        new InstanceRegistration(1, 50123, DateTimeOffset.UnixEpoch).BaseUrl
            .Should().Be(new Uri("http://localhost:50123/raider/"));
    }
}
