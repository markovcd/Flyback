// Flyback's C4 model. Render with scripts/diagrams.sh; see docs/diagrams/README.md.
workspace "Flyback" "A patchable synthesizer: one module graph makes a picture and a sound." {

    model {
        person = person "Person" "Builds and plays patches, records takes, shares presets and writes letters."
        pluginAuthor = person "Plugin author" "Writes a plugin against the contract, packs and signs it, and submits it."
        admin = person "Admin" "Publishes shared plugins and reads letters and reports."
        agent = person "Agent" "Builds Flyback, and drives it headless to check its work."

        group "Flyback" {
            flyback = softwareSystem "Flyback" "The editor, the viewer and flyback-cli, on the desktop, in a browser and on Android."
            presetSite = softwareSystem "Preset site" "The plugin site and preset site at flybackmodular.app: a Cloudflare Worker with D1 and R2."
        }

        github = softwareSystem "GitHub" "Releases signed builds, and runs the workflow that checks submissions." "External"
        cloudflareAccess = softwareSystem "Cloudflare Access" "Signs the admin in with a one-time PIN." "External"
        providers = softwareSystem "Assistant providers" "OpenAI-style and Gemini endpoints, and the claude and codex programs the person signed in to." "External"
        decisionModel = softwareSystem "Decision model server" "Answers typed questions with a probability, at POST /v1/systemone." "External"
        aptabase = softwareSystem "Aptabase" "Counts anonymous usage." "External"
        secretStore = softwareSystem "Secret store" "The operating system's keychain: DPAPI, Keychain or Secret Service." "External"
        ffmpeg = softwareSystem "ffmpeg" "Encodes takes and renders, and decodes MP3 samples." "External"
        devices = softwareSystem "Sound and MIDI devices" "The speakers, the sound input and MIDI controllers." "External"

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
    }

    views {
        systemContext flyback "Context" "C1: who uses Flyback and what it talks to." {
            include *
            include admin cloudflareAccess
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
            element "External" {
                background #999999
            }
        }
    }

}
