"""Alpha 1 English-only UI source contracts (no Cosmos toolchain needed).

These checks cover release-critical text and the language policy. They do not
replace a GUI smoke test or an exhaustive human review of every message.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return (ROOT / path).read_text(encoding="utf-8")


def test_alpha_uses_english_without_polish_runtime_fallback():
    loc = source("source/Core/Localization/LocalizationManager.cs")
    settings = source("source/Core/SystemSettings.cs")
    locale = source("source/Shell/Commands/CmdLocale.cs")
    assert "public const bool EnglishOnlyAlpha = true;" in loc
    assert "CurrentLanguage = Language.English;" in loc
    assert "if (EnglishOnlyAlpha)" in loc
    assert "if (!EnglishOnlyAlpha && CurrentLanguage == Language.English" in loc
    assert 'LanguageCode { get; private set; } = "en-US"' in settings
    assert 'LanguageCode = "en-US";' in settings
    assert "Alpha 1 supports en-US only" in locale


def test_release_critical_gui_strings_are_english():
    expected = {
        "source/GUI/Application/WelcomeApp.cs": (
            '"Welcome to ZOnderqOS 1.0 Alpha"',
            '"[Available in Beta 1]"',
            '"1. OVERVIEW"',
        ),
        "source/GUI/Application/SettingsApp.cs": (
            '"SYSTEM LANGUAGE"',
            '"DEFAULT SETTINGS RESTORED"',
            'return value ? "ENABLED" : "DISABLED";',
        ),
        "source/GUI/LoginScreenManager.cs": (
            '"INVALID USERNAME OR PASSWORD"',
            '"PASSWORD IS REQUIRED"',
        ),
        "source/GUI/LockScreenManager.cs": (
            '"ENTER PASSWORD TO UNLOCK"',
        ),
        "source/GUI/StartMenu.cs": ('"LOG OUT"', '"SHUT DOWN"'),
        "source/GUI/Application/UserAccountsApp.cs": (
            '"ADD USER"', '"CHANGE PASSWORD"',
        ),
        "source/GUI/Application/SysBenchmarkApp.cs": (
            '"Start quick test"',
        ),
        "source/GUI/Application/SystemUpdaterApp.cs": (
            '"No update package selected."', '"CHANGELOG"',
        ),
        "source/Shell/Commands/CmdUpdate.cs": (
            '"Verified USB update found!"',
        ),
    }
    for path, markers in expected.items():
        contents = source(path)
        for marker in markers:
            assert marker in contents, f"Missing {marker} in {path}"


def test_known_polish_release_strings_cannot_return():
    blocked = {
        "source/GUI/Application/WelcomeApp.cs": ('"Witamy w ZOnderqOS', '"SZYBKI START - WYBIERZ'),
        "source/GUI/Application/SettingsApp.cs": ('"USTAWIENIA"', '"WLACZONE"', '"WYLACZONE"'),
        "source/GUI/LoginScreenManager.cs": ('"NIEPRAWIDLOWY LOGIN LUB HASLO"',),
        "source/GUI/InitialSetupManager.cs": ('"HASLO JEST WYMAGANE"',),
        "source/GUI/LockScreenManager.cs": ('"WPISZ HASLO, ABY ODBLOKOWAC"',),
        "source/GUI/Application/UserAccountsApp.cs": ('"Konta lokalne"',),
        "source/GUI/Application/SysBenchmarkApp.cs": ('"Rozpocznij szybki test"',),
        "source/GUI/Application/SystemUpdaterApp.cs": ('"Brak wybranego pakietu aktualizacji."',),
        "source/Shell/Commands/CmdLocale.cs": ('"Aktualny jezyk / Current language: ',),
        "source/Shell/Commands/CmdUpdate.cs": ('"Znaleziono zweryfikowana aktualizacje USB!"',),
    }
    for path, phrases in blocked.items():
        contents = source(path)
        for phrase in phrases:
            assert phrase not in contents, f"Polish UI text still present in {path}: {phrase}"
