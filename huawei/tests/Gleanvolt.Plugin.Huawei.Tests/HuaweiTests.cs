using Gleanvolt.Chargers;
using Gleanvolt.Plugin.Huawei;
using Gleanvolt.Providers.Contracts;
using Gleanvolt.Providers.Testing;
using Gleanvolt.Providers.Contracts.Charger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gleanvolt.Plugin.Huawei.Tests;

/// <summary>The Huawei plugin against a fake SCharger: power and Unknown, the setpoint in kilowatts, the model.</summary>
public sealed class HuaweiTests : IAsyncLifetime
{
    private static readonly PluginInfo Plugin = new("huawei-scharger", "Huawei SCharger", "Huawei", 8099, 502);

    private readonly FakeModbusServer _charger = new();
    private PluginHarness _harness = null!;
    private HuaweiCharger _huawei = null!;

    public Task InitializeAsync()
    {
        _harness = new PluginHarness(Plugin, new Dictionary<string, string?> { ["Huawei:Host"] = $"127.0.0.1:{_charger.Port}" });
        _huawei = new HuaweiCharger(_harness.Modbus(), _harness.Settings, _harness.Session, TimeProvider.System, NullLogger<HuaweiCharger>.Instance);

        // A 7KS on L1 only, idle: 231 V, nothing drawn.
        _charger.SetHolding32(SChargerRegisters.PhaseL1Voltage, 2310);
        for (ushort address = 0x1002; address < 0x100E; address += 2)
        {
            _charger.SetHolding32(address, 0);
        }

        _charger.SetHolding32(SChargerRegisters.MaxChargePower, 74);
        _charger.Holding[SChargerRegisters.ChargingControl] = (ushort)SChargerControl.Resumed;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _charger.DisposeAsync();
    }

    [Fact]
    public async Task Idle_reads_unknown_and_drawing_reads_charging_as_the_built_in_driver_did()
    {
        var hub = await _harness.ConnectAsync(_huawei, new ChargerLimits(6, 32, 1, 230));

        var idle = await hub.ReadAsync();
        Assert.Equal(ChargerState.Unknown, idle.Status);
        Assert.True(idle.ChargingAllowed);
        Assert.Equal(32, idle.CurrentAmps);   // 7.4 kW on one phase at 230 V

        _charger.SetHolding32(SChargerRegisters.TotalOutputPower, 37);
        _charger.SetHolding32(SChargerRegisters.PhaseL1Current, 160);
        var charging = await hub.ReadAsync();
        Assert.Equal(ChargerState.Charging, charging.Status);
        Assert.Equal(3700, charging.PowerWatts, precision: 6);
    }

    [Fact]
    public async Task The_setpoint_is_power_at_the_welcomes_voltage_and_phases_and_a_pause_is_0_kW()
    {
        _harness.Settings.Get("model").Save("22kt");
        var hub = await _harness.ConnectAsync(_huawei, new ChargerLimits(6, 16, 3, 230));

        await hub.SetCurrentAsync(16);
        await hub.SetCurrentAsync(0);
        await hub.SetChargingAllowedAsync(false);

        // 16 A x 3 x 230 V = 11.0 kW; then 0; then the control paused.
        Assert.Equal(["0x2000=0,110", "0x2000=0,0", "0x2006=1"], _charger.Writes);
    }

    [Fact]
    public async Task A_7KS_says_one_phase_and_a_22KT_three()
    {
        Assert.Equal(1, _huawei.Capabilities.Phases);

        _huawei.SetModel("22kt");

        Assert.Equal(3, _huawei.Capabilities.Phases);
    }

    [Fact]
    public async Task The_check_tells_a_22KT_from_a_7KS_by_L2_and_saves_the_model_when_none_was_set()
    {
        Assert.False(_huawei.ModelKnown);
        Assert.StartsWith("SCharger-7KS", await _huawei.CheckAsync(_charger.Address, 1, CancellationToken.None));
        Assert.Equal("7ks", _harness.Settings.Get("model").Current);

        _harness.Settings.Get("model").Clear();
        _charger.SetHolding32(SChargerRegisters.PhaseL2Voltage, 2290);
        Assert.StartsWith("SCharger-22KT", await _huawei.CheckAsync(_charger.Address, 1, CancellationToken.None));
        Assert.Equal("22kt", _harness.Settings.Get("model").Current);
    }

    [Fact]
    public async Task Until_a_model_is_known_the_owner_is_asked_to_choose_one()
    {
        var hub = await _harness.ConnectAsync(_huawei, new ChargerLimits(6, 16, 1, 230));

        await hub.ReadAsync();

        var status = await hub.WaitForStatusAsync(s => !s.Healthy);
        Assert.True(status.NeedsOwner);
        Assert.Contains("model", status.Message);
    }

    [Fact]
    public async Task The_search_finds_an_SCharger_by_its_mains_voltage()
    {
        Assert.Equal("SCharger-7KS", await new HuaweiSearch(_harness.Modbus()).ProbeAsync(_charger.Address, CancellationToken.None));

        _charger.SetHolding32(SChargerRegisters.PhaseL1Voltage, 42);
        Assert.Null(await new HuaweiSearch(_harness.Modbus()).ProbeAsync(_charger.Address, CancellationToken.None));
    }

    [Theory]
    [InlineData(16, 3, 230, 110u)]
    [InlineData(16, 1, 230, 37u)]
    [InlineData(32, 3, 230, 220u)]
    [InlineData(0, 3, 230, 0u)]
    public void Amps_convert_to_tenths_of_a_kilowatt_never_above_22(int amps, int phases, double volts, uint expected)
    {
        Assert.Equal(expected, HuaweiCharger.PowerForAmps(amps, phases, volts));
    }
}
