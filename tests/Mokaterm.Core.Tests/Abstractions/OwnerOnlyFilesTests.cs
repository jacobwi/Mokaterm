using Mokaterm.Abstractions.Storage;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// The file modes the vault, the session logs and the web host's data directory are created with. All three carried
/// their own copy of these constants, in three assemblies, which is one missed edit away from a group-readable vault.
/// </summary>
public sealed class OwnerOnlyFilesTests
{
	private const UnixFileMode EveryoneElse =
		UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
		| UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

	[Fact]
	public void TheModes_GiveNothingToGroupOrOther()
	{
		Assert.Equal(UnixFileMode.None, OwnerOnlyFiles.ForFile & EveryoneElse);
		Assert.Equal(UnixFileMode.None, OwnerOnlyFiles.ForDirectory & EveryoneElse);
	}

	[Fact]
	public void TheModes_Are0600And0700()
	{
		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, OwnerOnlyFiles.ForFile);

		// A directory needs the execute bit to be entered at all, which is the only difference.
		Assert.Equal(OwnerOnlyFiles.ForFile | UnixFileMode.UserExecute, OwnerOnlyFiles.ForDirectory);
	}

	[Fact]
	public void Owned_SetsTheCreateModeOnlyWhereThePlatformHasOne()
	{
		FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write };

		Assert.Same(options, OwnerOnlyFiles.Owned(options));
		Assert.Equal(OperatingSystem.IsWindows() ? null : OwnerOnlyFiles.ForFile, options.UnixCreateMode);
	}

	[Fact]
	public void CreateDirectory_MakesTheWholePath_AndLeavesAnExistingOneAlone()
	{
		string root = Path.Combine(Path.GetTempPath(), $"mokaterm-owner-{Guid.NewGuid():N}");
		string nested = Path.Combine(root, "logs", "sessions");
		try
		{
			OwnerOnlyFiles.CreateDirectory(nested);
			Assert.True(Directory.Exists(nested));

			OwnerOnlyFiles.CreateDirectory(nested);
			Assert.True(Directory.Exists(nested));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
