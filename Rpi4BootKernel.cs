using System;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 shell bring-up kernel.
    ///
    /// Uses the normal Cosmos console and the real ZonderqOS Command dispatcher.
    /// IRQ/GIC initialization is enabled and has reached this managed stage on the
    /// physical Pi. USB is brought up in small, diagnosable stages.
    /// </summary>
    public sealed class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private string currentPath = "/";
        private bool promptShown;

        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine(" ZonderqOS ARM64 - Raspberry Pi 4");
            Console.WriteLine(" XHCI STAGE 1 - OWNERSHIP + RESET");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("UART: OFF");
            Console.WriteLine("Interrupts: ON (managed path reached)");
            Console.WriteLine("Generic PCI init: OFF (Pi-specific path used)");
            Console.WriteLine("Scheduler: OFF");
            Console.WriteLine("Keyboard: OFF (xHCI staged bring-up)");
            Console.WriteLine();

            Command.Initialize();
            Console.WriteLine("ZonderqOS Command dispatcher: OK");
            Console.WriteLine();
            Console.WriteLine("Shell self-test:");
            Console.WriteLine();

            RunSelfTest("pwd");
            RunSelfTest("echo ARM64 real shell dispatcher OK");
            RunSelfTest("echo parser-one && echo parser-two");
            RunSelfTest("help");

            Console.WriteLine();
            Rpi4XhciProbe.Run();
            Console.WriteLine();

            bool resetReady = Rpi4XhciResetStage.Run();
            Console.WriteLine();
            Console.WriteLine(resetReady
                ? "xHCI Stage 1 complete. Next: DMA + command/event rings."
                : "xHCI Stage 1 failed. Rings/HID were NOT attempted.");
            Console.WriteLine();
            Console.WriteLine("Shell core ready.");
            Console.WriteLine();
        }

        protected override void Run()
        {
            if (promptShown)
                return;

            Console.Write("zonderq@rpi4:" + currentPath + "$ ");
            promptShown = true;
        }

        private void RunSelfTest(string command)
        {
            Console.Write("$ ");
            Console.WriteLine(command);
            Command.Run(command, ref currentPath);
            Console.WriteLine();
        }
    }
}
