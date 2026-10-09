// Flyback's C4 model. Render with scripts/diagrams.sh; see docs/diagrams/README.md.
workspace "Flyback" "A patchable synthesizer: one module graph makes a picture and a sound." {

    model {
        person = person "Person" "Builds and plays patches, records takes, shares presets and writes letters."
        pluginAuthor = person "Plugin author" "Writes a plugin against the contract, packs and signs it, and submits it."
        admin = person "Admin" "Publishes shared plugins and reads letters and reports."
        agent = person "Agent" "Builds Flyback, and drives it headless to check its work."

        group "Flyback" {
            flyback = softwareSystem "Flyback" "The editor, the viewer and flyback-cli, on the desktop, in a browser and on Android." {
                editor = container "Editor" "Edits, plays and records patches, with the assistant beside them, and installs plugins and updates." ".NET 10, Avalonia" {
                    // A component named for a project is that project; ComponentDiagramTests holds its arrows to the project files.
                    desktop = component "Flyback.Editor.Desktop" "The program: startup, installing an update, and the plugins it ships." ".NET project"
                    editorLib = component "Flyback.Editor" "The window, the canvas, the inspector, the assistant column and recording, with no platform in it." ".NET project"
                    ui = component "Flyback.Ui" "What the editor and the viewer draw with: the preview, the sound device, MIDI and colors." ".NET project"
                    assist = component "Flyback.Assist" "The host's side of a conversation with an assistant." ".NET project"
                    host = component "Flyback.Host" "What every host reads: the output settings, the sizes, the saved presets." ".NET project"
                    pluginHost = component "Flyback.Plugins" "The plugin contract, and the host that loads plugins off disk." ".NET project" "Contract"
                    gpu = component "Flyback.Gpu" "The GPU renderer, its OpenGL binding and a headless context." ".NET project"
                    engine = component "Flyback.Engine" "The compiler, the four backends, the renderers, the text language and file I/O." ".NET project"
                    core = component "Flyback.Core" "The patch model, the module catalog, the opcodes and the Emitter." ".NET project" "Contract"
                    shippedPlugins = component "Shipped plugins" "Sound and MIDI, secret stores, assistants, the decision model and modules, each in plugins/<Name>/." ".NET projects"
                }
                viewer = container "flyback-viewer" "Plays a patch and writes nothing, in a window or hidden." ".NET 10, Avalonia"
                cli = container "flyback-cli" "Renders, checks and prints patches, packs plugins, and asks the assistant and the decision model." ".NET 10 console"
                webEditor = container "Web editor" "The editor in a page, with no assistant and no plugins window; opens and saves nothing." "WebAssembly, Avalonia"
                webViewer = container "Web viewer" "Plays a patch or a shared preset in a page." "WebAssembly, JavaScript, WebGL 2"
                androidEditor = container "Android editor" "The editor on a phone or a tablet, with its plugins linked in." ".NET 10, Avalonia"
                dataFolder = container "Data folder" "settings.json, allowed plugins, saved presets, conversations and recovery; the app's private folder on Android." "Files" "Store"
                pluginsFolder = container "Plugins folder" "The shipped plugins and installed plugin packages, beside the programs." "Files" "Store"
                browserStorage = container "Browser storage" "The web editor's settings and the web viewer's volume." "Local storage" "Store"
            }
            presetSite = softwareSystem "Preset site" "The plugin site and preset site at flybackmodular.app: a Cloudflare Worker with D1 and R2." {
                worker = container "Worker" "Serves the API, the pages, the media, the web editor and the web viewer, and admin.html." "TypeScript, Cloudflare Workers"
                d1 = container "Database" "Presets, plugins, tags, ratings, reports, letters and rate limits." "Cloudflare D1" "Store"
                r2 = container "Bucket" "Preset and plugin files, stills, loops, tracks, and framework files too large to be assets." "Cloudflare R2" "Store"
                flybackSite = container "flyback-site" "Checks submissions with Flyback's own readers, and pushes media and the default presets." ".NET 10 console"
            }
        }

        github = softwareSystem "GitHub" "Releases signed builds, and runs the workflow that checks submissions." "External"
        cloudflareAccess = softwareSystem "Cloudflare Access" "Signs the admin in with a one-time PIN." "External"
        providers = softwareSystem "Assistant providers" "OpenAI-style and Gemini endpoints, and the claude and codex programs the person signed in to." "External"
        decisionModel = softwareSystem "Decision model server" "Answers typed questions with a probability, at POST /v1/systemone." "External"
        aptabase = softwareSystem "Aptabase" "Counts anonymous usage." "External"
        secretStore = softwareSystem "Secret store" "The operating system's keychain: DPAPI, Keychain or Secret Service." "External"
        ffmpeg = softwareSystem "ffmpeg" "Encodes takes and renders, and decodes MP3 samples." "External"
        devices = softwareSystem "Sound and MIDI devices" "The speakers, the sound input and MIDI controllers." "External"

        // System context
        person -> flyback "Patches, plays and records in"
        person -> presetSite "Opens the web editor and the web viewer from"
        pluginAuthor -> flyback "Packs and signs plugins with" "flyback-cli"
        pluginAuthor -> presetSite "Submits plugin packages to"
        admin -> presetSite "Publishes shared plugins on"
        admin -> cloudflareAccess "Signs in through"
        agent -> flyback "Drives and checks" "flyback-cli, flyback-viewer, --json"

        flyback -> presetSite "Fetches and shares presets and plugins, sends letters to" "HTTPS"
        flyback -> github "Checks for and downloads releases from" "HTTPS"
        flyback -> providers "Sends the patch, its picture and its sound to, and takes edits from" "HTTPS, local process"
        flyback -> decisionModel "Asks typed questions of" "HTTP"
        flyback -> aptabase "Sends anonymous usage counts to" "HTTPS"
        flyback -> secretStore "Keeps API keys in"
        flyback -> ffmpeg "Encodes and decodes with" "Local process"
        flyback -> devices "Plays sound to, and hears sound input and MIDI from"

        presetSite -> github "Has submissions checked and rendered by" "HTTPS"
        cloudflareAccess -> presetSite "Guards the admin pages of"

        // Flyback's containers
        person -> editor "Patches, plays and records in"
        person -> viewer "Plays patches in"
        person -> webEditor "Patches in a browser in"
        person -> webViewer "Plays presets in a browser in"
        person -> androidEditor "Patches and plays in"
        pluginAuthor -> cli "Packs and signs plugins with" "pack-plugin"
        agent -> cli "Drives" "--json"
        agent -> viewer "Plays patches headless in" "--hidden"

        editor -> worker "Fetches and shares presets and plugins, sends letters to" "HTTPS"
        editor -> github "Checks for and downloads releases from" "HTTPS"
        editor -> providers "Asks the assistant through" "HTTPS, local process"
        editor -> decisionModel "Asks typed questions of" "HTTP"
        editor -> aptabase "Sends anonymous usage counts to" "HTTPS"
        editor -> secretStore "Keeps API keys in"
        editor -> ffmpeg "Encodes takes and decodes MP3 samples with" "Local process"
        editor -> devices "Plays sound to, and hears sound input and MIDI from"
        editor -> dataFolder "Reads and writes"
        editor -> pluginsFolder "Loads plugins from, and installs plugin packages into"

        viewer -> devices "Plays sound to, and hears sound input and MIDI from"
        viewer -> ffmpeg "Decodes MP3 samples with" "Local process"
        viewer -> dataFolder "Reads settings and saved presets from"
        viewer -> pluginsFolder "Loads plugins from"
        viewer -> editor "Hands plugin packages to"

        cli -> worker "Reads submissions to render from" "HTTPS"
        cli -> providers "Asks the assistant through" "HTTPS, local process"
        cli -> decisionModel "Asks typed questions of" "HTTP"
        cli -> secretStore "Reads API keys from"
        cli -> ffmpeg "Encodes renders with" "Local process"
        cli -> dataFolder "Reads settings, keeps conversations and allowed plugins in"
        cli -> pluginsFolder "Loads plugins from"
        cli -> viewer "Starts" "viewer"
        cli -> editor "Starts to draw a shot" "shot"

        webEditor -> worker "Fetches shared presets from" "HTTPS"
        webEditor -> webViewer "Opens a patch in"
        webEditor -> devices "Plays sound to, and hears sound input from" "Web Audio"
        webEditor -> browserStorage "Keeps its settings in"
        webViewer -> worker "Fetches presets from" "HTTPS"
        webViewer -> webEditor "Opens a patch for editing in"
        webViewer -> devices "Plays sound to, and hears sound input from" "Web Audio"
        webViewer -> browserStorage "Keeps its volume in"

        androidEditor -> worker "Fetches and shares presets from" "HTTPS"
        androidEditor -> devices "Plays sound to, and hears sound input from"
        androidEditor -> dataFolder "Reads and writes"

        // The editor's components
        desktop -> editorLib "Runs"
        editorLib -> assist "Holds conversations through"
        editorLib -> ui "Draws and plays with"
        ui -> gpu "Draws the picture with"
        ui -> host "Reads settings and presets through"
        assist -> pluginHost "Asks assistants through"
        host -> pluginHost "Uses"
        gpu -> engine "Compiles shaders with"
        pluginHost -> engine "Compiles patches with, out of a plugin's reach"
        engine -> core "Compiles"
        pluginHost -> shippedPlugins "Loads, each in its own load context"
        shippedPlugins -> pluginHost "Compiled against"
        shippedPlugins -> core "Compiled against"

        desktop -> github "Downloads releases from" "HTTPS"
        editorLib -> github "Checks for releases on" "HTTPS"
        editorLib -> worker "Fetches and shares presets and plugins, sends letters to" "HTTPS"
        editorLib -> aptabase "Sends anonymous usage counts to" "HTTPS"
        editorLib -> dataFolder "Keeps settings, layout and recovery in"
        editorLib -> pluginsFolder "Installs plugin packages into"
        assist -> dataFolder "Keeps conversations in"
        host -> dataFolder "Keeps saved presets in"
        pluginHost -> pluginsFolder "Loads plugins from"
        engine -> ffmpeg "Encodes takes and decodes MP3 samples with" "Local process"
        shippedPlugins -> providers "Asks the assistant through" "HTTPS, local process"
        shippedPlugins -> decisionModel "Asks typed questions of" "HTTP"
        shippedPlugins -> secretStore "Keeps API keys in"
        shippedPlugins -> devices "Plays sound to, and hears sound input and MIDI from"

        // The preset site's containers
        person -> worker "Browses and shares presets on" "HTTPS"
        pluginAuthor -> worker "Submits plugin packages to" "HTTPS"
        admin -> worker "Publishes shared plugins on" "admin.html"
        cloudflareAccess -> worker "Guards admin.html and the admin API of"

        worker -> d1 "Reads and writes rows in"
        worker -> r2 "Reads and writes files in"
        worker -> github "Starts the Validate workflow on" "HTTPS"
        github -> flybackSite "Runs on each submission"
        flybackSite -> worker "Sends verdicts, stills and default presets to" "HTTPS, Access service token"
    }

    views {
        systemContext flyback "Context" "C1: who uses Flyback and what it talks to." {
            include *
            include admin cloudflareAccess
            exclude "github -> presetSite"
            autolayout tb
        }

        container flyback "Desktop" "C2: Flyback's desktop programs, and where they keep what they keep." {
            include person pluginAuthor agent
            include editor viewer cli dataFolder pluginsFolder
            include presetSite github providers decisionModel aptabase secretStore ffmpeg devices
            exclude "presetSite -> github" "github -> presetSite" "person -> presetSite" "pluginAuthor -> presetSite"
            autolayout tb
        }

        container flyback "Web" "C2: Flyback in a browser and on Android." {
            include person
            include webEditor webViewer androidEditor browserStorage dataFolder
            include presetSite devices
            exclude "person -> presetSite"
            autolayout tb
        }

        component editor "Editor" "C3: the projects the editor is built from, and the plugin contract between them and the plugins." {
            include *
            exclude "github -> presetSite" "presetSite -> github"
            autolayout tb
        }

        container presetSite "Site" "C2: what the preset site runs on, and how a submission is checked." {
            include *
            exclude "flyback -> github" "person -> flyback" "pluginAuthor -> flyback"
            autolayout tb
        }

        styles {
            element "Element" {
                color #ffffff
            }
            element "Person" {
                shape person
                background #08427b
            }
            element "Software System" {
                background #1168bd
            }
            element "Container" {
                background #438dd5
            }
            element "Component" {
                background #85bbf0
                color #000000
            }
            element "Contract" {
                background #f5a623
            }
            element "Store" {
                shape cylinder
            }
            element "External" {
                background #999999
            }
        }
    }

}
