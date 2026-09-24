# syntax=docker/dockerfile:1

# The site, as the NAS runs it. Built from the repository root, with the
# release key the site's plugins are signed with:
#
#   docker build --secret id=release-key,env=RELEASE_SIGNING_KEY -f src/Flyback.Server/Dockerfile -t flyback-site .
#
# The Release workflow pushes it to ghcr.io/markovcd/flyback-site for amd64 and
# arm64, and deploy/site/publish-dev.sh pushes a local build as :dev. See
# deploy/site/README.md.

ARG SDK=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME=mcr.microsoft.com/dotnet/aspnet:10.0

# The build runs on the builder's own processor whatever the target: a
# framework-dependent publish runs anywhere, so an arm64 image needs no emulation.
FROM --platform=$BUILDPLATFORM ${SDK} AS build
WORKDIR /src

# What the site and its plugins report themselves as. Blank leaves it to
# Directory.Build.props; the Release workflow passes the release's.
ARG VERSION=""

# Directory.Packages.props among them: it carries every package version, so a
# restore without it has none.
COPY .editorconfig Directory.Build.props Directory.Build.targets Directory.Packages.props global.json ./
COPY site site
# All of src/, since the plugins the site starts with are whatever
# Flyback.Server.csproj lists.
COPY src src

# Each plugin the site starts with, packed and signed here with the release key
# handed in as a build secret, so the site never holds a key (ADR-0141).
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=secret,id=release-key,required=true \
    set -eu; \
    dotnet msbuild src/Flyback.Server -t:ListSitePlugins -p:SitePluginsFile=/tmp/site-plugins -nologo -v:q; \
    while IFS='|' read -r project name <&3; do \
      dotnet run --project src/Flyback.Cli -c Release -- pack-plugin "$project" \
        -o "src/Flyback.Server/Defaults/$name.fbkp" --key /run/secrets/release-key; \
    done 3< /tmp/site-plugins

RUN --mount=type=cache,target=/root/.nuget/packages \
    if [ -n "${VERSION}" ]; then export Version="${VERSION}"; fi;     dotnet publish src/Flyback.Server -c Release -o /app

FROM ${RUNTIME}
LABEL org.opencontainers.image.source=https://github.com/markovcd/Flyback
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Site__Database=/data/presets.db \
    Site__Media=/media

VOLUME ["/data"]
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Flyback.Server.dll"]
