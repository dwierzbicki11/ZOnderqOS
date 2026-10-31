using System;
using System.Collections.Generic;
using System.IO;
using ZonderqOS.Hardware;
using ZonderqOS.Platform;
using ZonderqOS.SystemCore;

static class Test
{
    static int checks;

    static void Expect(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static string Capture(Func<bool> action, out bool result)
    {
        TextWriter previous = Console.Out;
        using var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            result = action();
        }
        finally
        {
            Console.SetOut(previous);
        }
        return buffer.ToString();
    }

    public static void Main()
    {
        Expect(AudioPciDiagnostics.IsAudioClass(0x04, 0x01), "legacy audio");
        Expect(AudioPciDiagnostics.IsAudioClass(0x04, 0x03), "HDA");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x04, 0x00), "video must not be audio");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x02, 0x00), "network must not be audio");
        Expect(!AudioPciDiagnostics.IsAudioClass(0x04, 0x80), "other multimedia not assumed audio");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x03, 0x00).Contains("HdAudioDriver"), "HDA candidate");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x03, 0x01).Contains("unsupported"), "unknown prog-if");
        Expect(AudioPciDiagnostics.DescribeController(0x04, 0x01, 0x00).Contains("AC97"), "legacy controller");

        PciSysfsProvider.Available = true;
        PciSysfsProvider.Devices = new List<DeviceDescriptor>
        {
            new("00:01.0", 0x8086, 0x2668, 0x04, 0x03, 0x00),
            new("00:02.0", 0x8086, 0x2415, 0x04, 0x01, 0x00),
            new("00:03.0", 0x1234, 0x1111, 0x04, 0x00, 0x00),
            new("00:04.0", 0x8086, 0x10D3, 0x02, 0x00, 0x00)
        };
        string report = Capture(() => AudioPciDiagnostics.PrintReport(false), out bool ok);
        Expect(ok, "PCI enumeration success");
        Expect(report.Contains("Discovered PCI audio functions: 2"), "count only audio");
        Expect(report.Contains("00:01.0") && report.Contains("00:02.0"), "both audio functions");
        Expect(!report.Contains("00:03.0") && !report.Contains("00:04.0"), "exclude video and NIC");
        Expect(report.Contains("driver=UNVERIFIED codec=UNKNOWN pcm=NOT_TESTED"), "no fabricated binding");

        PciSysfsProvider.Devices.Clear();
        report = Capture(() => AudioPciDiagnostics.PrintReport(false), out ok);
        Expect(ok && report.Contains("Discovered PCI audio functions: 0"), "empty scan is valid");

        PciSysfsProvider.Available = false;
        report = Capture(() => AudioPciDiagnostics.PrintReport(false), out ok);
        Expect(!ok && report.Contains("PCI discovery unavailable"), "failed scan not reported as empty success");

        Console.WriteLine("[AUDIO-PCI-HOST] PASS " + checks + " checks (stubbed PCI provider; no hardware)");
    }
}

namespace ZonderqOS.Hardware
{
    public sealed class DeviceId
    {
        public DeviceId(string address) { Address = address; }
        public string Address { get; }
    }

    public sealed class DeviceDescriptor
    {
        public DeviceDescriptor(string address, ushort vendor, ushort device, byte cls, byte sub, byte progIf)
        {
            Id = new DeviceId(address);
            VendorId = vendor;
            DeviceId = device;
            ClassCode = cls;
            Subclass = sub;
            ProgrammingInterface = progIf;
        }

        public DeviceId Id { get; }
        public ushort VendorId { get; }
        public ushort DeviceId { get; }
        public byte ClassCode { get; }
        public byte Subclass { get; }
        public byte ProgrammingInterface { get; }
    }
}

namespace ZonderqOS.Platform
{
    public static class PciSysfsProvider
    {
        public static bool Available;
        public static List<DeviceDescriptor> Devices = new();
        public static bool TryDiscover(out List<DeviceDescriptor> devices)
        {
            devices = Available ? Devices : null;
            return Available;
        }
    }
}

namespace ZonderqOS
{
    public static class CommandIO
    {
        public static void WriteLine(string message) => Console.WriteLine(message);
    }
}
