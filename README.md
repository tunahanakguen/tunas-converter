# TUNAS CONVERTER

Ein schneller, einfacher Video-Converter für Windows – mit aktuellem FFmpeg, ohne Werbung, Open Source.

Datei reinziehen → optional Stellen herausschneiden → Format anklicken → fertig.

## Herunterladen

Den fertigen Installer gibt es unter [Releases](https://github.com/tunahanakguen/tunas-converter/releases): `TUNAS-Converter-Setup-1.0.0.exe` herunterladen und starten.
Er bringt alles mit (.NET, VLC, FFmpeg) und installiert nach `C:\Program Files\TUNAS Converter`.

## Funktionen (Version 1.0)

- Videos und Audiodateien per Drag & Drop oder Knopf hinzufügen, auch ganze Ordner
- Dateien werden nacheinander im Hintergrund gelesen – auch viele Clips auf einmal
- **Schneiden**: Anfang markieren, Ende markieren, „Herausschneiden“ – die Stelle verschwindet sofort (mit Player, Bild-für-Bild, Rückgängig)
- **Zusammenfügen** beliebig vieler Clips, auch mit unterschiedlicher Auflösung, Bildrate oder ohne Ton
  - Reihenfolge per Ziehen ändern, Gesamtlänge wird angezeigt, Name aus den Dateinamen vorgeschlagen
- Lautstärkeregler im Schneidefenster (wird gemerkt)
- Auch .ts-, .m2ts- und .mpg-Aufnahmen lassen sich sauber ansehen und schneiden; beschädigter Ton wird beim Umwandeln repariert
- Fortschritt auch in der Taskleiste
- **19 Zielformate**: MP4, MKV, AVI, MOV, WMV, WebM, Handy, MP3, FLV, MPEG, 3GP, M4A, WAV, FLAC, OGG, Opus, WMA, AC3, AMR
- Qualität **Original / Hoch / Mittel / Klein**
  - „Original“ packt verlustfrei um, wenn es geht (dauert nur Sekunden)
- **Größenschätzung** durch echte Stichproben und eine **Qualitäts-Ampel** im Vergleich zum Original
- FFmpeg läuft als eigener Prozess: Ein Fehler beim Umwandeln bringt das Programm nicht zum Absturz

Mehr zum Hintergrund und zum Fahrplan: [KONZEPT.md](KONZEPT.md)

## Voraussetzungen

- Windows 10/11 (64-Bit)
- [.NET 10 SDK](https://dotnet.microsoft.com/) zum Bauen
- FFmpeg: entweder `ffmpeg.exe` und `ffprobe.exe` in einen Unterordner `ffmpeg` neben der `TuNASConverter.exe` legen
  oder FFmpeg installieren:

  ```bash
  winget install Gyan.FFmpeg
  ```

## Bauen und starten

```bash
dotnet run --project src/TuNAS.Converter
```

## Installer bauen

Erzeugt `publish\TUNAS-Converter-Setup-<Version>.exe` mit .NET, VLC und FFmpeg (benötigt [Inno Setup](https://jrsoftware.org/isinfo.php)):

```bash
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
```

## Tests

Die Tests erzeugen kurze Testvideos mit FFmpeg und prüfen Schneiden, Zusammenfügen, alle Formate und die Größenschätzung.

```bash
dotnet test
```

## Projektaufbau

| Ordner | Inhalt |
|---|---|
| `src/TuNAS.Converter/Models` | Clip, Zeitbereiche, Formatkatalog |
| `src/TuNAS.Converter/Services` | ffprobe/ffmpeg-Aufrufe, Befehle bauen, Größenschätzung, Umwandeln |
| `src/TuNAS.Converter/ViewModels` | Logik des Hauptfensters |
| `src/TuNAS.Converter/*.xaml` | Hauptfenster und Schneidefenster |
| `tests/TuNAS.Converter.Tests` | Automatische Tests |
| `installer` | Inno-Setup-Skript |
| `tools` | Installer bauen, Logo und Programmsymbol erzeugen |
| `branding` | Logo-Vorlage (weiß auf schwarz) |

Einstellungen und Protokoll liegen unter `%AppData%\TuNAS Converter`.

## Lizenz

Copyright © 2026 Tunahan A.

TUNAS CONVERTER ist freie Software unter der **GNU General Public License v3.0** – siehe [LICENSE](LICENSE).
Du darfst das Programm nutzen, verändern und weitergeben, solange es unter derselben Lizenz offen bleibt.
Mitgelieferte Bausteine (FFmpeg, VLC u. a.) und ihre Lizenzen: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
