using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Plugins.Sample.Subnets;

/// <summary>An app tool: the network, broadcast, host range, host count and mask of an IPv4 or IPv6 prefix, as it is typed.</summary>
public sealed partial class SubnetCalculatorTool : AppToolBase
{
	private string _input = SampleSettings.DefaultSubnetStart;
	private SubnetInfo? _info;
	private string? _problem;

	/// <summary>What is wrong with the text, once there is text to be wrong.</summary>
	private string? ShownProblem => _input.Trim().Length > 0 ? _problem : null;

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	protected override void OnInitialized()
	{
		_input = SettingsService.Get<SampleSettings>().Clamped().SubnetStart;
		Calculate();
	}

	/// <summary>A count in full, and for a large IPv6 one the power of two it is, which reads better than 20 digits.</summary>
	private static string Count(BigInteger count, SubnetInfo info)
	{
		string text = count.ToString("N0", CultureInfo.CurrentCulture);
		int bits = (info.IsIPv6 ? 128 : 32) - info.PrefixLength;
		return info.IsIPv6 && bits > 16 && count == BigInteger.One << bits ? $"2^{bits} ({text})" : text;
	}

	private void OnInputChanged(string value)
	{
		_input = value;
		Calculate();
	}

	private void Calculate()
	{
		if (SubnetMath.TryCalculate(_input, out SubnetInfo? info, out string? problem))
		{
			_info = info;
			_problem = null;
		}
		else
		{
			_info = null;
			_problem = problem;
		}
	}
}
