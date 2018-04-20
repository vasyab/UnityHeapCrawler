using JetBrains.Annotations;

namespace UnityHeapCrawler
{
	/// <summary>
	/// Output settings for crawling result - memory tree.
	/// </summary>
	public class CrawlSettings
	{
		public bool Enabled = true;

		internal readonly string Caption;

		internal readonly float StartProgress;

		internal readonly float EndProgress;

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
			float startProgress,
			float endProgress)
		{
			Filename = filename;
			Caption = caption;
			StartProgress = startProgress;
			EndProgress = endProgress;
		}

		[NotNull]
		public static CrawlSettings CreateCustomRoots()
		{
			return new CrawlSettings("1-custom-roots.txt", "User Roots", 0.1f, 0.3f) {MaxChildren = 0};
		}

		[NotNull]
		public static CrawlSettings CreateStaticFields()
		{
			return new CrawlSettings("2-static-fields.txt", "Static Roots", 0.3f, 0.5f) {MaxDepth = 1};
		}

		[NotNull]
		public static CrawlSettings CreateHierarchy()
		{
			return new CrawlSettings("3-hierarchy.txt", "Hierarchy", 0.5f, 0.7f)
			{
				PrintOnlyGameObjects = true,
				MaxChildren = 0
			};
		}

		[NotNull]
		public static CrawlSettings CreateUnityObjects()
		{
			return new CrawlSettings("4-unity_objects.txt", "Unity Objects", 0.7f, 0.9f);
		}
	}
}