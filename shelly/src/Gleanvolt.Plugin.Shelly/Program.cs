using Gleanvolt.Chargers;
using Gleanvolt.Plugin.Shelly;

// The Shelly TopAC Portable EV Charger as a Gleanvolt charger plugin, over its local Gen2 RPC API. See README.md.

var plugin = new PluginInfo("shelly-topac", "Shelly TopAC", "Shelly", Port: 8097, DefaultDevicePort: 80);
var builder = PluginHost.CreateBuilder<ShellyCharger>(args, plugin);

builder.Services.AddSingleton(builder.Configuration.GetSection(ShellyOptions.SectionName).Get<ShellyOptions>() ?? new ShellyOptions());
builder.Services.AddSingleton<ShellyCredentials>();
builder.Services.AddSingleton<ShellyRpcClient>();

// Recorded while a recording runs, and with the page's password for Shelly's digest when the charger asks for one.
builder.Services.AddHttpClient(ShellyRpcClient.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
    {
        Credentials = provider.GetRequiredService<ShellyCredentials>(),
        PreAuthenticate = false,
    })
    .AddHttpMessageHandler<RecordingHandler>();

PluginHost.Build(builder).Run();

