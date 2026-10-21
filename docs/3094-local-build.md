# Local ZonderqOS GUI build — Cosmos 3.0.94 ONLY

## Why plain `dotnet publish` and `cosmos build` can fail

The source at upstream `CosmosOS/Cosmos` tag `v3.0.94` defines
`Cosmos.Kernel.System.Input.KeyEvent`, `Cosmos.Kernel.System.FileSystem`,
and `Cosmos.Kernel.System.Diagnostics`.
If the local NuGet packages labelled `3.0.94` do not expose them,
`CS0234` for the missing namespaces cascades into many `CS0246`
errors in the GUI. Correct C# `using` directives will not fix a wrong
assembly. Reading `cosmos --version` or checking a preprocessor constant
does not prove the origin/content of the consumed packages.

## Reproducible local full-GUI build

Prerequisites on Kali/Debian: .NET SDK 10, git, python3, tar, clang,
lld, nasm, xorriso, and network access for the initial source checkout,
NuGet dependencies, and Limine. Example:

```sh
sudo apt update
sudo apt install -y git python3 tar clang lld nasm xorriso
cd ~/Pulpit/ZOnderqOS
git switch main
git pull --ff-only origin main
./run.sh --build
```

The new build command:

1. Clones the exact `CosmosOS/Cosmos` `v3.0.94` tag into
   `~/.cache/zonderqos-cosmos-3094/` (or
   `$ZONDERQ_3094_WORK_DIR`).
2. Rebuilds Cosmos SDK, kernel, HAL and tools from that tag via the
   upstream package builder. The upstream builder runs under a **private
   HOME and NuGet cache**, not Kali's normal package configuration.
3. Copies the tracked ZonderqOS Git HEAD into a private build stage and
   writes a *stage-only* `NuGet.Config` mapping `Cosmos.*` to the exact
   locally built feed. It never replaces the repository's `NuGet.Config`.
4. Forces a clean project restore and rejects *any* Cosmos package not
   sourced from that feed, before compiling the keyboard API smoke check
   and the complete kernel.
5. Writes the resulting image to
   `output-3094/ZonderqOS-3094-disk.iso`. The normal GUI is
   **persistent-disk backed**, not RAM-root.

Use `./run.sh --build-probe` for the separate **read-only USB diagnostic**
image (`output-3094/ZonderqOS-3094-usb.iso`).

Both commands are deterministic against the local committed HEAD and
reject changes to tracked files until those changes are committed/stashed.
The private caches can be deleted by the user independently from the
repository.

## Safety and proof boundaries

- The normal GUI will only accept a deliberately provisioned, unique FAT32
  `ZONDERQDATA` data partition of a permitted partition type. **Never**
  format Kali root, swap, or EFI to satisfy it.
- Building an ISO is **not** an installer and does not repartition disks.
- A successful build proves compilation, **not** native display EDID
  detection, complete GUI behavior on physical Broadwell, or finished
  managed SMP/OrionGC Stage 23.
- This build path deliberately never falls back to Cosmos 3.0.85 or to
  an unverified public NuGet Cosmos package.

The CI script `tests/test_3094_local_build_contract.py` checks the
build guard; the full upstream compile and QEMU tests are separate, more
expensive validation steps.
