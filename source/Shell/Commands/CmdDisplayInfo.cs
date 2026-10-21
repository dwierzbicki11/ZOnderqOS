using System;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.Commands
{
    /// <summary>
    /// Read-only physical display diagnostics for Cosmos Gen3 3.0.94.
    /// Does not alter the driver mode or fabricate a framebuffer size.
    /// </summary>
    public sealed class CmdDisplayInfo : ICommand
    {
        public string Name => "displayinfo";
        public string Description => "Show live display driver, framebuffer resolution and supported modes";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: displayinfo");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                Canvas canvas = Canvas.GetFullScreen();
                Mode current = canvas.Mode;
                var advertised = canvas.AvailableModes;

                CommandIO.WriteLine("[DISPLAY3094] Provider: " + canvas.Name);
                CommandIO.WriteLine("[DISPLAY3094] Framebuffer: " +
                    current.Width + "x" + current.Height + "x" + (int)current.ColorDepth);
                CommandIO.WriteLine("[DISPLAY3094] Refresh: " + canvas.RefreshRate + " Hz");
                CommandIO.WriteLine("[DISPLAY3094] Advertised modes: " + advertised.Count);

                bool nativeAdvertised = current.Width == 1366 && current.Height == 768;
                int limit = advertised.Count < 64 ? advertised.Count : 64;
                for (int i = 0; i < limit; i++)
                {
                    Mode mode = advertised[i];
                    if (mode.Width == 1366 && mode.Height == 768 &&
                        (int)mode.ColorDepth == 32)
                        nativeAdvertised = true;

                    CommandIO.WriteLine("  " + mode.Width + "x" + mode.Height +
                        "x" + (int)mode.ColorDepth);
                }
                if (advertised.Count > limit)
                    CommandIO.WriteLine("  ... truncated; more than 64 modes.");

                if (current.Width == 800 && current.Height == 600)
                {
                    CommandIO.WriteLine("[DISPLAY3094] The physical framebuffer is still 800x600.");
                    if (!nativeAdvertised)
                    {
                        CommandIO.WriteLine("[DISPLAY3094] 1366x768 is not in the current display's");
                        CommandIO.WriteLine("  reported modes. Limine's 1366x768 request fell back.");
                        CommandIO.WriteLine("  Try UEFI boot instead of Legacy/CSM if available.");
                        CommandIO.WriteLine("  If UEFI also reports 800x600, native Intel panel");
                        CommandIO.WriteLine("  modesetting (not GUI scaling) is required.");
                    }
                    else
                    {
                        CommandIO.WriteLine("[DISPLAY3094] 1366x768 is reported: this requires");
                        CommandIO.WriteLine("  investigating why display-mode negotiation failed.");
                    }
                }

                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.WriteLine("displayinfo: " + ex.Message);
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
