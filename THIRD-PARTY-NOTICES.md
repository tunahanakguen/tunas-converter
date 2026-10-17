# Fremde Bausteine in TUNAS CONVERTER

TUNAS CONVERTER selbst steht unter der **GNU General Public License v3.0** (siehe [LICENSE](LICENSE)).
Der Installer liefert folgende fremde Programme und Bibliotheken mit. Sie gehören ihren jeweiligen Urhebern
und stehen unter ihren eigenen Lizenzen.

| Baustein | Zweck | Lizenz | Quelle |
|---|---|---|---|
| **FFmpeg** 9.0.2 (Build von gyan.dev, „full_build“) | Umwandeln, Schneiden, Zusammenfügen | GPL-3.0 (gebaut mit `--enable-gpl --enable-version3`) | https://ffmpeg.org – Quellcode: https://ffmpeg.org/releases/ · Build: https://www.gyan.dev/ffmpeg/builds/ |
| Darin u. a. x264, x265, libvpx, LAME, Opus, Vorbis, Xvid, OpenCORE-AMR | Codecs in FFmpeg | GPL / LGPL / BSD bzw. Apache (siehe FFmpeg-Lizenzdatei im Programmordner `ffmpeg`) | jeweils über die FFmpeg-Build-Seite |
| **LibVLC** 3.0 (VideoLAN.LibVLC.Windows) | Video-Vorschau im Schneidefenster | LGPL-2.1 oder neuer | https://www.videolan.org/vlc/libvlc.html |
| **LibVLCSharp** / LibVLCSharp.WPF 3.10 | .NET-Anbindung an LibVLC | LGPL-2.1 oder neuer | https://code.videolan.org/videolan/LibVLCSharp |
| **CommunityToolkit.Mvvm** 8.4 | Programmlogik (MVVM) | MIT | https://github.com/CommunityToolkit/dotnet |
| **.NET 10 Runtime** | Laufzeitumgebung | MIT | https://github.com/dotnet/runtime |

## Quellcode

- Der vollständige Quellcode von TUNAS CONVERTER liegt im GitHub-Repository dieses Projekts.
- Der Quellcode von FFmpeg und LibVLC ist über die oben genannten Seiten erhältlich. Auf Anfrage stellt der
  Autor die exakt verwendeten Quellcode-Versionen für mindestens drei Jahre zur Verfügung.

## Logo

Das TUNAS-Logo (Thunfisch im Ring) ist das Markenzeichen von Tunahan A. Der Programmcode steht unter der GPL;
das Logo darf zur Kennzeichnung dieses Programms verwendet werden, aber nicht für fremde Produkte.
