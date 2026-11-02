using System;
using System.IO;
using ZonderqOS;

// Disposable FAT32 sector images, no mounted filesystem or physical device.
class Image : IFatSectorReader
{
    public uint SectorSize { get; set; } = 512;
    public ulong SectorCount { get; set; }
    public uint Clusters = 65525;
    public uint FatSectors;
    public byte[] Boot, Fat0, Fat1;
    public ulong FailAt = ulong.MaxValue;
    public int Reads;
    public Image(uint size = 512, byte clusterSectors = 1, uint clusters = 65525)
    {
        Clusters = clusters;
        SectorSize = size;
        FatSectors = (uint)(((ulong)Clusters + 2) * 4 + size - 1) / size;
        SectorCount = 32 + 2UL * FatSectors + (ulong)Clusters * clusterSectors;
        Boot = new byte[size]; Fat0 = new byte[FatSectors * size]; Fat1 = new byte[Fat0.Length];
        Set16(Boot, 11, size); Boot[13] = clusterSectors; Set16(Boot, 14, 32); Boot[16] = 2;
        Set32(Boot, 32, (uint)SectorCount); Set32(Boot, 36, FatSectors); Set32(Boot, 44, 2);
        Boot[21] = 0xF8; Boot[510] = 0x55; Boot[511] = 0xAA;
        Entry(0, 0x0FFFFFF8); Entry(1, 0x0FFFFFFF); Entry(2, 0x0FFFFFFF);
    }
    public static void Set16(byte[] b, int p, uint v) { b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); }
    public static void Set32(byte[] b, int p, uint v) { for (int i = 0; i < 4; i++) b[p+i] = (byte)(v >> (i*8)); }
    public void Entry(uint cluster, uint value) { Set32(Fat0, (int)cluster*4, value); Set32(Fat1, (int)cluster*4, value); }
    public void ReadSector(ulong sector, byte[] buffer)
    {
        Reads++;
        if (sector >= SectorCount) throw new Exception("Read outside partition");
        if (sector == FailAt) throw new IOException("Device removed");
        if (sector == 0) { Array.Copy(Boot, buffer, buffer.Length); return; }
        if (sector >= 32 && sector < 32 + FatSectors)
            Array.Copy(Fat0, (long)(sector-32)*SectorSize, buffer, 0, buffer.Length);
        else if (sector >= 32 + FatSectors && sector < 32 + 2UL*FatSectors)
            Array.Copy(Fat1, (long)(sector-32-FatSectors)*SectorSize, buffer, 0, buffer.Length);
        else throw new Exception("Unexpected non-FAT read (FSInfo/data must not be scanned)");
    }
}
class Program
{
    static int passed;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static Fat32Space Read(Image i)
    {
        Check(Fat32Allocation.TryRead(i, out var s, out var error), error);
        Check(s.FreeBytes + s.AllocatedBytes + s.BadBytes == s.DataBytes, "accounting invariant");
        return s;
    }
    static void Reject(Image i)
    {
        Check(!Fat32Allocation.TryRead(i, out var s, out var error), "invalid image accepted");
        Check(s.DataBytes == 0 && s.FreeBytes == 0 && s.AllocatedBytes == 0 && s.BadBytes == 0, "partial statistics leaked");
        Check(!string.IsNullOrEmpty(error), "missing error");
    }
    static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static void Main()
    {
        Test("free clusters exclude reserved entries and FAT padding", () => {
            var i = new Image(); var s = Read(i);
            Check(s.FreeBytes == 65524UL*512 && s.AllocatedBytes == 512, "wrong cluster count");
        });
        Test("bad clusters and upper reserved bits", () => {
            var i = new Image(); i.Entry(3, 0xFFFFFFF7); i.Entry(4, 0xF0000000); i.Entry(5, 0xFFFFFFFF);
            var s = Read(i); Check(s.BadBytes == 512 && s.AllocatedBytes == 1024, "mask/bad count");
        });
        Test("full disk has zero free space", () => {
            var i = new Image(); for (uint c = 2; c < i.Clusters+2; c++) i.Entry(c, 0x0FFFFFFF);
            var s = Read(i); Check(s.FreeBytes == 0 && s.AllocatedBytes == s.DataBytes, "full disk misreported");
        });
        Test("active FAT one selected when mirroring disabled", () => {
            var i = new Image(); Image.Set16(i.Boot, 40, 0x81); Image.Set32(i.Fat1, 12, 0x0FFFFFFF);
            Check(Read(i).AllocatedBytes == 1024, "wrong active FAT");
        });
        Test("mirrored FAT mismatch fails closed", () => {
            var i = new Image(); Image.Set32(i.Fat1, 12, 0x0FFFFFFF); Reject(i);
        });
        Test("invalid active FAT rejected", () => { var i = new Image(); Image.Set16(i.Boot,40,0x82); Reject(i); });
        Test("4096 byte sectors and multi-sector clusters", () => {
            var i = new Image(4096,8); Check(Read(i).AllocatedBytes == 32768, "cluster byte size");
        });
        Test("truncated partition rejected before FAT reads", () => {
            var i = new Image(); i.SectorCount--; Reject(i); Check(i.Reads == 1, "read beyond boot");
        });
        Test("too-small FAT rejected", () => { var i = new Image(); Image.Set32(i.Boot,36,1); Reject(i); });
        Test("FAT16 geometry rejected", () => { var i = new Image(); Image.Set16(i.Boot,17,512); Reject(i); });
        Test("invalid cluster geometry rejected", () => { var i = new Image(); i.Boot[13]=3; Reject(i); });
        Test("invalid boot signature rejected", () => { var i = new Image(); i.Boot[510]=0; Reject(i); });
        Test("invalid FAT pointer rejected", () => { var i = new Image(); i.Entry(3, 1); Reject(i); });
        Test("out-of-range FAT pointer rejected", () => { var i = new Image(); i.Entry(3, i.Clusters+2); Reject(i); });
        Test("reserved FAT entry rejected", () => { var i = new Image(); i.Entry(3, 0x0FFFFFF0); Reject(i); });
        Test("disconnect during scan yields no partial result", () => { var i = new Image(); i.FailAt=35; Reject(i); });
        Test("trailing incomplete cluster excluded", () => {
            var i = new Image(512,8); i.SectorCount+=7; Image.Set32(i.Boot,32,(uint)i.SectorCount);
            Check(Read(i).DataBytes == 65525UL*4096, "trailing sectors counted");
        });
        Test("FSInfo ignored", () => { var i = new Image(); Image.Set16(i.Boot,48,1); Read(i); });
        Test("scan budget rejects enormous FAT before reading it", () => {
            var i = new Image(); Image.Set32(i.Boot,36,100000); i.SectorCount=10000000; Image.Set32(i.Boot,32,10000000);
            Reject(i); Check(i.Reads==1, "budget checked too late");
        });
        Test("fresh scan reflects allocation and release", () => {
            var i = new Image(); ulong initial = Read(i).FreeBytes;
            i.Entry(3, 0x0FFFFFFF); Check(Read(i).FreeBytes == initial-512, "allocation not observed");
            i.Entry(3, 0); Check(Read(i).FreeBytes == initial, "release not observed");
        });
        Test("free root cluster rejected", () => { var i = new Image(); i.Entry(2, 0); Reject(i); });
        Test("bad root cluster rejected", () => { var i = new Image(); i.Entry(2, 0x0FFFFFF7); Reject(i); });
        Test("root cluster in later FAT sector validated", () => {
            var i = new Image(); Image.Set32(i.Boot,44,1000); Reject(i);
            i.Entry(1000,0x0FFFFFFF); Read(i);
        });
        Test("multi-cluster root accepted", () => {
            var i = new Image(); i.Entry(2,3); i.Entry(3,0x0FFFFFFF);
            Check(Read(i).AllocatedBytes == 1024, "root chain counted incorrectly");
        });
        Test("invalid FAT media header rejected", () => { var i = new Image(); i.Entry(0,0); Reject(i); });
        Test("FAT media header must match BPB", () => { var i = new Image(); i.Boot[21]=0xF0; Reject(i); });
        Test("invalid reserved FAT entry one rejected", () => { var i = new Image(); i.Entry(1,0); Reject(i); });
        Test("FAT clean and error flags do not invalidate header", () => {
            var i = new Image(); i.Entry(1,0x03FFFFFF); Read(i);
        });
        Test("invalid mirrored FAT header rejected", () => {
            var i = new Image(); Image.Set32(i.Fat1,0,0); Reject(i);
        });
        Test("inactive FAT header ignored", () => {
            var i = new Image(); Image.Set16(i.Boot,40,0x81); Image.Set32(i.Fat0,0,0); Read(i);
        });
        Test("null reader rejected", () => Check(!Fat32Allocation.TryRead(null,out var s,out var e) && s.DataBytes==0, "null accepted"));
        Test("incremental scan bounds reads and publishes only on completion", () => {
            var i = new Image(); var expected = Read(i);
            Check(Fat32Allocation.TryBegin(i, out var scan, out var error), error);
            Fat32Space result = default;
            while (!scan.IsComplete) {
                int before = i.Reads;
                Check(scan.TryAdvance(3, out result, out error), error);
                Check(i.Reads-before <= 6, "step exceeded mirrored read budget");
                if (!scan.IsComplete) Check(result.DataBytes == 0 && result.FreeBytes == 0, "early totals");
            }
            Check(result.FreeBytes == expected.FreeBytes && result.AllocatedBytes == expected.AllocatedBytes, "incremental totals differ");
            int reads = i.Reads;
            Check(scan.TryAdvance(3, out result, out error) && i.Reads == reads, "completed scan reread device");
        });
        Test("incremental large volume exceeds synchronous budget", () => {
            var i = new Image(512, 1, 2200000); Reject(i);
            Check(Fat32Allocation.TryBegin(i, out var scan, out var error), error);
            Fat32Space result = default;
            while (!scan.IsComplete) Check(scan.TryAdvance(127, out result, out error), error);
            Check(result.FreeBytes == 2199999UL*512, "large volume totals");
        });
        Test("disconnect after progress permanently fails session", () => {
            var i = new Image(); Check(Fat32Allocation.TryBegin(i, out var scan, out var error), error);
            Check(scan.TryAdvance(1, out var result, out error), error);
            i.FailAt = 33;
            Check(!scan.TryAdvance(1, out result, out error) && result.DataBytes == 0, "disconnect accepted");
            i.FailAt = ulong.MaxValue; int before = i.Reads;
            Check(!scan.TryAdvance(1, out result, out error) && i.Reads == before && !scan.IsComplete, "failed session resumed");
        });
        Test("mirror mismatch after progress yields no totals", () => {
            var i = new Image(); Check(Fat32Allocation.TryBegin(i, out var scan, out var error), error);
            Check(scan.TryAdvance(1, out var result, out error), error);
            Image.Set32(i.Fat1, 512, 0x0FFFFFFF);
            Check(!scan.TryAdvance(1, out result, out error) && result.DataBytes == 0, "mirror failure leaked totals");
        });
        Test("zero step rejected without reads and can be retried", () => {
            var i = new Image(); Check(Fat32Allocation.TryBegin(i, out var scan, out var error), error);
            int before = i.Reads;
            Check(!scan.TryAdvance(0, out var result, out error) && i.Reads == before && error != null, "zero budget accepted");
            Check(scan.TryAdvance(1, out result, out error) && error == null, "valid retry failed");
        });
        Test("active FAT step reads only selected table", () => {
            var i = new Image(); Image.Set16(i.Boot, 40, 0x81); i.FailAt = 32;
            Check(Fat32Allocation.TryBegin(i, out var scan, out var error), error);
            int before = i.Reads;
            Check(scan.TryAdvance(2, out var result, out error) && i.Reads-before == 2, "inactive FAT read");
        });
        Test("independent sessions do not share progress or totals", () => {
            var a = new Image(); var b = new Image(); b.Entry(3, 0x0FFFFFFF);
            Check(Fat32Allocation.TryBegin(a, out var sa, out var error), error);
            Check(Fat32Allocation.TryBegin(b, out var sb, out error), error);
            Check(sa.TryAdvance(1, out var ra, out error), error);
            Check(sb.TryAdvance(uint.MaxValue, out var rb, out error), error);
            Check(sa.TryAdvance(uint.MaxValue, out ra, out error), error);
            Check(ra.FreeBytes == rb.FreeBytes+512, "sessions mixed");
        });
        Console.WriteLine(passed + " allocation checks passed");
    }
}
