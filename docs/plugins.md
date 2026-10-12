# Plugins

Design for plugins, starting in 0.2.0. Research and background are in `next-release.md`.

## Decisions

- **Who writes them**: mostly us for now, others later. So .NET plugins come first, built so they can open up later
  without a redesign.
- **What they add**: protocols, server tools, utility tools, Docker manager pages, System view pages, themes.
- **Code runs in-process**, behind a trust step. A separate process would not protect the vault (same user, same
  files), so it is not worth what it costs in UI.
- **Installed from a file the user picks.** A gallery comes later, and the package format is ready for it.
- **Hosts**: the desktop app installs from Settings. The web host loads only what the operator put in its plugins folder
  and lists it read-only, because there the code runs on the server. A plugin names the hosts it supports, the way serial
  and the local shell are desktop only today.

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
  "id": "moka.proxmox",
  "name": "Proxmox",
  "version": "1.0.0",
  "publisher": "Moka",
  "description": "Guests, storage and backups on a Proxmox host.",
  "sdk": "0.2",
  "assembly": "Mokaterm.Plugins.Proxmox.dll",
  "hosts": ["desktop", "web"]
}
```

A plugin without `assembly` is data only (a theme pack) and runs nothing. One format means one install flow, one list
and one gallery later.

Installed plugins live in `{DataDirectory}/plugins/<id>/`, with their state (enabled, trusted hash) in
`{DataDirectory}/plugins/plugins.json`. Nothing in either is secret, and neither names a machine.

## What a plugin can add

| Extension point | Today | Plugin use |
| --- | --- | --- |
| Protocol | `IProtocolProvider` and `AddProtocolUi` | A new protocol with its options editor and, if it needs one, its own session view. |
| Session tool | `AddSessionTool` | A toolbar tool like Docker or System, with its palette command. |
| Settings page | `AddSettingsPage` | The plugin's own settings, built from the shared fields. |
| Importer | `IConnectionImportSource` | Logins from another client. |
| System page | New: `AddSystemPage` | A page in the System view's side menu (fail2ban, nginx sites, Postgres). |
| Docker page | New: `AddDockerPage` | A page in the Docker manager's side menu (Portainer stacks, registries). |
| App tool | New: `AddAppTool` | A tool that needs no session (subnet calculator, JWT decoder), opened from the palette and a Tools menu in the top bar. |
| Themes | Data | Terminal themes from the pack's `themes` folder. |

### System and Docker pages

Both views are closed today: their pages are internal enums (`AdminPage`, `DockerManagerPage`) switched over in the
view's markup, and everything a page touches is internal to the SSH module. To open them:

- Every page becomes a descriptor: id, title, icon, component, order, and a rule that says why the page cannot be used
  on this server (the greyed entry `ToolNavEntry.Unavailable` already draws). The built-in pages register the same way
  a plugin does, so there is one mechanism and not two. Navigation goes by id (`Navigate("services")`).
- A page gets a public context instead of the internal one: run a script (through the exec channel budget, with
  `mokaterm_limit`, `LC_ALL=C` and the output marker), run as root (through `SudoRunner`), review and run a change (the
  `AdminAction` review that shows the exact commands), the server's facts (distribution, init system, which commands
  exist), polling while visible, and opening a remote file in the editor. Docker pages also get docker and compose
  calls through `DockerClient`, with names checked by `DockerNames`.
- The contracts go in UI.Common (they carry component types) and Abstractions (the records), because a plugin, like a
  module, references nothing else of ours. The SSH module implements them over what it already has. `AdminPageFrame`
  and `ToolNavigation` move to UI.Common so plugin pages look like ours.

## Loading

At start, before the service container is built:

1. Read `plugins.json`, skip anything disabled, built for another SDK, not meant for this host, or whose files no
   longer match the hash the user trusted.
2. Load each code plugin into its own `AssemblyLoadContext`. An assembly the host ships (Mokaterm.Abstractions,
   Mokaterm.UI.Common, Moka.Red, ASP.NET Components, Microsoft.Extensions) always comes from the host, because a second
   copy of `ComponentBase` is a different type and nothing would render. Everything else, native libraries included,
   comes from the plugin's own folder through `AssemblyDependencyResolver`. The contexts are not collectible: Blazor
   caches component metadata, so a plugin is never unloaded while the app runs.
3. The assembly names its module with `[assembly: MokatermPlugin(typeof(ProxmoxModule))]`, so nothing is scanned, and
   the host calls `AddModule` with it like any built-in module.
4. A plugin that fails to load is disabled and reported after unlock, the way a damaged vault document is.

Installing, enabling, disabling and removing all apply at the next start; the page offers "Restart now".

**Safe start.** A marker file is written before plugins load and removed once the first page has rendered. If it is
still there at the next start, the previous start died with plugins on, so this one starts without them and says which
were active. `MOKATERM_NO_PLUGINS=1` does the same on purpose.

## Static files and styles

- A plugin's `wwwroot` is served at `_content/<AssemblyName>/`, the path built-in modules already use (`vnc.js`,
  `rdp.js`), so a module can move between built in and plugin without changing a line.
- Desktop: one `BlazorWebView` subclass for the main page and `ToolWebView` that overrides `CreateFileProvider` and adds
  each plugin's folder to the app's own.
- Web host: a static file provider per plugin ahead of `MapStaticAssets`. Same origin, so the CSP does not change.
- Scoped CSS: the host's `*.styles.css` only holds libraries referenced at build time, so the shell links each plugin's
  `<AssemblyName>.bundle.scp.css` itself.
- An assembly name that matches a built-in one is refused at install, since both would claim the same `_content` path.

## Trust

- A data plugin installs and works without a warning: it runs nothing.
- A code plugin installs disabled. Enabling it shows the publisher, the files with their SHA-256, and a plain warning:
  it runs with the same access as Mokaterm, including the vault while it is unlocked and every open session. The hash
  is recorded, and files changed on disk later turn the plugin off until the user confirms again.
- Later, with the gallery: publisher signatures (Ed25519), with our key built into the app so our own plugins show as
  verified and skip the warning.

## SDK

- NuGet packages: `Mokaterm.Abstractions`, `Mokaterm.UI.Common`, and `Mokaterm.Plugin.Sdk`, which holds the MSBuild
  targets that build a `.mokaplugin`, marks the shared packages as provided by the host so they stay out of the zip,
  and brings the settings search generator as an analyzer.
- PublicApiAnalyzers on the two libraries, so a change to the surface is a reviewed diff. A lot is public today only
  because components need it (`ShellEditorRequests`, `QuickConnectRow`); each of those becomes a promise.
- Compatibility while we are on 0.x: a plugin built against 0.2 loads in any 0.2.x and nothing else. From 1.0 the major
  version decides. `sdk` in `plugin.json` is written by the SDK, never by hand.

## Settings page

"Plugins" in the Application group: each plugin with its name, version, publisher, a CODE or DATA chip, the hosts it
supports and its state (enabled, disabled, failed with the reason). Actions: install from file, enable, disable,
remove, show folder, restart now. On the web host the list is read-only and names the folder.

## Store

The listing has to say that Mokaterm supports plugins (policies 10.1.5 and 10.2.2). The data folder is writable in the
MSIX build, so nothing else changes there.

## Phases

**Phase 1, in 0.2.0**

1. A spike first: one component from a non-default load context, rendered on the desktop, in a tool window and on the
   web host, with its script loaded from `_content`. Everything else depends on this working.
2. Package format, loader, safe start, trust step and the Plugins page.
3. Static files and styles on both hosts.
4. System pages, Docker pages and app tools as descriptors, with the built-in pages moved onto them.
5. Theme packs.
6. SDK packages and the pack target, plus `samples/Mokaterm.Plugins.Sample` (a fail2ban System page and a theme pack)
   that the tests install and load.

**Phase 2**

- The Kubernetes manager (next-release item 8) built as our first real plugin, which proves the SDK on something real.
- Script tools: a System or Docker page described by a manifest (scripts, how to read their output into a list,
  actions on a row) and drawn by one built-in component. Nothing runs on the user's machine, which is what makes them
  the right format for community plugins.
- The gallery: an index in a GitHub repository with download addresses, hashes and signatures.
- A credential source extension point (1Password and Bitwarden CLIs, KeePass).

## Risks

- Rendering components from another load context is the assumption everything rests on; the spike checks it first.
- A Moka.Red update can change what plugins compile against. The 0.x rule covers it: a new minor means a rebuild.
- The hosts are not trimmed and must stay that way, or a plugin could call a framework API the build removed.
- No unloading: disabling a plugin always means a restart.
