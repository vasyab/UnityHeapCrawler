using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace UnityHeapCrawler
{
	public static class SnapshotHistory
	{
		[CanBeNull]
		private static ConditionalWeakTable<object, object> seenObjects;

		public static bool IsNew(object o)
		{
			if (seenObjects == null)
				return true;

			if (ReferenceEquals(o, null))
				return false;

			if (o.GetType().IsValueType)
				return false;

			object v;
			return !seenObjects.TryGetValue(o, out v);
		}

		public static void Store(IEnumerable<object> visitedObjects)
		{
			seenObjects = new ConditionalWeakTable<object, object>();
			foreach (var o in visitedObjects)
			{
				seenObjects.Add(o, o);
			}
		}

		public static bool IsPresent()
		{
			return seenObjects != null;
		}

		public static void Clear()
		{
			seenObjects = null;
		}
	}
}