using Gleanvolt.Chargers;

namespace Gleanvolt.Plugin.Solax;

/// <summary>The SolaX page's own texts. The use-modes (Stop, Fast, Eco, Green) stay as SolaX names them.</summary>
internal static class PageText
{
    public static readonly Words UseMode = new("Use-mode", "Režim nabíječky");
    public static readonly Words Model = new("Model", "Model");
    public static readonly Words PhasesIntro = new(
        "What the charger supplies: an X3 three phases, an X1 one. Gleanvolt checks its charge control against it when the "
        + "plugin next connects; restart the plugin after a change.",
        "Kolik fází nabíječka dodává: X3 tři, X1 jednu. Gleanvolt podle toho zkontroluje řízení nabíjení, až se plugin "
        + "příště připojí; po změně plugin restartujte.");
    public static readonly Words ThreePhases = new("Three phases (X3)", "Tři fáze (X3)");
    public static readonly Words OnePhase = new("One phase (X1)", "Jedna fáze (X1)");
    public static readonly Words PortHint = new("Modbus TCP: 502, unless it was changed in the charger's settings.", "Modbus TCP: 502, pokud se nezměnil v nastavení nabíječky.");
}
