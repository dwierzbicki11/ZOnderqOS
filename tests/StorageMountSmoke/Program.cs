using System;
using System.Collections.Generic;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.FileSystem;
using ZonderqOS;
using ZonderqOS.GUI;

// Only device/VFS/notification boundaries are doubles. Mount lookup and the
// root identity predicate are production sources; no physical disk is accessed.
namespace Cosmos.Kernel.System.Storage {
    public class Partition { public bool Approved; }
    public static class StorageManager { public static List<Partition> Partitions = new List<Partition>(); }
}
namespace Cosmos.Kernel.System.FileSystem {
    public class VfsMount { public string MountPoint, Name; public Partition Partition; }
    public static class VfsManager {
        public static List<VfsMount> Mounts = new List<VfsMount>();
        public static int UnmountCalls;
        public static bool TryUnmount(string path) { UnmountCalls++; return Mounts.RemoveAll(m => m.MountPoint == path) > 0; }
    }
}
namespace ZonderqOS {
    public static partial class Disk { public static void SetTestRoot(Partition p) { _rootPartition = p; } }
    public enum NotificationKind { Storage, Warning }
    public static class NotificationService { public static void Post(NotificationKind k,string a,string b,string c) { } }
    // Reproduces the old selection boundary for regression against the old GUI.
    public static class DataVolumePolicy {
        public static int Calls;
        public static bool IsSelectedDataPartition(Partition p) {
            Calls++; Partition selected = null;
            foreach (var item in StorageManager.Partitions) if (item.Approved) {
                if (selected != null) return false;
                selected = item;
            }
            return object.ReferenceEquals(p, selected);
        }
    }
}
class Program {
    static int passed;
    static void Check(bool ok,string message) { if (!ok) throw new Exception(message); }
    static Partition Root(string name = "rootfat") {
        var p = new Partition { Approved = true };
        StorageManager.Partitions.Clear(); StorageManager.Partitions.Add(p);
        VfsManager.Mounts.Clear(); VfsManager.UnmountCalls = 0; DataVolumePolicy.Calls = 0;
        VfsManager.Mounts.Add(new VfsMount { MountPoint = "/", Name = name });
        Disk.SetTestRoot(p); return p;
    }
    static void Test(string name,Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static void Main() {
        Test("replacement with same label is never mounted root", () => {
            Root(); Check(StorageMountManager.GetMountPoint(0) == "/", "initial root missing");
            StorageManager.Partitions[0] = new Partition { Approved = true };
            Check(StorageMountManager.GetMountPoint(0) == null, "replacement inherited root/cache");
        });
        Test("duplicate label does not hide original root", () => {
            Root(); StorageManager.Partitions.Add(new Partition { Approved = true });
            Check(StorageMountManager.GetMountPoint(0) == "/", "root lost after duplicate label");
            Check(StorageMountManager.GetMountPoint(1) == null, "duplicate shown mounted");
        });
        Test("reordered discovery follows object identity", () => {
            Root(); StorageManager.Partitions.Insert(0,new Partition());
            Check(StorageMountManager.GetMountPoint(0) == null && StorageMountManager.GetMountPoint(1) == "/", "index used as identity");
        });
        Test("lookup performs no policy reads and label changes do not retarget", () => {
            var p = Root(); p.Approved = false;
            Check(StorageMountManager.GetMountPoint(0) == "/" && DataVolumePolicy.Calls == 0, "lookup reran policy");
        });
        Test("RAM root uses same bound identity", () => {
            var p = Root("ramfat"); p.Approved = false;
            Check(StorageMountManager.GetMountPoint(0) == "/", "RAM root missing");
        });
        Test("missing or unrelated VFS root does not imply mounted", () => {
            Root(); VfsManager.Mounts.Clear(); Check(StorageMountManager.GetMountPoint(0) == null, "stale root shown");
            VfsManager.Mounts.Add(new VfsMount { MountPoint = "/", Name = "other" });
            Check(StorageMountManager.GetMountPoint(0) == null, "unrelated filesystem used");
        });
        Test("explicit VFS partition mounts still resolve", () => {
            var p = Root(); VfsManager.Mounts.Clear();
            VfsManager.Mounts.Add(new VfsMount { MountPoint = "/mnt/data", Name = "fat", Partition = p });
            Check(StorageMountManager.GetMountPoint(0) == "/mnt/data", "explicit mount missing");
        });
        Test("root unmount denied after discovery reorder", () => {
            Root(); StorageManager.Partitions.Insert(0,new Partition { Approved = true });
            Check(!StorageMountManager.TryUnmount(1,out var error) && error != null && VfsManager.UnmountCalls == 0, "root unmount reached VFS");
        });
        Test("invalid indices and absent binding rejected", () => {
            Root(); Disk.SetTestRoot(null);
            Check(!Disk.IsRootPartition(null) && StorageMountManager.GetMountPoint(0) == null, "null binding accepted");
            Check(StorageMountManager.GetMountPoint(-1) == null && StorageMountManager.GetMountPoint(10) == null, "invalid index accepted");
        });
        Test("manual mounting remains disabled", () => {
            Root(); Check(!StorageMountManager.TryMount(0,out var mount,out var error) && mount == null && error != null, "manual mount enabled");
        });
        Console.WriteLine(passed + " mount identity checks passed");
    }
}
