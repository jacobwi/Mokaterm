# Plugins

Plugins for Mokaterm, from 0.2.0. Research and background are in `next-release.md`. Phase 1 below is built; phase 2 is
planned.

## Decisions

- **Who writes them**: mostly us for now, others later. So .NET plugins came first, built so they can open up later
  without a redesign.
- **What they add**: protocols, server tools, utility tools, Docker manager pages, System view pages, themes.
- **Code runs in-process**, behind a trust step. A separate process would not protect the vault (same user, same
  files), so it is not worth what it costs in UI.
- **Installed from a file the user picks.** A gallery comes later, and the package format is ready for it.
- **Hosts**: the desktop app installs from Settings. The web host loads only what the operator put in its plugins folder
  and lists it read-only, because there the code runs on the server. A plugin names the hosts it supports, the way serial
  and the local shell are desktop only.

## One package for everything

A plugin is a `.mokaplugin` file, which is a zip:

```
plugin.json
lib/            the plugin's assembly and its private dependencies (code plugins only)
wwwroot/        scripts, styles and images, served at _content/<AssemblyName>/
themes/         terminal themes in any format the theme import reads
```

```json
{
  "id": "moka.sample",
  "name": "Sample plugin",
  "version": "1.0.0",
  "publisher": "Moka",
  "description": "Shows that a plugin's view, script, styles and themes load.",
  "sdk": "0.2",
  "assembly": "Mokaterm.Plugins.Sample.dll",
  "hosts": ["desktop", "web"]
}
```

A plugin without `assembly` carries data only (a theme pack) and runs nothing. One format means one install flow, one
list and one gallery later. The id is lower-case letters and digits joined by dots or dashes, and names the plugin's
folder.

On the desktop, installed plugins live in `{DataDirectory}/plugins/<id>/`, with installs that wait for the next start in
`.pending/<id>/` and the state (enabled, trusted hash, removals) in `plugins.json`. Nothing in any of them is secret, and
none names a machine.

## What a plugin can add

| Extension point | Registration | Plugin use |
| --- | --- | --- |
| Protocol | `IProtocolProvider` and `AddProtocolUi` | A new protocol with its options editor and, if it needs one, its own session view. |
| Session tool | `AddSessionTool` | A toolbar tool like Docker or System, with its palette command. |
| Settings page | `AddSettingsPage` | The plugin's own settings, built from the shared fields. |
| Importer | `IConnectionImportSource` | Logins from another client. |
| System page | `AddSystemPage` | A page in the System view's side menu (the sample's fail2ban page). |
| Docker page | `AddDockerPage` | A page in the Docker manager's side menu. |
| App tool | `AddAppTool` | A tool that needs no session (the sample's subnet calculator), in the top bar's Tools menu and the palette. |
| Themes | `themes/` in the package | Terminal themes, listed after the built-in ones. |

### System and Docker pages

Every page, built-in or not, is a descriptor (id, title, icon, component, order, and a rule that says why the page
cannot be used on this server), and the views navigate by id. A page derives from `SystemPageBase` or `DockerPageBase`
in `Mokaterm.UI.Common.ToolPages` and gets `ISystemPageContext` or `IDockerPageContext`:

- System: the server's facts (distribution, init system, package manager, which commands exist, with the commands a page
  names probed during detection), read scripts through the exec channel budget with the view's limits, changes through
  the same review that shows the exact commands, root mode, polling while visible, navigation, the file editor.
- Docker: docker and compose calls through `DockerClient` with names checked, the same review for changes, sudo state,
  polling and navigation.
- The look comes with them: `ToolPageFrame`, `ToolDetailPanel`, `ToolNavigation` and `PagePoller` are in UI.Common.

## Loading

At start, before the service container is built (`PluginCatalog.Open`, then `AddPlugins`):

1. Apply what waits from the last run: removals first, then installs from `.pending`.
2. Read each folder's manifest. Skip a plugin that is off, built for another SDK, not meant for this host, or whose files
   no longer match the hash the user trusted.
3. Load each code plugin into its own `AssemblyLoadContext`. An assembly the host ships (anything on its trusted
   platform list) always comes from the host, because a second copy of `ComponentBase` is a different type and nothing
   would render. Everything else, native libraries included, comes from the plugin's `lib`. The contexts are not
   collectible: Blazor caches component metadata, so a plugin is never unloaded while the app runs.
4. The assembly names its module with `[assembly: MokatermPlugin(typeof(SampleModule))]`, so nothing is scanned, and
   `AddPlugins` adds it like any built-in module.
5. Anything that fails is a record, never a crash: a plugin that fails to load, whose assembly name or module id is
   already taken, is marked failed with the reason, shown on the Plugins page and in a notice after unlock.

Installing, enabling, disabling and removing all apply at the next start. The desktop page offers "Restart now".

**Safe start.** A marker file is written before plugins load and removed once the first page has rendered (desktop) or
the server listens (web). If it is still there at the next start, the previous start died with plugins on, so this one
starts without them and says so. `MOKATERM_NO_PLUGINS=1` does the same on purpose.

## Static files and styles

- A plugin's `wwwroot` is served at `_content/<AssemblyName>/`, the path built-in modules already use (`vnc.js`,
  `rdp.js`), so a module can move between built in and plugin without changing a line.
- Desktop: `MokatermWebView`, the main page's and `ToolWebView`'s base, overrides `CreateFileProvider`.
- Web host: a static file provider per plugin ahead of `MapStaticAssets` (`UsePluginFiles`). Same origin, so the CSP
  does not change.
- Scoped CSS: the host's `*.styles.css` only holds libraries referenced at build time, so the shell links each plugin's
  `<AssemblyName>.bundle.scp.css` itself (`PluginStyles`).

## Trust

- A data plugin installs and works without a warning: it runs nothing.
- A code plugin installs turned off unless the user ticks "I trust this publisher". Turning it on shows the publisher,
  every file with its SHA-256, the plugin hash and a plain warning: it runs with the same access as Mokaterm, including
  the vault while it is unlocked and every open session.
- The hash is SHA-256 over every file's path and SHA-256, sorted by path, so a package and the folder it was installed
  into give the same value. What is trusted is exactly what was shown: if the files change while the question is open,
  turning the plugin on is refused. Every start hashes the folder again, and files changed on disk turn the plugin off
  until the user trusts them again.
- An unreadable `plugins.json` counts as empty, which turns every code plugin off.
- Packages are read whole under caps (64 MB file, 128 MB unpacked, 4096 entries) with sizes counted while unpacking, and
  any path that is rooted, climbs out with `..`, or sits outside the four top-level names is refused.

## SDK

- NuGet packages (not published yet): `Mokaterm.Abstractions`, `Mokaterm.UI.Common`, and `Mokaterm.Plugin.Sdk`, which
  holds the MSBuild targets that build a `.mokaplugin` and brings the settings search generator as an analyzer.
- The targets keep every assembly the host provides out of `lib` (decided from the restore graph), write `plugin.json`
  with the `sdk` of the Mokaterm.Abstractions the plugin compiled against, take only the project's own static assets plus
  its scoped CSS bundle, and zip deterministically to `<id>-<version>.mokaplugin`.

```xml
<PropertyGroup>
  <MokatermPluginId>acme.proxmox</MokatermPluginId>
  <MokatermPluginName>Proxmox</MokatermPluginName>
  <MokatermPluginPublisher>Acme</MokatermPluginPublisher>
  <MokatermPluginHosts>desktop;web</MokatermPluginHosts>
  <MokatermPluginPackOnBuild>true</MokatermPluginPackOnBuild>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Mokaterm.UI.Common" Version="0.2.0" />
  <PackageReference Include="Mokaterm.Plugin.Sdk" Version="0.2.0" PrivateAssets="all" />
  <MokatermPluginTheme Include="themes/*.json" />
</ItemGroup>
```

- Compatibility while we are on 0.x: a plugin built against 0.2 loads in any 0.2.x and nothing else. From 1.0 the major
  version decides.

## Settings page

"Plugins" in the Application group: each plugin with its name, version, publisher, a CODE or DATA chip, the hosts it
supports, its themes and its state (on, off, failed, files changed, not compatible, skipped) with the reason, plus what
the next start changes. Actions: install from file, turn on or off, trust new files, remove, and "Restart now" on the
desktop. On the web host the list is read-only and names the operator's folder.

## Store

The listing has to say that Mokaterm supports plugins (policies 10.1.5 and 10.2.2). The data folder is writable in the
MSIX build, and the Store build restarts through the platform.

## Phase 1, built for 0.2.0

- Package format, loader, safe start, trust, the Plugins page and the install dialog, "Restart now" on the desktop.
- Static files and styles on the desktop (main window and tool windows) and both web hosts.
- System pages, Docker pages and app tools as descriptors, with every built-in page moved onto them.
- Theme packs.
- The SDK targets and packages, and `samples/Mokaterm.Plugins.Sample`: a session tool with its own script and styles, a
  fail2ban System page, a subnet calculator app tool and a theme. The tests install and load the package it builds.

## Phase 2

- The Kubernetes manager (next-release item 8) built as our first real plugin.
- Script tools: a System or Docker page described by a manifest (scripts, how to read their output into a list, actions
  on a row) and drawn by one built-in component. Nothing runs on the user's machine, which is what makes them the right
  format for community plugins.
- The gallery: an index in a GitHub repository with download addresses, hashes and Ed25519 publisher signatures, with
  our key built into the app so our own plugins show as verified.
- A credential source extension point (1Password and Bitwarden CLIs, KeePass).
- PublicApiAnalyzers on Abstractions and UI.Common, and the release workflow packing and pushing the three NuGet
  packages.

## Known gaps

- No unloading: every change to a plugin means a restart.
- The `mta-*` row and chip classes a System page uses come from `SystemAdminView.razor.css`, which reaches any page drawn
  in the System view; plugin pages rely on that without a declared contract.
- A plugin page that throws trips the whole tool's error boundary, so "Try again" restarts the tool, not the page.
- The file picker offers every file type, not only `.mokaplugin`.
- Installing a real package through the page's file picker has not been run in an app yet. The manager's install is
  covered by tests with the sample package, the dialog was tried against a fake manager, and the DevHost's picker hands
  back canned files; the DevHost shows installed plugins when their folder and `plugins.json` are put in place.
