using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mokaterm.DevHost.Demo.Docker;

// The shop's config hashes, the way compose's config --hash works one out per service: here a hash of the service's
// block in the compose file the tree holds, so a file saved from the manager changes the hash of what it touched.
// The demo starts with shop-web-1 made from an older definition, so the compose pages have a service to mark.
internal sealed partial class DemoDockerEngine
{
	private const string ConfigHashLabel = "com.docker.compose.config-hash";
	private const string ComposeVersionLabel = "com.docker.compose.version";

	private static readonly string[] ShopServices = ["web", "api", "db"];

	private const string UnsetSmtpWarning = "time=\"2026-10-11T07:00:00Z\" level=warning msg=\"The \\\"SMTP_PASSWORD\\\" variable is not set. Defaulting to a blank string.\"\n";

	// The block under "  NAME:" in the services section, up to the next service or top-level key; null without one.
	internal static string? ServiceBlock(string compose, string service)
	{
		ArgumentNullException.ThrowIfNull(compose);
		bool inServices = false;
		bool inService = false;
		StringBuilder block = new();
		foreach (string raw in compose.ReplaceLineEndings("\n").Split('\n'))
		{
			string line = raw.TrimEnd();
			if (line.Length == 0)
			{
				continue;
			}

			bool topLevel = !char.IsWhiteSpace(line[0]);
			if (topLevel)
			{
				if (inService)
				{
					break;
				}

				inServices = line == "services:";
				continue;
			}

			bool serviceKey = inServices && line.StartsWith("  ", StringComparison.Ordinal) && !char.IsWhiteSpace(line[2]) && line.EndsWith(':');
			if (serviceKey)
			{
				if (inService)
				{
					break;
				}

				inService = line[2..^1] == service;
				continue;
			}

			if (inService)
			{
				block.Append(line.Trim()).Append('\n');
			}
		}

		return inService ? block.ToString() : null;
	}

	private string? ServiceHash(string project, string service) =>
		project == "shop" && _readFile(ShopFile) is { } compose && ServiceBlock(compose, service) is { } block
			? HexId($"compose shop/{service}\n{block}")
			: null;

	private static IReadOnlyDictionary<string, string> LabelsOf(DemoDockerContainer container)
	{
		if (container.ConfigHash.Length == 0)
		{
			return container.Labels;
		}

		Dictionary<string, string> labels = new(container.Labels, StringComparer.Ordinal)
		{
			[ConfigHashLabel] = container.ConfigHash,
			[ComposeVersionLabel] = ComposeVersion,
		};
		return labels;
	}

	// The hash compose would stamp now, or for web at the start, the one an older definition left.
	private string ShopHash(string service, bool fresh) =>
		!fresh && service == "web" ? HexId("compose shop/web before web.env") : ServiceHash("shop", service) ?? "";

	private DemoDockerResult ConfigHashes()
	{
		string compose = _readFile(ShopFile) ?? "";
		StringBuilder output = new();
		foreach (string service in ShopServices.Where(name => ServiceBlock(compose, name) is not null).Order(StringComparer.Ordinal))
		{
			output.Append(CultureInfo.InvariantCulture, $"{service} {ServiceHash("shop", service)}\n");
		}

		return new DemoDockerResult(0, output.ToString(), UnsetSmtpWarning);
	}

	// config --no-interpolate --format json, cut to what the manager reads: which variables each service names.
	private DemoDockerResult ConfigRaw()
	{
		string compose = _readFile(ShopFile) ?? "";
		Dictionary<string, object> services = new(StringComparer.Ordinal);
		foreach (string service in ShopServices)
		{
			if (ServiceBlock(compose, service) is { } block)
			{
				services[service] = new Dictionary<string, string> { ["x-demo-definition"] = block };
			}
		}

		return DemoDockerResult.Ok(JsonSerializer.Serialize(new { name = "shop", services }) + "\n");
	}

	// up makes a container again when its hash differs from what the files say now, as compose does.
	private bool IsOutOfDate(DemoDockerContainer container) =>
		container.Project is { } project
		&& container.Service is { } service
		&& ServiceHash(project, service) is { } hash
		&& !string.Equals(hash, container.ConfigHash, StringComparison.Ordinal);

	private void Recreate(DemoDockerContainer container)
	{
		if (container.Project is { } project && container.Service is { } service && ServiceHash(project, service) is { } hash)
		{
			container.ConfigHash = hash;
		}

		Start(container);
	}
}
