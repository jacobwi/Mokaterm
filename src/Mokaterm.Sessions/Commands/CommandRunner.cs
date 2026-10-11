using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// Starts command runs for one UI scope and ends them with it. Each run's hosts connect through
/// <see cref="DetachedConnector"/>, which is the session manager's connect without the tab.
/// </summary>
internal sealed class CommandRunner : ICommandRunner, IAsyncDisposable, IDisposable
{
	private readonly IConnectionRepository _repository;
	private readonly Lock _lock = new();
	private readonly List<CommandRun> _runs = [];
	private bool _disposed;

	public CommandRunner(
		IConnectionRepository repository,
		ICredentialStore credentialStore,
		IProtocolRegistry registry,
		IHostIdentityVerifier hostVerifier,
		IKnownHostsStore knownHosts,
		ISettingsService settings,
		IUserInteraction interaction,
		ILogger<CommandRunner> logger,
		TimeProvider timeProvider)
	{
		_repository = repository;
		Registry = registry;
		Verifier = hostVerifier;
		KnownHosts = knownHosts;
		Settings = settings;
		Interaction = interaction;
		Logger = logger;
		Time = timeProvider;
		Connector = new DetachedConnector(repository, credentialStore, registry, logger);
	}

	internal DetachedConnector Connector { get; }

	internal IProtocolRegistry Registry { get; }

	internal IHostIdentityVerifier Verifier { get; }

	internal IKnownHostsStore KnownHosts { get; }

	internal ISettingsService Settings { get; }

	internal IUserInteraction Interaction { get; }

	internal ILogger Logger { get; }

	internal TimeProvider Time { get; }

	public async Task<ICommandRun> StartAsync(CommandRunRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (RemoteCommandRequest.Problem(request.Command) is { } problem)
		{
			throw new ArgumentException(problem, nameof(request));
		}

		Guid[] connectionIds = [.. request.ConnectionIds.Distinct()];
		if (connectionIds.Length == 0)
		{
			throw new ArgumentException("Pick at least one login to run the command on.", nameof(request));
		}

		ObjectDisposedException.ThrowIf(IsDisposed(), this);
		ConnectionCatalog catalog = await LoadCatalogAsync(cancellationToken);
		CommandRunTarget[] targets = [.. connectionIds.Select(id => CommandRunTarget.Resolve(catalog, Registry, id))];
		CommandRun run = new(this, RemoteCommandRequest.Normalize(request.Command), request.Options.Clamped(), targets);

		bool added = false;
		lock (_lock)
		{
			if (!_disposed)
			{
				_runs.Add(run);
				added = true;
			}
		}

		if (!added)
		{
			await run.DisposeAsync();
			throw new ObjectDisposedException(GetType().Name);
		}

		run.Start();
		return run;
	}

	public async ValueTask DisposeAsync()
	{
		if (TryMarkDisposed(out CommandRun[] runs))
		{
			await Task.WhenAll(runs.Select(run => run.DisposeAsync().AsTask()));
		}
	}

	public void Dispose()
	{
		if (TryMarkDisposed(out CommandRun[] runs))
		{
			// A synchronous scope disposal cannot wait for network closes, so they finish in the background.
			_ = Task.WhenAll(runs.Select(run => run.DisposeAsync().AsTask()));
		}
	}

	internal Task<ConnectionCatalog> LoadCatalogAsync(CancellationToken cancellationToken) => _repository.GetCatalogAsync(cancellationToken).AsTask();

	/// <summary>Called by a run once it was disposed.</summary>
	internal void Forget(CommandRun run)
	{
		lock (_lock)
		{
			_ = _runs.Remove(run);
		}
	}

	private bool IsDisposed()
	{
		lock (_lock)
		{
			return _disposed;
		}
	}

	private bool TryMarkDisposed(out CommandRun[] runs)
	{
		lock (_lock)
		{
			runs = [.. _runs];
			if (_disposed)
			{
				return false;
			}

			_disposed = true;
			return true;
		}
	}
}
