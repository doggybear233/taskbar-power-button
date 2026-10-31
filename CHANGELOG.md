# Changelog

All notable changes to this project. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions use
[semantic versioning](https://semver.org/).

## [1.0.0] — 2026-10-31

First public release.

### Added

- Cancellable countdown before shutting down (5 s by default, configurable
  0–120 s), with three ways to abort: click the taskbar button again, press
  <kbd>Esc</kbd>, or use the button inside the countdown window.
- A *Power menu* button on the countdown card that opens the power menu without
  waiting for the countdown. <kbd>Shift</kbd> + click opens the same menu
  directly.
- Double-clicking the taskbar button skips the countdown and opens the power
  menu: the second click inside the system double-click interval is read as
  "menu", a slower second click is still "cancel". The decision
  window is 200 ms and the card is created only after it, so a double-click
  never flashes a shutdown window first, while a single click still responds
  immediately.
- The card and the menu dock next to the Start button. The taskbar rectangle and
  the `Start` button are read at launch, so a top, bottom, left or right taskbar,
  and a centred or left-aligned icon row, all land in the right place.
- Power menu with sleep, restart, shut down, sign out, lock, turn off display,
  scheduled shutdown, cancel shutdown and countdown settings.
- Interface in Chinese, Japanese and English, selected automatically from the
  Windows display language and overridable through `config.ini`, the
  `POWERBUTTON_LANG` environment variable or `--lang`. Detection reads
  `InstalledUICulture` rather than `CurrentUICulture`, because a process started
  from some launchers reports `en-US` even on a Japanese machine.
- Multi-size icon (nine sizes, each rendered natively) matching the Windows
  taskbar metrics.
- `install.ps1` / `uninstall.ps1`: rewrites the existing pinned shortcut in
  place, backs up the original on first install and restores it on uninstall.
- `install.cmd`: a one-click wrapper, because a `.ps1` that came out of a
  downloaded zip carries the mark of the web and the default `RemoteSigned`
  policy refuses it. It bypasses the policy for that one run, unblocks the
  unpacked files and keeps the window open so the result is readable.
- The installer now explains itself in Chinese, Japanese or English, following
  the same display-language rule as the UI.
- `-LnkPath` and `-Force` for the installer: it looks for `shutdown.lnk`, then
  for any pinned shortcut whose target is `shutdown.exe`, so a renamed or
  localised shortcut is still found; anything else is left untouched unless
  `-Force` is given. The path actually changed is recorded in
  `backup\link.txt` and restored by the uninstaller.
- The installer probes `Framework64`, then `Framework`, then `ProgramFiles(x86)`
  for `csc.exe`, so 32-bit and ARM64 Windows work without a separate build, and
  a machine without .NET Framework gets one readable sentence instead of a
  stack trace.

### Changed

- The shortcut no longer runs `shutdown.exe -s -t 0`. The countdown is drawn by
  the program and the shutdown is issued with `ExitWindowsEx` only when the
  countdown reaches zero, so Windows never shows its own "shutting down in less
  than a minute" dialog and cancelling leaves nothing registered with the
  operating system.
- The shortcut description and a shell verb in `HKCU` are written through
  `IShellLinkW`/`IPersistFile` in UTF-16. `WScript.Shell` writes `.lnk` files in
  the system ANSI code page, which turned parts of the Chinese description into
  `?` on a Japanese (CP932) system.
- The taskbar button's right-click menu belongs to Explorer and cannot be
  replaced from a pinned shortcut, so the docs now point at <kbd>Shift</kbd> +
  click and the card's *Power menu* button instead of claiming right-click.

### Notes

- No forced close: neither `/f` nor `EWX_FORCE` is used, so applications that
  refuse to close still get the usual "these apps are preventing shutdown"
  screen.
- The scheduled shutdown in the menu still uses `shutdown.exe`, because it has
  to outlive the process; Windows shows its dialog at the 60-second mark.
