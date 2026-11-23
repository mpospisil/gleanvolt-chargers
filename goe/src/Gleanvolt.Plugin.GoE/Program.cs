using Gleanvolt.Chargers;
using Gleanvolt.Plugin.GoE;

// The go-e Charger as a Gleanvolt charger plugin, over its local HTTP API v2. See README.md.

var plugin = new PluginInfo("goe-charger", "go-e Charger", "Goe", Port: 8100, DefaultDevicePort: 80);
var builder = PluginHost.CreateBuilder<GoeCharger>(args, plugin);

// Every exchange recorded while a recording runs; the charger answers in well under a second.
builder.Services.AddHttpClient(GoeCharger.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(4))
    .AddHttpMessageHandler<RecordingHandler>();
builder.Services.AddHttpClient("search");
builder.Services.AddSingleton<IChargerSearch, GoeSearch>();

PluginHost.Build(builder).Run();

