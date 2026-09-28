using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using Mokaterm.Modules.Ssh.Agent;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// An SSH agent on a named pipe of its own. It answers exactly as a real agent does, so the client is tested against
/// the wire format rather than against itself, and it keeps every request for the test to read back.
/// </summary>
internal sealed class FakeAgentServer : IAsyncDisposable
{
	private readonly CancellationTokenSource _stopping = new();
	private readonly List<byte[]> _requests = [];
	private readonly Func<byte[], byte[]> _respond;
	private readonly Task _loop;

	/// <param name="respond">Takes one request body and returns the response body, message type included.</param>
	public FakeAgentServer(Func<byte[], byte[]> respond)
	{
		_respond = respond;
		PipeName = "mokaterm-test-agent-" + Guid.NewGuid().ToString("N");
		_loop = Task.Run(ServeAsync);
	}

	public string PipeName { get; }

	public SshAgentAddress Address => new(SshAgentTransport.NamedPipe, PipeName);

	/// <summary>The request bodies the client sent, in order, with the frame length already stripped.</summary>
	public IReadOnlyList<byte[]> Requests
	{
		get
		{
			lock (_requests)
			{
				return [.. _requests];
			}
		}
	}

	/// <summary>An identities answer holding <paramref name="keys"/>, each a blob and a comment.</summary>
	public static byte[] IdentitiesAnswer(params (byte[] Blob, string Comment)[] keys)
	{
		SshWireWriter writer = new SshWireWriter().WriteByte(SshAgentClient.IdentitiesAnswer).WriteUInt32((uint)keys.Length);
		foreach ((byte[] blob, string comment) in keys)
		{
			writer.WriteString(blob).WriteString(Encoding.UTF8.GetBytes(comment));
		}

		return writer.ToArray();
	}

	public static byte[] SignAnswer(byte[] signature) =>
		new SshWireWriter().WriteByte(SshAgentClient.SignResponse).WriteString(signature).ToArray();

	public static byte[] Failure() => [SshAgentClient.Failure];

	public SshAgentClient Connect() => SshAgentClient.Connect(Address, TimeSpan.FromSeconds(10));

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		try
		{
			await _loop.WaitAsync(TimeSpan.FromSeconds(5));
		}
		catch (Exception)
		{
			// A server nobody connected to is still sitting in WaitForConnectionAsync; the process ends either way.
		}

		_stopping.Dispose();
	}

	private async Task ServeAsync()
	{
		while (!_stopping.IsCancellationRequested)
		{
			using NamedPipeServerStream pipe = new(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances);
			try
			{
				await pipe.WaitForConnectionAsync(_stopping.Token);
				await ServeOneAsync(pipe);
			}
			catch (Exception) when (_stopping.IsCancellationRequested)
			{
				return;
			}
			catch (IOException)
			{
				// The client hung up mid request; wait for the next one.
			}
		}
	}

	private async Task ServeOneAsync(NamedPipeServerStream pipe)
	{
		byte[] header = new byte[sizeof(uint)];
		while (!_stopping.IsCancellationRequested)
		{
			int read = await pipe.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, _stopping.Token);
			if (read < header.Length)
			{
				return;
			}

			byte[] body = new byte[BinaryPrimitives.ReadUInt32BigEndian(header)];
			await pipe.ReadExactlyAsync(body, _stopping.Token);
			lock (_requests)
			{
				_requests.Add(body);
			}

			byte[] response = _respond(body);
			BinaryPrimitives.WriteUInt32BigEndian(header, (uint)response.Length);
			await pipe.WriteAsync(header, _stopping.Token);
			await pipe.WriteAsync(response, _stopping.Token);
			await pipe.FlushAsync(_stopping.Token);
		}
	}
}
