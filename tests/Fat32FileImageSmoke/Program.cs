using System;
using System.IO;
using ZonderqOS;

// Host adapter only: production allocation scanner, actual disposable image bytes.
// The caller owns fixture creation. This executable never opens a disk for writing.
sealed class FilePartition : IFatSectorReader, IDisposable
{
    readonly FileStream stream;
    readonly ulong start;
    public ulong SectorCount { get; }
    public uint SectorSize => 512;
    public int Reads { get; private set; }
    public FilePartition(string path, ulong start, ulong count)
    {
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        this.start = start;
        SectorCount = count;
    }
    public void ReadSector(ulong sector, byte[] buffer)
    {
        if (sector >= SectorCount || buffer.Length != SectorSize)
            throw new IOException("Read outside fixture partition");
        Reads++;
        stream.Position = checked((long)((start + sector) * SectorSize));
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) throw new EndOfStreamException("Truncated fixture sector");
            offset += read;
        }
    }
    public void Dispose() { stream.Dispose(); }
}

static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Empty(Fat32Space s)
    {
        Check(s.DataBytes == 0 && s.FreeBytes == 0 && s.AllocatedBytes == 0 && s.BadBytes == 0,
              "Failure or incomplete scan leaked statistics");
    }
    static void Main(string[] args)
    {
        // path, partition start/count, expected data/free/allocated clusters, mode
        Check(args.Length == 7, "Expected fixture path, geometry, counts and mode");
        using var reader = new FilePartition(args[0], ulong.Parse(args[1]), ulong.Parse(args[2]));
        string mode = args[6];
        Check(mode == "valid" || mode == "invalid" || mode == "disconnect", "Unknown mode");
        bool began = Fat32Allocation.TryBegin(reader, out var scan, out var error);
        if (!began)
        {
            Check(mode == "invalid" && scan == null && !string.IsNullOrEmpty(error), "Unexpected begin failure: " + error);
            Console.WriteLine("PASS rejected boot");
            return;
        }
        Fat32Space space = default;
        bool ok = true;
        while (!scan.IsComplete)
        {
            int before = reader.Reads;
            ok = scan.TryAdvance(3, out space, out error);
            Check(reader.Reads - before <= 6, "Incremental read budget exceeded");
            if (!ok) break;
            if (!scan.IsComplete) Empty(space);
            if (mode == "disconnect") reader.Dispose();
        }
        if (mode != "valid")
        {
            Check(!ok && !scan.IsComplete && !string.IsNullOrEmpty(error), "Invalid/disconnected image accepted");
            Empty(space);
            int reads = reader.Reads;
            Check(!scan.TryAdvance(3, out space, out var retryError) && retryError == error,
                  "Failure did not remain terminal");
            Check(reader.Reads == reads, "Failed scan retried I/O");
            Empty(space);
        }
        else
        {
            Check(ok && scan.IsComplete && error == null, "Valid image failed: " + error);
            Check(space.DataBytes == ulong.Parse(args[3]) * 512 &&
                  space.FreeBytes == ulong.Parse(args[4]) * 512 &&
                  space.AllocatedBytes == ulong.Parse(args[5]) * 512 && space.BadBytes == 0,
                  "Wrong on-disk allocation totals");
            Check(Fat32Allocation.TryRead(reader, out var sync, out error), error);
            Check(sync.DataBytes == space.DataBytes && sync.FreeBytes == space.FreeBytes &&
                  sync.AllocatedBytes == space.AllocatedBytes && sync.BadBytes == space.BadBytes,
                  "Synchronous and incremental results differ");
        }
        Console.WriteLine("PASS " + mode);
    }
}
