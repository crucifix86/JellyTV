const { app, BrowserWindow, globalShortcut, session } = require('electron');

const YOUTUBE_TV_URL = 'https://www.youtube.com/tv';

// PlayStation 4 UA — YouTube TV explicitly supports it and serves the lean
// D-pad-navigable TV interface. Smart-TV UAs (Tizen/WebOS) also work; PS4
// is the most stable across YouTube's periodic UA-allowlist tightening.
const TV_USER_AGENT =
  'Mozilla/5.0 (PlayStation; PlayStation 4/12.00) AppleWebKit/605.1.15 ' +
  '(KHTML, like Gecko) Version/14.0 Safari/605.1.15';

// Hardware video decode on Intel iGPU (N95/N100 UHD Graphics).
// VaapiVideoDecoder + ANGLE/EGL is the combo Chromium uses on Linux for VAAPI.
app.commandLine.appendSwitch('enable-features',
  'VaapiVideoDecoder,VaapiVideoEncoder,CanvasOopRasterization');
app.commandLine.appendSwitch('disable-features', 'UseChromeOSDirectVideoDecoder');
app.commandLine.appendSwitch('use-gl', 'angle');
app.commandLine.appendSwitch('use-angle', 'gl-egl');
app.commandLine.appendSwitch('ignore-gpu-blocklist');
app.commandLine.appendSwitch('enable-zero-copy');

let mainWindow = null;

function createWindow() {
  mainWindow = new BrowserWindow({
    fullscreen: true,
    kiosk: true,
    frame: false,
    backgroundColor: '#000000',
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  // Spoof UA at the session level so subresource requests (player API, etc.)
  // also see the TV UA — not just the top-level navigation.
  session.defaultSession.setUserAgent(TV_USER_AGENT);
  mainWindow.webContents.setUserAgent(TV_USER_AGENT);

  mainWindow.loadURL(YOUTUBE_TV_URL, { userAgent: TV_USER_AGENT });

  // Double-tap Back to exit to JellyTV.
  //
  // YouTube TV swallows Back/Escape for its own internal navigation (video →
  // channel → home tabs), so a single press can't reliably mean "exit the
  // sub-app." Android TV's convention is the same: two Backs in quick
  // succession quits, anything slower stays inside the app. First back press
  // also shows a small toast so the user knows the second press will exit.
  const DOUBLE_BACK_WINDOW_MS = 1200;
  let lastBackAt = 0;

  // Self-contained snippet executed inside the YouTube page. Creates the
  // toast element on first call and reuses it after that.
  const showToastJs = `(() => {
    const id = '__jellytv_exit_toast';
    let el = document.getElementById(id);
    if (!el) {
      el = document.createElement('div');
      el.id = id;
      el.style.cssText = [
        'position:fixed','bottom:64px','left:50%','transform:translateX(-50%)',
        'background:rgba(0,0,0,0.85)','color:#fff','padding:14px 28px',
        'border-radius:10px','font:500 22px/1 Roboto,Arial,sans-serif',
        'z-index:2147483647','pointer-events:none',
        'transition:opacity 180ms ease','opacity:0',
      ].join(';');
      el.textContent = 'Press Back again to exit';
      document.body.appendChild(el);
    }
    el.style.opacity = '1';
    if (el.__t) clearTimeout(el.__t);
    el.__t = setTimeout(() => { el.style.opacity = '0'; }, 1100);
  })();`;

  mainWindow.webContents.on('before-input-event', (event, input) => {
    if (input.type !== 'keyDown') return;
    const isBack = input.key === 'BrowserBack'
      || input.key === 'Escape'
      || input.code === 'Escape'
      || input.key === 'Backspace';
    if (!isBack) return;

    const now = Date.now();
    if (now - lastBackAt < DOUBLE_BACK_WINDOW_MS) {
      console.log('[youtube-tv] double-back → exit');
      event.preventDefault();
      app.quit();
      return;
    }
    lastBackAt = now;
    mainWindow.webContents.executeJavaScript(showToastJs).catch(() => {});
  });

  // Keep navigation locked to youtube.com — block accidental exits to ads,
  // login redirects to other Google properties, etc. (Google auth flows
  // stay on accounts.google.com, which we allowlist.)
  const allowedHosts = new Set([
    'www.youtube.com',
    'youtube.com',
    'm.youtube.com',
    'accounts.google.com',
    'accounts.youtube.com',
  ]);
  mainWindow.webContents.on('will-navigate', (event, url) => {
    const host = new URL(url).hostname;
    if (!allowedHosts.has(host)) {
      event.preventDefault();
    }
  });
  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));

  mainWindow.on('closed', () => { mainWindow = null; });
}

app.whenReady().then(() => {
  createWindow();

  // Exit-to-launcher key. Ctrl+Q is deliberately distinct from Escape so the
  // user's Escape/Back keypresses pass through to YouTube's own navigation
  // (which already implements Android-TV-style back-out from video → channel
  // → home). When wired into JellyTV, the controller's HOME button should
  // emit this combo (or JellyTV sends SIGTERM, same effect).
  globalShortcut.register('CommandOrControl+Q', () => app.quit());
  globalShortcut.register('F10', () => app.quit());
});

app.on('will-quit', () => globalShortcut.unregisterAll());

app.on('window-all-closed', () => app.quit());
