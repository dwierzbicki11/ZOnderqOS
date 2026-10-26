#!/usr/bin/env bash
# Build ZOnderqOS using binaries packaged from the EXACT upstream Cosmos tag.
# This is intentionally separate from the older SMT patch builder.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="3.0.95"
UPSTREAM="https://github.com/CosmosOS/Cosmos.git"
STAGE1="${ZONDERQ_3095_NATIVE_STAGE1:-false}"
STAGE2="${ZONDERQ_3095_NATIVE_STAGE2:-false}"
STAGE3="${ZONDERQ_3095_NATIVE_STAGE3:-false}"
STAGE4="${ZONDERQ_3095_NATIVE_STAGE4:-false}"
# Build variants have independent source worktrees and NuGet package feeds,
# but can borrow already-downloaded objects of the exact pinned runtime.
BASE_WORK_ROOT="${ZONDERQ_3095_WORK_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/zonderqos-cosmos-3095}"
WORK_ROOT="$BASE_WORK_ROOT"
if [[ "$STAGE4" == true ]]; then
  WORK_ROOT="${WORK_ROOT}-native-stage4"
elif [[ "$STAGE3" == true ]]; then
  WORK_ROOT="${WORK_ROOT}-native-stage3"
elif [[ "$STAGE2" == true ]]; then
  WORK_ROOT="${WORK_ROOT}-native-stage2"
elif [[ "$STAGE1" == true ]]; then
  # Always use a separate private clone, feed and NuGet cache from stock 3.0.95.
  WORK_ROOT="${WORK_ROOT}-native-stage1"
fi
[[ "$STAGE1" == false || "$STAGE1" == true ]] || { echo "Invalid native Stage1 flag" >&2; exit 2; }
[[ "$STAGE2" == false || "$STAGE2" == true ]] || { echo "Invalid native Stage2 flag" >&2; exit 2; }
[[ "$STAGE3" == false || "$STAGE3" == true ]] || { echo "Invalid native Stage3 flag" >&2; exit 2; }
[[ "$STAGE4" == false || "$STAGE4" == true ]] || { echo "Invalid native Stage4 flag" >&2; exit 2; }
if [[ "$STAGE4" == true && "$STAGE3" != true ]]; then
  echo "Stage4 requires ZONDERQ_3095_NATIVE_STAGE3=true" >&2; exit 2
fi
if [[ "$STAGE3" == true && "$STAGE2" != true ]]; then
  echo "Stage3 requires ZONDERQ_3095_NATIVE_STAGE2=true" >&2; exit 2
fi
if [[ "$STAGE2" == true && "$STAGE1" != true ]]; then
  echo "Stage2 requires ZONDERQ_3095_NATIVE_STAGE1=true" >&2; exit 2
fi
MODE="${1:-disk}"
case "$MODE" in
  disk|probe) ;;
  *) echo "[3095-BUILD][ERROR] Expected disk or probe" >&2; exit 2 ;;
esac
if [[ "$STAGE1" == true && "$MODE" != probe ]]; then
  echo "[SMP3095-STAGE1][FAIL] isolated native probe only; disk profile forbidden" >&2
  exit 2
fi
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
  # Do not eagerly download the giant dotnet/runtime submodule.
  # Initialize it after checking whether the already-cached Stage1 checkout
  # contains the EXACT pinned runtime commit.
  git clone --depth 1 --branch "v$VERSION" "$UPSTREAM" "$SOURCE"
fi
[[ -d "$SOURCE/.git" ]] || die "Unexpected Cosmos checkout at $SOURCE."
tag_sha="$(git -C "$SOURCE" rev-parse "v$VERSION^{commit}")" ||
  die "Missing exact tag v$VERSION in Cosmos checkout."
head_sha="$(git -C "$SOURCE" rev-parse HEAD)"
[[ "$head_sha" == "$tag_sha" ]] ||
  die "Cosmos checkout does not point at v$VERSION. Will not mix runtime versions."
# Check and repair incomplete submodule checkouts BEFORE building packages.
# A cached clone may exist even when dotnet/runtime was never fully populated.
echo "[3095-BUILD] Checking pinned upstream submodules..."
git -C "$SOURCE" submodule sync --recursive ||
  die "Cannot synchronize pinned Cosmos submodules."

# Stage2 and Stage3 share the downloaded object database of the exact Stage1 runtime
# but NOT its working tree or patched Cosmos assemblies.
# Git verifies and checks out the gitlink from the official v3.0.95 tag.
if [[ "$STAGE2" == true || "$STAGE3" == true || "$STAGE4" == true ]]; then
  expected_runtime="$(git -C "$SOURCE" rev-parse 'HEAD:dotnet/runtime')" ||
    die "Exact Cosmos tag has no dotnet/runtime gitlink."
  runtime_donor="$BASE_WORK_ROOT-native-stage1/Cosmos/dotnet/runtime"
  if [[ -d "$runtime_donor" && ! -L "$runtime_donor" &&
        "$(git -C "$runtime_donor" rev-parse --show-toplevel 2>/dev/null || :)" == "$(realpath -e "$runtime_donor")" ]] &&
        git -C "$runtime_donor" cat-file -e "$expected_runtime^{commit}" 2>/dev/null; then
    echo "[3095-BUILD] Reusing pinned dotnet/runtime Git objects from Stage1 cache (no full redownload)."
    git -C "$SOURCE" submodule update --init --checkout \
      --reference "$runtime_donor" -- dotnet/runtime ||
      die "Cannot initialize Stage2 runtime from cached pinned objects."
    echo "[3095-BUILD] Stage2 runtime uses Git alternates from Stage1; keep Stage1 cache available."
  else
    echo "[3095-BUILD] Stage1 runtime objects unavailable; normal pinned-submodule download required once."
  fi
fi
if ! git -C "$SOURCE" submodule update --init --recursive --checkout; then
  if [[ "$STAGE1" != true ]]; then
    die "Cannot populate pinned upstream submodules; refusing automatic recovery outside the Stage1 private workspace."
  fi
  echo "[3095-BUILD] Submodule initialization failed; checking for broken dotnet/runtime HEAD..."
  bash "$ROOT/tools/repair-cosmos-3095-runtime-submodule.sh" "$SOURCE" "$WORK_ROOT" ||
    die "Safe submodule repair refused or failed; preserved cached files and did not reset any repository."
  git -C "$SOURCE" submodule update --init --recursive --checkout ||
    die "Pinned upstream submodules still unavailable after runtime repair."
fi
submodule_state="$(git -C "$SOURCE" submodule status --recursive)" ||
  die "Cannot inspect pinned submodule status."
if printf '%s\n' "$submodule_state" | grep -Eq '^[-+U]'; then
  die "A Cosmos submodule is uninitialized, not pinned, or conflicted."
fi
required_runtime_sources=(
  dotnet/runtime/src/coreclr/nativeaot/System.Private.StackTraceMetadata/src/Internal/StackTraceMetadata/MethodNameFormatter.cs
  dotnet/runtime/src/coreclr/tools/Common/Internal/Runtime/MetadataBlob.cs
  dotnet/runtime/src/coreclr/tools/Common/Internal/Runtime/MethodTable.Constants.cs
  dotnet/runtime/src/coreclr/tools/Common/Internal/Runtime/RuntimeConstants.cs
  dotnet/runtime/src/coreclr/nativeaot/Common/src/Internal/Runtime/MethodTable.cs
)
for runtime_file in "${required_runtime_sources[@]}"; do
  if [[ ! -f "$SOURCE/$runtime_file" ]]; then
    # A prior interrupted checkout can leave tracked submodule files missing
    # even when git submodule update sees the correct commit. Restore ONLY
    # these absent paths from the submodule's pinned HEAD, never reset --hard.
    relative="${runtime_file#dotnet/runtime/}"
    git -C "$SOURCE/dotnet/runtime" cat-file -e "HEAD:$relative" ||
      die "Pinned dotnet/runtime commit does not contain $relative"
    echo "[3095-BUILD] Restoring missing pinned runtime source: $relative"
    git -C "$SOURCE/dotnet/runtime" restore --source=HEAD -- "$relative" ||
      die "Cannot safely restore missing runtime source: $relative"
  fi
  [[ -f "$SOURCE/$runtime_file" ]] ||
    die "Pinned dotnet/runtime is incomplete: missing $runtime_file"
done
echo "[3095-BUILD] Pinned dotnet/runtime sources: PASS"

if [[ "$STAGE1" != true ]]; then
  [[ -z "$(git -C "$SOURCE" status --porcelain --untracked-files=no)" ]] ||
    die "Cosmos tracked files have changes. Will not build a modified tag."
fi

# Stage 1 is an explicit and isolated source overlay, not a vanilla tag package.
# Never replay an older SMP patch or package cache.
stamp_value="$tag_sha"
if [[ "$STAGE4" == true ]]; then
  python3 "$ROOT/tools/port-cosmos-3095-stage4.py" "$SOURCE" --apply
  overlay_hash="$(sha256sum "$ROOT/tools/port-cosmos-3095-stage1.py" "$ROOT/tools/port-cosmos-3095-stage2.py" "$ROOT/tools/port-cosmos-3095-stage3.py" "$ROOT/tools/port-cosmos-3095-stage4.py" | sha256sum | cut -d' ' -f1)"
  stamp_value="$tag_sha:smp3095-native-stage4:$overlay_hash"
  echo "[SMP3095-STAGE4] Native AP bootstrap + park, no managed scheduling, QEMU ONLY."
elif [[ "$STAGE3" == true ]]; then
  python3 "$ROOT/tools/port-cosmos-3095-stage3.py" "$SOURCE" --apply
  overlay_hash="$(sha256sum "$ROOT/tools/port-cosmos-3095-stage1.py" "$ROOT/tools/port-cosmos-3095-stage2.py" "$ROOT/tools/port-cosmos-3095-stage3.py" | sha256sum | cut -d' ' -f1)"
  stamp_value="$tag_sha:smp3095-native-stage3:$overlay_hash"
  echo "[SMP3095-STAGE3] BSP GS identity + native AP stack reservations, no AP startup."
elif [[ "$STAGE2" == true ]]; then
  python3 "$ROOT/tools/port-cosmos-3095-stage2.py" "$SOURCE" --apply
  overlay_hash="$(sha256sum "$ROOT/tools/port-cosmos-3095-stage1.py" "$ROOT/tools/port-cosmos-3095-stage2.py" | sha256sum | cut -d' ' -f1)"
  stamp_value="$tag_sha:smp3095-native-stage2:$overlay_hash"
  echo "[SMP3095-STAGE2] Dense logical IDs diagnostic; APs remain parked."
elif [[ "$STAGE1" == true ]]; then
  python3 "$ROOT/tools/port-cosmos-3095-stage1.py" "$SOURCE" --apply
  overlay_hash="$(sha256sum "$ROOT/tools/port-cosmos-3095-stage1.py" | cut -d' ' -f1)"
  stamp_value="$tag_sha:smp3095-native-stage1:$overlay_hash"
  echo "[SMP3095-STAGE1] Applied reviewed overlay to disposable 3.0.95 source; APs remain parked."
fi

# The upstream builder changes HOME/.nuget and installs global tools.
# Sandbox HOME and the source NuGet cache so Kali's real SDK and caches remain untouched.
required=(
  Cosmos.Sdk Cosmos.Kernel Cosmos.Kernel.System
  Cosmos.Kernel.Core Cosmos.Kernel.HAL.X64
)
rebuild=0
[[ -f "$STAMP" && "$(cat "$STAMP")" == "$stamp_value" ]] || rebuild=1
for package in "${required[@]}"; do
  [[ -s "$FEED/$package.$VERSION.nupkg" ]] || rebuild=1
done
if (( rebuild )); then
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
  printf '%s\n' "$stamp_value" > "$STAMP"
else
  echo "[3095-BUILD] Reusing packages built from the verified upstream tag."
fi

# Build a clean tracked snapshot, NEVER rewrite the user's NuGet.Config or files.
# The Cosmos.Sdk resolver reads NuGet.Config before --configfile can take effect;
# therefore a project-root config is required in THIS isolated staging directory.
rm -rf -- "$STAGE"
mkdir -p "$STAGE"
git -C "$ROOT" archive HEAD | tar -xf - -C "$STAGE"
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

# The project package cache is private to this exact 3.0.95 build variant.
# Keep it when the pinned Cosmos packages are unchanged. Rebuilding packages
# under the same NuGet version *must* invalidate their previously extracted
# copies, otherwise restore could silently reuse stale native assemblies.
if (( rebuild )); then
  echo "[3095-BUILD] Cosmos packages rebuilt; invalidating isolated project NuGet cache."
  rm -rf -- "$PROJECT_CACHE"
else
  echo "[3095-BUILD] Reusing isolated project NuGet cache (pinned packages unchanged)."
fi
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
    if [[ "$STAGE4" == true ]]; then
      output_file="ZonderqOS-3095-smp-stage4-probe.iso"
    elif [[ "$STAGE3" == true ]]; then
      output_file="ZonderqOS-3095-smp-stage3-probe.iso"
    elif [[ "$STAGE2" == true ]]; then
      output_file="ZonderqOS-3095-smp-stage2-probe.iso"
    elif [[ "$STAGE1" == true ]]; then
      output_file="ZonderqOS-3095-smp-stage1-probe.iso"
    fi
    ;;
esac
echo "[3095-BUILD] Publishing $MODE x64 ISO (no older Cosmos versions, no RAM root)..."
dotnet publish "$STAGE/ZonderqOS.csproj" \
  -c Debug -r linux-x64 -p:CosmosArch=x64 \
  -p:ZonderqCosmosKeyboardApi=Input \
  -p:ZonderqReadOnlyHardwareProbe="$read_only" \
  -p:ZonderqInputDiagnosticBoot="$diagnostic" \
  -p:ZonderqRamRoot=false \
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
