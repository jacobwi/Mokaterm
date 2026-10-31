# Plugins

Plugins for Mokaterm, from 0.2.0. Research and background are in `next-release.md`. Phase 1 below is built; phase 2 is
planned.

## Decisions

- **Who writes them**: mostly us for now, others later. So .NET plugins came first, built so they can open up later
  without a redesign.
- **What they add**: protocols, server tools, utility tools, Docker manager pages, System view pages, themes.
- **Code runs in-process**, behind a trust step. A separate process would not protect the vault (same user, same
  files), so it is not worth what it costs in UI.
- **Installed from a file the user picks, or from the gallery** (the Plugins page's Browse tab, desktop only), which
  hands the same install dialog the same package.
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
| Session tool | `AddSessionTool` | A toolbar tool like Docker or System, with its palette command, in a dialog or (desktop) a window of its own. |
| Settings page | `AddSettingsPage` | The plugin's own settings, built from the shared fields, in the settings list's Plugins group (the sample's refresh and subnet settings). |
| Importer | `IConnectionImportSource` | Logins from another client. |
| System page | `AddSystemPage` | A page in the System view's side menu (the sample's fail2ban page). |
| Docker page | `AddDockerPage` | A page in the Docker manager's side menu. |
| App tool | `AddAppTool` | A tool that needs no session (the sample's subnet calculator), in the top bar's Tools menu and the palette, in a dialog or (desktop) a window of its own. |
| Themes | `themes/` in the package | Terminal themes, listed after the built-in ones. |

### Settings

A plugin's settings are a record implementing `ISettingsSection`, stored in `settings.json` and carried by the settings
backup, with a page on `SettingsSectionBase<T>` registered through `AddSettingsPage` in `SettingsPageGroup.Plugins`.
Its section key and its page id both start with `plugin.` and the plugin's id (`plugin.moka.sample`), which the loader
checks: a plugin that picks another name (`ssh`, `terminal`) is refused and everything it registered is taken out again,
so it can never overwrite one of Mokaterm's own sections. The Plugins page links each loaded plugin to its page with a
Settings button, on the web host too. A removed plugin's section stays in `settings.json` untouched.

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
   already taken, or whose settings are not under its own name, is marked failed with the reason, shown on the Plugins
   page and in a notice after unlock.

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

- NuGet packages: `Mokaterm.Abstractions`, `Mokaterm.UI.Common`, `Mokaterm.Plugin.Sdk` (the MSBuild targets that build a
  `.mokaplugin`, plus the settings search generator as an analyzer) and `Mokaterm.Plugin.Templates`
  (`dotnet new mokaterm-plugin`).
- `build/pack-nuget.ps1 -Version <version>` packs all four into `artifacts/nuget`, and `build/test-plugin-template.ps1`
  checks them the way an outside author meets them (see Starting a plugin). CI packs them on every push, and the
  release workflow packs them at the tag's version, attaches them to the GitHub release and pushes them to nuget.org
  through Trusted Publishing: the nuget.org policy "Mokaterm releases" (owner BCat) trusts `release.yml` in this
  repository, and each release trades GitHub's OIDC token for a key that lasts an hour, so no NuGet key is stored. The
  `Mokaterm.` id prefix should be reserved on nuget.org (by email to account@nuget.org) so nobody else can publish
  under it.
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

### Starting a plugin

```
dotnet new install Mokaterm.Plugin.Templates
dotnet new mokaterm-plugin -n Acme.Proxmox --kind system-page --publisher "Acme" --hosts both
```

- `--kind`: `tool` (default) is an app tool (a password generator) with its settings page, `system-page` a System view
  page (kernel parameters read from `/proc/sys`) with its settings page, `theme` a theme pack with no code.
- `--id` defaults to the project name in lower case with anything outside `a-z`, `0-9`, dots and dashes turned into
  dashes; `--publisher` to the name up to its first dot; `--description` to a sentence about the kind; `--hosts` is
  `both`, `desktop` or `web`.
- The generated project references `Mokaterm.UI.Common` and `Mokaterm.Plugin.Sdk` at exactly the version the template
  package was packed with, so the four packages are released together. `build/test-plugin-template.ps1` packs them into
  a local feed, generates and builds every kind as an outside author would, checks each package and loads it through
  the real loader.

## Settings page

"Plugins" in the Application group: each plugin with its name, version, publisher, a CODE or DATA chip, the hosts it
supports, its themes and its state (on, off, failed, files changed, not compatible, skipped) with the reason, plus what
the next start changes. Actions: install from file, turn on or off, trust new files, remove, and "Restart now" on the
desktop. Where a gallery is registered (the desktop) the list is the Installed tab and the gallery is the Browse tab.
On the web host the list is read-only, names the operator's folder and has no tabs.

## Store

The listing has to say that Mokaterm supports plugins (policies 10.1.5 and 10.2.2). The data folder is writable in the
MSIX build, and the Store build restarts through the platform.

## Community plugins: the gallery

Everything lives on GitHub, with no server of ours: a gallery repository, GitHub Actions to build and check, Releases
for the packages and Pages for the index. Homebrew and Scoop run the same way. The app's side (the Browse tab, the
signature check and the update chips) is built; the index format is `docs/index-format.md` in the gallery repository.

**Submitting.** An author adds a plugin, or a new version, with a pull request to the gallery repository
(`jacobwi/mokaterm-plugins`) holding one small file per plugin:

```json
{
  "id": "acme.proxmox",
  "source": "https://github.com/acme/mokaterm-proxmox",
  "tag": "v1.2.0",
  "commit": "9f2c4e1",
  "license": "MIT",
  "categories": ["server-tools"]
}
```

**Building from source.** The gallery's workflow checks out that exact commit, builds it with the published SDK and
checks the result: the manifest against the file, the id against the folder, the `sdk` against current Mokaterm, the
settings names, the size, and that `lib` holds nothing the host ships. It then publishes the `.mokaplugin` to the
gallery's own Releases. What people install is always built from public source anyone can read, never a binary the
author uploaded, which is the F-Droid rule.

**Reviewing.** Code plugins run with Mokaterm's own access, so a person reads the diff of every new plugin and every
update before merging; the checks make that quick, they do not replace it. Theme packs, and later script tools, run
nothing on the user's machine and get a lighter review. Listing needs an open source license.

**The index.** The workflow writes `index.json` (each plugin's versions, download address, SHA-256, publisher,
categories, the source commit, and any version withdrawn) to GitHub Pages and signs it with an Ed25519 key whose public
half is built into the app. A compromised repository, account or CDN cannot swap a package without the signature
failing. Our own plugins are signed the same way and show as verified.

**In the app.** A Browse tab on the Plugins page, beside Installed. Only the desktop host registers the gallery
(`AddPluginGallery`), so the web host, whose operator copies files by hand, has no tab and asks nothing of GitHub.

- The index is fetched only when the tab is opened, never in the background, so nothing leaves the machine unasked. The
  tab says which host it asks. A window keeps what it read until Refresh; nothing is kept across starts.
- Plugins by category and search, each on a card with its publisher, license, the verified badge, CODE or DATA, the
  hosts, a link to the source and Install, Update or Installed.
- Install downloads the package, checks it against the signed index and then opens the same install dialog as a file
  does, adding "From the Mokaterm gallery, built from https://github.com/acme/mokaterm-proxmox at 9f2c4e1". The trust
  step does not change.
- Once the tab read the index, an installed plugin shows "Update available" when the index has a newer version built
  for this Mokaterm, and a warning with the gallery's reason when its version was withdrawn.

**Checks.** Requests carry a `Mokaterm/<version>` user agent and nothing else (no cookies, no referrer).

1. `index.json` (at most 4 MiB) and `index.json.sig` (at most 1 KiB) are read over https, each wait limited to 30
   seconds.
2. The signature (88 characters of base64, a final newline allowed) is checked with Ed25519 over the exact bytes
   against every key in `PluginGalleryKeys` before any field is read. A missing or wrong signature refuses the whole
   list. The app carries a list of keys so a new one can ship a release before the gallery signs with it.
3. The list is read with the format's limits (2000 plugins, 100 versions each, 1000 characters a text field, ids by
   the plugin id rule, https addresses, lower-case hashes and commits). A field that breaks a rule refuses the whole
   list, since the gallery's own workflow signed it. `format` above 1 asks for an update of Mokaterm.
4. A version is offered only when this build loads its `sdk` (data loads on any), the plugin names this host (or none)
   and it is not withdrawn. A card with nothing offered says why.
5. A download is https only, stops past the size the index lists (and 64 MiB), must be exactly that size and match its
   SHA-256, then goes through `IPluginManager.ReadPackageAsync` like a picked file. The package's id, version and code
   or data must match the entry.

`MOKATERM_GALLERY_INDEX` points the desktop app at another copy of the index (a preview, a mirror), which still has to
carry a production signature. The DevHost serves a demo gallery from memory, signed at start with a key made for that
run (the only place a key beside the production ones is trusted), listing the sample plugin's package when it is
built, a theme pack in two versions with the older withdrawn, and two plugins that do not fit the host;
`Mokaterm:Gallery:Index` points it at a real gallery instead.

**Order of work.**

1. Published packages and the template, so anyone can build a plugin at all (ready for 0.2.0; the first release
   tag publishes them).
2. PublicApiAnalyzers on Abstractions and UI.Common, so the surface plugins build against only changes on purpose.
3. The gallery repository: submission format, build and check workflow, the signing key kept as a GitHub secret.
4. The Browse tab, signature check and update chips in the app (built).

GitHub topic search (every repository tagged `mokaterm-plugin`) is not the source of truth: it has no hashes, no
review and a low rate limit. It could later list unreviewed plugins behind a clear warning.

## Phase 1, built for 0.2.0

- Package format, loader, safe start, trust, the Plugins page and the install dialog, "Restart now" on the desktop.
- Static files and styles on the desktop (main window and tool windows) and both web hosts.
- System pages, Docker pages and app tools as descriptors, with every built-in page moved onto them.
- Theme packs.
- The SDK targets and packages, the `dotnet new mokaterm-plugin` template (an app tool, a System page or a theme pack), and `samples/Mokaterm.Plugins.Sample`: a
  session tool with its own script and styles, a fail2ban System page, a subnet calculator app tool, a settings page and
  a theme. The tests install and load the package it builds.

## Phase 2

- The Kubernetes manager (next-release item 8) built as our first real plugin.
- Script tools: a System or Docker page described by a manifest (scripts, how to read their output into a list, actions
  on a row) and drawn by one built-in component. Nothing runs on the user's machine, which is what makes them the right
  format for community plugins.
- The gallery repository and its workflow (see Community plugins above; the app's side is built).
- A credential source extension point (1Password and Bitwarden CLIs, KeePass).
- PublicApiAnalyzers on Abstractions and UI.Common.

## Known gaps

- No unloading: every change to a plugin means a restart.
- The `mta-*` row and chip classes a System page uses come from `SystemAdminView.razor.css`, which reaches any page drawn
  in the System view; plugin pages rely on that without a declared contract.
- The gallery's production index exists (the four first-party plugins were released on it), but the app has not read it
  outside the DevHost and its tests: downloads follow GitHub's redirect to its storage host, which no test has done for
  real.
- Installing a real package through the page's file picker has not been run in an app yet. The manager's install is
  covered by tests with the sample package, the dialog was tried against a fake manager, and the DevHost's picker hands
  back canned files; the DevHost shows installed plugins when their folder and `plugins.json` are put in place.

Fixed in 0.2.5: a plugin registering a built-in's tool or page id is refused at load; a page that throws trips only its
own error boundary; the install picker offers `.mokaplugin` files; a waiting install names the version it brings; and a
gallery download belongs to the window, so leaving the Plugins page neither stops it nor loses the package.
