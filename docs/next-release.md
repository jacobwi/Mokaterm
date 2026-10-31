# Next release (0.2.5)

## Plugin system

1. **A plugin can no longer shadow a built-in id.** A plugin whose app tool, session tool, System page, Docker page or
   settings page registers under an id Mokaterm or another plugin already uses is refused at load, with everything it
   registered taken back out, the way a plugin with a stolen settings name already was. Session tools clash per
   protocol, since tools on different protocols share a command on purpose. Descriptors now say what they are through
   `IContributionIdentity` in Abstractions, which is what the loader checks.
2. **A page that throws no longer takes its tool with it.** Every System page, Docker page and app tool sits in a
   `ViewBoundary` of its own, so "Try again" reloads the page and the tool's other pages carry on. The boundary moved
   from the shell into `Mokaterm.UI.Common`, which is what lets the System and Docker views use it.
3. **The install picker offers `.mokaplugin` files only** (`ILocalFileAccess.PickFilesAsync` takes the extensions to
   offer; MAUI filters in its dialog, the browser through `accept`).
4. **A waiting install says which version it brings.** The record carries the pending folder's version, so a row reads
   "1.1.0 is installed. It loads at the next start." and a gallery card "1.1.0 waits for restart" instead of a
   versionless "Waits for restart".
5. **A gallery download belongs to the window, not the page.** Leaving the Plugins page no longer cancels it: the
   download runs as the window's `IPluginGallery.Download`, coming back shows how far it is, and a package that
   finished in the meantime opens its install dialog on return.
6. **A page kit in UI.Common.** `Mokaterm.UI.Common.PluginUi` (StatusChip, ChipRow, Fact/FactList, OutputBox) is what
   the four first-party plugins had drifted three copies of, and the System page classes (`mta-*`) ship as
   `system-pages.css`, linked by the view, so a plugin page styles itself by contract instead of reaching into the Ssh
   module's stylesheet.
7. **`Mokaterm.Plugin.TestHost`**, a fifth SDK package: the static-render harness a plugin's tests draw its views with,
   on a faked desktop or web host, with settings, notices and the clipboard steerable by the test.

Still to do: PublicApiAnalyzers on the two contract packages needs a baseline generator first (the RS0016 fixer cannot
run headless and there is no genapi tool on NuGet), so it stays on the list.

## Older plans

The notes below are what earlier releases were planned from, kept for the history. The "Carried over" item about the
fleet survey's local shell test landed in 705dd7b (the test puts a fake `docker` first on PATH).

## Reliability and shipping

1. **A real Linux test server.** Everything from 0.1.5 to 0.1.9 (System view, Docker manager, fleet overview, agent and
   X11 forwarding, security keys, tool windows) has met only fixtures, Git Bash and the DevHost. One container with
   OpenSSH and Docker, run through Docker Desktop on the dev machine, lets the live tests (`MOKATERM_TEST_SSH`) run
   against a real server. Expect it to find bugs.
2. **Updates that work without setup.** Done: the feed defaults to the GitHub releases, read through Velopack's GitHub
   source, and the Updates page says a check only asks for the release list. "Check at startup" (on by default) makes
   that one request each start. The Store build has no updater and is not affected.
3. **Flaky tests.** Sweep for timing-sensitive tests (the RDP hang-up test and an MQTT loopback port clash have both
   flaked), fix them, and add retries to the release workflow so one flake cannot stop a release again. 0.1.6 never
   published because of this.
4. **Code signing** for Setup.exe and the portable zip, so SmartScreen and Smart App Control stop warning. The packaging
   script already takes `-AzureTrustedSignFile`; it needs an Azure Artifact Signing account. The Store signs its own
   package.

## Fixes from the known gaps

5. Root downloads, zips and previews stream `sudo cat` through a channel SSH.NET buffers without limit, so a slow browser
   download can hold a whole file in memory. Bound it. Done: root reads are `ReadRange` pieces through sudo, the stream
   container files already used (`PieceReadStream`), so a reader holds at most two pieces (8 MiB) whatever its pace,
   with the next piece read ahead once past the first two; a file that changes between pieces fails the read.
   `RemoteCommand.RunAsync` keeps 1 MiB of stderr and drains the rest.
6. SSH keepalives expect no reply, so a half-open connection is only noticed when TCP gives up. Done: every keepalive
   interval also sends `keepalive@openssh.com` wanting a reply, and once the server has answered one, "Missed keepalive
   replies" (3) unanswered intervals drop the session and reconnect, as OpenSSH's `ServerAliveInterval` and
   `ServerAliveCountMax` do. A server that never answers is never dropped and stops being asked. Not yet run against a
   real server.
7. The fleet overview table cannot be sorted. Done: every column sorts by value, and the System view and Docker stats
   lists share the same `SortHeader`.

## Features

8. A Kubernetes manager, built like the Docker manager (pods, logs, exec, rollout restart over kubectl through SSH).
9. Live host stats (CPU, memory, disk) for a session in the toolbar or status bar, reusing the System view's sampler.
   Done in 0.2.3: the status bar, with one sampler per session shared with the System view's Overview.
10. `rz`/`sz` (ZMODEM) file transfer inside the terminal, which would also give serial sessions a transfer.
11. Command snippets with placeholders, such as `systemctl restart {{service}}`, asked for when run, including across
    several hosts. Done in 0.2.3.
12. Vault sync through a folder (OneDrive, Dropbox): the vault is encrypted per document already, so no account and no
    server of ours.
13. Windows notifications for trigger alerts, so a notice raised while the window is minimized is not missed. Done in
    0.2.3.
14. Docker registry login, image push and build. Done in 0.2.3, with tag and compose build.

## License

Mokaterm is MIT licensed from 0.2.0 (`LICENSE` at the root, `Copyright (c) 2026 Moka`), the same as Moka.Red, and
`Directory.Build.props` sets `PackageLicenseExpression` for the packages a plugin SDK would ship as. The vendored noVNC
stays MPL-2.0 and xterm.js MIT, each under its own license file. Plugin authors can build on the SDK under the same
terms, open or closed.

## Plugins

15. **Plugins**: features we, and later other people, build and the user installs. Phase 1 is built (see
    `plugins.md` for what it covers and what phase 2 adds). The findings that led to it are below.

### What we already have

Every protocol is an `IMokatermModule` that registers services and descriptors, `AddModule` skips a module id it has
seen, modules reference UI.Common and nothing else of ours, and their views are component types the shell renders by
type, with no routing. A module built outside this repo has the same shape as ours.

The extension points a module can fill today: `IProtocolProvider` with `AddProtocolUi` (a protocol, its options editor
and optionally its own session view), `AddSessionTool` (a toolbar tool, which also gets a palette command),
`AddSettingsPage`, and `IConnectionImportSource` (an importer). There is no extension point for credential sources
(1Password, Bitwarden, KeePass), terminal actions or shell commands outside a tool.

### What loading .NET code at runtime needs

1. Discovery at start (`{DataDirectory}/plugins/<id>/plugin.json` beside the assembly), before the container is built.
   Installing or removing a plugin means a restart.
2. One `AssemblyLoadContext` per plugin that resolves the plugin's own dependencies but hands back the host's
   Mokaterm.Abstractions, Mokaterm.UI.Common, Moka.Red and ASP.NET Components assemblies. A second copy of
   `ComponentBase` is a different type and nothing would render. Not collectible: Blazor caches component metadata.
3. Static files. Modules import scripts from `_content/<Assembly>/js/...` (VNC, RDP). On the desktop that needs a
   `BlazorWebView.CreateFileProvider` override serving each plugin's `wwwroot`, in the main view and in `ToolWebView`.
   On the web host, a static file provider per plugin under `/_content/<id>`.
4. Scoped CSS. `Mokaterm.Maui.styles.css` is built from the libraries referenced at build time, so a plugin's
   `.bundle.scp.css` would be missing and the shell has to link it.
5. Settings search. The generator runs at build time, so it ships to plugin authors as an analyzer in the SDK package.
6. An SDK. Abstractions and UI.Common as NuGet packages with a tracked public surface (PublicApiAnalyzers) and a host
   version range in `plugin.json`. Today a lot is public only because components need it (`ShellEditorRequests`,
   `QuickConnectRow`); in an SDK every one of those becomes a promise.

### Security

.NET has no sandbox. A plugin runs inside the process that holds the unlocked vault key, every saved password while
unlocked, and every live session. A separate process does not change that on the desktop: it runs as the same user, who
can read the app's memory and files anyway. It only buys crash isolation and a free choice of language. So the real
protection is what the user agrees to install: plugins off by default, installed only by the user from a file (never
fetched for them), listed in Settings with publisher and path, and a plain warning before the first load. On the web
host only the operator's folder counts, never an upload through the page, because the code runs on the server.

### Store policy

10.1.5 allows add-ons or extensions that enhance the product, with the user's consent, after install. 10.2.2 forbids
dynamically included code that fundamentally changes or extends the described functionality. So the listing has to
describe plugin support, and plugins have to stay within it.

### Not pursued: Store add-ons

Decided on 2026-10-12 not to sell add-ons. What we learned, in case that changes:

- A durable add-on (bought once) is the type that fits. The API is `StoreContext` in `Windows.Services.Store`, which
  needs package identity, so only the Store build could sell. Testing works because the app is already published.
- Microsoft's commerce takes 15% from apps (the 2021 figure). Policy 10.8.1 lets non-game PC apps use their own payment
  provider instead, even in the Store build.
- A Store add-on cannot deliver code (add-ons with packages are limited to a restricted set of developers), so paid
  code would ship inside our package and a license would turn it on.

Sources: [Create an add-on submission](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/add-on/create-app-submission),
[In-app purchases and trials](https://learn.microsoft.com/en-us/windows/uwp/monetize/in-app-purchases-and-trials),
[Store policies 7.20](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies).
