using System;
using ZonderqOS.Localization;

namespace ZonderqOS.Commands
{
    public class CmdLocale : ICommand
    {
        public string Name => "locale";
        public string Description => "Displays the system interface language";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                CommandIO.WriteLine("Current language: " + LocalizationManager.CurrentLanguageDisplayName + " (" + LocalizationManager.CurrentLanguageCode + ")");
                CommandIO.WriteLine("Available languages: en-US (Alpha 1)");
                CommandIO.WriteLine("Usage: locale set en-US");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            string subcmd = args[1].ToLowerInvariant();
            if (subcmd == "set" && args.Length >= 3)
            {
                string target = args[2];
                if (string.Equals(target, "pl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(target, "pl-pl", StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessage.WriteError("Polish is planned for Beta 1. Alpha 1 supports en-US only.", "LOCALE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
                else if (string.Equals(target, "en", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(target, "en-us", StringComparison.OrdinalIgnoreCase))
                {
                    LocalizationManager.SetLanguage(Language.English);
                    WriteMessage.WriteOK("Language set to: English (en-US).", "LOCALE");
                    CommandIO.LastCommandSuccess = true;
                    return;
                }
                else
                {
                    WriteMessage.WriteError("Unsupported language code: " + target + " (Alpha 1 supports en-US only)", "LOCALE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            CommandIO.WriteLine("Usage: locale set en-US");
            CommandIO.LastCommandSuccess = false;
        }
    }
}
