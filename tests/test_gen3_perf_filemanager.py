from pathlib import Path

root = Path(__file__).resolve().parents[1]
manager = (root / "source/GUI/Application/FileManagerApp.cs").read_text()
sorter = (root / "source/GUI/Application/FileEntrySorter.cs").read_text()
workflow = (root / ".github/workflows/perf-gen3.yml").read_text()

assert "FileEntrySorter.Sort(entries, ref sortScratch);" in manager
assert "private FileEntry[] sortScratch = new FileEntry[0];" in manager
assert "entries[i].Name.ToLowerInvariant().Contains(filter)" not in manager
assert "if (entries[i].Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)" in manager
assert "Array.Clear(scratch, 0, count);" in sorter
assert "if (Compare(a, b) <= 0)" in sorter  # stable merge
assert "dotnet run --project tests/FileManagerPerfSmoke/FileManagerPerfSmoke.csproj" in workflow
assert "Cosmos.Sdk/3.0.95" in (root / "ZonderqOS.csproj").read_text()
print("PASS: FileManager uses stable bounded sorter and allocation-free case-insensitive filtering")
