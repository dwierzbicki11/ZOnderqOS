using System;
using System.Collections.Generic;
using System.Reflection;
using ZonderqOS;
using ZonderqOS.GUI.Apps;

// UI/driver boundaries only; the app and incremental scanner are production code.
namespace Cosmos.Kernel.System.Graphics {
    public class Canvas { }
    public enum ConsoleKeyEx { RightArrow, LeftArrow }
    public class KeyEvent { public ConsoleKeyEx Key; }
}
namespace Cosmos.Kernel.System.Storage {
    public class Device { public ulong BlockCount, BlockSize; public string Name; }
    public class Partition : Device { public Device Host; public ulong StartSector; }
    public static class StorageManager {
        public static int DeviceCount => 0;
        public static Device GetDevice(int i) => null;
        public static List<Partition> Partitions = new List<Partition>();
    }
}
namespace Cosmos.Kernel.System.FileSystem {
    public class VfsMount { public string MountPoint, Name, Source; }
    public static class VfsManager { public static VfsMount[] Mounts = new VfsMount[0]; }
}
namespace ZonderqOS.GUI.Icons { public enum IconType { Folder, File } }
namespace ZonderqOS.GUI { public static class StorageMountManager { public static string GetMountPoint(int i) => null; } }
namespace ZonderqOS {
    public static class Disk {
        public static IFatSectorReader Reader;
        public static bool TryBeginRootSpaceScan(out Fat32Allocation.Scan scan, out string error)
            => Fat32Allocation.TryBegin(Reader, out scan, out error);
    }
}
namespace ZonderqOS.GUI.Apps {
    public class FakeWindow { public bool Visible = true, IsMinimized; }
    public abstract class SettingsToolWindow {
        public bool IsRunning = true;
        public FakeWindow Window = new FakeWindow();
        protected int Accent, Text, Muted, Good;
        protected SettingsToolWindow(string a,string b,int x,int y,int w,int h) { }
        public virtual void Update() { }
        protected abstract void RenderContent(Cosmos.Kernel.System.Graphics.Canvas canvas);
        protected abstract void RefreshData();
        protected abstract void OnClick(int x,int y);
        protected abstract void OnKeyboard(Cosmos.Kernel.System.Graphics.KeyEvent key);
        protected int HitRow(int x,int y,int n) => 0;
        protected void SetStatus(string s,int c) { }
        protected void DrawRow(Cosmos.Kernel.System.Graphics.Canvas c,int row,ZonderqOS.GUI.Icons.IconType icon,string a,string b,string d,int color) { }
    }
}
class GuiProgram {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Invoke(StorageInfoApp app,string name,params object[] args)
        => typeof(StorageInfoApp).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,args);
    static string Value(StorageInfoApp app) => ((string[])typeof(StorageInfoApp).GetField("values",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app))[0];
    static StorageInfoApp Open(Image image) {
        Disk.Reader=image; var app=new StorageInfoApp(0,0);
        Invoke(app,"OnClick",0,0); Invoke(app,"OnClick",0,0); return app;
    }
    static void Main() {
        var image=new Image(); var app=Open(image);
        Check(image.Reads==1 && Value(app)=="SKAN 0%", "view selection performed full scan");
        Invoke(app,"RenderContent",new Cosmos.Kernel.System.Graphics.Canvas());
        Check(image.Reads==1,"render read device");
        app.Update(); Check(image.Reads==9,"update read budget");
        Console.WriteLine("PASS selection/render/update read boundaries");
        app.Window.IsMinimized=true; app.Update(); Check(image.Reads==9,"minimized read");
        app.Window.IsMinimized=false;
        for(int n=0;n<200;n++) app.Update();
        Check(Value(app).StartsWith("Wolne "),"completion absent");
        int reads=image.Reads; app.Update(); Check(image.Reads==reads,"completed reread");
        Console.WriteLine("PASS pause and completion");
        Invoke(app,"RefreshData"); Check(Value(app)=="SKAN 0%","refresh stale result");
        Invoke(app,"OnClick",0,0); reads=image.Reads; app.Update(); Check(image.Reads==reads,"view switch retained scan");
        Console.WriteLine("PASS refresh restart and view cancellation");
        image=new Image(); app=Open(image); image.FailAt=33; app.Update();
        Check(Value(app)=="NIEDOSTEPNE","failure hidden"); reads=image.Reads;
        image.FailAt=ulong.MaxValue; app.Update(); Check(image.Reads==reads,"failed scan retried");
        Console.WriteLine("PASS failure stops reads and hides partial totals");
        image=new Image(); app=Open(image); app.IsRunning=false; reads=image.Reads; app.Update();
        Check(image.Reads==reads,"closed window read");
        Console.WriteLine("PASS closed window performs no reads");
        Console.WriteLine("5 storage UI boundary checks passed");
    }
}
