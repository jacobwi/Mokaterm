## What's new in 0.2.2

**Remote desktop (RDP)**

- A connection that fails now says which step it reached and why. A PC that does not answer on port 3389 is reported
  as such, with what to check: Remote Desktop turned on, the PC's firewall, and the port. Windows Home editions cannot
  accept Remote Desktop connections at all. Before, every one of these ended in a plain timeout.
- Every address a host name resolves to is tried, not only the first one.
- The server's certificate is checked before the login is sent.
- A Microsoft account name that the login cannot send is caught before connecting, with the name to use instead.
- A Kerberos server that cannot be reached no longer holds the connection open.
- Keys and clicks keep working after the remote desktop changes size, and a drag released outside the window no longer
  leaves the mouse button held down.

## What's new in 0.2.1

**Tool windows**

- On the desktop every tool can open in a window of its own, the Tools menu's tools included, with the same dark title
  bar as the main window.
- A tool window can stay on top, go back to the main window as a dialog, or bring the main window forward, and it comes
  back where you left it after a restart or an unlock.
- Settings, Appearance has a Tool windows section: open tools in their own window, reopen them at start, remember where
  they were, and show a tool's description in its header (off by default).

**Tooltips**

- Every icon button names itself in Mokaterm's own tooltip, placed below the button so it never covers the icon beside
  it: the top bar, the toolbars, the panels' collapse buttons, a tab's close button and the status bar included.
  Buttons that had no tooltip at all now have one, and greyed buttons and pages say why they cannot be used.
- Tooltips are dark instead of a white box, no longer stay up after a click, and are no longer cut off by the panel
  they sit in.
- Notifications open below the title bar instead of over the window's own buttons.

**Fixes**

- A password copied in a tool window is cleared from the clipboard on time even when that window was closed first.
- Downloads, previews and zips of files read as root go in pieces, so a slow download can no longer pull a whole file
  into memory. A file that changes on the server while it is read stops with a message instead of arriving mixed.
- An SSH connection that stops answering is noticed within about two minutes, and the session can reconnect, instead
  of waiting for the network to give up. Settings, SSH, "Missed keepalive replies" sets how many go unanswered first.
  A server that never answers keepalives is left alone.
- The fleet overview sorts by any column. The System view and the Docker stats page use the same sort headers.
- Sorting the System view's Users page no longer closes the view when two accounts share a name.

**Plugins**

- Three new plugins in the gallery: **Network Toolkit** (DNS, ping, port check, traceroute, whois, Wake-on-LAN, subnets),
  **Developer Utilities** (JWT, encoders, hashes, IDs, time, JSON and YAML, regex) and **Password Generator**
  (passwords, passphrases and PINs). Install them from Settings, Plugins, Browse.

## What's new in 0.2.0

**Plugins**

- Mokaterm now takes plugins: new protocols, server tools, pages in the System view and the Docker manager, tools in the
  new Tools menu at the top, and terminal themes.
- Install one from a `.mokaplugin` file in Settings, Plugins. A plugin that runs code installs turned off until you trust
  it: the install dialog lists every file with its hash, and a plugin whose files change on disk turns itself off until
  you trust the new files.
- Browse the plugin gallery from the same page on the desktop. The list is signed, every download is checked against
  it, and nothing is fetched until you open the tab. Installed plugins show when an update is available.
- Plugins can have settings of their own, under Plugins in the settings list.
- If a plugin stops Mokaterm from starting, the next start skips every plugin and says so. Setting
  `MOKATERM_NO_PLUGINS=1` does the same on purpose.
- The first plugin in the gallery is **Certificates**: read, make, convert, sign and encrypt with certificates and keys,
  check the certificate a server sends, and see the certificates a Linux server uses in the System view.
- On the web host, plugins come only from the operator's plugins folder (`Mokaterm:Plugins:Directory`, by default
  `plugins` in the data folder), and the page lists them without changing anything.
- Writing a plugin: `dotnet new install Mokaterm.Plugin.Templates`, then `dotnet new mokaterm-plugin`. The packages it
  builds against are on NuGet.

**Updates**

- Mokaterm now looks for new versions on GitHub by default. A check asks GitHub for the list of releases and sends
  nothing about you or this computer. Turn it off, or use another feed, in Settings, Updates.

**Other**

- Mokaterm is now MIT licensed.

## What's new in 0.1.9

**Docker**

- Compose and .env files open in the editor (highlighting, diff, conflict check). After a save, `compose config`
  checks the project and Up is offered.
- Check images for updates without pulling them, and update a Compose service or project in one step.
- A Cleanup page that shows exactly what each prune would remove, a live Stats page for every running container, and
  notices when a container turns unhealthy, stops on its own or keeps restarting.
- Create a container from an image, with the exact `docker run` shown before it runs.
- Open a container's published port in your browser: on the desktop through a tunnel over the session.
- Open a container shell in a tab of its own, and browse a container's files in the file browser.

**System view**

- Live CPU, memory, network and disk charts on the Overview, and process CPU measured over a second, with a process
  tree and renice.
- See what is using disk space, find large files, and read a disk's SMART health.
- Follow the journal and log files live.
- Install a key from your keychain into a user's authorized_keys, and remove keys.
- A Security page: effective sshd settings with fixes that are checked before sshd reloads, failed logins, fail2ban and
  listening ports.
- Security update counts, "reboot required", and a reboot that reconnects the session when the server is back.
- Edit a service's override file, create and delete groups, and delete ufw and firewalld rules.

**Fleet overview**

- Every saved Linux login in one table: OS, uptime, load, memory, disk, pending updates, reboot required, failed
  services and containers. Open it from the Connections panel or the command palette.

## What's new in 0.1.8

- The Docker manager's side menu matches the System view's: the same icons and labels, icons only in a narrower
  window and a page dropdown in a narrow one.

## What's new in 0.1.7

- On the desktop, System and Docker now show their content when opened in their own window. In 0.1.5 that window came up empty.
- Closing a System or Docker view, or changing its page, no longer leaves errors in the crash log.
- Built on Moka.Red 0.1.17, which now handles keyboard menus, dialog headers and nested split panes itself. Dialog
  icons follow its header style.

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

From 0.2.0, Mokaterm looks for a new version on GitHub shortly after it starts and shows a notice when there is
one. Nothing downloads or restarts without a click, because a restart closes every live session. Settings, Updates
turns the check off or points it at another feed.

Coming from 0.1.9 or earlier, run this version's Setup once: those versions had updates off.
