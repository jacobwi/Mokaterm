namespace Mokaterm.Abstractions.FileSystem;

/// <summary>The kinds of entries a <see cref="PermissionChange"/> reaches.</summary>
public enum PermissionTargets
{
	/// <summary>Everything. A link given directly changes what it points at, as chmod does.</summary>
	All,

	Folders,

	/// <summary>Everything that is neither a folder nor a link: files, devices, sockets and pipes.</summary>
	Files,
}

/// <summary>
/// A permission change applied the way chmod applies one: the bits in <see cref="Mask"/> are set or cleared to match
/// <see cref="Mode"/>, and every other bit keeps each entry's current value.
/// </summary>
public sealed record PermissionChange
{
	/// <summary>The nine permission bits plus the setuid, setgid and sticky bits.</summary>
	public const UnixFileMode AllBits = (UnixFileMode)0xFFF;

	private const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

	/// <summary>The values for the bits in <see cref="Mask"/>.</summary>
	public required UnixFileMode Mode { get; init; }

	/// <summary>The bits this change sets or clears. All of them by default, which makes <see cref="Mode"/> the whole mode.</summary>
	public UnixFileMode Mask { get; init; } = AllBits;

	/// <summary>Also changes everything inside a folder. Links inside are never followed or changed.</summary>
	public bool Recursive { get; init; }

	/// <summary>The kinds of entries that change, the given entry included.</summary>
	public PermissionTargets Targets { get; init; } = PermissionTargets.All;

	/// <summary>
	/// Like chmod's <c>X</c>: execute bits are only set on folders and on files that already have one. Clearing execute
	/// bits works as usual.
	/// </summary>
	public bool ConditionalExecute { get; init; }

	/// <summary>False when the result is <see cref="Mode"/> whatever an entry's current mode is.</summary>
	public bool NeedsCurrentMode => (Mask & AllBits) != AllBits || ConditionalExecute;

	/// <summary>True when the change reaches an entry of <paramref name="kind"/>.</summary>
	public bool Includes(RemoteEntryKind kind) => Targets switch
	{
		PermissionTargets.Folders => kind == RemoteEntryKind.Directory,
		PermissionTargets.Files => kind is RemoteEntryKind.File or RemoteEntryKind.Other,
		_ => true,
	};

	/// <summary>The mode an entry currently at <paramref name="current"/> ends up with.</summary>
	public UnixFileMode Apply(UnixFileMode current, bool isDirectory)
	{
		UnixFileMode mask = Mask & AllBits;
		UnixFileMode value = Mode & mask;
		if (ConditionalExecute && !isDirectory && (current & AnyExecute) == UnixFileMode.None)
		{
			value &= ~AnyExecute;
		}

		return (current & AllBits & ~mask) | value;
	}
}
