using Microsoft.JSInterop;

namespace Mokaterm.UI.Common.Interop;

/// <summary>
/// A read stream over a file the page handed out, dropped or picked, that releases the page's reference to the file when
/// disposed. Without that the page keeps every file ever read alive for as long as the circuit lasts.
/// </summary>
public sealed class JsStreamReferenceStream : Stream
{
	private readonly Stream _inner;
	private readonly long _length;
	private IJSStreamReference? _reference;

	public JsStreamReferenceStream(Stream inner, IJSStreamReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		_inner = inner;
		_reference = reference;
		_length = reference.Length;
	}

	/// <summary>Opens <paramref name="reference"/> for reading. The stream owns the reference from then on.</summary>
	public static async ValueTask<Stream> OpenAsync(IJSStreamReference reference, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(reference);
		try
		{
			Stream stream = await reference.OpenReadStreamAsync(long.MaxValue, cancellationToken);
			return new JsStreamReferenceStream(stream, reference);
		}
		catch
		{
			await ReleaseAsync(reference);
			throw;
		}
	}

	public override bool CanRead => _inner.CanRead;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	/// <summary>The browser reports the file size up front, so it is known although the stream cannot seek.</summary>
	public override long Length => _length;

	public override long Position
	{
		get => _inner.Position;
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

	public override int Read(Span<byte> buffer) => _inner.Read(buffer);

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		_inner.ReadAsync(buffer, offset, count, cancellationToken);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
		_inner.ReadAsync(buffer, cancellationToken);

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override async ValueTask DisposeAsync()
	{
		await _inner.DisposeAsync();
		await ReleaseAsync(Interlocked.Exchange(ref _reference, null));
		await base.DisposeAsync();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_inner.Dispose();
			_ = ReleaseAsync(Interlocked.Exchange(ref _reference, null));
		}

		base.Dispose(disposing);
	}

	private static async Task ReleaseAsync(IJSStreamReference? reference)
	{
		if (reference is null)
		{
			return;
		}

		try
		{
			await reference.DisposeAsync();
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// The page is gone, and the file reference with it.
		}
	}
}
