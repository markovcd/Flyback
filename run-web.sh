#!/bin/bash
# Compile and run Flyback.Editor.Web in All plugins configuration.

set -e

echo "Building and running Flyback.Editor.Web in All plugins configuration..."

dotnet run --project src/Flyback.Editor.Web -c "All plugins"
