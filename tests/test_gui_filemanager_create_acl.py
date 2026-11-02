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

for name, ok in checks.items():
    if not ok:
        raise AssertionError(name)
print(f"GUI create ACL static contract: {len(checks)} PASS (source-only)")
