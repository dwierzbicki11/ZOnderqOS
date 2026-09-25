#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_DIR="$ROOT_DIR/patches/cosmos-smt"
COSMOS_BASE_TAG="${ZONDERQ_COSMOS_BASE_TAG:-v3.0.85}"
COSMOS_REPO="${ZONDERQ_COSMOS_REPO:-https://github.com/CosmosOS/Cosmos.git}"
PATCH_ONLY=0
CHECK_ONLY=0
ALL_STAGES=0
MAX_STAGE=1

fail() {
    echo "[SMT][BLAD] $*" >&2
    exit 1
}

is_cosmos_checkout() {
    local path="$1"
    [[ -d "$path" ]] || return 1
    git -C "$path" rev-parse --is-inside-work-tree >/dev/null 2>&1 || return 1
    [[ -f "$path/src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs" ]] || return 1
    [[ -f "$path/src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s" ]] || return 1
}

bootstrap_cosmos_checkout() {
    local target="$1"

    command -v git >/dev/null 2>&1 || fail "Brak git w PATH, a checkout Cosmos nie istnieje."

    if [[ -e "$target" ]]; then
        fail "Nie znaleziono poprawnego checkoutu Cosmos, a sciezka docelowa juz istnieje: $target"
    fi

    echo "[SMT] Nie znaleziono checkoutu Cosmos. Klonuje $COSMOS_BASE_TAG do: $target" >&2
    git clone --recursive --branch "$COSMOS_BASE_TAG" "$COSMOS_REPO" "$target" >&2
    is_cosmos_checkout "$target" || fail "Sklonowano Cosmos, ale checkout jest niekompletny: $target"
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

    # Przy typowym layoucie /mnt/CosmosKernel/ZonderqOS checkout Cosmosa jest
    # siblingiem /mnt/CosmosKernel/Cosmos. Jesli jeszcze go nie ma, tworzymy go
    # wlasnie tam, zamiast blednie traktowac /mnt/CosmosKernel jako repo git.
    bootstrap_cosmos_checkout "$sibling"
    printf '%s\n' "$(cd "$sibling" && pwd)"
}

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
  ZONDERQ_COSMOS_REPO              alternatywny URL repo Cosmos

Przyklady:
  bash tools/prepare-cosmos-smt.sh --check-only
  bash tools/prepare-cosmos-smt.sh --through-stage 1 --patch-only
  bash tools/prepare-cosmos-smt.sh --through-stage 2
  bash tools/prepare-cosmos-smt.sh --through-stage 3
  bash tools/prepare-cosmos-smt.sh --through-stage 4
  bash tools/prepare-cosmos-smt.sh --through-stage 5
  bash tools/prepare-cosmos-smt.sh --through-stage 6
  bash tools/prepare-cosmos-smt.sh --through-stage 7
  bash tools/prepare-cosmos-smt.sh --through-stage 8
  bash tools/prepare-cosmos-smt.sh --through-stage 9
  bash tools/prepare-cosmos-smt.sh --through-stage 10
  bash tools/prepare-cosmos-smt.sh --through-stage 11
  bash tools/prepare-cosmos-smt.sh --through-stage 12
  bash tools/prepare-cosmos-smt.sh --through-stage 13
  bash tools/prepare-cosmos-smt.sh --through-stage 14
  bash tools/prepare-cosmos-smt.sh --through-stage 15
  bash tools/prepare-cosmos-smt.sh --through-stage 16
  bash tools/prepare-cosmos-smt.sh --through-stage 17
  bash tools/prepare-cosmos-smt.sh --through-stage 18
  bash tools/prepare-cosmos-smt.sh --through-stage 19
  bash tools/prepare-cosmos-smt.sh --all
EOF
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
COSMOS_ROOT="$(resolve_cosmos_root)"
[[ -n "$COSMOS_ROOT" ]] || fail "Nie udalo sie ustalic katalogu Cosmos."
is_cosmos_checkout "$COSMOS_ROOT" || fail "To nie wyglada na checkout Cosmos Gen3: $COSMOS_ROOT"

mapfile -t ALL_PATCHES < <(find "$PATCH_DIR" -maxdepth 1 -type f -name '[0-9][0-9][0-9][0-9]-*.patch' | sort)
(( ${#ALL_PATCHES[@]} > 0 )) || fail "Brak numerowanych patchy w $PATCH_DIR"

PATCHES=()
UNSELECTED_PATCHES=()
expected_stage=1
for patch in "${ALL_PATCHES[@]}"; do
    name="$(basename "$patch")"
    prefix="${name%%-*}"
    stage=$((10#$prefix))

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

base_sha="$(git -C "$COSMOS_ROOT" rev-list -n 1 "$COSMOS_BASE_TAG" 2>/dev/null || true)"
[[ -n "$base_sha" ]] || fail "Brak taga/ref '$COSMOS_BASE_TAG' w checkoutcie Cosmos."
head_sha="$(git -C "$COSMOS_ROOT" rev-parse HEAD)"
if [[ "${ZONDERQ_COSMOS_ALLOW_UNPINNED:-0}" != "1" && "$head_sha" != "$base_sha" ]]; then
    fail "Cosmos HEAD=$head_sha, oczekiwano $COSMOS_BASE_TAG=$base_sha. Checkoutnij $COSMOS_BASE_TAG albo ustaw ZONDERQ_COSMOS_ALLOW_UNPINNED=1 tylko do developmentu."
fi

if ! git -C "$COSMOS_ROOT" diff --cached --quiet; then
    fail "Checkout Cosmosa ma staged changes. Commit/stash je przed SMT bring-up."
fi

echo "[SMT] Cosmos source: $COSMOS_ROOT"
echo "[SMT] Base: $COSMOS_BASE_TAG ($head_sha)"
echo "[SMT] Wybrany zakres: etap 1..$SELECTED_STAGE (${#PATCHES[@]} patch/y)"

for patch in "${UNSELECTED_PATCHES[@]}"; do
    if git -C "$COSMOS_ROOT" apply --reverse --check "$patch" >/dev/null 2>&1; then
        fail "W checkoutcie jest juz zastosowany pozniejszy patch $(basename "$patch"). Do testu etapu $SELECTED_STAGE przywroc czysta baze $COSMOS_BASE_TAG."
    fi
done

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
elif (( SELECTED_STAGE == 2 )); then
    echo "[SMT] Etap 2 buduje gesty CpuId/APIC map; AP-y nadal pozostaja zaparkowane."
elif (( SELECTED_STAGE == 3 )); then
    echo "[SMT] Etap 3 wlacza GS-local storage na BSP i weryfikuje pierwszy tick LAPIC; AP-y nadal pozostaja zaparkowane."
elif (( SELECTED_STAGE == 4 )); then
    echo "[SMT] Etap 4 uruchamia AP-y w natywnym entry, przydziela osobne stosy i parkuje je po READY."
    echo "[SMT] Scheduler, przerwania i managed runtime pozostaja wylaczone na AP-ach."
elif (( SELECTED_STAGE == 5 )); then
    echo "[SMT] Etap 5 wykonuje ograniczony test pracy na kazdym AP i sprawdza osobne liczniki."
    echo "[SMT] Po tescie AP-y sa parkowane; scheduler, przerwania i managed runtime pozostaja wylaczone na AP-ach."
elif (( SELECTED_STAGE == 6 )); then
    echo "[SMT] Etap 6 wysyla fixed IPI do kazdego AP i sprawdza natywne potwierdzenie z EOI."
    echo "[SMT] AP-y pozostaja poza managed runtime i schedulerem; test korzysta z wlasnego wektora IDT."
elif (( SELECTED_STAGE == 7 )); then
    echo "[SMT] Etap 7 wykonuje powtarzalny native fixed-IPI rendezvous z kazdym AP."
    echo "[SMT] AP-y wracaja do natywnego idle/HLT; scheduler i managed runtime nadal pozostaja na BSP."
elif (( SELECTED_STAGE == 8 )); then
    echo "[SMT] Etap 8 budzi zaparkowane AP-y fixed IPI i wykonuje natywna komende po powrocie z HLT."
    echo "[SMT] Scheduler, GC i managed runtime nadal pozostaja wylaczane na AP-ach."
elif (( SELECTED_STAGE == 9 )); then
    echo "[SMT] Etap 9 uruchamia okresowy LAPIC timer osobno na kazdym AP i potwierdza kilka tickow."
    echo "[SMT] Timer jest potem zatrzymywany; scheduler i GC nadal nie wchodza na AP-y."
elif (( SELECTED_STAGE == 10 )); then
    echo "[SMT] Etap 10 wykonuje wielokrotny native GC stop-the-world rendezvous przez IPI 0xF3."
    echo "[SMT] AP-y publikuja kontekst, ACK epoki, parkuja z IF=0 i wracaja dopiero po release BSP."
    echo "[SMT] To jest fundament pod SMP-safe GarbageCollector; managed GC nie jest jeszcze uruchamiany na AP-ach."
elif (( SELECTED_STAGE == 11 )); then
    echo "[SMT] Etap 11 podpina prawdziwy OrionGC pod native stop-the-world z etapu 10."
    echo "[SMT] GarbageCollector.Collect zatrzymuje wszystkie AP-y przed mark/sweep i wznawia je w finally."
    echo "[SMT] Boot proof wykonuje trzy realne kolekcje i sprawdza przezycie zarzadzanej referencji."
elif (( SELECTED_STAGE == 12 )); then
    echo "[SMT] Etap 12 budzi AP-y do pojedynczego, alokacji-bezplatnego eksportu C# bez schedulera."
    echo "[SMT] Managed entry czyta GS-local CpuId, publikuje deterministyczny checksum i wraca do native HLT."
    echo "[SMT] BSP waliduje wynik oraz brak pozostalej komendy; scheduler i managed Thread nadal nie sa wlaczane na AP-ach."
elif (( SELECTED_STAGE == 13 )); then
    echo "[SMT] Etap 13 wykonuje jednorazowy handshake per-CPU z juz zainicjalizowanym schedulerem."
    echo "[SMT] BSP wykonuje enter/leave na kazdym PerCpuState, a kazdy AP waliduje swoja tozsamosc CPU-local i wraca do native HLT."
    echo "[SMT] Nie jest to jeszcze pelny scheduler SMP, migracja watkow ani preempcja."
elif (( SELECTED_STAGE == 14 )); then
    echo "[SMT] Etap 14 utrzymuje AP-y w alokacji-bezplatnej petli managed z wlaczonymi przerwaniami."
    echo "[SMT] Prawdziwy OrionGC zatrzymuje i wznawia te konteksty przez IPI 0xF3, a heartbeat potwierdza dalsze wykonanie po GC."
    echo "[SMT] AP-y wracaja potem do native HLT; managed Thread, dispatch, migracja i preempcja nadal nie sa wlaczone."
elif (( SELECTED_STAGE == 15 )); then
    echo "[SMT] Etap 15 przypisuje natywne stosy bootstrap AP do zarejestrowanych watkow idle z jawnymi granicami."
    echo "[SMT] OrionGC skanuje kazdy zdalny stos od RSP przechwyconego przez IPI 0xF3 i weryfikuje fingerprint kanarkow."
    echo "[SMT] AP-y wracaja do native HLT; dispatch zwyklych watkow, migracja i preempcja nadal nie sa wlaczone."
elif (( SELECTED_STAGE == 16 )); then
    echo "[SMT] Etap 16 kolejkuje prawdziwy SchedulerThread osobno na kazdy AP i przelacza go przez zwykla sciezke IRQ."
    echo "[SMT] Worker wykonuje sie na przydzielonym stosie, przechodzi pelny lifecycle exit i wraca timerem LAPIC do idle."
    echo "[SMT] Dispatch jest jeszcze sekwencyjny; wspolbiezne kolejki, migracja i pelna preempcja beda utwardzane dalej."
elif (( SELECTED_STAGE == 17 )); then
    echo "[SMT] Etap 17 wywlaszcza zywy SchedulerThread timerem LAPIC osobno na kazdym AP."
    echo "[SMT] Worker wraca do idle, jest ponownie wybierany i wznawia dokladnie zapisany kontekst przerwania."
    echo "[SMT] Watki pozostaja przypiete; migracja, wspolbiezne kolejki i load balancing beda utwardzane dalej."
elif (( SELECTED_STAGE == 18 )); then
    echo "[SMT] Etap 18 uruchamia po dwa przypiete workery na kazdym AP jako jedna wspolbiezna generacje."
    echo "[SMT] Per-CPU kolejki przechodza wielokrotna preempcje, a OrionGC zatrzymuje wszystkie aktywne AP-y."
    echo "[SMT] Globalny rejestr watkow jest synchronizowany i musi wrocic do stanu wyjsciowego po tescie."
elif (( SELECTED_STAGE == 19 )); then
    echo "[SMT] Etap 19 tworzy niezbalansowana kolejke na AP1 i rozprowadza gotowe, nieprzypiete watki na wszystkie AP-y."
    echo "[SMT] Kazdy zmigrowany worker musi wykonac sie na docelowym CPU, przejsc preempcje i wrocic do idle."
    echo "[SMT] OrionGC zatrzymuje aktywna generacje po migracji, a rejestr watkow musi wrocic do stanu wyjsciowego."
fi
