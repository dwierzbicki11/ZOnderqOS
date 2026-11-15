# ZonderqOS Gen 3 — Alpha Readiness Audit (sesja 2026-11-15, częściowa)

Branch: `fix/alpha-readiness-audit` (baza: `main` @ `b51d0544`). Nie scalono do `main`.
Legenda: **H** = test hostowy/statyczny, **Q** = QEMU, **HW** = real hardware.

## Potwierdzone błędy
1. **ARM64:** `GuiManager.cs:571` miał `#if !ARM64` (symbol nigdy niezdefiniowany); kod x64 `Platform.X64.SerialMemoryTelemetry` trafiał do buildu ARM64, gdzie `source/Platform/X64/**` jest usunięty z kompilacji.
2. **RAM:** `PhysicalMemoryDetector` zaokrąglał mapę pamięci w górę do "standardowych modułów" i pokazywał to jako *zainstalowany* RAM.
3. **`cosmos build -a x64` (i więc `verify-builds.sh`) pada** na x64 z 10× CS0246 `KeyEvent`. `dotnet build -r linux-x64 -p:CosmosArch=x64` oraz `dotnet publish ... -p:ZonderqCosmosKeyboardApi=Input` (tak jak `tools/build-cosmos-3095-local.sh`) przechodzą. Przyczyna **niezbadana**: najpewniej `cosmos build` nie przekazuje właściwości `ZonderqCosmosKeyboardApi`. **Otwarte.**

## Naprawione
| Commit | Zmiana |
|---|---|
| `22ecbbe9` | `#if !ZONDERQ_ARCH_ARM64` + 2 testy kontraktowe (niezdefiniowane symbole arch; niechronione `Platform.X64.`). Test pada na starym kodzie (zweryfikowane), przechodzi po poprawce (H). |
| `351d4aaf` | Installed RAM = tylko SMBIOS; brak SMBIOS ⇒ "nieznany". Rozdzielono: Usable (Limine `Usable`), firmware-visible, heap pool (PageAllocator). Zmienione: `PhysicalMemoryDetector`, `HardwareSnapshot`, `SettingsApp`, `TaskManagerMemoryCache`, `TaskManagerModernApp`, `CmdHwInfo`. |

Uwaga: Cosmos 3.0.95 Limine binding nie ma żądania SMBIOS (sprawdzone w symbolach pakietu), więc *potwierdzonej* pojemności zainstalowanej nie da się dziś odczytać — to świadomie "unknown", nie błąd. Parser SMBIOS wymaga własnego skanu pamięci fizycznej — nie wykonano.

## Wyniki testów
| Test | Typ | Wynik |
|---|---|---|
| `python3 -m unittest discover -s tests` (509 testów, w większości statyczne źródłowe) | H | PASS |
| `dotnet build` x64 @ `351d4aaf` | build | 0 błędów, 478 ostrzeżeń (nullable/CS0162, istniejące) |
| `dotnet build` ARM64 @ `351d4aaf` | build | 0 błędów, 479 ostrzeżeń |
| Dowód, że stary kod nie budował ARM64 | — | **nie wykonano** (build ARM64 starego kodu nie został uruchomiony); dowodem jest tylko test kontraktowy |
| Smoke QEMU x64, 1 vCPU, 60 s, ISO z `351d4aaf` | Q | PASS (ZONDERQDATA zamontowane, kernel booted, `PERF_BOOT_INIT_MS=1708`) |
| Smoke QEMU x64, 8 vCPU, 60 s | Q | PASS (`PERF_BOOT_INIT_MS=1869`); stock Cosmos, bez prywatnego SMP |
| `cosmos build` / `verify-builds.sh` | build | **FAIL** (pkt 3 powyżej) |
| Smoke / uruchomienie ARM64 w QEMU | Q | **nie wykonano** — brak `qemu-system-aarch64` |
| Unit-smoke C# w `tests/*Smoke` (dotnet) | H | **nie uruchomiono** |

Smoke sprawdza tylko boot i montowanie; log pokazuje też `Integrity scan detected 1 file status violation(s)` na świeżym obrazie — **niezbadane**.

## Pamięć (P0.2) — brak pomiarów
- **Nie wykonano** testu 60 min i nie ma żadnych próbek `MEM_SAMPLE`. Uruchomiłem QEMU 1 vCPU z `-serial file`, ale po ~1 min log miał 0 linii `MEM_SAMPLE`: `SampleIfDue` jest wywoływane tylko w pętli pulpitu (`GuiManager.cs`), a świeży obraz zatrzymuje się na ekranie konfiguracji/logowania. Przerwałem test; dalsza praca wymaga zautomatyzowania pierwszej konfiguracji przez QMP (`tools/qmp-gui-login.py` zakłada istniejące konto).
- **Przyczyny wzrostu 265→300 MiB nie zdiagnozowano.** Nie wiadomo, czy to wyciek, czy rosnące cache/logi. Poprzednie commity w `main` (np. `b51d0544`, `28277c6e`) już ograniczają alokacje idle; ich skuteczności tu nie zmierzono. Nie dodawano `GC.Collect()`.

## Nieprzeanalizowane w tej sesji (nie PASS, nie FAIL)
FAT32 (pełny dysk, odłączenie, przerwanie zapisu, ochrona ESP/obcych partycji), BootGuard/A-B i PR #199 (zob. niżej), sieć/PR #189 i RTL8136, schedulery/usługi/race conditions, optymalizacje GUI (dirty rect, kursor, 800×600/1366×768).

- PR #199: `main` zawiera już `c224ae3b` (Ed25519 trust gate, A/B preboot recovery). Różnica `main...pr199` to 47 plików; nie przeglądałem jej ani nie weryfikowałem podpisów/rollbacku (testy statyczne w repo przechodzą, ale to nie dowód działania z produkcyjnym Limine).
- PR #189: 51 plików, ~15k linii dodanych; nie uruchamiano wyników QEMU DHCP/ICMP/TCP/DNS/HTTP. Sieć w `main` jest wyłączona (`ZONDERQ_NETWORK_DISABLED`); zalecenie: nie włączać do czasu przeglądu.

## Ryzyka dla danych użytkownika (niezweryfikowane testami)
Brak testów zasilania/odłączenia/pełnego dysku w tej sesji. Kernel zgodnie z komentarzami w csproj odmawia montowania niezatwierdzonego FAT32/ESP Kali — nie testowano.

## Status
- x64: kompiluje (dotnet), smoke QEMU PASS; `cosmos build` FAIL.
- ARM64: kompiluje (dotnet); brak uruchomienia.
- Real hardware: **brak testów**.

## Blokery Alpha
1. `cosmos build`/CI `verify-builds.sh` nie przechodzi.
2. Brak pomiarów pamięci i diagnozy wzrostu RAM.
3. Brak testów odporności FAT32 i aktualizacji.
4. Brak walidacji BootGuard poza testami statycznymi.
5. ARM64 nieuruchomiony.

## Gotowość do Alpha
**Szacunek: ~40–50 %** — to ocena subiektywna, nie wynik pomiaru. Za: build obu architektur przechodzi, smoke 1/8 vCPU przechodzi, testy statyczne zielone. Przeciw: krytyczne obszary (dane, pamięć, aktualizacje, sprzęt) bez żadnych testów dynamicznych w tej sesji.
