# go-e Charger for Gleanvolt

The go-e Charger (Gemini, Gemini flex, HOMEfix, HOME+; hardware V3 and later), over its local HTTP API v2. A charger plugin for [Gleanvolt](https://github.com/mpospisil/gleanvolt): Gleanvolt decides when
and how fast the car charges, and this plugin reads and commands the charger. MIT.

**Expected, not verified.** Written from go-e's API documentation, as Gleanvolt's built-in driver was. Run it in `ChargeControl:DryRun`, record a session (below) and attach the recording to an issue.

## Before you start

- **The local HTTP API v2 on** in the go-e app (Internet → Advanced settings); it is off by default. Older units speak only API v1, which this does not.
- A **fixed address** in your router.
- **Gleanvolt 1.3.1 or later**, and its provider token: **Settings → External providers → Make the token**, then copy it.

## Run it

### Docker (Linux, a Raspberry Pi, a NAS)

```sh
docker run -d --name gleanvolt-plugin-goe --restart unless-stopped --network host \
  -v gleanvolt-plugin-goe:/data \
  -e Gleanvolt__Endpoint=http://localhost:8090 \
  -e Gleanvolt__Token=<the provider token> \
  ghcr.io/mpospisil/gleanvolt-plugin-goe:latest
```

`--network host` lets the page reach the charger on your LAN and puts it on port 8100. Run it on the same machine
as Gleanvolt, or set `Gleanvolt__Endpoint` to Gleanvolt's address and `Gleanvolt__PageUrl` to the page's address as your
browser reaches it (`http://<this machine>:8100/`).

### From the zip (Windows, Linux)

Download `gleanvolt-plugin-goe-<version>-<platform>.zip` from the
[releases](https://github.com/mpospisil/gleanvolt-chargers/releases), unpack it, and run:

```sh
./Gleanvolt.Plugin.GoE --Gleanvolt:Endpoint=http://localhost:8090 --Gleanvolt:Token=<the provider token>
```

On Windows, `Gleanvolt.Plugin.GoE.exe`. Nothing needs .NET installed. The settings it saves go to `data/` beside
it; `--DataDirectory=<path>` puts them elsewhere.

## Set it up

1. In Gleanvolt, **Settings → External providers** shows the **Charger** row connected, with an **Open** link to this
   plugin's page.
2. On the page, **Find the charger** searches your network, or type its address, then **Save and check**.
3. The page shows the charger's state, and Gleanvolt's Overview the car's. Start a charging mode on Gleanvolt's
   **Charging plan**.

Gleanvolt remembers the first charger plugin it accepts and lets in no other; to switch, stop the old one and press
**Forget** beside it under **Settings → External providers**. If Gleanvolt turns the plugin away because its charge
control asks more than the charger can do, the same page says which setting to lower.

## What it does

- **Two keys written, nothing else:** the force state `frc` and the setpoint `amp`, each only on a change. Both are kept across a restart of the charger.
- **A pause is the force state off**, since the charger takes nothing below 6 A; the next setpoint writes `amp` and turns it back on in one request.
- **Its own ceiling.** `ama`, and 16 A on an 11 kW unit: the plugin tells Gleanvolt, which never sends more.
- **A recording for testers.** **Start recording** on the page records every exchange with the charger, as sent and as
  received, for 15 minutes while Gleanvolt runs as usual. **Download** it and attach it to an
  [issue](https://github.com/mpospisil/gleanvolt-chargers/issues): it is how a charger nobody here has gets verified.

## Configuration

Every setting can be given on the command line (`--Section:Key=value`), in the environment (`Section__Key=value`) or with
`dotnet user-secrets` from the source tree. What is saved on the page wins over it.

| Key | Default | What it is |
|---|---|---|
| `Gleanvolt:Endpoint` | `http://localhost:8090` | Gleanvolt's address |
| `Gleanvolt:Token` | | The provider token, required |
| `Gleanvolt:PageUrl` | from `Urls` | The page's address as your browser reaches it |
| `Urls` | `http://localhost:8100` | Where the page listens |
| `DataDirectory` | `data` | Where the saved settings and the page's keys are kept, owner-only |
| `Goe:Host` | | The charger's address, until one is saved on the page |
