using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moka.Red.Feedback.CommandPalette;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

public sealed class ShellInputBridgeTests
{
	// Every wait is bounded: these tests run under a build lock other test runs queue behind, so a regression must fail
	// rather than hang.
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task Detach_WhileTheScriptIsStillAttaching_RemovesTheListenersOnceTheyExist()
	{
		// The vault locks while shell.js is still adding its window listeners: the shell unmounts before a handle exists.
		Harness harness = new();
		Task attach = harness.Bridge.AttachAsync();
		await Bounded(harness.Bridge.DetachAsync());

		harness.Js.Module.CompleteAttach(7);
		await Bounded(attach);

		Assert.Contains(harness.Js.Module.Calls, call => call.Identifier == "detachShell" && Equals(call.Args[0], 7));
		Assert.Equal(harness.SubscribersBeforeAttach, harness.Sessions.SubscriberCount);
	}

	[Fact]
	public async Task Detach_AfterAttaching_RemovesTheListenersAndTheSessionWatch()
	{
		Harness harness = new();
		Task attach = harness.Bridge.AttachAsync();
		harness.Js.Module.CompleteAttach(3);
		await Bounded(attach);
		Assert.Equal(harness.SubscribersBeforeAttach + 1, harness.Sessions.SubscriberCount);

		await Bounded(harness.Bridge.DetachAsync());

		Assert.Contains(harness.Js.Module.Calls, call => call.Identifier == "detachShell" && Equals(call.Args[0], 3));
		Assert.Equal(harness.SubscribersBeforeAttach, harness.Sessions.SubscriberCount);
	}

	[Fact]
	public async Task Attach_WhileThePreviousShellIsStillDetaching_AttachesItsOwnListeners()
	{
		// The old shell is still waiting for shell.js to remove its listeners when the next one mounts.
		Harness harness = new();
		Task first = harness.Bridge.AttachAsync();
		harness.Js.Module.CompleteAttach(3);
		await Bounded(first);

		harness.Js.Module.HoldNextDetach();
		Task detach = harness.Bridge.DetachAsync();
		Task second = harness.Bridge.AttachAsync();
		Assert.Equal(2, harness.Js.Module.Calls.Count(call => call.Identifier == "attachShell"));

		harness.Js.Module.CompleteAttach(4);
		await Bounded(second);
		harness.Js.Module.ReleaseDetach();
		await Bounded(detach);

		// The second shell owns the listeners now, so unmounting it removes them.
		await Bounded(harness.Bridge.DetachAsync());
		Assert.Contains(harness.Js.Module.Calls, call => call.Identifier == "detachShell" && Equals(call.Args[0], 4));
		Assert.Equal(harness.SubscribersBeforeAttach, harness.Sessions.SubscriberCount);
	}

	private static Task Bounded(Task task) => task.WaitAsync(Patience, TestContext.Current.CancellationToken);

	/// <summary>
	/// The bridge with everything it touches while attaching. The command registry is only asked to run shortcuts, which
	/// these tests never do, so the services behind its commands stay unset.
	/// </summary>
	private sealed class Harness
	{
		public Harness()
		{
			ShellState shell = new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
			SessionWorkspace workspace = new(Sessions);
			UserInteractionService interaction = new();
			SessionActions actions = new(Sessions, workspace, null!, null!, interaction, null!, shell, NullLogger<SessionActions>.Instance);
			ShellCommandRegistry commands = new(new MokaCommandPaletteService(), shell, workspace, actions, null!, null!, null!, null!, null!, null!, interaction, null!);
			Bridge = new ShellInputBridge(new ShellInterop(Js), commands, null!, new FixedSettings(), new WebEnvironment(), Sessions, NullLogger<ShellInputBridge>.Instance);
			SubscribersBeforeAttach = Sessions.SubscriberCount;
		}

		public ScriptedJsRuntime Js { get; } = new();

		public CountingSessionManager Sessions { get; } = new();

		public ShellInputBridge Bridge { get; }

		public int SubscribersBeforeAttach { get; }
	}

	/// <summary>The web host, where the bridge also watches sessions for the leave-page prompt.</summary>
	private sealed class WebEnvironment : IAppEnvironment
	{
		public HostKind Kind => HostKind.Web;

		public string PlatformName => "Web";

		public string AppVersion => "0.0.0";

		public string DataDirectory => "";

		public string TempDirectory => "";
	}

	/// <summary>No sessions; counts who listens for them.</summary>
	private sealed class CountingSessionManager : ISessionManager
	{
		private Action? _sessionsChanged;

		public event Action? SessionsChanged
		{
			add => _sessionsChanged += value;
			remove => _sessionsChanged -= value;
		}

		public int SubscriberCount => _sessionsChanged?.GetInvocationList().Length ?? 0;

		public IReadOnlyList<ISessionHandle> Sessions => [];

		public ISessionHandle? Find(Guid sessionId) => null;

		public Task<ISessionHandle> OpenAsync(Guid connectionId, SessionOpenOptions? options = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ISessionHandle OpenTransient(HostProfile host, ConnectionProfile connection, SessionOpenOptions? options = null) =>
			throw new NotSupportedException();

		public Task ReconnectAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task AdoptAsync(Guid sessionId, Guid connectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task DisconnectAsync(Guid sessionId) => Task.CompletedTask;

		public Task CloseAsync(Guid sessionId) => Task.CompletedTask;

		public Task CloseAllAsync() => Task.CompletedTask;

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	/// <summary>A JS runtime whose only module is shell.js.</summary>
	private sealed class ScriptedJsRuntime : IJSRuntime
	{
		public ScriptedModule Module { get; } = new();

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			InvokeAsync<TValue>(identifier, CancellationToken.None, args);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
			identifier == "import" ? ValueTask.FromResult((TValue)(object)Module) : throw new NotSupportedException(identifier);
	}

	private sealed record JsCall(string Identifier, object?[] Args);

	/// <summary>shell.js as the tests script it: attachShell answers when told to, and one detachShell can be held.</summary>
	private sealed class ScriptedModule : IJSObjectReference
	{
		private readonly Lock _gate = new();
		private readonly Queue<TaskCompletionSource<int>> _attaches = new();
		private readonly List<JsCall> _calls = [];
		private readonly TaskCompletionSource _detachReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private bool _holdNextDetach;

		public IReadOnlyList<JsCall> Calls
		{
			get
			{
				lock (_gate)
				{
					return [.. _calls];
				}
			}
		}

		/// <summary>Answers the oldest attachShell call still waiting.</summary>
		public void CompleteAttach(int handle)
		{
			TaskCompletionSource<int> attach;
			lock (_gate)
			{
				attach = _attaches.Dequeue();
			}

			attach.SetResult(handle);
		}

		/// <summary>Makes the next detachShell wait until <see cref="ReleaseDetach"/>.</summary>
		public void HoldNextDetach()
		{
			lock (_gate)
			{
				_holdNextDetach = true;
			}
		}

		public void ReleaseDetach() => _detachReleased.TrySetResult();

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			InvokeAsync<TValue>(identifier, CancellationToken.None, args);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		{
			lock (_gate)
			{
				_calls.Add(new JsCall(identifier, args ?? []));
				if (identifier == "attachShell")
				{
					TaskCompletionSource<int> attach = new(TaskCreationOptions.RunContinuationsAsynchronously);
					_attaches.Enqueue(attach);
					return new ValueTask<TValue>((Task<TValue>)(object)attach.Task);
				}

				if (identifier == "detachShell" && _holdNextDetach)
				{
					_holdNextDetach = false;
					return new ValueTask<TValue>(AfterReleaseAsync<TValue>());
				}
			}

			return ValueTask.FromResult(default(TValue)!);
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		private async Task<TValue> AfterReleaseAsync<TValue>()
		{
			await _detachReleased.Task;
			return default!;
		}
	}
}
