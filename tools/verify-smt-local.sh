#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

fail() {
  echo "[SMT-LOCAL][FAIL] $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Brak '$1' w PATH."
}

for cmd in git dotnet cosmos qemu-system-x86_64 python3; do
  require_command "$cmd"
done

resolve_cosmos_root() {
  if [[ -n "${ZONDERQ_COSMOS_SOURCE_ROOT:-}" ]]; then
    printf '%s\n' "$(cd "$ZONDERQ_COSMOS_SOURCE_ROOT" && pwd)"
    return
  fi

  local parent sibling
  parent="$(cd "$ROOT_DIR/.." && pwd)"
  sibling="$parent/Cosmos"

  if [[ -f "$sibling/src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs" ]]; then
    printf '%s\n' "$sibling"
    return
  fi
  if [[ -f "$parent/src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs" ]]; then
    printf '%s\n' "$parent"
    return
  fi

  fail "Nie znaleziono checkoutu Cosmos. Uruchom najpierw ./run.sh albo ustaw ZONDERQ_COSMOS_SOURCE_ROOT."
}

COSMOS_ROOT="$(resolve_cosmos_root)"
export ZONDERQ_COSMOS_SOURCE_ROOT="$COSMOS_ROOT"
PACKAGE_FEED="$COSMOS_ROOT/artifacts/package/release"
PACKAGE_CACHE="$ROOT_DIR/.nuget/smt-local-packages"
ISO="$ROOT_DIR/output-x64/ZonderqOS.iso"

echo "[SMT-LOCAL] Cosmos: $COSMOS_ROOT"
echo "[SMT-LOCAL] 1/4: preflight + Stage 1..9 + lokalne paczki Cosmos"
bash "$ROOT_DIR/tools/prepare-cosmos-smt.sh" --through-stage 9

[[ -d "$PACKAGE_FEED" ]] || fail "Brak lokalnego feedu Cosmos: $PACKAGE_FEED"

echo "[SMT-LOCAL] 2/4: restore ZonderqOS wyłącznie z patched Cosmos"
rm -rf "$PACKAGE_CACHE"
mkdir -p "$PACKAGE_CACHE"

NUGET_CONFIG="$(mktemp -t zonderq-smt-local.XXXXXX.config)"
cleanup() {
  rm -f "$NUGET_CONFIG"
}
trap cleanup EXIT

cat > "$NUGET_CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-cosmos" value="$PACKAGE_FEED" />
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
EOF

NUGET_PACKAGES="$PACKAGE_CACHE" dotnet restore "$ROOT_DIR/ZonderqOS.csproj"   -r linux-x64   -p:CosmosArch=x64   --configfile "$NUGET_CONFIG"   --force   --no-cache

for package in cosmos.sdk cosmos.kernel cosmos.kernel.hal.x64 cosmos.kernel.boot.limine; do
  metadata="$PACKAGE_CACHE/$package/3.0.85/.nupkg.metadata"
  [[ -f "$metadata" ]] || fail "Brak metadata patched package: $metadata"
  grep -Fq "$PACKAGE_FEED" "$metadata" || fail "$package nie pochodzi z lokalnego patched feedu."
done

echo "[SMT-LOCAL] 3/4: build realnego ISO x64"
rm -rf "$ROOT_DIR/obj" "$ROOT_DIR/bin" "$ROOT_DIR/output-x64"
NUGET_PACKAGES="$PACKAGE_CACHE" cosmos build -a x64 -v
[[ -s "$ISO" ]] || fail "Build nie utworzył ISO: $ISO"

echo "[SMT-LOCAL] 4/4: QEMU Stage 9 — single CPU regression"
STAGE09_QEMU_CPU_MODEL=Nehalem STAGE09_QEMU_SOCKETS=1 STAGE09_QEMU_CPUS=1 STAGE09_QEMU_CORES=1 STAGE09_QEMU_THREADS=1 STAGE09_QEMU_TIMEOUT_SECONDS=90 STAGE09_QEMU_STABLE_SECONDS=30 bash "$ROOT_DIR/tools/smoke-stage09-qemu.sh" "$ISO" "$ROOT_DIR/stage09-local-1cpu.log"

echo "[SMT-LOCAL] 4/4: QEMU Stage 9 — 4C/2T SMT"
STAGE09_QEMU_CPU_MODEL=Nehalem STAGE09_QEMU_SOCKETS=1 STAGE09_QEMU_CPUS=8 STAGE09_QEMU_CORES=4 STAGE09_QEMU_THREADS=2 STAGE09_QEMU_TIMEOUT_SECONDS=90 STAGE09_QEMU_STABLE_SECONDS=30 bash "$ROOT_DIR/tools/smoke-stage09-qemu.sh" "$ISO" "$ROOT_DIR/stage09-local-8cpu.log"

echo
echo "[SMT-LOCAL][PASS] Stage 1..9 działa lokalnie w realnym ISO/QEMU."
echo "[SMT-LOCAL][PASS] 1C/1T regression + 4C/2T SMT przeszły bez panic/triple fault/SMP warning."
echo "[SMT-LOCAL] Logi:"
echo "  $ROOT_DIR/stage09-local-1cpu.log"
echo "  $ROOT_DIR/stage09-local-8cpu.log"
