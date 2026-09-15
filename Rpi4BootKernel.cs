using System;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 stage-2 console probe.
    ///
    /// Uses the normal Cosmos KernelConsole/System.Console path instead of the
    /// custom direct-framebuffer renderer. UART, interrupts, scheduler and input
    /// remain disabled in the ARM64 project profile so this test changes only the
    /// terminal output path.
    /// </summary>
    public sealed class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private bool promptShown;

        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine(" ZonderqOS ARM64 - Raspberry Pi 4");
            Console.WriteLine(" STANDARD COSMOS CONSOLE TEST");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("KernelConsole / System.Console: OK");
            Console.WriteLine("Managed BeforeRun(): OK");
            Console.WriteLine("UART: OFF");
            Console.WriteLine("Interrupts: OFF");
            Console.WriteLine("Scheduler: OFF");
            Console.WriteLine("Keyboard: OFF (next hardware stage)");
            Console.WriteLine();
            Console.WriteLine("Terminal output path is alive.");
        }

        protected override void Run()
        {
            if (promptShown)
                return;

            Console.WriteLine("Managed Run(): OK");
            Console.WriteLine();
            Console.Write("zonderq@rpi4:/$ ");
            promptShown = true;
        }
    }
}
