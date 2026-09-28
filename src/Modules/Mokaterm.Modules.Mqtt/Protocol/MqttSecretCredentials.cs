using Mokaterm.Abstractions.Security;
using MQTTnet;

namespace Mokaterm.Modules.Mqtt.Protocol;

/// <summary>
/// The login MQTTnet asks for while it builds each CONNECT. MQTTnet's own <c>MqttClientCredentials</c> would hold
/// the password in a plain array for the whole life of the client; this keeps it in a <see cref="SecretBuffer"/>
/// and copies it out only when a packet is being written.
/// </summary>
internal sealed class MqttSecretCredentials : IMqttClientCredentialsProvider, IDisposable
{
	private readonly string _username;
	private readonly SecretBuffer? _password;
	private int _disposed;

	/// <param name="password">A copy this instance owns and wipes. The caller keeps its own.</param>
	public MqttSecretCredentials(string username, SecretBuffer? password)
	{
		_username = username;
		_password = password;
	}

	public string GetUserName(MqttClientOptions clientOptions) => _username;

	public byte[]? GetPassword(MqttClientOptions clientOptions)
	{
		// GetPassword returns an array by contract, so this copy lands on the heap where it cannot be wiped. It is
		// the narrowest the interface allows: nothing keeps a reference to it once the packet is written.
		if (Volatile.Read(ref _disposed) != 0 || _password is null || _password.IsDisposed)
		{
			return null;
		}

		return _password.Span.ToArray();
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_password?.Dispose();
		}
	}
}
