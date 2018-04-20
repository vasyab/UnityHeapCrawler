using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine.Profiling;

namespace UnityHeapCrawler
{
	public class TypeStats : IComparable<TypeStats>
	{
		[NotNull]
		public static readonly Dictionary<Type, TypeStats> Data = new Dictionary<Type, TypeStats>();

		[NotNull]
		public static readonly HashSet<Type> TrackedTypes = new HashSet<Type>();

		[NotNull]
		public readonly Type Type;

		public long SelfSize;

		public long TotalSize;

		public long NativeSize;

		public int Count;

		private readonly bool tracked;

		[NotNull]
		public readonly Dictionary<object, InstanceStats> Instances = new Dictionary<object, InstanceStats>(ReferenceEqualityComparer.Instance);

		public static void Init(List<Type> trackedTypes)
		{
			Data.Clear();
			TrackedTypes.Clear();

			foreach (var t in trackedTypes)
			{
				TrackedTypes.Add(t);
			}
		}

		public static void RegisterItem([NotNull] CrawlItem item)
		{
			var stats = DemandTypeStats(item.Object.GetType());

			stats.Count++;
			stats.SelfSize += item.SelfSize;
			stats.TotalSize += item.TotalSize;

			var unityObject = item.Object as UnityEngine.Object;
			if (unityObject != null)
				stats.NativeSize += Profiler.GetRuntimeMemorySizeLong(unityObject);

			if (stats.tracked)
				stats.DemandInstanceStats(item.Object).Size = item.TotalSize;
		}

		public static void RegisterInstance([NotNull] CrawlItem parent, [NotNull] string name, [NotNull] object instance)
		{
			var stats = DemandTypeStats(instance.GetType());
			if (!stats.tracked)
				return;

			var instanceStats = stats.DemandInstanceStats(instance);
			var rootPath = parent.GetRootPath() + "." + name;
			instanceStats.RootPaths.Add(rootPath);
		}

		private TypeStats([NotNull] Type type)
		{
			Type = type;
			tracked = TrackedTypes.Any(t => t.IsAssignableFrom(type));
		}

		public int CompareTo([CanBeNull] TypeStats other)
		{
			if (ReferenceEquals(this, other)) return 0;
			if (ReferenceEquals(null, other)) return 1;

			// descending
			return other.SelfSize.CompareTo(SelfSize);
		}

		[NotNull]
		private static TypeStats DemandTypeStats([NotNull] Type type)
		{
			TypeStats stats;
			if (!Data.TryGetValue(type, out stats))
			{
				stats = new TypeStats(type);
				Data[type] = stats;
			}
			return stats;
		}

		[NotNull]
		private InstanceStats DemandInstanceStats([NotNull] object instance)
		{
			InstanceStats stats;
			if (!Instances.TryGetValue(instance, out stats))
			{
				stats = new InstanceStats(instance);
				Instances[instance] = stats;
			}
			return stats;
		}
	}
}