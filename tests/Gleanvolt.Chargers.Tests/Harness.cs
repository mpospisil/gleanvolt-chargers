using Gleanvolt.Providers.Client;
using Gleanvolt.Providers.Client.Charger;
using Gleanvolt.Providers.Contracts.Charger;
using Gleanvolt.Providers.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gleanvolt.Chargers.Tests;

/// <summary>
/// One plugin as its host wires it, without the page: its settings in a data directory of its own, the recorder, the
/// session, and, once connected, its charger behind the ordinary SDK on <see cref="ChargerTestHub"/>.
/// </summary>
internal sealed class Harness : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _owned = [];

    public Harness(PluginInfo plugin, IDictionary<string, string?>? configuration = null)
    {
        Plugin = plugin;
        Directory = Path.Combine(Path.GetTempPath(), "gleanvolt-chargers-" + Guid.NewGuid().ToString("N"));
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build();
        Files = new ProviderSecretStore(Directory);
        Settings = new PluginSettings(Files, Configuration, plugin);
    }

    public PluginInfo Plugin { get; }

    public string Directory { get; }

    public IConfiguration Configuration { get; }

    public ProviderSecretStore Files { get; }

    public PluginSettings Settings { get; }

    public DeviceRecorder Recorder { get; } = new(TimeProvider.System);

    public GleanvoltSession Session { get; } = new();

    public ChargerTestHub? Hub { get; private set; }

    public ChargerProviderConnection? Connection { get; private set; }

    /// <summary>A Modbus device at the settings' address, as the Modbus plugins register it.</summary>
    public ModbusDevice Modbus(TimeSpan? interval = null)
    {
        var device = new ModbusDevice(
            Settings.Address, Settings.Get("unitId"), 502, 1, interval ?? TimeSpan.Zero, Recorder, NullLogger<ModbusDevice>.Instance);
        _owned.Add(device);
        return device;
    }

    /// <summary>Connects <paramref name="charger"/> to a test hub whose charge control asks <paramref name="limits"/>, and waits for it to be attached.</summary>
    public async Task<ChargerTestHub> ConnectAsync(PluginCharger charger, ChargerLimits limits, bool attaches = true)
    {
        Hub = await ChargerTestHub.StartAsync(new ChargerTestHubOptions { Limits = limits, Timeout = TimeSpan.FromSeconds(5) });
        var options = new ProviderOptions();
        Hub.Configure(options);
        options.ProviderId = Plugin.ProviderId;
        options.Name = Plugin.Name;
        options.InitialRetryDelay = TimeSpan.FromMilliseconds(100);
        options.RefusedRetryDelay = TimeSpan.FromMilliseconds(100);

        Connection = new ChargerProviderConnection(Options.Create(options), charger, NullLogger<ChargerProviderConnection>.Instance);
        _ = new StatusReporter(Connection, charger, Session, NullLogger<StatusReporter>.Instance);
        await Connection.StartAsync(CancellationToken.None);

        if (attaches)
        {
            await Hub.WaitForHelloAsync();
            await Eventually(() => Connection.IsAttached, "the plugin should have its welcome");
        }

        return Hub;
    }

    public static async Task Eventually(Func<bool> condition, string because)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), because);
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
        {
            await Connection.StopAsync(CancellationToken.None);
            Connection.Dispose();
        }

        if (Hub is not null)
        {
            await Hub.DisposeAsync();
        }

        foreach (var owned in _owned)
        {
            await owned.DisposeAsync();
        }

        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

/// <summary>An HTTP client factory whose every client goes through <paramref name="handler"/>, recorded as the plugins' are.</summary>
internal sealed class FakeClients(HttpMessageHandler handler, DeviceRecorder recorder) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(new RecordingHandler(recorder) { InnerHandler = handler }, disposeHandler: false);
}
