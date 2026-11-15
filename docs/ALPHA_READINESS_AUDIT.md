# ZonderqOS Gen 3 — Kompleksowy Audyt Gotowości Alpha

**Data:** 2026-11-15  
**Branch:** `fix/alpha-readiness-audit` (baza: `main` @ `b51d0544`, aktualny kod kernela @ `229de96d`, bez merge do `main`).  
**Legenda źródeł:** **S** = analiza statyczna/kontraktowa, **H** = test hostowy (C#/C/.NET), **Q** = QEMU (TCG, bez KVM), **HW** = real hardware.

---

## 1. Potwierdzone błędy i ustalenia diagnostyczne

1. **Kompilacja ARM64 (`source/GUI/GuiManager.cs:571`):** użyto niezdefiniowanego `#if !ARM64` zamiast `#if !ZONDERQ_ARCH_ARM64`. Kod x64 (`Platform.X64.SerialMemoryTelemetry`) trafiał do buildu ARM64 mimo wykluczenia `source/Platform/X64/**/*.cs` w `ZonderqOS.csproj`. **Naprawione (`22ecbbe9`).**
2. **Zgadywanie zainstalowanego RAM-u (`source/Hardware/PhysicalMemoryDetector.cs`):** funkcja `EstimateInstalledCapacity()` zaokrąglała pamięć z mapy Limine w górę do rozmiarów modułów (1, 2, 4, ..., 128 GiB) i prezentowała wynik jako zainstalowany RAM. Pakiet Cosmos 3.0.95 `Cosmos.Kernel.Boot.Limine` nie eksponuje żądania SMBIOS. **Naprawione (`351d4aaf`)** — zainstalowany RAM raportowany uczciwie jako nieznany (`0` / `"nieznany (brak SMBIOS)"`), a Usable (Limine `Usable`), Firmware-visible oraz Heap Pool (`PageAllocator`) są rozdzielone.
3. **Błąd `cosmos build -a x64` / `verify-builds.sh` (10× `CS0246 KeyEvent`):** narzędzie `cosmos build` wywołuje MSBuild z globalnym `-p:DefineConstants=ARCH_<X>`, co nadpisywało wszystkie symbole w `ZonderqOS.csproj` (w tym `ZONDERQ_COSMOS_INPUT_API`). **Naprawione (`229de96d`)** przez `TreatAsLocalProperty="DefineConstants"` w `ZonderqOS.csproj`.
4. **`Integrity scan detected 1 file status violation(s)` na świeżym systemie:** potwierdzone bezpośrednio z `/etc/integrity.db` na dysku `ZONDERQDATA`. `IntegrityGuard.MonitoredPaths` (`source/Security/IntegrityGuard.cs:39`) zawiera `/etc/zonderq/settings.conf`, którego `SystemSettings.Load()` (`source/Core/SystemSettings.cs:157-161`) nie tworzy przy pierwszym uruchomieniu (plik powstaje dopiero po zapisie ustawień). W efekcie `CreateDefaultBaseline()` zapisuje `/etc/zonderq/settings.conf||644` ze statusem `Missing`, a `ScanAll()` (`IntegrityGuard.cs:202-208`) zlicza brakujący plik jako naruszenie. Ponadto `InitialSetupManager` po zmianie hasła `root` nie odświeża `IntegrityGuard.UpdateBaseline()` dla `/etc/shadow`. **Otwarte (niski priorytet / kosmetyczne ostrzeżenie przy pierwszym starcie).**
5. **Pauza telemetrii `MEM_SAMPLE` na ekranie blokady:** `SerialMemoryTelemetry.SampleIfDue()` jest wywoływane wyłącznie w głównej pętli pulpitu (`GuiManager.cs:572`). Po uruchomieniu automatycznej blokady (`SystemSettings.AutoLockMinutes = 5`, `GuiManager.cs:355` `RunLockScreen()`) `MEM_SAMPLE` przestaje być emitowane mimo prawidłowego działania kernela (ticki LAPIC idą dalej). Podczas testu bezczynności utrzymano aktywność przez QMP `input-send-event` (ruch myszy co 120 s).
6. **Niedostępność KVM na hoście audytowym:** `/dev/kvm` nie istnieje; procesor hosta (`Intel Core i3-5005U`) nie wystawia flagi `vmx`/`svm` w `/proc/cpuinfo`, więc wszystkie testy QEMU działały pod TCG.

---

## 2. Naprawione problemy (commity na `fix/alpha-readiness-audit`)

| Commit | Zakres |
|---|---|
| `22ecbbe9` | `#if !ZONDERQ_ARCH_ARM64` w `GuiManager.cs:571` + testy regresyjne w `tests/test_p0_platform_compile_contract.py` wykrywające niezdefiniowane symbole arch i niechronione odwołania `Platform.X64.*`. |
| `351d4aaf` | Usunięcie zgadywania RAM-u w `PhysicalMemoryDetector.cs`; rozdzielenie `InstalledBytes` (`0` bez SMBIOS), `UsableBytes`, `TotalPhysicalBytes` (firmware-visible), `HeapPoolBytes` oraz aktualizacja konsumentów (`HardwareSnapshot`, `SettingsApp`, `TaskManagerMemoryCache`, `TaskManagerModernApp`, `CmdHwInfo`). |
| `229de96d` | `TreatAsLocalProperty="DefineConstants"` w `ZonderqOS.csproj` + test kontraktowy; odblokowanie `cosmos build -a x64` i `cosmos build -a arm64`. |

---

## 3. Wyniki pomiarów pamięci (QEMU 1 vCPU TCG, ISO z `229de96d`)

Dane surowe: [`docs/alpha-mem-soak.csv`](alpha-mem-soak.csv), wykres: [`docs/alpha-mem-soak.png`](alpha-mem-soak.png).  
Warunki: 2048 MiB RAM (`pool_mb=2025`), świeży dysk `ZONDERQDATA`, pierwsza konfiguracja i logowanie zautomatyzowane przez QMP, brak sztucznego `GC.Collect()`.

### Faza 1 — Idle (0 .. 3725 s uptime gościa)
- **Próbki:** `273` (`sec=78 .. 3725`, rozpiętość `3647 s`; w `sec=369..1298` sesja była zablokowana przez AutoLock, a `sec=1298..3725` to `243` ciągłe próbki odblokowanego pulpitu z ruchem myszy QMP co 120 s).
- **Stabilność:** `2737` ticków LAPIC, `0` błędów `ERROR`/`CRITICAL`/`PANIC`/`PageFault`.
- **PageAllocator Used:** `157.37 -> 157.75 MiB` (`+0.38 MiB` w ciągu > 61 min; nachylenie po rozgrzewaniu `sec >= 258`: **`+0.3574 MiB/h`**; ciągły odcinek odblokowany `sec >= 1298`: **`+0.3097 MiB/h`**).
- **OrionGC:** `gc_heap_kb` `+0.2963 MiB/h` (`+0.2573 MiB/h` w odcinku ciągłym); `gc_comm_kb` `+0.3562 MiB/h`; `gc_frag_kb` spadło `166 -> 97 KiB`; `gc_gen: 0 -> 0`, `gc_freed: 0 -> 0`.
- **Podział stron:** `p_gc: 38736 -> 38832` (`+96` stron), **`p_rat: 13 -> 13` (0)**, **`p_sys: 1539 -> 1539` (0)**.

### Faza 2 — 100 cykli otwierania/zamykania aplikacji (`sec=3735 .. 3946`)
Scenariusz na tej samej zalogowanej instancji: zamknięcie `WelcomeApp` + 100× (`Ctrl+Shift+Esc` otwierające Task Managera -> `Alt+F4` zamykające okno).
- **PageAllocator Used:** `157.75 -> 159.37 MiB` (**`+1.62 MiB`** dla 100 cykli = **`+16.6 KiB/cykl`**), potem stabilizacja (`delta_p=0` przy `sec=3936`).
- **Strony:** `p_gc: 38832 -> 39248` (`+416` stron), **`p_rat: 13 -> 13` (0)**, **`p_sys: 1539 -> 1539` (0)**.
- **OrionGC:** `gc_heap_kb: 150379 -> 151795` (`+14.16 KiB/cykl`), `gc_gen=0` (sterta nie przekroczyła jeszcze pierwszego progu Gen0 przy puli 2025 MiB, więc nie można tu rozdzielić martwego śmiecia od ewentualnych żywych referencji obiektów GUI).
- **Wniosek P0.2:** na obecnym `main` nie występuje gwałtowny wzrost RAM-u z ASUS X540LA (`265 -> 300 MiB` / 25 min ≈ `~82 MiB/h`); przyrost idle to **`~0.31–0.36 MiB/h`** wyłącznie w stercie `OrionGC` (`p_gc`), przy zerowym wzroście `p_rat` i `p_sys`.

---

## 4. Wyniki testów (tylko faktycznie wykonane po ostatnich zmianach kodu `229de96d`)

| Test / Build | Typ | Wynik | Uwagi |
|---|---|---|---|
| `cosmos build -a x64` (`output-x64/ZonderqOS.iso`) | build | **PASS** (0 błędów) | — |
| `cosmos build -a arm64` (`output-arm64/ZonderqOS.iso`) | build | **PASS** (0 błędów) | — |
| `python3 -m unittest discover -s tests -p "test_*.py"` | S / H | **PASS** | 510 testów w 7.455 s (`OK`) |
| `UpdateEngineSmoke` (Ed25519 RFC 8032, manifest v2, HMAC reject, A/B) | H | **PASS** | Host .NET 10, nie NativeAOT |
| `ServiceRuntimeSmoke`, `ServiceConfigSmoke`, `ProcessSignalsSmoke` | H | **PASS** | — |
| `RunFs.Tests`, `TmpFs.Tests` | H | **PASS** | — |
| `GuiTaskManagerRefreshSmoke` (2199 asercji), `GuiApplicationManagerSmoke` (105 asercji) | H | **PASS** | — |
| `tests/test_bootguard_policy.c` (gcc `-Wall -Wextra -Werror`) | H | **PASS** | — |
| QEMU 1 vCPU soak (3946 s uptime + 100 cykli aplikacji + ZONDERQDATA) | Q | **PASS** | `PERF_BOOT_INIT_MS=1388`, 0 błędów |
| QEMU 8 vCPU smoke (stock Cosmos 3.0.95) | Q | **PASS** | `Mounted ZONDERQDATA`, `PERF_BOOT_INIT_MS=1370` |
| `tools/bootguard` + `tests/test_bootguard_ovmf.sh` (OVMF) | Q | **NIE URUCHOMIONO** | Brak pakietu `gnu-efi` (`/usr/include/efi/efi.h`) |
| QEMU ARM64 boot | Q | **NIE URUCHOMIONO** | Brak `qemu-system-aarch64` w systemie |
| Test produkcyjny Limine A/B + uszkodzony ELF w slocie | Q | **NIE URUCHOMIONO** | — |
| Destrukcyjne testy FAT32 (pełny dysk, przerwanie zapisu, odłączenie) | Q | **NIE URUCHOMIONO** | Zgodnie z poleceniem kod FAT32 nie był ruszany |
| Testy na fizycznym sprzęcie (ASUS X540LA / drugi PC, RTL810xE) | HW | **NIE URUCHOMIONO** | Brak dostępu sprzętowego w sesji |

---

## 5. Przegląd obszarów (read-only) i ryzyka dla danych użytkownika

### P0.4 Bezpieczeństwo FAT32 i danych użytkownika
- `Kernel.cs:46-48` blokuje rozruch na niezatwierdzonym wolumenie (ochrona przed przypadkowym montowaniem obcego ESP/Kali).
- Kod FAT32 i `UpdateManager` nie były modyfikowane ani testowane destrukcyjnie (przerwanie zasilania, pełny dysk, odłączenie nośnika) — pozostaje otwartym ryzykiem przed wydaniem Alpha.

### P0.5 BootGuard i aktualizacje A/B (PR #199 vs `main`)
- `main` zawiera już implementację trust gate z PR #199 (`c224ae3b`): `TrustedReleaseKey.cs` (fail-closed bez wbudowanego klucza publicznego przy buildzie), `ReleaseSignatureVerifier.cs` (BouncyCastle Ed25519), `BootSlotManager.cs` oraz `tools/bootguard/`.
- `BootSlotManager.SyncLimineConfig()` generuje nazwy wpisów `ZonderqOS Slot A` i `ZonderqOS Slot B` zgodne z `bootguard_efi.c:210`.
- **Luka / bloker:** weryfikacja Ed25519 nie była testowana pod **Cosmos NativeAOT** na żywej aktualizacji; w CI BootGuard sprawdza się z testowym stubem (`limine_stub.efi`), nie z produkcyjnym `LIMINE.EFI` na jednym obrazie dysku.

### P1.6 GUI
- `GuiManager` używa `CursorDamageRenderer` (`CanMoveCursorWithoutSceneRedraw`) do unikania pełnego odrysowania przy ruchu kursora, obsługuje `800x600` i `1366x768`, a `IconManager` ogranicza cache pikseli do 256 wpisów LRU. `ApplicationManager.Close()`/`CloseAll()` odpinają okna i czyszczą przechwycenie myszy.
- Pozostała uwaga: 100 cykli Task Managera zwiększyło `gc_heap_kb` o `+14.16 KiB/cykl` bez uruchomienia GC (`gc_gen=0`) — warto w osobnym teście wymusić alokację aż do wyzwolenia Gen0, aby potwierdzić pełne odzyskiwanie zamkniętych okien.

### P1.7 Ethernet (PR #189 — bez modyfikacji)
- Produkcyjny `main` ma wyłączoną sieć (`CosmosEnableNetwork=false`, `ZONDERQ_NETWORK_DISABLED`).
- PR #189 zawiera izolowany profil diagnostyczny x64 (N1–N6: TX, DHCP, ICMP, TCP, DNS, HTTP na QEMU E1000E, wg `docs/3095-network-n1-qemu.md` zweryfikowany w CI run #44 — nie uruchamiano lokalnie w tej sesji) oraz **wyłącznie read-only sondę** rejestrów MMIO RTL810xE (`10EC:8136`, rev `06`), która po odczycie zwraca `Declined`. **Brak sterownika TX/RX/DMA/IRQ dla RTL810xE.**
- **Bezpieczna ścieżka integracji z GUI:** pozostać przy `CosmosEnableNetwork=false` do czasu: (1) lokalnego przejścia bramek QEMU N1–N6 na bazie obecnego `main`, (2) izolowanego sterownika/testu TX+RX RTL810xE poza GUI (najpierw QEMU/sprzętowy UART), (3) włączenia w GUI wyłącznie za jawnym przełącznikiem ustawień z obsługą timeoutów poza wątkiem renderującym.

### P1.8 Procesy, schedulery i usługi
- `ProcessManager` uruchamia wątki poza `_registryLock`, wywołuje `cts.Cancel()` poza blokadą, a w `finally` usuwa wpis i zwalnia `CancellationTokenSource`.
- `ServiceManager` zachowuje stałą kolejność blokad (`Sync` -> `_registryLock`), przerywa pętle usług przez `token.WaitHandle.WaitOne(...)`, i nie wprowadza prywatnego SMP/SMT (zachowany stock Cosmos 3.0.95).

---

## 6. Pozostałe blokery Alpha i szacunek gotowości

**Pozostałe blokery przed wydaniem Alpha:**
1. Brak destrukcyjnych testów FAT32 (pełny dysk, zanik zasilania w trakcie zapisu/aktualizacji, odłączenie nośnika).
2. Brak end-to-end testu aktualizacji A/B z podpisanym pakietem Ed25519 w NativeAOT i prawdziwym `LIMINE.EFI` + BootGuard w OVMF (lokalnie brak `gnu-efi`).
3. Brak uruchomienia i smoke testu **ARM64 w QEMU** (kompilacja przechodzi, brak binarki `qemu-system-aarch64` na hoście).
4. Brak weryfikacji na **realnym sprzęcie** (ASUS X540LA + drugi komputer) oraz brak sterownika TX/RX RTL810xE.
5. Kosmetyczne: fałszywy alarm `Integrity scan detected 1 file status violation(s)` przy pierwszym rozruchu (`/etc/zonderq/settings.conf`).

**Szacunek gotowości do Alpha: ~60–65 %** *(ocena jakościowa, nie metryka automatyczna)*.  
Uzasadnienie: krytyczne blokery kompilacji (`ARM64`, `cosmos build` `KeyEvent`), zgadywanie RAM-u oraz podejrzenie ciężkiego wycieku idle zostały wyjaśnione/naprawione i potwierdzone 65-minutowym biegiem QEMU + 100 cyklami aplikacji oraz buildem x64/ARM64; nadal jednak brakuje testów odporności FAT32, pełnego testu A/B z produkcyjnym Limine/Ed25519 pod NativeAOT, uruchomienia ARM64 oraz testów na fizycznym sprzęcie.
