using Devolutions.IronRdp;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// Exercises the native IronRDP input path: an operation built here has to be something the library's input
/// database accepts, which no amount of managed testing above it would prove.
/// </summary>
public sealed class RdpOperationHandleTests
{
	[Fact]
	public void Create_ProducesOperationsTheInputDatabaseTakes()
	{
		RdpOperation[] operations =
		[
			new(RdpOperationKind.MouseMove, 100, 200),
			new(RdpOperationKind.MouseDown, 0),
			new(RdpOperationKind.MouseUp, 4),
			new(RdpOperationKind.Wheel, 1, 120),
			new(RdpOperationKind.Wheel, 0, -120),
			new(RdpOperationKind.KeyDown, 0x1E),
			new(RdpOperationKind.KeyUp, 0xE04B),
		];

		using InputDatabase input = InputDatabase.New();

		foreach (RdpOperation operation in operations)
		{
			using RdpOperationHandle handle = RdpOperationHandle.Create(operation);
			using FastPathInputEventIterator events = input.Apply(handle.Operation);
			Assert.NotNull(events);
		}
	}

	[Fact]
	public void Create_EveryMappedScancode_IsAcceptedAsAKeyPress()
	{
		using InputDatabase input = InputDatabase.New();

		foreach (string code in RdpKeyboardMap.KnownCodes)
		{
			Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode scancode));
			using RdpOperationHandle down = RdpOperationHandle.Create(new RdpOperation(RdpOperationKind.KeyDown, scancode.Value));
			input.Apply(down.Operation).Dispose();
			using RdpOperationHandle up = RdpOperationHandle.Create(new RdpOperation(RdpOperationKind.KeyUp, scancode.Value));
			input.Apply(up.Operation).Dispose();
		}
	}

	[Fact]
	public void Create_RejectsAButtonRdpDoesNotHave() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => RdpOperationHandle.Create(new RdpOperation(RdpOperationKind.MouseDown, 9)));

	[Fact]
	public void Create_RejectsAnUnknownKind() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => RdpOperationHandle.Create(new RdpOperation((RdpOperationKind)99, 0)));

	[Fact]
	public void Create_ClampsAPositionThatWouldNotFit()
	{
		using InputDatabase input = InputDatabase.New();

		using RdpOperationHandle handle = RdpOperationHandle.Create(new RdpOperation(RdpOperationKind.MouseMove, 1_000_000, -5));
		using FastPathInputEventIterator events = input.Apply(handle.Operation);

		Assert.NotNull(events);
	}

	[Fact]
	public void DecodedImage_IsFourBytesPerPixelInTheFormatTheModuleUses()
	{
		using DecodedImage image = DecodedImage.New(PixelFormat.RgbA32, 32, 16);
		using BytesSlice data = image.GetData();

		Assert.Equal(32 * 16 * PngWriter.BytesPerPixel, (int)data.GetSize());
	}

	[Fact]
	public void ConfigFactory_BuildsAConfigForALoginAndForNoLoginAtAll()
	{
		using Config withLogin = RdpConfigFactory.Build(
			RdpConnectionOptions.Default,
			new RdpLogin("CORP", "alice", "secret"),
			1280,
			800,
			credssp: true);

		using Config withoutLogin = RdpConfigFactory.Build(
			RdpConnectionOptions.Default with { KeyboardLayout = 0x0409 },
			new RdpLogin("", "", ""),
			1920,
			1080,
			credssp: false);

		Assert.NotNull(withLogin);
		Assert.NotNull(withoutLogin);
	}

	[Fact]
	public void ConfigFactory_ClampsADesktopSizeOutsideWhatRdpNegotiates()
	{
		using Config config = RdpConfigFactory.Build(RdpConnectionOptions.Default, new RdpLogin("", "a", "b"), 0, 999_999, credssp: false);

		Assert.NotNull(config);
	}

	[Fact]
	public void ConfigFactory_AcceptsEveryPerformanceCombination()
	{
		foreach (bool wallpaper in (bool[])[false, true])
		{
			foreach (bool themes in (bool[])[false, true])
			{
				RdpConnectionOptions options = RdpConnectionOptions.Default with
				{
					Performance = new RdpPerformanceOptions
					{
						Wallpaper = wallpaper,
						Themes = themes,
						FontSmoothing = !wallpaper,
						FullWindowDrag = !themes,
					},
				};

				using Config config = RdpConfigFactory.Build(options, new RdpLogin("", "a", "b"), 800, 600, credssp: true);
				Assert.NotNull(config);
			}
		}
	}
}
