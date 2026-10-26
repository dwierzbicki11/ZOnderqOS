# ZonderqOS Gen3: stock Cosmos 3.0.95 / własne SMP wstrzymane (26.10.2026)

**Status:** kod własnego SMP/SMT został usunięty z aktywnego drzewa.
Pełne źródła i historię zachowano w gałęzi
[`archive/smp-paused-2026-10-26`](https://github.com/dwierzbicki11/ZOnderqOS/tree/archive/smp-paused-2026-10-26)
(commit `596067d498472b2611a678eefe257a1a48a3375d`).

## Zasady

- Projekt i dwa bezpośrednie pakiety są przypięte do `Cosmos.Sdk/3.0.95`;
  pakiety kompilowane są wyłącznie z oryginalnego taga `CosmosOS/Cosmos/v3.0.95`,
  z kontrolą źródła pakietów NuGet.
- **Nie aplikujemy** `patches/cosmos-smt/`, `patches/cosmos-3094/`, overlay
  Stage1–4 ani prywatnego schedulera/OrionGC. Aktywny projekt korzysta
  z *niezmodyfikowanego* upstream runtime.
- `CosmosEnableScheduler=true` zostaje — scheduler jedno-CPU jest potrzebny
  zwykłym wątkom; nie oznacza, że mamy własne SMP.
- QEMU używa bezwarunkowo **jednego vCPU** (`-smp 1`).
  Przez to test nie dowodzi gotowości SMP ani SMT.
- Na fizycznym sprzęcie nie startujemy AP przez nasz kod. To **nie jest dowód**,
  że upstream nie uruchomi innych procesorów w innych konfiguracjach.
- Nie instalujemy jeszcze na dysku: trwa weryfikacja boot / USB / storage /
  GUI oraz persystencji po migracji 3.0.95. Nie dotykać EFI hosta.

## Budowanie i testowanie

```bash
git switch main
git pull --ff-only
./run.sh --check
./run.sh --build-probe       # stock Cosmos v3.0.95, izolowany cache
./run.sh --qemu              # 1 vCPU
./run.sh --build            # GUI ISO; wymaga osobnego FAT32 ZONDERQDATA
```

`--qemu` nie buduje automatycznie ani nie modyfikuje dysków.
Artefakt read-only z GitHub Actions służy do diagnostyki, a nie do instalacji.
Wszelkie istniejące obrazy ISO w folderach output ze starych wersji należy
traktować jako **stare** i nie używać zamiast 3.0.95.

**Powrót do SMP:** tylko po oficjalnej obsłudze upstream przez Cosmos i po
osobnym dowodzie kompilacji, QEMU, GC/Thread i testów na realnym sprzęcie.
