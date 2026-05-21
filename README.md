# Lowgi

Lowgi is a small Windows tray app for monitoring Logitech mouse battery level.

It runs quietly in the system tray, shows the current battery percentage as a numeric tray icon, and can warn when the battery reaches a selected low-battery threshold.

## Features

- Numeric tray icon for battery percentage.
- Orange numeric icon when battery is at or below the selected warning threshold.
- Low-battery Windows notification with selectable thresholds: 2%, 5%, 10%, 15%, or 20%.
- Automatic single-device selection when only one Logitech device is detected.
- Dark tray menu.
- Optional autostart with Windows.
- Native HID++ battery polling.
- Logitech G Hub websocket battery source when G Hub is available.
- No local webserver or API.

## Notes

Lowgi currently targets Windows x64 and is published as a self-contained .NET 10 app.

Native polling is set to 10 seconds in this test build. For normal daily use, a longer polling interval is recommended to avoid waking the mouse more often than needed.
