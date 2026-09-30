#!/usr/bin/env bash
# Unpacks and decompiles plugin packages for /review-plugin without running any of
# their code, and writes what needs no judgment: the facts, and the floor a verdict
# may not go under.
#
#   ./scripts/review-plugin.sh <package.fbkp | folder of them> [work folder]
#
# Prints one line per package: its name, its floor and the folder holding its facts.md.

set -euo pipefail

ILSPY_VERSION=11.1.0.9782

# What a review reads in full. A package past any of these is refused unread: size is
# where hostile code hides, and where a reading runs out of attention before it runs out of code.
MAX_PACKED=$((16 << 20))
WARN_PACKED=$((4 << 20))
MAX_UNPACKED=$((32 << 20))
MAX_FILE=$((8 << 20))
MAX_ENTRIES=64
MAX_ASSEMBLIES=8
MAX_CODE_LINES=10000

ilspy() { dotnet tool exec "ilspycmd@$ILSPY_VERSION" --yes -- "$@"; }

source=${1:?usage: review-plugin.sh <package.fbkp | folder of them> [work folder]}
work=${2:-$(mktemp -d)}
repo=$(cd "$(dirname "$0")/.." && pwd)

if [ -d "$source" ]; then
  mapfile -t packages < <(find "$source" -maxdepth 1 -type f -iname '*.fbkp' | sort)
else
  packages=("$source")
fi

[ ${#packages[@]} -gt 0 ] || { echo "review-plugin: no .fbkp in $source" >&2; exit 1; }

mkdir -p "$work"

# flyback-cli reads a package the way the editor does, and its folder holds the contract the
# decompiler resolves Flyback's types against.
dotnet build "$repo/src/Flyback.Cli/Flyback.Cli.csproj" -c Release -v q --nologo >&2
refs="$repo/src/Flyback.Cli/bin/Release/net10.0"
flyback() { dotnet "$refs/flyback-cli.dll" "$@"; }

# The lines of one array in describe's indented JSON, across every build.
listed() { awk -v key="\"$1\": [" 'index($0, key) && /\[$/ { f = 1; next } f && /^ *\],?$/ { f = 0 } f' "$2" | sed 's/^ *"//; s/",\{0,1\}$//'; }

# An assembly under one of these names is let into the host's internals, or stands in for the host.
mapfile -t host_names < <({
  sed -n 's/.*InternalsVisibleTo Include="\([^"]*\)".*/\1/p' \
    "$repo"/src/Flyback.Core/*.csproj "$repo"/src/Flyback.Plugins/*.csproj "$repo"/Directory.Build.props
  printf '%s\n' Flyback.Core Flyback.Plugins Flyback.Engine
} | tr '[:upper:]' '[:lower:]' | sort -u)

# Plugin text made safe to put in front of a reader: printable ASCII only, and no backticks to close a fence with.
clean() { LC_ALL=C sed 's/[^[:print:]\t]/?/g; s/`/'"'"'/g'; }

# What a plugin's IL names, by what it would mean. A reject is never needed by a plugin that
# makes modules and presets; a look is sometimes needed, and the reading says whether it is.
labels=(); severities=(); patterns=()
check() { severities+=("$1"); labels+=("$2"); patterns+=("$3"); }

check reject "the network" '\]System\.Net\.'
check reject "files" '\]System\.IO\.(File|FileInfo|Directory|DirectoryInfo|FileStream|FileSystemWatcher|DriveInfo|RandomAccess)(::|[^A-Za-z]|$)|\]System\.IO\.Path::GetTemp'
check reject "other programs" '\]System\.Diagnostics\.Process(StartInfo)?(::|[^A-Za-z]|$)'
check reject "the registry" '\]Microsoft\.Win32\.Registry'
check reject "the environment" '\]System\.Environment::(GetEnvironmentVariable|SetEnvironmentVariable|GetFolderPath|get_UserName|get_UserDomainName|get_MachineName|get_CommandLine|GetCommandLineArgs|Exit|FailFast|get_CurrentDirectory|set_CurrentDirectory)'
check reject "native code" 'pinvokeimpl|\]System\.Runtime\.InteropServices\.(NativeLibrary|Marshal::GetDelegateForFunctionPointer|Marshal::GetFunctionPointerForDelegate|ComWrappers)|[[:space:]]calli[[:space:]]|method unmanaged'
check reject "code it loads while running" '\]System\.Reflection\.Emit\.|\]System\.Runtime\.Loader\.|\]System\.Reflection\.Assembly::(Load[A-Za-z]*|UnsafeLoadFrom)\(|\]System\.AppDomain::(Load|ExecuteAssembly|CreateInstance)|\]System\.Type::(GetType|InvokeMember|GetTypeFromProgID|GetTypeFromCLSID)\(|\]System\.Activator::CreateInstance|\]System\.Reflection\.(MethodBase|MethodInfo|ConstructorInfo)::Invoke|\]System\.Delegate::(CreateDelegate|DynamicInvoke)|\]System\.Linq\.Expressions\.[A-Za-z`0-9<>]*::Compile|\]System\.Runtime\.CompilerServices\.RuntimeHelpers::(RunClassConstructor|RunModuleConstructor|PrepareMethod)'
check reject "access checks turned off" 'IgnoresAccessChecksToAttribute|UnsafeAccessorAttribute|DynamicMethod'
check reject "stored keys" 'IPluginRegistry::AddSecretStore|\]Flyback\.[A-Za-z.]*ISecretStore|\]System\.Security\.Cryptography\.Protected(Data|Memory)'
check reject "an obfuscator's mark" '[Oo]bfuscat|ConfusedBy|Dotfuscator|SmartAssembly|SuppressIldasm|Babel|Eazfuscator|NETReactor|dotNETProtector|CryptoObfuscator|Xenocode|DeepSea|Goliath'
check look "an assistant" 'IPluginRegistry::AddPatchAssistant|IAssistantTransport'
check look "a sound output or a MIDI input" 'IPluginRegistry::(AddAudioOutput|AddMidiInput)'
check look "reflection" '\]System\.Type::(GetField|GetFields|GetMethod|GetMethods|GetProperty|GetProperties|GetMember|GetMembers|GetNestedType|GetInterface|GetConstructor)|\]System\.Reflection\.(FieldInfo|PropertyInfo)::(GetValue|SetValue)|\]System\.Reflection\.Assembly::(GetTypes|GetExportedTypes|GetType|GetEntryAssembly|GetCallingAssembly|GetExecutingAssembly)|\]System\.AppDomain::(get_CurrentDomain|GetAssemblies)'
check look "encoded or packed data" '\]System\.Convert::FromBase64|\]System\.Convert::FromHexString|\]System\.IO\.Compression\.|\]System\.Security\.Cryptography\.'
check look "raw memory" '\]System\.Runtime\.InteropServices\.Marshal::|\]System\.Runtime\.CompilerServices\.Unsafe::|[[:space:]](localloc|cpblk|initblk)([[:space:]]|$)'
check look "threads and timers" '\]System\.Threading\.(Thread|Timer|ThreadPool)(::|[^A-Za-z]|$)|\]System\.Threading\.Tasks\.Task::(Run|Delay)|\]System\.Threading\.Tasks\.TaskFactory::StartNew'
check look "process-wide hooks" '\]System\.AppDomain::add_|ModuleInitializerAttribute|\]System\.GC::|\]System\.Console::|PosixSignalRegistration'

# Characters that only hide text from a reader: zero-width, direction overrides, a byte-order mark, raw or escaped.
invisible=$'\xe2\x80[\x8b-\x8f\xaa-\xae]|\xe2\x81[\xa0-\xa9]|\xef\xbb\xbf|\\\\u(200[b-fB-F]|202[a-eA-E]|206[0-9]|[fF][eE][fF][fF])'

# Words that turn up in text written for whoever reviews the plugin rather than for the person patching with it.
addressed='ignore (all |any )?(previous|prior|above)|disregard|instruction|system prompt|you are (an?|the|now)|as an ai|language model|\b(claude|chatgpt|gpt-?[0-9]|llm|anthropic)\b|review|approv|reject|verdict|safe to (install|accept|publish)|pre-?approved|\badmin|trusted|whitelist|allowlist|already (checked|audited|verified)'

review() {
  local package=$1 name out facts sha size
  name=$(basename "$package"); name=${name%.*}
  out="$work/$name"
  facts="$out/facts.md"
  rm -rf "$out"; mkdir -p "$out/files" "$out/src" "$out/il"

  local rejects=() looks=()
  local body="$out/.body.md"; : > "$body"
  say() { printf '%s\n' "$*" >> "$body"; }
  fence() { say '~~~text'; clean >> "$body"; say '~~~'; }

  sha=$(sha256sum "$package" | cut -d' ' -f1)
  size=$(wc -c < "$package" | tr -d ' ')

  finish() {
    {
      printf '# %s\n\n' "$(printf '%s' "$(basename "$package")" | clean)"
      printf -- '- size: %s bytes\n- sha256: %s\n- work folder: %s\n\n' "$size" "$sha" "$out"
      if [ ${#rejects[@]} -gt 0 ]; then
        printf '## Floor: Reject\n\n'
        printf -- '- %s\n' "${rejects[@]}"
      else
        printf '## Floor: none\n\nNothing mechanical rules it out. The reading decides.\n'
      fi
      printf '\n## Look closer\n\n'
      if [ ${#looks[@]} -gt 0 ]; then printf -- '- %s\n' "${looks[@]}"; else printf 'Nothing.\n'; fi
      printf '\n'
      cat "$body"
    } > "$facts"
    rm -f "$body"
    printf '%s\t%s\t%s\n' "$name" "$([ ${#rejects[@]} -gt 0 ] && echo Reject || echo none)" "$out"
  }

  # The zip, checked before a byte of it is written anywhere.
  local listing
  if ! listing=$(unzip -Z1 "$package" 2>/dev/null); then
    rejects+=("not a zip")
    finish; return
  fi

  local entries unpacked links
  entries=$(printf '%s\n' "$listing" | grep -c '' || true)
  unpacked=$(unzip -Zt "$package" 2>/dev/null | sed -n 's/.* \([0-9][0-9]*\) bytes uncompressed.*/\1/p')
  links=$(unzip -Z "$package" 2>/dev/null | awk 'NR > 1 && $1 ~ /^l/' || true)

  [ "$size" -le "$MAX_PACKED" ] || rejects+=("too big to review: $size bytes packed, over $MAX_PACKED")
  [ "$size" -le "$WARN_PACKED" ] || [ "$size" -gt "$MAX_PACKED" ] || looks+=("unusually large: $size bytes packed")
  [ "$entries" -le "$MAX_ENTRIES" ] || rejects+=("too big to review: $entries entries, over $MAX_ENTRIES")
  [ "${unpacked:-0}" -le "$MAX_UNPACKED" ] || rejects+=("too big to review: unpacks to $unpacked bytes, over $MAX_UNPACKED")
  [ -z "$links" ] || rejects+=("a symbolic link")

  if [ ${#rejects[@]} -gt 0 ]; then
    say '## Entries (not unpacked)'; say
    printf '%s\n' "$listing" | head -n 200 | fence
    finish; return
  fi

  # The editor's own reading: its refusals, the signature checked, and what the code adds and names.
  local described="$out/describe.json" refused code=0
  flyback plugin describe --json "$package" > "$described" 2>&1 || code=$?
  [ "$code" -le 1 ] || { rejects+=("flyback-cli could not read it"); finish; return; }

  refused=$(sed -n 's/^  "refused": "\(.*\)",\{0,1\}$/\1/p' "$described" | clean)
  [ -z "$refused" ] || rejects+=("the editor refuses it: $refused")
  while IFS= read -r line; do rejects+=("the editor refuses a build: $line"); done \
    < <(sed -n 's/^      "refusal": "\(.*\)",\{0,1\}$/\1/p' "$described" | clean)
  [ -n "$refused" ] || grep -q '^  "signer": ' "$described" || rejects+=("unsigned")

  while IFS= read -r reach; do
    case "$reach" in
      "the network"|files|"other programs"|"the registry"|"native code"|"code it loads while running") rejects+=("the editor says it reaches $reach") ;;
      *) looks+=("the editor says it reaches $(printf '%s' "$reach" | clean)") ;;
    esac
  done < <(listed reaches "$described" | sort -u)

  while IFS= read -r adds; do
    case "$adds" in
      modules|presets) ;;
      "a secret store") rejects+=("it adds a secret store") ;;
      *) looks+=("it adds $(printf '%s' "$adds" | clean)") ;;
    esac
  done < <(listed adds "$described" | sort -u)

  say '## What the editor reads'; say
  fence < "$described"
  say

  if [ -n "$refused" ] || ! unzip -qq "$package" -d "$out/files" 2>/dev/null; then
    [ -n "$refused" ] || rejects+=("does not unpack cleanly")
    finish; return
  fi

  local top
  top=$(cd "$out/files" && find . -mindepth 1 -maxdepth 1 | sed 's|^\./||' | grep -vxE 'win|osx|linux|any|signature\.json' || true)
  [ -z "$top" ] || rejects+=("something other than a build folder or signature.json at the top")

  # Every file, by what it is.
  say '## Entries'; say
  say '| Path | Bytes | SHA-256 | Kind |'
  say '| --- | --- | --- | --- |'

  local assemblies=() seen=" " f rel magic kind hash id
  while IFS= read -r f; do
    rel=${f#"$out/files/"}
    hash=$(sha256sum "$f" | cut -d' ' -f1)
    magic=$(head -c 4 "$f" | od -An -tx1 | tr -d ' \n')
    kind=other

    case "$magic" in
      4d5a*)
        id=$(printf '%s' "$rel" | tr '/ ' '__' | clean)
        if ilspy --dump-table Assembly --json "$f" > "$out/il/$id.assembly.json" 2> /dev/null \
          && ilspy -il "$f" > "$out/il/$id.il" 2> "$out/il/$id.err"; then
          kind=managed
          case "$seen" in
            *" $hash "*) kind="managed, the same bytes as another build's" ;;
            *) seen="$seen$hash "; assemblies+=("$id|$f") ;;
          esac
        else
          kind=native
          rm -f "$out/il/$id.assembly.json" "$out/il/$id.il" "$out/il/$id.err"
        fi
        ;;
      7f454c46|cffaedfe|cefaedfe|cafebabe|feedfacf|feedface) kind=native ;;
      *) case "$rel" in *.json) kind=config ;; esac ;;
    esac

    case "$kind" in
      native) rejects+=("native code, which cannot be decompiled: $(printf '%s' "$rel" | clean)") ;;
      other) looks+=("a file that is neither code nor config: $(printf '%s' "$rel" | clean)") ;;
    esac

    [ "$(wc -c < "$f")" -le "$MAX_FILE" ] || rejects+=("too big to review: $(printf '%s' "$rel" | clean) is over $MAX_FILE bytes")

    say "| $(printf '%s' "$rel" | clean) | $(wc -c < "$f" | tr -d ' ') | ${hash:0:16} | $kind |"
  done < <(find "$out/files" -type f | LC_ALL=C sort)
  say

  [ ${#assemblies[@]} -le "$MAX_ASSEMBLIES" ] || rejects+=("too big to review: ${#assemblies[@]} distinct assemblies, over $MAX_ASSEMBLIES")

  if printf '%s\n' "${rejects[@]}" | grep -q '^too big'; then
    finish; return
  fi

  # Each assembly: decompiled, and read for what it names.
  local entry path asm i hits errors
  for entry in "${assemblies[@]}"; do
    id=${entry%%|*}; path=${entry#*|}
    asm=$(sed -n 's/^ *"Name": "\(.*\)",$/\1/p' "$out/il/$id.assembly.json" | head -n 1 | clean)

    ilspy -p -o "$out/src/$id" -r "$refs" "$path" > "$out/src/$id.log" 2>&1 || true
    errors=$(grep -c 'Error decompiling' "$out/src/$id.log" || true)

    say "## Assembly $(printf '%s' "$asm" | clean)"; say
    say "- file: $id"
    say "- C#: src/$id ($errors types that failed to decompile; read their IL in il/$id.il)"
    say "- IL: il/$id.il"
    say "- implements IFlybackPlugin: $(grep -q '\[Flyback\.Plugins\]Flyback\.Plugins\.IFlybackPlugin' "$out/il/$id.il" && echo yes || echo no)"
    say "- references: $(ilspy --dump-table AssemblyRef --json "$path" 2>/dev/null | sed -n 's/^ *"Name": "\(.*\)",$/\1/p' | sort -u | tr '\n' ' ' | clean)"
    say

    [ "$errors" -eq 0 ] || looks+=("$asm: $errors types did not decompile to C#")

    if printf '%s\n' "${host_names[@]}" | grep -qxF "$(printf '%s' "$asm" | tr '[:upper:]' '[:lower:]')"; then
      rejects+=("$asm: an assembly name the host trusts with its internals")
    fi

    for i in "${!patterns[@]}"; do
      hits=$(grep -rnE "${patterns[$i]}" "$out/il/$id.il" "$out/src/$id" | sed "s|^$out/||" || true)
      [ -n "$hits" ] || continue
      if [ "${severities[$i]}" = reject ]; then rejects+=("$asm names ${labels[$i]}"); else looks+=("$asm names ${labels[$i]}"); fi
      say "### ${labels[$i]} (${severities[$i]})"; say
      printf '%s\n' "$hits" | head -n 30 | cut -c1-300 | fence
      say
    done

    # Obfuscation: names nobody could have typed, and code that runs the moment the assembly loads.
    hits=$(LC_ALL=C grep -nE $'^[[:space:]]*\\.(class|method|field|property|event).*[\x80-\xff]' "$out/il/$id.il" || true)
    if [ -n "$hits" ]; then
      rejects+=("$asm: identifiers outside ASCII")
      say '### identifiers outside ASCII (reject)'; say
      printf '%s\n' "$hits" | head -n 30 | fence; say
    fi

    hits=$(awk "/^\\.class .*'<Module>'/ { m = 1 } m && /\\.method/ { print NR \": \" \$0 } /^} \\/\\/ end of class <Module>/ { m = 0 }" "$out/il/$id.il")
    if [ -n "$hits" ]; then
      looks+=("$asm: code in <Module>, which runs as the assembly loads")
      say '### code in <Module> (look)'; say
      printf '%s\n' "$hits" | fence; say
    fi

    hits=$(grep -oE '__StaticArrayInitTypeSize=[0-9]+' "$out/il/$id.il" | cut -d= -f2 | sort -n | tail -n 1 || true)
    if [ -n "$hits" ] && [ "$hits" -ge 65536 ]; then
      looks+=("$asm: a $hits-byte blob baked into the code")
    fi

    hits=$(ilspy --list-resources "$path" 2>/dev/null | clean || true)
    if [ -n "$hits" ]; then
      say '### embedded resources'; say
      printf '%s\n' "$hits" | fence; say
    fi

    # Every string it holds, for reading in full.
    grep -oE 'ldstr .*' "$out/il/$id.il" | sed 's/^ldstr //' > "$out/il/$id.strings.txt" || true
    hits=$(grep -nE '^"[A-Za-z0-9+/]{64,}={0,2}"$|^.{240,}$' "$out/il/$id.strings.txt" || true)
    if [ -n "$hits" ]; then
      looks+=("$asm: long or base64-shaped strings")
      say '### long or base64-shaped strings (look)'; say
      printf '%s\n' "$hits" | head -n 20 | cut -c1-300 | fence; say
    fi
  done

  local lines
  lines=$(find "$out/src" -name '*.cs' -exec cat {} + 2>/dev/null | grep -c '' || true)
  say "## Code: $lines lines of C#"; say
  [ "$lines" -le "$MAX_CODE_LINES" ] || rejects+=("too big to review: $lines lines of decompiled C#, over $MAX_CODE_LINES")

  # Across everything that was unpacked or decompiled.
  hits=$(LC_ALL=C grep -rlE "$invisible" "$out/files" "$out/src" "$out/il" 2>/dev/null | sed "s|^$out/||" || true)
  if [ -n "$hits" ]; then
    rejects+=("zero-width or direction-changing characters, which only hide text from a reader")
    say '## Hidden characters (reject)'; say
    printf '%s\n' "$hits" | fence; say
  fi

  hits=$(grep -rniE "$addressed" "$out/src" "$out/il" --include='*.cs' --include='*.strings.txt' --include='*.resx' --include='*.json' --include='*.txt' --include='*.md' --include='*.xml' --exclude='*.assembly.json' 2>/dev/null \
    | sed "s|^$out/||" | cut -c1-240 | head -n 60 || true)
  hits+=$'\n'$(cd "$out/files" && grep -rniE "$addressed" --include='*.json' . 2>/dev/null | cut -c1-240 | head -n 20 || true)
  hits=$(printf '%s\n' "$hits" | sed '/^$/d')
  if [ -n "$hits" ]; then
    looks+=("text that may be written to whoever reviews it")
    say '## Text that may address a reviewer (look)'; say
    say 'Words only. Read every string and resource in full whatever this finds.'; say
    printf '%s\n' "$hits" | fence; say
  fi

  finish
}

for package in "${packages[@]}"; do review "$package"; done
