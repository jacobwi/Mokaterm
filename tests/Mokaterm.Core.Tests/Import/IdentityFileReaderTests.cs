using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Import;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Tests.Keys;

namespace Mokaterm.Core.Tests.Import;

/// <summary>Finding and reading a login's key file, desktop only, against a home folder of the test's own.</summary>
public sealed class IdentityFileReaderTests : IAsyncDisposable
{
	private readonly TempFolder _home = new("mokaterm-key-home");

	[Fact]
	public void OnTheWebHost_NothingIsLocatedOrRead()
	{
		string path = WriteKey("id_ed25519", ReferenceKeys.Ed25519);
		IdentityFileReader reader = Reader(HostKind.Web);

		Assert.False(reader.IsAvailable);
		_ = Assert.Throws<InvalidOperationException>(() => reader.Locate(Entry("~/.ssh/id_ed25519")));
		_ = Assert.Throws<InvalidOperationException>(() => reader.Read(path));
	}

	[Fact]
	public void Read_OnTheDesktop_GivesTheKey()
	{
		IdentityFileReader reader = Reader(HostKind.Desktop);
		string path = WriteKey("id_ed25519", ReferenceKeys.Ed25519);

		IdentityPathResult location = reader.Locate(Entry("~/.ssh/id_ed25519"));
		using KeyFileMaterial? material = reader.Read(location.FullPath!);

		Assert.Equal(path, location.FullPath);
		Assert.Equal(ImportedKeyStatus.Ready, material!.Status);
		Assert.Equal(ReferenceKeys.Ed25519Fingerprint, material.Fingerprint);
	}

	[Fact]
	public void Read_GivesNullForAMissingFile_AndAReasonForAFolderOrAHugeFile()
	{
		IdentityFileReader reader = Reader(HostKind.Desktop);
		string folder = Path.Combine(_home.Path, ".ssh", "keys");
		Directory.CreateDirectory(folder);
		string huge = WriteKey("huge", new string('x', KeyFileImport.MaxFileBytes + 1));

		Assert.Null(reader.Read(Path.Combine(_home.Path, ".ssh", "nothing-here")));
		using KeyFileMaterial? fromFolder = reader.Read(folder);
		using KeyFileMaterial? fromHuge = reader.Read(huge);
		Assert.Equal(ImportedKeyStatus.Unreadable, fromFolder!.Status);
		Assert.Equal(ImportedKeyStatus.Unreadable, fromHuge!.Status);
		Assert.Contains("too large", fromHuge.Reason, StringComparison.Ordinal);
	}

	public ValueTask DisposeAsync() => _home.DisposeAsync();

	private static ImportedEntry Entry(string path) => new()
	{
		Key = "test:web",
		Name = "web",
		Address = "10.0.0.5",
		ProtocolId = "ssh",
		IdentityFilePath = path,
		IdentityPathStyle = IdentityPathStyle.OpenSsh,
	};

	private IdentityFileReader Reader(HostKind kind) =>
		new(new FakeAppEnvironment(_home.Path) { Kind = kind }, () => _home.Path, _ => null, "me", () => "desk");

	private string WriteKey(string name, string text)
	{
		string folder = Path.Combine(_home.Path, ".ssh");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, name);
		File.WriteAllText(path, text);
		return path;
	}
}
