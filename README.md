# Gleanvolt charger plugins

The EV charger plugins for [Gleanvolt](https://github.com/mpospisil/gleanvolt), the solar EV-charging controller. In
Gleanvolt every charger is a plugin: a small program for one charger family, with its own page for the charger's
address and settings, that connects to Gleanvolt with the provider token. Gleanvolt decides; the plugin reads and
commands the charger. A charger fix ships here, without a Gleanvolt release.

| Plugin | Chargers | Over | Image | Page |
|---|---|---|---|---|
| [SolaX](src/Gleanvolt.Plugin.Solax/README.md) | X1/X3-HAC, X1/X3-EVC | Modbus TCP | `ghcr.io/mpospisil/gleanvolt-plugin-solax` | 8098 |
| [Huawei](src/Gleanvolt.Plugin.Huawei/README.md) | SCharger-7KS-S0, SCharger-22KT-S0 | Modbus TCP | `ghcr.io/mpospisil/gleanvolt-plugin-huawei` | 8099 |
| [go-e](src/Gleanvolt.Plugin.GoE/README.md) | go-e Charger V3+ | HTTP API v2 | `ghcr.io/mpospisil/gleanvolt-plugin-goe` | 8100 |
| [Shelly](src/Gleanvolt.Plugin.Shelly/README.md) | Shelly TopAC (EVE01-11R) | Gen2 RPC | `ghcr.io/mpospisil/gleanvolt-plugin-shelly` | 8097 |

Each runs from its image (`linux/amd64`, `linux/arm64`) or from a self-contained zip (`win-x64`, `linux-x64`,
`linux-arm64`), and is released on its own: a tag `<name>-v<version>`, such as `solax-v1.0.0`. Gleanvolt 1.3.1 or later
(charger contract 1.1).

## Writing one

A plugin is an ASP.NET Core app on the MIT [Gleanvolt provider SDK](https://www.nuget.org/packages/Gleanvolt.Providers.Client)
(`Gleanvolt.Providers.Client`, with `Gleanvolt.Modbus` for a Modbus charger). Here, `Gleanvolt.Chargers.Shared` holds
what every plugin shares: the host and its guarded page, the base of a charger (`PluginCharger`) and its status, the
page's frame with its sections, the address store, the search, and the recording for testers. A plugin keeps its device:

- a `PluginCharger`: `Capabilities`, `ReadAsync`, `SetCurrentAsync`, `SetChargingAllowedAsync`, calling `Healthy` or
  `Problem` as it reads;
- a page, `Pages/Home.razor`, in a `PluginFrame` with what it shows of its charger;
- optionally an `IChargerSearch`, a read that says whether an address is its charger;
- a `Program.cs` that names it (`PluginInfo`) and registers its transport.

The contract (what a reading must hold, what a pause is, the commands) is in Gleanvolt's
[docs/PROVIDERS.md](https://github.com/mpospisil/gleanvolt/blob/main/docs/PROVIDERS.md#the-charger-kind).

## Building

```sh
dotnet test Gleanvolt.Chargers.slnx
```

The tests run each plugin against the SDK's `ChargerTestHub` (`Gleanvolt.Providers.Testing`) over a real socket, with
a fake charger on Modbus TCP or HTTP.

The SDK comes from nuget.org (`GleanvoltSdkVersion` in `Directory.Build.props`). To build against an SDK not released
yet, pack it from Gleanvolt's source into `artifacts/feed/`, which `NuGet.config` also reads:

```sh
cd ../gleanvolt
for p in Gleanvolt.Providers.Contracts Gleanvolt.Providers.Client Gleanvolt.Providers.Testing Gleanvolt.Modbus; do
  dotnet pack src/$p -c Release -p:BuildNumber=1 -o ../gleanvolt-chargers/artifacts/feed
done
```

An image, from the repository root: `docker build -f docker/Dockerfile --build-arg PLUGIN=Solax --build-arg PORT=8098 .`

## Licence

MIT (`LICENSE`). Third-party components and the SolaX register map's attribution: `THIRD-PARTY-NOTICES.md`.
