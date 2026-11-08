using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.GUI;
using ZonderqOS.SystemCore;

// Host-only dependencies. The test compiles the actual production FileClipboard
// and uses real temporary files, not a simulated in-memory filesystem.
namespace ZonderqOS
{
    internal static class SecurityContext
    {
        public static string CurrentUser = "root";
        public static bool IsAuthenticated = true;
    }

    internal static class UserManager
    {
        public static string HomeDirectory = Path.GetTempPath();
        public static string GetHomeDirectory(string user) => HomeDirectory;
    }

    internal static class SecurityLogger
    {
        public static void LogEvent(string level, string message) { }
    }

    internal static class PermissionManager
    {
        public static bool FailMove;
        public static bool FailCopy;
        public static bool FailCleanup;
        public static string DenyWritePath = "";
        public static string DenyReadPath = "";
        public static bool CanRead(string path, string user) => path != DenyReadPath;
        public static bool CanWrite(string path, string user) => path != DenyWritePath;
        public static void MovePermissionsUnder(string source, string destination)
        {
            if (FailMove)
                throw new IOException("simulated move metadata failure");
        }
        public static void CopyPermissionsUnder(string source, string destination, string user)
        {
            if (FailCopy)
                throw new IOException("simulated copy metadata failure");
        }
        public static void RemovePermissionsUnder(string destination)
        {
            if (FailCleanup)
                throw new IOException("simulated cleanup metadata failure");
        }
    }
}

namespace ZonderqOS.SystemCore
{
    internal static class VirtualFs
    {
        public static bool IsReadOnlyPath(string path) => false;
    }
}

internal static class Program
{
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("Clipboard regression: " + message);
    }

    private static void Case(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "zonderqos-gui-clipboard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FileClipboard.Clear();
        PermissionManager.FailMove = false;
        PermissionManager.FailCopy = false;
        PermissionManager.FailCleanup = false;
        PermissionManager.DenyWritePath = "";
        PermissionManager.DenyReadPath = "";
        SecurityContext.CurrentUser = "root";
        SecurityContext.IsAuthenticated = true;
        UserManager.HomeDirectory = root;
        try
        {
            action(root);
        }
        finally
        {
            FileClipboard.Clear();
            PermissionManager.FailMove = false;
            PermissionManager.FailCopy = false;
            PermissionManager.FailCleanup = false;
            PermissionManager.DenyWritePath = "";
            PermissionManager.DenyReadPath = "";
            SecurityContext.CurrentUser = "root";
            SecurityContext.IsAuthenticated = true;
            Directory.Delete(root, true);
        }
    }

    private static void RenameCommittedButMetadataFails()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "original.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "irreplaceable");
            FileClipboard.Set(source, false, true);
            PermissionManager.FailMove = true;
            bool ok = FileClipboard.TryPaste(destination, out string retained, out string error);
            Check(!ok, "metadata failure reported");
            Check(!string.IsNullOrEmpty(retained) && File.Exists(retained),
                "committed rename destination survives metadata exception");
            Check(File.ReadAllText(retained) == "irreplaceable", "moved bytes survive");
            Check(!File.Exists(source), "source was renamed");
            Check(!FileClipboard.HasItem, "clipboard cleared when original source vanished");
            Check(error != null && error.Contains("destination retained"),
                "user receives retained destination warning");
        });
    }

    private static void DirectoryRenameCommittedButMetadataFails()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "original");
            string nested = Path.Combine(source, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "data.txt"), "important");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            FileClipboard.Set(source, true, true);
            PermissionManager.FailMove = true;
            bool ok = FileClipboard.TryPaste(destination, out string retained, out string error);
            Check(!ok && Directory.Exists(retained), "renamed directory retained");
            Check(File.ReadAllText(Path.Combine(retained, "nested", "data.txt")) == "important",
                "nested data retained");
            Check(!Directory.Exists(source), "old directory no longer exists");
            Check(!FileClipboard.HasItem, "directory cut cleared after rename");
        });
    }

    private static void CopyMetadataFailureRetainsDestination()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "original.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "safe");
            FileClipboard.Set(source, false, false);
            PermissionManager.FailCopy = true;
            PermissionManager.FailCleanup = true;
            bool ok = FileClipboard.TryPaste(destination, out string retained, out string error);
            Check(!ok && retained != null && File.ReadAllText(retained) == "safe", "copy metadata failure retains complete destination");
            Check(File.Exists(source), "copy failure preserves source");
            Check(Directory.GetFiles(destination).Length == 1, "completed copy target retained");
            Check(FileClipboard.HasItem, "copy clipboard remains reusable");
            Check(error != null && error.Contains("simulated copy metadata"),
                "original error survives cleanup metadata exception");
        });
    }

    private static void SuccessfulCopyAndCut()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "original.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "payload");
            FileClipboard.Set(source, false, false);
            Check(FileClipboard.TryPaste(destination, out string first, out string firstError),
                "copy succeeds");
            Check(firstError == null && File.Exists(source) && File.Exists(first),
                "copy leaves source intact");
            Check(FileClipboard.HasItem, "copy remains in clipboard");
            Check(FileClipboard.TryPaste(destination, out string second, out string secondError),
                "second copy succeeds with collision");
            Check(second != first && File.Exists(second), "collision creates unique destination");
            FileClipboard.Set(source, false, true);
            Check(FileClipboard.TryPaste(destination, out string moved, out string moveError),
                "cut succeeds");
            Check(File.Exists(moved) && !File.Exists(source), "cut moves data");
            Check(!FileClipboard.HasItem, "successful cut clears clipboard");
        });
    }

    private static void ReadOnlyAclDestinationIsRejected()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "source.txt");
            string destination = Path.Combine(root, "locked");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "secret");
            SecurityContext.CurrentUser = "alice";
            PermissionManager.DenyWritePath = destination;
            FileClipboard.Set(source, false, true);
            Check(!FileClipboard.TryPaste(destination, out string retained, out string error),
                "custom ACL denies cut into read-only destination");
            Check(retained == null && error != null && error.Contains("not writable"),
                "read-only destination error is explicit");
            Check(File.Exists(source) && Directory.GetFiles(destination).Length == 0,
                "denied cut does not modify either directory");
            Check(FileClipboard.HasItem, "denied cut retains clipboard");
            FileClipboard.Set(source, false, false);
            Check(!FileClipboard.TryPaste(destination, out retained, out error),
                "custom ACL denies copy into read-only destination");
            Check(File.Exists(source) && Directory.GetFiles(destination).Length == 0,
                "denied copy does not create a file");
        });
    }

    private static void ExpiredSessionCannotPasteAsRoot()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "original.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "protected");
            FileClipboard.Set(source, false, true);
            SecurityContext.IsAuthenticated = false;
            Check(!FileClipboard.TryPaste(destination, out string path, out string error),
                "expired session cannot move clipboard data");
            Check(path == null && error != null && error.Contains("Authentication required"),
                "expired session reports auth failure");
            Check(File.Exists(source) && Directory.GetFiles(destination).Length == 0,
                "expired session leaves filesystem unchanged");
            SecurityContext.IsAuthenticated = true;
            SecurityContext.CurrentUser = null!;
            Check(!FileClipboard.TryPaste(destination, out path, out error),
                "missing user identity cannot silently use root");
            Check(File.Exists(source) && Directory.GetFiles(destination).Length == 0,
                "missing identity leaves filesystem unchanged");
        });
    }

    private static void TraversalPathsCannotEscapeHome()
    {
        Case(root =>
        {
            string home = Path.Combine(root, "home");
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(outside);
            string source = Path.Combine(home, "private.txt");
            File.WriteAllText(source, "protected");
            SecurityContext.CurrentUser = "alice";
            UserManager.HomeDirectory = home;
            FileClipboard.Set(source, false, false);
            string escapedDestination = home + "/../outside";
            Check(!FileClipboard.TryPaste(escapedDestination, out string path, out string error),
                "dot-dot destination denied before copying");
            Check(path == null && error != null && error.Contains("outside user home"),
                "unsafe destination reports explicit error");
            Check(Directory.GetFiles(outside).Length == 0 && File.Exists(source),
                "unsafe destination leaves source and outside directory unchanged");

            string externalSource = Path.Combine(outside, "secret.txt");
            File.WriteAllText(externalSource, "external");
            PermissionManager.DenyReadPath = externalSource;
            FileClipboard.Set(home + "/../outside/secret.txt", false, false);
            Check(!FileClipboard.TryPaste(home, out path, out error),
                "dot-dot clipboard source denied");
            Check(Directory.GetFiles(home).Length == 1 && File.Exists(externalSource),
                "unsafe source does not leak into user home");
            FileClipboard.Set("relative.txt", false, false);
            Check(!FileClipboard.TryPaste(home, out path, out error),
                "relative clipboard source denied");
            Check(path == null && error != null && error.Contains("Invalid clipboard path"),
                "relative clipboard source explicitly rejected");
            FileClipboard.Set(home + "/./private.txt", false, true);
            Check(!FileClipboard.TryPaste(home, out path, out error) && error.Contains("already in this location"),
                "canonical same-location cut rejected");
            Check(File.Exists(source) && Directory.GetFiles(home).Length == 1,
                "rejected clipboard paths leave user home unchanged");
        });
    }

    private static void DirectoryAclProtectsItsOwnRoot()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "protected");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "child.txt"), "protected");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            SecurityContext.CurrentUser = "alice";
            PermissionManager.DenyWritePath = source;
            FileClipboard.Set(source, true, true);
            Check(!FileClipboard.TryPaste(destination, out string path, out string error),
                "directory root ACL denies cut");
            Check(path == null && error != null && error.Contains("Permission denied"),
                "directory root ACL reports denial");
            Check(Directory.Exists(source) && Directory.GetDirectories(destination).Length == 0,
                "denied directory cut leaves both trees intact");

            PermissionManager.DenyWritePath = "";
            PermissionManager.DenyReadPath = source;
            FileClipboard.Set(source, true, false);
            Check(!FileClipboard.TryPaste(destination, out path, out error),
                "directory root read ACL denies copy");
            Check(Directory.Exists(source) && Directory.GetDirectories(destination).Length == 0,
                "denied directory copy leaves both trees intact");
        });
    }

    private static void ClipboardIsBoundToSelectingUser()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "private.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "private data");
            SecurityContext.CurrentUser = "alice";
            FileClipboard.Set(source, false, true);
            Check(FileClipboard.HasItem, "owner can see selected item");
            SecurityContext.CurrentUser = "bob";
            Check(!FileClipboard.HasItem && FileClipboard.SourcePath == null &&
                FileClipboard.DisplayName == "" && !FileClipboard.IsCut,
                "new account cannot inspect previous clipboard");
            Check(!FileClipboard.TryPaste(destination, out string path, out string error),
                "cross-user clipboard paste rejected");
            Check(path == null && error != null && error.Contains("another user"),
                "cross-user paste has explicit error");
            Check(!FileClipboard.HasItem && File.Exists(source) &&
                Directory.GetFiles(destination).Length == 0,
                "cross-user rejection clears clipboard without touching files");
        });
    }

    private static void UnauthenticatedSelectionCannotStageFiles()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "private.txt");
            File.WriteAllText(source, "private data");
            FileClipboard.Set(source, false, true);
            SecurityContext.IsAuthenticated = false;
            FileClipboard.Set(source, false, true);
            Check(!FileClipboard.HasItem && FileClipboard.SourcePath == null,
                "expired session cannot stage or retain a cut");
            SecurityContext.IsAuthenticated = true;
            Check(!FileClipboard.HasItem, "old cut does not reappear after login");
            SecurityContext.CurrentUser = null!;
            FileClipboard.Set(source, false, false);
            Check(!FileClipboard.HasItem, "missing identity cannot stage a copy");
        });
    }

    private static void CutRequiresWritableSourceParent()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "private.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "keep");
            PermissionManager.DenyWritePath = root;
            FileClipboard.Set(source, false, true);
            Check(!FileClipboard.TryPaste(destination, out string path, out string error),
                "cut from read-only parent rejected");
            Check(path == null && error != null && error.Contains("source folder"),
                "read-only parent error is explicit");
            Check(File.Exists(source) && Directory.GetFiles(destination).Length == 0,
                "denied parent cut does not move or copy data");
            PermissionManager.DenyWritePath = "";
            Check(FileClipboard.TryPaste(destination, out path, out error),
                "cut succeeds when parent is writable");
            Check(!File.Exists(source) && File.Exists(path),
                "authorized cut moves original data");
        });
    }

    private static void NonRootCannotCutFilesOutsideHome()
    {
        Case(root =>
        {
            string home = Path.Combine(root, "home");
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(outside);
            string source = Path.Combine(outside, "shared.txt");
            File.WriteAllText(source, "shared");
            SecurityContext.CurrentUser = "alice";
            UserManager.HomeDirectory = home;
            FileClipboard.Set(source, false, true);
            Check(!FileClipboard.TryPaste(home, out string path, out string error),
                "non-root cannot cut a file from outside home");
            Check(path == null && error != null && error.Contains("source folder"),
                "external cut has explicit error");
            Check(File.Exists(source) && Directory.GetFiles(home).Length == 0,
                "external cut does not mutate either directory");
            FileClipboard.Set(source, false, false);
            Check(FileClipboard.TryPaste(home, out path, out error),
                "readable external file may still be copied into home");
            Check(File.Exists(source) && File.Exists(path),
                "copy preserves the external source");
        });
    }

    private static void RelativeDestinationCannotBecomeAbsolute()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "source.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            File.WriteAllText(source, "keep");
            FileClipboard.Set(source, false, true);
            Check(!FileClipboard.TryPaste(destination.TrimStart('/'), out string path, out string error),
                "relative destination cannot become an absolute filesystem path");
            Check(path == null && error != null && error.Contains("Invalid clipboard path"),
                "relative destination reports validation failure");
            Check(File.ReadAllText(source) == "keep" && Directory.GetFiles(destination).Length == 0,
                "relative destination rejection preserves data");
            Check(FileClipboard.HasItem, "rejected relative destination retains clipboard");
        });
    }

    private static void MissingSourceIsRejected()
    {
        Case(root =>
        {
            string source = Path.Combine(root, "missing.txt");
            string destination = Path.Combine(root, "target");
            Directory.CreateDirectory(destination);
            FileClipboard.Set(source, false, true);
            Check(!FileClipboard.TryPaste(destination, out string path, out string error),
                "missing source rejected");
            Check(path == null && !FileClipboard.HasItem, "stale clipboard cleared");
            Check(Directory.GetFiles(destination).Length == 0, "no destination created");
        });
    }

    public static int Main()
    {
        RenameCommittedButMetadataFails();
        DirectoryRenameCommittedButMetadataFails();
        CopyMetadataFailureRetainsDestination();
        SuccessfulCopyAndCut();
        MissingSourceIsRejected();
        RelativeDestinationCannotBecomeAbsolute();
        ReadOnlyAclDestinationIsRejected();
        ExpiredSessionCannotPasteAsRoot();
        TraversalPathsCannotEscapeHome();
        DirectoryAclProtectsItsOwnRoot();
        ClipboardIsBoundToSelectingUser();
        UnauthenticatedSelectionCannotStageFiles();
        CutRequiresWritableSourceParent();
        NonRootCannotCutFilesOutsideHome();
        Console.WriteLine("GUI clipboard host C#: " + assertions + " PASS");
        return 0;
    }
}
