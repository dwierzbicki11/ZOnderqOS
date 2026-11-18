# ZOnderqOS — Issue #212: inwentaryzacja gałęzi przed Beta 1

Stan weryfikowany **2026-11-18** przez GitHub porównaniem `main...branch`.
Baza: `main=b9f80f6cb784ebcc20a48088ea6abb49595ffbcd`.
Tag `v1.0.2-alpha.1` istnieje i wskazuje dokładnie na tę rewizję.

## Stan wydania — warunek przed integracją

W GitHubie Issue #211 jest nadal **OPEN**; brak opublikowanego GitHub Release oraz zakończonego release CI z artefaktem dla tego commita. AGY zgłosił test fizycznego obrazu ISO (QEMU/OVMF) i poprawioną idempotencję; **bez opublikowanego, sprawdzalnego artefaktu nie traktować CI jako PASS**.
Przed scalaniem zmian wykonawca ma zamknąć brakującą formalną walidację wydania (nie ponawiać godzinnego RAM soak bez zmiany kernela).

## Wynik porównań

Przeanalizowano **28 gałęzi oprócz `main`**:
- **7** gałęzi `behind` / `ahead=0`: ich commity są już przodkami `main` (nie ma czego scalać);
- **21** `diverged`: zawierają co najmniej jeden nieprzeniesiony commit oraz nie mają nowszej bazy;
- **11 otwartych draft PR**: żadnego nie scalać hurtowo.
Wskaźniki `ahead/behind` poniżej oznaczają historię Git, **nie** liczbę zmian wartych przeniesienia ani wynik testów.

### Już zawarte w main — nie scalać drugi raz

| Branch | Ahead | Behind | Decyzja |
|---|---:|---:|---|
| `feat/p2-ram-oriongc-integrated` | 0 | 43 | Already in main |
| `feature/real-usb-updater` | 0 | 62 | Already in main |
| `fix/alpha1-complete-audit` | 0 | 54 | Already in main |
| `fix/alpha1-e2e-regression` | 0 | 40 | Already in main |
| `fix/alpha1-final-iso-bootguard` | 0 | 1 | Already in main |
| `fix/alpha-hardware-idle-memory` | 0 | 50 | Already in main |
| `fix/issue-211-alpha1-release` | 0 | 4 | Already in main |

### Gałęzie rozbieżne — kwalifikacja przed testami

| Branch | Ahead | Behind | Wstępne działanie |
|---|---:|---:|---|
| `archive/smp-paused-2026-10-26` | 62 | 166 | ARCHIVE, nie scalać |
| `feat/cosmos-3095-managed-smp-stage23` | 62 | 166 | BLOCK: SMP/SMT eksperymentalne |
| `feat/cosmos-3095-migration` | 7 | 166 | LEGACY: SDK 3.0.95 |
| `feat/cosmos-3095-upstream-bsp-only` | 13 | 166 | LEGACY: stary runtime, ręczny wybór zmian |
| `diag/failed-unlock-failpath-ram` | 7 | 44 | Diagnostyka QEMU; przenosić tylko bezpieczne instrumentarium |
| `experiment/oriongc-qemu-collect-probe` | 19 | 50 | EXPERIMENT; zakaz produkcyjnego ręcznego GC |
| `feat/audio-gen3` | 594 | 165 | Beta audio; porównać i portować modułowo, PR #194 |
| `feat/games-framework-gen3` | 15 | 165 | Beta gry; przenieść na świeży branch z testami |
| `feat/gui-responsive-gen3` | 842 | 165 | Beta GUI; duża stara historia — selektywny port |
| `feat/memory-optimizer-10s` | 13 | 50 | Beta RAM; PR #201, integracja po testach |
| `feat/memory-optimizer-main-port` | 1 | 54 | Porównać z #201, unikać duplikatów |
| `feat/storage-gen3` | 38 | 101 | Beta storage; PR #196, ochrona ZONDERQDATA |
| `feat/3094-qemu-e1000e-link` | 719 | 165 | Beta sieć; PR #189, selektywny port Cosmos 3.0.97 |
| `fix/alpha-integrity-account-sync` | 9 | 54 | Bezpieczeństwo kont; PR #200, ponownie zweryfikować |
| `fix/alpha-readiness-audit` | 7 | 54 | Wybrać brakujące poprawki, bez cofania Alfy |
| `fix/gen3-general-stability` | 46 | 72 | Stabilność; PR #198, osobne commity i testy |
| `fix/idle-memory-background-gpt6` | 3 | 62 | Wydajność; porównać z #197, bez fałszywych PASS |
| `fix/p0-update-trust-gate` | 122 | 72 | Krytyczny update/BootGuard; PR #199, nie nadpisać poprawionego bootchain |
| `fix/sysbench-baremetal-safe-report-3097` | 11 | 28 | Rozbieżna alternatywa diagnostyki; obecny main już raportuje, nie merge całego brancha |
| `fix/taskmanager-idle-memory` | 3 | 62 | Wydajność; PR #197, porównać pokrywające się poprawki |
| `perf/system-gen3` | 449 | 165 | Beta GUI/perf; PR #195, selektywny port |

## Kolejność prac AGY

1. Zakończ wymagane dowody wydania Alpha 1 na **niezmienionym** oznaczonym tagu (link do GitHub Actions/logów, SHA artefaktu i bezpieczny układ ISO), zgodnie z #211. Raport lokalny AGY nie zastępuje potwierdzonego runu CI.
2. Zablokuj regresję wydania: `main` na stock Cosmos 3.0.97, USB ZONDERQDATA, rozruch, hasła, updater, RAM. Po każdym scaleniu właściwe testy dotkniętego modułu.
3. Osobne kandydaty integracji: najpierw bezpieczeństwo i naprawy niskiego ryzyka, następnie gry i audio, GUI/perf, sieć, storage i RAM — kolejność może wynikać z zależności.
4. Dla każdej rozbieżnej gałęzi użyj `git diff main...<branch>` i `git log main..<branch>`; wybierz tylko przenaszalne zmiany. **Nie** merge bezpośrednio starych dużych gałęzi (audio 594, sieć 719, GUI 842 commity przed main), ponieważ zawierają znaczną nieaktualną historię SDK.
5. JEDNA funkcja = JEDEN aktualny branch/PR, z logami testów C#/Python i QEMU. Nie deklaruj działającego fizycznego sprzętu bez smoke użytkownika.
6. Nie kasuj żadnej gałęzi zawierającej nieprzeniesione commity bez ich udokumentowania. Usuwanie branchy już połączonych dopiero po potwierdzeniu przez AGY, że służą tylko historii.

## Wymagany raport końcowy

Dla każdej gałęzi: `MERGED / ALREADY_IN_MAIN / REWORK / ARCHIVED / BLOCKED`, link PR/commit, zależności, zakres zmian, testy i ewentualny powód odmowy. Bez zbiorczych niesprawdzonych merge.

**Zakres tego dokumentu**: inwentaryzacja na podstawie GitHub merge-base/ahead/behind; nie jest audytem jakości każdego diffu i nie stanowi zatwierdzenia 21 rozbieżnych branchy do integracji.
