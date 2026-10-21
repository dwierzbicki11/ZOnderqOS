# Klawiaturowe zarządzanie oknami — ZonderqOS Gen 3 (Cosmos 3.0.94)

| Skrót | Działanie |
| --- | --- |
| Alt+Tab | Następne działające okno (w kolejności przełączania); przy wyborze przywraca okno zminimalizowane |
| Alt+Shift+Tab | Przełączanie w przeciwnym kierunku |
| Alt+F4 | Zamyka aktywne okno lub otwarty panel Start/powiadomień/palety/kontekstowy |
| Alt+Spacja | Istniejąca paleta poleceń |
| Ctrl+Alt+L | Istniejąca blokada sesji |
| Ctrl+Alt+Delete | Istniejący ekran bezpieczeństwa |

## Zachowanie

- Skróty działają tylko po uwierzytelnieniu. Klawiatura jest obsługiwana przez `GuiManager` przed przekazaniem zdarzenia do aplikacji.
- Podczas Alt+Tab transient overlays są zamykane, aby fokus wrócił do pulpitu.
- Alt+F4 na otwartym panelu najpierw zamyka panel, **nie** aplikację pod spodem.
- Alt+F4 wywołuje `Window.CloseAction`, tak jak kliknięcie krzyżyka. Notatnik zachowuje dialog niezapisanych zmian.
- Do zmiany okien służy istniejący `ApplicationManager`. Nie powstaje osobna lista okien na każdy klawisz, nie wprowadzono usług/wątków ani operacji dyskowych.
- Użytkownik z minimalizowanymi wszystkimi oknami może wznowić ostatnie za pomocą Alt+Tab.
- Przy braku aplikacji lub przy tylko jednym widocznym oknie operacja pozostaje bezpieczna.

## Weryfikacja

```bash
python3 tests/test_3094_window_shortcuts.py
```

Testy obejmują kontrakty kodu oraz model kolejności trzech okien, w tym okna zminimalizowane. Pełne CI 3.0.94 ma skompilować ISO i uruchomić testy bootowania QEMU.

**Do ręcznego QA:** QEMU z przechwyceniem klawiatury oraz bare metal z klawiaturą USB: trzy okna, 4 × Alt+Tab, 4 × Alt+Shift+Tab, zminimalizowane okno, niezapisany Notatnik + Alt+F4, widoczna paleta + Alt+F4. Host maszyny wirtualnej może przechwytywać Alt+Tab — należy wtedy użyć prawidłowego keyboard grab. Brak testu sprzętowego oznacza, że skróty nie są jeszcze potwierdzone na fizycznym komputerze.

Bez zmian w wersji SDK, sterownikach USB, filesystemie i polityce zapisu na pendrive.
