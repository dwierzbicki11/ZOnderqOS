#!/usr/bin/env python3
"""Static guard for GUI create ACL policy (not a filesystem integration test)."""
from pathlib import Path

source = Path("source/GUI/Application/FileManagerApp.cs").read_text(encoding="utf-8")
begin = source.index("        private void FinishCreate()")
end = source.index("        private void RefreshAll()", begin)
body = source[begin:end]

checks = {
    "readonly virtual filesystem rejected": 'VirtualFs.IsReadOnlyPath(path)' in body,
    "nonroot parent must be inside home": '!IsInsidePath(currentPath, UserManager.GetHomeDirectory(user))' in body,
    "nonroot parent ACL checked": '!PermissionManager.CanWrite(currentPath, user)' in body,
    "parent ACL checked before filesystem mutation": body.index('PermissionManager.CanWrite(currentPath, user)') < body.index('File.WriteAllText(path, "")'),
    "new file gets user ACL": 'PermissionManager.SetPermission(path, user, dialogMode == 1 ? 644 : 700)' in body,
    "metadata failure leaves created data intact": 'Created, but ACL update failed:' in body,
    "metadata update occurs after successful create": body.index('Directory.CreateDirectory(path)') < body.index('PermissionManager.SetPermission(path, user, dialogMode == 1 ? 644 : 700)'),
    "create dialog reset after mutation": body.index('PermissionManager.SetPermission(path, user, dialogMode == 1 ? 644 : 700)') < body.index('dialogMode = 0;\n                dialogName = "";\n                RefreshKeepStatus();'),
}
for name, ok in checks.items():
    if not ok:
        raise AssertionError(name)
print(f"GUI create ACL static contract: {len(checks)} PASS (source-only)")
