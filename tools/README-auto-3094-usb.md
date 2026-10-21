# ZOnderqOS 3.0.94 — automatyczne ISO -> pendrive

Po jednorazowym git pull origin main uruchom:

    python3 tools/auto-update-3094-usb.py

Skrypt sam sprawdzi znany SanDisk /dev/sdb (123048296448 bajtów),
odczyta SHA aktualnego main i znajdzie udany build GitHub Actions
experimental-cosmos-3094-usb.yml dla tego samego SHA.
Nie pobierze starego ISO, obrazu diagnostycznego ani RAM-ONLY.

Pobierany artefakt:
zonderq-3094-NORMAL-GUI-REQUIRES-ZONDERQDATA

Pobranie i wypakowanie jest automatyczne. Skrypt weryfikuje SHA256 ZIP,
gdy podaje ją API GitHub, dokładną nazwę ISO, MBR i rozmiar. Następnie
odmontuje normalnie zamontowane partycje tego konkretnego SanDiska
(bez opcji force), uruchomi stary updater w dry-run i dopiero po
jego sukcesie wywoła jego --apply.

Stary updater wymaga wpisania UPDATE /dev/sdb, tworzy kopię pierwszych
32 MiB obszaru rozruchowego i zachowuje MBR partycji sdb4 ZONDERQDATA.
Kopia boot-region NIE jest kopią własnych plików na FAT32.

Tryb testowy bez zapisu:

    python3 tools/auto-update-3094-usb.py --dry-run

Jeżeli GitHub wymaga autoryzacji ZIP artefaktu, jednorazowo:

    sudo apt install gh
    gh auth login

Od kolejnego uruchomienia skrypt sam użyje tokenu gh. Jeżeli pobranie
anonimowe działa, instalacja gh nie jest potrzebna.

Bez dd i bez nadpisywania całego urządzenia. Gdy urządzenie nie jest
tym samym /dev/sdb lub tablica partycji jest inna, skrypt przerywa.
Nie zgaduje nowej nazwy dysku. Zabezpiecz ważne dane z ZONDERQDATA
oddzielną kopią przed pierwszym użyciem.
