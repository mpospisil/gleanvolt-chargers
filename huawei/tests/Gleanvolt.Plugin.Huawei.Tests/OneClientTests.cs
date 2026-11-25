using System.Net;
using Gleanvolt.Chargers;
using Gleanvolt.Plugin.Huawei;
using Gleanvolt.Providers.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gleanvolt.Plugin.Huawei.Tests;

/// <summary>
/// A charger that serves one Modbus client, with the plugin's own connection as it (#3): the page's Save and check and
/// the search ask the charger in use through that connection, never a second one.
/// </summary>
public sealed class OneClientTests : IAsyncLifetime
{
    private static readonly PluginInfo Plugin = new("huawei-scharger", "Huawei SCharger", "Huawei", 8099, 502);

    private readonly FakeModbusServer _charger = new();
    private OneClientRelay _relay = null!;
    private PluginHarness _harness = null!;
    private ModbusDevice _device = null!;
    private HuaweiCharger _huawei = null!;

    public async Task InitializeAsync()
    {
        _relay = new OneClientRelay(_charger.Port);
        _harness = new PluginHarness(Plugin, new Dictionary<string, string?> { ["Huawei:Host"] = _relay.Address.ToString() });
        _device = _harness.Modbus();
        _huawei = new HuaweiCharger(_device, _harness.Settings, _harness.Session, TimeProvider.System, NullLogger<HuaweiCharger>.Instance);

        // A 7KS on L1 only, idle, with the plugin reading it.
        _charger.SetHolding32(SChargerRegisters.PhaseL1Voltage, 2310);
        for (ushort address = 0x1002; address < 0x100E; address += 2)
        {
            _charger.SetHolding32(address, 0);
        }

        // Every register the read asks for: a failed exchange would close the plugin's connection.
        _charger.SetHolding32(SChargerRegisters.MaxChargePower, 74);
        _charger.Holding[SChargerRegisters.ChargingControl] = (ushort)SChargerControl.Resumed;

        await _huawei.ReadAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _relay.DisposeAsync();
        await _charger.DisposeAsync();
    }

    [Fact]
    public async Task Save_and_check_of_the_address_in_use_reads_over_the_plugins_own_connection()
    {
        Assert.StartsWith("SCharger-7KS", await _huawei.CheckAsync(_relay.Address, 1, CancellationToken.None));
        Assert.Equal(0, _relay.Dropped);
    }

    [Fact]
    public async Task The_search_finds_the_charger_the_plugin_is_reading()
    {
        var found = await new HuaweiSearch(_device).FindAsync([IPAddress.Loopback], CancellationToken.None);

        Assert.Equal("SCharger-7KS", Assert.Single(found).Description);
        Assert.Equal(0, _relay.Dropped);
    }
}
