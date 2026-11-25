using Gleanvolt.Chargers;
using Gleanvolt.Plugin.Huawei;

// Huawei's SCharger-7KS-S0 and SCharger-22KT-S0 as a Gleanvolt charger plugin. See README.md.

var plugin = new PluginInfo("huawei-scharger", "Huawei SCharger", "Huawei", Port: 8099, DefaultDevicePort: 502);
var builder = PluginHost.CreateBuilder<HuaweiCharger>(args, plugin);

// A second apart, and five seconds before a reconnect: Huawei's Modbus servers drop a client that asks faster, and
// refuse one that comes back at once, as Gleanvolt's built-in driver learnt.
builder.Services.AddSingleton(provider =>
{
    var settings = provider.GetRequiredService<PluginSettings>();
    return new ModbusDevice(
        settings.Address,
        settings.Get("unitId"),
        defaultPort: 502,
        defaultUnitId: 1,
        TimeSpan.FromSeconds(1),
        provider.GetRequiredService<DeviceRecorder>(),
        provider.GetRequiredService<ILogger<ModbusDevice>>(),
        reconnectBackoff: TimeSpan.FromSeconds(5));
});
builder.Services.AddSingleton<HuaweiSearch>();

PluginHost.Build(builder).Run();

