#!/usr/bin/env python3
"""Audit all tracked ZOnderqOS files for Polish user-visible runtime messages.

Static source audit: reports candidate strings, not proof of dynamic UI coverage.
Retains the pl-PL dictionary and Polish documentation/tests intentionally.
Requires only Python 3 and git; no Cosmos SDK or extra dependencies.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SELF = "tools/audit-runtime-english.py"
BINARY_EXTENSIONS = {
    ".iso", ".elf", ".efi", ".png", ".jpg", ".jpeg", ".gif", ".ico",
    ".pyc", ".so", ".o", ".zip", ".gz", ".pdf", ".woff", ".ttf",
}
# Search accent-free transliterations as well as actual Polish diacritics.
# A word must be bounded; substrings of English identifiers are not findings.
POLISH_STEMS = (
    "aktualizacj", "anulowan", "autoryzacj", "bezpieczn", "bled", "bl[aą]d",
    "blokad", "brakuje", "brak", "calym", "chwil", "czekaj", "czyszczen",
    "dane", "dostepn", "dostepu", "dyskow", "dzialan", "dziennik",
    "etykiet", "gotow", "hasel", "haslo", "informacj", "inicjalizacj",
    "instalacj", "istniejac", "katalog", "kategor", "klawisz", "kolejn",
    "komunikat", "konfiguracj", "koniec", "kontroln", "kopia", "kopiowan",
    "kryptograficzn", "ladunk", "liczb", "ladowan", "magazyn", "menedzer",
    "migawk", "mozna", "mozliwe", "musi", "nacisnij", "naglow", "nastepn",
    "niedozwolon", "niekompletn", "nieobslug", "nieoczekiwan",
    "nieodnalezion", "niepoprawn", "nieprawidlow", "nieudan",
    "niebezpieczn", "nieznan", "niezapis", "nowa", "nowy", "obliczan",
    "obraz", "odczyt", "odczekaj", "odblokowan", "odmow", "odmontowywan",
    "odswiez", "odtwor", "operacj", "ostatni", "ostrzezen", "otworz",
    "pamiec", "partycj", "plikow", "plik", "poczatek", "podaj",
    "podlacz", "podpis", "pominiet", "pomysln", "ponownie", "potwierdz",
    "powiadom", "powtorzon", "poziom", "przerwan", "przygotow",
    "przywro", "prob", "przelacz", "przeskan", "przypis",
    "rezerw", "rozmiar", "rozpoczn", "rozwiaz", "ruchy", "sciezk",
    "sesja", "sieciow", "skanowan", "skladnik", "slocie", "sprawdz",
    "strefa", "suma", "sumy", "szybk", "szyfrowan", "tablic", "trwa",
    "udalo", "udana", "uruchamian", "uruchom", "urzadzen", "usuniet",
    "ustawien", "ustawiono", "uzytkown", "uzyj", "uzywan", "uwaga",
    "weryfikacj", "wgrano", "wgrywan", "witaj", "witamy", "wlasciw",
    "wpisz", "wybrano", "wybierz", "wycofywan", "wyczys",
    "wykryto", "wylacz", "wymagan", "wynik", "wyswiet", "wyszukiwan",
    "zabezpiecz", "zablokow", "zakoncz", "zaladow", "zalogow",
    "zamkn", "zapis", "zaszyfr", "zastosowan", "zatrzym", "zaufan",
    "zawier", "zaznacz", "zidentyfik", "zmienion", "zostala",
    "zostalo", "zostal", "zweryfikowan", "zwyciestw",
)
ACCENTS = re.compile(r"[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]")
POLISH_WORD = re.compile(r"(?<![a-zA-Z])(?:" + "|".join(POLISH_STEMS) + r")[a-zA-Z]*\b", re.I)
# Single and double quotes on one line (includes shell and Python messages).
STRING = re.compile(r'(?<![\\w])(?:@|\\$@|@\\$|\\$)?(?:"(?:\\\\.|[^"\\\\\\n])*"|\\x27(?:\\\\.|[^\\x27\\\\\\n])*\\x27)')
TRANSLATION_BEGIN = "private static void RegisterPolishStrings()"
TRANSLATION_END = "private static void RegisterEnglishStrings()"


def inventory() -> list[str]:
    p = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, check=True,
                       stdout=subprocess.PIPE)
    return [x.decode("utf-8") for x in p.stdout.split(b"\\0") if x]


def file_group(path: str) -> str:
    if path == SELF:
        return "auditor"
    if path.startswith("tests/"):
        return "test-fixture"
    if path.startswith(("docs/", ".github/")) or path.endswith((".md", ".yml")):
        return "documentation-ci"
    if path.startswith(("output-", "output/")):
        return "shipped-output"
    if path.startswith("source/") or path == "Kernel.cs":
        return "runtime"
    if path.startswith("tools/") or path.endswith((".sh", ".py", ".c", ".h")):
        return "host-tool"
    return "resource-config"


def mask_polish_dictionary(content: str, path: str) -> str:
    if path != "source/Core/Localization/LocalizationManager.cs":
        return content
    start = content.find(TRANSLATION_BEGIN)
    end = content.find(TRANSLATION_END, start)
    if start < 0 or end < 0:
        raise RuntimeError("Localization dictionary boundaries not found")
    # Preserve line numbers. Never discard the English catalog.
    return content[:start] + re.sub(r"[^\\n]", " ", content[start:end]) + content[end:]


def extract_candidates(text: str, path: str):
    for line_no, line in enumerate(text.splitlines(), 1):
        trimmed = line.lstrip()
        if trimmed.startswith(("//", "*", "/*", "# ", "#\\t")):
            continue
        for m in STRING.finditer(line):
            literal = m.group()
            # Ignore shell comments after # and C# comments after // where
            # feasible, but preserve URLs in string values.
            before = line[:m.start()].strip()
            if before.startswith(("//", "#")):
                continue
            if "//" in before and not ('"' in before or "'" in before):
                continue
            if not ACCENTS.search(literal) and not POLISH_WORD.search(literal):
                continue
            kind = "diacritic" if ACCENTS.search(literal) else "polish-token"
            yield line_no, kind, literal[:170]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ci", action="store_true", help="Fail on suspected runtime text")
    ap.add_argument("--json", action="store_true", help="Print JSON inventory")
    args = ap.parse_args()
    groups = Counter()
    extensions = Counter()
    checked = Counter()
    findings = []
    skipped_binary = 0
    for name in inventory():
        p = ROOT / name
        group = file_group(name)
        groups[group] += 1
        extensions[p.suffix.lower() or "(none)"] += 1
        if p.suffix.lower() in BINARY_EXTENSIONS or not p.is_file():
            skipped_binary += 1
            continue
        raw = p.read_bytes()
        if b"\\0" in raw[:4096]:
            skipped_binary += 1
            continue
        try:
            data = raw.decode("utf-8")
        except UnicodeDecodeError:
            skipped_binary += 1
            continue
        checked[group] += 1
        if group in ("auditor", "test-fixture", "documentation-ci"):
            continue
        data = mask_polish_dictionary(data, name)
        for line, kind, snippet in extract_candidates(data, name):
            findings.append({
                "file": name, "line": line, "kind": kind,
                "group": group, "text": snippet,
            })
    summary = {
        "tracked_files": sum(groups.values()), "groups": dict(groups),
        "extensions": dict(extensions), "files_read": sum(checked.values()),
        "files_read_by_group": dict(checked),
        "binary_or_unreadable": skipped_binary,
        "candidate_count": len(findings),
        "candidates_by_group": dict(Counter(x["group"] for x in findings)),
    }
    if args.json:
        print(json.dumps({"summary": summary, "findings": findings}, indent=2,
                         ensure_ascii=False))
    else:
        print("[I18N-INVENTORY] " + json.dumps(summary, ensure_ascii=False))
        for f in findings:
            print(f"[I18N-CANDIDATE] {f['group']} {f['file']}:{f['line']} "
                  f"{f['kind']}: {f['text']}")
    # Candidate reporting is deliberately conservative: manual review
    # distinguishes genuine messages from metadata and intentional Polish.
    return 1 if args.ci and any(x["group"] == "runtime" for x in findings) else 0


if __name__ == "__main__":
    sys.exit(main())
