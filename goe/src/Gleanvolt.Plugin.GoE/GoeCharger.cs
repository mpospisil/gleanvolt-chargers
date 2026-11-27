using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gleanvolt.Chargers;
using Gleanvolt.Plugin.GoE.Resources;
using Gleanvolt.Providers.Contracts;
using Gleanvolt.Providers.Contracts.Charger;

namespace Gleanvolt.Plugin.GoE;

/// <summary>go-e's force state, <c>frc</c>.</summary>
public enum GoeForceState
{
    /// <summary>The charger's own logic decides (its schedule, its PV surplus, its access control).</summary>
    Neutral = 0,

    /// <summary>Charging is off.</summary>
    Off = 1,

    /// <summary>Charging is on.</summary>
    On = 2,
}

/// <summary>
/// The go-e Charger over its local HTTP API v2, behind the plugin: the driver built into Gleanvolt up to 1.3, ported.
/// The owner switches the API on in the go-e app first (Internet → Advanced settings → "Local HTTP API v2"); it is
/// off by default, and older units speak only API v1, which this does not.
///
/// <para><b>Reads.</b> One filtered <c>/api/status</c>. <c>car</c> is the status: 1 idle, 2 charging, 3 waiting for the
/// car, 4 finished with the car still connected. Whether 3 and 4 are the car's doing or the charger's is <c>alw</c>.
/// Power is <c>nrg[11]</c>, in watts. The unit's own ceiling is <c>ama</c>, and 16 A on an 11 kW unit.</para>
///
/// <para><b>Writes: two keys.</b> Start and stop write the force state <c>frc</c> (2 on, 1 off); the setpoint writes
/// <c>amp</c>. Both are kept across a restart of the charger, so a write happens only on a change, as Gleanvolt
/// already asks. Phase switching (<c>psm</c>) is read for the recording and never written.</para>
///
/// <para><b>Pause.</b> The charger refuses a setpoint below 6 A, so a pause is the force state Off, and the plugin
/// remembers it paused: readings then say "allowed, 0 A", what Gleanvolt set, and the next setpoint of 6 A or more
/// writes <c>amp</c> and turns the force state back on in one request. A stop while paused stays stopped.</para>
/// </summary>
public sealed class GoeCharger : PluginCharger
{
    /// <summary>The keys every read asks for. <c>psm</c>, <c>pnp</c>, <c>fwv</c>, <c>typ</c> and <c>var</c> are for the recording and the page.</summary>
    public static IReadOnlyList<string> StatusKeys { get; } =
        ["car", "alw", "frc", "amp", "ama", "var", "nrg", "err", "modelStatus", "psm", "pnp", "fwv", "typ"];

    /// <summary>The one request a read makes, relative to the charger.</summary>
    public static string StatusRequest { get; } = "api/status?filter=" + string.Join(',', StatusKeys);

    // nrg: U L1, L2, L3, N; I L1, L2, L3; P L1, L2, L3, N, total; pf L1, L2, L3, N. Power in W.
    private const int TotalPowerIndex = 11;

    /// <summary>The named HTTP client the charger is read and written with: recorded, 4 s a request.</summary>
    public const string HttpClientName = "goe";

    private readonly IHttpClientFactory _clients;
    private readonly PluginSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<GoeCharger> _logger;

    // What Gleanvolt last asked for, which the charger cannot hold on its own: whether it may charge (null until the
    // first start or stop), and whether the setpoint is a pause.
    private bool? _allowed;
    private bool _paused;
    private int? _deviceMaxAmps;

    public GoeCharger(IHttpClientFactory clients, PluginSettings settings, TimeProvider time, ILogger<GoeCharger> logger)
    {
        _clients = clients;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    /// <summary>6-32 A on three phases; the unit switches to one phase itself, and says it can.</summary>
    public override ChargerCapabilities Capabilities => new(6, 32, 3, CanSwitchPhases: true);

    /// <summary>The firmware version from the newest reading.</summary>
    public string? Firmware { get; private set; }

    /// <summary>The unit's own ceiling from the newest reading.</summary>
    public int? DeviceMaxAmps => _deviceMaxAmps;

    /// <summary>Whether the plugin holds a pause.</summary>
    public bool Paused => _paused;

    /// <summary>
    /// <c>car</c> and <c>alw</c> as the contract's state. Waiting and finished are the car's doing only while the charger
    /// allows charging; otherwise the charger is holding it, which is never "the car is full".
    /// </summary>
    public static ChargerState StateOf(int? car, bool allowed) => car switch
    {
        1 => ChargerState.Available,
        2 => ChargerState.Charging,
        3 => allowed ? ChargerState.Preparing : ChargerState.SuspendedEvse,
        4 => allowed ? ChargerState.SuspendedEv : ChargerState.SuspendedEvse,
        5 => ChargerState.Faulted,
        _ => ChargerState.Unknown,   // 0 and null: the charger's own internal error, not a fact about the car
    };

    private Uri? Base => DeviceAddress.Parse(_settings.Address.Current, 80)?.ToHttpBase();

    public override async Task<ChargerReading> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await ReadStatusAsync(Base ?? throw new NoAddressException(), cancellationToken).ConfigureAwait(false);
            if (!status.ContainsKey("car"))
            {
                throw new InvalidOperationException("The go-e status has no 'car': is this a go-e with the HTTP API v2?");
            }

            var power = status["nrg"] is JsonArray energy && energy.Count > TotalPowerIndex && Number(energy[TotalPowerIndex]) is { } watts
                ? watts
                : throw new InvalidOperationException("The go-e status has no total power (nrg[11]).");
            var force = Integer(status["frc"]) is { } frc ? (GoeForceState)frc : (GoeForceState?)null;
            Firmware = status["fwv"]?.ToString();

            var reading = new ChargerReading(
                _time.GetUtcNow(),
                StateOf(Integer(status["car"]), Boolean(status["alw"]) ?? false),
                Math.Max(0, power),
                CurrentAmps: _paused ? 0 : Integer(status["amp"]),
                // Off is "allowed" only when it is the plugin's own pause under a start: what Gleanvolt set was 0 A.
                ChargingAllowed: force is null ? null : force == GoeForceState.On || (force == GoeForceState.Off && _paused && _allowed == true),
                MaxCurrentAmps: _deviceMaxAmps,
                Mode: force?.ToString());

            LastReading = reading;
            Healthy();
            return reading;
        }
        catch (NoAddressException)
        {
            Problem(SharedText.NoAddress, ProviderStatusCodes.NotSetUp, needsOwner: true);
            throw;
        }
        catch (ApiOffException ex)
        {
            Problem(Words.From(PageText.ResourceManager, nameof(PageText.ApiOff)), ProviderStatusCodes.NotSetUp, needsOwner: true);
            _logger.LogWarning("{Reason}", ex.Message);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Reading the charger failed: {Reason}", ex.Message);
            Problem(SharedText.Unreachable.Format(ex.Message), ProviderStatusCodes.Unreachable, needsOwner: false);
            throw;
        }
    }

    public override async Task SetCurrentAsync(int amps, CancellationToken cancellationToken)
    {
        try
        {
            var clamped = Math.Clamp(amps, 0, _deviceMaxAmps ?? Capabilities.MaxCurrentAmps);

            if (clamped < Capabilities.MinCurrentAmps)
            {
                // A pause: the force state, since the charger takes nothing below 6 A. Under a stop it is already off.
                var wasPaused = _paused;
                _paused = true;
                if (!wasPaused && _allowed != false)
                {
                    await SetAsync([("frc", (int)GoeForceState.Off)], cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            // Out of a pause the setpoint goes first, in the same request as the force state back on, so the car does
            // not start at whatever held before the pause.
            var resume = _paused && _allowed == true;
            _paused = false;
            await SetAsync(
                resume ? [("amp", clamped), ("frc", (int)GoeForceState.On)] : [("amp", clamped)],
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RaiseChanged();
        }
    }

    public override async Task SetChargingAllowedAsync(bool allowed, CancellationToken cancellationToken)
    {
        _allowed = allowed;

        try
        {
            // A start under a pause leaves the charger off: the setpoint is 0 A until Gleanvolt raises it.
            await SetAsync([("frc", (int)(allowed && !_paused ? GoeForceState.On : GoeForceState.Off))], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RaiseChanged();
        }
    }

    public override void Reset()
    {
        _deviceMaxAmps = null;
        Firmware = null;
        base.Reset();
    }

    /// <summary>What the charger at <paramref name="address"/> is, read once: a go-e answers its status with a car state and a firmware version.</summary>
    public async Task<string> CheckAsync(DeviceAddress address, CancellationToken cancellationToken)
    {
        var status = await ReadStatusAsync(address.ToHttpBase(), cancellationToken).ConfigureAwait(false);
        return Describe(status) ?? throw new InvalidOperationException("it answers, but not as a go-e with the HTTP API v2.");
    }

    /// <summary>What a status says the charger is, or null when it is not a go-e's.</summary>
    public static string? Describe(JsonObject status) =>
        status.ContainsKey("car") && status["fwv"] is JsonValue firmware && firmware.TryGetValue<string>(out var version) && version.Length > 0
            ? $"go-e {status["typ"]?.ToString() ?? "Charger"} {status["var"]?.ToString() ?? "?"} kW, firmware {version}"
            : null;

    /// <summary>The status at <paramref name="at"/>, remembering the unit's ceiling for the next setpoint.</summary>
    internal async Task<JsonObject> ReadStatusAsync(Uri at, CancellationToken cancellationToken)
    {
        using var http = _clients.CreateClient(HttpClientName);
        using var response = await http.GetAsync(new Uri(at, StatusRequest), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ApiOffException();
        }

        response.EnsureSuccessStatusCode();

        JsonObject status;
        try
        {
            status = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject
                ?? throw new InvalidOperationException("The go-e status is not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The go-e status is not JSON.", ex);
        }

        var ceiling = Integer(status["ama"]) ?? Capabilities.MaxCurrentAmps;
        if (Integer(status["var"]) == 11)
        {
            ceiling = Math.Min(ceiling, 16);
        }

        _deviceMaxAmps = Math.Clamp(ceiling, Capabilities.MinCurrentAmps, Capabilities.MaxCurrentAmps);
        return status;
    }

    // One /api/set of the given keys. Its answer names each key with true, or with why it was refused.
    private async Task SetAsync(IReadOnlyList<(string Key, int Value)> values, CancellationToken cancellationToken)
    {
        var at = Base ?? throw new NoAddressException();
        var query = string.Join('&', values.Select(value => $"{value.Key}={value.Value.ToString(CultureInfo.InvariantCulture)}"));
        _logger.LogInformation("Setting {Query}.", query);

        using var http = _clients.CreateClient(HttpClientName);
        using var response = await http.GetAsync(new Uri(at, "api/set?" + query), cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The go-e refused {query}: HTTP {(int)response.StatusCode} {body}".TrimEnd());
        }

        JsonObject? answer;
        try
        {
            answer = JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            answer = null;
        }

        foreach (var (key, _) in values)
        {
            if (answer?[key] is not JsonValue accepted || !accepted.TryGetValue<bool>(out var ok) || !ok)
            {
                throw new InvalidOperationException($"The go-e did not accept {key}: {answer?[key]?.ToJsonString() ?? "no answer"}.");
            }
        }
    }

    private static int? Integer(JsonNode? node) => Number(node) is { } value ? (int)Math.Round(value) : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;

    private static bool? Boolean(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
}

/// <summary>The go-e answered 404 for its status: the local HTTP API v2 is off.</summary>
public sealed class ApiOffException() : InvalidOperationException("The go-e answered 404 for /api/status: switch on the local HTTP API v2 in the go-e app.");

/// <summary>
/// How the plugin finds its charger (#461): every host that accepts a connection on the charger's HTTP API port, 80,
/// whose <c>/api/status</c> is a go-e's. Reads only.
/// </summary>
public sealed class GoeSearch(IHttpClientFactory clients)
{
    public const int Port = 80;

    public Task<IReadOnlyList<FoundCharger>> FindAsync(IReadOnlyList<IPAddress> hosts, CancellationToken cancellationToken) =>
        LanScan.SearchAsync(hosts, Port, ProbeAsync, cancellationToken);

    public async Task<string?> ProbeAsync(DeviceAddress address, CancellationToken cancellationToken)
    {
        using var http = clients.CreateClient("search");
        http.Timeout = TimeSpan.FromSeconds(2);
        using var response = await http.GetAsync(new Uri(address.ToHttpBase(), GoeCharger.StatusRequest), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) is JsonObject status
                ? GoeCharger.Describe(status)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
