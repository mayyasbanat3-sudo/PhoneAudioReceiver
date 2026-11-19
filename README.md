# Phone Audio Receiver

**Hear your iPhone through your PC headphones, over Bluetooth.**
A tiny Windows tray app by **Mayyas Banat**.

[![Instagram](https://img.shields.io/badge/Instagram-@mayyas.banat-E4405F?logo=instagram&logoColor=white)](https://instagram.com/mayyas.banat)
[![WhatsApp](https://img.shields.io/badge/WhatsApp-+972%20594%20629%20259-25D366?logo=whatsapp&logoColor=white)](https://wa.me/972594629259)
[![License: MIT](https://img.shields.io/badge/license-MIT-22C55E)](LICENSE)

## How it works

Windows 10 (2004) and later include a Bluetooth A2DP *sink*. The app opens that link through the
`Windows.Media.Audio.AudioPlaybackConnection` API, and Windows itself receives, decodes and plays
the audio on the default output device. The app never touches the samples, so it adds no latency.

```
iPhone ──A2DP──▶ Bluetooth adapter ──▶ Windows A2DP sink ──▶ default output ──▶ headphones
                                            ▲
                          this app: opens / keeps / restores the link
```

## Features

- Auto-connect at launch; auto-reconnect after drops and after sleep
- Phone-only level meter (game audio is never counted)
- Tray icon; closing the window keeps the app running
- Optional start with Windows
- 0 ms CPU while hidden in the tray, about 22 MB memory

## Build

Requires the .NET 8 SDK.

```bash
cd src/PhoneAudioReceiver
dotnet build -c Release
```

## Use

1. Pair the iPhone with the PC once in Windows Bluetooth settings.
2. Run the app. It connects automatically.
3. Close the window to keep it in the tray; right-click the tray icon → Exit to quit.

## License

[MIT](LICENSE) © 2026 Mayyas Banat
