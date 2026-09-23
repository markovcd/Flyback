# syntax=docker/dockerfile:1

# A release, in the order one goes: restore, compile, test, and one
# self-contained publish per platform. This is not a development environment and
# nothing in it runs the program — it exists to produce the artifacts, and to
# fail if the tests do.
#
#   docker build --output artifacts .
#
# Everything before the publishes is a stage of its own, so the gate can be
# asked for without the part that takes the time:
#
#   docker build --target gate .
#
# which is what CI runs on every change. See "Building with Docker" in the
# README for what comes out and how to get at it.

ARG SDK=mcr.microsoft.com/dotnet/sdk:10.0

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
# still works; the release workflow is what passes the real one. The default
# carries a suffix because a bare version is a release to ReleaseFeed and to
# Usage, and .dockerignore leaves no commit here to mark the build otherwise.
ARG VERSION=0.1.0-dev

# Everything a change has to get past. An argument declared above the first FROM
# is one default for both stages, and a stage asks for one by repeating it bare.
FROM ${SDK} AS gate
ARG CONFIGURATION

# What the SDK image does not already have. libSkiaSharp is what the headless
# UI tests rasterize with, and it will not load at all without fontconfig
# beside it — which reads as a DllNotFoundException in every UI test rather
# than as anything to do with fonts. libX11 is Attention's ICCCM urgency hint
# on the Linux side (see Attention.cs) — XOpenDisplay already returns null and
# backs off quietly when there is no X server to answer, but the library it
# calls into still has to be there to be called. The fonts themselves are
# embedded in the application, so there is nothing else to install: no X
# server, no ICU (the projects are built InvariantGlobalization), no window
# manager.
#
# ffmpeg is the exception, and it is here for the tests rather than for the
# build. Nothing links against it and nothing published below carries it — it
# is a program Flyback looks for on PATH and does without (ADR-0089). But the
# tests that write an MP4 skip themselves when there is none, so without this
# the format most people will record in would be the one thing the gate below
# never exercises.
RUN apt-get update \
 && apt-get install --yes --no-install-recommends libfontconfig1 libx11-6 ffmpeg \
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
#
# Locked, so the packages.lock.json files committed beside each project are what
# is resolved and a restore that would need anything else fails here instead of
# quietly building against it. A version changed in Directory.Packages.props
# therefore arrives with the lock files that version produces:
#
#   dotnet restore Flyback.slnx --force-evaluate
#
# Only this restore is locked. The publishes below restore a runtime pack per
# platform, which no lock file taken without a runtime identifier describes.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore Flyback.slnx --locked-mode

# The public key the app trusts updates from, as base64 DER. Only a build on a
# developer's machine passes one: release.sh and make.sh hand in the local test
# key's, and on GitHub the committed release-key.pem stands. Kept in the
# environment, so every stage built on this one knows it is a local build.
ARG RELEASE_PUBLIC_KEY=""
ENV RELEASE_PUBLIC_KEY=${RELEASE_PUBLIC_KEY}

RUN if [ -n "${RELEASE_PUBLIC_KEY}" ]; then \
      printf -- '-----BEGIN PUBLIC KEY-----\n%s\n-----END PUBLIC KEY-----\n' "${RELEASE_PUBLIC_KEY}" \
        > src/Flyback.App/Updates/release-key.pem; \
    fi

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build Flyback.slnx -c ${CONFIGURATION} --no-restore

# The gate. Every test in the solution — the engine's, the shell's headless UI
# ones, the plugins' — and the build stops here if any of them does.
#
# --solution rather than a bare path: global.json runs `dotnet test` on
# Microsoft.Testing.Platform, which names what it is given. It prints each
# failing test to the console, so there is nothing to fish out of a log.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet test --solution Flyback.slnx -c ${CONFIGURATION} --no-build

# The same tests again, measured. A second run rather than a flag on the one
# above, because instrumentation rewrites the assemblies and several tests read
# a built assembly's bytes and assert on its metadata — coverage.runsettings
# says which ones and what it costs them. It also roughly doubles what the tests
# take, which is not a price the gate should pay for a number nothing is allowed
# to fail on.
#
# Its own stage, so nothing above waits for it: the gate is the first stage and
# the publishes build on the gate, not on this.
#
# No test takes a minute, so a test host that finishes none for fifteen has hung:
# the hang dump prints the tests it was in the middle of and ends it.
FROM gate AS measured
ARG CONFIGURATION

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet test --solution Flyback.slnx -c ${CONFIGURATION} --no-build \
      --coverage --coverage-settings coverage.runsettings --coverage-output-format cobertura \
      --hangdump --hangdump-timeout 15m --hangdump-type Mini

# The reports and nothing else, so the Coverage workflow can ask for them with
# --output and keep them beside the run.
FROM scratch AS coverage
COPY --from=measured /src/TestResults/ /

# The Figures plugin as a signed package, which the preset site starts with and
# the release carries (ADR-0141), at the release's version. The key arrives as a
# build secret and leaves no trace in any layer:
#
#   docker build --target figures --secret id=release-key,env=RELEASE_SIGNING_KEY --output dist .
#
# pack-plugin publishes the project, checks the package the way the editor
# will, loads it once, and writes nothing the editor would refuse. Version
# reaches that publish as an environment variable, which MSBuild reads as a property.
FROM gate AS packed
ARG CONFIGURATION
ARG VERSION

RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=secret,id=release-key,required=true \
    Version=${VERSION} dotnet run --project src/Flyback.Cli -c ${CONFIGURATION} --no-build -- \
      pack-plugin src/Flyback.Plugins.Figures -o /out/Flyback.Plugins.Figures.fbkp --key /run/secrets/release-key

FROM scratch AS figures
COPY --from=packed /out/ /

FROM gate AS publish
ARG RIDS
ARG CONFIGURATION
ARG VERSION

# One publish per identifier, each restoring its own runtime pack. Self-contained
# and single-file are the project's own doing rather than flags here — see
# Flyback.App.csproj, which turns both on the moment there is an identifier to
# build for. Not --no-build: a build for another platform is a different build
# from the one the tests just ran against.
#
# Three programs per platform, into one folder. The shell, the command line and
# the viewer are the same engine, the same plugin host and the same runtime
# behind three fronts, so publishing them over each other leaves one copy of all
# of it: each later publish rewrites the shared files with the same bytes and
# adds an executable, its deps.json and its runtimeconfig.json. Three folders
# would be three runtimes.
#
# The shell goes first, because on macOS its publish is what lays out the bundle
# — and after that the command line and the viewer go *inside* the bundle, where
# the payload they share now lives.
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
      dotnet publish src/Flyback.Viewer -c ${CONFIGURATION} -r ${rid} -o ${out} -p:Version=${VERSION}; \
    done

# A release as the Release workflow publishes it: a zip of each platform's
# folder, the Figures package, and SHA256SUMS with its signature. release.sh
# runs it, on GitHub and on a machine with a local test key:
#
#   docker build --target release --build-arg VERSION=1.4.0 --secret id=release-key,env=RELEASE_SIGNING_KEY --output dist .
#
# The signature is checked against the key's own public half. That half being
# release-key.pem is release.sh's check, made before anything is built.
#
# PACKAGE=folders lays each platform out as a folder to run instead of a zip,
# which is what release.sh asks for off GitHub; SHA256SUMS then lists every file.
FROM ${SDK} AS signed
ARG VERSION
ARG PACKAGE=zips

RUN apt-get update \
 && apt-get install --yes --no-install-recommends zip \
 && rm -rf /var/lib/apt/lists/*

COPY --from=publish /out/ /artifacts/
COPY --from=packed /out/ /dist/

WORKDIR /artifacts

RUN --mount=type=secret,id=release-key,required=true \
    set -eu; \
    for platform in *; do \
      case ${PACKAGE} in \
        zips) zip -qr /dist/flyback-${VERSION}-${platform}.zip ${platform} ;; \
        folders) cp -a ${platform} /dist/ ;; \
        *) echo "PACKAGE is zips or folders, not ${PACKAGE}" >&2; exit 1 ;; \
      esac; \
    done; \
    cd /dist; \
    find . -type f ! -name 'SHA256SUMS*' | sed 's|^\./||' | sort | xargs -d '\n' sha256sum > SHA256SUMS; \
    openssl dgst -sha256 -sign /run/secrets/release-key -out SHA256SUMS.sig SHA256SUMS; \
    openssl pkey -in /run/secrets/release-key -pubout -out /tmp/release-key.pem; \
    openssl dgst -sha256 -verify /tmp/release-key.pem -signature SHA256SUMS.sig SHA256SUMS

FROM scratch AS release
COPY --from=signed /dist/ /

# Nothing but the artifacts, so that `--output` writes the publish folders and
# not a filesystem around them. Last, so a build with no --target is this one.
# Publishing from Linux also means the executables
# carry their mode, which a cross-publish from Windows cannot manage — see the
# README for keeping it on the way out.
FROM scratch AS artifacts
COPY --from=publish /out/ /
