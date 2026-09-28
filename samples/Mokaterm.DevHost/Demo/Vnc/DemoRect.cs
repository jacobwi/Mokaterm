namespace Mokaterm.DevHost.Demo.Vnc;

/// <summary>A rectangle of the demo screen, in pixels.</summary>
internal readonly record struct DemoRect(int X, int Y, int Width, int Height)
{
	public int Right => X + Width;

	public int Bottom => Y + Height;

	public bool IsEmpty => Width <= 0 || Height <= 0;

	/// <summary>The smallest rectangle holding both.</summary>
	public DemoRect Union(DemoRect other)
	{
		if (IsEmpty)
		{
			return other;
		}

		if (other.IsEmpty)
		{
			return this;
		}

		int x = Math.Min(X, other.X);
		int y = Math.Min(Y, other.Y);
		return new DemoRect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
	}

	/// <summary>The part of this rectangle inside a screen of <paramref name="width"/> by <paramref name="height"/>.</summary>
	public DemoRect Clamp(int width, int height)
	{
		int x = Math.Clamp(X, 0, width);
		int y = Math.Clamp(Y, 0, height);
		return new DemoRect(x, y, Math.Clamp(Right, x, width) - x, Math.Clamp(Bottom, y, height) - y);
	}
}
