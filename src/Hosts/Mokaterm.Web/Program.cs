using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Core.Extensions;
using Mokaterm.Modules.Ftp;
using Mokaterm.Modules.Mqtt;
using Mokaterm.Modules.Rdp;
using Mokaterm.Modules.Ssh;
using Mokaterm.Modules.Telnet;
using Mokaterm.Modules.Vnc;
using Mokaterm.Sessions.Extensions;
using Mokaterm.UI.Extensions;
using Mokaterm.Web.Components;
using Mokaterm.Web.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// SignalR traces every hub invocation with its arguments at Debug, and in a circuit those arguments are keystrokes and
// form values: the master password, whatever is typed at a sudo prompt. These rules land after every configured one and
// for every provider named there, because a provider's own rule beats a general one whatever its category. A broad
// Debug level therefore cannot turn the trace on; a longer category under Microsoft.AspNetCore.SignalR still can.
builder.Services.PostConfigure<LoggerFilterOptions>(options =>
{
	List<string?> providers = [.. options.Rules.Select(rule => rule.ProviderName).Append(null).Distinct()];
	foreach (string? provider in providers)
	{
		options.Rules.Add(new LoggerFilterRule(provider, "Microsoft.AspNetCore.SignalR", LogLevel.Information, filter: null));
	}
});

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents(options =>
	{
		options.DetailedErrors = builder.Environment.IsDevelopment();

		// Sessions belong to the circuit; keep it alive through short network drops so SSH sessions survive them.
		options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
	})
	.AddHubOptions(options =>
	{
		// Pasting a large block into the terminal arrives as one interop call; the 32 KB default would drop the circuit.
		options.MaximumReceiveMessageSize = 1024 * 1024;
	});

// Nothing protected with these keys outlives the process: circuits, the descriptors that start them and antiforgery
// tokens all end with it. Keeping the keys in memory means no key file sits next to the vault, and no startup warning
// about keys stored unencrypted in a folder that may not survive the container.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

builder.Services.Configure<ForwardedHeadersOptions>(options => ReverseProxy.Configure(options, builder.Configuration));
builder.Services.AddHealthChecks();

builder.Services.AddSingleton<IAppEnvironment, WebAppEnvironment>();
builder.Services.AddSingleton<DownloadTicketStore>();
builder.Services.AddScoped<ILocalFileAccess, BrowserLocalFileAccess>();

builder.Services.AddMokaterm(modules => modules.AddSsh().AddFtp().AddVnc().AddRdp().AddTelnet().AddMqtt());
builder.Services.AddMokatermSessions();
builder.Services.AddMokatermUI();

WebApplication app = builder.Build();
AllowedHostsCheck.WarnIfOpen(app);

// First, so the HTTPS redirect, HSTS and every log line see the client's scheme and address instead of the proxy's.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
	// A handler rather than a path: the app has no error page, and re-running a path that is not mapped returns 404,
	// which the middleware answers by throwing the original exception again.
	app.UseExceptionHandler(errors => errors.Run(ErrorResponse.WriteAsync));
	app.UseHsts();
}

app.UseSecurityHeaders();
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapDownloads();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

app.Lifetime.ApplicationStopping.Register(() =>
{
	// Settings writes are debounced; do not lose the last change on shutdown.
	ISettingsService settings = app.Services.GetRequiredService<ISettingsService>();
	settings.FlushAsync().Wait(TimeSpan.FromSeconds(3));
});

app.Run();
