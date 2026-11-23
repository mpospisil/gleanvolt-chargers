# syntax=docker/dockerfile:1
#
# One charger plugin's image: ghcr.io/mpospisil/gleanvolt-plugin-<name>, built for linux/amd64 and linux/arm64 by
# .github/workflows/release.yml from a tag such as solax-v1.0.0. Cross-compiled, so an amd64 machine builds the Pi's
# arm64 image at native speed:
#
#   docker build --platform linux/arm64 -f docker/Dockerfile \
#       --build-arg PLUGIN=Solax --build-arg PORT=8098 -t gleanvolt-plugin-solax .
#
# PLUGIN is the project's suffix (Solax, Huawei, GoE, Shelly), PORT its page's.

ARG DOTNET_VERSION=10.0

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG TARGETARCH
ARG PLUGIN
ARG VERSION=0.0.0-dev
ARG SOURCE_REVISION=

WORKDIR /source
COPY NuGet.config Directory.Build.props ./
COPY artifacts/feed/ artifacts/feed/
# Each charger is a folder of its own, named for it in lower case: solax/src/Gleanvolt.Plugin.Solax.
COPY . .
RUN test -n "$PLUGIN" \
    && folder=$(echo "$PLUGIN" | tr '[:upper:]' '[:lower:]') \
    && dotnet publish "${folder}/src/Gleanvolt.Plugin.${PLUGIN}/Gleanvolt.Plugin.${PLUGIN}.csproj" \
        -a "$TARGETARCH" -c Release --self-contained false \
        -p:Version="$VERSION" -p:SourceRevisionId="$SOURCE_REVISION" -o /app \
    # The data directory, for the volume: created here, owned by the app's user below.
    && mkdir -p /data

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
ARG PLUGIN
ARG PORT
ARG APP_UID=1654
WORKDIR /app
COPY --from=build --chown=${APP_UID}:${APP_UID} /app .
COPY --chown=${APP_UID}:${APP_UID} LICENSE THIRD-PARTY-NOTICES.md /app/
LABEL org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.source="https://github.com/mpospisil/gleanvolt-chargers"

# The settings the page saves (the address, a password) and the page's own keys: owned by the app's user and closed to
# everyone else; a named volume takes these permissions over on first use.
COPY --from=build --chown=${APP_UID}:${APP_UID} --chmod=700 /data /data

USER ${APP_UID}
ENV DOTNET_EnableDiagnostics=0 \
    Urls=http://0.0.0.0:${PORT} \
    DataDirectory=/data \
    PLUGIN_DLL=Gleanvolt.Plugin.${PLUGIN}.dll
VOLUME /data
EXPOSE ${PORT}

ENTRYPOINT ["sh", "-c", "exec dotnet \"$PLUGIN_DLL\""]
