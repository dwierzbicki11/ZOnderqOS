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


class AlphaEnglishUITextContract(unittest.TestCase):
    def test_removed_polish_messages_do_not_reappear(self):
        for relative, fragments in OBSOLETE_UI_TEXT.items():
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
