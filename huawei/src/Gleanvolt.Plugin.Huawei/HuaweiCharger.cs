using System.Globalization;
using System.Net;
using Gleanvolt.Chargers;
using Gleanvolt.Modbus;
using Gleanvolt.Plugin.Huawei.Resources;
using Gleanvolt.Providers.Contracts;
using Gleanvolt.Providers.Contracts.Charger;

namespace Gleanvolt.Plugin.Huawei;

/// <summary>The SCharger's registers, from Huawei's SCharger Modbus interface definition. Values are 32-bit, high word first.</summary>
public static class SChargerRegisters
{
    /// <summary>Phase L1's voltage, tenths of a volt; L2 and L3 follow, two registers each.</summary>
    public const ushort PhaseL1Voltage = 0x1000;

    /// <summary>Phase L2's voltage: mains on a 22KT, nothing on a 7KS.</summary>
    public const ushort PhaseL2Voltage = 0x1002;

    /// <summary>Phase L1's current, tenths of an amp; L2 and L3 follow.</summary>
    public const ushort PhaseL1Current = 0x1006;

    /// <summary>The total output power, tenths of a kilowatt.</summary>
    public const ushort TotalOutputPower = 0x100C;

    /// <summary>The measurement block: the three voltages, the three currents and the power.</summary>
    public const ushort MeasurementsCount = 14;

    /// <summary>The maximum charge power, tenths of a kilowatt, 0-22 kW: the setpoint.</summary>
    public const ushort MaxChargePower = 0x2000;

    /// <summary>The charging control: <see cref="SChargerControl"/>.</summary>
    public const ushort ChargingControl = 0x2006;
}

/// <summary>The charging control register.</summary>
public enum SChargerControl : ushort
{
    /// <summary>What it reads before anyone wrote it.</summary>
    Standby = 0,

    /// <summary>Charging is paused: a stop.</summary>
    Paused = 1,

    /// <summary>Charging may go on: a start.</summary>
    Resumed = 2,
}

/// <summary>
/// Huawei's SCharger-7KS-S0 (one phase) and SCharger-22KT-S0 (three phases) behind the plugin: the driver built into
/// Gleanvolt up to 1.3, ported. The owner switches Modbus TCP on in FusionSolar first; it is off by default.
///
/// <para><b>No status.</b> The protocol reports power, voltages and currents and nothing else. Power or a current
/// above zero reads Charging; anything else Unknown, because no car and a car waiting both read 0 W, and Gleanvolt
/// never ends a session on Unknown.</para>
///
/// <para><b>The setpoint is power.</b> Gleanvolt speaks amps and the charger takes a maximum power in tenths of a
/// kilowatt. The plugin converts at the voltage and on the phases Gleanvolt's charge control says in its welcome (never
/// more phases than the model has): 16 A on three phases is 11.0 kW, on one 3.7 kW. A pause is 0 kW.</para>
///
/// <para><b>Expected, not verified.</b> Written from the protocol; owners report port 502 refused on 2023/2024
/// firmware, and nobody has confirmed the writes yet. Behind an EMMA, the EMMA manages the charger: leave Gleanvolt in
/// dry-run until a tester's recording shows a clean hand-over.</para>
/// </summary>
public sealed class HuaweiCharger : PluginCharger
{
    // 180-265 V in tenths: a European mains phase with margin.
    private const uint LowestMains = 1800;
    private const uint HighestMains = 2650;

    // Tenths of an amp: a phase drawing 1 A is delivering, even when the power's 100 W step still reads 0.
    private const uint DeliveringCurrent = 10;

    // The protocol's range for the maximum charge power: 0-22 kW.
    private const uint MaxPowerTenthsKw = 220;

    private readonly ModbusDevice _device;
    private readonly SavedSetting _model;
    private readonly GleanvoltSession _session;
    private readonly TimeProvider _time;
    private readonly ILogger<HuaweiCharger> _logger;

    public HuaweiCharger(ModbusDevice device, PluginSettings settings, GleanvoltSession session, TimeProvider time, ILogger<HuaweiCharger> logger)
    {
        _device = device;
        _model = settings.Get("model");
        _session = session;
        _time = time;
        _logger = logger;
    }

    /// <summary>The model: <c>22kt</c> or <c>7ks</c>, from <c>Huawei:Model</c> or the page; the 7KS until one says, the safer of the two.</summary>
    public string Model => _model.Current?.ToLowerInvariant() == "22kt" ? "22kt" : "7ks";

    /// <summary>Whether a model was set, by the configuration, the page, or the check that found L2 live.</summary>
    public bool ModelKnown => _model.Current is not null;

    /// <summary>The newest phase voltages, in volts.</summary>
    public IReadOnlyList<double>? LastVoltages { get; private set; }

    /// <summary>The 7KS: one phase, 6-32 A. The 22KT: three. Neither switches phases.</summary>
    public override ChargerCapabilities Capabilities => new(6, 32, Model == "22kt" ? 3 : 1);

    /// <summary>The phases the setpoint converts at: the charge control's, never more than the model has.</summary>
    public int SetpointPhases => Math.Clamp(_session.Limits?.Phases ?? Capabilities.Phases, 1, Capabilities.Phases);

    private double Volts => _session.Limits?.NominalVoltage is > 0 and var volts ? volts : 230;

    /// <summary>The power, in the register's tenths of a kilowatt, for <paramref name="amps"/> on <paramref name="phases"/> at <paramref name="volts"/>; never above 22 kW.</summary>
    public static uint PowerForAmps(int amps, int phases, double volts) =>
        Math.Min(MaxPowerTenthsKw, (uint)Math.Round(Math.Clamp(amps, 0, 32) * phases * volts / 100));

    /// <summary>The setpoint in whole amps that <paramref name="tenthsOfKilowatts"/> is.</summary>
    public static int AmpsForPower(uint tenthsOfKilowatts, int phases, double volts) =>
        (int)Math.Clamp(Math.Round(tenthsOfKilowatts * 100 / (phases * volts)), 0, 32);

    /// <summary>Whether <paramref name="tenthsOfVolts"/> is a mains voltage.</summary>
    public static bool IsMains(uint tenthsOfVolts) => tenthsOfVolts is >= LowestMains and <= HighestMains;

    public override async Task<ChargerReading> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reading = await _device.UseAsync(async client =>
            {
                var block = await client.ReadHoldingRegistersAsync(SChargerRegisters.PhaseL1Voltage, SChargerRegisters.MeasurementsCount, cancellationToken)
                    .ConfigureAwait(false);
                var power = Word32(block, SChargerRegisters.TotalOutputPower - SChargerRegisters.PhaseL1Voltage);
                var highestCurrent = Enumerable.Range(0, 3)
                    .Max(phase => Word32(block, SChargerRegisters.PhaseL1Current - SChargerRegisters.PhaseL1Voltage + (phase * 2)));
                LastVoltages = [.. Enumerable.Range(0, 3).Select(phase => Word32(block, phase * 2) / 10.0)];

                // Best-effort, as on SolaX: a failed read of the setpoint or the control must not fail the reading.
                var setpoint = await TryReadAsync(client, SChargerRegisters.MaxChargePower, 2, cancellationToken).ConfigureAwait(false);
                var control = await TryReadAsync(client, SChargerRegisters.ChargingControl, 1, cancellationToken).ConfigureAwait(false);

                return new ChargerReading(
                    _time.GetUtcNow(),
                    power > 0 || highestCurrent >= DeliveringCurrent ? ChargerState.Charging : ChargerState.Unknown,
                    power * 100.0,
                    CurrentAmps: setpoint is { } value ? AmpsForPower(Word32(value, 0), SetpointPhases, Volts) : null,
                    // Only "resumed" is the state a start leaves; standby is what it reads before anyone wrote it.
                    ChargingAllowed: control is { } raw ? (SChargerControl)raw[0] == SChargerControl.Resumed : null,
                    Mode: control is { } named ? ((SChargerControl)named[0]).ToString() : null);
            }, cancellationToken).ConfigureAwait(false);

            LastReading = reading;
            Healthy(ModelKnown ? null : Words.From(PageText.ResourceManager, nameof(PageText.ChooseModel)));
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
        // Below 6 A is a pause: 0 kW.
        var power = PowerForAmps(amps < Capabilities.MinCurrentAmps ? 0 : amps, SetpointPhases, Volts);
        _logger.LogInformation(
            "Maximum power {Kilowatts} kW for {Amps} A on {Phases} phase(s).",
            (power / 10.0).ToString("0.0", CultureInfo.InvariantCulture), amps, SetpointPhases);

        await _device.UseAsync(async client =>
        {
            await client.WriteMultipleRegistersAsync(SChargerRegisters.MaxChargePower, [(ushort)(power >> 16), (ushort)(power & 0xFFFF)], cancellationToken)
                .ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        RaiseChanged();
    }

    public override async Task SetChargingAllowedAsync(bool allowed, CancellationToken cancellationToken)
    {
        var control = allowed ? SChargerControl.Resumed : SChargerControl.Paused;

        await _device.UseAsync(async client =>
        {
            await client.WriteSingleRegisterAsync(SChargerRegisters.ChargingControl, (ushort)control, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        RaiseChanged();
    }

    public override void Reset()
    {
        LastVoltages = null;
        base.Reset();
    }

    /// <summary>
    /// What the charger at <paramref name="address"/> is, read once: L1 must be a mains voltage, and L2 being one too
    /// makes it a 22KT, which is saved as the model when none was set. The address in use is read through the plugin's
    /// own connection (#3).
    /// </summary>
    public async Task<string> CheckAsync(DeviceAddress address, byte unitId, CancellationToken cancellationToken)
    {
        var voltages = await _device.AtAsync(
            address,
            unitId,
            client => client.ReadHoldingRegistersAsync(SChargerRegisters.PhaseL1Voltage, 4, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var l1 = Word32(voltages, 0);
        var l2 = Word32(voltages, 2);

        if (!IsMains(l1))
        {
            throw new InvalidOperationException($"it answers, but phase L1 reads {l1 / 10.0} V: is this an SCharger?");
        }

        var model = IsMains(l2) ? "22kt" : "7ks";
        if (!ModelKnown)
        {
            _model.Save(model);
        }

        return $"SCharger-{(model == "22kt" ? "22KT" : "7KS")}, L1 {l1 / 10.0:0} V";
    }

    /// <summary>Sets the model on the page: sent in the next hello.</summary>
    public void SetModel(string model)
    {
        _model.Save(model == "22kt" ? "22kt" : "7ks");
        RaiseChanged();
    }

    private static uint Word32(ushort[] registers, int offset) => ((uint)registers[offset] << 16) | registers[offset + 1];

    private static async Task<ushort[]?> TryReadAsync(IModbusClient client, ushort address, ushort count, CancellationToken cancellationToken)
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

/// <summary>
/// How the plugin finds its charger (#461): every host that accepts a connection on the charger's Modbus port, 502,
/// whose L1 holds a mains voltage at 0x1000. The host in use is asked through the plugin's own connection, since the
/// charger's Modbus server is strict about clients (#3). Reads only.
/// </summary>
public sealed class HuaweiSearch(ModbusDevice device)
{
    public const int Port = 502;

    public Task<IReadOnlyList<FoundCharger>> FindAsync(IReadOnlyList<IPAddress> hosts, CancellationToken cancellationToken) =>
        device.FindAsync(hosts, Port, DescribeAsync, cancellationToken);

    public Task<string?> ProbeAsync(DeviceAddress address, CancellationToken cancellationToken) =>
        device.AtAsync(address, 1, client => DescribeAsync(client, cancellationToken), cancellationToken);

    private static async Task<string?> DescribeAsync(IModbusClient client, CancellationToken cancellationToken)
    {
        var voltages = await client.ReadHoldingRegistersAsync(SChargerRegisters.PhaseL1Voltage, 4, cancellationToken).ConfigureAwait(false);
        var l1 = ((uint)voltages[0] << 16) | voltages[1];
        var l2 = ((uint)voltages[2] << 16) | voltages[3];
        return HuaweiCharger.IsMains(l1) ? $"SCharger-{(HuaweiCharger.IsMains(l2) ? "22KT" : "7KS")}" : null;
    }
}
