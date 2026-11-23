using Gleanvolt.Chargers;
using Gleanvolt.Plugin.Solax;

// SolaX's EV chargers (X1/X3-HAC, X1/X3-EVC) as a Gleanvolt charger plugin. See README.md.

var plugin = new PluginInfo("solax-charger", "SolaX EV charger", "Solax", Port: 8098, DefaultDevicePort: 502);
var builder = PluginHost.CreateBuilder<SolaxCharger>(args, plugin);

// SolaX's protocol asks for a pause between instructions: 250 ms, as Gleanvolt's built-in driver used.
builder.Services.AddSingleton(provider =>
{
    var settings = provider.GetRequiredService<PluginSettings>();
    return new ModbusDevice(
        settings.Address,
        settings.Get("unitId"),
        defaultPort: 502,
        defaultUnitId: 1,
        TimeSpan.FromMilliseconds(250),
        provider.GetRequiredService<DeviceRecorder>(),
        provider.GetRequiredService<ILogger<ModbusDevice>>());
});
builder.Services.AddSingleton<IChargerSearch, SolaxSearch>();

PluginHost.Build(builder).Run();

