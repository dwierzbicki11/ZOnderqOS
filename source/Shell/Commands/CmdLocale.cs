using System;
using ZonderqOS.Localization;

namespace ZonderqOS.Commands
{
    public class CmdLocale : ICommand
    {
        public string Name => "locale";
        public string Description => "Wyswietla lub zmienia jezyk systemu / Displays or sets system language";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                CommandIO.WriteLine("Aktualny jezyk / Current language: " + LocalizationManager.CurrentLanguageDisplayName + " (" + LocalizationManager.CurrentLanguageCode + ")");
                CommandIO.WriteLine("Dostepne jezyki / Available languages: pl-PL, en-US");
                CommandIO.WriteLine("Uzycie / Usage: locale set [pl-PL|en-US]");
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
                    LocalizationManager.SetLanguage(Language.Polish);
                    WriteMessage.WriteOK("Zmieniono jezyk na: Polski (pl-PL).", "LOCALE");
                    CommandIO.LastCommandSuccess = true;
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
                    WriteMessage.WriteError("Nieznany kod jezyka / Unknown language code: " + target + " (uzyj pl-PL lub en-US)", "LOCALE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            CommandIO.WriteLine("Uzycie / Usage: locale set [pl-PL|en-US]");
            CommandIO.LastCommandSuccess = false;
        }
    }
}
