#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_DIR="$ROOT_DIR/patches/cosmos-smt"
COSMOS_ROOT="${ZONDERQ_COSMOS_SOURCE_ROOT:-$(cd "$ROOT_DIR/.." && pwd)}"
COSMOS_BASE_TAG="${ZONDERQ_COSMOS_BASE_TAG:-v3.0.85}"
PATCH_ONLY=0
CHECK_ONLY=0
ALL_STAGES=0
MAX_STAGE=1

usage() {
    cat <<'EOF'
Uzycie:
  bash tools/prepare-cosmos-smt.sh [opcje]

Opcje:
  --through-stage N  zastosuj etapy 1..N (domyslnie: 1)
  --all              zastosuj cala dostepna serie patchy
  --patch-only       nie buduj lokalnych paczek Cosmos po zastosowaniu patchy
  --check-only       tylko zweryfikuj baze i serie; niczego nie zmieniaj
  -h, --help         pokaz pomoc

Zmienne srodowiskowe:
  ZONDERQ_COSMOS_SOURCE_ROOT       sciezka do checkoutu Cosmos
  ZONDERQ_COSMOS_BASE_TAG          oczekiwana baza (domyslnie v3.0.85)
  ZONDERQ_COSMOS_ALLOW_UNPINNED=1 pozwol na inny HEAD (tylko development)

Przyklady:
  bash tools/prepare-cosmos-smt.sh --check-only
  bash tools/prepare-cosmos-smt.sh --through-stage 1 --patch-only
  bash tools/prepare-cosmos-smt.sh --through-stage 2
  bash tools/prepare-cosmos-smt.sh --all
EOF
}

fail() {
    echo "[SMT][BLAD] $*" >&2
    exit 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --through-stage)
            [[ $# -ge 2 ]] || fail "--through-stage wymaga numeru etapu."
            [[ "$2" =~ ^[1-9][0-9]*$ ]] || fail "Etap musi byc dodatnia liczba calkowita."
            MAX_STAGE="$2"
            shift 2
            ;;
        --all)
            ALL_STAGES=1
            shift
            ;;
        --patch-only)
            PATCH_ONLY=1
            shift
            ;;
        --check-only)
            CHECK_ONLY=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            fail "Nieznana opcja: $1. Uzyj --help."
            ;;
    esac
done

[[ -d "$PATCH_DIR" ]] || fail "Brak katalogu patchy: $PATCH_DIR"
[[ -d "$COSMOS_ROOT/.git" ]] || fail "COSMOS_ROOT nie jest repozytorium git: $COSMOS_ROOT"
[[ -f "$COSMOS_ROOT/src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs" ]] || \
    fail "To nie wyglada na checkout Cosmos Gen3: $COSMOS_ROOT"
[[ -f "$COSMOS_ROOT/src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s" ]] || \
    fail "Brak natywnego backendu x64 w: $COSMOS_ROOT"

mapfile -t ALL_PATCHES < <(find "$PATCH_DIR" -maxdepth 1 -type f -name '[0-9][0-9][0-9][0-9]-*.patch' | sort)
(( ${#ALL_PATCHES[@]} > 0 )) || fail "Brak numerowanych patchy w $PATCH_DIR"

PATCHES=()
UNSELECTED_PATCHES=()
expected_stage=1
for patch in "${ALL_PATCHES[@]}"; do
    name="$(basename "$patch")"
    prefix="${name%%-*}"
    stage=$((10#$prefix))

    # Jeden numer = jeden atomowy etap. Dzieki temu --through-stage N ma
    # jednoznaczne znaczenie i nie zalezy od przypadkowej kolejnosci nazw.
    if (( stage != expected_stage )); then
        fail "Seria patchy ma luke lub duplikat: oczekiwano etapu $(printf '%04d' "$expected_stage"), znaleziono $name"
    fi
    expected_stage=$((expected_stage + 1))

    if (( ALL_STAGES || stage <= MAX_STAGE )); then
        PATCHES+=("$patch")
    else
        UNSELECTED_PATCHES+=("$patch")
    fi
done

(( ${#PATCHES[@]} > 0 )) || fail "Wybrany zakres nie zawiera zadnego patcha."
SELECTED_STAGE="$(basename "${PATCHES[-1]}")"
SELECTED_STAGE=$((10#${SELECTED_STAGE%%-*}))

# Reproducible bring-up: seria jest utrzymywana i testowana wzgledem v3.0.85.
# git apply nie zmienia HEAD, wiec to sprawdzenie nadal dziala po zastosowaniu
# naszych niezacommitowanych patchy.
base_sha="$(git -C "$COSMOS_ROOT" rev-list -n 1 "$COSMOS_BASE_TAG" 2>/dev/null || true)"
[[ -n "$base_sha" ]] || fail "Brak taga/ref '$COSMOS_BASE_TAG' w checkoutcie Cosmos."
head_sha="$(git -C "$COSMOS_ROOT" rev-parse HEAD)"
if [[ "${ZONDERQ_COSMOS_ALLOW_UNPINNED:-0}" != "1" && "$head_sha" != "$base_sha" ]]; then
    fail "Cosmos HEAD=$head_sha, oczekiwano $COSMOS_BASE_TAG=$base_sha. Checkoutnij $COSMOS_BASE_TAG albo ustaw ZONDERQ_COSMOS_ALLOW_UNPINNED=1 tylko do developmentu."
fi

# Nie dotykamy indexu uzytkownika. Nasza seria zawsze pozostaje unstaged.
if ! git -C "$COSMOS_ROOT" diff --cached --quiet; then
    fail "Checkout Cosmosa ma staged changes. Commit/stash je przed SMT bring-up."
fi

echo "[SMT] Cosmos source: $COSMOS_ROOT"
echo "[SMT] Base: $COSMOS_BASE_TAG ($head_sha)"
echo "[SMT] Wybrany zakres: etap 1..$SELECTED_STAGE (${#PATCHES[@]} patch/y)"

# Etap wyzszy niz wybrany nie moze juz byc obecny w checkoutcie. Inaczej test
# etapu 1 w rzeczywistosci testowalby takze kod z etapu 2/3.
for patch in "${UNSELECTED_PATCHES[@]}"; do
    if git -C "$COSMOS_ROOT" apply --reverse --check "$patch" >/dev/null 2>&1; then
        fail "W checkoutcie jest juz zastosowany pozniejszy patch $(basename "$patch"). Do testu etapu $SELECTED_STAGE przywroc czysta baze $COSMOS_BASE_TAG."
    fi
done

# Preflight calego wybranego szeregu wykonujemy na osobnym worktree. To lapie
# zaleznosci miedzy patchami w prawidlowej kolejnosci i gwarantuje, ze walidacja
# nie pozostawi polowy serii w prawdziwym checkoutcie.
preflight_dir="$(mktemp -d -t zonderq-smt-preflight.XXXXXX)"
cleanup_preflight() {
    git -C "$COSMOS_ROOT" worktree remove --force "$preflight_dir" >/dev/null 2>&1 || true
    rm -rf "$preflight_dir" >/dev/null 2>&1 || true
}
trap cleanup_preflight EXIT

git -C "$COSMOS_ROOT" worktree add --detach --quiet "$preflight_dir" "$head_sha" || \
    fail "Nie mozna utworzyc tymczasowego worktree do preflightu."

for patch in "${PATCHES[@]}"; do
    name="$(basename "$patch")"
    git -C "$preflight_dir" apply --check "$patch" || fail "Preflight nie przeszedl dla $name"
    git -C "$preflight_dir" apply "$patch"
    echo "[SMT] PRECHECK OK: $name"
done

cleanup_preflight
trap - EXIT

# Lista sciezek nalezacych do wybranej serii. Zmiany poza nia sa zawsze obce.
mapfile -t allowed_paths < <(
    for patch in "${PATCHES[@]}"; do
        sed -n 's|^+++ b/||p' "$patch"
    done | grep -v '^/dev/null$' | sort -u
)
mapfile -t dirty_paths < <(
    {
        git -C "$COSMOS_ROOT" diff --name-only
        git -C "$COSMOS_ROOT" ls-files --others --exclude-standard
    } | sort -u
)

for dirty in "${dirty_paths[@]}"; do
    allowed=0
    for path in "${allowed_paths[@]}"; do
        if [[ "$dirty" == "$path" ]]; then
            allowed=1
            break
        fi
    done
    (( allowed )) || fail "Obca lokalna zmiana poza wybrana seria SMT: $dirty"
done

# Wykrywanie juz zastosowanego prefixu musi uwzgledniac zaleznosci miedzy
# etapami. Patch N moze zmieniac te same linie co patch N-1, przez co zwykle
# `git apply --reverse --check patchN-1` daje falszywy negatyw. Dlatego robimy
# snapshot aktualnych plikow w tymczasowym worktree i cofamy serie OD KONCA.
state_dir="$(mktemp -d -t zonderq-smt-state.XXXXXX)"
cleanup_state() {
    git -C "$COSMOS_ROOT" worktree remove --force "$state_dir" >/dev/null 2>&1 || true
    rm -rf "$state_dir" >/dev/null 2>&1 || true
}
trap cleanup_state EXIT

git -C "$COSMOS_ROOT" worktree add --detach --quiet "$state_dir" "$head_sha" || \
    fail "Nie mozna utworzyc tymczasowego worktree do wykrycia stanu patchy."

for path in "${allowed_paths[@]}"; do
    src="$COSMOS_ROOT/$path"
    dst="$state_dir/$path"
    if [[ -e "$src" || -L "$src" ]]; then
        mkdir -p "$(dirname "$dst")"
        rm -rf "$dst"
        cp -a "$src" "$dst"
    else
        rm -rf "$dst"
    fi
done

APPLIED_PATCH=()
for _ in "${PATCHES[@]}"; do
    APPLIED_PATCH+=(0)
done

for (( i=${#PATCHES[@]}-1; i>=0; i-- )); do
    patch="${PATCHES[$i]}"
    if git -C "$state_dir" apply --reverse --check "$patch" >/dev/null 2>&1; then
        git -C "$state_dir" apply --reverse "$patch"
        APPLIED_PATCH[$i]=1
    fi
done

# Zastosowane etapy musza tworzyc prefix 1..N. Stage 2 bez Stage 1 albo inna
# mieszanka oznacza uszkodzony checkout, a nie stan ktory helper ma zgadywac.
applied_count=0
seen_gap=0
for i in "${!PATCHES[@]}"; do
    if (( APPLIED_PATCH[$i] )); then
        (( seen_gap == 0 )) || fail "Wykryto etap $(basename "${PATCHES[$i]}") bez wszystkich poprzednich etapow."
        applied_count=$((applied_count + 1))
    else
        seen_gap=1
    fi
done

# Po cofnieciu wykrytego prefixu snapshot ma byc identyczny z baza. To lapie
# reczne/obce modyfikacje nawet wtedy, gdy dotykaja pliku nalezacego do patcha.
if ! git -C "$state_dir" diff --quiet || [[ -n "$(git -C "$state_dir" ls-files --others --exclude-standard)" ]]; then
    fail "Checkout Cosmosa zawiera zmiany w plikach SMT, ktore nie odpowiadaja wybranemu prefixowi patchy."
fi

cleanup_state
trap - EXIT

echo "[SMT] Wykryty zastosowany prefix: etap 1..$applied_count"

if (( CHECK_ONLY )); then
    echo "[SMT] CHECK-ONLY OK: baza i seria do etapu $SELECTED_STAGE sa spojne."
    exit 0
fi

# Aplikacja jest transakcyjna na poziomie naszej serii: jesli kolejny patch
# nie przejdzie, cofamy tylko patche nalozone w tym uruchomieniu.
newly_applied=()
rollback_new_patches() {
    local i
    for (( i=${#newly_applied[@]}-1; i>=0; i-- )); do
        git -C "$COSMOS_ROOT" apply --reverse "${newly_applied[$i]}" >/dev/null 2>&1 || true
    done
}
trap rollback_new_patches ERR

for i in "${!PATCHES[@]}"; do
    patch="${PATCHES[$i]}"
    name="$(basename "$patch")"

    if (( APPLIED_PATCH[$i] )); then
        echo "[SMT] OK (juz jest): $name"
        continue
    fi

    git -C "$COSMOS_ROOT" apply --check "$patch" || fail "Nie mozna zastosowac: $name"
    git -C "$COSMOS_ROOT" apply "$patch"
    newly_applied+=("$patch")
    echo "[SMT] Zastosowano: $name"
done
trap - ERR

if (( PATCH_ONLY )); then
    echo "[SMT] Patch-only: zakonczono na etapie $SELECTED_STAGE; pomijam budowanie paczek."
    exit 0
fi

[[ -f "$COSMOS_ROOT/.devcontainer/postCreateCommand.sh" ]] || \
    fail "Brak oficjalnego skryptu budowania paczek Cosmosa."

cat <<EOF
[SMT] Buduje lokalne paczki Cosmos 3.0.85 z seria do etapu $SELECTED_STAGE.
[SMT] Oficjalny skrypt Cosmosa wyczysci cache cosmos.*, utworzy lokalny feed
[SMT] i zainstaluje lokalne narzedzia Cosmos.Patcher/Cosmos.Tools.
EOF

(
    cd "$COSMOS_ROOT"
    export VersionPrefix=3.0.85
    bash .devcontainer/postCreateCommand.sh
)

echo
echo "[SMT] Lokalne paczki gotowe do etapu $SELECTED_STAGE."
if (( SELECTED_STAGE == 1 )); then
    echo "[SMT] Etap 1 tylko publikuje Limine MP request i pozostawia AP-y zaparkowane."
    echo "[SMT] Nie uruchamia jeszcze scheduler/GC na dodatkowych CPU."
fi
