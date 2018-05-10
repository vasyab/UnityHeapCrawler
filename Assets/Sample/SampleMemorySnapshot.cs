using UnityHeapCrawler;
using UnityEditor;
using UnityEditor.Animations;
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
				.AddTrackedTypes(typeof(Sprite))
				.AddTrackedTypes(typeof(Texture));

			var animators = collector.AddUnityRootsGroup<AnimatorController>
			(
				"animator-controllers",
				"Animator Controllers",
				CrawlOrder.SriptableObjects
			);
			animators.MinItemSize = 1;

			collector.UserRootsSettings.MinItemSize = 1;

			collector.HierarchySettings.MinItemSize = 1;
			collector.HierarchySettings.PrintOnlyGameObjects = false;

			collector.PrefabsSettings.MinItemSize = 1;

			collector.UnityObjectsSettings.MinItemSize = 1;

			collector.Start();
		}
	}
}