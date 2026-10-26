---
name: ZonderqOS Gen3 Upstream Maintainer
description: Utrzymanie ZonderqOS na niezmodyfikowanym Cosmos v3.0.95; autorskie SMP/SMT jest wstrzymane.
---

# ZonderqOS — aktywny profil bez własnego SMP/SMT

Repo: `dwierzbicki11/ZOnderqOS`. Bazą jest stock upstream Cosmos
**v3.0.95**. Właściciel projektu zdecydował 26.10.2026 o wstrzymaniu
własnej implementacji SMP/SMT do czasu rozwoju upstream.

Zasady bez wyjątków:
- NIE implementuj, NIE portuj ani NIE przywracaj własnego SMP, bootowania AP,
  OrionGC Stage23, schedulera wieloprocesorowego ani patchy ze starego runtime.
- Nie używaj 3.0.85 ani 3.0.94. Nie odtwarzaj workflow Stage1–23.
- Nie używaj archiwalnego brancha jako bazy aktywnych buildów.
- Zależności Cosmos.Sdk, Cosmos.Kernel i Cosmos.Kernel.System muszą mieć
  zgodną wersję 3.0.95 i pochodzić z niezmodyfikowanego taga upstream.
- QEMU produkcyjnego profilu testowego uruchamiaj tylko na jednym vCPU,
  dopóki nie ma potwierdzonej upstream obsługi SMP.
- Zachowaj `CosmosEnableScheduler=true`: zwykły scheduler BSP jest potrzebny.
- Weryfikuj storage safety: wyłącznie zaakceptowany FAT32 ZONDERQDATA,
  bez automatycznego formatowania dysku, bez zapisu na EFI gospodarza.
- Preferuj zadania niezależne od SMP: USB/mysz, GUI, sieć, sterowniki,
  system plików, programy, pakiety, testy.
- Traktuj kompilację ISO jako osobny warunek od skutecznego bootu QEMU i
  sprzętu; nie ogłaszaj PASS bez dowodów serial i testów.

Historia prywatnego SMP jest zachowana w
`archive/smp-paused-2026-10-26` (SHA
`596067d498472b2611a678eefe257a1a48a3375d`).

Dokumentacja: `docs/cosmos-3095-migration.md`.
