# Changelog

All notable changes to **Phone Audio Receiver** are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-11-19

A complete visual redesign for Windows 11.

### Added
- New app icon: violet-to-cyan gradient tile with a white sound wave, in every size from 16 to 256 px.
- Light and dark themes with a toggle button in the title bar. The choice is saved; until it is
  used, the app follows the Windows theme.
- Coloured status dot on the tray icon: grey idle, amber connecting, blue connected,
  green receiving audio, red error.
- Themed tray menu with rounded corners.
- Exe metadata: product name, author and copyright.

### Changed
- UI rebuilt in WPF with the Windows 11 acrylic backdrop (semi-transparent, blurred),
  Segoe UI Variable text, Segoe Fluent Icons, rounded cards and toggle switches.
- Larger status area with an icon, a status line and a detail line (phone name, errors).
- The window renders in software mode, so the app never uses the GPU while you play games.

### Performance
- Hidden in the tray: 0 ms CPU over 30 s, about 56 MB private memory.
- Window open with the level meter running: about 0.3 % of one CPU core.

## [0.1.0] - 2026-11-19

First working version.

### Added
- Connects to a paired iPhone through Windows' built-in Bluetooth A2DP sink
  (`AudioPlaybackConnection`). Windows decodes and plays the audio, so the app adds no latency.
- Automatic connection at launch and reconnection after drops (2, 5, 10, 20, 30, then every
  60 seconds, up to 15 tries), when the phone reappears, and after the PC wakes from sleep.
- Watches paired devices only; no Bluetooth scanning.
- Phone-only level meter read from the audio session Windows creates for the phone, so game
  audio is never counted.
- Shows the current output device.
- Tray icon with Open, Connect/Disconnect and Exit. Closing the window keeps the app running.
- Optional start with Windows (minimized to the tray).
- Single instance: launching it again shows the existing window.

### Performance
- Hidden in the tray: 0 ms CPU over 30 s, about 22 MB private memory.

[0.2.0]: https://github.com/mayyasbanat3-sudo/PhoneAudioReceiver/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/mayyasbanat3-sudo/PhoneAudioReceiver/releases/tag/v0.1.0
