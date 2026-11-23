namespace Gleanvolt.Plugin.Shelly;

/// <summary>The plugin's settings, from the <c>Shelly</c> section. The address and the password are entered on the page.</summary>
public sealed class ShellyOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Shelly";

    /// <summary>How long one RPC call may take. A read is four calls, and Gleanvolt waits 5 seconds for it.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>The highest current the charger takes: 16 A for the EVE01-11R (11 kW, three phases).</summary>
    public int MaxCurrentAmps { get; init; } = 16;

    /// <summary>How often the owner's own settings (auto balance) are read again for the warning.</summary>
    public TimeSpan OwnerSettingsInterval { get; init; } = TimeSpan.FromMinutes(5);
}
