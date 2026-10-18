using System.Globalization;
using System.Runtime.CompilerServices;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// Searches a tree with one <c>find</c> on the server (<see cref="RemoteScripts.Search"/>) instead of a listing per
/// folder, for every file system of this module that can run a script: the login's SFTP view on the session's SSH
/// connection, the root view through sudo, and a container's files through docker exec. What it returns is what
/// <see cref="RemoteTreeWalker"/> would return for the query, apart from the order: find walks depth first, so when a cap
/// cuts the search short the entries that made it are the ones find met first rather than the shallowest. When the server
/// cannot run the script the walk answers instead, and the caller sees the same search either way.
/// </summary>
/// <remarks>
/// The script runs to its end under the module's usual runners, its output capped by them, and stops find itself once
/// the query's caps are passed (see <see cref="RemoteScripts.Search"/>). Cancelling cancels the runner, which closes the
/// channel: SSH.NET first asks the server to send TERM to the session's process, the server then closes the channel's
/// pipes, and the next write of tee or find there ends them. A find that writes nothing for a long time (a hung mount) is
/// left to <see cref="ServerSearchBudget.TimeLimit"/>.
/// </remarks>
internal static class ServerSearch
{
	/// <summary>
	/// The arguments of <see cref="RemoteScripts.Search"/> for <paramref name="root"/>, an absolute folder, and
	/// <paramref name="query"/>.
	/// </summary>
	public static string[] Arguments(string root, RemoteSearchQuery query, ServerSearchBudget? budget = null)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(query);
		ServerSearchBudget spend = budget ?? ServerSearchBudget.Default;
		SearchNamePatterns names = SearchNamePatterns.For(query);
		string kinds = query.Kinds switch
		{
			RemoteSearchKinds.Files => "f",
			RemoteSearchKinds.Folders => "d",
			_ => "a",
		};

		return
		[
			root,
			Number(query.MaxDepth),
			query.IncludeHidden ? "1" : "",
			kinds,
			Number(query.MaxFolders),
			Number(spend.MaxRecordBytes),
			Number((int)Math.Ceiling(spend.TimeLimit.TotalSeconds)),
			names.Single,
			Number(ResultStop(query, names.SingleExact)),
			Number(ResultStop(query, names.AlternativesExact)),
			.. names.Alternatives,
		];
	}

	/// <summary>
	/// How many of find's matches the script lets through before it stops find: the query's cap when find's match is
	/// exactly the query's, and 0 (never) when find matches more, since only the check here can tell how many count.
	/// </summary>
	public static int ResultStop(RemoteSearchQuery query, bool patternExact)
	{
		ArgumentNullException.ThrowIfNull(query);
		return patternExact && query.Kinds != RemoteSearchKinds.None ? query.MaxResults : 0;
	}

	/// <summary>
	/// Runs the search through <paramref name="run"/> and yields what it found, or walks the tree with
	/// <see cref="RemoteTreeWalker"/> when there is no <paramref name="run"/>, when it returns null (this server cannot
	/// search) or when its output cannot be read, which also calls <paramref name="unusable"/>. Progress reports as the
	/// walk does: the folder at the start, each folder entered, and how it ended.
	/// </summary>
	/// <param name="root">The folder, already absolute and checked by the file system.</param>
	/// <param name="run">Runs <see cref="RemoteScripts.Search"/> with the arguments given and returns what it printed after its marker.</param>
	/// <param name="wholeSeconds">Cut times to whole seconds, as the file system's own listings report them.</param>
	/// <exception cref="RemoteFileSystemException">
	/// The folder cannot be searched, the connection failed, or find ran out of time, after the entries it found until then.
	/// </exception>
	public static async IAsyncEnumerable<RemoteFileEntry> SearchAsync(
		IRemoteFileSystem fileSystem,
		string root,
		RemoteSearchQuery query,
		IProgress<RemoteSearchProgress>? progress,
		Func<IReadOnlyList<string>, CancellationToken, Task<ReadOnlyMemory<byte>?>>? run,
		Action unusable,
		bool wholeSeconds,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(unusable);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.MaxResults);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.MaxFolders);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.MaxDepth);
		cancellationToken.ThrowIfCancellationRequested();

		ServerSearchBudget budget = ServerSearchBudget.Default;
		ServerSearchResult? result = null;
		if (run is not null)
		{
			progress?.Report(new RemoteSearchProgress { FoldersVisited = 1, CurrentFolder = root });
			if (await run(Arguments(root, query, budget), cancellationToken) is { } payload)
			{
				result = ServerSearchOutput.Read(payload.Span, root, query, wholeSeconds, budget);
				if (result is null)
				{
					unusable();
				}
			}
		}

		if (result is null or { NeedsWalk: true })
		{
			await foreach (RemoteFileEntry entry in RemoteTreeWalker.SearchAsync(fileSystem, root, query, progress, cancellationToken))
			{
				yield return entry;
			}

			yield break;
		}

		int visited = 1;
		int found = 0;
		foreach (ServerSearchStep step in result.Steps)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (step.Entry is { } entry)
			{
				found++;
				yield return entry;
			}
			else
			{
				progress?.Report(new RemoteSearchProgress { FoldersVisited = ++visited, CurrentFolder = step.Folder, Found = found });
			}
		}

		progress?.Report(new RemoteSearchProgress
		{
			FoldersVisited = result.FoldersVisited,
			UnreadableFolders = result.UnreadableFolders,
			Found = found,
			Limit = result.Limit,
		});

		if (result.TimedOut)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, TimedOutMessage(budget), root);
		}
	}

	/// <summary>What the search says when the server stopped find at <see cref="ServerSearchBudget.TimeLimit"/>.</summary>
	internal static string TimedOutMessage(ServerSearchBudget budget)
	{
		ArgumentNullException.ThrowIfNull(budget);
		return string.Create(CultureInfo.CurrentCulture, $"The search ran for {budget.TimeLimit.TotalMinutes:0} minutes on the server, so the server stopped it.");
	}

	private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
