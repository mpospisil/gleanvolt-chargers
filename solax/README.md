# SolaX EV charger for Gleanvolt

SolaX's X1/X3-HAC and X1/X3-EVC chargers, over Modbus TCP. A charger plugin for [Gleanvolt](https://github.com/mpospisil/gleanvolt): Gleanvolt decides when
and how fast the car charges, and this plugin reads and commands the charger. MIT.

**The reference charger.** Gleanvolt's built-in driver has run an X3-HAC since 1.0; this plugin is that driver, ported. Until a site has run it for a while, try it in `ChargeControl:DryRun` first.

## Before you start

- **Modbus TCP on** in the charger's settings, on port 502 (unit 1 by default).
- **Unlinked from the inverter's own "Green"/"ECO" charging**, so only Gleanvolt drives it.
- A **fixed address** in your router.
- **Gleanvolt 1.3.3 or later**, which pairs with the plugin.

## Run it

### Docker (Linux, a Raspberry Pi, a NAS)

```sh
docker run -d --name gleanvolt-plugin-solax --restart unless-stopped --network host \
  -v gleanvolt-plugin-solax:/data \
  -e Gleanvolt__Endpoint=http://localhost:8090 \
  ghcr.io/mpospisil/gleanvolt-plugin-solax:latest
```

`--network host` lets the page reach the charger on your LAN and puts it on port 8098. Run it on the same machine
as Gleanvolt, or set `Gleanvolt__Endpoint` to Gleanvolt's address and `Gleanvolt__PageUrl` to the page's address as your
browser reaches it (`http://<this machine>:8098/`).

`Gleanvolt__Token` is optional. Without it the plugin asks to be paired, and you accept it in Gleanvolt (see "Set it up"). Set it, or `--Gleanvolt:Token=<token>` for the zip, only to use a token you made on Gleanvolt's Settings → Plugins.

### From the zip (Windows, Linux)

Download `gleanvolt-plugin-solax-<version>-<platform>.zip` from the
[releases](https://github.com/mpospisil/gleanvolt-chargers/releases), unpack it, and run:

```sh
./Gleanvolt.Plugin.Solax --Gleanvolt:Endpoint=http://localhost:8090
```

On Windows, `Gleanvolt.Plugin.Solax.exe`. Nothing needs .NET installed. The settings it saves go to `data/` beside
it; `--DataDirectory=<path>` puts them elsewhere.

## Set it up

1. Start the plugin, then accept it in Gleanvolt: **Settings → Plugins** (or the setup Wizard) lists it
   as asking to be paired. Press **Accept**. The **Charger** row then shows connected, with an **Open** link to this
   plugin's page.
2. On the page, **Find the charger** searches your network, or type its address and port, then **Save and check**.
3. The page shows the charger's state, and Gleanvolt's Overview the car's. Start a charging mode on Gleanvolt's
   **Charging plan**.

Gleanvolt remembers the first charger plugin it accepts and lets in no other; to switch, stop the old one and press
**Forget** beside it under **Settings → Plugins**. If Gleanvolt turns the plugin away because its charge
control asks more than the charger can do, the same page says which setting to lower.

## What it does

- **Phases.** An X3 supplies three phases, an X1 one. Choose it on the page (or `Solax:Phases=1`); Gleanvolt checks its charge control against it at the next connection.
- **A flaky link.** The charger's Modbus link drops for a poll or two many times a day, and for a while after it comes back the use-mode register can read values that are no use-mode at all. The plugin reports "allowed" only when it reads *Fast*, and Gleanvolt judges the owner's intent over time, as it did with the built-in driver. A read that fails is a charger Gleanvolt could not read: it decides nothing on it, and suspends a running mode only after 2 minutes.
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
| `Urls` | `http://localhost:8098` | Where the page listens |
| `DataDirectory` | `data` | Where the saved settings and the page's keys are kept, owner-only |
| `Solax:Host` | | The charger's address, until one is saved on the page |
| `Solax:UnitId` | `1` | Its Modbus unit id |
| `Solax:Phases` | `3` | `1` for an X1 |
