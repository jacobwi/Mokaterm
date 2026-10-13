using System.Net;
using System.Text;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Plugins;
using Mokaterm.Core.Plugins.Gallery;

namespace Mokaterm.Core.Tests.Plugins.Gallery;

/// <summary>
/// Reading the gallery's list: the signature is checked over the exact bytes before anything is parsed, every read is
/// capped, and a request says nothing but that it comes from Mokaterm.
/// </summary>
public sealed class PluginGalleryClientTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static string Host => PluginHosts.Desktop;

	[Fact]
	public async Task ReadIndex_SignedWithATrustedKey_ReadsTheList()
	{
		TestGallery gallery = new();
		gallery.Publish(TestGallery.Index(TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0"))));
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryIndex index = await client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct);

		Assert.Equal("acme.proxmox", Assert.Single(index.Plugins).Id);
	}

	[Fact]
	public async Task ReadIndex_WithOneByteChanged_IsRefused()
	{
		TestGallery gallery = new();
		byte[] signed = gallery.Publish(TestGallery.Index(TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0"))));

		// The same list with one letter of the name changed, after it was signed.
		byte[] tampered = (byte[])signed.Clone();
		int name = Encoding.UTF8.GetString(tampered).IndexOf("Plugin acme", StringComparison.Ordinal);
		tampered[name] = (byte)'p';
		gallery.Server.Serve(TestGallery.IndexAddress, tampered);
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(() => client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("not signed with a key this version of Mokaterm trusts", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadIndex_SignedWithAnotherKey_IsRefused()
	{
		TestGallery gallery = new();
		TestGallery stranger = new();
		byte[] index = gallery.Publish(TestGallery.Index());
		gallery.Server.Serve(TestGallery.SignatureAddress, TestGallery.SignatureFile(stranger.Sign(index)));
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(() => client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("not signed with a key", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadIndex_SignedWithTheSecondOfTwoTrustedKeys_ReadsTheList()
	{
		// A new key ships beside the old one before the gallery switches, so both have to be accepted meanwhile.
		TestGallery gallery = new();
		TestGallery previous = new();
		gallery.Publish(TestGallery.Index());
		using PluginGalleryClient client = gallery.Client([previous.PublicKey, gallery.PublicKey]);

		PluginGalleryIndex index = await client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct);

		Assert.Empty(index.Plugins);
	}

	[Fact]
	public async Task ReadIndex_WithoutASignature_IsRefused()
	{
		TestGallery gallery = new();
		gallery.Publish(TestGallery.Index());
		gallery.Server.Forget(TestGallery.SignatureAddress);
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(() => client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct));

		Assert.Equal("The plugin list has no signature, so it was not used.", error.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("not a signature\n")]
	[InlineData("AAAA\n")]
	public async Task ReadIndex_WithASignatureFileThatHoldsNone_IsRefused(string file)
	{
		TestGallery gallery = new();
		gallery.Publish(TestGallery.Index());
		gallery.Server.Serve(TestGallery.SignatureAddress, Encoding.ASCII.GetBytes(file));
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(() => client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("holds no signature", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadIndex_WithASignatureWrittenOnAWindowsCheckout_ReadsTheList()
	{
		TestGallery gallery = new();
		byte[] index = gallery.Publish(TestGallery.Index());
		gallery.Server.Serve(TestGallery.SignatureAddress, Encoding.ASCII.GetBytes(Convert.ToBase64String(gallery.Sign(index)) + "\r\n"));
		using PluginGalleryClient client = gallery.Client();

		Assert.Empty((await client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct)).Plugins);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ReadIndex_LargerThan4MiB_IsRefused_WithOrWithoutALength(bool hideLength)
	{
		TestGallery gallery = new();
		gallery.Publish(TestGallery.Index());
		gallery.Server.Serve(TestGallery.IndexAddress, new byte[(4 * 1024 * 1024) + 1], hideLength);
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(() => client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct));

		Assert.Equal("The plugin list is larger than 4 MB, so it was not read.", error.Message);

		// Nothing past the list was asked for: a list that is refused is never verified or parsed.
		Assert.Single(gallery.Server.Requests);
	}

	[Fact]
	public async Task ReadIndex_WithASignatureOver1KiB_IsRefused()
	{
		TestGallery gallery = new();
		gallery.Publish(TestGallery.Index());
		gallery.Server.Serve(TestGallery.SignatureAddress, new byte[1025], hideLength: true);
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(() => client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct));

		Assert.Equal("The plugin list's signature is larger than 1 KB, so it was not read.", error.Message);
	}

	[Fact]
	public async Task ReadIndex_WhenTheHostFails_SaysWhatItAnswered()
	{
		TestGallery gallery = new();
		gallery.Server.Refuse(TestGallery.IndexAddress, HttpStatusCode.ServiceUnavailable);
		using PluginGalleryClient client = gallery.Client();

		PluginGalleryException error = await Assert.ThrowsAsync<PluginGalleryException>(() => client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct));

		Assert.Equal("gallery.test answered 503 (Service Unavailable) when asked for the plugin list.", error.Message);
	}

	[Fact]
	public async Task ReadIndex_SendsAUserAgentNamingMokaterm_AndNothingElse()
	{
		TestGallery gallery = new();
		gallery.Publish(TestGallery.Index());
		using PluginGalleryClient client = gallery.Client();

		await client.ReadIndexAsync(Host, Ct).WaitAsync(Limit, Ct);

		Assert.Equal([TestGallery.IndexAddress, TestGallery.SignatureAddress], gallery.Server.Requests.Select(request => request.RequestUri));
		foreach (HttpRequestMessage request in gallery.Server.Requests)
		{
			Assert.Equal(HttpMethod.Get, request.Method);
			Assert.Matches("^Mokaterm/[0-9]", request.Headers.UserAgent.ToString());
			Assert.Equal(["User-Agent"], request.Headers.Select(header => header.Key));
			Assert.Null(request.Content);
		}
	}

	[Fact]
	public async Task ReadIndex_CancelledByTheCaller_StopsAsCancelled()
	{
		TestGallery gallery = new();
		gallery.Publish(TestGallery.Index());
		using PluginGalleryClient client = gallery.Client();
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadIndexAsync(Host, cancelled.Token).WaitAsync(Limit, Ct));
	}

	[Fact]
	public void ProductionKey_IsThePublicHalfTheGalleryPublishes()
	{
		// signing/gallery-2026.pub.pem in the gallery repository: an Ed25519 SubjectPublicKeyInfo, whose last 32 bytes are
		// the raw key.
		byte[] pem = Convert.FromBase64String("MCowBQYDK2VwAyEAQJ26EO3DeujvVNqCciJKtByZJqouNos8pdQjpMqQZmE=");

		Assert.Equal(PluginGalleryKeys.Gallery2026, Convert.ToBase64String(pem[^32..]));
		Assert.Equal([PluginGalleryKeys.Gallery2026], PluginGalleryKeys.Production);
		Assert.Equal(PluginGalleryKeys.Production, new PluginGalleryOptions().TrustedKeys);
		Assert.Equal("https://jacobwi.github.io/mokaterm-plugins/index.json", new PluginGalleryOptions().Index.AbsoluteUri);
	}

	[Theory]
	[InlineData("http://gallery.test/index.json")]
	[InlineData("file:///C:/gallery/index.json")]
	public void Options_WithAnAddressThatIsNotHttps_AreRefused(string address)
	{
		PluginGalleryOptions options = new() { Index = new Uri(address) };

		Assert.Throws<ArgumentException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddPluginGallery(options));
	}

	[Theory]
	[InlineData("")]
	[InlineData("not base64")]
	[InlineData("AAAA")]
	public void Options_WithAKeyThatIsNotOne_AreRefused(string key)
	{
		PluginGalleryOptions options = new() { TrustedKeys = [key] };

		Assert.Throws<ArgumentException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddPluginGallery(options));
	}

	[Fact]
	public void Options_WithNoKeys_AreRefused()
	{
		PluginGalleryOptions options = new() { TrustedKeys = [] };

		Assert.Throws<ArgumentException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddPluginGallery(options));
	}

	[Fact]
	public async Task Download_ThatIsNotHttps_IsNeverFetched()
	{
		TestGallery gallery = new();
		using PluginGalleryClient client = gallery.Client();
		PluginGalleryVersion version = new()
		{
			Version = "1.0.0",
			Sdk = TestGallery.Sdk,
			Url = new Uri("http://downloads.test/acme.proxmox-1.0.0.mokaplugin"),
			Sha256 = new string('0', 64),
			Size = 10,
			Commit = TestGallery.Commit,
			Published = DateTimeOffset.UnixEpoch,
		};

		await Assert.ThrowsAsync<PluginGalleryException>(() => client.DownloadAsync(version, Ct).WaitAsync(Limit, Ct));

		Assert.Empty(gallery.Server.Requests);
	}

	[Fact]
	public void SignatureAddress_SitsBesideTheList()
	{
		using PluginGalleryClient client = new TestGallery().Client();

		Assert.Equal(TestGallery.SignatureAddress, client.Signature);
	}
}
