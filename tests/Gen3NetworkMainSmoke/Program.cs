using System;
using ZonderqOS.Hardware;
namespace ZonderqOS.Hardware
{
    public interface IPciConfigAccessor
    {
        byte Read8(byte bus, byte device, byte function, byte offset);
        ushort Read16(byte bus, byte device, byte function, byte offset);
    }
}
internal sealed class MissingPci : IPciConfigAccessor
{
    public byte Read8(byte bus, byte device, byte function, byte offset) => 0xFF;
    public ushort Read16(byte bus, byte device, byte function, byte offset) => 0xFFFF;
}
internal static class Program
{
    private static void Check(bool yes,string reason) { if(!yes) throw new Exception(reason); }
    private static void Main()
    {
        var absent = new MissingPci();
        Check(!E1000EPciIdentity.TryReadReady(absent,out _),"Missing E1000E accepted");
        Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(absent,out _,out _),"Missing IRQ route accepted");
        var bar = new PciBarDescriptor(0,PciBarKind.Memory32,0xFEB00000UL,false);
        Check(bar.Index==0 && bar.Kind==PciBarKind.Memory32 && bar.Address==0xFEB00000UL,"PCI BAR decoding");
        var nic = new Realtek810xERegisterSnapshot("00:11:22:33:44:55",false,0,false,0);
        Check(!nic.Carrier && nic.SpeedMbps==0,"Unverified NIC must not claim a link");
        Check(!string.IsNullOrWhiteSpace(Realtek810xERegisterDecoder.GetChipVariant(0)),"Chip variant fallback");
        Console.WriteLine("GEN3-NETWORK-3097 PASS: missing NIC fail-closed, IRQ, BAR, Realtek snapshot");
    }
}
