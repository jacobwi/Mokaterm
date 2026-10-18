using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Sessions.Terminal;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

/// <summary>
/// The names cleanup is allowed to touch, the two rules, and that a foreign file and a file still being written survive
/// both the rules and the settings page's deletes.
/// </summary>
public sealed class SessionLogRetentionTests : IAsyncDisposable
{
	private static readonly DateTimeOffset Now = new(2026, 9, 26, 14, 25, 30, TimeSpan.Zero);

	private readonly FakeTimeProvider _time = new(Now);
	private readonly UncachedSettingsService _settings = new();
	private readonly TempFolder _data = new("mokaterm-log-retention");
	private readonly SessionLogRetention _retention;

	public SessionLogRetentionTests()
	{
		_settings.Set(new SessionLogSettings { Folder = Folder });
		_retention = new SessionLogRetention(_settings, new FakeAppEnvironment(_data.Path), _time, new ListLogger<SessionLogRetention>());
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private string Folder => Path.Combine(_data.Path, "logs");

	[Theory]
	[InlineData("abc@10.0.0.5:22", 0, ".log")]
	[InlineData("abc@10.0.0.5:22", 1, ".log")]
	[InlineData("abc@10.0.0.5:22", SessionLogNames.MaxAttempts, ".cast")]
	[InlineData("10.0.0.5:22", 0, ".log")]
	[InlineData("root@[fe80::1]:22", 0, ".cast")]
	[InlineData("admin/domain@some host.example.com:2222", 3, ".log")]
	[InlineData("COM3", 0, ".log")]
	[InlineData("a-very-long-user-name-that-goes-past-the-cap@a.host.name.that.is.far.longer.than.forty.eight.characters.example:22", 0, ".log")]
	[InlineData("\u00fc@\u00f6:22", 0, ".log")]
	public void IsGenerated_TakesEveryNameTheWriterBuilds(string endpoint, int attempt, string extension)
	{
		string name = SessionLogNames.Build(endpoint, Now, attempt, extension);

		Assert.True(SessionLogNames.IsGenerated(name), name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("notes.log")]
	[InlineData("server.log")]
	[InlineData("web01-root-20260926-142530.txt")]
	[InlineData("web01-root-20260926-142530.LOG")]
	[InlineData("web01-root-20260926-142530.log.bak")]
	[InlineData("web01-root-20260926-142530.cast.json")]
	[InlineData("web01-root-20261326-142530.log")]
	[InlineData("web01-root-20260931-142530.log")]
	[InlineData("web01-root-20260926-246030.log")]
	[InlineData("web01-root-2026092-142530.log")]
	[InlineData("web01-root-20260926-142530-0.log")]
	[InlineData("web01-root-20260926-142530-01.log")]
	[InlineData("web01-root-20260926-142530-21.log")]
	[InlineData("web 01-root-20260926-142530.log")]
	[InlineData("web01-root-20260926-142530 .log")]
	[InlineData("sub/web01-20260926-142530.log")]
	[InlineData("..\\web01-20260926-142530.log")]
	[InlineData("web01-root-\u0662\u0660\u0662\u0666\u0660\u0669\u0662\u0666-142530.log")]
	[InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-20260926-142530.log")]
	public void IsGenerated_RefusesWhatTheWriterNeverMakes(string name) =>
		Assert.False(SessionLogNames.IsGenerated(name), name);

	[Fact]
	public void Expired_WithTheDefaults_KeepsEverything()
	{
		SessionLogFile[] files = [File("a-20200101-000000.log", Now.AddYears(-6), 50L * 1024 * 1024 * 1024)];

		Assert.Empty(SessionLogRetention.Expired(files, Empty, new SessionLogSettings(), Now));
	}

	[Fact]
	public void Expired_ByAge_TakesFilesLastWrittenBeforeTheCutoff_ButNotOneInUse()
	{
		SessionLogFile old = File("old-20260901-100000.log", Now.AddDays(-25), 10);
		SessionLogFile writing = File("writing-20260901-100000.log", Now.AddDays(-25), 10);
		SessionLogFile recent = File("recent-20260920-100000.log", Now.AddDays(-6), 10);

		IReadOnlyList<SessionLogFile> expired = SessionLogRetention.Expired(
			[old, writing, recent],
			InUse(writing),
			new SessionLogSettings { DeleteAfterDays = 7 },
			Now);

		Assert.Equal([old], expired);
	}

	[Fact]
	public void Expired_OverTheSizeCap_TakesTheOldestFirstUntilTheRestFit()
	{
		const long Megabyte = 1024 * 1024;
		SessionLogFile oldest = File("a-20260901-100000.log", Now.AddDays(-20), 2 * Megabyte);
		SessionLogFile writing = File("b-20260902-100000.log", Now.AddDays(-19), 2 * Megabyte);
		SessionLogFile middle = File("c-20260903-100000.log", Now.AddDays(-18), 2 * Megabyte);
		SessionLogFile newest = File("d-20260904-100000.log", Now.AddDays(-1), 2 * Megabyte);

		// Eight megabytes against a cap of three: the file being written still counts, so the next oldest goes instead.
		IReadOnlyList<SessionLogFile> expired = SessionLogRetention.Expired(
			[newest, middle, writing, oldest],
			InUse(writing),
			new SessionLogSettings { KeepAtMostMegabytes = 3 },
			Now);

		Assert.Equal([oldest, middle, newest], expired);
	}

	[Fact]
	public void Expired_WithBothRules_MeasuresTheCapAfterTheAgeRule()
	{
		const long Megabyte = 1024 * 1024;
		SessionLogFile ancient = File("a-20260101-100000.log", Now.AddDays(-200), 5 * Megabyte);
		SessionLogFile older = File("b-20260920-100000.log", Now.AddDays(-5), 2 * Megabyte);
		SessionLogFile newer = File("c-20260925-100000.log", Now.AddDays(-1), 2 * Megabyte);

		IReadOnlyList<SessionLogFile> expired = SessionLogRetention.Expired(
			[ancient, older, newer],
			Empty,
			new SessionLogSettings { DeleteAfterDays = 30, KeepAtMostMegabytes = 3 },
			Now);

		Assert.Equal([ancient, older], expired);
	}

	[Fact]
	public async Task CleanUpAsync_DeletesOnlyOurOwnOldFiles_DirectlyInTheFolder()
	{
		_settings.Set(new SessionLogSettings { Folder = Folder, DeleteAfterDays = 7 });
		string ours = Write("web01-root-20260901-101010.log", Now.AddDays(-25));
		string timed = Write("web01-root-20260901-101011.cast", Now.AddDays(-25));
		string recent = Write("web01-root-20260925-101010.log", Now.AddDays(-1));
		string foreign = Write("notes.log", Now.AddDays(-400));
		string other = Write("readme.txt", Now.AddDays(-400));
		string nested = Write(Path.Combine("archive", "web01-root-20250101-101010.log"), Now.AddDays(-400));

		SessionLogDeletion deletion = await _retention.CleanUpAsync(Ct);

		Assert.Equal(2, deletion.Deleted);
		Assert.False(System.IO.File.Exists(ours));
		Assert.False(System.IO.File.Exists(timed));
		Assert.True(System.IO.File.Exists(recent));
		Assert.True(System.IO.File.Exists(foreign));
		Assert.True(System.IO.File.Exists(other));
		Assert.True(System.IO.File.Exists(nested));
	}

	[Fact]
	public async Task CleanUpAsync_WithTheRulesOff_DeletesNothing()
	{
		string ours = Write("web01-root-20200101-101010.log", Now.AddYears(-6));

		SessionLogDeletion deletion = await _retention.CleanUpAsync(Ct);

		Assert.Equal(SessionLogDeletion.None, deletion);
		Assert.True(System.IO.File.Exists(ours));
	}

	[Fact]
	public async Task CleanUpAsync_LeavesAFileBeingWritten_UntilItsRecordingCloses()
	{
		_settings.Set(new SessionLogSettings { Folder = Folder, DeleteAfterDays = 1 });
		string path = Write("web01-root-20260901-101010.log", Now.AddDays(-25));

		using (_retention.MarkWriting(path))
		{
			SessionLogDeletion kept = await _retention.CleanUpAsync(Ct);
			Assert.Equal(0, kept.Deleted);
			Assert.True(System.IO.File.Exists(path));
		}

		SessionLogDeletion deleted = await _retention.CleanUpAsync(Ct);

		Assert.Equal(1, deleted.Deleted);
		Assert.False(System.IO.File.Exists(path));
	}

	[Fact]
	public void Delete_TakesOnlyNamedLogFiles_AndCountsOneBeingWritten()
	{
		string named = Write("web01-root-20260925-101010.log", Now);
		string writing = Write("web01-root-20260925-101011.log", Now);
		string untouched = Write("web01-root-20260925-101012.log", Now);
		string foreign = Write("notes.log", Now);
		string outside = Write(Path.Combine("..", "web01-root-20260925-101013.log"), Now);
		using IDisposable mark = _retention.MarkWriting(writing);

		SessionLogDeletion deletion = _retention.Delete(
			[Path.GetFileName(named), Path.GetFileName(writing), "notes.log", "../web01-root-20260925-101013.log", "..\\web01-root-20260925-101013.log"]);

		Assert.Equal(new SessionLogDeletion { Deleted = 1, InUse = 1 }, deletion);
		Assert.False(System.IO.File.Exists(named));
		Assert.True(System.IO.File.Exists(writing));
		Assert.True(System.IO.File.Exists(untouched));
		Assert.True(System.IO.File.Exists(foreign));
		Assert.True(System.IO.File.Exists(outside));
	}

	[Fact]
	public void DeleteAll_KeepsForeignFilesAndOnesBeingWritten()
	{
		string first = Write("web01-root-20260925-101010.log", Now);
		string second = Write("db-postgres-20260925-101010-2.cast", Now);
		string writing = Write("web01-root-20260925-101011.log", Now);
		string foreign = Write("notes.log", Now);
		using IDisposable mark = _retention.MarkWriting(writing);

		SessionLogDeletion deletion = _retention.DeleteAll();

		Assert.Equal(new SessionLogDeletion { Deleted = 2, InUse = 1 }, deletion);
		Assert.False(System.IO.File.Exists(first));
		Assert.False(System.IO.File.Exists(second));
		Assert.True(System.IO.File.Exists(writing));
		Assert.True(System.IO.File.Exists(foreign));
	}

	[Fact]
	public void List_LeavesOutWhatIsNotALog_AndAMissingFolderIsEmpty()
	{
		Assert.Empty(_retention.List());

		_ = Write("web01-root-20260925-101010.log", Now);
		_ = Write("notes.log", Now);

		Assert.Equal("web01-root-20260925-101010.log", Assert.Single(_retention.List()).Name);
	}

	public ValueTask DisposeAsync() => _data.DisposeAsync();

	private static IReadOnlySet<string> Empty { get; } = new HashSet<string>();

	private static HashSet<string> InUse(params SessionLogFile[] files) => [.. files.Select(file => file.Name)];

	private static SessionLogFile File(string name, DateTimeOffset writtenAt, long length) => new()
	{
		Name = name,
		Length = length,
		WrittenAt = writtenAt,
	};

	private string Write(string relativePath, DateTimeOffset writtenAt)
	{
		string path = Path.GetFullPath(Path.Combine(Folder, relativePath));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		System.IO.File.WriteAllText(path, "output\r\n");
		System.IO.File.SetLastWriteTimeUtc(path, writtenAt.UtcDateTime);
		return path;
	}
}
