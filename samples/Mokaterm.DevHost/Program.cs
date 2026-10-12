using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Plugins;
using Mokaterm.DevHost.Components;
using Mokaterm.DevHost.Demo;
using Mokaterm.DevHost.Demo.Mqtt;
using Mokaterm.DevHost.Demo.Telnet;
using Mokaterm.DevHost.Demo.Vnc;
using Mokaterm.DevHost.Hosting;
using Mokaterm.Modules.Ftp;
using Mokaterm.Modules.Local;
using Mokaterm.Modules.Mqtt;
using Mokaterm.Modules.Rdp;
using Mokaterm.Modules.Serial;
using Mokaterm.Modules.Ssh;
using Mokaterm.Modules.Telnet;
using Mokaterm.Modules.Vnc;
using Mokaterm.Sessions.Extensions;
using Mokaterm.UI.Extensions;
using Mokaterm.Web.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Every circuit unlocks the vault without a password, so this host must never run anywhere but a developer machine.
if (!builder.Environment.IsDevelopment())
{
	throw new InvalidOperationException("Mokaterm.DevHost unlocks its vault for every visitor and only runs in the Development environment.");
}

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents(options => options.DetailedErrors = true)
	.AddHubOptions(options =>
	{
		// Pasting a large block into the terminal arrives as one interop call; the 32 KB default would drop the circuit.
		options.MaximumReceiveMessageSize = 1024 * 1024;
	});

builder.Services.AddSingleton<IAppEnvironment, DevAppEnvironment>();
builder.Services.AddSingleton<ILocalFileAccess, DemoLocalFileAccess>();
builder.Services.AddSingleton<DevMasterPassword>();
builder.Services.AddSingleton<DemoVncServer>();
builder.Services.AddSingleton<DemoTelnetServer>();
builder.Services.AddSingleton<DemoMqttServer>();
builder.Services.AddSingleton<DevVaultSeeder>();

// Serial is registered so its editor, settings page and session panel can be looked at. It opens a real port on this
// machine, and there is no demo device to seed a login against: plug something in, or add a virtual pair. The local
// shell runs a real shell on this machine for whoever opens the page, which this host only allows in Development.
// Plugins are installed through the page here, as on the desktop, so the Plugins page can be tried. The folder outlives
// a run (unlike the vault), so an install survives the restart it waits for. Mokaterm:Plugins:Directory moves it.
string? pluginsDirectory = builder.Configuration["Mokaterm:Plugins:Directory"];
PluginCatalog plugins = PluginCatalog.Open(new PluginCatalogOptions
{
	Directory = string.IsNullOrWhiteSpace(pluginsDirectory) ? Path.Combine(Path.GetTempPath(), "mokaterm-devhost", "plugins") : pluginsDirectory,
	Host = HostKind.Web,
	UserManaged = true,
	SkipAll = PluginCatalogOptions.SkipAllRequested(),
});
builder.Services.AddMokaterm(modules => modules.AddSsh().AddFtp().AddVnc().AddRdp().AddTelnet().AddMqtt().AddSerial().AddLocal().AddModule(new DemoModule()).AddPlugins(plugins));
builder.Services.AddMokatermSessions();
builder.Services.AddMokatermUI();

WebApplication app = builder.Build();

// The demo servers first: the seeded logins need the loopback ports they picked.
DemoVncServer vnc = app.Services.GetRequiredService<DemoVncServer>();
vnc.Start();
app.Lifetime.ApplicationStopping.Register(() => vnc.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)));

DemoTelnetServer telnet = app.Services.GetRequiredService<DemoTelnetServer>();
telnet.Start();
app.Lifetime.ApplicationStopping.Register(() => telnet.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)));

DemoMqttServer mqtt = app.Services.GetRequiredService<DemoMqttServer>();
await mqtt.StartAsync();
app.Lifetime.ApplicationStopping.Register(() => mqtt.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)));

// Seeded before the server listens, so no circuit can see a vault that does not exist yet.
await app.Services.GetRequiredService<DevVaultSeeder>().SeedAsync(app.Lifetime.ApplicationStopping);

app.UseAntiforgery();

app.UsePluginFiles(plugins);
app.MapStaticAssets();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

// Listening with its plugins loaded, so the next start may load them again (see PluginCatalog).
app.Lifetime.ApplicationStarted.Register(plugins.ConfirmStarted);

await app.RunAsync();
