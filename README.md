# PerformanceOverlay

Kompakte Windows-11-Overlay-Anzeige für FPS, CPU-/GPU-Auslastung und Temperatur, Netzwerk-Durchsatz und Paketverlust. Die Daten werden in einer horizontalen Zeile angezeigt.

## Anti-Cheat-Kompatibilität

Das originale RICOCHET Anti-Cheat wird nicht integriert: Es ist ein proprietäres Activision-System ohne öffentliche Integrationsschnittstelle. Die beigefügte, unsignierte `Guardian TrustGuard.exe` ist kein offizielles RICOCHET-Modul und wird nicht ausgeführt oder als Abhängigkeit verwendet.

Der standardmäßig aktive `AntiCheatSafeMode` ist eine defensive Kompatibilitätsfunktion. Das Overlay:

- injiziert keine DLLs und installiert keine Treiber;
- liest oder schreibt keinen Spielspeicher und setzt keine Windows-/Input-Hooks;
- verwendet für FPS ausschließlich das externe PresentMon-ETW-Verfahren;
- pausiert FPS-Telemetrie und blendet sich für konfigurierte geschützte Fenstertitel aus. `Call of Duty` ist im Beispielprofil voreingetragen.

Das ist keine Garantie für eine Freigabe durch ein Anti-Cheat-System. Geschützte Spiele können jedes externe Overlay blockieren oder als unerwünscht einstufen. Bei einem geschützten Titel Safe-Modus aktiviert lassen und die offizielle Spiel-/Anti-Cheat-Dokumentation beachten.

## Start

```powershell
.\tools\Download-PresentMon.ps1
dotnet run -c Release
```

Für einen fertigen x64-Build genügt `PerformanceOverlay.exe`. Der FPS-Wert wird nur aus PresentMon-Frame-Presents berechnet. GPU-Werte verwenden `nvidia-smi.exe`; CPU-Temperatur kann je nach ACPI-Sensor nicht verfügbar sein und wird dann als `--` dargestellt.

Die Konfiguration liegt unter `%APPDATA%\PerformanceOverlay\settings.json` und kann über das Tray-Menü geöffnet werden. Unterstützt werden Schriftart, Größe, Textfarbe, Hintergrundfarbe, Transparenz, Eckenradius, Monitor, Position, Klick-Durchlässigkeit, Messintervall, Ping-Ziel und die Safe-Modus-Ausschlussliste.

Borderless/Windowed ist zuverlässiger als exklusives Fullscreen; Anti-Cheat kann externe Overlays ausblenden.
