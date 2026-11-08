using System;
using System.Collections.Generic;
using ZonderqOS.Security;
using ZonderqOS.SystemCore.Updates;

namespace ZonderqOS.Commands
{
    public sealed class CmdUpdate : ICommand
    {
        public string Name => "zupdate";
        public string Description => "System update manager: check, install and rollback OS updates";

        public void Execute(string[] args, ref string currentPath)
        {
            UpdateManager.Initialize();

            if (args.Length < 2)
            {
                PrintStatusOverview();
                return;
            }

            string subcmd = args[1].ToLowerInvariant();

            if (subcmd == "status")
            {
                PrintStatusOverview();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "check")
            {
                bool force = args.Length > 2 && args[2].ToLowerInvariant() == "--force";
                CommandIO.WriteLine("Sprawdzanie dostepnosci aktualizacji w kanale: " + UpdateManager.Channel.ToString().ToUpperInvariant() + "...");
                
                UpdatePackageInfo pkg;
                string msg;
                bool found = UpdateManager.CheckForUpdates(force, out pkg, out msg);
                
                if (found && pkg != null)
                {
                    WriteMessage.WriteOK("Znaleziono nowa aktualizacje!", "UPDATE");
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("  Tytul:         " + pkg.Title);
                    CommandIO.WriteLine("  Wersja celu:   " + pkg.Version + " (Build " + pkg.BuildNumber + ")");
                    CommandIO.WriteLine("  Waznosc:       " + pkg.Severity);
                    CommandIO.WriteLine("  Data wydania:  " + pkg.ReleaseDate);
                    CommandIO.WriteLine("  Rozmiar:       " + pkg.SizeKb + " KB");
                    CommandIO.WriteLine("  Suma SHA-256:  " + pkg.ExpectedSha256);
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("Dziennik zmian (Changelog):");
                    for (int i = 0; i < pkg.Changelog.Length; i++)
                    {
                        CommandIO.WriteLine("  * " + pkg.Changelog[i]);
                    }
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("Wpisz 'zupdate apply' aby zainstalowac aktualizacje.");
                }
                else
                {
                    WriteMessage.WriteInfo(msg, "UPDATE");
                }
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "apply" || subcmd == "install")
            {
                if (SecurityContext.CurrentUser != "root")
                {
                    WriteMessage.WriteError("Wymagane uprawnienia administratora root do instalacji aktualizacji.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                UpdatePackageInfo pkg = UpdateManager.AvailablePackage;
                if (pkg == null)
                {
                    // Auto-check first
                    string chkMsg;
                    UpdateManager.CheckForUpdates(false, out pkg, out chkMsg);
                }

                if (pkg == null)
                {
                    WriteMessage.WriteInfo("Brak oczekujacych aktualizacji. Uzyj 'zupdate check --force'.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Rozpoczynanie procesu instalacji " + pkg.Version + "...");
                string error;
                bool ok = UpdateManager.ApplyUpdate(pkg, (pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK("Aktualizacja zakonczona pomyslnie! Uruchom ponownie system ('reboot').", "UPDATE");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Niepowodzenie aktualizacji: " + error, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            if (subcmd == "rollback")
            {
                if (SecurityContext.CurrentUser != "root")
                {
                    WriteMessage.WriteError("Wymagane uprawnienia administratora root do wycofania zmian.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!UpdateManager.HasRollbackSnapshot())
                {
                    WriteMessage.WriteError("Brak zapisanej migawki systemu do przywrocenia.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Przywracanie poprzedniego stanu systemu z kopii zapasowej...");
                string error;
                bool ok = UpdateManager.Rollback((pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK("Przywrocono poprzednia wersje systemu. Zalecany restart.", "UPDATE");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Niepowodzenie rollbacku: " + error, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            if (subcmd == "channel")
            {
                if (args.Length < 3)
                {
                    CommandIO.WriteLine("Aktualny kanal: " + UpdateManager.Channel.ToString().ToUpperInvariant());
                    CommandIO.WriteLine("Dostepne kanaly: stable, lts, beta, testing");
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                string ch = args[2].ToLowerInvariant();
                UpdateChannel target;
                if (ch == "beta") target = UpdateChannel.Beta;
                else if (ch == "lts") target = UpdateChannel.Lts;
                else if (ch == "testing") target = UpdateChannel.Testing;
                else target = UpdateChannel.Stable;

                UpdateManager.SetChannel(target);
                WriteMessage.WriteOK("Przestawiono kanal aktualizacji na: " + target.ToString().ToUpperInvariant(), "UPDATE");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "history")
            {
                List<string> hist = UpdateManager.GetHistory();
                CommandIO.WriteLine("=== Historia Aktualizacji Systemu ZonderqOS ===");
                if (hist.Count == 0)
                {
                    CommandIO.WriteLine("  Brak zarejestrowanych wpisow w dzienniku /var/log/update.log");
                }
                else
                {
                    for (int i = 0; i < hist.Count; i++)
                    {
                        CommandIO.WriteLine("  " + hist[i]);
                    }
                }
                CommandIO.LastCommandSuccess = true;
                return;
            }

            PrintHelp();
            CommandIO.LastCommandSuccess = false;
        }

        private void PrintStatusOverview()
        {
            CommandIO.WriteLine("=== ZonderqOS System Updater (zupdate) ===");
            CommandIO.WriteLine("  Biezaca wersja:   " + UpdateManager.CurrentVersion);
            CommandIO.WriteLine("  Kompilacja:       " + UpdateManager.CurrentBuild);
            CommandIO.WriteLine("  Kanal wydań:      " + UpdateManager.Channel.ToString().ToUpperInvariant());
            CommandIO.WriteLine("  Ostatnie badanie: " + UpdateManager.LastCheckTime);
            CommandIO.WriteLine("  Stan systemu:     " + (UpdateManager.IsRebootRequired ? "WYMAGANY RESTART (Aktualizacja oczekuje)" : "Sprawny / Aktualny"));
            CommandIO.WriteLine("  Punkt przywrocenia: " + (UpdateManager.HasRollbackSnapshot() ? "Dostepny" : "Brak"));
            CommandIO.WriteLine("");
            PrintHelp();
        }

        private void PrintHelp()
        {
            CommandIO.WriteLine("Dostepne polecenia zupdate:");
            CommandIO.WriteLine("  zupdate status                 - Podsumowanie stanu systemu i wersji");
            CommandIO.WriteLine("  zupdate check [--force]        - Sprawdzenie dostepnosci nowych pakietow");
            CommandIO.WriteLine("  zupdate apply                  - Instalacja dostepnej aktualizacji");
            CommandIO.WriteLine("  zupdate rollback               - Przywrocenie poprzedniej migawki");
            CommandIO.WriteLine("  zupdate channel [stable|lts|beta] - Zmiana galezi dystrybucji");
            CommandIO.WriteLine("  zupdate history                - Podglad historii instalacji");
        }
    }
}
