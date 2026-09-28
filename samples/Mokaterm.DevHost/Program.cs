using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Extensions;
using Mokaterm.DevHost.Components;
using Mokaterm.DevHost.Demo;
using Mokaterm.DevHost.Demo.Mqtt;
using Mokaterm.DevHost.Demo.Telnet;
using Mokaterm.DevHost.Demo.Vnc;
using Mokaterm.DevHost.Hosting;
using Mokaterm.Modules.Ftp;
using Mokaterm.Modules.Mqtt;
using Mokaterm.Modules.Rdp;
using Mokaterm.Modules.Serial;
using Mokaterm.Modules.Ssh;
using Mokaterm.Modules.Telnet;
using Mokaterm.Modules.Vnc;
using Mokaterm.Sessions.Extensions;
using Mokaterm.UI.Extensions;

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
// machine, and there is no demo device to seed a login against: plug something in, or add a virtual pair.
builder.Services.AddMokaterm(modules => modules.AddSsh().AddFtp().AddVnc().AddRdp().AddTelnet().AddMqtt().AddSerial().AddModule(new DemoModule()));
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

app.MapStaticAssets();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

await app.RunAsync();
