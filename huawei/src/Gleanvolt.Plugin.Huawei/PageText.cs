using Gleanvolt.Chargers;

namespace Gleanvolt.Plugin.Huawei;

/// <summary>The Huawei page's own texts. SCharger, FusionSolar and the control states stay as Huawei names them.</summary>
internal static class PageText
{
    public static readonly Words ChooseModel = new(
        "Choose the charger's model on this page, or save its address: the check tells the two apart.",
        "Vyberte model nabíječky na této stránce nebo uložte její adresu: ověření oba modely rozliší.");
    public static readonly Words Model = new("Model", "Model");
    public static readonly Words ModelIntro = new(
        "The 7KS charges on one phase, the 22KT on three. Gleanvolt checks its charge control against it when the plugin "
        + "next connects; restart the plugin after a change.",
        "7KS nabíjí na jedné fázi, 22KT na třech. Gleanvolt podle toho zkontroluje řízení nabíjení, až se plugin příště "
        + "připojí; po změně plugin restartujte.");
    public static readonly Words Voltages = new("Phase voltages", "Napětí fází");
    public static readonly Words Control = new("Charging control", "Řízení nabíjení");
    public static readonly Words NoStatus = new(
        "The SCharger reports no status, only power: with no power it reads Unknown, whether a car is plugged in or not.",
        "SCharger nehlásí stav, jen výkon: bez výkonu ukazuje Unknown, ať je auto připojené, nebo ne.");
    public static readonly Words PortHint = new("Modbus TCP: 502, unless it was changed in the charger's settings.", "Modbus TCP: 502, pokud se nezměnil v nastavení nabíječky.");
}
