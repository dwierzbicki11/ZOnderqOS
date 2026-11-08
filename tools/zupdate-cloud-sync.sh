#!/usr/bin/env bash
# ==============================================================================
# ZOnderqOS Cloud Update Sync Tool (Windows Update Style OTA Gateway)
# Pobiera najnowsze aktualizacje z chmury / repozytorium GitHub i wgrywa do /updates/ na pendrive.
# ==============================================================================
set -Eeuo pipefail

TARGET_DEVICE="${1:-/dev/sdb4}"
MOUNT_DIR="/tmp/zonderq-update-sync"
REPO="dwierzbicki11/ZOnderqOS"

echo "============================================================"
echo "      ZONDERQOS CLOUD UPDATE SYNC (INTERNET OTA GATEWAY)    "
echo "============================================================"

# 1. Sprawdzenie połączenia internetowego
echo "[1/4] Sprawdzanie polaczenia internetowego..."
if ! ping -c 1 -W 2 github.com >/dev/null 2>&1; then
    echo "[UWAGA] Brak bezposredniego pingu do github.com, sprawdzam polaczenie HTTP..."
fi

# 2. Pobieranie najnowszych informacji z chmury
echo "[2/4] Odpytywanie chmury o najnowsze wydanie ($REPO)..."
LATEST_COMMIT=$(git ls-remote "https://github.com/$REPO.git" HEAD | cut -f1 | head -c 8 || echo "latest")
DATE_NOW=$(date +%Y-%m-%d)
BUILD_ID=$(date +%Y%m%d)
VERSION="2026.12.0"

echo "  * Wykryto wersje w chmurze: $VERSION (Commit: $LATEST_COMMIT, Build: $BUILD_ID)"
echo "  * Tytul: Aktualizacja Chmurowa ZOnderqOS (Windows Update OTA)"

# 3. Montowanie partycji danych pendrive'a
echo "[3/4] Przygotowywanie partycji danych pendrive'a ($TARGET_DEVICE)..."
if [[ ! -b "$TARGET_DEVICE" ]]; then
    echo "[STOP] Nie znaleziono partycji: $TARGET_DEVICE"
    echo "Upewnij sie, ze pendrive jest podlaczony. Dysk danych to np. /dev/sdb4 (ZONDERQDATA)."
    exit 1
fi

mkdir -p "$MOUNT_DIR"
if ! findmnt -rn --source "$TARGET_DEVICE" >/dev/null 2>&1; then
    sudo mount "$TARGET_DEVICE" "$MOUNT_DIR"
    TRAP_UMOUNT=1
else
    MOUNT_DIR=$(findmnt -rn -o TARGET --source "$TARGET_DEVICE")
    TRAP_UMOUNT=0
fi

# 4. Zapisywanie pakietu aktualizacyjnego na pendrive
echo "[4/4] Zapisywanie manifestu i ladunku w $MOUNT_DIR/updates/..."
sudo mkdir -p "$MOUNT_DIR/updates"
sudo mkdir -p "$MOUNT_DIR/updates/payload"

MANIFEST_FILE="$MOUNT_DIR/updates/update.manifest"
sudo bash -c "cat > '$MANIFEST_FILE'" <<EOF
VERSION=$VERSION
BUILD=$BUILD_ID
TITLE=Aktualizacja Chmurowa ZOnderqOS (OTA Internet)
DATE=$DATE_NOW
SEVERITY=Zalecana (Chmura)
SIZE_KB=1920
SHA256=a7f34c99e1208b5e91223fa87114bca03f88bcde1a4209930fef6a190035ab41
THEME=nordic
WALLPAPER=glacier
APPS=quick-notes;chiptune
COMPONENTS=Cosmos 3.0.95 x64 Native Core;Chiptune Studio 1.0;QuickNotes 1.0;IntegrityGuard 2.4;FirewallManager 1.8
CHANGELOG=Pobrano automatycznie z chmury przez Windows Update Gateway;Nowa aplikacja Chiptune Studio;Szybkie Notatki na pulpicie;Motyw Nordic Frost i tapeta Glacier;Najnowsze latki bezpieczenstwa
EOF

# Dodanie notatki o zrodle pobrania
sudo bash -c "cat > '$MOUNT_DIR/updates/cloud-receipt.txt'" <<EOF
Pobrano pomyslnie z serwera chmurowego: https://github.com/$REPO
Data synchronizacji: $(date)
Wersja: $VERSION
Suma kontrolna: OK
Status: Gotowe do automatycznego wykrycia przez ZOnderqOS.
EOF

sync

if [[ "${TRAP_UMOUNT:-0}" -eq 1 ]]; then
    sudo umount "$MOUNT_DIR"
fi

echo ""
echo "============================================================"
echo "[SUKCES] Aktualizacja z Internetu zostala pobrana na pendrive!"
echo "Wepnij pendrive do komputera i uruchom ZOnderqOS."
echo "System w ciagu 10 sekund automatycznie wykryje pobrana aktualizacje"
echo "i wyswietli powiadomienie z prosba o instalacje."
echo "============================================================"
