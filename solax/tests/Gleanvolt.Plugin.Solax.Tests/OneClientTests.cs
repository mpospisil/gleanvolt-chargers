using System.Net;
using Gleanvolt.Chargers;
using Gleanvolt.Plugin.Solax;
using Gleanvolt.Providers.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gleanvolt.Plugin.Solax.Tests;

/// <summary>
/// The charger serves one Modbus client, and the plugin's own connection is it (#3): the page's Save and check and the
/// search ask the charger in use through that connection, never a second one, which the charger drops.
/// </summary>
public sealed class OneClientTests : IAsyncLifetime
{
    private static readonly PluginInfo Plugin = new("solax-charger", "SolaX EV charger", "Solax", 8098, 502);

    private readonly FakeModbusServer _charger = new();
    private OneClientRelay _relay = null!;
    private PluginHarness _harness = null!;
    private ModbusDevice _device = null!;
    private SolaxCharger _solax = null!;

    public async Task InitializeAsync()
    {
        _relay = new OneClientRelay(_charger.Port);
        _harness = new PluginHarness(Plugin, new Dictionary<string, string?> { ["Solax:Host"] = _relay.Address.ToString() });
        _device = _harness.Modbus();
        _solax = new SolaxCharger(_device, _harness.Settings, TimeProvider.System, NullLogger<SolaxCharger>.Instance);

        // An X3-HAC, charging at 10 A in Fast, with the plugin reading it.
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

        await _solax.ReadAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _relay.DisposeAsync();
        await _charger.DisposeAsync();
    }

    [Fact]
    public async Task A_second_client_is_dropped_as_by_the_charger()
    {
        await using var second = _device.ClientFor(_relay.Address, 1);

        await Assert.ThrowsAnyAsync<Exception>(() => second.ReadInputRegistersAsync(SolaxRegisters.RunMode, 1, CancellationToken.None));
        Assert.Equal(1, _relay.Dropped);
    }

    [Fact]
    public async Task Save_and_check_of_the_address_in_use_reads_over_the_plugins_own_connection()
    {
        Assert.StartsWith("X3-HAC (503BAB123456)", await _solax.CheckAsync(_relay.Address, 1, CancellationToken.None));
        Assert.Equal(0, _relay.Dropped);
    }

    [Fact]
    public async Task The_search_finds_the_charger_the_plugin_is_reading()
    {
        var found = await new SolaxSearch(_device).FindAsync([IPAddress.Loopback], CancellationToken.None);

        var charger = Assert.Single(found);
        Assert.Equal(_relay.Address, charger.Address);
        Assert.Equal("X3-HAC (503BAB123456)", charger.Description);
        Assert.Equal(0, _relay.Dropped);
    }
}
