## What's new in 0.3.7

**System**

- **Updates Ubuntu is still rolling out are marked "Held by rollout"** on the Packages page, with a note saying why Upgrade
  all leaves them for now. They install once your machine's turn comes.

**Interface**

- Tabs that do not fit in a narrow panel now go into a menu instead of wrapping onto a second row.

Built on Moka.Red 0.3.16.

## What's new in 0.3.6

**Desktop**

- **Closing no longer freezes the window.** If ending your sessions takes a moment, a closing screen shows while it
  happens.
- **The main window opens where you left it**, at its size, and maximized if it was.
- The top bar dims while the window is not the active one, the way Windows' own title bars do.

**Appearance**

- **Status bar styles**: Standard, Accent, Hairline, Floating, Chips, Segments and Terminal, in Settings, Appearance.
- The current page in the System, Docker and plugin side menus uses the same highlight as the rest of the app.

**System**

- Fixed: Upgrade all skipped any update that needs a new package, which is every new kernel, and those stayed listed. It
  now installs them too.

## What's new in 0.3.5

**Docker**

- Command output, compose messages and health check results now wrap at spaces instead of splitting words at the end of a
  line.

Built on Moka.Red 0.3.14.

## What's new in 0.3.4

**System and Docker**

- **The System tool's Overview is a live dashboard**: gauges for CPU, load, memory and the root disk that open their
  pages, charts that scroll as new readings come in, and tiles for uptime, failed services, updates and reboots. Detail
  panels show their facts as tiles and lists, the sshd check and the reboot wait show their steps, and next cron runs and
  failed logins are timelines.
- **Docker's Stats page has live charts**: CPU and memory for the busiest containers, network and disk for all of them,
  with rates in the table, and a row opens that container's charts. Inspect shows tiles and cards above the raw JSON, and
  the Overview breaks containers and disk space into charts.

**Plugins**

- **The gallery's plugins were redesigned too.** Update Certificates, Developer Utilities, Kubernetes, Network Toolkit and
  Password Generator from the Plugins page. Network Toolkit also fixes a Ping size field that could make a ping fail.

## What's new in 0.3.3

**Plugins**

- **Fixed: after one crash, plugins stayed off at every start.** A start that skipped plugins because the last one ended
  while they were loading never cleared that state, so "Plugins are off for this start" came back every time. The first
  start of 0.3.3 clears it, and the one after loads your plugins again. The notice now also has a Restart now button.

## What's new in 0.3.2

**System and Docker**

- **Every list in the System and Docker tools is a real table now**: one line per row, columns that sort, the open row
  highlighted, the row menu on a right-click, and long lists (installed packages, processes, failed logins) scroll
  through thousands of rows instead of stopping at a cap.
- **The Docker manager looks and works like the System tool**: a header with the filter, the page's main action and
  refresh, and the list with its panel beside it. Containers are grouped by compose project in groups that fold, with
  logs, shell and restart a hover away; the Overview shows its counts as tiles, and Cleanup shows one kind at a time.

**Start screen**

- **A new start screen**: favorites and recent logins as two lists with each machine's colour, protocol, port and
  environment, the actions as tiles with their shortcuts, and a count of what the vault holds. An empty vault gets
  larger tiles and the protocols this copy speaks, and the offer to reopen last time's sessions names them.
- Fixed: clicking a star on the start screen also opened that login.

## What's new in 0.3.1

**Plugins**

- **The gallery's plugins have 0.3 builds**: Certificates 1.2.0, Developer Utilities 1.2.0, Kubernetes 1.1.0, Network
  Toolkit 1.3.0 and Password Generator 1.2.0. Settings, Plugins shows the updates, and installs them in place of the
  0.2 builds that 0.3.0 marked incompatible.
- For plugin authors: `Mokaterm.Plugin.TestHost` registers a navigation manager, so a test that renders a view with
  `ViewTabs` no longer fails.

## What's new in 0.3.0

**Look**

- **A new design: Mokaterm is built on Moka.Red 0.3.7 and its Red theme.** Controls are a little taller and easier to
  hit, toolbars wrap onto a second line instead of cutting off, and buttons, tabs and titles follow the theme's letter
  case. Its fonts (Inter, Space Grotesk and JetBrains Mono) come with the app. Density still applies: Compact, Cozy or
  Comfortable in Settings, Appearance.
- **A connection shows its steps while it connects.** SSH lists connect, host key, login and shell (jump hosts first);
  RDP lists connect, negotiate, TLS, certificate, sign-in and session. One that fails marks the step it failed at, with
  the reason under it and Edit connection beside it.
- **The file browser fits narrow panels.** The path gets a row of its own when the panel is small, with its copy button
  after it, a folder's first listing shows a loading outline, and an empty folder says so. Running transfers show a
  progress bar with the size, speed and time left.
- The System and Docker tools use Moka.Red's page header and tabs, and Docker's container and compose lists line up
  their columns across every group.
- The web host's reconnect screen is Moka.Red's, and says what each state means for the sessions the server holds.

**Desktop**

- **Mokaterm checks for the Microsoft Edge WebView2 Runtime before it opens.** Windows 11 and the installer bring it,
  but the portable zip does not, and a PC without it used to fail with no explanation. It now says what is missing and
  offers to open Microsoft's download page.
- `MOKATERM_STARTUP_TRACE=1` writes how long each step of a start took to `Data\logs\startup-*.log`, for reporting a
  slow start.

**Plugins**

- **Plugins built for 0.2 need a 0.3 build.** A plugin is tied to the Mokaterm minor version it was built against, so
  after this update Settings, Plugins marks 0.2 plugins as incompatible and leaves them off until a 0.3 build is
  installed. Nothing else about them changes.

**Under the hood**

- MAUI 10.0.120, FluentFTP 55.1.0 and Velopack 1.2.161.

## What's new in 0.2.11

**Look**

- Built on Moka.Red 0.2.0: dialogs fade out when they close instead of vanishing, and keep to the window when it is
  narrow.

**Plugins**

- **Network Toolkit 1.2.3 fixes its Connections tab**, which drew each connection's local address, remote address and
  state on top of each other and ran past the bottom of the tool. Settings, Plugins shows the update.

## What's new in 0.2.10

**Fixes**

- **Fixed: restarting after installing a plugin could say "another copy of Mokaterm is already running".** The copy
  that was closing held the data folder's lock a little longer than the new copy waited for it. The new copy now waits
  for the lock itself, and the old one ends itself after an orderly close instead of lingering.

## What's new in 0.2.9

**Terminal**

- **ZMODEM transfers: run `sz` on the server and Mokaterm answers it.** The offer bar shows the file's name, size and
  date (Save / Skip / Cancel), a progress line runs under the terminal (percent, speed, time left), and a finished
  transfer leaves a short `[mokaterm]` line in the scrollback. `rz` on the server offers to pick files and streams them
  out. The protocol is implemented in Mokaterm itself, to the last detail (corruption rewinds and retransmits, cancel
  works from either side, several files per run), and because nothing in it is SSH-specific, **serial and Telnet
  sessions get their first file transfer**.
- **Fixed: agent and X11 forwarding has never worked against a real server.** The connection died the moment the server
  opened the forwarded agent channel. Found by Mokaterm's own new Linux test server; forwarding now registers the
  channel with the SSH library the way its own port forwards do.
- **Fixed: a command run without a shell that read its input (`cat`, and anything like it) hung until its timeout.**
  Its input is now closed with an explicit end-of-file. Also found by the test server, against which the whole SSH
  suite, 2,092 tests, now runs.

**Look**

- Built on Moka.Red 0.1.22: a select's dropdown is a raised panel that fits its content, with a check on the chosen
  option and a styled scrollbar; the status bar's rules are drawn by the library instead of styled neighbours; and a
  JavaScript report can no longer take a render down with it (the crash behind 0.2.6's withdrawal, hardened at the
  source).

**Plugins**

- **Kubernetes 1.0.0 in the gallery**: a session tool for SSH with a cluster overview, pods and their detail, tail logs
  that follow, a one-shot command runner over exec, and confirmed rollout restart, undo and status. Settings, Plugins,
  Browse installs it.

## What's new in 0.2.8

**Plugins**

- Plugin updates get found on their own: the startup check also reads the plugin gallery's signed list once (Settings,
  Updates, "Check for plugin updates", on by default) and shows a notice naming the plugins with newer versions.
  Installing stays where it was (the Plugins page, with its trust step), and the web host never checks.
- The Plugins page gains "Check for updates" beside "Install from file", and a banner above the rows when updates are
  known. Every window of the run shares the one trusted list, so installed plugins show their update chips as soon as
  the app starts.

## What's new in 0.2.7

**Fixes 0.2.6, which failed to start.**

- 0.2.6 opened with "Mokaterm stopped working: Object reference not set to an instance of
  an object": the new cross-pane tab drag imported its script at a path that does not exist,
  and the failure took the whole interface down with it. The import now uses the path the
  shell itself uses, and a drag bridge that cannot attach is logged and swallowed, so tabs
  still move through their menu and the commands.
- If you installed 0.2.6, update to 0.2.7 from Settings, Updates (or the GitHub release).

## What's new in 0.2.6

**Connections**

- A tab can be dragged from one pane to another: the pane it is over draws a dashed target and takes the tab on drop.
  Dragging within a pane still reorders, and dropping files on the terminal works exactly as before.
- The connection and host editors submit on Enter, like every other editor, with one clearly primary button in every
  mode, and each section is a heading rather than a styled line.
- A favorite is a star on the welcome rows themselves (on for a favorite, pressable by keyboard), and the menu key or
  Shift+F10 opens a row's menu in the connections panel, so favoriting never needs a mouse.
- The quick connect's refusal text wraps instead of cutting off the reason.

**Look and consistency**

- The welcome screen's favorites take the same limit as recent connections and say how many more are in the panel; the
  welcome rows and the connections panel show the same logins at the same density.
- The settings search marks where each word landed in a row's label.
- The keychain and known-hosts lists show their shape while the vault is being read, instead of reading as empty.
- The focused pane is a tinted strip, not only an underline; the session toolbar's height follows the density set under
  Appearance; the Compact toolbar keeps the recording dot and Reconnect beside its menu.

## What's new in 0.2.5

**Plugins**

- A plugin that tries to take the id of a tool, page or protocol Mokaterm already has is refused at load, with the
  reason on the Plugins page, instead of silently replacing what was there.
- A page that throws no longer takes its whole tool with it: each System page, Docker page and tool says what went
  wrong itself, and Try again reloads that page alone.
- The install picker offers `.mokaplugin` files instead of every file.
- A plugin waiting for the next start says which version it brings, on its row and on its gallery card, instead of a
  versionless "Waits for restart".
- A download from the gallery belongs to the window rather than the page: leaving Settings no longer stops it, and a
  package that finished meanwhile opens its install dialog when you come back.
- For plugin authors: a page kit in `Mokaterm.UI.Common` (chips, fact lists, output boxes, and the System page styles as
  a linked contract rather than styles a page hopes to reach), and a fifth package, `Mokaterm.Plugin.TestHost`, that
  draws a plugin's views in its tests on a faked desktop or web host.

**Plugin updates (1.1.0, in the gallery)**

- **Certificates**: the server check probes each TLS version on its own and warns when a server still accepts one older
  than 1.2; STARTTLS for SMTP, IMAP and FTP; the System page finds certificates in Caddy, HAProxy, lighttpd and Traefik
  beside certbot, nginx and Apache, and can refresh itself while open.
- **Network Toolkit**: DS and DNSKEY lookups with the DNSSEC OK bit, the answer's AD flag marks validated replies;
  whois falls back to RDAP over HTTPS where port 43 gives no answer; the recent targets of the window sit under the
  DNS, ping, port, traceroute and whois inputs.
- **Developer Utilities**: a Common patterns picker in the regex tool (email, URL, IPv4 and IPv6, UUID, JWT, semver and
  more), pragmatic patterns with named groups where they help.
- **Password Generator**: characters to leave out of generated passwords, and the EFF short word list for shorter
  passphrases.

## What's new in 0.2.4

**Remote desktop (RDP)**

- A login with a short domain name, such as `CORP\you`, signs in quickly: Mokaterm finds the domain's full name the way
  Windows' own Remote Desktop does, so Kerberos works instead of waiting out a long lookup.
- With network level authentication off, RDP signs in by itself with the saved name and password, sent encrypted after
  the PC's certificate is checked. That is how Microsoft Entra ID (`AzureAD\you@company.com`) and Microsoft accounts
  get in from a PC that is not joined to Entra. A switch keeps Windows' own sign-in screen instead.
- A refused connection says what to change on the PC or in the login, and the security the PC picks is checked before
  the login goes out.

**Docker**

- The Docker manager spots containers stuck in a restart loop: a red Restart loop tag on the container and its compose
  service, a count on the Overview and one notice. Its Restarts tab says why (the exit code in plain words, out of
  memory, how long runs last and the log before the crash), with Stop restarting one click away.

**Desktop**

- Dialogs open below the title bar, so their close and header buttons are no longer covered by the window's minimize,
  maximize and close buttons.
- The Microsoft Store version shows its version as 0.2.4, like the GitHub version, instead of 0.2.4.0.

## What's new in 0.2.3

**Sessions and terminals**

- The status bar shows live CPU, memory and root disk use for the machine of the SSH session in front, coloured when it
  gets busy or full. Point at it for load, swap and uptime, or click it to open the System tool. Settings, Appearance
  turns it off or changes how often it reads.
- Saved commands can ask for values: write `{{name}}` or `{{name:default}}` and Mokaterm asks before typing or running
  the command, with a preview of the final line. Docker and Go templates such as `{{.Names}}` keep working as before.
- On the desktop, a trigger that shows a notice also shows a Windows notification while Mokaterm is minimized or behind
  another window, and clicking it brings that session's tab back. By default it names only the session and the rule; a
  setting adds the matched line.
- Pausing a session's triggers now holds when its tab moves to another pane.
- The quick connect box remembers the protocol you last picked.

**Files**

- Searching files over SSH runs one search on the server instead of opening every folder in turn, so a big tree takes
  seconds instead of minutes, as root and inside containers too. Accounts limited to SFTP, and BSD and macOS servers,
  search folder by folder as before.
- A folder that cannot be read, as root or in a container, now says so instead of looking empty.

**Docker**

- Log in to container registries from the new Registries page. The password goes to docker on stdin, and you can keep
  it in the vault for one-click logins.
- Build images (build args masked), tag and push them, with live output and a Stop button, and build a compose
  project's images.

**Import and session logs**

- Importing from OpenSSH, PuTTY or WinSCP on the desktop can copy the private keys your logins use into the keychain,
  PuTTY `.ppk` files included, and set those logins to sign in with them. A key with a passphrase asks for it when you
  connect.
- Session logs can clean up after themselves: delete logs older than a set number of days, or keep them under a size
  cap. The settings page shows how much space they take and can delete one, several or all of them.

**Remote desktop (RDP)**

- Sideways scrolling goes the right way, and a fast scroll no longer arrives as a tiny one.
- When the server ends a session (you sign out, an administrator disconnects you, or someone else connects), the tab
  says why instead of reporting a lost connection; a real drop still reconnects.
- A remote desktop that can no longer be drawn drops the session so you can reconnect, instead of freezing.

**Security and look**

- A password copied from Mokaterm is cleared at its timeout even while the window is in the background, and when you
  close the app before the timeout, unless something else has been copied since.
- Every remaining hint (what a figure or count means, why a row is marked, the exact time behind "3 days ago") shows in
  Mokaterm's own tooltip. The browser's tooltip stays only for the full text of names and paths that are cut short.
- The fingerprint copy buttons in a host key question say which fingerprint they copy.

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
