using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class RemoteOutputTests
{
	[Fact]
	public void Require_PartialListingWithEntries_IsKept()
	{
		byte[] output = Output("G", "f", "1", "1694959123.0000000000", "644", "root", "root", "", "", "readable.txt");

		ReadOnlyMemory<byte> payload = RemoteOutput.Require(1, output, "find: './locked': Permission denied", "/srv", allowPartialListing: true);

		Assert.Single(RemoteListingParser.Parse(payload.Span, "/srv", isStat: false));
	}

	[Fact]
	public void Require_ListingWithOnlyItsTag_IsRefusedAsUnreadable()
	{
		byte[] output = Output("G");

		RemoteFileSystemException error = Assert.Throws<RemoteFileSystemException>(() =>
			RemoteOutput.Require(1, output, "find: '.': Permission denied", "/root", allowPartialListing: true));

		Assert.Equal(RemoteFileErrorKind.PermissionDenied, error.Kind);
	}

	[Fact]
	public void Require_EmptyFolder_IsAnEmptyListing()
	{
		byte[] output = Output("G");

		ReadOnlyMemory<byte> payload = RemoteOutput.Require(0, output, "", "/srv/empty", allowPartialListing: true);

		Assert.Empty(RemoteListingParser.Parse(payload.Span, "/srv/empty", isStat: false));
	}

	private static byte[] Output(params string[] fields)
	{
		StringBuilder builder = new("MOKATERM-OUT\0");
		foreach (string field in fields)
		{
			builder.Append(field).Append('\0');
		}

		return Encoding.UTF8.GetBytes(builder.ToString());
	}
}
