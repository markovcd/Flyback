# syntax=docker/dockerfile:1

# The preset site, as the NAS runs it. Built from the repository root:
#
#   docker build -f src/Flyback.Server/Dockerfile -t flyback-presets .
#
# Add --platform linux/arm64 for an ARM NAS. See deploy/presets/README.md.

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
COPY src/Flyback.Server src/Flyback.Server

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Flyback.Server -c Release -o /app

FROM ${RUNTIME}
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Presets__Database=/data/presets.db \
    Presets__Media=/media

VOLUME ["/data"]
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Flyback.Server.dll"]
