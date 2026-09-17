#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COSMOS_ROOT="${ZONDERQ_COSMOS_SOURCE_ROOT:-$(cd "$ROOT_DIR/.." && pwd)}"
SMT_STAGE="${ZONDERQ_SMT_STAGE:-1}"
CACHE_ROOT="${ZONDERQ_SMT_CACHE_ROOT:-$ROOT_DIR/.cache/smt}"
NUGET_PACKAGES="${ZONDERQ_NUGET_PACKAGES:-$CACHE_ROOT/nuget-stage${SMT_STAGE}}"
NUGET_CONFIG="$CACHE_ROOT/NuGet.stage${SMT_STAGE}.Config"
LOCAL_FEED="$COSMOS_ROOT/artifacts/package/release"

fail() {
    echo "[SMT-BUILD][BLAD] $*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Brak $1 w PATH."
}

[[ "$SMT_STAGE" =~ ^[1-9][0-9]*$ ]] || fail "ZONDERQ_SMT_STAGE musi byc dodatnia liczba calkowita."
[[ -d "$COSMOS_ROOT/.git" ]] || fail "Nie znaleziono checkoutu Cosmos w: $COSMOS_ROOT. Ustaw ZONDERQ_COSMOS_SOURCE_ROOT."
[[ -f "$COSMOS_ROOT/src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs" ]] || fail "To nie wyglada na Cosmos Kernel Gen3: $COSMOS_ROOT"

require_command dotnet
require_command sha256sum

stage_patches=()
for ((stage = 1; stage <= SMT_STAGE; stage++)); do
    mapfile -t matches < <(find "$ROOT_DIR/patches/cosmos-smt" -maxdepth 1 -type f -name "$(printf '%04d' "$stage")-*.patch" | sort)
    (( ${#matches[@]} == 1 )) || fail "Nie znaleziono dokladnie jednego patcha dla etapu $stage."
    stage_patches+=("${matches[0]}")
done
PATCH_FINGERPRINT="$(cat "${stage_patches[@]}" | sha256sum | awk '{print $1}')"

mkdir -p "$CACHE_ROOT" "$NUGET_PACKAGES"
STAMP_FILE="$CACHE_ROOT/stage${SMT_STAGE}-${PATCH_FINGERPRINT}.ready"

runtime_stub_ready() {
    local stubs="$COSMOS_ROOT/src/Cosmos.Kernel.Core_Plugs/System/private/Runtime/CompilerHelpers/Stubs.cs"
    [[ -f "$stubs" ]] && grep -Fq 'RhWaitForPendingFinalizers' "$stubs"
}

local_feed_ready() {
    [[ -d "$LOCAL_FEED" ]] || return 1
    compgen -G "$LOCAL_FEED/Cosmos.SDK.3.0.85*.nupkg" >/dev/null || return 1
    compgen -G "$LOCAL_FEED/Cosmos.Kernel.3.0.85*.nupkg" >/dev/null || return 1
    compgen -G "$LOCAL_FEED/Cosmos.Kernel.HAL.X64.3.0.85*.nupkg" >/dev/null || return 1
    compgen -G "$LOCAL_FEED/Cosmos.Kernel.Boot.Limine.3.0.85*.nupkg" >/dev/null || return 1
}

if [[ ! -f "$STAMP_FILE" ]] || ! runtime_stub_ready || ! local_feed_ready; then
    echo "[SMT-BUILD] Przygotowuje patched Cosmos do etapu $SMT_STAGE..."
    echo "[SMT-BUILD] Source: $COSMOS_ROOT"
    ZONDERQ_COSMOS_SOURCE_ROOT="$COSMOS_ROOT" \
        bash "$ROOT_DIR/tools/prepare-cosmos-smt.sh" --through-stage "$SMT_STAGE"

    runtime_stub_ready || fail "Po przygotowaniu Cosmosa nadal brakuje RhWaitForPendingFinalizers."
    local_feed_ready || fail "Po przygotowaniu Cosmosa brakuje wymaganych paczek w $LOCAL_FEED."

    rm -f "$CACHE_ROOT"/stage${SMT_STAGE}-*.ready
    printf '%s\n' "$PATCH_FINGERPRINT" > "$STAMP_FILE"
else
    echo "[SMT-BUILD] Patched Cosmos stage $SMT_STAGE jest juz gotowy - pomijam przebudowe Cosmosa."
fi

cat > "$NUGET_CONFIG" <<EOF_CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-cosmos" value="$LOCAL_FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-cosmos">
      <package pattern="Cosmos.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF_CONFIG

# Cosmos ma nadal wersje 3.0.85, wiec zwykly globalny cache NuGet nie odroznia
# oficjalnych paczek od naszych patched paczek. Czyscimy tylko prywatny cache
# launchera i wymuszamy ponowne pobranie Cosmos.* z lokalnego feedu.
rm -rf "$NUGET_PACKAGES"/cosmos.*

export NUGET_PACKAGES
export PATH="$HOME/.dotnet/tools:$PATH"

echo "[SMT-BUILD] Restore ZonderqOS z lokalnego patched feedu..."
dotnet restore "$ROOT_DIR/ZonderqOS.csproj" \
    -r linux-x64 \
    -p:CosmosArch=x64 \
    --configfile "$NUGET_CONFIG" \
    --force \
    --no-cache

for package in cosmos.sdk cosmos.kernel cosmos.kernel.hal.x64 cosmos.kernel.boot.limine; do
    metadata="$NUGET_PACKAGES/$package/3.0.85/.nupkg.metadata"
    [[ -f "$metadata" ]] || fail "Brak metadata po restore: $metadata"
    grep -Fq "$LOCAL_FEED" "$metadata" || {
        echo "[SMT-BUILD][BLAD] $package 3.0.85 nie pochodzi z patched local feedu." >&2
        cat "$metadata" >&2
        exit 1
    }
done

require_command cosmos

echo "[SMT-BUILD] Paczki Cosmos zweryfikowane. Buduje x64..."
cd "$ROOT_DIR"
cosmos build -a x64

ISO="$ROOT_DIR/output-x64/ZonderqOS.iso"
[[ -f "$ISO" ]] || fail "Build zakonczyl sie bez obrazu: $ISO"
echo "[SMT-BUILD] OK: $ISO"
