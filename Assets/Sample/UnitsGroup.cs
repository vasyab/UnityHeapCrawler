using JetBrains.Annotations;

namespace Sample
{
	public class UnitsGroup
	{
		[NotNull]
		public readonly Unit[] Units;

		public UnitsGroup([NotNull] params Unit[] units)
		{
			Units = units;
		}
	}
}