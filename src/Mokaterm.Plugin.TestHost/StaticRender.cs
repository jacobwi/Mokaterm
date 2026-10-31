using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Plugin.TestHost;

/// <summary>
/// Renders a component to HTML with Blazor's static renderer and the services a plugin's views inject, faked. The
/// browser never runs, so after-render interop and event handlers stay out of reach: a test checks what a view shows,
/// not what it does to the page. A test that needs its own fakes, or a section of settings with values in it, passes
/// them through <paramref name="configure"/>; everything the fakes recorded is read back from the instances it kept.
/// </summary>
public static class StaticRender
{
	public static Task<string> RenderAsync<TComponent>(
		Dictionary<string, object?> parameters,
		HostKind host = HostKind.Desktop,
		Action<IServiceCollection>? configure = null)
		where TComponent : IComponent =>
		RenderAsync<TComponent>(services =>
		{
			services.AddSingleton<IAppEnvironment>(new TestEnvironment(host));
			configure?.Invoke(services);
		}, parameters);

	public static async Task<string> RenderAsync<TComponent>(Action<IServiceCollection> services, Dictionary<string, object?> parameters)
		where TComponent : IComponent
	{
		ArgumentNullException.ThrowIfNull(services);
		ServiceCollection collection = new();
		collection.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		collection.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		collection.AddScoped<IJSRuntime, IdleJsRuntime>();
		collection.AddSingleton<IClipboardService, TestClipboard>();
		collection.AddSingleton<TestInteraction>();
		collection.AddSingleton<IUserInteraction>(provider => provider.GetRequiredService<TestInteraction>());
		collection.AddSingleton<ILocalFileAccess, TestFiles>();
		collection.AddSingleton<TestSettings>();
		collection.AddSingleton<ISettingsService>(provider => provider.GetRequiredService<TestSettings>());
		collection.AddSingleton(TimeProvider.System);
		services(collection);
		await using ServiceProvider provider = collection.BuildServiceProvider();
		await using AsyncServiceScope scope = provider.CreateAsyncScope();
		await using HtmlRenderer renderer = new(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());

		using CancellationTokenSource limit = new(TimeSpan.FromSeconds(30));
		return await renderer.Dispatcher.InvokeAsync(async () =>
		{
			HtmlRootComponent component = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
			return component.ToHtmlString();
		}).WaitAsync(limit.Token);
	}
}


/// <summary>Like the runtime Blazor uses while prerendering, every call throws, which components read as "no JavaScript yet".</summary>
public sealed class IdleJsRuntime : IJSRuntime
{
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw NoJavaScript(identifier);

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw NoJavaScript(identifier);

	private static InvalidOperationException NoJavaScript(string identifier) =>
		new($"JavaScript interop calls cannot be issued during static rendering ({identifier}).");
}

public sealed class TestClipboard : IClipboardService
{
	/// <summary>What the last copy wrote, plain and secret alike.</summary>
	public string? LastCopied { get; private set; }

	public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
	{
		LastCopied = text;
		return ValueTask.CompletedTask;
	}

	public ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);

	public ValueTask WriteSecretAsync(string secret, CancellationToken cancellationToken = default)
	{
		LastCopied = secret;
		return ValueTask.CompletedTask;
	}
}

/// <summary>One notice a view raised, as the test reads it back.</summary>
public sealed record TestNotice(NoticeSeverity Severity, string Message, string? Title = null);

/// <summary>Answers prompts with nothing and keeps every notice, for a test that checks what a view told the user.</summary>
public sealed class TestInteraction : IUserInteraction
{
	private readonly List<TestNotice> _notices = [];

	public IReadOnlyList<TestNotice> TakeNotices()
	{
		TestNotice[] taken = [.. _notices];
		_notices.Clear();
		return taken;
	}

	public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) => NotSupported<HostTrustDecision>();

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) => NotSupported<CredentialPromptResult?>();

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) => NotSupported<SecretPromptResult?>();

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) => NotSupported<IReadOnlyList<string>?>();

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => NotSupported<ConfirmResult>();

	public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) => NotSupported<OverwriteDecision>();

	public void Notify(NoticeSeverity severity, string message, string? title = null) =>
		_notices.Add(new TestNotice(severity, message, title));

	private static Task<T> NotSupported<T>() => Task.FromException<T>(new NotSupportedException("A static render answers no prompt."));
}

/// <summary>Settings every section of start empty, which a test fills through <see cref="Set{T}"/>.</summary>
public sealed class TestSettings : ISettingsService
{
	private readonly Dictionary<Type, object> _sections = [];

	public void Set<T>(T section)
		where T : class, ISettingsSection, new() => _sections[typeof(T)] = section;

	public event Action<string>? Changed
	{
		add { }
		remove { }
	}

	public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public T Get<T>()
		where T : class, ISettingsSection, new() => _sections.TryGetValue(typeof(T), out object? section) ? (T)section : new();

	public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
		where T : class, ISettingsSection, new()
	{
		ArgumentNullException.ThrowIfNull(update);
		Set(update(Get<T>()));
		return Task.CompletedTask;
	}

	public Task ResetAsync<T>(CancellationToken cancellationToken = default)
		where T : class, ISettingsSection, new()
	{
		_sections.Remove(typeof(T));
		return Task.CompletedTask;
	}

	public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class TestFiles : ILocalFileAccess
{
	public bool CanPickFolders => false;

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([]);

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([]);

	public ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(false);
}

/// <summary>The host the render pretends to run on, with nothing else about the machine true.</summary>
public sealed record TestEnvironment(HostKind Kind) : IAppEnvironment
{
	public string PlatformName => "Test";

	public string AppVersion => "0.0.0";

	public string DataDirectory => Path.GetTempPath();

	public string TempDirectory => Path.GetTempPath();
}
