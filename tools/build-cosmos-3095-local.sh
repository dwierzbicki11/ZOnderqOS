#!/usr/bin/env bash
# Build ZOnderqOS using binaries packaged from the EXACT upstream Cosmos tag.
# This is intentionally separate from the older SMT patch builder.
set -euo pipefail

# Fail closed: archived AP/SMT experiments must never be enabled here.
for flag in ZONDERQ_3095_NATIVE_STAGE1 ZONDERQ_3095_NATIVE_STAGE2 ZONDERQ_3095_NATIVE_STAGE3 ZONDERQ_3095_NATIVE_STAGE4 ZONDERQ_COSMOS_ALLOW_UNPINNED; do
  if [[ "${!flag:-}" == "1" || "${!flag:-}" == "true" ]]; then
    echo "[3095-BUILD][ERROR] Archived SMP or unpinned runtime flag $flag is forbidden in the stock build." >&2
    exit 2
  fi
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="3.0.95"
UPSTREAM="https://github.com/CosmosOS/Cosmos.git"
WORK_ROOT="${ZONDERQ_3095_WORK_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/zonderqos-cosmos-3095}"
MODE="${1:-disk}"
case "$MODE" in
  disk|probe) ;;
  *) echo "[3095-BUILD][ERROR] Expected disk or probe" >&2; exit 2 ;;
esac
if [[ $# -gt 1 ]]; then
  echo "[3095-BUILD][ERROR] Unexpected additional arguments" >&2
  exit 2
fi

die() { printf '[3095-BUILD][ERROR] %s\n' "$*" >&2; exit 1; }
for executable in git dotnet python3 tar sha256sum clang lld nasm xorriso; do
  command -v "$executable" >/dev/null 2>&1 ||
    die "Missing $executable (on Debian/Kali: sudo apt install git clang lld nasm xorriso python3)"
done
dotnet --list-sdks | grep -Eq '^10[.]' ||
  die ".NET SDK 10 is required for Cosmos $VERSION."
[[ -z "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]] ||
  die "Tracked ZOnderqOS files have local changes. Commit or stash before packaging a reproducible image."

mkdir -p "$WORK_ROOT"
WORK_ROOT="$(cd "$WORK_ROOT" && pwd)"
SOURCE="$WORK_ROOT/Cosmos"
FEED="$SOURCE/artifacts/package/release"
ISOLATED_HOME="$WORK_ROOT/isolated-home"
SOURCE_CACHE="$WORK_ROOT/source-nuget-cache"
PROJECT_CACHE="$WORK_ROOT/project-nuget-cache"
STAGE="$WORK_ROOT/project-stage"
STAMP="$WORK_ROOT/cosmos-tag-package.stamp"
mkdir -p "$ISOLATED_HOME"

if [[ ! -e "$SOURCE" ]]; then
  echo "[3095-BUILD] Cloning exact upstream tag v$VERSION into private cache..."
  git clone --depth 1 --branch "v$VERSION" --recurse-submodules "$UPSTREAM" "$SOURCE"
fi
[[ -d "$SOURCE/.git" ]] || die "Unexpected Cosmos checkout at $SOURCE."
tag_sha="$(git -C "$SOURCE" rev-parse "v$VERSION^{commit}")" ||
  die "Missing exact tag v$VERSION in Cosmos checkout."
head_sha="$(git -C "$SOURCE" rev-parse HEAD)"
[[ "$head_sha" == "$tag_sha" ]] ||
  die "Cosmos checkout does not point at v$VERSION. Will not mix runtime versions."
[[ -z "$(git -C "$SOURCE" status --porcelain --untracked-files=no)" ]] ||
  die "Cosmos tracked files have changes. Will not build a modified tag."

# The upstream builder changes HOME/.nuget and installs global tools.
# Sandbox HOME and the source NuGet cache so Kali's real SDK and caches remain untouched.
required=(
  Cosmos.Sdk Cosmos.Kernel Cosmos.Kernel.System
  Cosmos.Kernel.Core Cosmos.Kernel.HAL.X64
)
rebuild=0
[[ -f "$STAMP" && "$(cat "$STAMP")" == "$tag_sha" ]] || rebuild=1
for package in "${required[@]}"; do
  [[ -s "$FEED/$package.$VERSION.nupkg" ]] || rebuild=1
done
if (( rebuild )); then
  # The private cache is invalid only when the exact-tag packages change.
  rm -rf -- "$PROJECT_CACHE"
  echo "[3095-BUILD] Building the matching Cosmos SDK/runtime from v$VERSION..."
  (
    cd "$SOURCE"
    HOME="$ISOLATED_HOME" \
    DOTNET_CLI_HOME="$ISOLATED_HOME" \
    NUGET_PACKAGES="$SOURCE_CACHE" \
    VersionPrefix="$VERSION" \
      bash .devcontainer/postCreateCommand.sh
  )
  for package in "${required[@]}"; do
    [[ -s "$FEED/$package.$VERSION.nupkg" ]] ||
      die "Upstream packaging did not produce $package.$VERSION.nupkg."
  done
  printf '%s\n' "$tag_sha" > "$STAMP"
else
  echo "[3095-BUILD] Reusing packages built from the verified upstream tag."
fi

# Build a clean tracked snapshot, NEVER rewrite the user's NuGet.Config or files.
# The Cosmos.Sdk resolver reads NuGet.Config before --configfile can take effect;
# therefore a project-root config is required in THIS isolated staging directory.
rm -rf -- "$STAGE"
mkdir -p "$STAGE"
git -C "$ROOT" archive HEAD | tar -xf - -C "$STAGE"

# CI-only display mode experiment: alter the disposable staged bootloader
# configuration, NEVER the checked-out production source or host partitions.
GUI_TEST_MODE="${ZONDERQ_3095_GUI_TEST_MODE:-}"
GUI_TEST_NATIVE=false
if [[ -n "$GUI_TEST_MODE" ]]; then
  [[ "$MODE" == disk ]] || die "GUI resolution probe requires disk-backed GUI"
  case "$GUI_TEST_MODE" in
    800x600|1366x768|1920x1080) ;;
    *) die "Unsupported GUI test resolution: $GUI_TEST_MODE" ;;
  esac
  python3 - "$STAGE/Bootloader/limine.conf" "$GUI_TEST_MODE" <<'PY'
import pathlib, re, sys
path = pathlib.Path(sys.argv[1])
original = path.read_text()
modified, count = re.subn(r"(?m)^([ \t]*resolution:[ \t]*)[0-9]+x[0-9]+x32[ \t]*$",
                          lambda match: match.group(1) + sys.argv[2] + "x32",
                          original)
if count != 1:
    raise SystemExit("[3095-BUILD][ERROR] Expected exactly one Limine resolution entry")
path.write_text(modified)
print("[3095-BUILD] Disposable test ISO requests " + sys.argv[2] + "x32")
PY
  GUI_TEST_NATIVE=true
fi
cat > "$STAGE/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="cosmos-local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="cosmos-local"><package pattern="Cosmos.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF

mkdir -p "$PROJECT_CACHE"
export NUGET_PACKAGES="$PROJECT_CACHE"
# Use the tooling that was just built from the exact same Cosmos tag.
export PATH="$ISOLATED_HOME/.dotnet/tools:$PATH"
export ZONDERQ_LOCAL_FEED="$FEED"
export ZONDERQ_PACKAGE_VERSION="$VERSION"

echo "[3095-BUILD] Restoring only from pinned local Cosmos packages..."
dotnet restore "$STAGE/ZonderqOS.csproj" \
  -r linux-x64 -p:CosmosArch=x64 \
  --configfile "$STAGE/NuGet.Config"

# Never trust the package version alone: nuget.org published an API-incompatible
# assembly with the same version. Check EVERY resolved Cosmos package's origin.
python3 - <<'PY'
import json, os, pathlib, sys
feed = pathlib.Path(os.environ["ZONDERQ_LOCAL_FEED"]).resolve()
cache = pathlib.Path(os.environ["NUGET_PACKAGES"])
version = os.environ["ZONDERQ_PACKAGE_VERSION"]
required = {"cosmos.sdk", "cosmos.kernel", "cosmos.kernel.system",
            "cosmos.kernel.core", "cosmos.kernel.hal.x64"}
found = set()
for package_dir in cache.glob("cosmos.*"):
    version_dir = package_dir / version
    if not version_dir.is_dir():
        continue
    metadata = version_dir / ".nupkg.metadata"
    if not metadata.is_file():
        sys.exit(f"[3095-BUILD][ERROR] Missing package provenance: {metadata}")
    source = pathlib.Path(json.loads(metadata.read_text())["source"]).resolve()
    if source != feed:
        sys.exit(f"[3095-BUILD][ERROR] Wrong Cosmos package origin: {package_dir.name}: {source}")
    found.add(package_dir.name)
if missing := required - found:
    sys.exit(f"[3095-BUILD][ERROR] Missing pinned Cosmos packages: {sorted(missing)}")
print("[3095-BUILD] Package provenance: PASS (upstream tag, private local feed)")
PY

echo "[3095-BUILD] Compiling the actual 3.0.95 keyboard contract first..."
dotnet build "$STAGE/tests/KeyboardApiSmoke/KeyboardApiSmoke.csproj" \
  -c Release -p:CosmosApiTestVersion="$VERSION" --nologo

output="$STAGE/output"
case "$MODE" in
  disk)
    read_only=false
    diagnostic=false
    output_file="ZonderqOS-3095-disk.iso"
    ;;
  probe)
    read_only=true
    diagnostic=true
    output_file="ZonderqOS-3095-usb.iso"
    ;;
esac
echo "[3095-BUILD] Publishing $MODE x64 ISO (no older Cosmos versions, no RAM root)..."
dotnet publish "$STAGE/ZonderqOS.csproj" \
  -c Debug -r linux-x64 -p:CosmosArch=x64 \
  -p:ZonderqCosmosKeyboardApi=Input \
  -p:ZonderqReadOnlyHardwareProbe="$read_only" \
  -p:ZonderqInputDiagnosticBoot="$diagnostic" \
  -p:ZonderqRamRoot=false \
  -p:ZonderqGuiTestNativeFramebuffer="$GUI_TEST_NATIVE" \
  -o "$output" --no-restore

[[ -s "$output/ZonderqOS.iso" ]] || die "Publish finished without an ISO."
mkdir -p "$ROOT/output-3095"
cp -- "$output/ZonderqOS.iso" "$ROOT/output-3095/$output_file"
echo "[3095-BUILD] ISO: $ROOT/output-3095/$output_file"
sha256sum "$ROOT/output-3095/$output_file"
if [[ "$MODE" == disk ]]; then
  echo "[3095-BUILD] WARNING: The full GUI needs its own approved FAT32 ZONDERQDATA volume."
  echo "[3095-BUILD] Never format or mount Kali's EFI partition as ZONDERQDATA."
fi
