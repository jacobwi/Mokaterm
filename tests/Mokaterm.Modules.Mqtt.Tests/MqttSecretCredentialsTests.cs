using System.Text;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Mqtt.Protocol;
using MQTTnet;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttSecretCredentialsTests
{
	[Fact]
	public void GetPassword_ReturnsTheSecretWhileTheProviderIsAlive()
	{
		using MqttSecretCredentials credentials = new("operator", SecretBuffer.FromString("hunter2"));
		MqttClientOptions options = new();

		Assert.Equal("operator", credentials.GetUserName(options));
		Assert.Equal("hunter2", Encoding.UTF8.GetString(credentials.GetPassword(options)!));
	}

	[Fact]
	public void GetPassword_ReturnsANewArrayEachTime_SoNothingHoldsTheSecret()
	{
		using MqttSecretCredentials credentials = new("operator", SecretBuffer.FromString("hunter2"));
		MqttClientOptions options = new();

		Assert.NotSame(credentials.GetPassword(options), credentials.GetPassword(options));
	}

	[Fact]
	public void GetPassword_IsNullForALoginWithoutOne()
	{
		using MqttSecretCredentials credentials = new("operator", password: null);

		Assert.Null(credentials.GetPassword(new MqttClientOptions()));
	}

	[Fact]
	public void Dispose_WipesTheBufferAndStopsHandingOutTheSecret()
	{
		SecretBuffer password = SecretBuffer.FromString("hunter2");
		MqttSecretCredentials credentials = new("operator", password);

		credentials.Dispose();

		Assert.True(password.IsDisposed);
		Assert.Null(credentials.GetPassword(new MqttClientOptions()));
	}

	[Fact]
	public void Dispose_Twice_IsHarmless()
	{
		MqttSecretCredentials credentials = new("operator", SecretBuffer.FromString("hunter2"));

		credentials.Dispose();
		credentials.Dispose();
	}
}
