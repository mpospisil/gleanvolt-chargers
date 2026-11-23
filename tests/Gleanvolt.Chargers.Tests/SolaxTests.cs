using Gleanvolt.Plugin.Solax;
using Gleanvolt.Providers.Contracts;
using Gleanvolt.Providers.Contracts.Charger;
using Gleanvolt.Providers.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gleanvolt.Chargers.Tests;

/// <summary>
/// The SolaX plugin against a fake charger on Modbus TCP and the SDK's test hub: what it reads, what it writes, and that
/// it does what Gleanvolt's built-in driver did, register for register.
/// </summary>
public sealed class SolaxTests : IAsyncLifetime
{
    private static readonly PluginInfo Plugin = new("solax-charger", "SolaX EV charger", "Solax", 8098, 502);

    private readonly FakeModbusServer _charger = new();
    private Harness _harness = null!;
    private SolaxCharger _solax = null!;

    public Task InitializeAsync()
    {
        _harness = new Harness(Plugin, new Dictionary<string, string?> { ["Solax:Host"] = $"127.0.0.1:{_charger.Port}" });
        _solax = new SolaxCharger(_harness.Modbus(), _harness.Settings, TimeProvider.System, NullLogger<SolaxCharger>.Instance);

        // An X3-HAC, charging at 10 A in Fast, 6.9 kW.
        _charger.Input[SolaxRegisters.RunMode] = 2;
        _charger.Input[SolaxRegisters.ChargePowerTotal] = 6900;
        _charger.Holding[SolaxRegisters.ChargerUseMode] = 1;
        _charger.Holding[SolaxRegisters.ChargeCurrentSetpoint] = 1000;
        var serial = "503BAB123456";
        for (ushort i = 0; i < SolaxRegisters.SerialNumberCount; i++)
        {
            var a = i * 2 < serial.Length ? serial[i * 2] : '\0';
            var b = i * 2 + 1 < serial.Length ? serial[i * 2 + 1] : '\0';
            _charger.Holding[(ushort)(SolaxRegisters.SerialNumberStart + i)] = (ushort)(a << 8 | b);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _charger.DisposeAsync();
    }

    [Fact]
    public async Task A_charging_charger_reads_as_the_built_in_driver_read_it()
    {
        var hub = await _harness.ConnectAsync(_solax, new ChargerLimits(6, 16, 3, 230));

        var reading = await hub.ReadAsync();

        Assert.Equal(new ChargerCapabilities(6, 32, 3), hub.Capabilities);
        Assert.Equal(ChargerState.Charging, reading.Status);
        Assert.Equal(6900, reading.PowerWatts);
        Assert.Equal(10, reading.CurrentAmps);
        Assert.True(reading.ChargingAllowed);
        Assert.Equal("Fast", reading.Mode);
        Assert.Equal("X3-HAC", SolaxCharger.ModelOf(_solax.SerialNumber));
        Assert.True(_solax.Status!.Healthy);
    }

    [Theory]
    [InlineData(7, false, null)]     // junk after the link recovered: not Fast, and no mode
    [InlineData(0, false, "Stop")]
    [InlineData(2, false, "Eco")]
    public async Task Only_Fast_is_allowed_and_a_junk_use_mode_is_no_mode(ushort useMode, bool allowed, string? mode)
    {
        _charger.Holding[SolaxRegisters.ChargerUseMode] = useMode;
        var hub = await _harness.ConnectAsync(_solax, new ChargerLimits(6, 16, 3, 230));

        var reading = await hub.ReadAsync();

        Assert.Equal(allowed, reading.ChargingAllowed);
        Assert.Equal(mode, reading.Mode);
    }

    [Fact]
    public async Task A_holding_register_the_charger_refuses_does_not_fail_the_reading()
    {
        _charger.Holding.TryRemove(SolaxRegisters.ChargerUseMode, out _);
        _charger.Holding.TryRemove(SolaxRegisters.ChargeCurrentSetpoint, out _);
        var hub = await _harness.ConnectAsync(_solax, new ChargerLimits(6, 16, 3, 230));

        var reading = await hub.ReadAsync();

        Assert.Equal(ChargerState.Charging, reading.Status);
        Assert.Null(reading.ChargingAllowed);
        Assert.Null(reading.CurrentAmps);
    }

    [Fact]
    public async Task Commands_write_the_two_registers_the_built_in_driver_wrote_and_a_pause_is_0_A()
    {
        var hub = await _harness.ConnectAsync(_solax, new ChargerLimits(6, 16, 3, 230));

        Assert.True((await hub.SetChargingAllowedAsync(true)).Succeeded);
        Assert.True((await hub.SetCurrentAsync(10)).Succeeded);
        Assert.True((await hub.SetCurrentAsync(0, "a cloud")).Succeeded);
        Assert.True((await hub.SetCurrentAsync(40)).Succeeded);
        Assert.True((await hub.SetChargingAllowedAsync(false)).Succeeded);

        Assert.Equal(["0x060D=1", "0x0628=1000", "0x0628=0", "0x0628=3200", "0x060D=0"], _charger.Writes);
    }

    [Fact]
    public async Task An_X1_says_one_phase_and_is_refused_by_a_charge_control_set_to_three()
    {
        _harness.Settings.Get("phases").Save("1");

        var hub = await _harness.ConnectAsync(_solax, new ChargerLimits(6, 16, 3, 230), attaches: false);

        await Harness.Eventually(() => hub.LastRefusal is not null, "the hello should be refused");
        Assert.Equal(RefusalCodes.Limits, hub.LastRefusal!.Value.Code);
        Assert.Contains("ChargeControl:Phases (3)", hub.LastRefusal.Value.Reason);
    }

    [Fact]
    public async Task Without_an_address_the_read_fails_and_the_owner_is_asked_for_one()
    {
        var harness = new Harness(Plugin);
        await using var _ = harness;
        var solax = new SolaxCharger(harness.Modbus(), harness.Settings, TimeProvider.System, NullLogger<SolaxCharger>.Instance);
        var hub = await harness.ConnectAsync(solax, new ChargerLimits(6, 16, 3, 230));

        await Assert.ThrowsAnyAsync<Exception>(() => hub.ReadAsync());

        var status = await hub.WaitForStatusAsync(s => !s.Healthy);
        Assert.True(status.NeedsOwner);
        Assert.Equal(ProviderStatusCodes.NotSetUp, status.Code);
    }

    [Fact]
    public async Task The_check_names_the_model_and_refuses_an_inverter()
    {
        Assert.StartsWith("X3-HAC (503BAB123456)", await _solax.CheckAsync(_charger.Address, 1, CancellationToken.None));

        _charger.Input[SolaxRegisters.RunMode] = 2300;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _solax.CheckAsync(_charger.Address, 1, CancellationToken.None));
        Assert.Contains("inverter", error.Message);
    }

    [Fact]
    public async Task The_search_finds_a_SolaX_charger()
    {
        Assert.Equal("X3-HAC (503BAB123456)", await new SolaxSearch(_harness.Modbus()).ProbeAsync(_charger.Address, CancellationToken.None));
    }

    [Fact]
    public async Task A_recording_holds_every_exchange_as_sent_and_received()
    {
        _harness.Recorder.Start();
        var hub = await _harness.ConnectAsync(_solax, new ChargerLimits(6, 16, 3, 230));

        await hub.ReadAsync();
        await hub.SetCurrentAsync(8);

        var json = _harness.Recorder.ToJson(Plugin.ProviderId, "test", null);
        Assert.Contains("read input 0x001D x1", json);
        Assert.Contains("write 0x0628 = 0320", json);
    }

    [Theory]
    [InlineData(0, ChargerState.Available)]
    [InlineData(1, ChargerState.Preparing)]
    [InlineData(2, ChargerState.Charging)]
    [InlineData(3, ChargerState.Finishing)]
    [InlineData(7, ChargerState.SuspendedEv)]
    [InlineData(8, ChargerState.SuspendedEvse)]
    [InlineData(12, ChargerState.SuspendedEvse)]
    [InlineData(13, ChargerState.Available)]
    [InlineData(14, ChargerState.Unknown)]
    public void Each_run_mode_maps_to_a_state_Gleanvolt_reads_the_same_way(ushort runMode, ChargerState expected)
    {
        Assert.Equal(expected, SolaxCharger.StateOf(runMode));
    }
}
