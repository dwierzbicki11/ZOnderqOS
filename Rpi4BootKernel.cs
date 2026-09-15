using System;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 shell bring-up kernel.
    ///
    /// Uses the normal Cosmos console and the real ZonderqOS Command dispatcher.
    /// Hardware input is still intentionally disabled, so this stage runs a small
    /// scripted shell self-test and then leaves the normal prompt on screen.
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
            Console.WriteLine(" REAL SHELL CORE TEST");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("UART: OFF");
            Console.WriteLine("Interrupts: OFF");
            Console.WriteLine("Scheduler: OFF");
            Console.WriteLine("Keyboard: OFF");
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
            Console.WriteLine("Shell core ready. USB keyboard support is the next hardware stage.");
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
