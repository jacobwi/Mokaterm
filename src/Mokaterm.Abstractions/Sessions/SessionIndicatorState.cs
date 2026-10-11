namespace Mokaterm.Abstractions.Sessions;

public enum SessionIndicatorState
{
	/// <summary>On, with nothing going through it right now.</summary>
	On,

	/// <summary>On and in use, such as a forwarded connection being relayed.</summary>
	Active,

	/// <summary>Asked for but not working, for the reason the description gives.</summary>
	Problem,
}
