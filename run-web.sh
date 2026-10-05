#!/bin/bash
# Compile and run Flyback.Editor.Web in All plugins configuration, then open in browser.

set -e

echo "Building and running Flyback.Editor.Web in All plugins configuration..."

# Run dotnet and open browser when app URL appears
dotnet run --project src/Flyback.Editor.Web -c "All plugins" 2>&1 | (
    browser_opened=false
    while IFS= read -r line; do
        echo "$line"
        if [[ $browser_opened == false && $line == *"App url:"* ]]; then
            url=$(echo "$line" | sed 's/.*App url: //' | xargs)
            if [[ $url == http://* ]]; then
                echo "Opening browser at $url"
                start "$url" 2>/dev/null &
                browser_opened=true
            fi
        fi
    done
)
