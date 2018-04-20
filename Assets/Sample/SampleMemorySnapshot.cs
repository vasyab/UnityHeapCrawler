using UnityHeapCrawler;
using UnityEditor;
using UnityEngine;

namespace Sample
{
	public static class SampleMemorySnapshot
	{
		[MenuItem("Tools/Memory/Customized Heap Snapshot")]
		public static void HeapSnapshot()
		{
			var collector = new HeapSnapshotCollector()
				.AddRoot(Game.Instance, "Game.Instance")
				.AddRootTypes(typeof(UnitsGroup))
				.AddTrackedTypes(typeof(Unit))
				.AddTrackedTypes(typeof(Texture));

			collector.CustomRootsSettings.MinItemSize = 1;

			collector.HierarchySettings.MinItemSize = 1;
			collector.HierarchySettings.PrintOnlyGameObjects = false;

			collector.Start();
		}
	}
}