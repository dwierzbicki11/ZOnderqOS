using System;
using System.Runtime.CompilerServices;
using ZonderqOS.Hardware;

internal static class PciBarDecoderTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        Require(PciBarDecoder.TryDecodeMemory(0xFEBF0000u, 0, out PciMemoryBar bar32), "32-bit memory BAR must decode");
        Require(bar32.Address == 0xFEBF0000ul && bar32.Kind == PciMemoryBarKind.Memory32, "32-bit BAR address/type mismatch");

        Require(PciBarDecoder.TryDecodeMemory(0x0000C00Cu, 0x00000012u, out PciMemoryBar bar64), "64-bit memory BAR must decode");
        Require(bar64.Address == 0x000000120000C000ul && bar64.Kind == PciMemoryBarKind.Memory64 && bar64.Prefetchable, "64-bit BAR address/flags mismatch");

        Require(!PciBarDecoder.TryDecodeMemory(0x0000C001u, 0, out _), "I/O BAR must fail closed");
        Require(!PciBarDecoder.TryDecodeMemory(0u, 0, out _), "unassigned BAR must fail closed");
        Require(!PciBarDecoder.TryDecodeMemory(0xFFFFFFFFu, 0, out _), "all-ones BAR must fail closed");
        Require(!PciBarDecoder.TryDecodeMemory(0x0000C002u, 0, out _), "reserved memory BAR type must fail closed");
        Require(!PciBarDecoder.TryDecodeMemory(0x0000C006u, 0, out _), "reserved memory BAR type 11b must fail closed");
        Require(!PciBarDecoder.TryDecodeMemory(0x00000004u, 0, out _), "64-bit BAR with zero address must fail closed");
        Require(!PciBarDecoder.TryDecodeMemory(0x0000C004u, 0xFFFFFFFFu, out _), "64-bit BAR with invalid high dword must fail closed");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("D2.2 BAR decoder: " + message);
    }
}
