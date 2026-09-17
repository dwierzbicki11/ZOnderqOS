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
    fi
done

(( ${#PATCHES[@]} > 0 )) || fail "Wybrany zakres nie zawiera zadnego patcha."
SELECTED_STAGE="$(basename "${PATCHES[-1]}")"
SELECTED_STAGE=$((10#${SELECTED_STAGE%%-*}))

# Wszystkie sciezki dotykane przez wybrany prefiks. Potrzebujemy obu stron
# diffu, zeby poprawnie obslugiwac takze przyszle rename/delete patche.
mapfile -t allowed_paths < <(
    for patch in "${PATCHES[@]}"; do
        sed -n -e 's|^+++ b/||p' -e 's|^--- a/||p' "$patch"
    done | sort -u
)

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

# Preflight calego szeregu wykonujemy na osobnym worktree. Oprocz zwyklego
# git apply --check wykrywamy tez, jaki PELNY prefiks serii jest juz obecny w
# prawdziwym checkoutcie. To jest wazniejsze niz reverse --check pojedynczego
# patcha: pozniejszy etap moze legalnie zmienic ten sam fragment co wczesniejszy
# i wtedy cofniecie patcha 1 z finalnego stanu etapu 2 nie musi byc mozliwe.
preflight_dir="$(mktemp -d -t zonderq-smt-preflight.XXXXXX)"
cleanup_preflight() {
    git -C "$COSMOS_ROOT" worktree remove --force "$preflight_dir" >/dev/null 2>&1 || true
    rm -rf "$preflight_dir" >/dev/null 2>&1 || true
}
trap cleanup_preflight EXIT

git -C "$COSMOS_ROOT" worktree add --detach --quiet "$preflight_dir" "$head_sha" || \
    fail "Nie mozna utworzyc tymczasowego worktree do preflightu."

checkout_matches_preflight() {
    local path actual expected
    for path in "${allowed_paths[@]}"; do
        actual="$COSMOS_ROOT/$path"
        expected="$preflight_dir/$path"

        if [[ -e "$expected" || -L "$expected" ]]; then
            [[ -e "$actual" || -L "$actual" ]] || return 1
            cmp -s "$expected" "$actual" || return 1
        elif [[ -e "$actual" || -L "$actual" ]]; then
            return 1
        fi
    done
    return 0
}

applied_prefix=-1
if checkout_matches_preflight; then
    applied_prefix=0
fi

prefix_count=0
for patch in "${PATCHES[@]}"; do
    name="$(basename "$patch")"
    git -C "$preflight_dir" apply --check "$patch" || fail "Preflight nie przeszedl dla $name"
    git -C "$preflight_dir" apply "$patch"
    prefix_count=$((prefix_count + 1))
    echo "[SMT] PRECHECK OK: $name"

    if checkout_matches_preflight; then
        applied_prefix=$prefix_count
    fi
done

cleanup_preflight
trap - EXIT

# Obce zmiany sa blokowane. Dirty checkout jest dozwolony tylko wtedy, gdy
# dokladnie odpowiada jednemu z pelnych prefiksow 0..N naszej serii.
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

if (( applied_prefix < 0 )); then
    fail "Lokalne zmiany w sciezkach SMT nie odpowiadaja zadnemu pelnemu prefiksowi serii 0..$SELECTED_STAGE. Przywroc czysta baze albo popraw serie."
fi

echo "[SMT] Wykryty zastosowany prefiks: 0..$applied_prefix"

if (( CHECK_ONLY )); then
    echo "[SMT] CHECK-ONLY OK: baza i seria do etapu $SELECTED_STAGE sa spojne."
    exit 0
fi

# Aplikacja jest transakcyjna na poziomie zmian wykonanych w tym uruchomieniu.
# Zaczynamy dokladnie po wykrytym prefiksie, wiec ponowne wywolanie jest
# idempotentne, a przejscie np. z etapu 2 do 3 nie probuje nakladac 1/2 ponownie.
newly_applied=()
rollback_new_patches() {
    local i
    for (( i=${#newly_applied[@]}-1; i>=0; i-- )); do
        git -C "$COSMOS_ROOT" apply --reverse "${newly_applied[$i]}" >/dev/null 2>&1 || true
    done
}
trap rollback_new_patches ERR

for (( i=0; i<applied_prefix; i++ )); do
    echo "[SMT] OK (juz jest): $(basename "${PATCHES[$i]}")"
done

for (( i=applied_prefix; i<${#PATCHES[@]}; i++ )); do
    patch="${PATCHES[$i]}"
    name="$(basename "$patch")"
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
elif (( SELECTED_STAGE == 2 )); then
    echo "[SMT] Etap 2 buduje dense CPU topology; AP-y nadal pozostaja zaparkowane."
fi
