using System.Text.Json;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class ProtocolOptionsTests
{
	[Fact]
	public void With_TypedValues_ReadBackThroughTypedAccessors()
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With("port", 2222)
			.With("compress", true)
			.With("encoding", "utf-8")
			.WithEnum<AuthenticationMethod>("method", AuthenticationMethod.KeyboardInteractive);

		Assert.Equal(4, options.Count);
		Assert.Equal(2222, options.GetInt32("port", 0));
		Assert.Equal("2222", options.GetString("port"));
		Assert.True(options.GetBoolean("compress", false));
		Assert.Equal("true", options["compress"]);
		Assert.Equal("utf-8", options.GetString("encoding", "ascii"));
		Assert.Equal(AuthenticationMethod.KeyboardInteractive, options.GetEnum("method", AuthenticationMethod.Password));
		Assert.Equal(7, options.GetInt32("missing", 7));
		Assert.Equal(7, options.GetInt32("encoding", 7));
		Assert.Empty(ProtocolOptions.Empty);
	}

	[Theory]
	[InlineData("KeyboardInteractive", AuthenticationMethod.KeyboardInteractive)]
	[InlineData("keyboardinteractive", AuthenticationMethod.KeyboardInteractive)]
	[InlineData("  PublicKey  ", AuthenticationMethod.PublicKey)]

	// Enum.TryParse takes numbers and comma separated lists as well, and it ORs a list, so either can land on a value
	// nobody stored. Everything here was written by WithEnum, so only a name counts.
	[InlineData("1", AuthenticationMethod.Password)]
	[InlineData("2", AuthenticationMethod.Password)]
	[InlineData("99", AuthenticationMethod.Password)]
	[InlineData("Password, PublicKey", AuthenticationMethod.Password)]
	[InlineData("Sneakernet", AuthenticationMethod.Password)]
	[InlineData("", AuthenticationMethod.Password)]
	public void GetEnum_TakesOnlyTheNameOfADefinedMember(string stored, AuthenticationMethod expected) =>
		Assert.Equal(expected, ProtocolOptions.Empty.With("method", stored).GetEnum("method", AuthenticationMethod.Password));

	[Fact]
	public void GetEnum_MissingKey_IsTheFallback() =>
		Assert.Equal(AuthenticationMethod.Agent, ProtocolOptions.Empty.GetEnum("method", AuthenticationMethod.Agent));

	[Fact]
	public void With_NullOrEmpty_RemovesKeyWithoutChangingOriginal()
	{
		ProtocolOptions original = ProtocolOptions.Empty.With("a", "1").With("b", "2");

		ProtocolOptions removed = original.With("a", "").With("b", (string?)null).With("c", (int?)null);

		Assert.Empty(removed);
		Assert.Equal(2, original.Count);
	}

	[Fact]
	public void Equality_IgnoresInsertionOrder()
	{
		ProtocolOptions first = ProtocolOptions.Empty.With("a", "1").With("b", "2");
		ProtocolOptions second = ProtocolOptions.From([new("b", "2"), new("a", "1")]);

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
		Assert.NotEqual(first, second.With("b", "3"));
	}

	[Fact]
	public void Json_RoundTripsAsFlatSortedObject()
	{
		ProtocolOptions options = ProtocolOptions.Empty.With("zeta", "last").With("alpha", 1).With("mid", false);

		string json = JsonSerializer.Serialize(options, MokatermJson.Compact);
		ProtocolOptions? read = JsonSerializer.Deserialize<ProtocolOptions>(json, MokatermJson.Compact);

		Assert.Equal("""{"alpha":"1","mid":"false","zeta":"last"}""", json);
		Assert.Equal(options, read);
	}

	[Fact]
	public void Json_ReadsNumbersAndBooleansAsStringsAndSkipsNulls()
	{
		ProtocolOptions? read = JsonSerializer.Deserialize<ProtocolOptions>("""{ "port": 22, "compress": true, "ratio": 1.5, "skip": null, "empty": "" }""");

		Assert.NotNull(read);
		Assert.Equal(3, read.Count);
		Assert.Equal("22", read["port"]);
		Assert.Equal("true", read["compress"]);
		Assert.Equal("1.5", read["ratio"]);
		Assert.False(read.ContainsKey("skip"));
	}

	[Theory]
	[InlineData("[]")]
	[InlineData("""{ "nested": { "a": 1 } }""")]
	[InlineData("""{ "list": [1, 2] }""")]
	public void Json_NonStringValues_AreRejected(string json) =>
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProtocolOptions>(json));

	[Fact]
	public void Json_ConnectionProfileKeepsOptions()
	{
		ConnectionProfile profile = new()
		{
			Id = Guid.NewGuid(),
			HostId = Guid.NewGuid(),
			ProtocolId = "ssh",
			Port = 2222,
			Options = ProtocolOptions.Empty.With("keepAliveSeconds", 30),
		};

		string json = JsonSerializer.Serialize(profile, MokatermJson.Document);
		ConnectionProfile? read = JsonSerializer.Deserialize<ConnectionProfile>(json, MokatermJson.Document);

		Assert.NotNull(read);
		Assert.Equal(profile.Options, read.Options);
		Assert.Equal(30, read.Options.GetInt32("keepAliveSeconds", 0));
		Assert.Contains("\"keepAliveSeconds\": \"30\"", json, StringComparison.Ordinal);
		Assert.Equal(profile, read);
	}
}
