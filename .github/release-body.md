## Install

Download **Moka.Mokaterm-win-Setup.exe** and run it. It installs for the current user, needs no
administrator, and adds a Start menu and a desktop shortcut. **Moka.Mokaterm-win-Portable.zip** is the
same build without an installer.

Windows 10 1809 (build 17763) or later, x64. Setup installs the WebView2 runtime if the machine lacks it.

## This build is not signed

SmartScreen shows "Windows protected your PC" before Setup runs. Pick **More info**, then **Run anyway**.
Where Smart App Control is on, it blocks the file outright.

## Where your data lives

Settings, the vault and session logs stay in `%LOCALAPPDATA%\Mokaterm`. The app installs to
`%LOCALAPPDATA%\Moka.Mokaterm`, and uninstalling removes only that folder: your hosts, keys and passwords
are left alone.

## Updating

In-app updates are off until you set an update feed in Settings, Updates. Nothing downloads or restarts
without a click, because a restart closes every live session.
