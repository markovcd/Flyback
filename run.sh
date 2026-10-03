#!/bin/bash
# Compile and run Flyback (Flyback.Editor.Desktop) in All plugins configuration.

set -e

echo "Building and running Flyback in All plugins configuration..."

dotnet run --project src/Flyback.Editor.Desktop -c "All plugins"
