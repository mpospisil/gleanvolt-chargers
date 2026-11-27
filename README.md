# Gleanvolt charger plugins

The EV charger plugins for [Gleanvolt](https://github.com/mpospisil/gleanvolt), the solar EV-charging controller. In
Gleanvolt every charger is a plugin: a small program for one charger family, with its own page for the charger's
address and settings, that connects to Gleanvolt and asks to be paired. Gleanvolt decides; the plugin reads and
commands the charger. A charger fix ships here, without a Gleanvolt release.

| Plugin | Chargers | Over | Image | Page |
|---|---|---|---|---|
| [SolaX](solax/README.md) | X1/X3-HAC, X1/X3-EVC | Modbus TCP | `ghcr.io/mpospisil/gleanvolt-plugin-solax` | 8098 |
| [Huawei](huawei/README.md) | SCharger-7KS-S0, SCharger-22KT-S0 | Modbus TCP | `ghcr.io/mpospisil/gleanvolt-plugin-huawei` | 8099 |
| [go-e](goe/README.md) | go-e Charger V3+ | HTTP API v2 | `ghcr.io/mpospisil/gleanvolt-plugin-goe` | 8100 |
| [Shelly](shelly/README.md) | Shelly TopAC (EVE01-11R) | Gen2 RPC | `ghcr.io/mpospisil/gleanvolt-plugin-shelly` | 8097 |

Each runs from its image (`linux/amd64`, `linux/arm64`) or from a self-contained zip (`win-x64`, `linux-x64`,
`linux-arm64`), and is released on its own: a tag `<name>-v<version>`, such as `solax-v1.0.0`. Gleanvolt 1.3.3 or later for pairing
(charger contract 1.1).

## Layout: a solution per charger

Each charger is a folder of its own, laid out as a plugin in its own repository is:

```
solax/
  Gleanvolt.Plugin.Solax.slnx
  README.md
  src/Gleanvolt.Plugin.Solax/      the plugin: Program.cs, its charger, Pages/Home.razor
  tests/Gleanvolt.Plugin.Solax.Tests/
```

A charger references only packages from nuget.org: `Gleanvolt.Chargers.Hosting` (the host, the page, the settings, the
search, the recording) and, for its tests, `Gleanvolt.Providers.Testing` (`PluginHarness`, `FakeModbusServer`). Nothing
is shared between the folders but `Directory.Build.props` and `NuGet.config`, so any one of them can be lifted into a
repository of its own unchanged, with those two files beside it.

A plugin's page and status texts live in `Resources/PageText.resx` (English, the neutral file) and
`Resources/PageText.cs.resx` (Czech) under its `src` folder. The root `Directory.Build.targets` generates an internal
`PageText` class from the neutral file, so a missing key is a compile error. A language the plugin lacks falls back to
English. Statuses use `Words.From(PageText.ResourceManager, nameof(PageText.Key))`.

## Your own charger, in your own repository

You don't need this repository to support another charger. Start a repository with a folder like the ones here, or copy
one, and follow the
[Gleanvolt.Chargers.Hosting README](https://github.com/mpospisil/gleanvolt/blob/main/src/Gleanvolt.Chargers.Hosting/README.md):
a plugin is a `PluginCharger` (its capabilities, a read, the two commands), a page in the shared `PluginFrame`, and a
`Program.cs` that names it. Gleanvolt takes any charger plugin the owner accepts, or one given a provider token; the owner runs it. The contract
is in Gleanvolt's [docs/PROVIDERS.md](https://github.com/mpospisil/gleanvolt/blob/main/docs/PROVIDERS.md#the-charger-kind).
A charger worth having here is welcome as a pull request.

## Building

```sh
dotnet test solax/Gleanvolt.Plugin.Solax.slnx      # one charger
dotnet test Gleanvolt.Chargers.slnx                # every charger
```

The tests run each plugin against the SDK's `ChargerTestHub` over a real socket, with a fake charger on Modbus TCP or
HTTP.

The packages come from nuget.org (`GleanvoltSdkVersion` in `Directory.Build.props`). To build against ones not released
yet, pack them from Gleanvolt's source into `artifacts/feed/`, which `NuGet.config` also reads:

```sh
cd ../gleanvolt
for p in Gleanvolt.Providers.Contracts Gleanvolt.Providers.Client Gleanvolt.Providers.Testing Gleanvolt.Chargers.Hosting Gleanvolt.Modbus; do
  dotnet pack src/$p -c Release -p:BuildNumber=1 -o ../gleanvolt-chargers/artifacts/feed
done
```

An image, from the repository root: `docker build -f docker/Dockerfile --build-arg PLUGIN=Solax --build-arg PORT=8098 .`

## Licence

MIT (`LICENSE`). Third-party components and the SolaX register map's attribution: `THIRD-PARTY-NOTICES.md`.
