## What's new in 0.1.6

- On the desktop, System and Docker now show their content when opened in their own window. In 0.1.5 that window came up empty.
- Closing a System or Docker view, or changing its page, no longer leaves errors in the crash log.

## What's new in 0.1.5

**Tools in every SSH session**

- **System**: a Linux manager for services, packages, cron, time zone, hostname, network interfaces, users, disks,
  processes, logs and the firewall. It detects the distribution and uses the right commands, shows every command before
  running it, and goes through sudo with the password on stdin.
- **Docker**: containers with logs, inspect, stats, env, mounts and a shell, Compose projects with per-service actions
  and merged logs, images, volumes and networks. Works with `docker compose` and `docker-compose`, finds compose and
  `.env` files, and runs commands inside a container.
- On the desktop each tool can open in its own window.

**SSH**

- Agent forwarding and X11 forwarding, per login and off by default.
- FIDO2 security keys (`ecdsa-sk`, `ed25519-sk`): from an agent, from key files, or made new from the keychain.

**Files**

- Transfers are verified with SHA-256 and resume where they stopped.
- Find files on the server, compare a remote folder with a local one, and copy, cut and paste on the server.
- Compress and extract archives, bookmark folders, and let the browser follow the terminal's folder.
- The editor highlights syntax and shows a diff before saving.
- Dropped files can upload without asking.

**Terminal and workspace**

- A local shell tab: PowerShell, cmd, Git Bash or WSL. Docker and Kubernetes shells too.
- Up to four panes, triggers that color matches or raise a notice, and timed session recordings that play back.
- Run one command on many logins and see the output per host.
- Right-click can paste, or copy a selection and paste otherwise.
- Quick connect suggests saved logins and recent targets as you type.
- The session toolbar is grouped, with a Tools menu, and can be set to flat or compact.

**Settings**

- The search finds single settings, not only pages.
- Settings can be exported and imported.

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
