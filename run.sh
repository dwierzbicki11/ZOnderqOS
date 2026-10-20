#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COSMOS_TAG="v3.0.85"
COSMOS_REPO="https://github.com/CosmosOS/Cosmos.git"

# Version pin protects the locally patched Stage 23 SMP runtime and htop.
# Cosmos v3.0.94 changed the keyboard API; a complete 3.0.94 runtime patch
# port is required before this pin (and SDK, CI and NuGet feed) can move.
COSMOS_VERSION="${COSMOS_TAG#v}"

fail() {
    echo "[BLAD] $*" >&2
    exit 1
}

print_help() {
    cat <<'EOF_HELP'
ZonderqOS Gen3 — launcher x64 / arm64 + QEMU

  ./run.sh                    interaktywny wybor CPU i RAM
  ./run.sh x64                uruchom x86_64
  ./run.sh arm64              uruchom ARM64
  ./run.sh x64 --cpu EPYC-Milan --cores 24 --threads 2 --ram 8G
  ./run.sh --check            sprawdz wersje SDK/SMT i checkout Cosmos, bez budowania
  ./run.sh --status           to samo co --check
  ./run.sh --help             pomoc bez pobierania Cosmos

Gdy lokalny Cosmos zawiera stare lub niepelne patche SMP:
  ZONDERQ_SMT_CLEAN_WORKTREE=1 ZONDERQ_ALLOW_TEST_BRANCH=1 ./run.sh x64
  Tworzy osobny git worktree na v3.0.85, nie resetuje oryginalnego Cosmos.

Wazne: system nadal uzywa lokalnie patchowanego Cosmos v3.0.85 z
produkcynym SMT/htop. Obsluga API KeyEvent z 3.0.94 jest przygotowana
w kodzie, ale pelna migracja kernela na 3.0.94 wymaga przeniesienia
patchy i testow QEMU. Nie zmieniaj samego COSMOS_TAG.
EOF_HELP
}

verify_cosmos_checkout_revision() {
    local checkout="$1"
    local expected actual

    expected="$(git -C "$checkout" rev-list -n 1 "$COSMOS_TAG" 2>/dev/null || true)"
    [[ -n "$expected" ]] || \
        fail "Checkout Cosmos w $checkout nie zawiera taga $COSMOS_TAG. Nie zmieniam HEAD ani lokalnych plikow."

    actual="$(git -C "$checkout" rev-parse HEAD 2>/dev/null || true)"
    [[ "$actual" == "$expected" ]] || \
        fail "Cosmos ma HEAD=${actual:-unknown}, ale SMT wymaga $COSMOS_TAG ($expected). Nie wykonuje automatycznego resetu. Zrob backup lokalnych zmian i przelacz Cosmos na $COSMOS_TAG. Cosmos 3.0.94 wymaga osobnej migracji patchy."
}

# Read-only diagnostics: no git pull, cache deletion, clone or SDK install.
run_preflight() {
    local checkout=""
    echo "[COSMOS] ZonderqOS Gen3 / runtime SMT: $COSMOS_TAG (wersja paczek $COSMOS_VERSION)"
    echo "[COSMOS] KeyEvent dla 3.0.94: kompatybilnosc API gotowa, pelny runtime jeszcze niezmigrowany."
    bash "$ROOT_DIR/tools/verify-cosmos-compat.sh" ||
        fail "Niezgodne wersje SDK, paczek lub SMT. Nie buduj systemu."

    if [[ -n "${ZONDERQ_COSMOS_SOURCE_ROOT:-}" ]]; then
        checkout="$ZONDERQ_COSMOS_SOURCE_ROOT"
        is_cosmos_checkout "$checkout" ||
            fail "Nieprawidlowy ZONDERQ_COSMOS_SOURCE_ROOT: $checkout"
    elif is_cosmos_checkout "$ROOT_DIR/../Cosmos"; then
        checkout="$ROOT_DIR/../Cosmos"
    elif is_cosmos_checkout "$ROOT_DIR/.."; then
        checkout="$ROOT_DIR/.."
    fi

    if [[ -n "$checkout" ]]; then
        verify_cosmos_checkout_revision "$checkout"
        echo "[COSMOS] Checkout: $checkout ($COSMOS_TAG)"
    else
        echo "[COSMOS] Brak lokalnego checkoutu; normalny start sprobuje pobrac $COSMOS_TAG."
    fi

    if command -v dotnet >/dev/null 2>&1; then
        echo "[TOOLS] dotnet: $(dotnet --version)"
    else
        echo "[TOOLS] Brak dotnet w PATH (wymagany do budowania)."
    fi
    if type -P cosmos >/dev/null 2>&1; then
        echo "[TOOLS] cosmos: $(type -P cosmos)"
    else
        echo "[TOOLS] Brak cosmos CLI w PATH (wymagany do budowania)."
    fi

    echo "[COSMOS] Preflight PASS (to nie jest test ISO ani QEMU)."
}

cleanup_generated_tracked_changes() {
    local dirty
    local path
    local unsafe=0
    local -a generated=()

    dirty="$(git -C "$ROOT_DIR" status --porcelain --untracked-files=no)"
    [[ -n "$dirty" ]] || return 0

    while IFS= read -r line; do
        [[ -n "$line" ]] || continue
        path="${line:3}"
        case "$path" in
            output-*/*|bin/*|obj/*|.nuget/*)
                generated+=("$path")
                ;;
            *)
                unsafe=1
                ;;
        esac
    done <<< "$dirty"

    if (( ${#generated[@]} > 0 )); then
        echo "[GIT] Cofam lokalne zmiany tylko w wygenerowanych artefaktach builda..."
        git -C "$ROOT_DIR" restore --worktree --staged -- "${generated[@]}" 2>/dev/null || \
            git -C "$ROOT_DIR" checkout -- "${generated[@]}"
    fi

    if (( unsafe )); then
        echo "[BLAD] Sa lokalne zmiany w prawdziwych plikach projektu; nie bede ich automatycznie kasowal." >&2
        git -C "$ROOT_DIR" status --short >&2
        exit 1
    fi
}

cleanup_generated_untracked_cache() {
    # Te katalogi sa w 100% generowane przez restore. Nie moga byc traktowane
    # jako recznie dodane .props/.targets przez check_untracked_build_inputs().
    rm -rf \
        "$ROOT_DIR/.nuget/smt-local-packages" \
        "$ROOT_DIR/.nuget/arm64-packages"
}

is_cosmos_checkout() {
    local path="$1"
    [[ -d "$path" ]] || return 1
    git -C "$path" rev-parse --is-inside-work-tree >/dev/null 2>&1 || return 1
    [[ -f "$path/src/Cosmos.Kernel.Core/Runtime/Stdllib.cs" ]] || return 1
}

bootstrap_cosmos_checkout() {
    local target="$1"

    command -v git >/dev/null 2>&1 || \
        fail "Brak git w PATH, a checkout Cosmos nie istnieje."

    if [[ -e "$target" ]]; then
        fail "Nie znaleziono poprawnego checkoutu Cosmos, a sciezka docelowa juz istnieje: $target"
    fi

    echo "[SMT] Nie znaleziono Cosmos. Klonuje $COSMOS_TAG do: $target"
    git clone --recursive --branch "$COSMOS_TAG" "$COSMOS_REPO" "$target"

    is_cosmos_checkout "$target" || \
        fail "Sklonowano Cosmos, ale checkout jest niekompletny: $target"
}

resolve_cosmos_root() {
    if [[ -n "${ZONDERQ_COSMOS_SOURCE_ROOT:-}" ]]; then
        is_cosmos_checkout "$ZONDERQ_COSMOS_SOURCE_ROOT" || \
            fail "ZONDERQ_COSMOS_SOURCE_ROOT nie wskazuje poprawnego checkoutu Cosmos: $ZONDERQ_COSMOS_SOURCE_ROOT"
        printf '%s\n' "$(cd "$ZONDERQ_COSMOS_SOURCE_ROOT" && pwd)"
        return 0
    fi

    local parent
    local sibling
    parent="$(cd "$ROOT_DIR/.." && pwd)"
    sibling="$parent/Cosmos"

    if is_cosmos_checkout "$sibling"; then
        printf '%s\n' "$(cd "$sibling" && pwd)"
        return 0
    fi

    if is_cosmos_checkout "$parent"; then
        printf '%s\n' "$parent"
        return 0
    fi

    bootstrap_cosmos_checkout "$sibling" >&2
    printf '%s\n' "$(cd "$sibling" && pwd)"
}

install_cosmos_wrapper() {
    local real_cosmos
    real_cosmos="$(type -P cosmos 2>/dev/null || true)"
    [[ -n "$real_cosmos" ]] || return 0

    export ZONDERQ_REAL_COSMOS="$real_cosmos"
    export ZONDERQ_ROOT_DIR="$ROOT_DIR"

    cosmos() {
        local arg
        local previous=""
        local arm64_build=0
        local package_cache

        for arg in "$@"; do
            if [[ ( "$previous" == "-a" || "$previous" == "--architecture" ) && "$arg" == "arm64" ]]; then
                arm64_build=1
            fi
            if [[ "$arg" == "--architecture=arm64" ]]; then
                arm64_build=1
            fi
            previous="$arg"
        done

        if (( arm64_build )); then
            if ! command -v dotnet >/dev/null 2>&1; then
                echo "[BLAD] Brak dotnet w PATH wymagany do ARM64 restore." >&2
                return 1
            fi

            package_cache="$ZONDERQ_ROOT_DIR/.nuget/arm64-packages"
            rm -rf "$package_cache"
            mkdir -p "$package_cache"

            echo "[ARM64] Wymuszam czysty restore linux-arm64 / CosmosArch=arm64..."
            NUGET_PACKAGES="$package_cache" dotnet restore "$ZONDERQ_ROOT_DIR/ZonderqOS.csproj" \
                -r linux-arm64 \
                -p:CosmosArch=arm64 \
                --force \
                --no-cache || return $?

            if [[ ! -d "$package_cache/cosmos.kernel.hal.arm64/$COSMOS_VERSION" ]]; then
                echo "[BLAD] Restore ARM64 nie pobral Cosmos.Kernel.HAL.ARM64 $COSMOS_VERSION." >&2
                return 1
            fi
            if [[ ! -d "$package_cache/cosmos.kernel.native.arm64/$COSMOS_VERSION" ]]; then
                echo "[BLAD] Restore ARM64 nie pobral Cosmos.Kernel.Native.ARM64 $COSMOS_VERSION." >&2
                return 1
            fi

            export NUGET_PACKAGES="$package_cache"
        fi

        "$ZONDERQ_REAL_COSMOS" "$@"
    }

    export -f cosmos
}

# These commands deliberately bypass checkout/bootstrap and disk mutations.
case "${1:-}" in
    -h|--help) print_help; exit 0 ;;
    --check|--status) run_preflight; exit 0 ;;
esac

cleanup_generated_tracked_changes

cosmos_root="$(resolve_cosmos_root)" || exit $?
[[ -n "$cosmos_root" ]] || fail "Nie udalo sie ustalic katalogu Cosmos."
verify_cosmos_checkout_revision "$cosmos_root"

# Keep the developer's Cosmos checkout untouched. Use a separate worktree keyed
# to the SMT patch CONTENT, not the ZonderqOS commit. Launcher/docs/CI changes
# must not trigger a fresh dotnet/runtime checkout.
if [[ "${ZONDERQ_SMT_CLEAN_WORKTREE:-0}" == "1" ]]; then
    require_command sha256sum
    smt_parent="$(cd "$ROOT_DIR/.." && pwd)"
    smt_patch_digest="$(cd "$ROOT_DIR" && sha256sum patches/cosmos-smt/*.patch tools/prepare-cosmos-smt.sh | sha256sum | awk '{print $1}')"
    smt_worktree="$smt_parent/.cosmos-smt-${smt_patch_digest:0:12}"
    source_common="$(git -C "$cosmos_root" rev-parse --path-format=absolute --git-common-dir)"

    # Reuse previous commit-keyed worktrees from older launchers. This also
    # allows resuming a submodule clone interrupted on its first invocation.
    if [[ ! -e "$smt_worktree" ]]; then
        for candidate in "$smt_parent"/.cosmos-smt-????????????; do
            [[ -d "$candidate" ]] || continue
            candidate_rev="${candidate##*/.cosmos-smt-}"
            [[ "$candidate_rev" =~ ^[0-9a-f]{12}$ ]] || continue
            git -C "$ROOT_DIR" rev-parse --verify "${candidate_rev}^{commit}" >/dev/null 2>&1 || continue
            git -C "$ROOT_DIR" diff --quiet "$candidate_rev" HEAD -- patches/cosmos-smt tools/prepare-cosmos-smt.sh || continue
            is_cosmos_checkout "$candidate" || continue
            candidate_common="$(git -C "$candidate" rev-parse --path-format=absolute --git-common-dir 2>/dev/null || true)"
            [[ "$candidate_common" == "$source_common" ]] || continue
            candidate_head="$(git -C "$candidate" rev-parse HEAD 2>/dev/null || true)"
            [[ "$candidate_head" == "$(git -C "$cosmos_root" rev-list -n 1 "$COSMOS_TAG")" ]] || continue
            smt_worktree="$candidate"
            echo "[SMT] Ponownie uzywam istniejacego Cosmos: $smt_worktree"
            break
        done
    fi

    if [[ ! -e "$smt_worktree" ]]; then
        echo "[SMT] Tworze izolowany Cosmos $COSMOS_TAG dla patch digest: $smt_worktree"
        git -C "$cosmos_root" worktree add --detach "$smt_worktree" "$COSMOS_TAG" || \
            fail "Nie udalo sie utworzyc izolowanego worktree."
    fi

    [[ -d "$smt_worktree" ]] && is_cosmos_checkout "$smt_worktree" || \
        fail "Docelowy katalog nie jest poprawnym checkoutem Cosmos: $smt_worktree"
    worktree_common="$(git -C "$smt_worktree" rev-parse --path-format=absolute --git-common-dir)"
    [[ "$source_common" == "$worktree_common" ]] || \
        fail "Istniejacy worktree pochodzi z innego repo. Niczego nie nadpisuje: $smt_worktree"
    verify_cosmos_checkout_revision "$smt_worktree"

    # Prefer local submodule clones from the original Cosmos checkout:
    # avoid downloading the huge dotnet/runtime repository repeatedly.
    # -c overrides only this git process; it never changes shared git config.
    submodule_options=()
    if git -C "$cosmos_root/dotnet/runtime" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
        submodule_options+=(-c "protocol.file.allow=always" -c "submodule.dotnet/runtime.url=$cosmos_root/dotnet/runtime")
        echo "[SMT] dotnet/runtime: lokalny checkout Cosmos (bez pobierania z GitHub)."
    fi
    if git -C "$cosmos_root/src/Cosmos.Kernel.Native.MultiArch/ACPI/lai" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
        submodule_options+=(-c "protocol.file.allow=always" -c "submodule.src/Cosmos.Kernel.Native.MultiArch/lai.url=$cosmos_root/src/Cosmos.Kernel.Native.MultiArch/ACPI/lai")
    fi
    git -C "$smt_worktree" "${submodule_options[@]}" submodule update --init --recursive || \
        fail "Nie mozna zainicjalizowac submodulow izolowanego Cosmos."

    echo "[SMT] Izolowany worktree: $smt_worktree (oryginal pozostal bez zmian)."
    cosmos_root="$smt_worktree"
fi
# Clear caches only after confirming the correct version of Cosmos.
cleanup_generated_untracked_cache
export ZONDERQ_COSMOS_SOURCE_ROOT="$cosmos_root"
echo "[SMT] Cosmos source: $ZONDERQ_COSMOS_SOURCE_ROOT ($COSMOS_TAG)"

install_cosmos_wrapper
exec bash "$ROOT_DIR/run-core.sh" "$@"
