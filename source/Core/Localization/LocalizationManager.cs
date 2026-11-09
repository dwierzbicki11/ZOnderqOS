using System;
using System.Collections.Generic;

namespace ZonderqOS.Localization
{
    public enum Language
    {
        Polish,
        English
    }

    /// <summary>
    /// Central high-performance localization manager for ZonderqOS Gen 3.
    /// Provides zero-allocation localized string lookups during GUI frame renders,
    /// language switching with persistent storage, and easy extensibility.
    /// </summary>
    public static class LocalizationManager
    {
        public const string LangPolish = "pl-PL";
        public const string LangEnglish = "en-US";

        private static readonly Dictionary<string, string> StringsPl = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> StringsEn = new Dictionary<string, string>(StringComparer.Ordinal);

        public static Language CurrentLanguage { get; private set; } = Language.Polish;

        public static string CurrentLanguageCode => CurrentLanguage == Language.Polish ? LangPolish : LangEnglish;

        public static string CurrentLanguageDisplayName => CurrentLanguage == Language.Polish ? "Polski (pl-PL)" : "English (en-US)";

        public static event Action OnLanguageChanged;

        static LocalizationManager()
        {
            RegisterPolishStrings();
            RegisterEnglishStrings();
        }

        public static void Initialize()
        {
            try
            {
                string code = SystemSettings.LanguageCode;
                if (!string.IsNullOrEmpty(code))
                {
                    if (string.Equals(code, LangEnglish, StringComparison.OrdinalIgnoreCase))
                        CurrentLanguage = Language.English;
                    else
                        CurrentLanguage = Language.Polish;
                }
            }
            catch
            {
                CurrentLanguage = Language.Polish;
            }
        }

        public static bool SetLanguage(Language newLang, bool persist = true)
        {
            if (CurrentLanguage == newLang)
                return true;

            CurrentLanguage = newLang;

            if (persist)
            {
                try
                {
                    SystemSettings.SetLanguage(CurrentLanguageCode);
                }
                catch { }
            }

            try
            {
                OnLanguageChanged?.Invoke();
            }
            catch { }

            return true;
        }

        public static bool SetLanguageByCode(string code, bool persist = true)
        {
            if (string.Equals(code, LangEnglish, StringComparison.OrdinalIgnoreCase))
                return SetLanguage(Language.English, persist);

            return SetLanguage(Language.Polish, persist);
        }

        /// <summary>
        /// Fast zero-allocation lookup of localized string by key.
        /// Guaranteed not to allocate objects or garbage on repeated render calls.
        /// </summary>
        public static string T(string key)
        {
            if (key == null) return string.Empty;

            Dictionary<string, string> dict = CurrentLanguage == Language.English ? StringsEn : StringsPl;

            if (dict.TryGetValue(key, out string val))
                return val;

            // Fallback to Polish if missing in English
            if (CurrentLanguage == Language.English && StringsPl.TryGetValue(key, out val))
                return val;

            return key;
        }

        /// <summary>
        /// Formatted string localization lookup.
        /// </summary>
        public static string T(string key, params object[] args)
        {
            string format = T(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        private static void RegisterPolishStrings()
        {
            var d = StringsPl;
            // Common
            d["common.ok"] = "OK";
            d["common.cancel"] = "Anuluj";
            d["common.save"] = "Zapisz";
            d["common.close"] = "Zamknij";
            d["common.apply"] = "Zastosuj";
            d["common.back"] = "Wstecz";
            d["common.next"] = "Dalej";
            d["common.yes"] = "Tak";
            d["common.no"] = "Nie";
            d["common.error"] = "Blad";
            d["common.warning"] = "Ostrzezenie";
            d["common.info"] = "Informacja";
            d["common.refresh"] = "Odswiez";
            d["common.status"] = "Status";
            d["common.restart"] = "Uruchom ponownie";

            // Taskbar & Start Menu
            d["taskbar.start"] = "Start";
            d["start.apps"] = "Programy";
            d["start.settings"] = "Ustawienia";
            d["start.terminal"] = "Terminal";
            d["start.files"] = "Pliki";
            d["start.tasks"] = "Menedzer Zadan";
            d["start.updates"] = "Aktualizacje";
            d["start.about"] = "O systemie";
            d["start.welcome"] = "Centrum Powitalne";
            d["start.lock"] = "Zablokuj ekran";
            d["start.logout"] = "Wyloguj";
            d["start.shutdown"] = "Wylacz komputer";
            d["start.restart"] = "Uruchom ponownie";

            // Welcome App
            d["welcome.title"] = "Witamy w ZOnderqOS";
            d["welcome.tab.overview"] = "Przeglad";
            d["welcome.tab.alpha"] = "Nowosci Alfa";
            d["welcome.tab.shortcuts"] = "Skroty";
            d["welcome.tab.language"] = "Jezyk / Language";
            d["welcome.tab.about"] = "O Systemie";
            d["welcome.select_lang"] = "Wybierz jezyk systemu / Select system language:";
            d["welcome.lang_desc"] = "Zmiana jezyka wchodzi w zycie natychmiast i jest zapamietywana w ustawieniach.";
            d["welcome.alpha_stage"] = "Wersja Pre-Alpha (dazymy do Alpha 1.0 Release Candidate)";
            d["welcome.quick_launch"] = "Szybki start:";

            // Settings App
            d["settings.title"] = "Ustawienia Systemu";
            d["settings.tab.language"] = "Jezyk";
            d["settings.tab.appearance"] = "Wyglad";
            d["settings.tab.performance"] = "Wydajnosc";
            d["settings.tab.network"] = "Siec";
            d["settings.lang.current"] = "Aktualny jezyk interfejsu:";
            d["settings.lang.switch"] = "Zmien jezyk na:";
            d["settings.lang.saved"] = "Jezyk zostal zapisany i zaktualizowany.";

            // Updater App
            d["updater.title"] = "Centrum Aktualizacji ZonderqOS";
            d["updater.check"] = "Sprawdz aktualizacje";
            d["updater.install"] = "Zainstaluj aktualizacje";
            d["updater.rollback"] = "Wycofaj zmiany (Rollback)";
            d["updater.status.ready"] = "System jest gotowy i zsynchronizowany.";
            d["updater.status.up_to_date"] = "Twoj system posiada najnowsze dostepne oprogramowanie.";
            d["updater.status.available"] = "Dostepna nowa aktualizacja USB.";
            d["updater.status.mismatch"] = "Wykryto niezgodnosc kernela z dyskiem! Wymagany restart.";
            d["updater.status.reboot_req"] = "Aktualizacja zakonczona. Uruchom ponownie komputer.";
            d["updater.channel"] = "Kanal aktualizacji:";
            d["updater.active_slot"] = "Aktywny slot rozruchowy:";
            d["updater.fallback_slot"] = "Slot zapasowy (Fallback):";

            // Task Manager
            d["taskmgr.title"] = "Menedzer Zadan";
            d["taskmgr.cpu"] = "Procesor CPU";
            d["taskmgr.memory"] = "Pamiec RAM";
            d["taskmgr.processes"] = "Procesy";
            d["taskmgr.system"] = "System";
            d["taskmgr.used_ram"] = "Uzywana pamiec:";
            d["taskmgr.installed_ram"] = "Zainstalowana fizyczna:";

            // Terminal Shell Help & Output
            d["shell.help.title"] = "Dostepne polecenia powloki ZonderqOS:";
            d["shell.help.footer"] = "Wpisz '<polecenie> --help' lub 'man <polecenie>' po szczegoly.";
            d["shell.cmd.not_found"] = "Polecenie nie zostalo rozpoznane: {0}";
            d["shell.file.not_found"] = "Plik lub katalog nie istnieje: {0}";
            d["shell.access.denied"] = "Odmowa dostepu: brak uprawnien.";
            d["shell.update.check"] = "Wyszukiwanie dostepnych pakietow aktualizacji...";
            d["shell.update.verified"] = "Pakiet zweryfikowany pomyslnie (podpis HMAC i sumy SHA-256 poprawne).";
            d["shell.update.up_to_date"] = "Brak nowszych aktualizacji. System jest aktualny.";
        }

        private static void RegisterEnglishStrings()
        {
            var d = StringsEn;
            // Common
            d["common.ok"] = "OK";
            d["common.cancel"] = "Cancel";
            d["common.save"] = "Save";
            d["common.close"] = "Close";
            d["common.apply"] = "Apply";
            d["common.back"] = "Back";
            d["common.next"] = "Next";
            d["common.yes"] = "Yes";
            d["common.no"] = "No";
            d["common.error"] = "Error";
            d["common.warning"] = "Warning";
            d["common.info"] = "Information";
            d["common.refresh"] = "Refresh";
            d["common.status"] = "Status";
            d["common.restart"] = "Restart";

            // Taskbar & Start Menu
            d["taskbar.start"] = "Start";
            d["start.apps"] = "Applications";
            d["start.settings"] = "Settings";
            d["start.terminal"] = "Terminal";
            d["start.files"] = "File Manager";
            d["start.tasks"] = "Task Manager";
            d["start.updates"] = "Updates";
            d["start.about"] = "About";
            d["start.welcome"] = "Welcome Center";
            d["start.lock"] = "Lock Screen";
            d["start.logout"] = "Log Out";
            d["start.shutdown"] = "Shut Down";
            d["start.restart"] = "Restart";

            // Welcome App
            d["welcome.title"] = "Welcome to ZOnderqOS";
            d["welcome.tab.overview"] = "Overview";
            d["welcome.tab.alpha"] = "Alpha Highlights";
            d["welcome.tab.shortcuts"] = "Shortcuts";
            d["welcome.tab.language"] = "Language / Jezyk";
            d["welcome.tab.about"] = "About System";
            d["welcome.select_lang"] = "Select system language / Wybierz jezyk systemu:";
            d["welcome.lang_desc"] = "Language changes take effect immediately and are saved to persistent settings.";
            d["welcome.alpha_stage"] = "Pre-Alpha Release (targeting Alpha 1.0 Release Candidate)";
            d["welcome.quick_launch"] = "Quick Launch:";

            // Settings App
            d["settings.title"] = "System Settings";
            d["settings.tab.language"] = "Language";
            d["settings.tab.appearance"] = "Appearance";
            d["settings.tab.performance"] = "Performance";
            d["settings.tab.network"] = "Network";
            d["settings.lang.current"] = "Current interface language:";
            d["settings.lang.switch"] = "Switch language to:";
            d["settings.lang.saved"] = "Language setting saved and applied successfully.";

            // Updater App
            d["updater.title"] = "ZonderqOS Update Center";
            d["updater.check"] = "Check for updates";
            d["updater.install"] = "Install update";
            d["updater.rollback"] = "Rollback changes";
            d["updater.status.ready"] = "System is ready and synchronized.";
            d["updater.status.up_to_date"] = "Your system is up to date with the latest software.";
            d["updater.status.available"] = "New USB update package available.";
            d["updater.status.mismatch"] = "Kernel and disk mismatch detected! System reboot required.";
            d["updater.status.reboot_req"] = "Update installed successfully. Please restart your computer.";
            d["updater.channel"] = "Update channel:";
            d["updater.active_slot"] = "Active boot slot:";
            d["updater.fallback_slot"] = "Fallback slot:";

            // Task Manager
            d["taskmgr.title"] = "Task Manager";
            d["taskmgr.cpu"] = "CPU Processor";
            d["taskmgr.memory"] = "RAM Memory";
            d["taskmgr.processes"] = "Processes";
            d["taskmgr.system"] = "System";
            d["taskmgr.used_ram"] = "Used memory:";
            d["taskmgr.installed_ram"] = "Physical installed:";

            // Terminal Shell Help & Output
            d["shell.help.title"] = "Available ZonderqOS shell commands:";
            d["shell.help.footer"] = "Type '<command> --help' or 'man <command>' for more details.";
            d["shell.cmd.not_found"] = "Command not recognized: {0}";
            d["shell.file.not_found"] = "File or directory not found: {0}";
            d["shell.access.denied"] = "Access denied: insufficient permissions.";
            d["shell.update.check"] = "Searching for available update packages...";
            d["shell.update.verified"] = "Package verified successfully (HMAC signature and SHA-256 hashes valid).";
            d["shell.update.up_to_date"] = "No new updates found. System is up to date.";
        }
    }
}
