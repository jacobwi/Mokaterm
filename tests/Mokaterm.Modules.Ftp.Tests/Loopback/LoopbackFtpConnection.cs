using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

/// <summary>One control connection of <see cref="LoopbackFtpServer"/>. Replies are modelled on vsftpd, including its vague 550s.</summary>
internal sealed class LoopbackFtpConnection : IAsyncDisposable
{
	private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
	private static readonly TimeSpan DataTimeout = TimeSpan.FromSeconds(20);

	private readonly LoopbackFtpServer _server;
	private readonly TcpClient _client;
	private Stream _stream;
	private StreamReader _reader;
	private string _directory = RemotePath.Root;
	private string? _user;
	private bool _loggedIn;
	private bool _protectData;
	private string? _renameFrom;
	private Task<Stream>? _pendingData;

	public LoopbackFtpConnection(LoopbackFtpServer server, TcpClient client)
	{
		_server = server;
		_client = client;
		_stream = client.GetStream();
		_reader = CreateReader(_stream);
	}

	private LoopbackFtpServerOptions Options => _server.Options;

	private FakeFileTree Files => _server.Files;

	public void Abort() => _client.Dispose();

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		if (Options.ImplicitTls)
		{
			await StartTlsAsync(cancellationToken);
		}

		if (_server.OpenConnections > Options.MaxConnections)
		{
			await ReplyAsync("421 Too many connections from this IP.", cancellationToken);
			return;
		}

		await ReplyAsync("220 Loopback FTP ready.", cancellationToken);
		while (await _reader.ReadLineAsync(cancellationToken) is { } line)
		{
			int space = line.IndexOf(' ');
			string verb = (space < 0 ? line : line[..space]).ToUpperInvariant();
			string argument = space < 0 ? "" : line[(space + 1)..];
			_server.Commands.Enqueue(verb == "PASS" ? "PASS ***" : line);
			if (!await HandleAsync(verb, argument, cancellationToken))
			{
				return;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		DiscardPendingData();
		try
		{
			await _stream.DisposeAsync();
		}
		catch (IOException)
		{
			// The peer already reset the connection.
		}

		_client.Dispose();
	}

	private static StreamReader CreateReader(Stream stream) => new(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

	private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

	private static string ListLine(string name, FakeNode node)
	{
		char type = node.Kind switch
		{
			FakeNodeKind.Directory => 'd',
			FakeNodeKind.Link => 'l',
			_ => '-',
		};
		long size = node.Kind == FakeNodeKind.File ? node.Content.Length : 4096;
		string month = node.Modified.ToString("MMM", CultureInfo.InvariantCulture);
		string target = node.Kind == FakeNodeKind.Link ? " -> " + node.LinkTarget : "";
		return Invariant($"{type}{UnixFileModeFormat.ToSymbolic(node.Mode)}    1 owner    group {size,12} {month} {node.Modified.Day,2} {node.Modified.Year,5} {name}{target}");
	}

	private static string MachineLine(string name, FakeNode node)
	{
		string type = node.Kind switch
		{
			FakeNodeKind.Directory => "dir",
			FakeNodeKind.Link => "OS.unix=symlink",
			_ => "file",
		};
		string size = node.Kind == FakeNodeKind.File ? Invariant($"size={node.Content.Length};") : "";
		return Invariant($"type={type};{size}modify={node.Modified:yyyyMMddHHmmss};UNIX.mode=0{UnixFileModeFormat.ToOctal(node.Mode)}; {name}");
	}

	private async Task<bool> HandleAsync(string verb, string argument, CancellationToken cancellationToken)
	{
		switch (verb)
		{
			case "USER":
				_user = argument;
				_loggedIn = false;
				await ReplyAsync("331 Please specify the password.", cancellationToken);
				return true;
			case "PASS":
				await LoginAsync(argument, cancellationToken);
				return true;
			case "AUTH" when Options.Certificate is not null && !Options.ImplicitTls:
				await ReplyAsync("234 Proceed with negotiation.", cancellationToken);
				await StartTlsAsync(cancellationToken);
				return true;
			case "PBSZ":
				await ReplyAsync("200 PBSZ set to 0.", cancellationToken);
				return true;
			case "PROT":
				_protectData = argument.Trim().Equals("P", StringComparison.OrdinalIgnoreCase);
				await ReplyAsync("200 PROT now set.", cancellationToken);
				return true;
			case "FEAT":
				await ReplyAsync(Features(), cancellationToken);
				return true;
			case "OPTS" or "TYPE" or "MODE" or "STRU":
				await ReplyAsync("200 OK.", cancellationToken);
				return true;
			case "SYST":
				await ReplyAsync("215 UNIX Type: L8", cancellationToken);
				return true;
			case "NOOP":
				await ReplyAsync("200 NOOP ok.", cancellationToken);
				return true;
			case "QUIT" when Options.IgnoreQuit:
				return true;
			case "QUIT":
				await ReplyAsync("221 Goodbye.", cancellationToken);
				return false;
			default:
				break;
		}

		if (!_loggedIn)
		{
			await ReplyAsync("530 Please login with USER and PASS.", cancellationToken);
			return true;
		}

		Task handled = verb switch
		{
			"PWD" => ReplyAsync(Invariant($"257 \"{_directory.Replace("\"", "\"\"", StringComparison.Ordinal)}\" is the current directory"), cancellationToken),
			"CWD" => ChangeDirectoryAsync(argument, cancellationToken),
			"CDUP" => ChangeDirectoryAsync("..", cancellationToken),
			"PASV" => OpenPassiveAsync(extended: false, cancellationToken),
			"EPSV" => OpenPassiveAsync(extended: true, cancellationToken),
			"LIST" => ListAsync(argument, machine: false, cancellationToken),
			"MLSD" when Options.MachineListings => ListAsync(argument, machine: true, cancellationToken),
			"MLST" when Options.MachineListings => StatAsync(argument, cancellationToken),
			"SIZE" => SizeAsync(argument, cancellationToken),
			"MDTM" => ModifiedAsync(argument, cancellationToken),
			"MFMT" when Options.SupportsMfmt => SetModifiedAsync(argument, cancellationToken),
			"RETR" => RetrieveAsync(argument, cancellationToken),
			"STOR" => StoreAsync(argument, cancellationToken),
			"MKD" => MakeDirectoryAsync(argument, cancellationToken),
			"RMD" => RemoveDirectoryAsync(argument, cancellationToken),
			"DELE" => DeleteAsync(argument, cancellationToken),
			"RNFR" => RenameFromAsync(argument, cancellationToken),
			"RNTO" => RenameToAsync(argument, cancellationToken),
			"SITE" => SiteAsync(argument, cancellationToken),
			_ => ReplyAsync("500 Unknown command.", cancellationToken),
		};
		await handled;
		return true;
	}

	private string Features()
	{
		StringBuilder reply = new("211-Features:\r\n SIZE\r\n MDTM\r\n UTF8\r\n EPSV\r\n PASV\r\n");
		if (Options.MachineListings)
		{
			reply.Append(" MLST type*;size*;modify*;UNIX.mode*;\r\n");
		}

		if (Options.SupportsMfmt)
		{
			reply.Append(" MFMT\r\n");
		}

		if (Options.Certificate is not null)
		{
			reply.Append(" AUTH TLS\r\n PBSZ\r\n PROT\r\n");
		}

		return reply.Append("211 End").ToString();
	}

	private async Task LoginAsync(string password, CancellationToken cancellationToken)
	{
		if (_user is not null && Options.Users.TryGetValue(_user, out LoopbackFtpUser? account) && account.Password == password)
		{
			_directory = account.Home;
			_loggedIn = true;
		}
		else if (_user == "anonymous" && Options.AllowAnonymous)
		{
			_directory = Options.AnonymousHome;
			_loggedIn = true;
		}

		await ReplyAsync(_loggedIn ? "230 Login successful." : "530 Login incorrect.", cancellationToken);
	}

	private async Task StartTlsAsync(CancellationToken cancellationToken)
	{
		SslStream tls = new(_stream, leaveInnerStreamOpen: false);
		await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Options.Certificate }, cancellationToken);
		_stream = tls;
		_reader = CreateReader(tls);
	}

	private async Task ReplyAsync(string text, CancellationToken cancellationToken)
	{
		await _stream.WriteAsync(Utf8.GetBytes(text + "\r\n"), cancellationToken);
		await _stream.FlushAsync(cancellationToken);
	}

	private string Resolve(string argument) => RemotePath.Combine(_directory, argument.Trim());

	private Task ChangeDirectoryAsync(string argument, CancellationToken cancellationToken)
	{
		string target = Resolve(argument);
		if (!Files.IsDirectory(target))
		{
			return ReplyAsync("550 Failed to change directory.", cancellationToken);
		}

		_directory = target;
		return ReplyAsync("250 Directory successfully changed.", cancellationToken);
	}

	private Task OpenPassiveAsync(bool extended, CancellationToken cancellationToken)
	{
		DiscardPendingData();
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start(1);
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;

		// Accept and, for PROT P, finish the TLS handshake in the background: clients differ in whether they start TLS
		// on the data connection before or after the transfer command.
		_pendingData = AcceptDataAsync(listener, _protectData, cancellationToken);
		return ReplyAsync(
			extended
				? Invariant($"229 Entering Extended Passive Mode (|||{port}|)")
				: Invariant($"227 Entering Passive Mode ({Options.PassiveAddress},{port >> 8},{port & 0xFF})."),
			cancellationToken);
	}

	private async Task<Stream> AcceptDataAsync(TcpListener listener, bool protect, CancellationToken cancellationToken)
	{
		try
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(DataTimeout);
			Socket socket = await listener.AcceptSocketAsync(timeout.Token);
			NetworkStream stream = new(socket, ownsSocket: true);
			if (!protect)
			{
				return stream;
			}

			SslStream tls = new(stream, leaveInnerStreamOpen: false);
			await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Options.Certificate }, timeout.Token);
			return tls;
		}
		finally
		{
			listener.Stop();
		}
	}

	private async Task<Stream?> OpenDataAsync(string preliminaryReply, CancellationToken cancellationToken)
	{
		Task<Stream>? pending = _pendingData;
		_pendingData = null;
		if (pending is null)
		{
			await ReplyAsync("425 Use PORT or PASV first.", cancellationToken);
			return null;
		}

		await ReplyAsync(preliminaryReply, cancellationToken);
		try
		{
			return await pending.WaitAsync(DataTimeout, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			await ReplyAsync("425 Failed to establish connection.", cancellationToken);
			return null;
		}
	}

	private void DiscardPendingData()
	{
		Task<Stream>? pending = _pendingData;
		_pendingData = null;
		if (pending is not null)
		{
			_ = CloseQuietlyAsync(pending);
		}
	}

	private static async Task CloseQuietlyAsync(Task<Stream> pending)
	{
		try
		{
			await (await pending).DisposeAsync();
		}
		catch (Exception)
		{
			// Nobody connected, or the client gave up on the connection.
		}
	}

	private async Task SendAsync(string preliminaryReply, byte[] content, CancellationToken cancellationToken)
	{
		Stream? data = await OpenDataAsync(preliminaryReply, cancellationToken);
		if (data is null)
		{
			return;
		}

		bool complete = false;
		try
		{
			await data.WriteAsync(content, cancellationToken);
			if (data is SslStream tls)
			{
				await tls.ShutdownAsync();
			}

			complete = true;
		}
		catch (IOException)
		{
			// The client stopped reading and closed the connection.
		}
		finally
		{
			await CloseQuietlyAsync(Task.FromResult(data));
		}

		await ReplyAsync(complete ? "226 Transfer complete." : "426 Failure writing network stream.", cancellationToken);
	}

	private async Task SendHalfAndStallAsync(string preliminaryReply, byte[] content, CancellationToken cancellationToken)
	{
		Stream? data = await OpenDataAsync(preliminaryReply, cancellationToken);
		if (data is null)
		{
			return;
		}

		try
		{
			await data.WriteAsync(content.AsMemory(0, content.Length / 2), cancellationToken);
			await data.FlushAsync(cancellationToken);
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		}
		finally
		{
			await CloseQuietlyAsync(Task.FromResult(data));
		}
	}

	private async Task ListAsync(string argument, bool machine, CancellationToken cancellationToken)
	{
		bool all = machine;
		string path = argument.Trim();
		if (!machine && Options.ListAllFilesSupported)
		{
			while (path.StartsWith('-'))
			{
				int space = path.IndexOf(' ');
				all |= (space < 0 ? path : path[..space]).Contains('a', StringComparison.Ordinal);
				path = space < 0 ? "" : path[(space + 1)..].TrimStart();
			}
		}

		string directory = Resolve(path);
		if (!Files.IsDirectory(directory))
		{
			DiscardPendingData();
			await ReplyAsync("550 Could not list directory.", cancellationToken);
			return;
		}

		StringBuilder listing = new();
		foreach ((string name, FakeNode node) in Files.Children(directory))
		{
			if (all || !name.StartsWith('.'))
			{
				listing.Append(machine ? MachineLine(name, node) : ListLine(name, node)).Append("\r\n");
			}
		}

		await SendAsync("150 Here comes the directory listing.", Utf8.GetBytes(listing.ToString()), cancellationToken);
	}

	private Task StatAsync(string argument, CancellationToken cancellationToken)
	{
		string path = Resolve(argument);
		return Files.Get(path) is { } node
			? ReplyAsync(Invariant($"250-Listing {path}\r\n {MachineLine(path, node)}\r\n250 End"), cancellationToken)
			: ReplyAsync("550 No such file or directory.", cancellationToken);
	}

	private Task SizeAsync(string argument, CancellationToken cancellationToken) =>
		Files.Resolve(Resolve(argument)) is { Kind: FakeNodeKind.File } node
			? ReplyAsync(Invariant($"213 {node.Content.Length}"), cancellationToken)
			: ReplyAsync("550 Could not get file size.", cancellationToken);

	private Task ModifiedAsync(string argument, CancellationToken cancellationToken) =>
		Files.Resolve(Resolve(argument)) is { } node
			? ReplyAsync(Invariant($"213 {node.Modified:yyyyMMddHHmmss}"), cancellationToken)
			: ReplyAsync("550 Could not get file modification time.", cancellationToken);

	private Task SetModifiedAsync(string argument, CancellationToken cancellationToken)
	{
		int space = argument.IndexOf(' ');
		if (space < 0 || !DateTime.TryParseExact(argument[..space], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime modified))
		{
			return ReplyAsync("501 Bad MFMT arguments.", cancellationToken);
		}

		if (Files.Get(Resolve(argument[(space + 1)..])) is not { } node)
		{
			return ReplyAsync("550 No such file.", cancellationToken);
		}

		node.Modified = modified;
		return ReplyAsync(Invariant($"213 Modify={modified:yyyyMMddHHmmss}; {argument[(space + 1)..]}"), cancellationToken);
	}

	private Task RetrieveAsync(string argument, CancellationToken cancellationToken)
	{
		if (Files.Resolve(Resolve(argument)) is { Kind: FakeNodeKind.File } node)
		{
			return Options.StallDownloads
				? SendHalfAndStallAsync("150 Opening BINARY mode data connection.", node.Content, cancellationToken)
				: SendAsync("150 Opening BINARY mode data connection.", node.Content, cancellationToken);
		}

		DiscardPendingData();
		return ReplyAsync("550 Failed to open file.", cancellationToken);
	}

	private async Task StoreAsync(string argument, CancellationToken cancellationToken)
	{
		string path = Resolve(argument);
		if (!Files.IsDirectory(RemotePath.GetParent(path)) || Files.Get(path) is { Kind: FakeNodeKind.Directory })
		{
			DiscardPendingData();
			await ReplyAsync("553 Could not create file.", cancellationToken);
			return;
		}

		Stream? data = await OpenDataAsync("150 Ok to send data.", cancellationToken);
		if (data is null)
		{
			return;
		}

		if (Options.StallUploads)
		{
			// Nothing is read, so the client's writes fill the socket buffers and then wait.
			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}
			finally
			{
				await CloseQuietlyAsync(Task.FromResult(data));
			}
		}

		using MemoryStream received = new();
		try
		{
			await data.CopyToAsync(received, cancellationToken);
		}
		catch (IOException)
		{
			// Clients close TLS data connections without close_notify; what arrived is kept, as real servers do.
		}
		finally
		{
			await CloseQuietlyAsync(Task.FromResult(data));
		}

		Files.Write(path, received.ToArray());
		await ReplyAsync("226 Transfer complete.", cancellationToken);
	}

	private Task MakeDirectoryAsync(string argument, CancellationToken cancellationToken)
	{
		string path = Resolve(argument);
		if (Files.Get(path) is not null || !Files.IsDirectory(RemotePath.GetParent(path)))
		{
			return ReplyAsync("550 Create directory operation failed.", cancellationToken);
		}

		Files.AddDirectory(path);
		return ReplyAsync(Invariant($"257 \"{path}\" created"), cancellationToken);
	}

	private Task RemoveDirectoryAsync(string argument, CancellationToken cancellationToken)
	{
		string path = Resolve(argument);
		if (RemotePath.IsRoot(path) || Files.Get(path) is not { Kind: FakeNodeKind.Directory } || Files.Children(path).Count > 0)
		{
			return ReplyAsync("550 Remove directory operation failed.", cancellationToken);
		}

		Files.Remove(path);
		return ReplyAsync("250 Remove directory operation successful.", cancellationToken);
	}

	private Task DeleteAsync(string argument, CancellationToken cancellationToken)
	{
		string path = Resolve(argument);
		return Files.Get(path) is { Kind: not FakeNodeKind.Directory } && Files.Remove(path)
			? ReplyAsync("250 Delete operation successful.", cancellationToken)
			: ReplyAsync("550 Delete operation failed.", cancellationToken);
	}

	private Task RenameFromAsync(string argument, CancellationToken cancellationToken)
	{
		string path = Resolve(argument);
		_renameFrom = Files.Get(path) is null ? null : path;
		return ReplyAsync(_renameFrom is null ? "550 RNFR command failed." : "350 Ready for RNTO.", cancellationToken);
	}

	private Task RenameToAsync(string argument, CancellationToken cancellationToken)
	{
		string? from = _renameFrom;
		_renameFrom = null;
		string to = Resolve(argument);
		if (from is null)
		{
			return ReplyAsync("503 RNFR required first.", cancellationToken);
		}

		if (Files.Get(to) is { Kind: FakeNodeKind.Directory } || !Files.IsDirectory(RemotePath.GetParent(to)))
		{
			return ReplyAsync("550 Rename failed.", cancellationToken);
		}

		Files.Remove(to);
		Files.Move(from, to);
		return ReplyAsync("250 Rename successful.", cancellationToken);
	}

	private Task SiteAsync(string argument, CancellationToken cancellationToken)
	{
		string[] parts = argument.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
		if (!Options.SupportsChmod || parts.Length < 3 || !parts[0].Equals("CHMOD", StringComparison.OrdinalIgnoreCase))
		{
			return ReplyAsync("500 Unknown SITE command.", cancellationToken);
		}

		if (!UnixFileModeFormat.TryParseOctal(parts[1].PadLeft(3, '0'), out UnixFileMode mode))
		{
			return ReplyAsync("501 SITE CHMOD command failed.", cancellationToken);
		}

		if (Files.Get(Resolve(parts[2])) is not { } node)
		{
			return ReplyAsync("550 SITE CHMOD command failed.", cancellationToken);
		}

		node.Mode = mode;
		return ReplyAsync("200 SITE CHMOD command ok.", cancellationToken);
	}
}
