using Gleanvolt.Providers.Client;
using Microsoft.Extensions.Configuration;

namespace Gleanvolt.Chargers.Tests;

/// <summary>What every plugin shares: the address, the settings, the search's networks, the recording.</summary>
public sealed class SharedTests
{
    [Theory]
    [InlineData("192.168.1.6", 502, "192.168.1.6:502")]
    [InlineData(" charger.local:8080/ ", 80, "charger.local:8080")]
    [InlineData("http://192.168.1.30", 80, "192.168.1.30:80")]
    [InlineData("192.168.1.6:80", 502, "192.168.1.6:80")]
    [InlineData("http://192.168.1.6/rpc", 80, null)]
    [InlineData("ftp://192.168.1.6", 80, null)]
    [InlineData("http://admin:pw@192.168.1.6", 80, null)]
    [InlineData("", 80, null)]
    public void An_address_is_a_host_with_an_optional_port(string text, int defaultPort, string? expected)
    {
        Assert.Equal(expected, DeviceAddress.Parse(text, defaultPort)?.ToString());
    }

    [Fact]
    public void A_saved_setting_wins_over_the_configuration_and_survives_a_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gleanvolt-chargers-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Solax:Host"] = "192.168.1.6" })
                .Build();
            var plugin = new PluginInfo("solax-charger", "SolaX", "Solax", 8098, 502);
            var settings = new PluginSettings(new ProviderSecretStore(directory), configuration, plugin);

            Assert.Equal(SettingSource.Configuration, settings.Address.Source);
            settings.Address.Save("192.168.1.7");

            var restarted = new PluginSettings(new ProviderSecretStore(directory), configuration, plugin);
            Assert.Equal("192.168.1.7", restarted.Address.Current);
            Assert.Equal(SettingSource.Page, restarted.Address.Source);

            restarted.Address.Clear();
            Assert.Equal("192.168.1.6", restarted.Address.Current);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("192.168.1.0/24", 254)]
    [InlineData("10.0.4.0/22", 1022)]
    [InlineData("10.0.0.0/21", -1)]
    [InlineData("8.8.8.0/24", -1)]
    [InlineData("nonsense", -1)]
    public void The_search_takes_a_private_network_no_larger_than_a_22(string network, int hosts)
    {
        Assert.Equal(hosts, LanScan.Hosts(network)?.Count ?? -1);
    }

    [Fact]
    public async Task The_recording_keeps_the_path_and_never_the_authorization_header()
    {
        var recorder = new DeviceRecorder(TimeProvider.System);
        recorder.Start();
        using var http = new HttpClient(new RecordingHandler(recorder) { InnerHandler = new FakeTopAc() });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.50/rpc/Enum.GetStatus?owner=%22service:0%22&role=%22work_state%22");
        request.Headers.Authorization = new("Digest", "secret-response");

        await http.SendAsync(request);

        var json = recorder.ToJson("shelly-topac", "test", null);
        Assert.Contains("GET /rpc/Enum.GetStatus", json);
        Assert.Contains("charger_free", json);
        Assert.DoesNotContain("secret-response", json);
    }

    [Fact]
    public async Task Nothing_is_recorded_until_a_recording_starts()
    {
        var recorder = new DeviceRecorder(TimeProvider.System);
        using var http = new HttpClient(new RecordingHandler(recorder) { InnerHandler = new FakeTopAc() });

        await http.GetAsync("http://192.168.1.50/rpc/Enum.GetStatus?owner=%22service:0%22&role=%22work_state%22");

        Assert.Equal(0, recorder.Count);
    }
}
