using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpInputTranslatorTests
{
	private static RdpInputTranslator Translator() => new(1024, 768);

	[Fact]
	public void Translate_MouseMove_SendsThePosition()
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.MouseMove(10, 20)]);

		RdpOperation only = Assert.Single(operations);
		Assert.Equal(new RdpOperation(RdpOperationKind.MouseMove, 10, 20), only);
	}

	[Fact]
	public void Translate_TheSamePositionTwice_SendsItOnce()
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate(
		[
			RdpInputEvent.MouseMove(10, 20),
			RdpInputEvent.MouseMove(10, 20),
			RdpInputEvent.MouseMove(11, 20),
		]);

		Assert.Equal(2, operations.Count);
	}

	[Fact]
	public void Translate_ClampsAPositionOutsideTheDesktop()
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.MouseMove(5000, -4)]);

		Assert.Equal(new RdpOperation(RdpOperationKind.MouseMove, 1023, 0), Assert.Single(operations));
	}

	[Fact]
	public void Resize_ClampsThePointerIntoTheSmallerDesktop()
	{
		RdpInputTranslator translator = Translator();
		translator.Translate([RdpInputEvent.MouseMove(1000, 700)]);

		translator.Resize(640, 480);

		// The remembered position moved with the desktop, so an event that lands on it again says nothing new.
		Assert.Empty(translator.Translate([RdpInputEvent.MouseMove(1000, 700)]));
		Assert.Equal(new RdpOperation(RdpOperationKind.MouseMove, 100, 100), Assert.Single(translator.Translate([RdpInputEvent.MouseMove(100, 100)])));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	public void Translate_PassesEveryButtonThrough(int button)
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.MouseDown(button), RdpInputEvent.MouseUp(button)]);

		Assert.Equal(
		[
			new RdpOperation(RdpOperationKind.MouseDown, button),
			new RdpOperation(RdpOperationKind.MouseUp, button),
		], operations);
	}

	[Theory]
	[InlineData(5)]
	[InlineData(-1)]
	public void Translate_DropsAButtonRdpDoesNotHave(int button)
	{
		RdpInputTranslator translator = Translator();

		Assert.Empty(translator.Translate([RdpInputEvent.MouseDown(button)]));
	}

	[Theory]
	[InlineData(RdpInputTranslator.WheelNotch, -RdpInputTranslator.WheelNotch)]
	[InlineData(-RdpInputTranslator.WheelNotch, RdpInputTranslator.WheelNotch)]
	public void Translate_VerticalWheel_TurnsThePageDirectionAround(int page, int rdp)
	{
		// The page scrolls down for a positive deltaY; Windows scrolls down for a negative MOUSEEVENTF_WHEEL.
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.Wheel(vertical: true, page)]);

		Assert.Equal(new RdpOperation(RdpOperationKind.Wheel, 1, rdp), Assert.Single(operations));
	}

	[Theory]
	[InlineData(RdpInputTranslator.WheelNotch)]
	[InlineData(-RdpInputTranslator.WheelNotch)]
	[InlineData(240)]
	[InlineData(-240)]
	public void Translate_HorizontalWheel_KeepsThePageDirection(int page)
	{
		// The page scrolls right for a positive deltaX, and so does Windows for a positive MOUSEEVENTF_HWHEEL, the
		// way FreeRDP's clients send it (X11 button 7 is a positive PTR_FLAGS_HWHEEL).
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.Wheel(vertical: false, page)]);

		Assert.Equal(new RdpOperation(RdpOperationKind.Wheel, 0, page), Assert.Single(operations));
	}

	[Theory]
	[InlineData(true, 600, new[] { -255, -255, -90 })]
	[InlineData(true, -600, new[] { 255, 255, 90 })]
	[InlineData(false, 600, new[] { 255, 255, 90 })]
	[InlineData(false, -600, new[] { -255, -255, -90 })]
	[InlineData(false, 255, new[] { 255 })]
	[InlineData(false, 256, new[] { 255, 1 })]
	public void Translate_ATurnTooBigForOneEvent_GoesAsSeveral(bool vertical, int page, int[] expected)
	{
		// The rotation is nine bits of two's complement, and IronRDP would keep only the low eight of 600.
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.Wheel(vertical, page)]);

		Assert.Equal(expected, operations.Select(operation => operation.B));
		Assert.All(operations, operation => Assert.Equal(vertical ? 1 : 0, operation.A));
	}

	[Theory]
	[InlineData(true, int.MaxValue)]
	[InlineData(true, int.MinValue)]
	[InlineData(false, int.MaxValue)]
	[InlineData(false, int.MinValue)]
	public void Translate_AnAbsurdTurn_IsCappedAtWhatOneEventMayAskFor(bool vertical, int page)
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.Wheel(vertical, page)]);

		Assert.Equal(RdpInputTranslator.MaxWheelUnits, Math.Abs(operations.Sum(operation => operation.B)));
		Assert.All(operations, operation => Assert.InRange(Math.Abs(operation.B), 1, RdpInputTranslator.MaxUnitsPerEvent));
	}

	[Fact]
	public void Translate_DropsAWheelEventOfNothing() =>
		Assert.Empty(Translator().Translate([RdpInputEvent.Wheel(vertical: true, 0)]));

	[Fact]
	public void Translate_KeyDown_SendsTheScancode()
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.KeyDown("KeyA")]);

		Assert.Equal(new RdpOperation(RdpOperationKind.KeyDown, 0x1E), Assert.Single(operations));
	}

	[Fact]
	public void Translate_DropsAKeyWithNoScancode()
	{
		RdpInputTranslator translator = Translator();

		Assert.Empty(translator.Translate([RdpInputEvent.KeyDown("AudioVolumeUp"), RdpInputEvent.KeyUp("AudioVolumeUp")]));
		Assert.Empty(translator.HeldKeys);
	}

	[Fact]
	public void HeldKeys_FollowsWhatIsPressedAndReleased()
	{
		RdpInputTranslator translator = Translator();

		translator.Translate([RdpInputEvent.KeyDown("ControlLeft"), RdpInputEvent.KeyDown("KeyC")]);
		Assert.Equal(2, translator.HeldKeys.Count);

		translator.Translate([RdpInputEvent.KeyUp("KeyC")]);
		Assert.Equal(new RdpScancode(false, 0x1D), Assert.Single(translator.HeldKeys));
	}

	[Fact]
	public void Translate_ARepeatedKeyDown_IsHeldOnlyOnce()
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.KeyDown("KeyA"), RdpInputEvent.KeyDown("KeyA")]);

		// Both go to the server, because auto repeat is a real key press, but only one key is held.
		Assert.Equal(2, operations.Count);
		Assert.Single(translator.HeldKeys);
	}

	[Fact]
	public void ReleaseKeys_LetsGoOfEverythingNewestFirst()
	{
		RdpInputTranslator translator = Translator();
		translator.Translate([RdpInputEvent.KeyDown("ControlLeft"), RdpInputEvent.KeyDown("AltLeft"), RdpInputEvent.KeyDown("Delete")]);

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.ReleaseKeys()]);

		Assert.Equal(
		[
			new RdpOperation(RdpOperationKind.KeyUp, 0xE053),
			new RdpOperation(RdpOperationKind.KeyUp, 0x38),
			new RdpOperation(RdpOperationKind.KeyUp, 0x1D),
		], operations);
		Assert.Empty(translator.HeldKeys);
	}

	[Fact]
	public void ReleaseKeys_AlsoLetsGoOfHeldButtons()
	{
		RdpInputTranslator translator = Translator();
		translator.Translate([RdpInputEvent.MouseDown(0), RdpInputEvent.MouseDown(2)]);

		IReadOnlyList<RdpOperation> operations = translator.Translate([RdpInputEvent.ReleaseKeys()]);

		Assert.Equal(
		[
			new RdpOperation(RdpOperationKind.MouseUp, 2),
			new RdpOperation(RdpOperationKind.MouseUp, 0),
		], operations);
		Assert.Empty(translator.HeldButtons);
	}

	[Fact]
	public void ReleaseKeys_Twice_SendsNothingTheSecondTime()
	{
		RdpInputTranslator translator = Translator();
		translator.Translate([RdpInputEvent.KeyDown("ShiftLeft")]);

		Assert.Single(translator.Translate([RdpInputEvent.ReleaseKeys()]));
		Assert.Empty(translator.Translate([RdpInputEvent.ReleaseKeys()]));
	}

	[Fact]
	public void ReleaseEverything_ReleasesButtonsBeforeKeys()
	{
		RdpInputTranslator translator = Translator();
		translator.Translate([RdpInputEvent.KeyDown("ControlLeft"), RdpInputEvent.MouseDown(0)]);

		IReadOnlyList<RdpOperation> operations = translator.ReleaseEverything();

		Assert.Equal(
		[
			new RdpOperation(RdpOperationKind.MouseUp, 0),
			new RdpOperation(RdpOperationKind.KeyUp, 0x1D),
		], operations);
	}

	[Fact]
	public void PointerMovedByServer_MakesTheNextIdenticalMoveRedundant()
	{
		RdpInputTranslator translator = Translator();

		translator.PointerMovedByServer(300, 400);

		Assert.Empty(translator.Translate([RdpInputEvent.MouseMove(300, 400)]));
		Assert.Single(translator.Translate([RdpInputEvent.MouseMove(301, 400)]));
	}

	[Fact]
	public void Translate_KeepsTheOrderItWasGiven()
	{
		RdpInputTranslator translator = Translator();

		IReadOnlyList<RdpOperation> operations = translator.Translate(
		[
			RdpInputEvent.MouseMove(5, 5),
			RdpInputEvent.MouseDown(0),
			RdpInputEvent.MouseUp(0),
		]);

		Assert.Equal(
		[
			new RdpOperation(RdpOperationKind.MouseMove, 5, 5),
			new RdpOperation(RdpOperationKind.MouseDown, 0),
			new RdpOperation(RdpOperationKind.MouseUp, 0),
		], operations);
	}

	[Fact]
	public void Translate_RejectsANullList() =>
		Assert.Throws<ArgumentNullException>(() => Translator().Translate(null!));
}
