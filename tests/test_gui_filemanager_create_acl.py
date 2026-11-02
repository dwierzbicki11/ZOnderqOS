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

for name, ok in checks.items():
    if not ok:
        raise AssertionError(name)
print(f"GUI create ACL static contract: {len(checks)} PASS (source-only)")
