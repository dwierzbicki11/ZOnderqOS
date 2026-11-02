// Execute production command classes with fault-injectable storage boundaries.
// Physical files live only in a disposable host temporary directory; this is
// not a Cosmos/FAT driver or QEMU test.
using System;
using System.IO;
using System.Collections.Generic;
using ZonderqOS;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

internal static class Program
{
    static int passed;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Run(string name, bool move, string source, string destination,
                    Action setup, Action verify)
    {
        VirtualFs.Files.Clear();
        Disk.FailWrite = Disk.FailDelete = Disk.FailCopy = false;
        PermissionManager.ReadAllowed = true;
        PermissionManager.Copies = PermissionManager.Moves = 0;
        CommandIO.LastCommandSuccess = true; // deliberately stale success
        setup();
        string cwd = "/";
        ICommand command = move ? new CmdMv() : new CmdCp();
        command.Execute(new[] { move ? "mv" : "cp", source, destination }, ref cwd);
        verify();
        Console.WriteLine("PASS " + name);
        passed++;
    }
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "zonderq-storage-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source"), dest = Path.Combine(root, "dest");
        try
        {
            foreach (bool move in new[] { false, true })
            {
                foreach (byte[] payload in new[] { new byte[] { 0xff, 0xfe, 0x41, 0 }, new byte[] { 0xc3, 0x28 }, new byte[] { 0x80 } })
                {
                    Run("lossy disk to RAM transfer rejected " + move, move, source, "/tmp/dest",
                        () => System.IO.File.WriteAllBytes(source, payload),
                        () => { Check(!CommandIO.LastCommandSuccess, "lossy transfer reported success"); Check(!VirtualFs.Files.ContainsKey("/tmp/dest"), "lossy destination created"); Check(System.IO.File.Exists(source) && Convert.ToHexString(System.IO.File.ReadAllBytes(source)) == Convert.ToHexString(payload), "original bytes lost"); });
                }
                foreach (byte[] payload in new[] { Array.Empty<byte>(), new byte[] { 0xef, 0xbb, 0xbf, 0x41 }, new byte[] { 0, 0x41, 0xc4, 0x85, 0xf0, 0x9f, 0x98, 0x80 } })
                {
                    Run("lossless disk to RAM transfer " + move, move, source, "/tmp/dest",
                        () => System.IO.File.WriteAllBytes(source, payload),
                        () => { Check(CommandIO.LastCommandSuccess, "lossless transfer failed"); Check(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(VirtualFs.Files["/tmp/dest"])) == Convert.ToHexString(payload), "bytes changed during transfer"); Check(System.IO.File.Exists(source) != move, "wrong source lifetime"); });
                }
            }
            foreach (bool move in new[] { false, true })
            {
                foreach (string ramSource in new[] { "/tmp/source", "/run/source" })
                {
                    foreach (string payload in new[] { "\ud800", "\udfff", "a\ud800b", "\ud800\ud800", "\udc00\ud800" })
                    {
                        Run("malformed RAM text rejected before disk write " + move + ramSource, move, ramSource, dest,
                            () => VirtualFs.Files[ramSource] = payload,
                            () => { Check(!CommandIO.LastCommandSuccess, "malformed transfer succeeded"); Check(VirtualFs.Files.TryGetValue(ramSource, out string remaining) && remaining == payload, "RAM source changed"); Check(!System.IO.File.Exists(dest), "malformed transfer created a physical destination"); });
                    }
                    foreach (string payload in new[] { "", "plain", "\ufeffA", "\0ą\ud83d\ude00", "\ud800\udc00\udbff\udfff" })
                    {
                        Run("valid RAM text copied losslessly " + move + ramSource, move, ramSource, dest,
                            () => VirtualFs.Files[ramSource] = payload,
                            () => { Check(CommandIO.LastCommandSuccess, "valid transfer failed"); Check(Convert.ToHexString(System.IO.File.ReadAllBytes(dest)) == Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(payload)), "encoded bytes differ"); Check(VirtualFs.Files.ContainsKey(ramSource) != move, "wrong RAM source lifetime"); });
                        System.IO.File.Delete(dest);
                    }
                }
            }
            Run("partial copy cannot report success", false, source, dest,
                () => { System.IO.File.WriteAllText(source, "complete"); Disk.FailCopy = true; },
                () => { Check(!CommandIO.LastCommandSuccess, "partial copy succeeded"); Check(System.IO.File.Exists(dest), "fault must leave partial destination"); Check(PermissionManager.Copies == 0, "permissions copied after failure"); });
            System.IO.File.Delete(dest);
            Run("physical copy succeeds", false, source, dest, () => {},
                () => { Check(CommandIO.LastCommandSuccess && System.IO.File.ReadAllText(dest) == "complete", "copy failed"); Check(PermissionManager.Copies == 1, "permissions missing"); });
            System.IO.File.Delete(dest);
            Run("RAM to disk write failure preserves source", true, "/tmp/source", dest,
                () => { VirtualFs.Files["/tmp/source"] = "data"; Disk.FailWrite = true; },
                () => Check(!CommandIO.LastCommandSuccess && VirtualFs.Files.ContainsKey("/tmp/source"), "source lost"));
            Run("RAM to RAM delete failure is reported", true, "/tmp/source", "/run/dest",
                () => { VirtualFs.Files["/tmp/source"] = "data"; Disk.FailDelete = true; },
                () => Check(!CommandIO.LastCommandSuccess && VirtualFs.Files.ContainsKey("/tmp/source") && VirtualFs.Files.ContainsKey("/run/dest"), "delete failure hidden"));
            Run("RAM to disk delete failure is reported", true, "/tmp/source", dest,
                () => { VirtualFs.Files["/tmp/source"] = "data"; Disk.FailDelete = true; },
                () => Check(!CommandIO.LastCommandSuccess && VirtualFs.Files.ContainsKey("/tmp/source") && System.IO.File.Exists(dest), "delete failure hidden"));
            System.IO.File.Delete(dest);
            Run("disk to RAM delete failure is reported", true, source, "/tmp/dest",
                () => Disk.FailDelete = true,
                () => Check(!CommandIO.LastCommandSuccess && System.IO.File.Exists(source) && VirtualFs.Files.ContainsKey("/tmp/dest"), "delete failure hidden"));
            Run("disk to RAM move requires read permission", true, source, "/tmp/dest",
                () => PermissionManager.ReadAllowed = false,
                () => Check(!CommandIO.LastCommandSuccess && System.IO.File.Exists(source) && !VirtualFs.Files.ContainsKey("/tmp/dest"), "read permission bypassed"));
            Run("copy read exception clears stale success", false, source, "/tmp/dest",
                () => TestFaults.ThrowRead = true,
                () => { TestFaults.ThrowRead = false; Check(!CommandIO.LastCommandSuccess, "read failure hidden"); });
            Run("move read exception preserves source", true, source, "/tmp/dest",
                () => TestFaults.ThrowRead = true,
                () => { TestFaults.ThrowRead = false; Check(!CommandIO.LastCommandSuccess && System.IO.File.Exists(source), "read failure hidden"); });
            Run("RAM move succeeds", true, "/tmp/source", "/run/dest",
                () => VirtualFs.Files["/tmp/source"] = "data",
                () => Check(CommandIO.LastCommandSuccess && !VirtualFs.Files.ContainsKey("/tmp/source") && VirtualFs.Files["/run/dest"] == "data", "RAM move failed"));
            Run("physical move succeeds", true, source, dest, () => {},
                () => Check(CommandIO.LastCommandSuccess && !System.IO.File.Exists(source) && System.IO.File.ReadAllText(dest) == "complete" && PermissionManager.Moves == 1, "move failed"));
            Console.WriteLine(passed + " behavioral checks passed");
        }
        finally { Directory.Delete(root, true); }
    }
}
namespace ZonderqOS.SystemCore
{
    static class CommandIO { public static bool LastCommandSuccess; }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string level, string text) {} }
    static class PermissionManager
    {
        public static bool ReadAllowed = true;
        public static int Copies, Moves;
        public static bool CanRead(string path, string user) => ReadAllowed;
        public static bool CanWrite(string path, string user) => true;
        public static void RemovePermission(string path) {}
        public static void CopyPermissionsUnder(string a, string b, string user) { Copies++; }
        public static void MovePermissionsUnder(string a, string b) { Moves++; }
    }
    static class PathResolver { public static string GetAbsolutePath(string cwd, string path) => path; }
    static class VirtualFs
    {
        public static Dictionary<string, string> Files = new Dictionary<string, string>();
        public static bool IsVirtualPath(string path) => RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path);
        public static bool TryRead(string path, out string content) => Files.TryGetValue(path, out content);
    }
    static class RunFs
    {
        public static bool IsRunPath(string p) => p.StartsWith("/run/");
        public static bool FileExists(string p) => VirtualFs.Files.ContainsKey(p);
        public static bool DirectoryExists(string p) => false;
        public static bool TryMoveFile(string source, string destination, out string error)
        {
            error = "invalid move";
            if (!VirtualFs.Files.TryGetValue(source, out string value) || VirtualFs.Files.ContainsKey(destination)) return false;
            VirtualFs.Files.Add(destination, value);
            VirtualFs.Files.Remove(source);
            error = null;
            return true;
        }
    }
    static class TmpFs
    {
        // Test physical paths are under the host temp directory too. Only these
        // explicit names represent virtual nodes in the test boundary.
        public static bool IsTmpPath(string p) => p == "/tmp/source" || p == "/tmp/dest";
        public static bool FileExists(string p) => VirtualFs.Files.ContainsKey(p);
        public static bool DirectoryExists(string p) => false;
        public static bool TryMoveFile(string source, string destination, out string error)
        {
            error = "invalid move";
            if (!VirtualFs.Files.TryGetValue(source, out string value) || VirtualFs.Files.ContainsKey(destination)) return false;
            VirtualFs.Files.Add(destination, value);
            VirtualFs.Files.Remove(source);
            error = null;
            return true;
        }
    }
    static class DeviceFs { public static bool IsDevicePath(string p) => false; }
}
namespace ZonderqOS
{
    static class WriteMessage { public static void WriteError(string message, string category) {} }
    static class Disk
    {
        public static bool FailWrite, FailDelete, FailCopy;
        public static void CreateFile(string path, string content)
        {
            CommandIO.LastCommandSuccess = false;
            if (FailWrite) return;
            if (VirtualFs.IsVirtualPath(path)) VirtualFs.Files[path] = content;
            else System.IO.File.WriteAllText(path, content);
            CommandIO.LastCommandSuccess = true;
        }
        public static void DeleteFile(string path)
        {
            CommandIO.LastCommandSuccess = false;
            if (FailDelete) return;
            if (VirtualFs.IsVirtualPath(path)) VirtualFs.Files.Remove(path);
            else System.IO.File.Delete(path);
            CommandIO.LastCommandSuccess = true;
        }
        public static void CopyFile(string source, string dest)
        {
            CommandIO.LastCommandSuccess = false;
            if (FailCopy) { System.IO.File.WriteAllText(dest, "partial"); return; }
            System.IO.File.Copy(source, dest);
            CommandIO.LastCommandSuccess = true;
        }
        public static void MoveFile(string source, string dest)
        {
            CommandIO.LastCommandSuccess = false;
            System.IO.File.Move(source, dest);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
namespace ZonderqOS.Commands
{
    interface ICommand { void Execute(string[] args, ref string cwd); }
    static class TestFaults { public static bool ThrowRead; }
    static class File
    {
        public static bool Exists(string p) => System.IO.File.Exists(p);
        public static byte[] ReadAllBytes(string p)
        {
            if (TestFaults.ThrowRead) throw new IOException("Device disconnected");
            return System.IO.File.ReadAllBytes(p);
        }
        public static string ReadAllText(string p)
        {
            if (TestFaults.ThrowRead) throw new IOException("Device disconnected");
            return System.IO.File.ReadAllText(p);
        }
    }
}
