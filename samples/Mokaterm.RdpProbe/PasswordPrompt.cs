using System.Text;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.RdpProbe;

/// <summary>Gets the password from MOKATERM_RDP_PASSWORD or from the keyboard, never echoing it.</summary>
internal static class PasswordPrompt
{
	public const string Variable = "MOKATERM_RDP_PASSWORD";

	public static SecretBuffer Read(string user)
	{
		if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } fromEnvironment)
		{
			Console.WriteLine($"Using the password in {Variable}.");
			return SecretBuffer.FromString(fromEnvironment);
		}

		if (Console.IsInputRedirected)
		{
			throw new InvalidOperationException($"Set {Variable}, or run the probe in a console so it can ask for the password.");
		}

		Console.Write($"Password for {user}: ");
		StringBuilder typed = new();
		try
		{
			while (true)
			{
				ConsoleKeyInfo key = Console.ReadKey(intercept: true);
				if (key.Key == ConsoleKey.Enter)
				{
					break;
				}

				if (key.Key == ConsoleKey.Backspace)
				{
					if (typed.Length > 0)
					{
						typed[^1] = '\0';
						typed.Length--;
					}

					continue;
				}

				if (key.KeyChar != '\0')
				{
					typed.Append(key.KeyChar);
				}
			}

			Console.WriteLine();
			return SecretBuffer.FromString(typed.ToString());
		}
		finally
		{
			// What the builder held is overwritten before it is let go.
			for (int i = 0; i < typed.Length; i++)
			{
				typed[i] = '\0';
			}
		}
	}
}
