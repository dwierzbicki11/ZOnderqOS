"""Regression guard for Polish user-facing text already removed from Alpha 1.

The pl-PL translation catalog is deliberately retained for Beta 1.
This contract checks high-visibility strings that previously slipped through
diacritic-only audits; it is not a language classifier.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

# Do not modify CLI command names, filesystem keys, or the retained pl-PL dictionary.
OBSOLETE_UI_TEXT = {
    "source/GUI/GuiManager.cs": (
        "Przytrzymaj przycisk ZASILANIA", "Witaj, ", 
        "SYSTEM ZONDERQOS ZOSTAL WYLACZONY",
        "Twoj komputer moze byc teraz bezpiecznie wylaczony",
        "Nacisnij klawisz R, aby zrestartowac",
    ),
    "source/Core/BootSessionHealth.cs": (
        "SYSTEM ZONDERQOS ZOSTAL WYLACZONY",
        "Twoj komputer moze byc teraz bezpiecznie wylaczony",
        "Nacisnij klawisz [ R ]",
    ),
    "source/GUI/Application/DeviceManagerApp.cs": (
        "WYBRANO URZADZENIE", "ZMIENIONO KATEGORIE URZADZEN",
    ),
    "source/GUI/Application/AppCenterApp.cs": (
        "ZACZNIJ PISAC, ABY FILTROWAC", "WYBRANO APLIKACJE",
    ),
    "source/GUI/Application/SecurityCenterApp.cs": (
        "ZAPORA SIECIOWA JEST WYLACZONA", "Zalecana natychmiastowa inspekcja",
    ),
    "source/GUI/Application/SystemUpdaterApp.cs": (
        "Trwa operacja aktualizacji - poczekaj",
        "ZAINSTALUJ TERAZ", "Trwa przywracanie migawki",
    ),
    "source/GUI/Application/NotepadApp.cs": ("System plikow tylko do odczytu", "Bez nazwy"),
    "source/GUI/Application/PassVaultApp.cs": (
        "Router Domowy Wi-Fi", "Serwer SSH Backup", "Portfel Crypto / Baza",
    ),
    "source/GUI/Application/BasicIdeApp.cs": (
        "FANFARA KONCOWA", "ZGADNIJ LICZBE", "ZA MALO!",
    ),
    "source/SystemCore/Diagnostics/SystemDiagnosticRunner.cs": (
        "Nie wykonano pomiaru 3600s", "Nie wykonano testu ponownego rozruchu",
    ),
    "source/Core/Updates/UpdateManager.cs": (
        "Sprawdzanie obecnosci plikow pakietu",
        "Brak dostepnej migawki do przywrocenia",
        "Przywracanie zakonczone pomyslnie",
    ),
    "source/Core/SystemIdentity.cs": ("Witaj w ZOnderqOS 1.0 Alpha!",),
    "source/Shell/Commands/CmdSysmond.cs": ("Demon sysmond",),
}


# Previously missed messages outside the main localized controls.
# Only actual release strings are guarded: the pl-PL catalog and stable
# identifiers (paths, command names, configuration keys) are intentionally kept.
ADDITIONAL_LEGACY_UI_TEXT = {
    "Kernel.cs": ("SPRAWDZANIE PAMIECI I DYSKOW", "WCZYTYWANIE USTAWIEN", "URUCHAMIANIE POWLOKI"),
    "source/GUI/BootSplash.cs": ("URUCHAMIANIE SYSTEMU",),
    "source/GUI/LockScreenManager.cs": ("SESJA ZABLOKOWANA", "Sesja zablokowana", "ZA DUZO PROB"),
    "source/GUI/LoginScreenManager.cs": ("WPISZ NAZWE UZYTKOWNIKA", "ZALOGOWANO", "AUTORYZACJA WYMAGANA"),
    "source/GUI/NotificationCenter.cs": ("System nie ma nic nowego do pokazania",),
    "source/GUI/Taskbar.cs": ("System wykryl zmiane listy partycji",),
    "source/GUI/StartMenu.cs": ('IndexOf("Aktualizac"',),
    "source/GUI/Application/PassVaultApp.cs": ("Wpisz PIN: 1234 aby odblokowac sejf", "Zmieniono PIN glowny", "KATEGORIA:", '"KOPIUJ"'),
    "source/GUI/Application/SecurityCenterApp.cs": ("UWAGA: Zapora zostala wylaczona", "ZAPORA SIECIOWA", "ZGODNY", "zmiana trybu zapory"),
    "source/GUI/Application/SystemUpdaterApp.cs": ("Wymagany restart komputera", "Skanowanie nosnika", "ZRESTARTUJ TERAZ", "ZLY PODPIS"),
    "source/Core/Updates/UpdateManager.cs": ("Wykryto nowa wersje", "Weryfikacja sumy", "NIEPODPISANY", "ZAINSTALOWANO"),
    "source/Core/Updates/UpdateManifest.cs": ("Niepoprawny wpis manifestu", "Niezgodna architektura pakietu"),
    "source/Core/Updates/BootSlotManager.cs": ("Niepoprawny format SHA-256 kernela", "Nieprawidlowy slot docelowy"),
    "source/Core/KernelPanic.cs": ("Wykryto awarie rozruchu",),
    "source/GUI/Application/NotepadApp.cs": ("Niezapisane zmiany", "Open plik"),
    "source/GUI/Application/HexViewerApp.cs": ("Nieprawidlowa sciezka", "INNY PLIK"),
    "source/GUI/Application/SpriteStudioApp.cs": ("Wyczyszczono siatke", "Wczytano pomyslnie"),
    "source/GUI/Application/AppCenterApp.cs": ('"GOTOWE"', '"KATEGORIA"'),
    "source/GUI/Application/BasicIdeApp.cs": ("Nierozpoznana instrukcja", "KONSOLA WYJSCIOWA"),
    "source/GUI/Application/UserAccountsApp.cs": ("WPISZ NAZWE NOWEGO UZYTKOWNIKA",),
    "source/GUI/Application/KernelAdvancedApp.cs": ("RECZNE COLLECT ZABLOKOWANE",),
    "source/GUI/Application/TetrisApp.cs": ("KONIEC GRY!", "Pauza"),
    "source/GUI/Application/SpaceInvadersApp.cs": ("Przygotuj sie na nastepna fale",),
    "source/GUI/Application/CalendarApp.cs": ("STREFA CZASOWA",),
    "source/Shell/Commands/CmdSysmond.cs": ("Inicjalizacja sysmond w tle",),
    "source/Shell/Commands/CmdNetwork.cs": ("Konfiguracja DHCP dla aktywnej karty",),
    "source/Security/PasswordPolicy.cs": ("HASLO MUSI MIEC MINIMUM", "HASLO JEST ZA DLUGIE"),
    "source/SystemCore/Diagnostics/SystemDiagnosticRunner.cs": ("Nie wykonywano operacji GUI",),
    "zonderq-wgraj-iso.sh": ("Brakuje programu:", "Nie mozna odmontowac", "Wgrano istniejacy obraz ISO"),
    "tools/prepare-zonderq-usb.sh": ("PRZYGOTOWANIE PENDRIVE", "Inicjalizacja struktury katalogow"),
    "tools/deploy-to-usb.sh": ("Kopiowanie nowego kernela", "Pomyslnie zaktualizowano"),
    "tools/flash-clean-to-usb.sh": ("Wdrozenie zakonczone", "Partycja ZONDERQDATA"),
    "tools/zupdate-cloud-sync.sh": ("Sprawdzanie polaczenia internetowego", "Aktualizacja z Internetu zostala"),
    "tools/create-update-package.sh": ("Pomyslnie wygenerowano pakiet",),
    "tools/test-qemu-boot.sh": ("Weryfikacja kluczowych kryteriow",),
}


class AlphaEnglishUITextContract(unittest.TestCase):
    def test_removed_polish_messages_do_not_reappear(self):
        for relative, fragments in OBSOLETE_UI_TEXT.items():
            text = (ROOT / relative).read_text(encoding="utf-8")
            for fragment in fragments:
                with self.subTest(file=relative, text=fragment):
                    self.assertNotIn(fragment, text)

    def test_additional_polish_messages_do_not_reappear(self):
        for relative, fragments in ADDITIONAL_LEGACY_UI_TEXT.items():
            text = (ROOT / relative).read_text(encoding="utf-8")
            for fragment in fragments:
                with self.subTest(file=relative, text=fragment):
                    self.assertNotIn(fragment, text)

    def test_english_only_alpha_setting_is_retained(self):
        localization = (ROOT / "source/Core/Localization/LocalizationManager.cs").read_text(encoding="utf-8")
        settings = (ROOT / "source/Core/SystemSettings.cs").read_text(encoding="utf-8")
        self.assertIn("public const bool EnglishOnlyAlpha = true;", localization)
        self.assertIn('CurrentLanguage = Language.English;', localization)
        self.assertIn('LanguageCode { get; private set; } = "en-US";', settings)
        self.assertIn("RegisterPolishStrings();", localization)


if __name__ == "__main__":
    unittest.main()
