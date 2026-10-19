using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
using Sys = Cosmos.Kernel.System;

namespace Cosmos3094SmpBoot;

/// <summary>
/// Isolated Cosmos 3.0.94 boot probe; no ZonderqOS 3.0.85 binaries are used.
/// The HAL must complete Stage 4/5 before BeforeRun emits the BOOT marker.
/// </summary>
public sealed class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Log.WriteString("[SMP3094-PROBE] BOOT\n");
    }

    protected override void Run()
    {
        // Keep the kernel alive for a post-proof QEMU stability window.
        Thread.Sleep(100);
    }
}
