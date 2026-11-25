using Gleanvolt.Chargers;

namespace Gleanvolt.Plugin.Shelly;

/// <summary>The Shelly page's own texts. Shelly, TopAC and the names of the charger's own settings and states stay as Shelly writes them.</summary>
internal static class PageText
{
    public static readonly Words Unauthorized = new(
        "The charger asks for a password: enter the one set in the Shelly app on this page.",
        "Nabíječka vyžaduje heslo: zadejte na této stránce heslo nastavené v aplikaci Shelly.");
    public static readonly Words NotTopAc = new(
        "The device at the address does not answer as a Shelly TopAC: {0}",
        "Zařízení na adrese neodpovídá jako Shelly TopAC: {0}");
    public static readonly Words AutoBalanceOn = new(
        "Auto balance is on in the Shelly app. It lowers the current on its own and works against Gleanvolt: turn it off.",
        "V aplikaci Shelly je zapnuté Auto balance. Samo snižuje proud a jde proti Gleanvoltu: vypněte ho.");

    public static readonly Words WorkState = new("work_state", "work_state");
    public static readonly Words PausedByGleanvolt = new("paused by Gleanvolt", "pozastaveno Gleanvoltem");
    public static readonly Words PerPhase = new("Per phase", "Po fázích");
    public static readonly Words UnitFound = new("total_power is in", "total_power je v");
    public static readonly Words UnitUnknown = new("not settled until the car draws", "neurčeno, dokud auto neodebírá");
    public static readonly Words OwnerSettings = new("In the Shelly app", "V aplikaci Shelly");
    public static readonly Words On = new("on", "zapnuto");
    public static readonly Words Off = new("off", "vypnuto");

    public static readonly Words PasswordHeading = new("The charger's password", "Heslo nabíječky");
    public static readonly Words PasswordIntro = new(
        "Only when authentication is on in the Shelly app. It is kept in the plugin's data directory, readable by the plugin "
        + "alone, and never shown again.",
        "Jen pokud je v aplikaci Shelly zapnuté ověřování. Uloží se do datového adresáře pluginu, čitelné jen pro plugin, "
        + "a znovu se už nezobrazí.");
    public static readonly Words PasswordSet = new("A password is saved.", "Heslo je uložené.");
    public static readonly Words PasswordNone = new("No password: authentication is off.", "Žádné heslo: ověřování je vypnuté.");
    public static readonly Words Save = new("Save", "Uložit");
    public static readonly Words Remove = new("Remove", "Odebrat");
    public static readonly Words PortHint = new("The local RPC API: 80.", "Místní RPC API: 80.");
}
