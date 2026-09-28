using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Vault;

namespace Mokaterm.UI.Tests;

public sealed class VaultUnlockScreenTests
{
	private const string ResetOffer = "Forgot your password?";

	// Anyone who reaches the web host sees its lock screen, and the vault refuses a reset from there.
	[Fact]
	public async Task Render_OnTheWebHost_OffersNoReset()
	{
		string html = await RenderAsync(HostKind.Web);

		Assert.DoesNotContain(ResetOffer, html, StringComparison.Ordinal);
		Assert.Contains("Master password", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_OnTheDesktop_OffersTheReset()
	{
		string html = await RenderAsync(HostKind.Desktop);

		Assert.Contains(ResetOffer, html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(HostKind kind) => StaticRender.RenderAsync<VaultUnlockScreen>(
		services =>
		{
			services.AddMokaRed();
			services.AddSingleton(TimeProvider.System);
			services.AddSingleton<IAppEnvironment>(new TestEnvironment(kind));
			services.AddSingleton<IVault, LockedVault>();
			services.AddSingleton<ISessionManager, NoSessions>();
			services.AddSingleton<ISettingsService, DefaultSettings>();
			services.AddScoped<VaultScreenState>();
			services.AddScoped<FormInterop>();
		},
		new Dictionary<string, object?>(StringComparer.Ordinal));

	private sealed class TestEnvironment(HostKind kind) : IAppEnvironment
	{
		public HostKind Kind => kind;

		public string PlatformName => kind.ToString();

		public string AppVersion => "0.0.0";

		public string DataDirectory => "";

		public string TempDirectory => "";
	}

	/// <summary>A vault that exists and is locked, without device unlock.</summary>
	private sealed class LockedVault : IVault
	{
		public event Action<VaultStatus>? StatusChanged
		{
			add { }
			remove { }
		}

		public VaultStatus Status => VaultStatus.Locked;

		public LockReason? LastLockReason => null;

		public bool IsDeviceUnlockAvailable => false;

		public bool IsDeviceUnlockEnabled => false;

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task CreateAsync(string masterPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<UnlockResult> UnlockAsync(string masterPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<UnlockResult> UnlockWithDeviceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SetDeviceUnlockAsync(bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<UnlockResult> ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public void Lock(LockReason reason = LockReason.User)
		{
		}

		public void ReportActivity()
		{
		}

		public Task ResetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class NoSessions : ISessionManager
	{
		public event Action? SessionsChanged
		{
			add { }
			remove { }
		}

		public IReadOnlyList<ISessionHandle> Sessions => [];

		public ISessionHandle? Find(Guid sessionId) => null;

		public Task<ISessionHandle> OpenAsync(Guid connectionId, SessionOpenOptions? options = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ISessionHandle OpenTransient(HostProfile host, ConnectionProfile connection, SessionOpenOptions? options = null) =>
			throw new NotSupportedException();

		public Task ReconnectAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task AdoptAsync(Guid sessionId, Guid connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DisconnectAsync(Guid sessionId) => throw new NotSupportedException();

		public Task CloseAsync(Guid sessionId) => throw new NotSupportedException();

		public Task CloseAllAsync() => Task.CompletedTask;

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class DefaultSettings : ISettingsService
	{
		public event Action<string>? Changed
		{
			add { }
			remove { }
		}

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public T Get<T>()
			where T : class, ISettingsSection, new() => new();

		public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task ResetAsync<T>(CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
