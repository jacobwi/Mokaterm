using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Plugins;
using Mokaterm.Core.Plugins.Gallery;
using BouncyEd25519 = Org.BouncyCastle.Math.EC.Rfc8032.Ed25519;

namespace Mokaterm.Plugins.Tests;

/// <summary>
/// The sample package as the gallery hands it over, through public types only: a signed list names it, the gallery
/// downloads and checks it, and the manager installs it and loads it at the next start exactly as it does a picked file.
/// </summary>
public sealed class GalleryInstallTests : IDisposable
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

	private static readonly Uri IndexAddress = new("https://gallery.test/index.json");

	private static readonly Uri PackageAddress = new("https://downloads.test/moka.sample-1.0.0.mokaplugin");

	private static readonly string[] Categories = ["utilities"];

	private static readonly string[] Hosts = [PluginHosts.Desktop, PluginHosts.Web];

	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mokaterm-plugin-tests", Guid.NewGuid().ToString("N"));

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SamplePackage_FromASignedList_InstallsAndLoadsLikeAFile()
	{
		byte[] package = SamplePluginPackage.ReadAllBytes();
		byte[] secret = RandomNumberGenerator.GetBytes(BouncyEd25519.SecretKeySize);
		byte[] publicKey = new byte[BouncyEd25519.PublicKeySize];
		BouncyEd25519.GeneratePublicKey(secret, 0, publicKey, 0);
		byte[] index = List(package);
		byte[] signature = new byte[BouncyEd25519.SignatureSize];
		BouncyEd25519.Sign(secret, 0, index, 0, index.Length, signature, 0);
		Dictionary<Uri, byte[]> files = new()
		{
			[IndexAddress] = index,
			[new Uri(IndexAddress.AbsoluteUri + ".sig")] = Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n"),
			[PackageAddress] = package,
		};

		PluginCatalog first = Open();
		first.ConfirmStarted();
		ServiceCollection services = new();
		services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(_folder));
		new MokatermBuilder(services).AddPlugins(first);
		services.AddPluginGallery(
			new PluginGalleryOptions { Index = IndexAddress, TrustedKeys = [Convert.ToBase64String(publicKey)] },
			() => new FileServer(files));
		await using (ServiceProvider provider = services.BuildServiceProvider())
		{
			await using AsyncServiceScope window = provider.CreateAsyncScope();
			IPluginGallery gallery = window.ServiceProvider.GetRequiredService<IPluginGallery>();
			IPluginManager manager = window.ServiceProvider.GetRequiredService<IPluginManager>();

			PluginGalleryEntry entry = (await gallery.ReadIndexAsync(refresh: false, Ct).WaitAsync(Limit, Ct)).Find(SamplePluginPackage.Id)!;
			using IPluginPackage read = await gallery.DownloadAsync(entry, entry.Newest!, Ct).WaitAsync(Limit, Ct);

			Assert.Equal(SamplePluginPackage.Id, read.Id);
			Assert.True(read.Origin!.Verified);
			Assert.Null(read.Problem);
			await manager.InstallAsync(read, enable: true, Ct).WaitAsync(Limit, Ct);
		}

		PluginCatalog next = Open();
		next.ConfirmStarted();

		Assert.Equal(PluginStatus.Enabled, Assert.Single(next.Records).Status);
		Assert.Equal(SamplePluginPackage.AssemblyName, Assert.Single(next.Loaded).Info.AssemblyName);
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_folder))
			{
				Directory.Delete(_folder, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The loaded assembly keeps its file open until the process ends; the temp folder can wait.
		}
	}

	private static byte[] List(byte[] package)
	{
		using ZipArchiveReader manifest = new(package);
		return JsonSerializer.SerializeToUtf8Bytes(new
		{
			format = 1,
			generated = "2026-10-13T09:00:00Z",
			plugins = new[]
			{
				new
				{
					id = SamplePluginPackage.Id,
					name = "Sample plugin",
					publisher = "Moka",
					description = "The sample.",
					categories = Categories,
					source = "https://github.com/jacobwi/Mokaterm",
					license = "MIT",
					verified = true,
					hasCode = true,
					hosts = Hosts,
					versions = new[]
					{
						new
						{
							version = manifest.Version,
							sdk = manifest.Sdk,
							url = PackageAddress.AbsoluteUri,
							sha256 = Convert.ToHexStringLower(SHA256.HashData(package)),
							size = package.Length,
							commit = new string('a', 40),
							published = "2026-10-13T09:00:00Z",
							withdrawn = (string?)null,
						},
					},
				},
			},
		});
	}

	private PluginCatalog Open() => PluginCatalog.Open(new PluginCatalogOptions { Directory = _folder, Host = HostKind.Desktop, UserManaged = true });

	/// <summary>The version and sdk the sample's own <c>plugin.json</c> names, which the list has to repeat.</summary>
	private sealed class ZipArchiveReader : IDisposable
	{
		private readonly JsonDocument _manifest;

		public ZipArchiveReader(byte[] package)
		{
			using System.IO.Compression.ZipArchive zip = new(new MemoryStream(package), System.IO.Compression.ZipArchiveMode.Read);
			using Stream manifest = zip.GetEntry("plugin.json")!.Open();
			_manifest = JsonDocument.Parse(manifest);
		}

		public string Version => _manifest.RootElement.GetProperty("version").GetString()!;

		public string Sdk => _manifest.RootElement.GetProperty("sdk").GetString()!;

		public void Dispose() => _manifest.Dispose();
	}

	/// <summary>Answers from a table of files, as GitHub Pages and Releases would.</summary>
	private sealed class FileServer(IReadOnlyDictionary<Uri, byte[]> files) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(files.TryGetValue(request.RequestUri!, out byte[]? body)
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body), RequestMessage = request }
				: new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
	}
}
