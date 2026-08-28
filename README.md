# PerformanceOverlay

Kompakte Windows-11-Overlay-Anzeige für FPS, CPU-/GPU-Auslastung und Temperatur, Netzwerk-Durchsatz und Paketverlust. Die Daten werden in einer horizontalen Zeile angezeigt.

## Start

```powershell
.\tools\Download-PresentMon.ps1
dotnet run -c Release
```

Für einen fertigen x64-Build genügt `PerformanceOverlay.exe`. Der FPS-Wert wird nur aus PresentMon-Frame-Presents berechnet. GPU-Werte verwenden `nvidia-smi.exe`; CPU-Temperatur kann je nach ACPI-Sensor nicht verfügbar sein und wird dann als `--` dargestellt.

Die Konfiguration liegt unter `%APPDATA%\PerformanceOverlay\settings.json` und kann über das Tray-Menü geöffnet werden. Unterstützt werden Schriftart, Größe, Textfarbe, Hintergrundfarbe, Transparenz, Eckenradius, Monitor, Position, Klick-Durchlässigkeit, Messintervall und Ping-Ziel.

Das Overlay injiziert keinen Code in Spiele. Borderless/Windowed ist zuverlässiger als exklusives Fullscreen; Anti-Cheat kann externe Overlays ausblenden.
