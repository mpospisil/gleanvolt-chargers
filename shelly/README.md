# Shelly TopAC for Gleanvolt

The Shelly TopAC Portable EV Charger (EVE01-11R: 6–16 A, three phases, 11 kW), over its local Gen2 RPC API. A charger plugin for [Gleanvolt](https://github.com/mpospisil/gleanvolt): Gleanvolt decides when
and how fast the car charges, and this plugin reads and commands the charger. MIT.

**Not yet verified on hardware.** Written from Shelly's API documentation; every test is synthetic. Run it in `ChargeControl:DryRun`, record a session (below) and attach the recording to an issue.

## Before you start

- A **fixed address** in your router.
- **Auto balance off** if you use Shelly EM load management: it lowers the current on its own and works against Gleanvolt.
- Authentication may stay on: enter the password set in the Shelly app on the plugin's page.
- **Gleanvolt 1.3.3 or later**, which pairs with the plugin.

## Run it

### Docker (Linux, a Raspberry Pi, a NAS)

```sh
docker run -d --name gleanvolt-plugin-shelly --restart unless-stopped --network host \
  -v gleanvolt-plugin-shelly:/data \
  -e Gleanvolt__Endpoint=http://localhost:8090 \
  ghcr.io/mpospisil/gleanvolt-plugin-shelly:latest
```

`--network host` lets the page reach the charger on your LAN and puts it on port 8097. Run it on the same machine
as Gleanvolt, or set `Gleanvolt__Endpoint` to Gleanvolt's address and `Gleanvolt__PageUrl` to the page's address as your
browser reaches it (`http://<this machine>:8097/`).

`Gleanvolt__Token` is optional. Without it the plugin asks to be paired, and you accept it in Gleanvolt (see "Set it up"). Set it, or `--Gleanvolt:Token=<token>` for the zip, only to use a token you made on Gleanvolt's Settings → Plugins.

### From the zip (Windows, Linux)

Download `gleanvolt-plugin-shelly-<version>-<platform>.zip` from the
[releases](https://github.com/mpospisil/gleanvolt-chargers/releases), unpack it, and run:

```sh
./Gleanvolt.Plugin.Shelly --Gleanvolt:Endpoint=http://localhost:8090
```

On Windows, `Gleanvolt.Plugin.Shelly.exe`. Nothing needs .NET installed. The settings it saves go to `data/` beside
it; `--DataDirectory=<path>` puts them elsewhere.

## Set it up

1. Start the plugin, then accept it in Gleanvolt: **Settings → Plugins** (or the setup Wizard) lists it
   as asking to be paired. Press **Accept**. The **Charger** row then shows connected, with an **Open** link to this
   plugin's page.
2. On the page, type the charger's address (its IP, or `shelly.local` names) and port (80), then **Save and check**. With
   authentication on in the Shelly app, save its password there too.
3. The page shows the charger's state, and Gleanvolt's Overview the car's. Start a charging mode on Gleanvolt's
   **Charging plan**.

Gleanvolt remembers the first charger plugin it accepts and lets in no other; to switch, stop the old one and press
**Forget** beside it under **Settings → Plugins**. If Gleanvolt turns the plugin away because its charge
control asks more than the charger can do, the same page says which setting to lower.

## What it does

- **The password.** With authentication on in the Shelly app, the plugin sends Shelly's digest (user `admin`) with the password saved on its page. It is kept in the data directory, readable by the plugin alone, never shown again and never recorded.
- **The unit of `total_power`.** Not documented: the plugin compares it with V × I on the three phases once the car draws over 500 W, and keeps the unit it finds.
- **A pause is the start/stop switch off,** since the charger takes nothing below 6 A; the next setpoint writes the limit first, then the switch back on.
- **A recording for testers.** **Start recording** on the page records every exchange with the charger, as sent and as
  received, for 15 minutes while Gleanvolt runs as usual. **Download** it and attach it to an
  [issue](https://github.com/mpospisil/gleanvolt-chargers/issues): it is how a charger nobody here has gets verified.

## Configuration

Every setting can be given on the command line (`--Section:Key=value`), in the environment (`Section__Key=value`) or with
`dotnet user-secrets` from the source tree. What is saved on the page wins over it.

| Key | Default | What it is |
|---|---|---|
| `Gleanvolt:Endpoint` | `http://localhost:8090` | Gleanvolt's address |
| `Gleanvolt:Token` | | An optional token; without it the plugin asks to be paired |
| `Gleanvolt:PageUrl` | from `Urls` | The page's address as your browser reaches it |
| `Urls` | `http://localhost:8097` | Where the page listens |
| `DataDirectory` | `data` | Where the saved settings and the page's keys are kept, owner-only |
| `Shelly:Host` | | The charger's address, until one is saved on the page |
| `Shelly:MaxCurrentAmps` | `16` | The highest current the charger takes; Gleanvolt never sends more |
