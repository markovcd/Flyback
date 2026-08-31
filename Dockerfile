# syntax=docker/dockerfile:1

# A release, in the order one goes: restore, compile, test, and one
# self-contained publish per platform. This is not a development environment and
# nothing in it runs the program — it exists to produce the artifacts, and to
# fail if the tests do.
#
#   docker build --output artifacts .
#
# See "Building with Docker" in the README for what comes out and how to get at
# it.

ARG SDK=mcr.microsoft.com/dotnet/sdk:10.0

FROM ${SDK} AS build

# One runtime identifier per platform. The project supports two more — win-arm64
# and osx-x64 — and asking for them is an argument rather than an edit:
#
#   --build-arg RIDS="win-x64 win-arm64 osx-arm64 osx-x64 linux-x64"
#
# Each is a whole self-contained copy of the runtime, so the list is what the
# build costs in time and in disk.
ARG RIDS="win-x64 osx-arm64 linux-x64"
ARG CONFIGURATION=Release

# What the built binaries report themselves as, in the About window and in
# their own file properties — see Directory.Build.props for how Version
# reaches AssemblyVersion, AssemblyFileVersion and AssemblyInformationalVersion.
# Defaulted rather than required, so a plain `docker build --output artifacts .`
# still works; the release workflow is what passes the real one.
ARG VERSION=0.1.0

# The one thing the SDK image does not already have. libSkiaSharp is what the
# headless UI tests rasterise with, and it will not load at all without
# fontconfig beside it — which reads as a DllNotFoundException in every UI test
# rather than as anything to do with fonts. The fonts themselves are embedded in
# the application, so there is nothing else to install: no X server, no ICU
# (the projects are built InvariantGlobalization), no window manager.
RUN apt-get update \
 && apt-get install --yes --no-install-recommends libfontconfig1 \
 && rm -rf /var/lib/apt/lists/*

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /src

# Everything, in one layer, rather than the usual dance of copying the project
# files first to cache the restore. The solution names every project, so a
# restore with one of them missing fails outright — which makes the fast version
# of this a file that has to be edited every time a project is added, and
# silently wrong until somebody notices. The NuGet cache below buys back most of
# what that would have saved.
COPY . .

# Shared by every step that touches NuGet, including the per-platform publishes
# below — those are what actually download something, since each runtime pack is
# a fresh set of packages. With the cache a second build fetches nothing.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore Flyback.slnx

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build Flyback.slnx -c ${CONFIGURATION} --no-restore

# The gate. Every test in the solution — the engine's, the shell's headless UI
# ones, the plugins' — and the build stops here if any of them does.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet test Flyback.slnx -c ${CONFIGURATION} --no-build

# One publish per identifier, each restoring its own runtime pack. Self-contained
# and single-file are the project's own doing rather than flags here — see
# Flyback.App.csproj, which turns both on the moment there is an identifier to
# build for. Not --no-build: a build for another platform is a different build
# from the one the tests just ran against.
#
# Two programs per platform, into one folder. The shell and the command line are
# the same engine, the same plugin host and the same runtime behind two fronts,
# so publishing them over each other leaves one copy of all of it: the second
# publish rewrites the shared files with the same bytes and adds an executable,
# its deps.json and its runtimeconfig.json. Two folders would be two runtimes.
#
# The shell goes first, because on macOS its publish is what lays out the bundle
# — and after that the command line goes *inside* the bundle, where the payload
# it shares now lives.
#
# macOS goes one folder deeper and then loses that folder again. Publishing for
# an osx identifier lays out Flyback.app *beside* the publish output, so the
# payload goes to osx-arm64/publish and the bundle lands at
# osx-arm64/Flyback.app rather than inside its own payload — and once it has,
# the payload is every one of those files a second time, since the bundle is a
# copy of it. MacBundle.targets leaves it alone because a person who typed -o
# asked for it; nobody asked for this one, so out it goes and the identifier is
# left holding the bundle alone.
RUN --mount=type=cache,target=/root/.nuget/packages \
    set -eu; \
    for rid in ${RIDS}; do \
      case ${rid} in \
        osx-*) out=/out/${rid}/publish ;; \
        *)     out=/out/${rid} ;; \
      esac; \
      dotnet publish src/Flyback.App -c ${CONFIGURATION} -r ${rid} -o ${out} -p:Version=${VERSION}; \
      case ${rid} in \
        osx-*) rm -rf ${out}; out=/out/${rid}/Flyback.app/Contents/MacOS ;; \
      esac; \
      dotnet publish src/Flyback.Cli -c ${CONFIGURATION} -r ${rid} -o ${out} -p:Version=${VERSION}; \
    done

# Nothing but the artifacts, so that `--output` writes the publish folders and
# not a filesystem around them. Publishing from Linux also means the executables
# carry their mode, which a cross-publish from Windows cannot manage — see the
# README for keeping it on the way out.
FROM scratch AS artifacts
COPY --from=build /out/ /
