# Sourced, not run: leaves RELEASE_SIGNING_KEY exported, holding the PEM the Release
# workflow reads from the secret of that name. Off GitHub it is a local test key, taken
# from the user's environment or made and kept there where there is none.
#
#   . ./release-key.sh

if [ -z "${RELEASE_SIGNING_KEY:-}" ] && [ "${GITHUB_ACTIONS:-}" != true ]; then
  # Windows keeps it in the user's environment, which a shell opened before it was set does not see.
  if command -v powershell.exe >/dev/null 2>&1; then
    RELEASE_SIGNING_KEY="$(powershell.exe -NoProfile -Command '[Environment]::GetEnvironmentVariable("RELEASE_SIGNING_KEY", "User")' | tr -d '\r')"
  fi

  if [ -z "${RELEASE_SIGNING_KEY:-}" ]; then
    RELEASE_SIGNING_KEY="$(openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 | tr -d '\r')"

    if command -v powershell.exe >/dev/null 2>&1; then
      printf '%s\n' "$RELEASE_SIGNING_KEY" | powershell.exe -NoProfile -Command \
        '[Environment]::SetEnvironmentVariable("RELEASE_SIGNING_KEY", ([Console]::In.ReadToEnd() -replace "`r", ""), "User")'
      echo "release-key: made a local test key and kept it in the user environment as RELEASE_SIGNING_KEY" >&2
    else
      printf "export RELEASE_SIGNING_KEY='%s\n'\n" "$RELEASE_SIGNING_KEY" >> "$HOME/.profile"
      echo "release-key: made a local test key and kept it in ~/.profile as RELEASE_SIGNING_KEY" >&2
    fi
  fi
fi

if [ -z "${RELEASE_SIGNING_KEY:-}" ]; then
  echo "release-key: RELEASE_SIGNING_KEY is not set" >&2
  return 1 2>/dev/null || exit 1
fi

export RELEASE_SIGNING_KEY

# Its public half as base64 DER, which a Docker build made here embeds in place of
# the committed release-key.pem. Never on GitHub, where the committed key stands.
if [ "${GITHUB_ACTIONS:-}" != true ]; then
  RELEASE_PUBLIC_KEY="$(printf '%s\n' "$RELEASE_SIGNING_KEY" | openssl pkey -pubout -outform DER | openssl base64 -A)"
  export RELEASE_PUBLIC_KEY
fi
