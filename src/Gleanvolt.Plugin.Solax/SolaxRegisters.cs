namespace Gleanvolt.Plugin.Solax;

/// <summary>
/// The SolaX GEN2 EV charger's registers (X1/X3-HAC, and the EVC on the same map), cross-referenced with the
/// wills106/homeassistant-solax-modbus integration (plugin_solax_ev_charger.py, Apache-2.0: see
/// THIRD-PARTY-NOTICES.md). The input registers are verified on an X3-HAC; the two holding registers are written by
/// Gleanvolt's built-in driver since 1.0 on that same charger.
/// </summary>
public static class SolaxRegisters
{
    /// <summary>Input: the total charge power, in watts, unsigned.</summary>
    public const ushort ChargePowerTotal = 0x000B;

    /// <summary>Input: the run mode, 0-13, in OCPP 1.6's order (see <see cref="SolaxCharger.StateOf"/>).</summary>
    public const ushort RunMode = 0x001D;

    /// <summary>Holding: the current setpoint, in hundredths of an amp (16 A is 1600).</summary>
    public const ushort ChargeCurrentSetpoint = 0x0628;

    /// <summary>Holding: the use-mode (<see cref="SolaxUseMode"/>).</summary>
    public const ushort ChargerUseMode = 0x060D;

    /// <summary>Holding: the serial number, ASCII, two characters a register.</summary>
    public const ushort SerialNumberStart = 0x0600;

    /// <summary>How many registers the serial number takes.</summary>
    public const ushort SerialNumberCount = 7;

    /// <summary>The highest run mode: Stopping.</summary>
    public const ushort HighestRunMode = 13;

    /// <summary>The current setpoint register's step: hundredths of an amp.</summary>
    public const double AmpsPerCount = 0.01;
}

/// <summary>The use-mode register's values. Only Stop and Fast are ever written; Eco and Green are the owner's.</summary>
public enum SolaxUseMode
{
    Stop = 0,
    Fast = 1,
    Eco = 2,
    Green = 3,
}
