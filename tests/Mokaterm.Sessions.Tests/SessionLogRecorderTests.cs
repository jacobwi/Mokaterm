using System.Text;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Terminal;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class SessionLogRecorderTests : IAsyncDisposable
{
	/// <summary>The name every session in these tests gets, because the clock never moves.</summary>
	private const string LogName = "10.0.0.5-abc-20260926-142530.log";

	/// <summary>The name a second file of the same session in the same second falls back to.</summary>
	private const string SecondLogName = "10.0.0.5-abc-20260926-142530-1.log";

	private static readonly HostProfile Host = new() { Id = Guid.NewGuid(), Address = "10.0.0.5" };

	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 26, 14, 25, 30, TimeSpan.Zero));
	private readonly FakeConnectionRepository _repository = new();
	private readonly FakeCredentialStore _credentials = new();
	private readonly UncachedSettingsService _settings = new();
	private readonly FakeProtocolProvider _ssh = new(TestProtocols.Ssh);
	private readonly FakeProtocolProvider _ftp = new(TestProtocols.Ftp);
	private readonly ListLogger<SessionLogRecorder> _logger = new();
	private readonly TempFolder _dataDirectory = new("mokaterm-session-logs");
	private readonly ConnectionProfile _connection;
	private readonly ConnectionProfile _fileOnly;
	private readonly SessionManager _manager;
	private SessionLogRecorder? _recorder;

	public SessionLogRecorderTests()
	{
		CredentialInfo password = _credentials.Add(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "shared", Kind = CredentialKind.Password, IsShared = true },
			new CredentialSecretInput { Password = "pw" });
		_connection = new ConnectionProfile
		{
			Id = Guid.NewGuid(),
			HostId = Host.Id,
			ProtocolId = "ssh",
			Username = "abc",
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = password.Id,
		};

		_fileOnly = _connection with { Id = Guid.NewGuid(), ProtocolId = "ftp" };
		_repository.Add(Host, _connection, _fileOnly);
		_manager = new SessionManager(
			_repository,
			_credentials,
			new ProtocolRegistry([_ssh, _ftp]),
			new FakeHostIdentityVerifier(),
			new FakeUserInteraction(),
			_settings,
			new ListLogger<SessionManager>(),
			_time);
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private SessionLogRecorder Recorder => _recorder ?? CreateRecorder(_settings.Get<SessionLogSettings>());

	/// <summary>The folder these tests point the settings at.</summary>
	private string LogFolder => Path.Combine(_dataDirectory.Path, "picked");

	[Fact]
	public void Folder_WithoutASetting_SitsUnderTheDataDirectory()
	{
		SessionLogRecorder recorder = CreateRecorder(new SessionLogSettings());

		Assert.Equal(Path.Combine(_dataDirectory.Path, "logs", "sessions"), recorder.Folder);
	}

	[Fact]
	public async Task RecordEverySession_WritesWhatTheServerPrinted_UnderANameNamingTheSession()
	{
		CreateRecorder(new SessionLogSettings { RecordEverySession = true, Folder = LogFolder });
		(ISessionHandle handle, FakeTerminalChannel channel) = await OpenConnectedAsync();

		channel.Emit("total 0\r\n");

		string text = await WaitForLogAsync(LogName, "total 0");
		Assert.StartsWith("[mokaterm] session log: abc@10.0.0.5:22, started 2026-09-26 14:25:30 +00:00", text, StringComparison.Ordinal);
		Assert.Contains("Nothing typed is recorded.", text, StringComparison.Ordinal);
		SessionLogStatus status = Recorder.StatusFor(handle.Id);
		Assert.Equal(SessionLogState.Recording, status.State);
		Assert.Equal(LogName, status.FileName);
		Assert.Equal("total 0\r\n".Length, status.Bytes);
	}

	[Fact]
	public async Task Input_IsNeverWritten()
	{
		CreateRecorder(new SessionLogSettings { RecordEverySession = true, Folder = LogFolder });
		(ISessionHandle handle, FakeTerminalChannel channel) = await OpenConnectedAsync();
		channel.Emit("Password: ");
		_ = await WaitForLogAsync(LogName, "Password: ");

		// A password answering a prompt is exactly what a sink must never see.
		await handle.Terminal!.SendTextAsync("hunter2\r", Ct);
		channel.Emit("\r\nwelcome\r\n");

		string text = await WaitForLogAsync(LogName, "welcome");
		Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
		Assert.Equal("hunter2\r", channel.InputText);
	}

	[Fact]
	public async Task SetRecording_RecordsOneSessionWhileTheSettingIsOff_AndStopsOnRequest()
	{
		CreateRecorder(new SessionLogSettings { Folder = LogFolder });
		(ISessionHandle handle, FakeTerminalChannel channel) = await OpenConnectedAsync();
		channel.Emit("before\r\n");
		await Eventually.StaysTrueAsync(() => !Directory.Exists(LogFolder), "nothing is written while the setting is off");
		Assert.Equal(SessionLogState.Off, Recorder.StatusFor(handle.Id).State);

		Recorder.SetRecording(handle.Id, true);
		channel.Emit("after\r\n");
		string text = await WaitForLogAsync(LogName, "after");

		// Attach hands a new sink the replay buffer first, so the file opens with what the terminal already shows.
		Assert.Contains("before", text, StringComparison.Ordinal);

		Recorder.SetRecording(handle.Id, false);
		Assert.Equal(SessionLogState.Off, Recorder.StatusFor(handle.Id).State);
		channel.Emit("while stopped\r\n");
		await Eventually.StaysTrueAsync(
			() => TryReadLog(LogName)?.Contains("while stopped", StringComparison.Ordinal) == false,
			"output after the stop is not written");

		// The file is closed with the recording, so it can be deleted.
		await Eventually.TrueAsync(() => TryDelete(Path.Combine(LogFolder, LogName)), "the file handle is released");
	}

	[Fact]
	public async Task PlainText_DropsEscapeSequences()
	{
		CreateRecorder(new SessionLogSettings { RecordEverySession = true, Folder = LogFolder });
		(_, FakeTerminalChannel channel) = await OpenConnectedAsync();

		channel.Emit("[32mgreen[0m\r\n");

		string text = await WaitForLogAsync(LogName, "green");
		Assert.DoesNotContain("", text, StringComparison.Ordinal);
		Assert.EndsWith("green\r\n", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Raw_KeepsEveryByte()
	{
		CreateRecorder(new SessionLogSettings { RecordEverySession = true, Folder = LogFolder, Format = SessionLogFormat.Raw });
		(_, FakeTerminalChannel channel) = await OpenConnectedAsync();

		channel.Emit("[32mgreen[0m\r\n");

		string text = await WaitForLogAsync(LogName, "green");
		Assert.EndsWith("[32mgreen[0m\r\n", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PastTheSizeLimit_TheFileSaysSoAndTakesNoMore()
	{
		CreateRecorder(new SessionLogSettings
		{
			RecordEverySession = true,
			Folder = LogFolder,
			SizeLimitKilobytes = SessionLogSettings.MinSizeLimitKilobytes,
		});

		(ISessionHandle handle, FakeTerminalChannel channel) = await OpenConnectedAsync();
		string kilobyte = new('x', 1024);
		for (int i = 0; i < 24; i++)
		{
			channel.Emit(kilobyte);
		}

		await Eventually.TrueAsync(() => Recorder.StatusFor(handle.Id).State == SessionLogState.Full, "the recording reports a full file");
		SessionLogStatus status = Recorder.StatusFor(handle.Id);
		Assert.Equal(SessionLogSettings.MinSizeLimitKilobytes * 1024L, status.Bytes);
		Assert.Equal("The 16 KB limit was reached.", status.Message);
		string text = ReadLog(LogName);
		Assert.EndsWith("the rest of this session is not recorded.\r\n", text, StringComparison.Ordinal);
		await Eventually.StaysTrueAsync(() => ReadLog(LogName).Length == text.Length, "a full file takes nothing more");
	}

	[Fact]
	public async Task TurningRecordingOnAgain_StartsANewFile()
	{
		CreateRecorder(new SessionLogSettings { Folder = LogFolder });
		(ISessionHandle handle, FakeTerminalChannel channel) = await OpenConnectedAsync();
		Recorder.SetRecording(handle.Id, true);
		channel.Emit("first file\r\n");
		_ = await WaitForLogAsync(LogName, "first file");

		Recorder.SetRecording(handle.Id, false);
		Recorder.SetRecording(handle.Id, true);
		channel.Emit("second file\r\n");

		// The same session in the same second, so the name it wants is taken and the next one is used.
		_ = await WaitForLogAsync(SecondLogName, "second file");
		Assert.Equal(2, Recorder.ListFiles().Count);
		Assert.DoesNotContain("second file", ReadLog(LogName), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ClosingASession_ClosesItsFile()
	{
		CreateRecorder(new SessionLogSettings { RecordEverySession = true, Folder = LogFolder });
		(ISessionHandle handle, FakeTerminalChannel channel) = await OpenConnectedAsync();
		channel.Emit("bye\r\n");
		_ = await WaitForLogAsync(LogName, "bye");

		await _manager.CloseAsync(handle.Id);

		await Eventually.TrueAsync(() => Recorder.StatusFor(handle.Id).State == SessionLogState.Off, "the recording goes with the tab");
		await Eventually.TrueAsync(() => TryDelete(Path.Combine(LogFolder, LogName)), "the file handle is released");
	}

	[Fact]
	public async Task DisposeAsync_ClosesEveryFile()
	{
		CreateRecorder(new SessionLogSettings { RecordEverySession = true, Folder = LogFolder });
		(_, FakeTerminalChannel first) = await OpenConnectedAsync();
		(_, FakeTerminalChannel second) = await OpenConnectedAsync();
		first.Emit("one\r\n");
		second.Emit("two\r\n");
		await Eventually.TrueAsync(() => Recorder.ListFiles().Count == 2, "both sessions have a file");

		await Recorder.DisposeAsync();

		IReadOnlyList<SessionLogFile> files = Recorder.ListFiles();
		Assert.Equal(2, files.Count);
		Assert.All(files, file => Assert.True(TryDelete(Path.Combine(LogFolder, file.Name)), "the file handle is released"));
	}

	[Fact]
	public async Task ASessionWithoutATerminal_IsNotRecorded()
	{
		CreateRecorder(new SessionLogSettings { RecordEverySession = true, Folder = LogFolder });

		ISessionHandle handle = await _manager.OpenAsync(_fileOnly.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Connected, "the file-only session connects");
		Recorder.SetRecording(handle.Id, true);

		Assert.Null(handle.Terminal);
		Assert.Equal(SessionLogState.Off, Recorder.StatusFor(handle.Id).State);
		Assert.Empty(Recorder.ListFiles());
	}

	[Theory]
	[InlineData("..\\vault.json")]
	[InlineData("../vault.json")]
	[InlineData("logs/a.log")]
	[InlineData("a.txt")]
	[InlineData(" ")]
	public void OpenRead_RefusesAnythingButAPlainLogName(string name)
	{
		SessionLogRecorder recorder = CreateRecorder(new SessionLogSettings { Folder = LogFolder });

		_ = Assert.Throws<ArgumentException>(() => recorder.OpenRead(name));
	}

	[Theory]
	[InlineData("abc@10.0.0.5:22", LogName)]
	[InlineData("10.0.0.5:22", "10.0.0.5-20260926-142530.log")]
	[InlineData("root@[fe80::1]:22", "fe80_1-root-20260926-142530.log")]
	[InlineData("admin/domain@some host.example.com:2222", "some_host.example.com-admin_domain-20260926-142530.log")]
	public void BuildFileName_KeepsTheSessionReadableAndTheNameUsable(string endpoint, string expected) =>
		Assert.Equal(expected, SessionLogWriter.BuildFileName(endpoint, new DateTimeOffset(2026, 9, 26, 14, 25, 30, TimeSpan.Zero), attempt: 0));

	public async ValueTask DisposeAsync()
	{
		if (_recorder is { } recorder)
		{
			await recorder.DisposeAsync();
		}

		await _manager.DisposeAsync();
		await _dataDirectory.DisposeAsync();
	}

	private static bool TryDelete(string path)
	{
		try
		{
			File.Delete(path);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private SessionLogRecorder CreateRecorder(SessionLogSettings settings)
	{
		_settings.Set(settings);
		_recorder = new SessionLogRecorder(_manager, _settings, new FakeAppEnvironment(_dataDirectory.Path), _time, _logger);
		return _recorder;
	}

	private async Task<string> WaitForLogAsync(string name, string contains)
	{
		await Eventually.TrueAsync(
			() => TryReadLog(name)?.Contains(contains, StringComparison.Ordinal) == true,
			$"the log holds \"{contains}\"");
		return ReadLog(name);
	}

	private string? TryReadLog(string name)
	{
		try
		{
			return ReadLog(name);
		}
		catch (IOException)
		{
			return null;
		}
	}

	private string ReadLog(string name)
	{
		using Stream stream = Recorder.OpenRead(name);
		using StreamReader reader = new(stream, Encoding.UTF8);
		return reader.ReadToEnd();
	}

	private async Task<(ISessionHandle Handle, FakeTerminalChannel Channel)> OpenConnectedAsync()
	{
		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Connected, "the session connects");
		FakeProtocolSession session = _ssh.Sessions[^1];
		Assert.NotNull(session.Channel);
		return (handle, session.Channel);
	}
}
