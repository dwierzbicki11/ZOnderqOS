# Niezależna ocena techniczna i strategiczna roadmapy ZOnderqOS Gen3 (Beta 1–3)

**Data audytu:** 2026-11-18  
**Repozytorium:** `dwierzbicki11/ZOnderqOS`  
**Analizowana gałąź:** `fix/alpha1-e2e-regression` (`48ad0981`), oparta na `feat/cosmos-3097-migration` (`d110f4d3`) i `fix/audit-security-kernel-stabilization` (`64156f6d`)  
**Docelowy SDK:** Cosmos Kernel Gen3 **3.0.97** (x86_64)  

---

## 1. Streszczenie najważniejszych ustaleń

1. **Migracja Cosmos 3.0.95 → 3.0.97 została ukończona** (`ZonderqOS.csproj`, `tests/KeyboardApiSmoke/KeyboardApiSmoke.csproj`, clean build x64 i ARM64 w `verify-builds.sh`, `pytest`: `565 passed, 200 subtests passed`).
2. **Propozycja ChatGPT ma poważne błędy kolejności i ignoruje rzeczywisty stan repozytorium**:
   - Wpycha **Ethernet Realtek RTL810xE** i **Audio Intel HDA (WAV/PCM)** do **Beta 1**, mimo że w repozytorium **nie ma ani jednej linii sterownika Intel HDA ani RTL810xE** (`source/Drivers/` nie istnieje, `source/Audio/` to wyłącznie `Melodies.cs` / `MelodyPlayer.cs` / `Speaker.cs` dla PC Speaker, a `ZonderqOS.csproj` ma `CosmosEnableNetwork=false`).
   - Przesuwa **aktualizacje A/B z Ed25519 i BootGuard** do **Beta 2**, mimo że kod (`BootSlotManager.cs`, `UpdateManager.cs`, `ReleaseSignatureVerifier.cs`, `bootguard_efi.c`) **już istnieje**, a jego brak w Beta 1 oznacza brak bezpiecznej ścieżki aktualizacji dla testerów.
   - Zaniża priorytet **realnego FAT32 i bezpieczeństwa danych**, traktując je jako „dodatek do Beta 1” zamiast warunku wejściowego zamykanego już na styku Alpha 1 → Beta 1.
   - Planuje **framework gier C#** w Beta 2 jako nowy duży temat, podczas gdy w `source/GUI/Application/` działa już 8 gier (`SnakeApp`, `TetrisApp`, `MinesweeperApp`, `Game2048App`, `BreakoutApp`, `SolitaireApp`, `BattleshipApp`, `SpaceInvadersApp`, `Demo3DApp`).
3. **Werdykt końcowy:** **CZĘŚCIOWO SIĘ ZGADZAM** — ogólny podział na 3 bety jest rozsądny, ale **kolejność funkcji w Beta 1 i Beta 2 wymaga przestawienia**, aby nie zablokować projektu wielomiesięcznym pisaniem sterowników bare-metal przed ustabilizowaniem podstaw OS.

---

## 2. Aktualny stan repozytorium

| Obszar | Rzeczywisty stan w kodzie | Weryfikacja QEMU | Weryfikacja sprzęt (ASUS X540LA) |
|---|---|---|---|
| **Kernel & Scheduler** | BSP-only (`Kernel.cs`, `ProcessManager.cs` z atomowym `lifecycleState`), stock Cosmos 3.0.97, brak własnego SMP | **PASS** (1 i 8 vCPU, 10 restartów) | NIEZWERYFIKOWANY na 3.0.97 |
| **GUI & Framebuffer** | `GuiManager.cs`, `CursorDamageRenderer.cs`, `DisplayModeSelector.cs`, `IconManager.cs` z `ClearSessionCache()` | **PASS** (105 cykli GUI, `+0.19 MB`) | NIEZWERYFIKOWANY na 3.0.97 |
| **RAM & OrionGC** | `MemoryGuardian.cs`, `MemoryOptimizer.cs`, `NativeScratchBuffer<T>` | **PASS** (3.0.95: `+0.19 MiB / 60m`; 3.0.97 soak w toku) | NIEZWERYFIKOWANY |
| **FAT32 & Storage** | `Disk.cs`, `PathResolver.cs`, `DataVolumePolicy.cs`, `TmpFs.cs`, `RunFs.cs` | **PASS** (zapis/odczyt/ACL na `ZONDERQDATA`) | NIEZWERYFIKOWANY na 3.0.97 |
| **USB, xHCI, HID** | Stock Cosmos `CosmosEnableUsb=true`, `InputBusDiagnostics.cs` | **PASS** (`qemu-xhci`, `usb-kbd`, `usb-tablet`) | NIEZWERYFIKOWANY |
| **PCI / ACPI / Power** | `PciConfigMechanism1.cs`, `PciConfigIoAccessor.cs` (z lockiem), `BootSessionHealth.cs` | **PASS** (boot/reboot w QEMU; ACPI shutdown timeout w E2E konsoli) | NIEZWERYFIKOWANY |
| **Ethernet & TCP/IP** | `source/Core/Network.cs` wyłączony (`ZONDERQ_NETWORK_DISABLED`, `CosmosEnableNetwork=false`); brak sterownika RTL810xE | **NIEURUCHOMIONE** (wyłączone w buildzie) | NIEZWERYFIKOWANY |
| **Audio** | Wyłącznie PC Speaker (`source/Hardware/Speaker.cs`, `source/Audio/MelodyPlayer.cs`). Brak Intel HDA / WAV / PCM | **NIEZWERYFIKOWANY** (brak HDA) | NIEZWERYFIKOWANY |
| **Konta, ACL, Security** | `UserManager.cs`, `PermissionManager.cs`, `AuthenticationGuard.cs`, `IntegrityGuard.cs`, `PassVaultApp.cs` (v3 HMAC+CTR), `SecurityCenterApp.cs` | **PASS** (50× Lock/Unlock, 20× błędne hasło, `alice` ACL) | NIEZWERYFIKOWANY |
| **BootGuard & A/B** | `BootSlotManager.cs`, `UpdateManager.cs`, `TrustedReleaseKey.cs` (pusty klucz = fail-closed), `tools/bootguard/bootguard_efi.c` | **CZĘŚCIOWO** (`UpdateEngineSmoke` + `ConfirmSlotHealth` PASS; `.efi` w OVMF **BLOCKED** — brak `gnu-efi`) | NIEZWERYFIKOWANY |
| **Aplikacje & Gry** | ~35 aplikacji w `source/GUI/Application/`, w tym 8 gier + `BasicIdeApp` | **PASS** (TaskManager, LockScreen, Terminal/Shell) | NIEZWERYFIKOWANY |
| **x86_64 vs ARM64** | Oba cele kompilują się czysto (`verify-builds.sh`) | x64 **PASS**; ARM64 **BLOCKED** (brak `qemu-system-aarch64`) | Tylko x86_64 |

---

## 3. Krytyczna analiza propozycji ChatGPT

| Funkcja | Istnieje w repo? | Stopień implementacji | Działa w QEMU? | Test na sprzęcie? | Cosmos 3.0.97 API? | Wymaga własnego sterownika? | Konieczna do Beta 1? |
|---|---|---|---|---|---|---|---|
| **Ethernet RTL810xE + TCP/IP** | Częściowo (`Network.cs` wyłączony, branch `feat/3094-qemu-e1000e-link`) | ~10% (UI/komendy są, stos wyłączony) | NIE | NIE | Częściowo (e1000/podstawy, brak RTL810xE) | **TAK (RTL810xE)** | **NIE** (najpierw e1000 w QEMU) |
| **Audio Intel HDA (WAV/PCM)** | NIE (tylko PC Speaker) | 0% dla HDA | NIE | NIE | Ograniczone | **TAK (pełny sterownik HDA)** | **NIE** (przenieść do Beta 2) |
| **USB HID / pendrive / xHCI** | TAK (stock Cosmos) | ~80% | TAK (`usb-kbd`/`tablet`) | NIEZWERYFIKOWANY | TAK | NIE (chyba że quirk ASUS) | **TAK** (krytyczne dla USB) |
| **Natywne rozdzielczości + GUI** | TAK (`DisplayModeSelector`, `GuiManager`) | ~85% | TAK | NIEZWERYFIKOWANY | TAK (Limine FB) | NIE | **TAK** |
| **FAT32 + Menedżer plików** | TAK (`Disk.cs`, `FileManagerApp.cs`) | ~85% | TAK | NIEZWERYFIKOWANY | TAK | NIE | **TAK** (domknąć przed/w Beta 1) |
| **A/B Updates + Ed25519 + BootGuard** | TAK (`BootSlotManager`, `UpdateManager`, `bootguard_efi.c`) | ~90% (brak tylko klucza prod. i testu OVMF `.efi`) | Częściowo (bez OVMF `.efi`) | NIEZWERYFIKOWANY | TAK | NIE | **TAK (Beta 1, nie Beta 2!)** |
| **Framework gier C#** | TAK (8 gier + `feat/games-framework-gen3`) | ~70% | Częściowo | NIE | TAK (`Canvas`) | NIE | **NIE** (Beta 2/3) |
| **Instalator SSD/HDD** | NIE (tylko skrypty USB `deploy-to-usb.sh`) | ~15% | NIE | NIE | TAK | NIE | **NIE** (Beta 3) |

---

## 4. Główne błędy w propozycji ChatGPT

1. **Ethernet Realtek RTL810xE w Beta 1 jest nierealistyczny jako twardy wymóg**: pisanie od zera sterownika bare-metal dla konkretnego układu Realtek + debugowanie przerwań/DMA/DHCP/DNS bez wcześniejszego uruchomienia `CosmosEnableNetwork=true` na emulowanym `e1000` w QEMU zablokuje Beta 1 na tygodnie. **Najpierw `e1000` w QEMU (Beta 1), dopiero potem RTL810xE na fizycznym ASUS X540LA (Beta 2).**
2. **Audio Intel HDA nie może blokować Beta 1**: Intel HDA wymaga pełnej inicjalizacji kontrolera PCI, CORB/RIRB, kodeków, BDL i buforów DMA. W repozytorium nie ma ani jednej linii HDA. W Beta 1 wystarczy stabilny `Speaker.cs`, a HDA powinno trafić do **Beta 2**.
3. **Przesunięcie A/B Updates i BootGuard do Beta 2 to błąd**: 90% kodu już istnieje (`BootSlotManager`, `UpdateManager`, `bootguard_efi.c`), brakuje tylko wgrania klucza Ed25519 i testu w OVMF. Wypuszczenie Beta 1 bez działających aktualizacji uniemożliwia bezpieczne dostarczanie poprawek testerom.
4. **FAT32 i bezpieczeństwo danych nie mogą czekać**: podstawowy FAT32 i ACL już działają, ale w Beta 1 muszą przejść testy pełnego dysku, uszkodzonych metadanych i fizycznego pendrive'a — nie w Beta 3.
5. **Framework gier jest mniej ważny niż sterowniki i instalator**: gry już istnieją; inwestowanie w abstrakcyjny framework przed siecią i HDA to zła alokacja czasu.
6. **Akceleracja GPU i własne SMP/SMT**: słusznie wydzielone poza główny nurt — w Cosmos 3.0.97 brak stabilnego wielordzeniowego schedulera i sterowników GPU (Intel/AMD/NVIDIA), więc własne SMP/SMT musi pozostać zamrożone.
7. **Brakujący temat krytyczny**: **ACPI shutdown / power-off na fizycznym sprzęcie i w QEMU** (w teście E2E konsoli ACPI shutdown przekroczył 20s timeout i wymagał `SIGTERM` z zewnątrz) oraz **testy OVMF `.efi`** (`gnu-efi`).

---

## 5. Alternatywna roadmapa AGY

### **Alpha 1** (obecny etap — gotowość ~92%)
- **Wymagane:** stabilny kernel 3.0.97, GUI, FAT32 + ACL, Lock/Unlock, 100+ cykli aplikacji, 60-min test RAM.
- **Kryterium ukończenia:** wszystkie testy QEMU PASS + 1 próbny boot na fizycznym ASUS X540LA z USB.

### **Beta 1 — Stabilizacja, Aktualizacje i Sieć Bazowa (QEMU)**
- **Wymagane:**
  - Domknięcie **A/B Updates + Ed25519 + BootGuard `.efi` w OVMF** (zainstalować `gnu-efi`, wgrać klucz przez `provision-release-public-key.py`, przetestować 3x fail → rollback).
  - Włączenie **`CosmosEnableNetwork=true` na `e1000` w QEMU** (DHCP, DNS, podstawowy TCP/IP + `ping`).
  - Pełne testy **FAT32** (pełny dysk, błędy zapisu, polskie znaki) i naprawa **ACPI shutdown**.
  - Weryfikacja **USB HID / pendrive** na fizycznym ASUS X540LA.
- **Opcjonalne:** rejestracja `CmdHistory`, `CmdAlias`, `CmdConfig`.
- **Pracochłonność:** 2–3 tygodnie.

### **Beta 2 — Sprzęt Docelowy (Realtek, HDA) i Usługi Sieciowe**
- **Wymagane:**
  - Sterownik **Ethernet Realtek RTL810xE** dla fizycznego ASUS X540LA.
  - Sterownik **Audio Intel HDA** (PCM/WAV, mikser).
  - Klient **HTTP** (i przygotowanie pod TLS/HTTPS w oparciu o BouncyCastle) + aktualizacje online.
  - Rozbudowa `SettingsApp` i `TaskManagerModernApp`.
- **Opcjonalne:** porządki w frameworku gier (`feat/games-framework-gen3`).
- **Pracochłonność:** 4–6 tygodni.

### **Beta 3 — Instalator, Długotrwała Stabilność i Hardening**
- **Wymagane:**
  - Bezpieczny **instalator SSD/HDD** (partycjonowanie, ESP + `ZONDERQDATA`, instalacja BootGuard `.efi`, ochrona przed nadpisaniem obcych partycji EFI/Windows/Linux).
  - Testy kompatybilności na wielu maszynach x86_64.
  - Ponowny pełny audyt bezpieczeństwa, fuzzing parsera powłoki i 24h soak test RAM.
- **Pracochłonność:** 3–4 tygodnie.

### **Release Candidate → 1.0**
- Zamrożenie funkcji, testy regresyjne QEMU + sprzęt, podpisane obrazy wydania.

### **Rozwijane osobno (poza ścieżką krytyczną 1.0)**
- **SMP/SMT** (czeka na stabilny upstream Cosmos), **Wi-Fi RTL8723BE**, **akceleracja GPU**, **ARM64** (utrzymywać czystą kompilację CI), **przeglądarka WWW**.

---

## 6. Porównanie dwóch planów

| Obszar | ChatGPT proponuje | AGY proponuje | Zgoda? | Uzasadnienie techniczne |
|---|---|---|---|---|
| **Ethernet** | Beta 1 (RTL810xE) | Beta 1 (`e1000` QEMU) → Beta 2 (RTL810xE) | **NIE** | Brak kodu RTL810xE i wyłączony stos sieciowy; najpierw trzeba uruchomić `CosmosEnableNetwork` w QEMU |
| **Audio** | Beta 1 (Intel HDA) | Beta 2 | **NIE** | W repo jest tylko PC Speaker; pisanie HDA od zera zablokuje Beta 1 |
| **USB** | Beta 1 | Beta 1 | **TAK** | Stock xHCI/HID działa w QEMU, wymaga testu na fizycznym USB |
| **FAT32** | Beta 1 | Alpha 1 / Beta 1 (pełne edge-case'y) | **TAK** | Działa w QEMU, w Beta 1 trzeba domknąć pełny dysk i testy uszkodzeń |
| **GUI** | Beta 1 | Alpha 1 / Beta 1 | **TAK** | Działa stabilnie (`+0.19 MB` po 105 cyklach), w Beta 1 dopracować rozdzielczości |
| **A/B Updates** | Beta 2 | **Beta 1** | **NIE** | 90% kodu już gotowe; potrzebne od razu w Beta 1 do bezpiecznych aktualizacji |
| **Framework gier** | Beta 2 | Beta 2/3 (opcjonalne) | **CZĘŚCIOWO** | 8 gier już działa; niższy priorytet niż sterowniki i instalator |
| **Instalator** | Beta 3 | Beta 3 | **TAK** | Wysokie ryzyko utraty danych; wymaga wcześniej stabilnego FAT32 i BootGuard |
| **SMP/SMT** | Osobno | Osobno | **TAK** | Stock Cosmos 3.0.97 nie gwarantuje stabilnego managed SMP |
| **ARM64** | Osobno | Osobno (CI build) | **TAK** | Buduje się czysto, ale priorytetem dla 1.0 jest x86_64 |

---

## 7. Niezależna ocena gotowości

| Etap | Gotowość implementacji | Potwierdzenie testami | Uzasadnienie |
|---|---:|---:|---|
| **Alpha 1** | **92%** | **85%** | Kod na 3.0.97 gotowy i przetestowany w QEMU (10 restartów, 105 cykli GUI, 50× Lock/Unlock, 20× błędne hasło, FAT32/ACL). Brakuje jedynie testu na fizycznym ASUS X540LA oraz dokończenia trwającego 60-min soaka na 3.0.97. |
| **Beta 1** | **55%** | **25%** | A/B Updates i BootGuard mają ~90% kodu, ale brakuje `.efi` w OVMF (`gnu-efi`), a sieć (`e1000`) jest całkowicie wyłączona (`CosmosEnableNetwork=false`). |
| **Beta 2** | **15%** | **0%** | Brak sterowników Realtek RTL810xE, brak Intel HDA, brak klienta HTTP/HTTPS. |
| **Beta 3** | **10%** | **0%** | Brak instalatora SSD/HDD (są tylko skrypty USB). |

**Najważniejsze blokery wersji 1.0:**
1. Brak przetestowanego w OVMF BootGuarda `.efi` i wgranego klucza Ed25519.
2. Wyłączony stos sieciowy i brak sterownika fizycznej karty sieciowej (RTL810xE).
3. Brak sterownika audio Intel HDA.
4. Brak bezpiecznego instalatora SSD/HDD.
5. Brak weryfikacji na fizycznym sprzęcie (ASUS X540LA).

---

## 8. Końcowy werdykt

**CZĘŚCIOWO SIĘ ZGADZAM.**  
Podział na 3 bety jest dobry jako struktura, ale **plan ChatGPT dla Beta 1 jest nierealistyczny**, bo wrzuca do jednego worka dwa nieistniejące sterowniki bare-metal (RTL810xE + Intel HDA), a jednocześnie odsuwa niemal gotowe aktualizacje A/B do Beta 2. Przeniesienie A/B + BootGuard (OVMF) + `e1000` (QEMU) do **Beta 1**, a RTL810xE + Intel HDA do **Beta 2** daje znacznie bezpieczniejszą i przewidywalną ścieżkę do wersji **1.0**.
