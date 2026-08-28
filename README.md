# PerformanceOverlay

Kompakte Windows-11-Overlay-Anzeige für FPS, CPU-/GPU-Auslastung und Temperatur, Netzwerk-Durchsatz und Paketverlust. Die Daten werden in einer horizontalen Zeile angezeigt.

## Anti-Cheat-Kompatibilität

Das originale RICOCHET Anti-Cheat wird nicht integriert: Es ist ein proprietäres Activision-System ohne öffentliche Integrationsschnittstelle. Die beigefügte, unsignierte `Guardian TrustGuard.exe` ist kein offizielles RICOCHET-Modul und wird nicht ausgeführt oder als Abhängigkeit verwendet.

Der optionale `AntiCheatSafeMode` ist eine defensive Kompatibilitätsfunktion und standardmäßig deaktiviert, damit das Overlay beim Einschalten dauerhaft sichtbar bleibt. Das Overlay:

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

Für einen fertigen x64-Build genügt `PerformanceOverlay.exe`. Während ein Spiel oder eine andere Anwendung aktiv ist, wird der FPS-Wert aus den tatsächlich präsentierten Frames über PresentMon berechnet. Auf dem Windows-Desktop bzw. Startbildschirm verwendet das Overlay als Fallback die aktuelle DWM-Kompositions-/Bildschirmrate, damit dort ebenfalls ein Live-FPS-Wert sichtbar bleibt. Wenn ein geschütztes Spiel die externe Frame-Telemetrie blockiert, bleibt der Spielwert korrekt `--` statt eine erfundene Monitorrate anzuzeigen. GPU-Werte verwenden `nvidia-smi.exe`; CPU-Temperatur kann je nach ACPI-Sensor nicht verfügbar sein und wird dann als `--` dargestellt.

Zusätzliche Desktop-Verknüpfungen können `PerformanceOverlay.exe --toggle` zum Ein-/Ausblenden und `PerformanceOverlay.exe --settings` zum Öffnen der Einstellungsseite verwenden. Die Anwendung startet nicht automatisch mit Windows.

Die Konfiguration liegt unter `%APPDATA%\PerformanceOverlay\settings.json` und kann über **Overlay-Einstellungen …** im Tray-Menü bearbeitet werden. Die Oberfläche unterstützt Schriftart, Größe, Textfarbe, Hintergrundfarbe, stufenlose Transparenz, Eckenradius, Monitor, Position, Klick-Durchlässigkeit, Messintervall, Ping-Ziel, die Safe-Modus-Ausschlussliste sowie einen frei wählbaren globalen Ein/Aus-Hotkey (Modifier plus F-Taste, Buchstabe oder Ziffer). Änderungen werden sofort gespeichert und angewendet. Standard: `Ctrl+Shift+F10`.

Der Hotkey wird als Windows-System-Hotkey registriert und nicht über einen globalen Tastatur-Hook erfasst. Falls die Kombination bereits von Windows oder einer anderen Anwendung belegt ist, bleibt das Overlay über Tray, Desktop-Verknüpfung oder `--toggle` steuerbar; in diesem Fall eine andere Kombination auswählen.

Für FPS-Telemetrie benötigt der Windows-Benutzer Zugriff auf ETW-Leistungsprotokolle (lokale Gruppe **Leistungsprotokollbenutzer**). Nach einer neuen Gruppenmitgliedschaft ist eine erneute Windows-Anmeldung erforderlich. Die CPU-Temperatur wird aus einem vorhandenen LibreHardwareMonitor-/OpenHardwareMonitor-Provider oder einem expliziten Windows-ACPI-CPU-Sensor gelesen. Wenn kein solcher Sensor verfügbar ist, bleibt sie korrekt `--`; die Anwendung erfindet keinen Wert. Die GPU-Temperatur der NVIDIA-Karte kommt direkt aus `nvidia-smi.exe`.

Borderless/Windowed ist zuverlässiger als exklusives Fullscreen; Anti-Cheat kann externe Overlays ausblenden.
