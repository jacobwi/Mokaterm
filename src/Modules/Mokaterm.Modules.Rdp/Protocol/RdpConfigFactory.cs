using Devolutions.IronRdp;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>Builds the IronRDP <see cref="Config"/> for one connection attempt.</summary>
internal static class RdpConfigFactory
{
	/// <summary>What the server shows in its session list. RDP has a 15 character limit on it.</summary>
	public const string ClientName = "Mokaterm";

	/// <summary>
	/// The client build number RDP clients report. 7601 is the Windows 7 SP1 build every server still accepts, and
	/// claiming a newer one would promise features this client does not have.
	/// </summary>
	public const uint ClientBuild = 7601;

	/// <param name="credssp">
	/// False turns network level authentication off, which sends no credentials at all and leaves the login to the
	/// server's own screen.
	/// </param>
	public static Config Build(RdpConnectionOptions options, RdpLogin login, int width, int height, bool credssp)
	{
		ArgumentNullException.ThrowIfNull(options);
		ConfigBuilder builder = ConfigBuilder.New();
		try
		{
			// The signature takes the height first.
			builder.SetDesktopSize((ushort)Math.Clamp(height, RdpConnectionOptions.MinDesktopSize, RdpConnectionOptions.MaxDesktopSize),
				(ushort)Math.Clamp(width, RdpConnectionOptions.MinDesktopSize, RdpConnectionOptions.MaxDesktopSize));
			builder.SetClientName(ClientName);
			builder.SetClientDir("C:\\");
			builder.SetClientBuild(ClientBuild);
			builder.SetEnableTls(true);
			builder.SetEnableCredssp(credssp);
			builder.SetAutologon(credssp);

			// The server sends pointer shapes, which the view turns into a real cursor. Software rendering would
			// instead burn the pointer into the picture, and it would lag one frame behind the mouse.
			builder.SetEnableServerPointer(true);
			builder.SetPointerSoftwareRendering(false);

			if (options.KeyboardLayout != RdpKeyboardLayout.ServerDefault)
			{
				builder.SetKeyboardLayout((uint)options.KeyboardLayout);
			}

			builder.SetKeyboardType(KeyboardType.IbmEnhanced);
			builder.SetKeyboardFunctionalKeysCount(12);

			if (login.Domain.Length > 0)
			{
				builder.SetDomain(login.Domain);
			}

			// Build refuses a config with no credentials at all, even when nothing will be sent.
			builder.WithUsernameAndPassword(login.Username, login.Password);

			PerformanceFlags flags = BuildPerformanceFlags(options.Performance);
			try
			{
				builder.SetPerformanceFlags(flags);
				return builder.Build();
			}
			finally
			{
				flags.Dispose();
			}
		}
		finally
		{
			builder.Dispose();
		}
	}

	private static PerformanceFlags BuildPerformanceFlags(RdpPerformanceOptions performance)
	{
		PerformanceFlags flags = PerformanceFlags.NewEmpty();
		try
		{
			if (!performance.Wallpaper)
			{
				flags.AddFlag(PerformanceFlagsType.DisableWallpaper);
			}

			if (!performance.Themes)
			{
				flags.AddFlag(PerformanceFlagsType.DisableTheming);
				flags.AddFlag(PerformanceFlagsType.DisableMenuAnimations);
			}

			if (performance.FontSmoothing)
			{
				flags.AddFlag(PerformanceFlagsType.EnableFontSmoothing);
			}

			if (!performance.FullWindowDrag)
			{
				flags.AddFlag(PerformanceFlagsType.DisableFullWindowDrag);
			}

			return flags;
		}
		catch
		{
			flags.Dispose();
			throw;
		}
	}
}
