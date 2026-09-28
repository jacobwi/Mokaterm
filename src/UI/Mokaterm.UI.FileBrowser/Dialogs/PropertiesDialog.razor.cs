using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.Core.Enums;
using Moka.Red.Core.Icons;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Everything about one or more entries, with owner, group and permissions editable as the logged-in user or as root.
/// Closes with a <c>PropertiesChange</c> holding only what changed, and leaves running it to the caller. The server is
/// only read through the delegates passed in, because a dialog must not open other dialogs.
/// </summary>
public sealed partial class PropertiesDialog : ComponentBase, IDisposable
{
	private const string LabelWidth = "84px";
	private const string MutedStyle = "color:var(--moka-color-on-surface-variant)";
	private const string MicroLabelStyle = "letter-spacing:0.08em;color:var(--moka-color-on-surface-tertiary)";
	private const string UserValue = "user";
	private const string RootValue = "root";
	private const string AllTargets = "all";
	private const string FolderTargets = "folders";
	private const string FileTargets = "files";

	// Progress arrives after every folder listed; rendering each one would flood a slow connection with render batches.
	private const long ProgressRenderMilliseconds = 150;

	private static readonly PermissionRow[] PermissionRows =
	[
		new("Owner", UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute),
		new("Group", UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute),
		new("Others", UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute),
	];

	private readonly CancellationTokenSource _lifetime = new();
	private PropertiesForm _form = default!;
	private RemoteAccounts _accounts = RemoteAccounts.Empty;
	private CancellationTokenSource? _measurement;
	private SizeState _sizeState;
	private TreeSize? _size;
	private string? _sizeError;
	private long _lastProgressRender;
	private bool _disposed;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	/// <summary>The selection, at least one entry.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<RemoteFileEntry> Entries { get; set; } = [];

	/// <summary>The section whose first field gets the focus.</summary>
	[Parameter]
	public PropertiesSection Section { get; set; }

	/// <summary>The logged-in account.</summary>
	[Parameter]
	public string UserName { get; set; } = "";

	/// <summary>Preselects root, which follows the browser's current mode.</summary>
	[Parameter]
	public bool AsRoot { get; set; }

	[Parameter]
	public bool SupportsElevation { get; set; }

	[Parameter]
	public RemoteFileSystemFeatures Features { get; set; }

	/// <summary>Reads the server's users and groups for the owner and group lists. Null leaves plain text fields.</summary>
	[Parameter]
	public Func<CancellationToken, Task<RemoteAccounts>>? LoadAccounts { get; set; }

	/// <summary>Walks the selection and reports the totals as they grow. Null hides the size of folders.</summary>
	[Parameter]
	public Func<Action<TreeSize>, CancellationToken, Task<TreeSize>>? MeasureSize { get; set; }

	private string HeaderName => _form.Single?.Name ?? EntryVisuals.Count(_form.Entries.Count, "item", "items");

	private string HeaderPath => _form.Single?.Path ?? _form.Location;

	private MokaIconDefinition HeaderIcon => _form.Single is { } entry ? EntryVisuals.IconFor(entry)
		: _form.CanMeasure ? MokatermIcons.Folder
		: MokatermIcons.File;

	private MokaColor? HeaderColor => _form.Single is { } entry ? EntryVisuals.ColorFor(entry) : null;

	private string TypeText => _form.Single is { } entry ? EntryVisuals.KindLabel(entry) : EntryVisuals.KindSummary(_form.Entries);

	private bool HasSelectedFiles => _form.Entries.Any(entry => entry.Kind == RemoteEntryKind.File);

	private bool ApplyAsRoot => _form.RunsAsRoot && !_form.IsRootUser;

	private string RunAsValue => _form.AsRoot ? RootValue : UserValue;

	private string TargetsValue => _form.Targets switch
	{
		PermissionTargets.Folders => FolderTargets,
		PermissionTargets.Files => FileTargets,
		_ => AllTargets,
	};

	private string SizeText
	{
		get
		{
			if (_size is not { } size)
			{
				return "";
			}

			bool lowerBound = _sizeState == SizeState.Stopped || size.Truncated || size.UnreadableFolders > 0;
			return (lowerBound ? "at least " : "") + EntryVisuals.DetailedSize(size.Bytes);
		}
	}

	// Selected files count toward a selection's total, so "contains" would only be right for a single folder.
	private string ContainsLabel => _form.Single is null ? "Total" : "Contains";

	private string ContainsText => _size is { } size && EntryVisuals.CountList(size.Files, size.Folders, size.Links) is { Length: > 0 } counts
		? counts
		: _sizeState == SizeState.Running ? "Counting..." : "Nothing";

	private string? SizeNote
	{
		get
		{
			if (_size is not { } size || _sizeState == SizeState.Running)
			{
				return null;
			}

			List<string> notes = [];
			if (size.Truncated)
			{
				notes.Add(string.Create(CultureInfo.CurrentCulture, $"Stopped after {size.Items:N0} items."));
			}
			else if (_sizeState == SizeState.Stopped)
			{
				notes.Add("Stopped before the end.");
			}

			if (size.UnreadableFolders > 0)
			{
				notes.Add(EntryVisuals.Count(size.UnreadableFolders, "folder", "folders") + " could not be read.");
			}

			return notes.Count == 0 ? null : string.Join(' ', notes);
		}
	}

	private string LinksNote => _form.Editable.Count == 0
		? "Links keep their owner and permissions here: changing them would change what the link points at."
		: EntryVisuals.Count(_form.SkippedLinks, "link", "links") + (_form.SkippedLinks == 1 ? " is" : " are")
			+ " left out: changing a link's owner or permissions would change what it points at.";

	private string OwnershipInsideText => (_form.Owner.ToApply(includeUnchanged: true), _form.Group.ToApply(includeUnchanged: true)) switch
	{
		({ } owner, { } group) => $"Everything inside gets owner {owner} and group {group}.",
		({ } owner, null) => $"Everything inside gets owner {owner}; groups stay.",
		(null, { } group) => $"Everything inside gets group {group}; owners stay.",
		_ => "Enter an owner or a group to give everything inside.",
	};

	private string PermissionsInsideText
	{
		get
		{
			string reached = !_form.PermissionsRecursive ? "Everything inside"
				: _form.Targets switch
				{
					PermissionTargets.Folders => "Folders inside",
					PermissionTargets.Files => "Files inside",
					_ => "Everything inside",
				};

			return _form.Mode.IsMixed
				? reached + " gets the boxes you set; dashes keep each item's own setting."
				: reached + " gets " + _form.Mode.Symbolic + ". Links are left alone.";
		}
	}

	private string OwnerNeedsRootText => _form.CanElevate
		? "Only root can give files to another user. Choose root above."
		: "Only root can give files to another user, and this connection cannot run as root.";

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	protected override void OnInitialized() =>
		_form = new PropertiesForm(Entries, Features, UserName, AsRoot, SupportsElevation);

	protected override async Task OnInitializedAsync()
	{
		if (LoadAccounts is null || !_form.CanEditOwnership)
		{
			return;
		}

		try
		{
			_accounts = await LoadAccounts(_lifetime.Token);
		}
		catch (Exception)
		{
			// The lists only help; names can still be typed.
			_accounts = RemoteAccounts.Empty;
		}
	}

	private static string EmptyHint(AccountField field) => field.IsMixed ? "mixed" : "unchanged";

	/// <summary>The id behind a typed name, or a hint that the server does not know it.</summary>
	private static string? AccountHelp(AccountField field, IReadOnlyList<RemoteAccount> accounts, string idLabel, string noun)
	{
		if (field.IsEmpty)
		{
			return field.IsMixed ? "The items differ; empty keeps each one." : null;
		}

		if (!field.IsValid || accounts.Count == 0)
		{
			return null;
		}

		if (AccountSearch.Resolve(accounts, field.Trimmed) is { } account)
		{
			return string.Equals(account.Name, field.Trimmed, StringComparison.Ordinal)
				? string.Create(CultureInfo.InvariantCulture, $"{idLabel} {account.Id}")
				: string.Create(CultureInfo.InvariantCulture, $"{account.Name}, {idLabel} {account.Id}");
		}

		return AccountSearch.TryParseId(field.Trimmed, out _)
			? $"No {noun} on the server has this {idLabel}."
			: $"The server lists no {noun} by this name.";
	}

	private async Task CalculateSizeAsync()
	{
		if (MeasureSize is null || _sizeState == SizeState.Running || _disposed)
		{
			return;
		}

		using CancellationTokenSource measurement = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		_measurement = measurement;
		_sizeState = SizeState.Running;
		_size = new TreeSize();
		_sizeError = null;
		try
		{
			_size = await MeasureSize(OnSizeProgress, measurement.Token);
			_sizeState = SizeState.Done;
		}
		catch (OperationCanceledException) when (measurement.IsCancellationRequested)
		{
			// Stopped by the user, or the dialog closed: the counts so far stay on screen as a lower bound.
			_sizeState = SizeState.Stopped;
		}
		catch (Exception ex)
		{
			_sizeState = SizeState.Failed;
			_sizeError = RemoteErrorText.Describe(ex);
		}
		finally
		{
			_measurement = null;
		}
	}

	private void OnSizeProgress(TreeSize size)
	{
		if (_disposed || _sizeState != SizeState.Running)
		{
			return;
		}

		_size = size;
		long now = Environment.TickCount64;
		if (now - _lastProgressRender >= ProgressRenderMilliseconds)
		{
			_lastProgressRender = now;
			_ = InvokeAsync(StateHasChanged);
		}
	}

	private void StopSize() => _measurement?.Cancel();

	private void OnTargetsChanged(string value) => _form.Targets = value switch
	{
		FolderTargets => PermissionTargets.Folders,
		FileTargets => PermissionTargets.Files,
		_ => PermissionTargets.All,
	};

	private void OnRunAsChanged(string value) => _form.AsRoot = _form.CanElevate && value == RootValue;

	private void OnKeyDown(KeyboardEventArgs args)
	{
		// Plain Enter is left to the fields: a recursive chown as root is too much to set off by picking a name.
		if (args.Key == "Enter" && (args.CtrlKey || args.MetaKey))
		{
			Apply();
		}
	}

	private void Apply()
	{
		if (_form.BuildChange() is { } change)
		{
			Dialog?.Close(change);
		}
	}

	private void Cancel() => Dialog?.Cancel();

	private enum SizeState
	{
		Idle,
		Running,
		Done,
		Stopped,
		Failed,
	}

	private sealed record PermissionRow(string Label, UnixFileMode Read, UnixFileMode Write, UnixFileMode Execute);
}
