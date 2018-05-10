using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace UnityHeapCrawler
{
	/// <summary>
	/// Output settings for crawling result - memory tree.
	/// </summary>
	public class CrawlSettings
	{
		[NotNull]
		public static IComparer<CrawlSettings> PriorityComparer { get; } = new PriorityRelationalComparer();

		public bool Enabled = true;

		internal readonly CrawlPriority Priority;

		[NotNull]
		internal readonly Action RootsCollector;

		internal readonly string Caption;

		/// <summary>
		/// Resulting memory tree file name.
		/// </summary>
		[NotNull]
		public string Filename;

		/// <summary>
		/// Print children in memory tree. Disable to include only root objects.
		/// </summary>
		public bool PrintChildren = true;

		/// <summary>
		/// Print only GameObjects in hierarchy mode.
		/// </summary>
		public bool PrintOnlyGameObjects = false;

		/// <summary>
		/// Follow references to unity objects or leave them for a later crawling stage
		/// </summary>
		public bool IncludeUnityObjects = false;

		/// <summary>
		/// Maximum children depth in memory tree. 0 - infinity.
		/// </summary>
		public int MaxDepth = 0;

		/// <summary>
		/// Maximum children printed for one object in memory tree. Children are sorted by total size
		/// </summary>
		public int MaxChildren = 10;

		/// <summary>
		/// Minimum object size to be included in memory tree.
		/// </summary>
		public int MinItemSize = 1024; // bytes

		public CrawlSettings(
			[NotNull] string filename,
			[NotNull] string caption,
			[NotNull] Action rootsCollector,
			CrawlPriority priority)
		{
			Filename = filename;
			Caption = caption;
			RootsCollector = rootsCollector;
			Priority = priority;
		}

		[NotNull]
		public static CrawlSettings CreateUserRoots([NotNull] Action objectsProvider)
		{
			return new CrawlSettings("user-roots", "User Roots", objectsProvider, CrawlPriority.UserRoots)
			{
				MaxChildren = 0
			};
		}

		[NotNull]
		public static CrawlSettings CreateStaticFields([NotNull] Action objectsProvider)
		{
			return new CrawlSettings("static-fields", "Static Roots", objectsProvider, CrawlPriority.StaticFields)
			{
				MaxDepth = 1
			};
		}

		[NotNull]
		public static CrawlSettings CreateHierarchy([NotNull] Action objectsProvider)
		{
			return new CrawlSettings("hierarchy", "Hierarchy", objectsProvider, CrawlPriority.Hierarchy)
			{
				PrintOnlyGameObjects = true,
				MaxChildren = 0
			};
		}

		[NotNull]
		public static CrawlSettings CreateScriptableObjects([NotNull] Action objectsProvider)
		{
			return new CrawlSettings("scriptable_objects", "Scriptable Objects", objectsProvider, CrawlPriority.UnityObjects)
			{
				IncludeUnityObjects = true
			};
		}

		[NotNull]
		public static CrawlSettings CreatePrefabs([NotNull] Action objectsProvider)
		{
			return new CrawlSettings("prefabs", "Prefabs", objectsProvider, CrawlPriority.Prefabs)
			{
				PrintOnlyGameObjects = true,
				MaxChildren = 0
			};
		}

		[NotNull]
		public static CrawlSettings CreateUnityObjects([NotNull] Action objectsProvider)
		{
			return new CrawlSettings("unity_objects", "Unity Objects", objectsProvider, CrawlPriority.UnityObjects);
		}

		public override string ToString()
		{
			return $"Crawl Settings [{Caption}, {Filename}]";
		}

		private sealed class PriorityRelationalComparer : IComparer<CrawlSettings>
		{
			public int Compare(CrawlSettings x, CrawlSettings y)
			{
				if (ReferenceEquals(x, y)) return 0;
				if (ReferenceEquals(null, y)) return 1;
				if (ReferenceEquals(null, x)) return -1;
				return x.Priority.CompareTo(y.Priority);
			}
		}
	}
}