# ZOnderqOS Alpha 1 — full English-language audit

**Date:** 2026-11-18  
**Audited main commit:** `ef268607666ed7602d8002c474bc5bdd24794981`  
**Audit CI run:** https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/44349678136

## Verdict

**FAIL — English-only user-facing text is not complete.** Previous English-only regression guards pass, but the exhaustive tracked-file inventory found substantial Polish runtime strings, including boot messages, login/lock-screen flow, the disk manager, app center, BASIC IDE, notifications, update engine, and user-facing bundled update metadata.

## Inventory and scope

Main has 639 tracked files. The audit branch has 641 (including the two audit-only files). 585 text files were read and 56 binary/unreadable files excluded from linguistic static checks. Every tracked path was classified. Intentional Polish in documentation, tests, and the disabled Polish translation dictionary is not a runtime localization defect. The scanner considered 284 runtime text files, 37 host-tool text files, 2 shipped-output text files, and 17 resource/config files.

The current static scan detected **153 candidate occurrences**, grouped as: **136 runtime source occurrences across 41 files**, **11 shipped-output occurrences across 2 files**, and **6 host-tool occurrences across 4 files**. These are **candidate strings**, not a guaranteed count of distinct visible messages. Repeated messages, technical paths, and non-displayed lookup strings can inflate the count; unrecognized transliterations, image-embedded words, concatenations, dynamically loaded content, and runtime-generated strings may still be missed.

**Confirmed impact categories:**

- Boot splash / startup: `DYSK GOTOWY`, `LADOWANIE USLUG SYSTEMOWYCH`, `GOTOWE`.
- Sign-in and shell: `WPISZ LOGIN RECZNIE`, `ODSWIEZONO LISTE KONT`, `Error uruchamiania GUI`.
- GUI applications: entire clusters in Disk Manager, App Center, BASIC IDE, System Log, Screenshot, Settings Tool Window, calendar, notes, and image tools.
- Security and updates: firewall rule descriptions, A/B boot-slot error and status text, manifest validation, updater report labels.
- Shipped update package: the human-readable `Co_Nowego_W_Aktualizacji.txt` is Polish, and the manifest includes `SEVERITY=Zalecana` plus a Polish `CHANGELOG`.
- Host tools: USB preparation, QEMU memory soak, branch maintenance and build verification retain Polish diagnostics.

**Not translation defects:** the retained `pl-PL` dictionary; Polish documentation/test data; technically required filenames such as `/root/Co_Nowego_W_Aktualizacji.txt` (file migration requires compatibility review); and language glyph character literals. Some matches are internal search aliases rather than displayed text. Generated update manifests must be regenerated and cryptographically signed if their signed content changes — never change their text in isolation.

## Complete static candidate inventory

Each reference points to the unmodified audited `main` commit. Counts indicate source occurrences rather than distinct message templates.

### Runtime C# and kernel (136 candidates)

| File / line | Detected literal |
|---|---|
| [`Kernel.cs:49`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/Kernel.cs#L49) | `"DYSK GOTOWY"` |
| [`Kernel.cs:64`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/Kernel.cs#L64) | `"LADOWANIE USLUG SYSTEMOWYCH"` |
| [`Kernel.cs:80`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/Kernel.cs#L80) | `"PRZYGOTOWYWANIE SESJI"` |
| [`source/Audio/Melodies.cs:11`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Audio/Melodies.cs#L11) | `"Bez tytulu"` |
| [`source/Core/SystemSettings.cs:134`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/SystemSettings.cs#L134) | `"WYLACZONA"` |
| [`source/Core/Updates/BootSlotManager.cs:114`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/BootSlotManager.cs#L114) | `"Error inicjalizacji BootSlotManager: "` |
| [`source/Core/Updates/BootSlotManager.cs:567`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/BootSlotManager.cs#L567) | `"Obraz kernela jest zbyt krotki."` |
| [`source/Core/Updates/BootSlotManager.cs:599`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/BootSlotManager.cs#L599) | `"Error SHA-256 tymczasowego obrazu kernela."` |
| [`source/Core/Updates/BootSlotManager.cs:650`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/BootSlotManager.cs#L650) | `"Error bezpiecznego stagingu slotu "` |
| [`source/Core/Updates/BootSlotManager.cs:756`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/BootSlotManager.cs#L756) | `$"Aktywowano slot {targetSlot}. Limine default_entry zaktualizowany. Wymagany restart."` |
| [`source/Core/Updates/BootSlotManager.cs:838`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/BootSlotManager.cs#L838) | `$"Error sumy SHA-256 obrazu kernela w slocie przywracania {previousSlot}."` |
| [`source/Core/Updates/UpdateManager.cs:1276`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/UpdateManager.cs#L1276) | `"Tytul wydania:        "` |
| [`source/Core/Updates/UpdateManager.cs:1279`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/UpdateManager.cs#L1279) | `"Suma SHA-256 kernela: "` |
| [`source/Core/Updates/UpdateManager.cs:1295`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/UpdateManager.cs#L1295) | `"/root/Co_Nowego_W_Aktualizacji.txt"` |
| [`source/Core/Updates/UpdateManager.cs:1753`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/UpdateManager.cs#L1753) | `"] ROLLBACK: Przywrocono wersje "` |
| [`source/Core/Updates/UpdateManifest.cs:27`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/UpdateManifest.cs#L27) | `"Zalecana"` |
| [`source/Core/Updates/UpdateManifest.cs:92`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/UpdateManifest.cs#L92) | `"Manifest jest pusty."` |
| [`source/Core/Updates/UpdateManifest.cs:133`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Core/Updates/UpdateManifest.cs#L133) | `"Powtorzone pole manifestu: "` |
| [`source/GUI/Application/AppCenterApp.cs:228`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/AppCenterApp.cs#L228) | `"ZAINSTALOWANE"` |
| [`source/GUI/Application/AppCenterApp.cs:280`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/AppCenterApp.cs#L280) | `"WYNIKI"` |
| [`source/GUI/Application/AppCenterApp.cs:338`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/AppCenterApp.cs#L338) | `"SZCZEGOLY"` |
| [`source/GUI/Application/AppCenterApp.cs:354`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/AppCenterApp.cs#L354) | `"ZAINSTALOWANA"` |
| [`source/GUI/Application/AppCenterApp.cs:390`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/AppCenterApp.cs#L390) | `"WYCZYSC FILTR"` |
| [`source/GUI/Application/AppCenterApp.cs:565`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/AppCenterApp.cs#L565) | `"WYCZYSZCZONO FILTR"` |
| [`source/GUI/Application/BasicIdeApp.cs:109`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L109) | `"160 PRINT \"-&gt; BRAWO! TRAFIONO W \"; TRIES; \" PROBIE!\""` |
| [`source/GUI/Application/BasicIdeApp.cs:116`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L116) | `"10 REM --- TABLICZKA MNOZENIA ---"` |
| [`source/GUI/Application/BasicIdeApp.cs:118`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L118) | `"30 PRINT \"TABLICZKA MNOZENIA 1-5\""` |
| [`source/GUI/Application/BasicIdeApp.cs:128`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L128) | `"130 PRINT \"TABLICA SKOMPLETOWANA!\""` |
| [`source/GUI/Application/BasicIdeApp.cs:164`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L164) | `"150 PRINT \"KONIEC CIAGU.\""` |
| [`source/GUI/Application/BasicIdeApp.cs:343`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L343) | `"Koniec programu (OK)"` |
| [`source/GUI/Application/BasicIdeApp.cs:377`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L377) | `"--- KONIEC PROGRAMU ---"` |
| [`source/GUI/Application/BasicIdeApp.cs:463`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L463) | `"Error: RETURN bez GOSUB"` |
| [`source/GUI/Application/BasicIdeApp.cs:684`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L684) | `"Error: NEXT bez FOR"` |
| [`source/GUI/Application/BasicIdeApp.cs:1020`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L1020) | `"PRZYKLAD"` |
| [`source/GUI/Application/BasicIdeApp.cs:1021`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BasicIdeApp.cs#L1021) | `"WYCZYSC"` |
| [`source/GUI/Application/BattleshipApp.cs:244`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BattleshipApp.cs#L244) | `"TRAFIENIE! Trafiles wrogi okret! Masz dodatkowy strzal."` |
| [`source/GUI/Application/BattleshipApp.cs:319`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/BattleshipApp.cs#L319) | `"PORAZKA! Wrog zatopil Twoja flote. Sprobuj ponownie!"` |
| [`source/GUI/Application/CalendarApp.cs:59`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/CalendarApp.cs#L59) | `"GOTOWE"` |
| [`source/GUI/Application/CalendarApp.cs:90`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/CalendarApp.cs#L90) | `"ODSWIEZONO DATE SYSTEMOWA"` |
| [`source/GUI/Application/CalendarApp.cs:183`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/CalendarApp.cs#L183) | `"DZISIAJ"` |
| [`source/GUI/Application/CalendarApp.cs:275`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/CalendarApp.cs#L275) | `"DZISIAJ"` |
| [`source/GUI/Application/CalendarApp.cs:309`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/CalendarApp.cs#L309) | `"DZISIAJ"` |
| [`source/GUI/Application/DeviceManagerApp.cs:48`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DeviceManagerApp.cs#L48) | `"PONOWNIE SKANUJ PARTYCJE"` |
| [`source/GUI/Application/DiagnosticsApp.cs:23`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiagnosticsApp.cs#L23) | `"Status: GUI dziala poprawnie"` |
| [`source/GUI/Application/DiskManagerApp.cs:33`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L33) | `"PARTYCJE"` |
| [`source/GUI/Application/DiskManagerApp.cs:76`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L76) | `"GOTOWE"` |
| [`source/GUI/Application/DiskManagerApp.cs:107`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L107) | `"ODSWIEZONO MAGAZYN DANYCH"` |
| [`source/GUI/Application/DiskManagerApp.cs:150`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L150) | `"ODSWIEZONO MAGAZYN DANYCH"` |
| [`source/GUI/Application/DiskManagerApp.cs:200`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L200) | `"ODSWIEZONO MAGAZYN DANYCH"` |
| [`source/GUI/Application/DiskManagerApp.cs:323`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L323) | `"PARTYCJA"` |
| [`source/GUI/Application/DiskManagerApp.cs:392`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L392) | `"PARTYCJA"` |
| [`source/GUI/Application/DiskManagerApp.cs:398`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L398) | `"Wolumin systemowy / jest chroniony przed odmontowaniem."` |
| [`source/GUI/Application/DiskManagerApp.cs:410`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L410) | `"Widok VFS jest tylko informacyjny. Montowanie wykonuj w zakladce PARTYCJE."` |
| [`source/GUI/Application/DiskManagerApp.cs:443`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L443) | `"SKANUJ PARTYCJE"` |
| [`source/GUI/Application/DiskManagerApp.cs:459`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L459) | `"TYLKO PODGLAD VFS"` |
| [`source/GUI/Application/DiskManagerApp.cs:460`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L460) | `"PARTYCJE -&gt; MOUNT"` |
| [`source/GUI/Application/DiskManagerApp.cs:613`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L613) | `"PARTYCJA "` |
| [`source/GUI/Application/DiskManagerApp.cs:667`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L667) | `"PONOWNIE ODCZYTANO TABLICE PARTYCJI"` |
| [`source/GUI/Application/DiskManagerApp.cs:683`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L683) | `"WOLUMIN SYSTEMOWY / JEST CHRONIONY"` |
| [`source/GUI/Application/DiskManagerApp.cs:695`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L695) | `"ZAMONTOWANO PARTYCJE"` |
| [`source/GUI/Application/DiskManagerApp.cs:707`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L707) | `"ODMONTOWANO PARTYCJE"` |
| [`source/GUI/Application/DiskManagerApp.cs:784`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/DiskManagerApp.cs#L784) | `"GOTOWE"` |
| [`source/GUI/Application/HexViewerApp.cs:338`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/HexViewerApp.cs#L338) | `"NASTEPNE"` |
| [`source/GUI/Application/KernelAdvancedApp.cs:64`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/KernelAdvancedApp.cs#L64) | `"Liczba watkow schedulera"` |
| [`source/GUI/Application/KernelAdvancedApp.cs:66`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/KernelAdvancedApp.cs#L66) | `"CPU obslugiwane obecnie przez scheduler"` |
| [`source/GUI/Application/KernelAdvancedApp.cs:88`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/KernelAdvancedApp.cs#L88) | `"Automatyczne kolekcje wykonane przez runtime"` |
| [`source/GUI/Application/KernelAdvancedApp.cs:223`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/KernelAdvancedApp.cs#L223) | `"HARDWARE: SNAPSHOT TYLKO DO ODCZYTU"` |
| [`source/GUI/Application/KernelAdvancedApp.cs:239`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/KernelAdvancedApp.cs#L239) | `"ODSWIEZONO SNAPSHOT KERNELA"` |
| [`source/GUI/Application/MusicPlayerApp.cs:239`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/MusicPlayerApp.cs#L239) | `"Zatrzymany"` |
| [`source/GUI/Application/NetworkAdvancedApp.cs:15`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/NetworkAdvancedApp.cs#L15) | `"Network - funkcja usunieta"` |
| [`source/GUI/Application/NotepadApp.cs:369`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/NotepadApp.cs#L369) | `"Nowy dokument"` |
| [`source/GUI/Application/NotepadApp.cs:433`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/NotepadApp.cs#L433) | `"Anulowano"` |
| [`source/GUI/Application/NotepadApp.cs:466`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/NotepadApp.cs#L466) | `"Anulowano"` |
| [`source/GUI/Application/NotepadApp.cs:508`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/NotepadApp.cs#L508) | `"Anulowano"` |
| [`source/GUI/Application/NotepadApp.cs:609`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/NotepadApp.cs#L609) | `"Anulowano"` |
| [`source/GUI/Application/PaintApp.cs:134`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/PaintApp.cs#L134) | `"Error generowania pliku"` |
| [`source/GUI/Application/PaintApp.cs:144`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/PaintApp.cs#L144) | `"Plik: "` |
| [`source/GUI/Application/PaintApp.cs:520`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/PaintApp.cs#L520) | `"NOWY"` |
| [`source/GUI/Application/QuickNotesApp.cs:111`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/QuickNotesApp.cs#L111) | `"★ Nowa notatka"` |
| [`source/GUI/Application/QuickNotesApp.cs:347`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/QuickNotesApp.cs#L347) | `"Plik: "` |
| [`source/GUI/Application/ScreenshotApp.cs:27`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/ScreenshotApp.cs#L27) | `"Zrzut ekranu"` |
| [`source/GUI/Application/ScreenshotApp.cs:33`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/ScreenshotApp.cs#L33) | `"Zrzut ekranu"` |
| [`source/GUI/Application/ScreenshotApp.cs:136`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/ScreenshotApp.cs#L136) | `"ZRZUT EKRANU ZOnderqOS"` |
| [`source/GUI/Application/ScreenshotApp.cs:171`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/ScreenshotApp.cs#L171) | `"Skrot klawiszowy: Ctrl + Alt + S robi natychmiastowy zrzut"` |
| [`source/GUI/Application/SecurityCenterApp.cs:600`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SecurityCenterApp.cs#L600) | `"SUMA SHA-256 (PREFIX)"` |
| [`source/GUI/Application/SettingsToolWindow.cs:26`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SettingsToolWindow.cs#L26) | `"GOTOWE"` |
| [`source/GUI/Application/SettingsToolWindow.cs:55`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SettingsToolWindow.cs#L55) | `"ODSWIEZONO"` |
| [`source/GUI/Application/SettingsToolWindow.cs:170`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SettingsToolWindow.cs#L170) | `"GOTOWE"` |
| [`source/GUI/Application/SpriteStudioApp.cs:351`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SpriteStudioApp.cs#L351) | `"// Wygenerowano przez ZOnderqOS Sprite Studio"` |
| [`source/GUI/Application/SpriteStudioApp.cs:367`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SpriteStudioApp.cs#L367) | `"Saved tablice C# w: "` |
| [`source/GUI/Application/SpriteStudioApp.cs:554`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SpriteStudioApp.cs#L554) | `"OPERACJE"` |
| [`source/GUI/Application/StorageInfoApp.cs:20`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/StorageInfoApp.cs#L20) | `"Diski i partycje"` |
| [`source/GUI/Application/StorageInfoApp.cs:20`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/StorageInfoApp.cs#L20) | `"Magazyn danych - ZOnderqOS"` |
| [`source/GUI/Application/SysBenchmarkApp.cs:508`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SysBenchmarkApp.cs#L508) | `"Error katalogu VFS"` |
| [`source/GUI/Application/SysBenchmarkApp.cs:908`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SysBenchmarkApp.cs#L908) | `" ms [Suma: 0x"` |
| [`source/GUI/Application/SysBenchmarkApp.cs:1003`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SysBenchmarkApp.cs#L1003) | `"JEDNOSTKA CENTRALNA (CPU):"` |
| [`source/GUI/Application/SysBenchmarkApp.cs:1017`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SysBenchmarkApp.cs#L1017) | `"PODSYSTEM PAMIECI OPERACYJNEJ (RAM):"` |
| [`source/GUI/Application/SystemLogApp.cs:66`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SystemLogApp.cs#L66) | `"PODGLAD OSTATNICH WPISOW"` |
| [`source/GUI/Application/SystemLogApp.cs:67`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SystemLogApp.cs#L67) | `"STRZALKI GORA/DOL: PRZEWIJANIE  \\|  DELETE: WYCZYSC (2X)"` |
| [`source/GUI/Application/SystemLogApp.cs:207`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SystemLogApp.cs#L207) | `"TYLKO ROOT MOZE CZYSIC LOGI"` |
| [`source/GUI/Application/SystemLogApp.cs:218`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SystemLogApp.cs#L218) | `"DELETE PONOWNIE W CIAGU 6 S ABY WYCZYSC LOG"` |
| [`source/GUI/Application/SystemLogApp.cs:237`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SystemLogApp.cs#L237) | `"LOG WYCZYSZCZONY"` |
| [`source/GUI/Application/SystemLogApp.cs:237`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SystemLogApp.cs#L237) | `"LOG WYCZYSZCZONY TYLKO W PAMIECI"` |
| [`source/GUI/Application/SystemUpdaterApp.cs:580`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/SystemUpdaterApp.cs#L580) | `"Data wydania: "` |
| [`source/GUI/Application/TaskManagerModernApp.cs:2049`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/TaskManagerModernApp.cs#L2049) | `"RAM ZAINSTALOWANY"` |
| [`source/GUI/Application/TimerApp.cs:484`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/TimerApp.cs#L484) | `"Strefa: UTC"` |
| [`source/GUI/Application/UserAccountsApp.cs:242`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/UserAccountsApp.cs#L242) | `"ANULOWANO"` |
| [`source/GUI/Application/UserAccountsApp.cs:326`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Application/UserAccountsApp.cs#L326) | `"ZA DUZO PROB - ODCZEKAJ "` |
| [`source/GUI/BootSplash.cs:96`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/BootSplash.cs#L96) | `"GOTOWE"` |
| [`source/GUI/CommandPalette.cs:264`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/CommandPalette.cs#L264) | `"Wpisz np. terminal, settings, disk..."` |
| [`source/GUI/LoginScreenManager.cs:146`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/LoginScreenManager.cs#L146) | `"ODSWIEZONO LISTE KONT"` |
| [`source/GUI/LoginScreenManager.cs:567`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/LoginScreenManager.cs#L567) | `"ZOnderqOS chroni pulpit przed dostepem bez uwierzytelnienia."` |
| [`source/GUI/LoginScreenManager.cs:580`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/LoginScreenManager.cs#L580) | `"WPISZ LOGIN RECZNIE"` |
| [`source/GUI/NotificationCenter.cs:617`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/NotificationCenter.cs#L617) | `"WYCZYSC"` |
| [`source/GUI/ScreenshotService.cs:48`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/ScreenshotService.cs#L48) | `"ZRZUT EKRANU"` |
| [`source/GUI/ScreenshotService.cs:49`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/ScreenshotService.cs#L49) | `"Saved zrzut ekranu"` |
| [`source/GUI/ScreenshotService.cs:50`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/ScreenshotService.cs#L50) | `"Plik: "` |
| [`source/GUI/ScreenshotService.cs:58`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/ScreenshotService.cs#L58) | `"ZRZUT EKRANU"` |
| [`source/GUI/ScreenshotService.cs:59`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/ScreenshotService.cs#L59) | `"Error zapisu zrzutu"` |
| [`source/GUI/StorageMountManager.cs:43`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/StorageMountManager.cs#L43) | `"Montowanie zablokowane"` |
| [`source/GUI/StorageMountManager.cs:57`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/StorageMountManager.cs#L57) | `"Odmontowanie zablokowane"` |
| [`source/GUI/StorageMountManager.cs:69`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/StorageMountManager.cs#L69) | `"Odmontowanie nie powiodlo sie"` |
| [`source/GUI/StorageMountManager.cs:76`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/StorageMountManager.cs#L76) | `"MAGAZYN"` |
| [`source/GUI/StorageMountManager.cs:77`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/StorageMountManager.cs#L77) | `"Wolumin odmontowany"` |
| [`source/GUI/StorageMountManager.cs:84`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/StorageMountManager.cs#L84) | `"Error odmontowania woluminu"` |
| [`source/GUI/StorageMountManager.cs:93`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/StorageMountManager.cs#L93) | `"MAGAZYN"` |
| [`source/GUI/Taskbar.cs:138`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Taskbar.cs#L138) | `"centrum aplik"` |
| [`source/GUI/Taskbar.cs:408`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/GUI/Taskbar.cs#L408) | `"MAGAZYN"` |
| [`source/Security/FirewallManager.cs:104`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Security/FirewallManager.cs#L104) | `"Ruch sieciowy WWW (HTTP)"` |
| [`source/Security/FirewallManager.cs:105`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Security/FirewallManager.cs#L105) | `"Ruch szyfrowany SSL/TLS (HTTPS)"` |
| [`source/Security/FirewallManager.cs:109`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Security/FirewallManager.cs#L109) | `"Blokada zdalnego dostepu SSH"` |
| [`source/Security/FirewallManager.cs:110`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Security/FirewallManager.cs#L110) | `"Blokada nieszyfrowanego protokolu Telnet"` |
| [`source/Security/FirewallManager.cs:111`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Security/FirewallManager.cs#L111) | `"Blokada portu SMB / NetBIOS"` |
| [`source/Shell/Commands/CmdGui.cs:23`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Shell/Commands/CmdGui.cs#L23) | `$"Error uruchamiania GUI: {ex.Message}"` |
| [`source/Shell/Commands/CmdNetwork.cs:17`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Shell/Commands/CmdNetwork.cs#L17) | `"  net dns &lt;domena&gt;                 - Rozwiaz nazwe domeny"` |
| [`source/Shell/Commands/CmdNetwork.cs:89`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/source/Shell/Commands/CmdNetwork.cs#L89) | `"Nieznana podkomenda: "` |

### Shipped update files (11 candidates)

| File / line | Detected literal |
|---|---|
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:1`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L1) | `=== ZOnderqOS Gen 3 - Aktualizacja 1.0.2 (Build 3120) ===` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:2`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L2) | `Data wydania: 2026-11-09` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:4`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L4) | `Glowne nowosci w tej aktualizacji:` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:5`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L5) | `1. W pelni autonomiczny start systemu z aktywnego slotu rozruchowego (Limine v10).` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:6`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L6) | `2. Automatyczny wybor slotu A/B bez koniecznosci recznego wyboru przez uzytkownika.` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:7`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L7) | `3. Centralny LocalizationManager: obsluga jezykow pl-PL oraz en-US.` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:8`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L8) | `4. Zaostrzona weryfikacja integralnosci cyfrowej HMAC-SHA256 pakietow i kernela.` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:9`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L9) | `5. Zabezpieczenie przed uszkodzeniem obcych partycji (Kali Linux, Windows, EFI).` |
| [`output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt:10`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/payload/root/Co_Nowego_W_Aktualizacji.txt#L10) | `6. Optymalizacja zuzycia pamieci RAM (ponizej 1 GB dla bazowego systemu).` |
| [`output-3095/updates-package/update.manifest:7`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/update.manifest#L7) | `SEVERITY=Zalecana` |
| [`output-3095/updates-package/update.manifest:14`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/output-3095/updates-package/update.manifest#L14) | `CHANGELOG=Aktualizacja jadra Cosmos 3.0.95 x64 Native Core;Obsluga bezpiecznego przelaczania slotow rozruchowych A/B;Automatyczny watchdog awaryjny po nieudanym starcie;W` |

### Host tooling and developer scripts (6 candidates)

| File / line | Detected literal |
|---|---|
| [`tools/prepare-zonderq-usb.sh:115`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/tools/prepare-zonderq-usb.sh#L115) | `'Existing ZONDERQDATA partition found on %s. Formatting pominiete, zachowywanie istniejacych danych uzytkownika.\n'` |
| [`tools/prune-branch-archive.sh:113`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/tools/prune-branch-archive.sh#L113) | `"Zamknięte podczas porządkowania branchy. **Nie scalono tego PR-a.** Pełna historia starego brancha została zachowana pod tagiem \\`$tag\\` (SHA \\`${sha_for[$branch]}\\`). M` |
| [`tools/qemu-ram-soak-60min.sh:12`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/tools/qemu-ram-soak-60min.sh#L12) | `"[SOAK] Brak $ISO_PATH lub $ELF_PATH. Uruchamiam cosmos build -a x64..."` |
| [`tools/qemu-ram-soak-60min.sh:71`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/tools/qemu-ram-soak-60min.sh#L71) | `"[SOAK][FAIL] Brak próbek [MEM_SAMPLE] w serial.log"` |
| [`verify-builds.sh:12`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/verify-builds.sh#L12) | `"Brak 'cosmos' w PATH."` |
| [`verify-builds.sh:31`](https://github.com/dwierzbicki11/ZOnderqOS/blob/ef268607666ed7602d8002c474bc5bdd24794981/verify-builds.sh#L31) | `"$label build skonczyl sie bez $iso"` |

## Validation and remaining gaps

- The audit GitHub Action ran a repository-wide tracked-file inventory and exited with failure **because the English-only runtime policy is not satisfied**.
- Existing `test_alpha_english_ui_contract.py` regression checks passed in the same workflow. This confirms those old checks are insufficiently comprehensive.
- No full Cosmos build, QEMU GUI walkthrough, USB update application, or real-hardware end-to-end language verification is asserted here.
- The enumerated strings remain unchanged on `main`. This PR is an audit tool/report, **not a translation patch**.

## Remediation order

1. Translate boot/login/lock messages and application labels, keeping UI measurements and ASCII glyph constraints in mind.
2. Translate A/B update diagnostics, security labels, shell errors, and application sample-program output without changing functional identifiers or paths.
3. Rebuild and re-sign the bundled update manifest/package only after determining supported signature/key handling and compatibility.
4. Translate host-tool operator messages. Consider preserving command confirmation tokens or clearly communicating changes; do not silently alter confirmation logic.
5. Extend high-recall contracts to catch whole-file Polish strings, and run QEMU/physical smoke checks before declaring 100%.
