# syntax=docker/dockerfile:1

# The site, as the NAS runs it. Built from the repository root, with the
# release key the Figures plugin is signed with:
#
#   docker build --secret id=release-key,env=RELEASE_SIGNING_KEY -f src/Flyback.Server/Dockerfile -t flyback-site .
#
# Add --platform linux/arm64 for an ARM NAS. See deploy/site/README.md.

ARG SDK=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${SDK} AS build
WORKDIR /src

# Directory.Packages.props among them: it carries every package version, so a
# restore without it has none.
COPY .editorconfig Directory.Build.props Directory.Build.targets Directory.Packages.props global.json ./
COPY site/assets site/assets
COPY src/Flyback.Core src/Flyback.Core
COPY src/Flyback.Engine src/Flyback.Engine
COPY src/Flyback.Plugins src/Flyback.Plugins
COPY src/Flyback.Cli src/Flyback.Cli
COPY src/Flyback.Plugins.Figures src/Flyback.Plugins.Figures
COPY src/Flyback.Server src/Flyback.Server

# The plugin the site starts with, packed and signed here with the release key
# handed in as a build secret, so the site never holds a key (ADR-0141).
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=secret,id=release-key,required=true \
    dotnet run --project src/Flyback.Cli -c Release -- pack-plugin src/Flyback.Plugins.Figures \
      -o src/Flyback.Server/Defaults/Figures.fbkp --key /run/secrets/release-key

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Flyback.Server -c Release -o /app

FROM ${RUNTIME}
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Site__Database=/data/presets.db \
    Site__Media=/media

VOLUME ["/data"]
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Flyback.Server.dll"]
