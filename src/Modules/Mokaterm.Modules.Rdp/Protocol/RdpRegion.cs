namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>A rectangle of the desktop, in pixels, with the right and bottom edges outside it.</summary>
internal readonly record struct RdpRegion(int X, int Y, int Width, int Height)
{
	public int Right => X + Width;

	public int Bottom => Y + Height;

	public long Area => (long)Width * Height;

	public bool IsEmpty => Width <= 0 || Height <= 0;

	/// <summary>The smallest rectangle holding both. An empty one contributes nothing.</summary>
	public RdpRegion Union(RdpRegion other)
	{
		if (other.IsEmpty)
		{
			return this;
		}

		if (IsEmpty)
		{
			return other;
		}

		int left = Math.Min(X, other.X);
		int top = Math.Min(Y, other.Y);
		int right = Math.Max(Right, other.Right);
		int bottom = Math.Max(Bottom, other.Bottom);
		return new RdpRegion(left, top, right - left, bottom - top);
	}

	/// <summary>The part of this rectangle inside a desktop of the given size, or an empty one when none of it is.</summary>
	public RdpRegion Clip(int width, int height)
	{
		int left = Math.Clamp(X, 0, width);
		int top = Math.Clamp(Y, 0, height);
		int right = Math.Clamp(Right, left, width);
		int bottom = Math.Clamp(Bottom, top, height);
		return right > left && bottom > top ? new RdpRegion(left, top, right - left, bottom - top) : default;
	}
}
