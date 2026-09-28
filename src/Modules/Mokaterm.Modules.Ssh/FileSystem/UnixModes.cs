using Mokaterm.Abstractions.FileSystem;
using Renci.SshNet.Sftp;

namespace Mokaterm.Modules.Ssh.FileSystem;

/// <summary>Converts between POSIX mode bits, SSH.NET attributes and <see cref="UnixFileMode"/>.</summary>
internal static class UnixModes
{
	private const int PermissionMask = 0xFFF;
	private const int TypeMask = 0xF000;
	private const int DirectoryType = 0x4000;
	private const int RegularFileType = 0x8000;
	private const int SymbolicLinkType = 0xA000;

	/// <summary>The permission and special bits of a raw <c>st_mode</c>. <see cref="UnixFileMode"/> uses the same bit values.</summary>
	public static UnixFileMode FromBits(int mode) => (UnixFileMode)(mode & PermissionMask);

	/// <summary>The entry kind encoded in the type bits of a raw <c>st_mode</c>.</summary>
	public static RemoteEntryKind KindFromBits(int mode) => (mode & TypeMask) switch
	{
		DirectoryType => RemoteEntryKind.Directory,
		RegularFileType => RemoteEntryKind.File,
		SymbolicLinkType => RemoteEntryKind.SymbolicLink,
		_ => RemoteEntryKind.Other,
	};

	public static RemoteEntryKind KindOf(SftpFileAttributes attributes) =>
		attributes.IsSymbolicLink ? RemoteEntryKind.SymbolicLink
		: attributes.IsDirectory ? RemoteEntryKind.Directory
		: attributes.IsRegularFile ? RemoteEntryKind.File
		: RemoteEntryKind.Other;

	public static UnixFileMode FromAttributes(SftpFileAttributes attributes)
	{
		UnixFileMode mode = UnixFileMode.None;
		Add(ref mode, attributes.OwnerCanRead, UnixFileMode.UserRead);
		Add(ref mode, attributes.OwnerCanWrite, UnixFileMode.UserWrite);
		Add(ref mode, attributes.OwnerCanExecute, UnixFileMode.UserExecute);
		Add(ref mode, attributes.GroupCanRead, UnixFileMode.GroupRead);
		Add(ref mode, attributes.GroupCanWrite, UnixFileMode.GroupWrite);
		Add(ref mode, attributes.GroupCanExecute, UnixFileMode.GroupExecute);
		Add(ref mode, attributes.OthersCanRead, UnixFileMode.OtherRead);
		Add(ref mode, attributes.OthersCanWrite, UnixFileMode.OtherWrite);
		Add(ref mode, attributes.OthersCanExecute, UnixFileMode.OtherExecute);
		Add(ref mode, attributes.IsUIDBitSet, UnixFileMode.SetUser);
		Add(ref mode, attributes.IsGroupIDBitSet, UnixFileMode.SetGroup);
		Add(ref mode, attributes.IsStickyBitSet, UnixFileMode.StickyBit);
		return mode;
	}

	/// <summary>Sets every permission and special bit. SSH.NET only sends attributes that changed, so a later setstat carries just the mode.</summary>
	public static void ApplyTo(SftpFileAttributes attributes, UnixFileMode mode)
	{
		attributes.OwnerCanRead = mode.HasFlag(UnixFileMode.UserRead);
		attributes.OwnerCanWrite = mode.HasFlag(UnixFileMode.UserWrite);
		attributes.OwnerCanExecute = mode.HasFlag(UnixFileMode.UserExecute);
		attributes.GroupCanRead = mode.HasFlag(UnixFileMode.GroupRead);
		attributes.GroupCanWrite = mode.HasFlag(UnixFileMode.GroupWrite);
		attributes.GroupCanExecute = mode.HasFlag(UnixFileMode.GroupExecute);
		attributes.OthersCanRead = mode.HasFlag(UnixFileMode.OtherRead);
		attributes.OthersCanWrite = mode.HasFlag(UnixFileMode.OtherWrite);
		attributes.OthersCanExecute = mode.HasFlag(UnixFileMode.OtherExecute);
		attributes.IsUIDBitSet = mode.HasFlag(UnixFileMode.SetUser);
		attributes.IsGroupIDBitSet = mode.HasFlag(UnixFileMode.SetGroup);
		attributes.IsStickyBitSet = mode.HasFlag(UnixFileMode.StickyBit);
	}

	private static void Add(ref UnixFileMode mode, bool isSet, UnixFileMode flag)
	{
		if (isSet)
		{
			mode |= flag;
		}
	}
}
