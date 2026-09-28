using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Vnc;

/// <summary>Typed access to the <c>vnc.*</c> keys a VNC connection keeps in its <see cref="ProtocolOptions"/>.</summary>
public sealed record VncConnectionOptions
{
	public const string SharedKey = "vnc.shared";

	public const string ViewOnlyKey = "vnc.viewOnly";

	public const string ScalingKey = "vnc.scaling";

	public const string QualityKey = "vnc.quality";

	public const string CompressionKey = "vnc.compression";

	public const string EncryptionKey = "vnc.encryption";

	public const int MinLevel = 0;

	public const int MaxLevel = 9;

	/// <summary>noVNC's default JPEG quality for the Tight encoding.</summary>
	public const int DefaultQuality = 6;

	/// <summary>noVNC's default zlib compression level for the Tight encoding.</summary>
	public const int DefaultCompression = 2;

	public static VncConnectionOptions Default { get; } = new();

	/// <summary>Lets other clients keep their connection to the same screen. Off means the server may disconnect them.</summary>
	public bool Shared { get; init; } = true;

	/// <summary>Watch without sending keys or mouse events. The view can still turn input back on.</summary>
	public bool ViewOnly { get; init; }

	public VncScalingMode Scaling { get; init; } = VncScalingMode.Fit;

	/// <summary>0 (smallest, blurry) to 9 (sharpest). Only servers using the Tight encoding act on it.</summary>
	public int Quality { get; init; } = DefaultQuality;

	/// <summary>0 (least CPU) to 9 (smallest data).</summary>
	public int Compression { get; init; } = DefaultCompression;

	public VncEncryptionMode Encryption { get; init; } = VncEncryptionMode.Preferred;

	public static VncConnectionOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return new VncConnectionOptions
		{
			Shared = options.GetBoolean(SharedKey, true),
			ViewOnly = options.GetBoolean(ViewOnlyKey, false),
			Scaling = options.GetEnum(ScalingKey, VncScalingMode.Fit),
			Quality = Clamp(options.GetInt32(QualityKey, DefaultQuality)),
			Compression = Clamp(options.GetInt32(CompressionKey, DefaultCompression)),
			Encryption = options.GetEnum(EncryptionKey, VncEncryptionMode.Preferred),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options
			.With(SharedKey, Shared ? null : "false")
			.With(ViewOnlyKey, ViewOnly ? "true" : null)
			.WithEnum<VncScalingMode>(ScalingKey, Scaling)
			.With(QualityKey, Clamp(Quality))
			.With(CompressionKey, Clamp(Compression))
			.WithEnum<VncEncryptionMode>(EncryptionKey, Encryption);
	}

	private static int Clamp(int level) => Math.Clamp(level, MinLevel, MaxLevel);
}
