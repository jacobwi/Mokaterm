using System.Runtime.CompilerServices;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;
using Renci.SshNet.Sftp;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class UnixModesTests
{
	[Theory]
	[InlineData(0x81A4, "644", RemoteEntryKind.File)]
	[InlineData(0x41ED, "755", RemoteEntryKind.Directory)]
	[InlineData(0xA1FF, "777", RemoteEntryKind.SymbolicLink)]
	[InlineData(0x43FF, "1777", RemoteEntryKind.Directory)]
	[InlineData(0x89ED, "4755", RemoteEntryKind.File)]
	[InlineData(0x21B6, "666", RemoteEntryKind.Other)]
	[InlineData(0x61B0, "660", RemoteEntryKind.Other)]
	public void RawModes_SplitIntoKindAndPermissions(int mode, string octal, RemoteEntryKind kind)
	{
		Assert.Equal(octal, UnixFileModeFormat.ToOctal(UnixModes.FromBits(mode)));
		Assert.Equal(kind, UnixModes.KindFromBits(mode));
	}

	[Theory]
	[InlineData("755")]
	[InlineData("644")]
	[InlineData("600")]
	[InlineData("4711")]
	[InlineData("2775")]
	[InlineData("1777")]
	[InlineData("7777")]
	[InlineData("000")]
	public void Attributes_RoundTripEveryBit(string octal)
	{
		Assert.True(UnixFileModeFormat.TryParseOctal(octal, out UnixFileMode mode));
		SftpFileAttributes attributes = CreateAttributes();

		UnixModes.ApplyTo(attributes, mode);

		Assert.Equal(mode, UnixModes.FromAttributes(attributes));
	}

	[Fact]
	public void Attributes_ReadIndividualBits()
	{
		SftpFileAttributes attributes = CreateAttributes();
		attributes.OwnerCanRead = true;
		attributes.OwnerCanWrite = true;
		attributes.GroupCanRead = true;
		attributes.OthersCanExecute = true;
		attributes.IsGroupIDBitSet = true;

		Assert.Equal(
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherExecute | UnixFileMode.SetGroup,
			UnixModes.FromAttributes(attributes));
	}

	// SSH.NET builds attributes from server replies only; its public setters are all the mapping touches.
	private static SftpFileAttributes CreateAttributes() =>
		(SftpFileAttributes)RuntimeHelpers.GetUninitializedObject(typeof(SftpFileAttributes));
}
