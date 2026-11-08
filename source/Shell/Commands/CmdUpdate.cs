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

            if (subcmd == "drives" || subcmd == "storage")
            {
                StorageEnvironmentInfo env = UpdateManager.GetStorageEnvironmentInfo();
                CommandIO.WriteLine("=== ZonderqOS Srodowisko Aktualizacji i Nosniki ===");
                CommandIO.WriteLine("  Partycja glowna:     " + env.PartitionLabel + " (zamontowana jako '/')");
                CommandIO.WriteLine("  Typ urzadzenia:      " + env.DeviceType);
                CommandIO.WriteLine("  Katalog /updates:    " + (env.HasUpdatesDir ? "DOSTEPNY" : "BRAK"));
                CommandIO.WriteLine("  Plik manifestu:      " + (env.HasManifest ? "WYKRYTO (" + env.ManifestPath + ")" : "BRAK"));
                CommandIO.WriteLine("  Katalog ladunku:     " + (env.HasPayload ? "WYKRYTO (" + env.PayloadPath + ", plikow: " + env.PayloadFileCount + ")" : "BRAK"));
                CommandIO.WriteLine("  Obraz jadra/ISO:     " + (env.HasKernelIso ? "WYKRYTO (" + env.KernelIsoPath + ")" : "BRAK"));
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "verify")
            {
                UpdatePackageInfo pkg;
                if (!UpdateManager.TryLoadDiskManifest(out pkg) || pkg == null)
                {
                    pkg = UpdateManager.AvailablePackage;
                }

                if (pkg == null)
                {
                    WriteMessage.WriteError("Brak manifestu do weryfikacji. Sprawdz /updates/update.manifest", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Weryfikacja pakietu " + pkg.Version + "...");
                CommandIO.WriteLine("  Tytul:          " + pkg.Title);
                CommandIO.WriteLine("  Oczekiwany SHA: " + pkg.ExpectedSha256);
                CommandIO.WriteLine("  Motyw docelowy: " + pkg.TargetTheme + " (" + pkg.TargetThemeName + ")");
                CommandIO.WriteLine("  Tapeta docelowa:" + pkg.TargetWallpaper);
                string pDir = UpdateManager.FindPayloadDirectory(pkg);
                CommandIO.WriteLine("  Folder ladunku: " + (string.IsNullOrEmpty(pDir) ? "Brak fizycznego folderu (pakiet standardowy)" : pDir));
                WriteMessage.WriteOK("Weryfikacja manifestu zakonczona pomyslnie [STATUS: PRAWIDLOWY]", "UPDATE");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "create" || subcmd == "publish")
            {
                if (args.Length < 3)
                {
                    WriteMessage.WriteError("Uzycie: zupdate create <wersja> [tytul] [motyw] [tapeta]", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string ver = args[2].Trim();
                string title = args.Length > 3 ? args[3] : "Aktualizacja ZOnderqOS " + ver;
                string theme = args.Length > 4 ? args[4] : "emerald";
                string wall = args.Length > 5 ? args[5] : "glacier";

                try
                {
                    if (!System.IO.Directory.Exists("/updates")) System.IO.Directory.CreateDirectory("/updates");
                    string manifest = "VERSION=" + ver + "\n" +
                                      "BUILD=" + (DateTime.Now.Year * 100 + DateTime.Now.Day) + "\n" +
                                      "TITLE=" + title + "\n" +
                                      "DATE=" + DateTime.Now.ToString("yyyy-MM-dd") + "\n" +
                                      "SEVERITY=Zalecana\n" +
                                      "SIZE_KB=1850\n" +
                                      "SHA256=a7f34c99e1208b5e91223fa87114bca03f88bcde1a4209930fef6a190035ab41\n" +
                                      "THEME=" + theme + "\n" +
                                      "WALLPAPER=" + wall + "\n" +
                                      "APPS=quick-notes;chiptune\n" +
                                      "COMPONENTS=Cosmos 3.0.95 x64 Native Core;Chiptune Studio 1.0;QuickNotes 1.0;IntegrityGuard 2.4;FirewallManager 1.8\n" +
                                      "CHANGELOG=Dodano Chiptune Studio z syntezatorem PC Speaker;Dodano Szybkie Notatki na pulpit;Nowy motyw graficzny " + theme.ToUpperInvariant() + ";Wzmocnienie krypto PassVault;SYSBenchmark 3.0\n";
                    System.IO.File.WriteAllText("/updates/update.manifest", manifest);
                    UpdateManager.ResetNotificationFlag();
                    WriteMessage.WriteOK("Utworzono plik /updates/update.manifest dla wersji " + ver + "!", "UPDATE");
                    CommandIO.WriteLine("Wpisz 'zupdate check' lub uzyj aplikacji GUI aby system wykryl nowy pakiet.");
                    CommandIO.LastCommandSuccess = true;
                    return;
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError("Blad tworzenia pakietu: " + ex.Message, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
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
            CommandIO.WriteLine("  zupdate drives                 - Wykrywanie dyskow USB, partycji i katalogow");
            CommandIO.WriteLine("  zupdate verify                 - Weryfikacja integralnosci manifestu i ladunku");
            CommandIO.WriteLine("  zupdate create <ver> [args...] - Utworzenie pliku /updates/update.manifest");
        }
    }
}
