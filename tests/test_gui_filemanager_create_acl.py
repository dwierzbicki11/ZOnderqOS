#!/usr/bin/env python3
"""Static guard for GUI create ACL policy (not a filesystem integration test)."""
from pathlib import Path

source = Path("source/GUI/Application/FileManagerApp.cs").read_text(encoding="utf-8")
begin = source.index("        private void FinishCreate()")
end = source.index("        private void RefreshAll()", begin)
body = source[begin:end]

checks = {
    "readonly virtual filesystem rejected": 'VirtualFs.IsReadOnlyPath(path)' in body,
    "create requires authenticated session": 'if (!SecurityContext.IsAuthenticated ||' in body,
    "missing user identity is rejected": 'string.IsNullOrEmpty(SecurityContext.CurrentUser)' in body,
    "no implicit root fallback": 'SecurityContext.CurrentUser ?? "root"' not in body,
    "auth guard precedes disk writes": body.index('if (!SecurityContext.IsAuthenticated ||') < body.index('File.WriteAllText(path, "")'),
    "nonroot parent must be inside home": '!IsInsidePath(currentPath, UserManager.GetHomeDirectory(user))' in body,
    "nonroot parent ACL checked": '!PermissionManager.CanWrite(currentPath, user)' in body,
    "parent ACL checked before filesystem mutation": body.index('PermissionManager.CanWrite(currentPath, user)') < body.index('File.WriteAllText(path, "")'),
    "new file gets user ACL": 'PermissionManager.SetPermission(path, user, dialogMode == 1 ? 644 : 700)' in body,
    "metadata failure leaves created data intact": 'Created, but ACL update failed:' in body,
    "metadata update occurs after successful create": body.index('Directory.CreateDirectory(path)') < body.index('PermissionManager.SetPermission(path, user, dialogMode == 1 ? 644 : 700)'),
    "create dialog reset after mutation": body.index('PermissionManager.SetPermission(path, user, dialogMode == 1 ? 644 : 700)') < body.index('dialogMode = 0;\n                dialogName = "";\n                RefreshKeepStatus();'),
}
checks["create rechecks directory read access after dialog opens"] = (
    "if (!CanReadInSession(currentPath))" in body
    and body.index("if (!CanReadInSession(currentPath))") < body.index("File.Exists(path)")
    and "Permission denied: cannot create in unreadable folder" in body
)
checks["revoked create directory invalidates its own stale listing"] = (
    "string.Equals(loadedDirectoryPath, currentPath, StringComparison.Ordinal)" in body
    and body.index("ClearDeniedListing();") < body.index("File.Exists(path)")
    and "dialogMode = 0;" in body
    and 'dialogName = "";' in body
)

# The non-root home guard must reject lexical traversal before trusting a
# prefix. This is a source contract; host C# clipboard tests exercise the
# actual traversal rejection with real temporary directories.
inside_begin = source.index("        private static bool IsInsidePath(")
inside_end = source.index("        private void NavigateTo(", inside_begin)
inside_body = source[inside_begin:inside_end]
checks["home containment rejects dot segments"] = (
    "FileClipboard.IsSafeAbsolutePath(path)" in inside_body
    and "FileClipboard.IsSafeAbsolutePath(root)" in inside_body
)

# The home shortcut, toolbar and initial listing must all agree on the
# authenticated user's home. Root keeps its legacy /root boot behavior.
home_begin = source.index("        private static string ResolveSessionHome()")
home_end = source.index("        public FileManagerApp(", home_begin)
home_body = source[home_begin:home_end]
checks["home follows authenticated session"] = (
    'SecurityContext.CurrentUser == "root"' in home_body
    and 'UserManager.GetHomeDirectory(SecurityContext.CurrentUser)' in home_body
    and 'FileClipboard.IsSafeAbsolutePath(home)' in home_body
)
checks["unknown user does not inherit root home"] = (
    'home == "/root"' in home_body and 'return "/";' in home_body
)
checks["home toolbar follows session"] = 'NavigateTo(ResolveSessionHome())' in source
checks["home sidebar follows session"] = 'SidebarEntry.Location("Home", ResolveSessionHome()' in source
checks["initial folder follows session"] = 'currentPath = ResolveSessionHome();' in source

# File Manager listing, navigation and editor launches are all read
# operations. Do not rely on a write-only ACL guard to protect file names.
read_begin = source.index("        private static bool CanReadInSession(")
read_end = source.index("        private void NavigateTo(", read_begin)
read_body = source[read_begin:read_end]
checks["read requires authenticated session"] = (
    "SecurityContext.IsAuthenticated" in read_body
    and "PermissionManager.CanRead(path, SecurityContext.CurrentUser)" in read_body
    and "FileClipboard.IsSafeAbsolutePath(path)" in read_body
)
navigate_body = source[source.index("        private void NavigateTo("):source.index("        public override void HandleKeyboard(")]
checks["navigation checks ACL before enumeration"] = "if (!CanReadInSession(path))" in navigate_body
open_body = source[source.index("        private void OpenEntry("):source.index("        private void BeginCreate(")]
checks["editor launch checks ACL"] = (
    "if (!CanReadInSession(entry.FullPath))" in open_body
    and open_body.index("CanReadInSession(entry.FullPath)") < open_body.index("nanoLauncher(entry.FullPath)")
)
refresh_body = source[source.index("        private bool RefreshInternal("):source.index("        private void ApplySearchFilter(")]
checks["refresh checks ACL before listing"] = (
    "if (!CanReadInSession(targetPath))" in refresh_body
    and refresh_body.index("CanReadInSession(targetPath)") < refresh_body.index("Directory.GetDirectories(targetPath)")
)

copy_body = source[source.index("        private void CopySelected("):source.index("        private void PasteClipboard(")]
checks["copy checks read ACL before clipboard mutation"] = (
    "CanReadInSession(entry.FullPath)" in copy_body
    and copy_body.index("CanReadInSession(entry.FullPath)") < copy_body.index("FileClipboard.Set(")
)
checks["cut checks source write ACL"] = (
    "cut && !PermissionManager.CanWrite(entry.FullPath, SecurityContext.CurrentUser)" in copy_body
)
checks["cut checks parent write ACL before staging"] = (
    "Path.GetDirectoryName(entry.FullPath)" in copy_body
    and "!PermissionManager.CanWrite(sourceParent, user)" in copy_body
    and copy_body.index("!PermissionManager.CanWrite(sourceParent, user)") < copy_body.index("FileClipboard.Set(")
)
checks["cut outside home rejected before staging"] = (
    "!IsInsidePath(sourceParent, UserManager.GetHomeDirectory(user))" in copy_body
)

# Directory permissions can change while a File Manager window is open.
# Rejected F5/filter/open/copy must not reuse names from a stale snapshot.
invalidate_begin = source.index("        private void ClearDeniedListing()")
invalidate_end = source.index("        private bool RefreshInternal(", invalidate_begin)
invalidate_body = source[invalidate_begin:invalidate_end]
checks["revoked directory clears cached entries and selection"] = all(
    marker in invalidate_body for marker in (
        "entries.Clear();", "cachedEntries.Clear();",
        "hasLoadedDirectory = false;", "loadedDirectoryPath = null;",
        "selectedIndex = -1;", "scrollIndex = 0;",
        "contextMenuVisible = false;", "ResetClickSequence();"
    )
)
checks["denied refresh invalidates only its own cached path"] = (
    "string.Equals(loadedDirectoryPath, targetPath, StringComparison.Ordinal)" in refresh_body
    and "ClearDeniedListing();" in refresh_body
    and refresh_body.index("ClearDeniedListing();") < refresh_body.index("Directory.GetDirectories(targetPath)")
)
filter_begin = source.index("        private void ApplySearchFilter()")
filter_end = source.index("        private void RebuildSidebar()", filter_begin)
filter_body = source[filter_begin:filter_end]
checks["filter cannot revive revoked directory listing"] = (
    "if (!CanReadInSession(currentPath))" in filter_body
    and "ClearDeniedListing();" in filter_body
    and filter_body.index("ClearDeniedListing();") < filter_body.index("entries.AddRange(cachedEntries);")
)
checks["opening cached file rechecks parent ACL"] = (
    "if (!CanReadInSession(currentPath))" in open_body
    and open_body.index("ClearDeniedListing();") < open_body.index("FileEntry entry = entries[index];")
)
checks["clipboard selection rechecks parent ACL"] = (
    "if (!CanReadInSession(currentPath))" in copy_body
    and copy_body.index("ClearDeniedListing();") < copy_body.index("FileClipboard.Set(")
)

checks["clipboard does not stage deleted or replaced entry"] = (
    "entry.IsDirectory ? Directory.Exists(entry.FullPath) : File.Exists(entry.FullPath)" in copy_body
    and copy_body.index("Directory.Exists(entry.FullPath)") < copy_body.index("FileClipboard.Set(")
    and "Item unavailable; press F5 to refresh" in copy_body
)
checks["editor does not create a file from a stale tile"] = (
    "if (!File.Exists(entry.FullPath))" in open_body
    and open_body.index("if (!File.Exists(entry.FullPath))") < open_body.index("nanoLauncher(entry.FullPath)")
    and "File unavailable; press F5 to refresh" in open_body
)
checks["editor exceptions stay inside file manager"] = (
    "try\n                {\n                    nanoLauncher(entry.FullPath);" in open_body
    and "catch (Exception ex)" in open_body
    and "Open error: " in open_body
)

# New file/folder names must be safe on FAT32 before any filesystem write.
name_body = Path("source/GUI/FileManagerNamePolicy.cs").read_text(encoding="utf-8")
assert "public static bool IsSafeNewEntryName(string name)" in name_body
checks["FAT32 name policy runs before combining or writing paths"] = (
    "if (!FileManagerNamePolicy.IsSafeNewEntryName(name))" in body
    and body.index("if (!FileManagerNamePolicy.IsSafeNewEntryName(name))") < body.index("Path.Combine(currentPath, name)")
)
checks["FAT32 name policy rejects reserved and control characters"] = (
    "char.IsControl(c)" in name_body
    and all(("c == '" + char + "'") in name_body for char in "/:*?<>|")
    and ("c == '\\\\'") in name_body
    and ("c == '\"'") in name_body
    and "name[name.Length - 1] == '.'" in name_body
    and 'name == ".."' in name_body
)

checks["FAT32 name policy enforces LFN length and UTF-16 validity"] = (
    "name.Length > 255" in name_body
    and "c >= '\\uD800'" in name_body
    and "c >= '\\uDC00'" in name_body
)
checks["create uses the exact dialog name without silently trimming"] = (
    'string name = dialogName ?? "";' in body
    and 'string name = (dialogName ?? "").Trim();' not in body
    and 'string.IsNullOrWhiteSpace(name)' in body
)
checks["FAT32 device aliases are rejected even with extensions"] = (
    "int dot = name.IndexOf('.');" in name_body
    and "name.Substring(0, dot)" in name_body
    and all(('"' + device + '"') in name_body for device in ("CON", "PRN", "AUX", "NUL"))
    and "stem.Length == 4" in name_body
    and "stem[3] >= '1'" in name_body
    and "stem[3] <= '9'" in name_body
    and 'stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)' in name_body
    and 'stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)' in name_body
)

for name, ok in checks.items():
    if not ok:
        raise AssertionError(name)
print(f"GUI create ACL static contract: {len(checks)} PASS (source-only)")
