# Huawei SCharger for Gleanvolt

Huawei's SCharger-7KS-S0 (one phase) and SCharger-22KT-S0 (three phases), over Modbus TCP. A charger plugin for [Gleanvolt](https://github.com/mpospisil/gleanvolt): Gleanvolt decides when
and how fast the car charges, and this plugin reads and commands the charger. MIT.

**Expected, not verified.** Written from Huawei's protocol, as Gleanvolt's built-in driver was. Owners report port 502 refused on 2023/2024 firmware, and nobody has confirmed the writes yet. Run it in `ChargeControl:DryRun`, record a session (below) and attach the recording to an issue. Behind an EMMA, the EMMA manages the charger: stay in dry-run until a recording shows a clean hand-over.

## Before you start

- **Modbus TCP on** in FusionSolar (the charger's commissioning page); it is off by default.
- A **fixed address** in your router.
- **Gleanvolt 1.3.3 or later**, which pairs with the plugin.

## Run it

### Docker (Linux, a Raspberry Pi, a NAS)

```sh
docker run -d --name gleanvolt-plugin-huawei --restart unless-stopped --network host \
  -v gleanvolt-plugin-huawei:/data \
  -e Gleanvolt__Endpoint=http://localhost:8090 \
  ghcr.io/mpospisil/gleanvolt-plugin-huawei:latest
```

`--network host` lets the page reach the charger on your LAN and puts it on port 8099. Run it on the same machine
as Gleanvolt, or set `Gleanvolt__Endpoint` to Gleanvolt's address and `Gleanvolt__PageUrl` to the page's address as your
browser reaches it (`http://<this machine>:8099/`).

`Gleanvolt__Token` is optional. Without it the plugin asks to be paired, and you accept it in Gleanvolt (see "Set it up"). Set it, or `--Gleanvolt:Token=<token>` for the zip, only to use a token you made on Gleanvolt's Settings → Plugins.

### From the zip (Windows, Linux)

Download `gleanvolt-plugin-huawei-<version>-<platform>.zip` from the
[releases](https://github.com/mpospisil/gleanvolt-chargers/releases), unpack it, and run:

```sh
./Gleanvolt.Plugin.Huawei --Gleanvolt:Endpoint=http://localhost:8090
```

On Windows, `Gleanvolt.Plugin.Huawei.exe`. Nothing needs .NET installed. The settings it saves go to `data/` beside
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

- **No status.** The SCharger reports power, voltages and currents and nothing else. With power it reads *Charging*; without, *Unknown*, since no car and a car waiting both read 0 W. Gleanvolt never ends a session on *Unknown*.
- **The setpoint is power.** The plugin converts Gleanvolt's amps at the voltage and on the phases Gleanvolt's charge control says in its welcome: 16 A on three phases is 11.0 kW.
- **The model.** Saving the address tells a 22KT (L2 live) from a 7KS and saves it; you can also choose it on the page or set `Huawei:Model`.
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
| `Urls` | `http://localhost:8099` | Where the page listens |
| `DataDirectory` | `data` | Where the saved settings and the page's keys are kept, owner-only |
| `Huawei:Host` | | The charger's address, until one is saved on the page |
| `Huawei:UnitId` | `1` | Its Modbus unit id |
| `Huawei:Model` | | `7ks` or `22kt`; the check sets it |
