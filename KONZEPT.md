# TUNAS CONVERTER – Konzept

Autor: Tunahan A.

## Ziel

Ein Windows-Programm, mit dem man Videos **schnell und einfach** umwandelt, kürzt und zusammenfügt:
Datei rein → optional Stellen herausschneiden → Format anklicken → fertig.
Kein Projekt-Aufwand wie bei einem großen Schnittprogramm. Hauptziel: **Dateien kleiner machen und
unnötige Stellen entfernen**, ohne die Qualität unnötig zu verschlechtern.

Das Programm ist Open Source (GPL-3.0) und wird als installierbare `.exe` verteilt.

## Bedienung

**Hauptfenster**
- Kopfzeile mit Logo, „+ Video“, „+ Audio“, Schalter „Zusammenfügen“
- Dateiliste mit Vorschaubild, Länge, Auflösung, Größe; pro Datei: Reihenfolge, „Schneiden“, Entfernen
- Dateien per Drag & Drop (auch ganze Ordner)
- Unten große Formatknöpfe: MP4, MKV, AVI, MOV, WMV, WebM, Handy, MP3, „Mehr“

**Nach Klick auf ein Format**
- Qualität: **Original / Hoch / Mittel / Klein**
- Qualitäts-Ampel und geschätzte Dateigröße im Vergleich zum Original
- Dateiname (beim Zusammenfügen oder bei einer einzelnen Datei), höchstens 35 Zeichen
- Zielordner, „Umwandeln“, Fortschrittsbalken, „Abbrechen“

**Schneidefenster**
- Player mit Abspielen, Bild für Bild, Zeitleiste
- „[ Anfang“ und „Ende ]“ markieren → „Herausschneiden“: die Stelle verschwindet sofort,
  Zeitleiste und Zeitanzeige zeigen die neue Länge, beim Abspielen wird sie übersprungen
- Rückgängig, „Alle Schnitte löschen“, Tastenkürzel (Leertaste, Pfeile, I, O, Entf, Strg+Z)

## Formate

**Video:** MP4, MKV, AVI, MOV, WMV, WebM, FLV, MPEG, 3GP, Handy (MP4 bis 720p)
**Audio:** MP3, M4A (AAC), WAV, FLAC, OGG, Opus, WMA, AC3, AMR
**Eingabe:** praktisch alles, was FFmpeg lesen kann (u. a. AVI, MP4, MKV, MOV, WMV, FLV, WebM, MPEG, VOB,
TS/MTS/M2TS, 3GP, RM/RMVB sowie gängige Audioformate)

## Qualität und Dateigröße

1. **„Original“ = verlustfrei umpacken**, wann immer möglich (FFmpeg „Stream Copy“): keine Qualitätsverluste,
   dauert Sekunden. Das Programm prüft, ob das Zielformat die vorhandenen Codecs aufnehmen kann
   (z. B. VP9/Opus aus MKV in MP4). Verlustfrei schneiden geht nur an Schlüsselbildern.
2. **Hoch / Mittel / Klein** bedeuten eine feste Bildqualität (CRF), keine feste Datenrate.
3. **Nie größer als das Original:** Beim Neu-Kodieren erhalten Bild und Ton höchstens die Datenrate der Quelle.
4. **Größenschätzung** durch kurze Probe-Umwandlungen, aktualisiert nach jedem Schnitt.
5. **Qualitäts-Ampel:** grün = verlustfrei, gelb = kaum sichtbarer Unterschied, orange = sichtbar schlechter.

## Zusammenfügen ohne Abstürze

- FFmpeg läuft als **eigener Prozess**; ein Fehler betrifft nur diesen Auftrag, nie das Programm.
- 64-Bit; Dateien werden **nacheinander** eingelesen, eine kaputte Datei blockiert die anderen nicht.
- Gleiche Formate → verlustfrei aneinanderhängen. Unterschiedliche Formate → jeder Clip wird einzeln
  in ein gemeinsames Format gebracht (Auflösung des größten Clips, höchste Bildrate bis 60 fps),
  danach werden die Teile verlustfrei verbunden. Fehlender Ton wird durch Stille ersetzt.

## Technik

| Baustein | Wahl |
|---|---|
| Sprache / Oberfläche | C# mit .NET 10 (64-Bit), WPF |
| Video-Player | LibVLCSharp (VLC) |
| Videomotor | FFmpeg (eigener Prozess, mitgeliefert) |
| Installer | Inno Setup |
| Tests | xUnit mit echtem FFmpeg |

## Design

Corporate Design schwarz-weiß wie das TUNAS-Logo (weißer Thunfisch im Ring auf schwarz).
Farbig sind nur die Qualitäts-Ampel und Fehlermeldungen.

## Fahrplan

### Version 0.1 – erledigt
- [x] Hauptfenster mit Dateiliste, Drag & Drop, Reihenfolge
- [x] 19 Zielformate, Qualität Original/Hoch/Mittel/Klein, verlustfreies Umpacken
- [x] Größenschätzung, Qualitäts-Ampel, Obergrenze „nie größer als das Original“
- [x] Schneidefenster mit Player, sofortigem Herausschneiden, Rückgängig
- [x] Zusammenfügen beliebig vieler Clips
- [x] Eigener Dateiname (max. 35 Zeichen), nie überschreiben
- [x] Installer

### Danach
- [ ] Geräte-Voreinstellungen (WhatsApp, Apple, Android …)
- [ ] Drehen/Spiegeln
- [ ] Zielgröße in MB vorgeben
- [ ] Untertitel einbrennen
- [ ] Auftragsliste speichern (nach Neustart weitermachen)
- [ ] Signierter Installer (z. B. über SignPath für Open-Source-Projekte)
