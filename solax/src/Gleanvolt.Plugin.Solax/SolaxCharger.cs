using System.Net;
using System.Globalization;
using Gleanvolt.Chargers;
using Gleanvolt.Modbus;
using Gleanvolt.Providers.Contracts;
using Gleanvolt.Providers.Contracts.Charger;

namespace Gleanvolt.Plugin.Solax;

/// <summary>
/// A SolaX EV charger (X1/X3-HAC, X1/X3-EVC) behind the plugin: the driver built into Gleanvolt up to 1.3, ported.
///
/// <para><b>What it keeps from the driver.</b> A read is the run mode and the power (input registers) and,
/// best-effort, the use-mode and the setpoint (holding registers): a holding register that fails does not fail the
/// read. Start and stop write the use-mode, Fast or Stop; the setpoint writes hundredths of an amp; a pause is a
/// setpoint of 0 A, which the charger takes.</para>
///
/// <para><b>The flaky link.</b> The charger's Modbus link drops tens of times a day, and for a while after it comes
/// back the use-mode register can read values that are no use-mode at all. This plugin never decides anything from
/// the use-mode: it reports "allowed" only when it reads Fast, and Gleanvolt, which watched the built-in driver read
/// the same register, judges the owner's intent over time on its own side. A read that fails throws, so Gleanvolt
/// reads Unknown and decides nothing, as it always did.</para>
/// </summary>
public sealed class SolaxCharger : PluginCharger
{
    private readonly ModbusDevice _device;
    private readonly SavedSetting _phases;
    private readonly ILogger<SolaxCharger> _logger;
    private readonly TimeProvider _time;

    public SolaxCharger(ModbusDevice device, PluginSettings settings, TimeProvider time, ILogger<SolaxCharger> logger)
    {
        _device = device;
        _phases = settings.Get("phases");
        _time = time;
        _logger = logger;
    }

    /// <summary>The use-mode in the newest reading, as the charger said it: a name, or the raw number for one it isn't.</summary>
    public string? LastUseMode { get; private set; }

    /// <summary>The serial number, read once after an address is set; null until then, or when it could not be read.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>
    /// 6-32 A per SolaX's protocol, paused at 0 A, on the phases the page says: three unless the owner chose one, for an
    /// X1. Nothing switches phases.
    /// </summary>
    public override ChargerCapabilities Capabilities => new(6, 32, Phases);

    /// <summary>The phases: <c>Solax:Phases</c>, or the page's, 1 or 3; 3 when neither says.</summary>
    public int Phases => _phases.Current == "1" ? 1 : 3;

    /// <summary>The run mode as the contract's state. Gleanvolt reads only connected or not, and winding down, so each maps to one that reads the same.</summary>
    public static ChargerState StateOf(ushort runMode) => runMode switch
    {
        0 => ChargerState.Available,
        1 => ChargerState.Preparing,
        2 => ChargerState.Charging,
        3 => ChargerState.Finishing,
        4 => ChargerState.Faulted,
        5 => ChargerState.Unavailable,
        6 => ChargerState.Available,       // Reserved: no car
        7 => ChargerState.SuspendedEv,
        8 => ChargerState.SuspendedEvse,
        9 => ChargerState.Unavailable,     // Update
        10 => ChargerState.Available,      // CardActivation: no car yet
        11 => ChargerState.Available,      // StartDelay: as the driver read it, no car connected
        12 => ChargerState.SuspendedEvse,  // ChargePaused: the charger's pause, Gleanvolt's included
        13 => ChargerState.Available,      // Stopping: as the driver read it, no car connected
        _ => ChargerState.Unknown,
    };

    /// <summary>The use-mode <paramref name="raw"/> names, or null for a value outside the known ones.</summary>
    public static SolaxUseMode? UseModeOf(ushort raw) => raw <= (ushort)SolaxUseMode.Green ? (SolaxUseMode)raw : null;

    /// <summary>The serial number in <paramref name="registers"/>, or null when they hold none.</summary>
    public static string? SerialOf(IReadOnlyList<ushort> registers)
    {
        var text = new string([.. registers.SelectMany(register => new[] { (char)(register >> 8), (char)(register & 0xFF) })]).TrimEnd('\0', ' ');
        return text.Length >= 5 && text.All(character => character is (>= 'A' and <= 'Z') or (>= '0' and <= '9')) ? text : null;
    }

    /// <summary>The model a SolaX charger's serial number names, or null for one this plugin does not know.</summary>
    public static string? ModelOf(string? serial) => serial switch
    {
        null => null,
        _ when serial.StartsWith("C107", StringComparison.Ordinal) => "X1-EVC 7 kW",
        _ when serial.StartsWith("C311", StringComparison.Ordinal) => "X3-EVC 11 kW",
        _ when serial.StartsWith("C322", StringComparison.Ordinal) => "X3-EVC 22 kW",
        _ when serial.Length >= 5 && serial[0] == '5' => serial.Substring(1, 2) switch
        {
            "02" => "X1-HAC",
            "03" => "X3-HAC",
            "04" => "A1-HAC",
            "05" => "J1-HAC",
            "06" => "X1-HAC-S",
            "07" => "X3-HAC-S",
            "08" => "C1-HAC",
            "09" => "C3-HAC",
            _ => null,
        },
        _ => null,
    };

    public override async Task<ChargerReading> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reading = await _device.UseAsync(async client =>
            {
                var runMode = await ReadInputAsync(client, SolaxRegisters.RunMode, cancellationToken).ConfigureAwait(false);
                var power = await ReadInputAsync(client, SolaxRegisters.ChargePowerTotal, cancellationToken).ConfigureAwait(false);
                var useMode = await TryReadHoldingAsync(client, SolaxRegisters.ChargerUseMode, cancellationToken).ConfigureAwait(false);
                var setpoint = await TryReadHoldingAsync(client, SolaxRegisters.ChargeCurrentSetpoint, cancellationToken).ConfigureAwait(false);

                if (SerialNumber is null)
                {
                    SerialNumber = SerialOf(await TryReadHoldingAsync(client, SolaxRegisters.SerialNumberStart, SolaxRegisters.SerialNumberCount, cancellationToken).ConfigureAwait(false) ?? []);
                }

                var mode = useMode is { } raw ? UseModeOf(raw) : null;
                LastUseMode = useMode is { } value ? mode?.ToString() ?? value.ToString(CultureInfo.InvariantCulture) : null;

                return new ChargerReading(
                    _time.GetUtcNow(),
                    StateOf(runMode),
                    power,
                    CurrentAmps: setpoint is { } amps ? (int)Math.Round(amps * SolaxRegisters.AmpsPerCount) : null,
                    // Only Fast lets the charger charge. A junk value after the link recovered is not Fast either, and
                    // what it means is Gleanvolt's to judge over time, not this one read's.
                    ChargingAllowed: useMode is null ? null : mode == SolaxUseMode.Fast,
                    Mode: mode?.ToString());
            }, cancellationToken).ConfigureAwait(false);

            LastReading = reading;
            Healthy();
            return reading;
        }
        catch (NoAddressException)
        {
            Problem(SharedText.NoAddress, ProviderStatusCodes.NotSetUp, needsOwner: true);
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
        // Below 6 A is a pause, and the charger takes 0 A for one; never above its 32 A.
        var clamped = amps < Capabilities.MinCurrentAmps ? 0 : Math.Min(amps, Capabilities.MaxCurrentAmps);
        var value = (ushort)Math.Round(clamped / SolaxRegisters.AmpsPerCount);

        await _device.UseAsync(async client =>
        {
            await client.WriteSingleRegisterAsync(SolaxRegisters.ChargeCurrentSetpoint, value, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        RaiseChanged();
    }

    public override async Task SetChargingAllowedAsync(bool allowed, CancellationToken cancellationToken)
    {
        var mode = allowed ? SolaxUseMode.Fast : SolaxUseMode.Stop;

        await _device.UseAsync(async client =>
        {
            await client.WriteSingleRegisterAsync(SolaxRegisters.ChargerUseMode, (ushort)mode, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        RaiseChanged();
    }

    public override void Reset()
    {
        SerialNumber = null;
        LastUseMode = null;
        base.Reset();
    }

    /// <summary>
    /// What the charger at <paramref name="address"/> is, read once: its model from the serial number, and its run
    /// mode. Throws when it does not answer as a SolaX charger: the inverter's register at the run mode's address is a
    /// number far outside 0-13.
    /// </summary>
    public async Task<string> CheckAsync(DeviceAddress address, byte unitId, CancellationToken cancellationToken)
    {
        await using var client = _device.ClientFor(address, unitId);
        var runMode = await ReadInputAsync(client, SolaxRegisters.RunMode, cancellationToken).ConfigureAwait(false);

        if (runMode > SolaxRegisters.HighestRunMode)
        {
            throw new InvalidOperationException(
                $"it answers, but its run mode register reads {runMode}: is this the inverter's address?");
        }

        var serial = SerialOf(await TryReadHoldingAsync(client, SolaxRegisters.SerialNumberStart, SolaxRegisters.SerialNumberCount, cancellationToken).ConfigureAwait(false) ?? []);
        return $"{ModelOf(serial) ?? "SolaX charger"}{(serial is null ? "" : $" ({serial})")}, {StateOf(runMode)}";
    }

    private static async Task<ushort> ReadInputAsync(IModbusClient client, ushort address, CancellationToken cancellationToken) =>
        (await client.ReadInputRegistersAsync(address, 1, cancellationToken).ConfigureAwait(false))[0];

    private static async Task<ushort?> TryReadHoldingAsync(IModbusClient client, ushort address, CancellationToken cancellationToken) =>
        (await TryReadHoldingAsync(client, address, 1, cancellationToken).ConfigureAwait(false))?[0];

    // Best-effort, for telemetry: these registers are not on every charger and firmware, and a failed read must not
    // fail the whole reading.
    private static async Task<ushort[]?> TryReadHoldingAsync(IModbusClient client, ushort address, ushort count, CancellationToken cancellationToken)
    {
        try
        {
            return await client.ReadHoldingRegistersAsync(address, count, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>The search: a device on port 502 whose run mode is 0-13 and whose serial names a SolaX charger, or that answers like one.</summary>
/// <summary>
/// How the plugin finds its charger (#461): every host that accepts a connection on the charger's Modbus port, 502,
/// asked whether it is this charger. Reads only.
/// </summary>
public sealed class SolaxSearch(ModbusDevice device)
{
    public const int Port = 502;

    public Task<IReadOnlyList<FoundCharger>> FindAsync(IReadOnlyList<IPAddress> hosts, CancellationToken cancellationToken) =>
        LanScan.SearchAsync(hosts, Port, ProbeAsync, cancellationToken);

    public async Task<string?> ProbeAsync(DeviceAddress address, CancellationToken cancellationToken)
    {
        await using var client = device.ClientFor(address, 1);
        var runMode = (await client.ReadInputRegistersAsync(SolaxRegisters.RunMode, 1, cancellationToken).ConfigureAwait(false))[0];
        if (runMode > SolaxRegisters.HighestRunMode)
        {
            return null;
        }

        try
        {
            var serial = SolaxCharger.SerialOf(await client.ReadHoldingRegistersAsync(SolaxRegisters.SerialNumberStart, SolaxRegisters.SerialNumberCount, cancellationToken).ConfigureAwait(false));
            return SolaxCharger.ModelOf(serial) is { } model ? $"{model} ({serial})" : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
