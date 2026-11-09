using System;
using System.Collections.Generic;
using System.IO;
using ZonderqOS.Security;
using ZonderqOS.SystemCore;
using ZonderqOS.SystemCore.Updates;

namespace ZonderqOS.Commands
{
    public sealed class CmdUpdate : ICommand
    {
        public string Name => "zupdate";
        public string Description => "System update manager: check, verify, install and rollback OS & kernel updates";

        public void Execute(string[] args, ref string currentPath)
        {
            UpdateManager.Initialize();
            BootSlotManager.Initialize();

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

            if (subcmd == "slots" || subcmd == "bootslots")
            {
                PrintSlotsDiagnostics();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "check")
            {
                bool force = args.Length > 2 && args[2].ToLowerInvariant() == "--force";
                CommandIO.WriteLine("Skanowanie nosnikow USB i katalogu /updates...");

                UpdatePackageInfo pkg;
                string msg;
                bool found = UpdateManager.CheckForUpdates(force, out pkg, out msg);

                if (found && pkg != null)
                {
                    WriteMessage.WriteOK("Znaleziono zweryfikowana aktualizacje USB!", "UPDATE");
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("  Tytul:         " + pkg.Title);
                    CommandIO.WriteLine("  Wersja celu:   " + pkg.Version + " (Build " + pkg.BuildNumber + ")");
                    CommandIO.WriteLine("  Architektura:  " + pkg.Architecture);
                    CommandIO.WriteLine("  Waznosc:       " + pkg.Severity);
                    CommandIO.WriteLine("  Data wydania:  " + pkg.ReleaseDate);
                    CommandIO.WriteLine("  Podpis cyfrowy:" + (pkg.IsSigned ? (pkg.IsSignatureValid ? "ZAUFANY (HMAC-SHA256 DEV-KEY)" : "NIEPRAWIDLOWY!") : "BRAK"));
                    CommandIO.WriteLine("  Suma kernela:  " + (string.IsNullOrEmpty(pkg.KernelSha256) ? "Nieokreslona" : pkg.KernelSha256));
                    CommandIO.WriteLine("  Plik kernela:  " + (string.IsNullOrEmpty(pkg.KernelImagePath) ? "Brak w pakiecie" : pkg.KernelImagePath));
                    CommandIO.WriteLine("  Liczba plikow: " + (pkg.Files != null ? pkg.Files.Count.ToString() : "0"));
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("Dziennik zmian (Changelog):");
                    for (int i = 0; i < pkg.Changelog.Length; i++)
                    {
                        CommandIO.WriteLine("  * " + pkg.Changelog[i]);
                    }
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("Wpisz 'zupdate verify' aby sprawdzic sumy SHA-256 lub 'zupdate install' aby zainstalowac.");
                }
                else
                {
                    WriteMessage.WriteInfo(msg, "UPDATE");
                }
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
                    WriteMessage.WriteError("Brak dostepnego pakietu do weryfikacji. Wloz USB z /updates/update.manifest", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Kryptograficzna weryfikacja pakietu " + pkg.Version + "...");
                string error;
                bool ok = UpdateManager.VerifyPackageIntegrity(pkg, (pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK("Integralnosc i autentycznosc pakietu potwierdzona pomyslnie [STATUS: PRAWIDLOWY]", "UPDATE");
                    CommandIO.WriteLine("  Kernel SHA-256: " + pkg.ExpectedSha256);
                    CommandIO.WriteLine("  Podpis cyfrowy: " + (pkg.IsSigned ? "ZAUFANY" : "Brak (pakiet lokalny)"));
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Weryfikacja nie powiodla sie: " + error, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                }
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
                    string chkMsg;
                    UpdateManager.CheckForUpdates(false, out pkg, out chkMsg);
                }

                if (pkg == null)
                {
                    WriteMessage.WriteInfo("Brak oczekujacych aktualizacji na USB. Uzyj 'zupdate check --force'.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string inactiveSlot = BootSlotManager.GetInactiveSlot();
                CommandIO.WriteLine($"Instalacja wersji {pkg.Version} do osobnego slotu rozruchowego (Slot {inactiveSlot})...");

                string error;
                bool ok = UpdateManager.ApplyUpdate(pkg, (pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK($"Aktualizacja zainstalowana pomyslnie w slocie {inactiveSlot}! Nowy kernel zostanie uruchomiony po restarcie.", "UPDATE");
                    CommandIO.WriteLine("Wpisz 'reboot' aby uruchomic zaktualizowany system.");
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
                    string fallbackKernel = BootSlotManager.GetSlotKernelPath(BootSlotManager.FallbackSlot);
                    if (System.IO.File.Exists(fallbackKernel))
                    {
                        CommandIO.WriteLine("Brak migawki /var/updates/backup, ale wykryto alternatywny slot rozruchowy (" + BootSlotManager.FallbackSlot + ").");
                        CommandIO.WriteLine("Wycofywanie aktywnego slotu rozruchowego (A/B Boot Rollback)...");
                        if (BootSlotManager.ManualRollback(out string rbErr))
                        {
                            WriteMessage.WriteOK("Przelaczono aktywny slot rozruchowy na " + BootSlotManager.ActiveSlot + ". Wymagany restart.", "UPDATE");
                            CommandIO.WriteLine("Wpisz 'reboot' aby uruchomic system z poprzedniego slotu.");
                            CommandIO.LastCommandSuccess = true;
                        }
                        else
                        {
                            WriteMessage.WriteError("Niepowodzenie przelaczenia slotu: " + rbErr, "UPDATE");
                            CommandIO.LastCommandSuccess = false;
                        }
                        return;
                    }

                    WriteMessage.WriteError("Brak zapisanej migawki systemu ani sprawnego slotu zapasowego do przywrocenia.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Wycofywanie aktualizacji (A/B Rollback) do poprzedniego slotu...");
                string error;
                bool ok = UpdateManager.Rollback((pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK("Przywrocono poprzedni stan i slot rozruchowy (" + BootSlotManager.ActiveSlot + "). Wymagany restart.", "UPDATE");
                    CommandIO.WriteLine("Wpisz 'reboot' aby uruchomic poprzednia wersje.");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Niepowodzenie rollbacku: " + error, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                }
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
                CommandIO.WriteLine("  Aktywny slot boot:   Slot " + env.ActiveBootSlot);
                CommandIO.WriteLine("  Uruchomiony kernel:  " + env.RunningKernelVersion + " (Build " + env.RunningKernelBuild + ")");
                CommandIO.WriteLine("  Katalog /updates:    " + (env.HasUpdatesDir ? "DOSTEPNY" : "BRAK"));
                CommandIO.WriteLine("  Plik manifestu:      " + (env.HasManifest ? "WYKRYTO (" + env.ManifestPath + ")" : "BRAK"));
                CommandIO.WriteLine("  Katalog ladunku:     " + (env.HasPayload ? "WYKRYTO (" + env.PayloadPath + ", plikow: " + env.PayloadFileCount + ")" : "BRAK"));
                CommandIO.WriteLine("  Obraz kernela:       " + (env.HasKernelBinary ? "WYKRYTO (" + env.KernelBinaryPath + ")" : "BRAK"));
                CommandIO.LastCommandSuccess = true;
                return;
            }

            PrintHelp();
            CommandIO.LastCommandSuccess = false;
        }

        private void PrintStatusOverview()
        {
            CommandIO.WriteLine("=== ZonderqOS System Updater & Boot Diagnostics ===");
            CommandIO.WriteLine("  Uruchomiony kernel:  " + KernelBuildInfo.KernelVersion + " (Build " + KernelBuildInfo.KernelBuild + ")");
            CommandIO.WriteLine("  Architektura:        " + KernelBuildInfo.Architecture + " (" + KernelBuildInfo.CosmosVersion + ")");
            CommandIO.WriteLine("  Data kompilacji:     " + KernelBuildInfo.CompileTimestamp);
            CommandIO.WriteLine("  Aktywny slot:        Slot " + BootSlotManager.ActiveSlot + " (" + KernelBuildInfo.RunningSlotPath + ")");
            CommandIO.WriteLine("  Stan biezacego slotu:" + (KernelBuildInfo.IsHealthyAndConfirmed ? "ZATWIERDZONY (HEALTHY)" : "OCZEKUJE NA POTWIERDZENIE"));
            CommandIO.WriteLine("  Wersja w /etc:       " + UpdateManager.CurrentVersion + " (Build " + UpdateManager.CurrentBuild + ")");

            // Detection of mismatch between booted kernel and disk version
            if (!string.Equals(KernelBuildInfo.KernelVersion, UpdateManager.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            {
                CommandIO.WriteLine("");
                WriteMessage.WriteInfo("[OSTRZEZENIE] WYKRYTO ROZBIEZNOSC: Uruchomiony kernel (" + KernelBuildInfo.KernelVersion +
                    ") rozni sie od wersji na dysku (" + UpdateManager.CurrentVersion + ").", "UPDATE");
                CommandIO.WriteLine("  Wymagany restart ('reboot') aby zainicjalizowac nowy kernel ze zaktualizowanego slotu.");
            }

            if (!string.IsNullOrEmpty(BootSlotManager.PendingSlot))
            {
                WriteMessage.WriteInfo("[OSTRZEZENIE] Oczekujacy slot rozruchowy: Slot " + BootSlotManager.PendingSlot +
                    " (Proby rozruchu: " + BootSlotManager.BootAttempts + "/" + BootSlotManager.MaxBootAttempts + ")", "BOOT");
            }

            CommandIO.WriteLine("");
            CommandIO.WriteLine("  Ostatnie badanie:    " + UpdateManager.LastCheckTime);
            CommandIO.WriteLine("  Zrodlo aktualizacji: " + UpdateManager.SourceMode.ToString().ToUpperInvariant());
            CommandIO.WriteLine("  Wymagany restart:    " + (UpdateManager.IsRebootRequired ? "TAK (nowy kernel oczekuje w slocie " + BootSlotManager.ActiveSlot + ")" : "Nie"));
            CommandIO.WriteLine("  Punkt przywrocenia:  " + (UpdateManager.HasRollbackSnapshot() ? "Dostepny (migawka A/B)" : "Brak"));
            CommandIO.WriteLine("");
            PrintHelp();
        }

        private void PrintSlotsDiagnostics()
        {
            CommandIO.WriteLine("=== Diagnostyka Slotow Rozruchowych A/B (Limine) ===");
            CommandIO.WriteLine("  Aktywny slot:     Slot " + BootSlotManager.ActiveSlot);
            CommandIO.WriteLine("  Slot rezerwowy:   Slot " + BootSlotManager.FallbackSlot);
            CommandIO.WriteLine("  Slot testowany:   " + (string.IsNullOrEmpty(BootSlotManager.PendingSlot) ? "Brak (stabilny)" : "Slot " + BootSlotManager.PendingSlot));
            CommandIO.WriteLine("  Licznik prob:     " + BootSlotManager.BootAttempts + " / " + BootSlotManager.MaxBootAttempts);
            CommandIO.WriteLine("");
            CommandIO.WriteLine("SLOT A:");
            CommandIO.WriteLine("  Sciezka:          " + BootSlotManager.SlotAPath);
            CommandIO.WriteLine("  Obecnosc pliku:   " + (File.Exists(BootSlotManager.SlotAPath) ? "TAK" : "BRAK"));
            CommandIO.WriteLine("  Stan zdrowia:     " + BootSlotManager.SlotAStatus);
            CommandIO.WriteLine("  Wersja kernela:   " + BootSlotManager.SlotAVersion + " (Build " + BootSlotManager.SlotABuild + ")");
            CommandIO.WriteLine("");
            CommandIO.WriteLine("SLOT B:");
            CommandIO.WriteLine("  Sciezka:          " + BootSlotManager.SlotBPath);
            CommandIO.WriteLine("  Obecnosc pliku:   " + (File.Exists(BootSlotManager.SlotBPath) ? "TAK" : "BRAK"));
            CommandIO.WriteLine("  Stan zdrowia:     " + BootSlotManager.SlotBStatus);
            CommandIO.WriteLine("  Wersja kernela:   " + (string.IsNullOrEmpty(BootSlotManager.SlotBVersion) ? "Pusty" : BootSlotManager.SlotBVersion + " (Build " + BootSlotManager.SlotBBuild + ")"));
            CommandIO.WriteLine("");
            CommandIO.WriteLine("Konfiguracja Limine:");
            CommandIO.WriteLine("  Plik w /boot:     " + (File.Exists(BootSlotManager.LimineBootConfig) ? "ZSYNCHRONIZOWANY" : "Brak"));
            CommandIO.WriteLine("  Plik w /:         " + (File.Exists(BootSlotManager.LimineRootConfig) ? "ZSYNCHRONIZOWANY" : "Brak"));
        }

        private void PrintHelp()
        {
            CommandIO.WriteLine("Dostepne polecenia zupdate:");
            CommandIO.WriteLine("  zupdate status                 - Status kernela, aktywnego slotu i wersji");
            CommandIO.WriteLine("  zupdate slots                  - Diagnostyka slotow rozruchowych A/B");
            CommandIO.WriteLine("  zupdate check [--force]        - Sprawdzenie pakietow aktualizacji na USB");
            CommandIO.WriteLine("  zupdate verify                 - Kryptograficzna weryfikacja SHA-256 i podpisu");
            CommandIO.WriteLine("  zupdate install                - Bezpieczna instalacja do nieaktywnego slotu");
            CommandIO.WriteLine("  zupdate rollback               - Powrot (Rollback) do poprzedniego slotu");
            CommandIO.WriteLine("  zupdate history                - Historia zainstalowanych aktualizacji");
            CommandIO.WriteLine("  zupdate drives                 - Diagnostyka partycji i katalogow aktualizacji");
        }
    }
}
