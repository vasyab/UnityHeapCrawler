using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine.Profiling;

namespace UnityHeapCrawler
{
	internal class InstanceStats : IComparable<InstanceStats>
	{
		[NotNull]
		public readonly object Instance;

		public long Size;

		public long NativeSize
		{
			get
			{
				var unityObject = Instance as UnityEngine.Object;
				if (unityObject != null)
					return Profiler.GetRuntimeMemorySizeLong(unityObject);
				return -1L;
			}
		}

		[NotNull]
		public List<string> RootPaths = new List<string>();

		public InstanceStats([NotNull] object instance)
		{
			Instance = instance;
		}

		public int CompareTo(InstanceStats other)
		{
			if (ReferenceEquals(this, other)) return 0;
			if (ReferenceEquals(null, other)) return 1;

			long total = Size + NativeSize;
			long otherTotal = other.Size + other.NativeSize;
			// descending
			return otherTotal.CompareTo(total);
		}
	}
}