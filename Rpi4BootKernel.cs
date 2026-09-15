using System;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 interactive ARM64 kernel.
    ///
    /// Keeps Cosmos' QEMU-oriented generic keyboard path disabled and uses the
    /// Pi-specific VL805/xHCI polling driver for a direct USB HID boot keyboard.
    /// </summary>
    public sealed class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private const int MaxInputLength = 256;

        private readonly char[] inputBuffer = new char[MaxInputLength];
        private int inputLength;
        private string currentPath = "/";
        private bool promptShown;
        private bool keyboardWasReady;

        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine(" ZonderqOS ARM64 - Raspberry Pi 4");
            Console.WriteLine(" REAL SHELL + VL805 USB KEYBOARD");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("UART: OFF");
            Console.WriteLine("Interrupts/GIC: ON");
            Console.WriteLine("Generic PCI: OFF (BCM2711 path)");
            Console.WriteLine("Cosmos generic keyboard: OFF (QEMU virtio)");
            Console.WriteLine("VL805 xHCI keyboard: polling mode");
            Console.WriteLine("Scheduler: OFF");
            Console.WriteLine();

            Command.Initialize();
            Console.WriteLine("ZonderqOS Command dispatcher: OK");
            Console.WriteLine();

            // Keep the already validated read-only probe as the mapping/discovery
            // guard before the DMA-capable keyboard driver takes ownership.
            Rpi4XhciProbe.Run();
            Console.WriteLine();

            bool controllerReady = Rpi4UsbKeyboard.Initialize();
            if (!controllerReady)
            {
                Console.WriteLine();
                Console.WriteLine("USB keyboard controller init FAILED:");
                Console.WriteLine("  " + Rpi4UsbKeyboard.LastError);
                Console.WriteLine("Shell remains alive but input is unavailable.");
            }
            else if (Rpi4UsbKeyboard.KeyboardReady)
            {
                Console.WriteLine();
                Console.WriteLine("USB HID boot keyboard: READY");
                Console.WriteLine("Type commands directly at the prompt.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("xHCI is ready; no supported direct USB boot keyboard detected yet.");
                Console.WriteLine("Plug the keyboard directly into a Raspberry Pi USB port.");
                Console.WriteLine("Hot-plug polling is active; external USB hubs are not supported yet.");
            }

            keyboardWasReady = Rpi4UsbKeyboard.KeyboardReady;
            Console.WriteLine();
        }

        protected override void Run()
        {
            if (!promptShown)
                ShowPrompt();

            if (!Rpi4UsbKeyboard.ControllerReady)
                return;

            Rpi4UsbKeyboard.PollAttach();

            if (!keyboardWasReady && Rpi4UsbKeyboard.KeyboardReady)
            {
                Console.WriteLine();
                Console.WriteLine("[USB] HID boot keyboard connected.");
                inputLength = 0;
                ShowPrompt();
            }

            int guard = 16;
            while (guard-- > 0 && Rpi4UsbKeyboard.TryReadKey(out char key))
                HandleKey(key);

            if (keyboardWasReady && !Rpi4UsbKeyboard.KeyboardReady)
            {
                Console.WriteLine();
                Console.WriteLine("[USB] Keyboard unavailable: " + Rpi4UsbKeyboard.LastError);
                inputLength = 0;
                ShowPrompt();
            }

            keyboardWasReady = Rpi4UsbKeyboard.KeyboardReady;
        }

        private void HandleKey(char key)
        {
            if (key == '\r' || key == '\n')
            {
                Console.WriteLine();

                if (inputLength > 0)
                {
                    string command = new string(inputBuffer, 0, inputLength);
                    inputLength = 0;
                    Command.Run(command, ref currentPath);
                }

                ShowPrompt();
                return;
            }

            if (key == '\b')
            {
                if (inputLength > 0)
                {
                    inputLength--;
                    Console.Write("\b \b");
                }
                return;
            }

            if (key == (char)27)
            {
                ClearCurrentLine();
                return;
            }

            if (key == '\t')
                key = ' ';

            if (key < ' ' || key > '~')
                return;

            if (inputLength >= MaxInputLength - 1)
                return;

            inputBuffer[inputLength++] = key;
            Console.Write(key);
        }

        private void ClearCurrentLine()
        {
            while (inputLength > 0)
            {
                inputLength--;
                Console.Write("\b \b");
            }
        }

        private void ShowPrompt()
        {
            Console.Write("zonderq@rpi4:" + currentPath + "$ ");
            promptShown = true;
        }
    }
}
