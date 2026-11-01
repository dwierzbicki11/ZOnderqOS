using System;
using System.IO;
using ZonderqOS.GUI;
using ZonderqOS.SystemCore;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;

// Production clipboard with fault-injected filesystem/permission boundaries.
// Only disposable host files are touched; this is not a Cosmos driver test.
class Program
{
    static int passed;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Run(string name, bool folder, bool cut, Action<string,string> setup,
        Action<string,string,bool,string,string> verify)
    {
        string root = Path.Combine(Path.GetTempPath(), "clipboard-" + Guid.NewGuid());
        string source = Path.Combine(root, "source"), destination = Path.Combine(root, "target");
        IODirectory.CreateDirectory(destination);
        Faults.Reset(); FileClipboard.Clear();
        if (folder) { IODirectory.CreateDirectory(source); IOFile.WriteAllText(Path.Combine(source,"a"),"A"); IOFile.WriteAllText(Path.Combine(source,"b"),"B"); }
        else IOFile.WriteAllBytes(source, new byte[] { 0, 255, 128, 1 });
        try {
            setup(source,destination);
            FileClipboard.Set(source,folder,cut);
            bool ok = FileClipboard.TryPaste(destination,out string path,out string error);
            verify(source,Path.Combine(destination,"source"),ok,path,error);
            Console.WriteLine("PASS " + name); passed++;
        } finally { Faults.Reset(); IODirectory.Delete(root,true); }
    }
    static void Main()
    {
        Run("permission failure after native move preserves only copy", false,true,
            (s,d) => Faults.PermissionFailure=true,
            (s,d,ok,p,e) => { Check(!ok && !IOFile.Exists(s) && IOFile.Exists(d),"only copy deleted after permission failure"); Check(p==d && e!=null,"destination/error missing"); });
        Run("partial recursive source deletion preserves complete destination", true,true,
            (s,d) => { Faults.RejectMove=true; Faults.PartialDelete=s; },
            (s,d,ok,p,e) => { Check(!ok && IOFile.ReadAllText(Path.Combine(d,"a"))=="A" && IOFile.ReadAllText(Path.Combine(d,"b"))=="B","complete copy deleted"); Check(!IOFile.Exists(Path.Combine(s,"a")) && IOFile.Exists(Path.Combine(s,"b")),"fault not exercised"); Check(p==d && FileClipboard.HasItem,"recovery state missing"); });
        Run("failed source file deletion preserves both copies",false,true,
            (s,d) => { Faults.RejectMove=true; Faults.DeleteFailure=s; },
            (s,d,ok,p,e) => Check(!ok && IOFile.Exists(s) && IOFile.Exists(d),"copy removed after delete failure"));
        Run("move that throws after changing filesystem is not retried",false,true,
            (s,d) => Faults.ThrowAfterMove=true,
            (s,d,ok,p,e) => Check(!ok && !IOFile.Exists(s) && IOFile.Exists(d) && Faults.Copies==0,"ambiguous move retried or destination removed"));
        Run("partial copy remains available and source is preserved",false,false,
            (s,d) => Faults.PartialCopy=true,
            (s,d,ok,p,e) => Check(!ok && IOFile.Exists(s) && IOFile.Exists(d) && p==d && e!=null,"partial copy recovery missing"));
        Run("collision after name selection preserves other file",false,false,
            (s,d) => Faults.Collision=true,
            (s,d,ok,p,e) => Check(!ok && IOFile.ReadAllText(d)=="other writer" && IOFile.Exists(s),"collision file deleted"));
        Run("normal binary copy succeeds",false,false,(s,d)=>{},
            (s,d,ok,p,e) => Check(ok && IOFile.ReadAllBytes(d)[1]==255 && IOFile.Exists(s) && FileClipboard.HasItem,"copy failed"));
        Run("native file move clears clipboard",false,true,(s,d)=>{},
            (s,d,ok,p,e) => Check(ok && IOFile.Exists(d) && !IOFile.Exists(s) && !FileClipboard.HasItem,"move failed"));
        Run("fallback directory move succeeds",true,true,(s,d)=>Faults.RejectMove=true,
            (s,d,ok,p,e) => Check(ok && !IODirectory.Exists(s) && IOFile.ReadAllText(Path.Combine(d,"b"))=="B" && !FileClipboard.HasItem,"fallback failed"));
        Run("copy permission failure retains completed data",false,false,(s,d)=>Faults.PermissionFailure=true,
            (s,d,ok,p,e) => Check(!ok && IOFile.Exists(s) && IOFile.Exists(d),"copy removed after metadata failure"));
        Console.WriteLine(passed + " clipboard checks passed");
    }
}
namespace ZonderqOS.GUI
{
    static class Faults
    {
        public static bool RejectMove,ThrowAfterMove,PermissionFailure,PartialCopy,Collision;
        public static string PartialDelete,DeleteFailure;
        public static int Copies;
        public static void Reset() { RejectMove=ThrowAfterMove=PermissionFailure=PartialCopy=Collision=false; PartialDelete=DeleteFailure=null; Copies=0; }
    }
    static class File
    {
        public static bool Exists(string p)=>IOFile.Exists(p);
        public static void Copy(string s,string d) {
            Faults.Copies++;
            if(Faults.Collision) IOFile.WriteAllText(d,"other writer");
            if(Faults.PartialCopy) { IOFile.WriteAllText(d,"partial"); throw new IOException("Full disk"); }
            IOFile.Copy(s,d);
        }
        public static void Move(string s,string d) {
            if(Faults.RejectMove) throw new IOException("Cross-device move unsupported");
            IOFile.Move(s,d);
            if(Faults.ThrowAfterMove) throw new IOException("Lost device response");
        }
        public static void Delete(string p) { if(p==Faults.DeleteFailure) throw new IOException("Device removed"); IOFile.Delete(p); }
    }
    static class Directory
    {
        public static bool Exists(string p)=>IODirectory.Exists(p);
        public static string[] GetFiles(string p)=>IODirectory.GetFiles(p);
        public static string[] GetDirectories(string p)=>IODirectory.GetDirectories(p);
        public static void CreateDirectory(string p)=>IODirectory.CreateDirectory(p);
        public static void Move(string s,string d) { if(Faults.RejectMove) throw new IOException("Cross-device move unsupported"); IODirectory.Move(s,d); }
        public static void Delete(string p,bool recursive) {
            if(p==Faults.PartialDelete) { IOFile.Delete(Path.Combine(p,"a")); throw new IOException("Device removed during recursive delete"); }
            IODirectory.Delete(p,recursive);
        }
    }
}
namespace ZonderqOS.SystemCore
{
    static class SecurityContext { public static string CurrentUser="root"; }
    static class VirtualFs { public static bool IsReadOnlyPath(string p)=>false; }
    static class UserManager { public static string GetHomeDirectory(string u)=>"/home/"+u; }
    static class SecurityLogger { public static void LogEvent(string l,string m) {} }
    static class PermissionManager
    {
        public static bool CanRead(string p,string u)=>true;
        public static bool CanWrite(string p,string u)=>true;
        public static void MovePermissionsUnder(string s,string d) { if(Faults.PermissionFailure) throw new IOException("Permission metadata write failed"); }
        public static void CopyPermissionsUnder(string s,string d,string u) { if(Faults.PermissionFailure) throw new IOException("Permission metadata write failed"); }
        public static void RemovePermissionsUnder(string p) {}
    }
}
