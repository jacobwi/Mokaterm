using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Tests.TestSupport;

/// <summary>
/// A service provider over a per-test temp directory with a fake clock, tiny Argon2 parameters, captured logs and a
/// fake <see cref="IUserInteraction"/>. Disposing it disposes the services and deletes the directory.
/// </summary>
internal sealed class CoreTestContext : IAsyncDisposable
{
	public const string MasterPassword = "correct horse battery staple";

	/// <summary>Where these contexts keep their data, so a test that needs the path before the context can build one.</summary>
	public const string TempFolderPrefix = "mokaterm-core-tests";

	/// <param name="configure">Extra registrations, applied before Core's own.</param>
	/// <param name="dataDirectory">
	/// Another context's directory, for a second provider standing in for a second app instance on the same data.
	/// </param>
	public CoreTestContext(Action<IServiceCollection>? configure = null, string? dataDirectory = null)
	{
		DataDirectory = dataDirectory ?? TempFolder.NewPath(TempFolderPrefix);
		Directory.CreateDirectory(DataDirectory);

		ServiceCollection services = new();
		services.AddSingleton<TimeProvider>(Time);
		services.AddSingleton(Logs);
		services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
		services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(DataDirectory));
		services.AddSingleton<IUserInteraction>(Interaction);
		services.AddSingleton(new VaultOptions { Argon2MemoryKiB = 64, Argon2Iterations = 1, Argon2Parallelism = 1 });
		configure?.Invoke(services);
		services.AddMokatermCore();
		Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
	}

	public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero));

	public LogSink Logs { get; } = new();

	public FakeUserInteraction Interaction { get; } = new();

	public string DataDirectory { get; }

	public string VaultDirectory => Path.Combine(DataDirectory, VaultHeaderStore.DocumentsDirectoryName);

	public string HeaderPath => Path.Combine(DataDirectory, VaultHeaderStore.HeaderFileName);

	public ServiceProvider Services { get; }

	public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

	/// <summary>Creates the vault in a new scope, leaving that scope unlocked.</summary>
	public async Task<AsyncServiceScope> CreateVaultScopeAsync()
	{
		AsyncServiceScope scope = CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync();
		await vault.CreateAsync(MasterPassword);
		return scope;
	}

	/// <summary>Opens another scope and unlocks the existing vault in it.</summary>
	public async Task<AsyncServiceScope> UnlockedScopeAsync()
	{
		AsyncServiceScope scope = CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync();
		UnlockResult result = await vault.UnlockAsync(MasterPassword);
		Assert.True(result.Succeeded, $"Unlock failed: {result.Status}");
		return scope;
	}

	public async ValueTask DisposeAsync()
	{
		await Services.DisposeAsync();
		await TempFolder.DeleteAsync(DataDirectory);
	}
}
