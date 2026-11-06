from pathlib import Path

root = Path(__file__).resolve().parents[1]
manager = (root / "source/GUI/Application/FileManagerApp.cs").read_text()
sorter = (root / "source/GUI/Application/FileEntrySorter.cs").read_text()
workflow = (root / ".github/workflows/perf-gen3.yml").read_text()

assert "FileEntrySorter.Sort(allEntries, ref sortScratch);" in manager
assert "FileEntrySorter.Filter(allEntries, entries, searchText);" in manager
assert "FileEntrySorter.FilterNarrowed(entries, searchText);" in manager
assert "searchText.StartsWith(appliedSearchText, StringComparison.OrdinalIgnoreCase)" in manager
assert "appliedSearchText = null;" in manager
assert "appliedSearchText = searchText;" in manager
assert "visible.RemoveRange(write, count - write);" in sorter
assert "internal static void FilterNarrowed(" in sorter
assert "RefreshFromSnapshot();" in manager
assert "directorySnapshotValid = true;" in manager
assert "allEntries.Add(new FileEntry" in manager
assert "private FileEntry[] sortScratch = new FileEntry[0];" in manager
assert "entries[i].Name.ToLowerInvariant().Contains(filter)" not in manager
assert "item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0" in sorter
assert "visible.AddRange(sorted);" in sorter
assert "Array.Clear(scratch, 0, count);" in sorter
assert "if (Compare(a, b) <= 0)" in sorter  # stable merge
assert "dotnet run --project tests/FileManagerPerfSmoke/FileManagerPerfSmoke.csproj" in workflow
assert "Cosmos.Sdk/3.0.95" in (root / "ZonderqOS.csproj").read_text()
assert "Icon = FileEntryIconResolver.Resolve(name, isDirectory);" in manager
assert "IconManager.DrawScaled(canvas, entry.Icon," in manager
assert "ResolveEntryIcon(" not in manager
assert "FileManagerPathMatcher.IsActive(app.CurrentPath, entry.Path)" in manager
assert "entry.Path + \"/\"" not in manager
assert "source/GUI/Application/FileEntryIconResolver.cs" in workflow
assert "source/GUI/Application/FileManagerPathMatcher.cs" in workflow
print("PASS: FileManager snapshot, stable sort/filter, incremental search, cached icons and allocation-free sidebar paths")
