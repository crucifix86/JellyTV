# JellyTV YouTube App

Electron wrapper around `youtube.com/tv` — the smart-TV interface Google ships
to Sony/LG/Samsung TVs and PlayStation. D-pad navigable, lean, and maintained
by Google so it survives YouTube's frequent backend churn.

## Run

```bash
cd apps/youtube
npm install
npm start            # X11 / generic
npm run start:wayland  # native Wayland (recommended on the test box)
```

## Exit to launcher

- `Ctrl+Q` or `F10` — quits the app cleanly. JellyTV (once integrated) will
  also send SIGTERM on its HOME button, same effect.
- `Escape` / `Backspace` are intentionally **not** intercepted — they pass
  through to YouTube's own back-navigation (video → channel → home), matching
  the Android TV experience.

## Sign-in

First launch, YouTube TV shows a pairing code. On your phone, open
`youtube.com/activate` and enter the code. Session persists across restarts.

## Hardware acceleration

Configured for Intel iGPU (UHD Graphics on N95/N100) via VAAPI + ANGLE/EGL.
Verify with `chrome://gpu` (open via DevTools in dev builds). For AMD/NVIDIA,
the flags in `main.js` may need adjustment.

## Why Electron, not yt-dlp or Android emulation?

YouTube's Data API doesn't return playable streams anymore, and scraping
(yt-dlp) is a constant cat-and-mouse with Google. The TV web UI is the path
of least resistance: it's the actual interface Google ships to TVs, so it
breaks the least, and we don't ship an Android emulator.
