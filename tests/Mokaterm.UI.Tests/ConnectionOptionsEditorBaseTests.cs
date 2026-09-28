using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The draft cycle every protocol's options editor shares. The record the fields read is replaced before the change is
/// reported, so a field shows what was just picked: the SSH editor reported first and kept showing the old value until
/// the connection editor handed the options back.
/// </summary>
public sealed class ConnectionOptionsEditorBaseTests
{
	[Fact]
	public async Task AChangedField_ShowsAtOnceEvenWhenTheParentNeverAnswers()
	{
		TestEditor editor = new();
		editor.Apply(ProtocolOptions.Empty.With("demo.name", "first"));

		Assert.Equal("first", editor.Visible.Name);

		await editor.ChangeNameAsync("second");

		// Nothing was handed back, and the field already shows the new name.
		Assert.Equal("second", editor.Visible.Name);
		Assert.Equal("second", editor.Reported?.GetString("demo.name"));
	}

	[Fact]
	public async Task TheOptionsHandedIn_WinOverWhatWasReported()
	{
		TestEditor editor = new();
		editor.Apply(ProtocolOptions.Empty.With("demo.name", "first"));
		await editor.ChangeNameAsync("second");

		// The connection editor owns the options: a login reloaded under this editor replaces the draft.
		editor.Apply(ProtocolOptions.Empty.With("demo.name", "third"));

		Assert.Equal("third", editor.Visible.Name);
	}

	[Fact]
	public async Task ADerivedValue_IsRecomputedOnBothPaths()
	{
		TestEditor editor = new();
		editor.Apply(ProtocolOptions.Empty.With("demo.name", "first"));

		Assert.Equal(1, editor.Recomputed);

		await editor.ChangeNameAsync("second");

		Assert.Equal(2, editor.Recomputed);
	}

	[Fact]
	public async Task TheDefaultPort_IsReportedOnlyWhenItChanges()
	{
		TestEditor editor = new();
		editor.Apply(ProtocolOptions.Empty.With("demo.port", "990"));

		Assert.Equal(new int?[] { 990 }, editor.Ports);

		// The editor above renders for every report, which brings the options back here: only a change may go out.
		editor.Apply(ProtocolOptions.Empty.With("demo.port", "990"));
		await editor.ChangeNameAsync("second");

		Assert.Equal(new int?[] { 990 }, editor.Ports);

		editor.Apply(ProtocolOptions.Empty);

		Assert.Equal(new int?[] { 990, null }, editor.Ports);
	}

	/// <summary>A module's typed options record, standing in for the seven real ones.</summary>
	private sealed record DemoOptions
	{
		public string Name { get; init; } = "";

		public int? Port { get; init; }

		public static DemoOptions From(ProtocolOptions options) => new()
		{
			Name = options.GetString("demo.name") ?? "",
			Port = options.GetString("demo.port") is { } port ? int.Parse(port, CultureInfo.InvariantCulture) : null,
		};

		public ProtocolOptions ApplyTo(ProtocolOptions options) => options.With("demo.name", Name).With("demo.port", Port);
	}

	private sealed class TestEditor : ConnectionOptionsEditorBase<DemoOptions>
	{
		private readonly List<int?> _ports = [];

		public DemoOptions Visible => Current;

		public ProtocolOptions? Reported { get; private set; }

		public IReadOnlyList<int?> Ports => _ports;

		public int Recomputed { get; private set; }

		/// <summary>Hands the editor its parameters, the way a render does.</summary>
		public void Apply(ProtocolOptions options)
		{
			Options = options;
			OptionsChanged = new EventCallback<ProtocolOptions>(null, (Action<ProtocolOptions>)(reported => Reported = reported));
			DefaultPortChanged = new EventCallback<int?>(null, (Action<int?>)_ports.Add);
			OnParametersSet();
		}

		public Task ChangeNameAsync(string name) => ChangeAsync(Current with { Name = name });

		protected override DemoOptions From(ProtocolOptions options) => DemoOptions.From(options);

		protected override ProtocolOptions ApplyTo(DemoOptions current, ProtocolOptions options) => current.ApplyTo(options);

		protected override void OnCurrentChanged()
		{
			Recomputed++;
			ReportDefaultPort(Current.Port);
		}
	}
}
