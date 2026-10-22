#!/usr/bin/env python3
"""Fetch the newest successful main Cosmos 3.0.94 full-GUI ISO and update
one previously provisioned ZOnderqOS SanDisk USB without overwriting FAT32.

Run as the normal login user, not root. This script downloads/verifies only;
actual device writing is delegated to the existing fail-closed, interactive
update-3094-one-usb.py script with sudo. No dd, shell redirection to a disk,
repartitioning or formatting is ever used.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

REPO = "dwierzbicki11/ZOnderqOS"
WORKFLOW = "experimental-cosmos-3094-usb.yml"
ARTIFACT = "zonderq-3094-NORMAL-GUI-REQUIRES-ZONDERQDATA"
API = "https://api.github.com/repos/" + REPO
EXPECTED_DEVICE = "/dev/sdb"
EXPECTED_BYTES = 123048296448
DATA_START = "65536"
MAX_ZIP_SIZE = 64 * 1024 * 1024
MAX_ISO_SIZE = 65536 * 512


class Refused(RuntimeError):
    """Safety or provenance check failed; no USB write is permitted."""


class BuildPending(Refused):
    """The matching main build is not complete; no device operation allowed."""


def fail(message: str) -> None:
    raise Refused(message)


def cmd(*args: str) -> str:
    try:
        return subprocess.check_output(args, text=True, stderr=subprocess.PIPE).strip()
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        fail(f"Nie można uruchomić {' '.join(args)}: {exc}")


def check_usb(device: str = EXPECTED_DEVICE) -> list[str]:
    """Unprivileged checks. The privileged writer always rechecks all MBR details."""
    if os.path.realpath(device) != EXPECTED_DEVICE:
        fail("Dozwolony tylko poprzednio zweryfikowany pendrive /dev/sdb.")
    if not Path(device).exists():
        fail("Brak /dev/sdb; sprawdź lsblk. Nie wybieram automatycznie innego dysku.")
    if cmd("lsblk", "-dn", "-o", "TYPE", device) != "disk":
        fail("/dev/sdb nie jest całym dyskiem.")
    if cmd("lsblk", "-dn", "-o", "TRAN", device) != "usb":
        fail("/dev/sdb nie jest urządzeniem USB.")
    if "SanDisk" not in cmd("lsblk", "-dn", "-o", "MODEL", device):
        fail("/dev/sdb nie jest oczekiwanym SanDiskiem.")
    if cmd("lsblk", "-bdn", "-o", "SIZE", device) != str(EXPECTED_BYTES):
        fail("Inna pojemność pendrive'a niż zweryfikowane 123048296448 B.")
    part = device + "4"
    if not Path(part).exists():
        fail("Brak partycji danych /dev/sdb4. Nic nie zapisuję.")
    if cmd("lsblk", "-dn", "-o", "START", part) != DATA_START:
        fail("Partycja /dev/sdb4 ma nieprawidłowy sektor początkowy.")
    if cmd("lsblk", "-dn", "-o", "FSTYPE", part) != "vfat":
        fail("Partycja danych nie jest rozpoznawana jako FAT.")
    if cmd("lsblk", "-dn", "-o", "LABEL", part) != "ZONDERQDATA":
        fail("Etykieta danych nie jest ZONDERQDATA.")

    # Only this known stick's mounted partitions may be unmounted.
    try:
        # lsblk JSON may flatten partitions into separate root objects when
        # NAME is absent and tree mode is not forced. Both NAME and --tree
        # ensure we get one disk with nested children before unmounting.
        info = json.loads(cmd("lsblk", "--json", "--tree",
                              "-o", "NAME,PATH,TYPE,MOUNTPOINTS", device))
    except ValueError:
        fail("Nieprawidłowy wynik lsblk JSON.")
    nodes = info.get("blockdevices", [])
    if len(nodes) != 1 or nodes[0].get("path") != device:
        fail("Nieoczekiwane drzewo urządzeń lsblk (oczekiwano jednego /dev/sdb z podpartycjami).")
    result: list[str] = []

    def visit(node: dict) -> None:
        path = node.get("path", "")
        mounts = [x for x in (node.get("mountpoints") or []) if x]
        if mounts:
            if node.get("type") != "part" or not path.startswith(device):
                fail("Odmowa odmontowania urządzenia innego niż partycja SanDisk.")
            for mount in mounts:
                if mount in ("/", "/boot", "/boot/efi", "/home", "/usr", "/var") or not str(mount).startswith(("/media/", "/run/media/", "/mnt/")):
                    fail(f"Niebezpieczny/nieznany punkt montowania: {mount}")
            result.append(path)
        for child in node.get("children", []) or []:
            visit(child)

    visit(nodes[0])
    return result


def get_token() -> str | None:
    # Secret values are never logged.
    for env in ("GH_TOKEN", "GITHUB_TOKEN"):
        if os.environ.get(env):
            return os.environ[env]
    if shutil.which("gh"):
        try:
            return subprocess.check_output(
                ["gh", "auth", "token"], text=True, stderr=subprocess.DEVNULL, timeout=10
            ).strip() or None
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
            pass
    return None


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    """Never forward API credentials to a signed artifact CDN URL."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        host = urllib.parse.urlparse(newurl).hostname or ""
        if not (host == "api.github.com" or host == "github.com" or
                host.endswith(".githubusercontent.com") or
                host.endswith(".blob.core.windows.net")):
            fail("GitHub przekierował pobieranie do nieoczekiwanego hosta.")
        outgoing = super().redirect_request(req, fp, code, msg, headers, newurl)
        if outgoing is not None:
            outgoing.headers.pop("Authorization", None)
            outgoing.unredirected_hdrs.pop("Authorization", None)
        return outgoing


class Github:
    def __init__(self, token: str | None):
        self.token = token
        self.opener = urllib.request.build_opener(SafeRedirect())

    def request(self, url: str):
        if not url.startswith(API + "/"):
            fail("Nieoczekiwany adres API GitHub.")
        req = urllib.request.Request(url, headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "ZOnderqOS-3094-USB-Updater/1.0",
            "X-GitHub-Api-Version": "2022-11-28",
        })
        if self.token:
            req.add_unredirected_header("Authorization", "Bearer " + self.token)
        try:
            return self.opener.open(req, timeout=30)
        except urllib.error.HTTPError as exc:
            if exc.code in (401, 403, 404) and not self.token:
                fail("GitHub wymaga autoryzacji do artefaktów. Jednorazowo: sudo apt install gh && gh auth login; potem ponów to samo polecenie.")
            fail(f"Błąd API GitHub HTTP {exc.code}. Sprawdź konto i dostęp do repozytorium.")
        except urllib.error.URLError as exc:
            fail(f"Brak połączenia HTTPS z GitHub: {exc.reason}")

    def json(self, url: str) -> dict:
        with self.request(url) as response:
            body = response.read(5 * 1024 * 1024 + 1)
        if len(body) > 5 * 1024 * 1024:
            fail("Odpowiedź API GitHub jest zbyt duża.")
        try:
            return json.loads(body)
        except (ValueError, UnicodeDecodeError):
            fail("Niepoprawna odpowiedź JSON z GitHub.")


def select_artifact(gh: Github) -> tuple[int, dict, str]:
    """Only the successful workflow for the EXACT current main commit qualifies."""
    branch = gh.json(API + "/branches/main")
    head = branch.get("commit", {}).get("sha", "")
    if not re.fullmatch(r"[a-f0-9]{40}", head):
        fail("Brak wiarygodnego SHA gałęzi main.")
    runs = gh.json(API + "/actions/workflows/" + WORKFLOW +
                   "/runs?branch=main&per_page=50")
    relevant = [run for run in runs.get("workflow_runs", [])
                if run.get("head_branch") == "main" and run.get("head_sha") == head
                and run.get("event") in ("push", "workflow_dispatch")]
    matches = [run for run in relevant
               if run.get("status") == "completed" and run.get("conclusion") == "success"]
    if not matches:
        if not relevant:
            raise BuildPending(f"Workflow 3.0.94 dla main {head[:12]} jeszcze nie pojawil sie w GitHub Actions.")
        latest = max(relevant, key=lambda x: x.get("id", 0))
        run_id = latest.get("id")
        state = latest.get("status", "unknown")
        if state in ("queued", "requested", "waiting", "pending", "in_progress"):
            raise BuildPending(
                f"Build main {head[:12]} (run {run_id}) trwa: {state}. "
                f"https://github.com/{REPO}/actions/runs/{run_id}")
        fail(f"Build main {head[:12]} (run {run_id}) zakonczyl sie wynikiem "
             f"{latest.get('conclusion')}; nie zapisuje starszego ISO. "
             f"https://github.com/{REPO}/actions/runs/{run_id}")
    run = max(matches, key=lambda x: x.get("id", 0))
    rid = run.get("id")
    if not isinstance(rid, int) or rid < 1:
        fail("Błędny identyfikator builda.")
    artifacts = gh.json(API + f"/actions/runs/{rid}/artifacts")
    selected = [a for a in artifacts.get("artifacts", []) if a.get("name") == ARTIFACT
                and not a.get("expired", True)
                and a.get("workflow_run", {}).get("head_sha", head) == head]
    if len(selected) != 1 or not isinstance(selected[0].get("id"), int):
        fail("Brak jednego ważnego artefaktu pełnego GUI (ZONDERQDATA). Nie używam RAM-ONLY.")
    return rid, selected[0], head


def wait_for_artifact(gh: Github, wait_minutes: int = 120,
                      *, pause=time.sleep, clock=time.monotonic) -> tuple[int, dict, str]:
    """Poll safely for current main CI; never choose a build from another SHA.

    At most 40 unauthenticated requests/hour for the two metadata endpoints.
    Pause/clock injection allows fast deterministic tests without networking.
    """
    deadline = clock() + wait_minutes * 60
    last_message = None
    while True:
        try:
            return select_artifact(gh)
        except BuildPending as exc:
            remaining = deadline - clock()
            if remaining <= 0:
                fail(f"{exc} Brak gotowego ISO po okresie sprawdzania; pendrive bez zmian.")
            if str(exc) != last_message:
                print("[AUTO] " + str(exc), flush=True)
                print("[AUTO] Sprawdzam automatycznie kolejny wynik CI; Ctrl+C przerywa bez zapisu.",
                      flush=True)
                last_message = str(exc)
            pause(min(180, remaining))


def assert_current_main(gh: Github, sha: str) -> None:
    """Fail closed if main moved after an ISO was selected."""
    head = gh.json(API + "/branches/main").get("commit", {}).get("sha")
    if head != sha:
        fail("Main zmienil sie podczas pobierania ISO. Nie zapisuje starszej wersji; "
             "uruchom aktualizator jeszcze raz.")


def verify_zip_digest(path: Path, artifact: dict) -> None:
    digest = artifact.get("digest")
    if not digest:
        return  # Older GitHub APIs can omit the digest. CRC and MBR still checked.
    if not isinstance(digest, str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", digest):
        fail("Nieobsługiwany format sumy kontrolnej artefaktu GitHub.")
    sha = hashlib.sha256()
    with path.open("rb") as src:
        for chunk in iter(lambda: src.read(1024 * 1024), b""):
            sha.update(chunk)
    if sha.hexdigest() != digest.split(":", 1)[1]:
        fail("NIEZGODNA suma SHA-256 pobranego artefaktu.")


def verify_iso(iso: Path) -> None:
    size = iso.stat().st_size
    if size < 512 * 64 or size > MAX_ISO_SIZE or size % 512:
        fail("ISO ma niezgodny rozmiar lub wychodzi poza sektor 65536.")
    with iso.open("rb") as fh:
        mbr = fh.read(512)
    if mbr[510:512] != b"\x55\xaa":
        fail("Obraz nie posiada sygnatury bootowalnego MBR.")
    if int.from_bytes(mbr[502:506], "little") or int.from_bytes(mbr[506:510], "little"):
        fail("ISO nie może zawierać czwartej partycji; jest zarezerwowana na ZONDERQDATA.")


def download_and_extract(gh: Github, artifact: dict, head: str, output_dir: Path) -> Path:
    output_dir.mkdir(parents=True, exist_ok=True)
    iso = output_dir / ("ZonderqOS-3094-" + head[:12] + ".iso")
    if iso.exists():
        iso.unlink()  # Never reuse a local file with unknown content.
    artifact_id = artifact["id"]
    print(f"[AUTO] Pobieram {ARTIFACT}, artifact ID {artifact_id}...", flush=True)
    with tempfile.TemporaryDirectory(prefix="zonderq-3094-", dir=output_dir) as temp:
        temp_dir = Path(temp)
        zip_path = temp_dir / "artifact.zip"
        with gh.request(API + f"/actions/artifacts/{artifact_id}/zip") as response, zip_path.open("wb") as target:
            total = 0
            while True:
                block = response.read(1024 * 1024)
                if not block:
                    break
                total += len(block)
                if total > MAX_ZIP_SIZE:
                    fail("Pobrany plik ZIP jest podejrzanie duży.")
                target.write(block)
        verify_zip_digest(zip_path, artifact)
        try:
            with zipfile.ZipFile(zip_path, "r") as archive:
                files = [x for x in archive.infolist() if not x.is_dir()]
                if len(files) != 1 or files[0].filename != "ZonderqOS.iso" or files[0].file_size > MAX_ISO_SIZE:
                    fail("Artefakt nie zawiera dokładnie jednego oczekiwanego ZonderqOS.iso.")
                temporary_iso = temp_dir / "ZonderqOS.iso"
                with archive.open(files[0]) as source, temporary_iso.open("wb") as target:
                    shutil.copyfileobj(source, target, 1024 * 1024)
                verify_iso(temporary_iso)
                os.replace(temporary_iso, iso)
        except (zipfile.BadZipFile, zipfile.LargeZipFile) as exc:
            fail(f"Uszkodzony ZIP artefaktu: {exc}")
    print(f"[AUTO] ISO sprawdzone: {iso} ({iso.stat().st_size} B)")
    return iso


def updater_call(iso: Path, apply: bool) -> None:
    updater = Path(__file__).resolve().with_name("update-3094-one-usb.py")
    if not updater.is_file():
        fail("Brak tools/update-3094-one-usb.py — wymagany bezpieczny updater.")
    args = [sys.executable, str(updater)]
    if apply:
        args.append("--apply")
    args += [EXPECTED_DEVICE, str(iso)]
    if os.geteuid() != 0:
        if not shutil.which("sudo"):
            fail("Brak sudo do bezpiecznej weryfikacji i zapisu pendrive'a.")
        args.insert(0, "sudo")
    print("[AUTO] " + ("Aktualizacja pendrive'a (wymaga potwierdzenia)." if apply else
                        "Weryfikacja pendrive'a bez zapisu."), flush=True)
    if subprocess.call(args) != 0:
        fail("Weryfikacja/aktualizacja USB przerwana. Niczego nie ponawiam automatycznie.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Pobierz z main i zaktualizuj ZOnderqOS USB bez dd.")
    parser.add_argument("--dry-run", action="store_true", help="Pobierz ISO i sprawdź, ale niczego nie zapisuj.")
    parser.add_argument("--wait-minutes", type=int, default=120,
                        help="Maksymalny czas lokalnego sprawdzania CI (0 = bez ponawiania).")
    args = parser.parse_args(argv)
    if args.wait_minutes < 0 or args.wait_minutes > 120:
        fail("Dozwolony zakres --wait-minutes: 0..120.")
    if os.geteuid() == 0:
        fail("Uruchom jako zwykły użytkownik (bez sudo); skrypt sam użyje sudo tylko do USB.")
    if not Path(__file__).resolve().with_name("update-3094-one-usb.py").exists():
        fail("Brak bezpiecznego oryginalnego updatera. Uruchom z katalogu repozytorium.")

    mounts = check_usb()
    print("[AUTO] SanDisk /dev/sdb, FAT32 ZONDERQDATA: wstępna identyfikacja PASS.")
    gh = Github(get_token())
    run_id, artifact, head = wait_for_artifact(gh, args.wait_minutes)
    print(f"[AUTO] Udany build main: {head[:12]}, GitHub Actions run {run_id}.")
    iso = download_and_extract(gh, artifact, head,
                               Path.home() / "Pobrane" / "ZOnderqOS-3094")
    assert_current_main(gh, head)

    if mounts:
        if args.dry_run:
            print("[AUTO] Partycje USB są zamontowane; --dry-run niczego nie odmontowuje.")
            print("[AUTO] Aby zweryfikować w trybie dry-run, najpierw je odmontuj.")
            return 0
        print("[AUTO] Odmontowuję tylko partycje zweryfikowanego SanDiska: " + ", ".join(mounts))
        for mount_device in reversed(mounts):
            if subprocess.call(["sudo", "umount", mount_device]) != 0:
                fail("Odmontowanie nie powiodło się (otwarty plik?). Nie zapisuję pendrive'a.")

    updater_call(iso, apply=False)
    if args.dry_run:
        print("[AUTO] DRY RUN PASS. Nic nie zapisano na pendrivie.")
        return 0
    assert_current_main(gh, head)
    updater_call(iso, apply=True)  # Original updater asks for exact UPDATE /dev/sdb.
    print("[AUTO] SUKCES: aktualny obraz main jest na USB, FAT32 ZONDERQDATA zachowana.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (Refused, OSError, ValueError) as exc:
        print(f"[AUTO][STOP] {exc}", file=sys.stderr)
        raise SystemExit(1)
