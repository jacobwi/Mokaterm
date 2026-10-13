using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Plugins;
using Mokaterm.Core.Plugins.Gallery;
using BouncyEd25519 = Org.BouncyCastle.Math.EC.Rfc8032.Ed25519;

namespace Mokaterm.Core.Tests.Plugins.Gallery;

/// <summary>
/// A gallery for one test: a signing key made for it, a fake server holding the list, its signature and the packages,
/// and the JSON shapes the format describes, built so a test can break one field at a time.
/// </summary>
internal sealed class TestGallery
{
	public static readonly Uri IndexAddress = new("https://gallery.test/mokaterm-plugins/index.json");

	public static readonly Uri SignatureAddress = new("https://gallery.test/mokaterm-plugins/index.json.sig");

	public const string Commit = "9f2c4e1a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e";

	public const string Generated = "2026-10-13T09:00:00Z";

	private readonly byte[] _secret = RandomNumberGenerator.GetBytes(BouncyEd25519.SecretKeySize);

	public TestGallery()
	{
		byte[] publicKey = new byte[BouncyEd25519.PublicKeySize];
		BouncyEd25519.GeneratePublicKey(_secret, 0, publicKey, 0);
		PublicKey = Convert.ToBase64String(publicKey);
	}

	public string PublicKey { get; }

	public FakeGalleryServer Server { get; } = new();

	public static string Sdk => TestPlugins.Sdk;

	/// <summary>Where a package for <paramref name="id"/> and <paramref name="version"/> is served.</summary>
	public static Uri PackageAddress(string id, string version) =>
		new($"https://downloads.test/releases/{id}-v{version}/{id}-{version}.mokaplugin");

	public byte[] Sign(byte[] data)
	{
		byte[] signature = new byte[BouncyEd25519.SignatureSize];
		BouncyEd25519.Sign(_secret, 0, data, 0, data.Length, signature, 0);
		return signature;
	}

	/// <summary>Serves <paramref name="index"/> and its signature, signed with this gallery's key.</summary>
	public byte[] Publish(JsonObject index)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(index.ToJsonString());
		Server.Serve(IndexAddress, bytes);
		Server.Serve(SignatureAddress, SignatureFile(Sign(bytes)));
		return bytes;
	}

	/// <summary>Serves a package where <see cref="Version"/> says it is.</summary>
	public void Host(string id, string version, byte[] package) => Server.Serve(PackageAddress(id, version), package);

	public static byte[] SignatureFile(byte[] signature) => Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n");

	/// <summary>A client over this gallery's server, trusting this gallery's key unless told otherwise.</summary>
	public PluginGalleryClient Client(IReadOnlyList<string>? keys = null) =>
		new(new PluginGalleryOptions { Index = IndexAddress, TrustedKeys = keys ?? [PublicKey] }, Server);

	/// <summary>The gallery a window gets, over a real plugin manager.</summary>
	public PluginGallery Gallery(PluginManager manager, HostKind host = HostKind.Desktop) =>
		new(Client(), manager, new FakeAppEnvironment(Path.GetTempPath()) { Kind = host });

	public static JsonObject Index(params JsonObject[] plugins) => new()
	{
		["format"] = 1,
		["generated"] = Generated,
		["plugins"] = new JsonArray([.. plugins]),
	};

	public static JsonObject Plugin(string id, bool hasCode, params JsonObject[] versions) => new()
	{
		["id"] = id,
		["name"] = $"Plugin {id}",
		["publisher"] = "Tests",
		["description"] = "A plugin for tests.",
		["categories"] = new JsonArray("utilities", "server-tools"),
		["source"] = "https://github.com/acme/mokaterm-tests",
		["license"] = "MIT",
		["verified"] = false,
		["hasCode"] = hasCode,
		["hosts"] = new JsonArray("desktop", "web"),
		["versions"] = new JsonArray([.. versions]),
	};

	/// <summary>A version entry for <paramref name="package"/>, with its real size and hash unless a test changes them.</summary>
	public static JsonObject Version(string id, string version, byte[] package, string? sdk = null, string? withdrawn = null) => new()
	{
		["version"] = version,
		["sdk"] = sdk ?? Sdk,
		["url"] = PackageAddress(id, version).AbsoluteUri,
		["sha256"] = Convert.ToHexStringLower(SHA256.HashData(package)),
		["size"] = package.Length,
		["commit"] = Commit,
		["published"] = Generated,
		["withdrawn"] = withdrawn,
	};

	/// <summary>A version entry with no package behind it, for tests that only read the list.</summary>
	public static JsonObject Version(string version, string? sdk = null, string? withdrawn = null) =>
		Version("acme.listed", version, Encoding.ASCII.GetBytes(version), sdk, withdrawn);
}
