using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace UnityHeapCrawler
{
	internal class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		[NotNull]
		public static ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

		public new bool Equals(object x, object y)
		{
			return ReferenceEquals(x, y);
		}

		public int GetHashCode(object o)
		{
			return RuntimeHelpers.GetHashCode(o);
		}
	}
}