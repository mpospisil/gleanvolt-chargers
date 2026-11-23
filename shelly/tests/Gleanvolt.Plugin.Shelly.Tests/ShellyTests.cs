using Gleanvolt.Chargers;
using System.Net;
using Gleanvolt.Plugin.Shelly;
using Gleanvolt.Providers.Contracts;
using Gleanvolt.Providers.Testing;
using Gleanvolt.Providers.Contracts.Charger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gleanvolt.Plugin.Shelly.Tests;

/// <summary>The Shelly TopAC plugin, moved from Gleanvolt, against the fake TopAC Gleanvolt's tests used.</summary>
public sealed class ShellyTests : IAsyncLifetime
{
    private static readonly PluginInfo Plugin = new("shelly-topac", "Shelly TopAC", "Shelly", 8097, 80);

    private readonly FakeTopAc _topAc = new();
    private PluginHarness _harness = null!;
    private ShellyCharger _charger = null!;

    public Task InitializeAsync()
    {
        _harness = new PluginHarness(Plugin, new Dictionary<string, string?> { ["Shelly:Host"] = "192.168.1.50" });
        var options = new ShellyOptions();
        var rpc = new ShellyRpcClient(_harness.FakeClients(_topAc), _harness.Settings, options);
        _charger = new ShellyCharger(rpc, options, TimeProvider.System, NullLogger<ShellyCharger>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_charging_TopAc_reads_with_its_16_A_ceiling_on_three_phases()
    {
        _topAc.Charging(10);
        _topAc.CurrentLimit = 10;
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        var reading = await hub.ReadAsync();

        Assert.Equal(new ChargerCapabilities(6, 16, 3), hub.Capabilities);
        Assert.Equal(ChargerState.Charging, reading.Status);
        Assert.Equal(3 * 230 * 10, reading.PowerWatts, precision: 6);
        Assert.Equal(10, reading.CurrentAmps);
        Assert.Equal(16, reading.MaxCurrentAmps);
    }

    [Fact]
    public async Task A_pause_is_the_switch_off_held_by_the_plugin_and_a_setpoint_resumes_it_limit_first()
    {
        _topAc.Charging(10);
        _topAc.CurrentLimit = 10;
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        await hub.SetChargingAllowedAsync(true);
        await hub.SetCurrentAsync(0);
        await hub.SetCurrentAsync(8);

        Assert.Equal(["start_charging=true", "start_charging=false", "current_limit=8", "start_charging=true"], _topAc.Writes);
    }

    [Fact]
    public async Task A_charger_asking_for_a_password_with_none_saved_asks_the_owner_for_it()
    {
        _topAc.Fail = HttpStatusCode.Unauthorized;
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        await Assert.ThrowsAnyAsync<Exception>(() => hub.ReadAsync());

        var status = await hub.WaitForStatusAsync(s => !s.Healthy);
        Assert.True(status.NeedsOwner);
        Assert.Equal(ProviderStatusCodes.Refused, status.Code);
        Assert.Contains("password", status.Message);
    }

    [Fact]
    public void The_password_is_offered_for_Shellys_digest_only_and_only_once_saved()
    {
        var credentials = new ShellyCredentials(_harness.Settings);
        var charger = new Uri("http://192.168.1.50/");

        Assert.Null(credentials.GetCredential(charger, "Digest"));

        _harness.Settings.Get("password").Save("s3cret");

        Assert.Equal("admin", credentials.GetCredential(charger, "Digest")!.UserName);
        Assert.Equal("s3cret", credentials.GetCredential(charger, "Digest")!.Password);
        Assert.Null(credentials.GetCredential(charger, "Basic"));
    }

    [Fact]
    public async Task Auto_balance_on_in_the_Shelly_app_asks_the_owner_to_turn_it_off()
    {
        _topAc.AutoBalance = true;
        var hub = await _harness.ConnectAsync(_charger, new ChargerLimits(6, 16, 3, 230));

        await hub.ReadAsync();

        var status = await hub.WaitForStatusAsync(s => !s.Healthy);
        Assert.True(status.NeedsOwner);
        Assert.Contains("Auto balance", status.Message);
    }

    [Theory]
    [InlineData("charger_free", true, ChargerState.Available)]
    [InlineData("charger_wait", false, ChargerState.SuspendedEvse)]
    [InlineData("charger_complete", true, ChargerState.SuspendedEv)]
    [InlineData("charger_complete", false, ChargerState.SuspendedEvse)]
    [InlineData("charger_new_in_firmware_9", true, ChargerState.Unknown)]
    public void Each_work_state_maps_to_one_contract_state(string workState, bool startCharging, ChargerState expected)
    {
        Assert.Equal(expected, ShellyCharger.StateOf(workState, startCharging));
    }
}
