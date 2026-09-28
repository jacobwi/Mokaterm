using FluentFTP;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>Maps FluentFTP listing items to <see cref="RemoteFileEntry"/>.</summary>
internal static class FtpEntryMapper
{
	/// <summary>False for the <c>.</c> and <c>..</c> entries and for names no path can be built from.</summary>
	public static bool IsListable(FtpListItem item)
	{
		ArgumentNullException.ThrowIfNull(item);
		return item.SubType is not (FtpObjectSubType.SelfDirectory or FtpObjectSubType.ParentDirectory) && RemotePath.IsValidName(item.Name);
	}

	/// <param name="path">Absolute path of the entry. The name is taken from it, not from the item.</param>
	public static RemoteFileEntry ToEntry(FtpListItem item, string path)
	{
		ArgumentNullException.ThrowIfNull(item);
		string normalized = RemotePath.Normalize(path);
		RemoteEntryKind kind = ToKind(item.Type);
		bool isLink = kind == RemoteEntryKind.SymbolicLink;
		return new RemoteFileEntry
		{
			Name = RemotePath.IsRoot(normalized) ? RemotePath.Root : RemotePath.GetName(normalized),
			Path = normalized,
			Kind = kind,

			// Parsers report -1 when the listing had no size.
			Size = Math.Max(0, item.Size),
			LastModified = ToTimestamp(item.Modified),
			Permissions = ToPermissions(item),
			Owner = NullIfBlank(item.RawOwner),
			Group = NullIfBlank(item.RawGroup),
			LinkTarget = isLink ? NullIfBlank(item.LinkTarget) : null,
			LinkTargetKind = isLink && item.LinkObject is { } target ? ToKind(target.Type) : null,
		};
	}

	public static RemoteEntryKind ToKind(FtpObjectType type) => type switch
	{
		FtpObjectType.File => RemoteEntryKind.File,
		FtpObjectType.Directory => RemoteEntryKind.Directory,
		FtpObjectType.Link => RemoteEntryKind.SymbolicLink,
		_ => RemoteEntryKind.Other,
	};

	/// <summary>
	/// Machine listings (MLSD) carry UTC times. Classic LIST output has no time zone at all, so UTC is the least wrong
	/// reading. <see cref="DateTime.MinValue"/> means the listing had no time.
	/// </summary>
	public static DateTimeOffset? ToTimestamp(DateTime value)
	{
		if (value == DateTime.MinValue || value == DateTime.MaxValue)
		{
			return null;
		}

		return value.Kind == DateTimeKind.Local
			? new DateTimeOffset(value)
			: new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
	}

	/// <summary>
	/// Permission bits, or null when the listing had none (Windows servers). FluentFTP stores <see cref="FtpListItem.Chmod"/>
	/// as octal digits written in decimal (755) and keeps setuid, setgid and sticky separately.
	/// </summary>
	public static UnixFileMode? ToPermissions(FtpListItem item)
	{
		ArgumentNullException.ThrowIfNull(item);
		UnixFileMode special = ToSpecialBits(item.SpecialPermissions);
		if (item.Chmod > 0 && TryParseChmod(item.Chmod, out UnixFileMode mode))
		{
			return mode | special;
		}

		bool hasBits = item.OwnerPermissions != FtpPermission.None
			|| item.GroupPermissions != FtpPermission.None
			|| item.OthersPermissions != FtpPermission.None
			|| special != UnixFileMode.None;

		// "----------" is a real mode (000) that parses to no bits at all.
		if (!hasBits && !IsModeString(item.RawPermissions))
		{
			return null;
		}

		return ToBits(item.OwnerPermissions, UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute)
			| ToBits(item.GroupPermissions, UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute)
			| ToBits(item.OthersPermissions, UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute)
			| special;
	}

	/// <summary>Reads a FluentFTP chmod value such as 755 or 4755. False when a digit is not octal.</summary>
	public static bool TryParseChmod(int chmod, out UnixFileMode mode)
	{
		mode = UnixFileMode.None;
		if (chmod is < 0 or > 7777)
		{
			return false;
		}

		int value = 0;
		int scale = 1;
		for (int remaining = chmod; remaining > 0; remaining /= 10)
		{
			int digit = remaining % 10;
			if (digit > 7)
			{
				return false;
			}

			value += digit * scale;
			scale *= 8;
		}

		mode = (UnixFileMode)value;
		return true;
	}

	private static UnixFileMode ToSpecialBits(FtpSpecialPermissions special) =>
		(special.HasFlag(FtpSpecialPermissions.SetUserID) ? UnixFileMode.SetUser : UnixFileMode.None)
		| (special.HasFlag(FtpSpecialPermissions.SetGroupID) ? UnixFileMode.SetGroup : UnixFileMode.None)
		| (special.HasFlag(FtpSpecialPermissions.Sticky) ? UnixFileMode.StickyBit : UnixFileMode.None);

	private static UnixFileMode ToBits(FtpPermission permission, UnixFileMode read, UnixFileMode write, UnixFileMode execute) =>
		(permission.HasFlag(FtpPermission.Read) ? read : UnixFileMode.None)
		| (permission.HasFlag(FtpPermission.Write) ? write : UnixFileMode.None)
		| (permission.HasFlag(FtpPermission.Execute) ? execute : UnixFileMode.None);

	// An ls-style mode such as "drwxr-xr-x", possibly followed by an ACL marker like '+' or '@'.
	private static bool IsModeString(string? raw)
	{
		if (raw is null || raw.Length < 10)
		{
			return false;
		}

		foreach (char c in raw.AsSpan(1, 9))
		{
			if (c is not ('-' or 'r' or 'w' or 'x' or 's' or 'S' or 't' or 'T'))
			{
				return false;
			}
		}

		return true;
	}

	private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
