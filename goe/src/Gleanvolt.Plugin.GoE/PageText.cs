using Gleanvolt.Chargers;

namespace Gleanvolt.Plugin.GoE;

/// <summary>The go-e page's own texts. go-e and the API's keys stay as go-e names them.</summary>
internal static class PageText
{
    public static readonly Words ApiOff = new(
        "The charger answers, but its local HTTP API v2 is off: switch it on in the go-e app (Internet → Advanced settings).",
        "Nabíječka odpovídá, ale její místní HTTP API v2 je vypnuté: zapněte ho v aplikaci go-e (Internet → Pokročilá nastavení).");
    public static readonly Words ForceState = new("Force state (frc)", "Vynucený stav (frc)");
    public static readonly Words Ceiling = new("The unit's ceiling (ama)", "Strop jednotky (ama)");
    public static readonly Words Firmware = new("Firmware", "Firmware");
    public static readonly Words PausedByGleanvolt = new("paused by Gleanvolt", "pozastaveno Gleanvoltem");
    public static readonly Words PortHint = new("The local HTTP API v2: 80.", "Místní HTTP API v2: 80.");
}
