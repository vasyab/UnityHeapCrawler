using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace UnityHeapCrawler
{
	/// <summary>
	/// Tool for crawling mono heap and collecting memory usage.
	/// <para>
	/// 1. Analyze managed memory consumption prior to reducing it
	/// 2. Locate managed memory leaks.
	/// </para>
	/// </summary>
	public class HeapSnapshotCollector
	{
		/// <summary>
		/// <see cref="CrawlSettings"/> for user defined roots. 
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings UserRootsSettings;

		/// <summary>
		/// <see cref="CrawlSettings"/> for static fields in all types. 
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings StaticFieldsSettings;

		/// <summary>
		/// <see cref="CrawlSettings"/> for GameObjects in scene hierarchy.
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings HierarchySettings;

		/// <summary>
		/// <see cref="CrawlSettings"/> for all ScriptableObjects.		
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings ScriptableObjectsSettings;

		/// <summary>
		/// <see cref="CrawlSettings"/> for all loaded Prefabs.
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings PrefabsSettings;

		/// <summary>
		/// <see cref="CrawlSettings"/> for all other Unity objects (Texture, Material, etc).		
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings UnityObjectsSettings;

		/// <summary>
		/// Only show new objects (compared to previous snapshot) in all reports
		/// <para>
		/// Useful to find memory leaks
		/// </para>
		/// </summary>
		public bool DifferentialMode = true;

		/// <summary>
		/// Which size estimations are used
		/// <para>
		/// - Managed - heap estimation
		/// - Native - native size estimation for Unity objects
		/// - Total - Managed + Native
		/// </para>
		/// </summary>
		public SizeMode SizeMode = SizeMode.Managed;

		#region PrivateFields

		[NotNull]
		private readonly List<CrawlSettings> crawlOrder = new List<CrawlSettings>();

		[NotNull]
		private readonly List<CrawlItem> customRoots = new List<CrawlItem>();

		[NotNull]
		private readonly List<Type> rootTypes = new List<Type>();

		[NotNull]
		private readonly List<Type> forbiddenTypes = new List<Type>();

		[NotNull]
		private readonly List<Type> staticTypes = new List<Type>();

		[NotNull]
		private readonly List<Type> trackedTypes = new List<Type>();

		[NotNull]
		private readonly HashSet<object> unityObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);

		[NotNull]
		private readonly HashSet<object> visitedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);

		[NotNull]
		private readonly Queue<CrawlItem> rootsQueue = new Queue<CrawlItem>();

		[NotNull]
		private readonly Queue<CrawlItem> localRootsQueue = new Queue<CrawlItem>();

		private int minTypeSize = 1024;

		private SizeFormat sizeFormat = SizeFormat.Short;

		private string outputDir = "";

		#endregion

		public HeapSnapshotCollector()
		{
			UserRootsSettings = CrawlSettings.CreateUserRoots(CollectUserRoots);
			StaticFieldsSettings = CrawlSettings.CreateStaticFields(CollectStaticFields);
			HierarchySettings = CrawlSettings.CreateHierarchy(CollectRootHierarchyGameObjects);
			ScriptableObjectsSettings = CrawlSettings.CreateScriptableObjects(() => CollectUnityObjects(typeof(ScriptableObject)));
			PrefabsSettings = CrawlSettings.CreatePrefabs(CollectPrefabs);
			UnityObjectsSettings = CrawlSettings.CreateUnityObjects(() => CollectUnityObjects(typeof(Object)));

			forbiddenTypes.Add(typeof(TypeData));
			forbiddenTypes.Add(typeof(TypeStats));
			forbiddenTypes.Add(typeof(SnapshotHistory));
		}

		/// <summary>
		/// <para>Add custom root. It will be crawled before any other objects.</para>
		/// <para>It should be useful to add your big singletons as custom roots.</para>
		/// </summary>
		/// <param name="root">Root to crawl (C# object instance)</param>
		/// <param name="name">Root name in report</param>
		/// <returns></returns>
		[NotNull]
		public HeapSnapshotCollector AddRoot([NotNull] object root, [NotNull] string name)
		{
			string itemName = string.Format("{0} [{1}]", name, root.GetType().GetDisplayName());
			customRoots.Add(new CrawlItem(null, root, itemName));
			return this;
		}

		/// <summary>
		/// <para>Add root types.</para>
		/// <para>Objects of these types will be treated as roots and will not be included into crawl trees they were found in.</para>
		/// </summary>
		/// <param name="types">Root types</param>
		/// <returns></returns>
		[NotNull]
		public HeapSnapshotCollector AddRootTypes([NotNull] params Type[] types)
		{
			rootTypes.AddRange(types);
			return this;
		}

		/// <summary>
		/// <para>Forbid some types to be crawled. Crawler will not follow links to instances of those types or count them to total size.</para>
		/// <para>Useful when you need to collect only a local snapshot from custom definded roots without triggering whole heap to be crawled. 
		/// Also can be useful to reduce memory consumption by crawling only a part of the heap.</para>
		/// </summary>
		/// <param name="types">Forbidden types</param>
		/// <returns></returns>
		[NotNull]
		public HeapSnapshotCollector AddForbiddenTypes([NotNull] params Type[] types)
		{
			forbiddenTypes.AddRange(types);
			return this;
		}

		/// <summary>
		/// <para>Add additional types to check for static fields and add them as roots.</para>
		/// <para>This is a workaround for an unsupported case. Consider following class:</para>
		/// <code>
		/// class GenericClass&lt;T&gt;
		/// {
		///     public static List&lt;T&gt; StaticList;
		/// }
		/// </code>
		/// <para>Static fields <c>GenericClass&lt;A&gt;.StaticList</c> and <c>GenericClass&lt;B&gt;.StaticList</c> would be different lists that should both be counted as roots. 
		/// Crawler cannot find those roots automatically due to C# Reflection limitations.</para>
		/// </summary>
		/// <param name="types">Forbidden types</param>
		/// <returns></returns>
		[NotNull]
		public HeapSnapshotCollector AddStaticTypes([NotNull] params Type[] types)
		{
			staticTypes.AddRange(types);
			return this;
		}

		/// <summary>
		/// <para>Enable root paths tracking for specific types.</para>
		/// <para>Can affect memory consumption - for each instance of specified types all root paths to it will be logged. 
		/// Useful when you already know exact type is leaked and need to find what is holding it.</para>
		/// </summary>
		/// <param name="types">Types to track</param>
		/// <returns></returns>
		[NotNull]
		public HeapSnapshotCollector AddTrackedTypes([NotNull] params Type[] types)
		{
			trackedTypes.AddRange(types);
			return this;
		}

		/// <summary>
		/// <para>Add custom roots crawling group. Results will be wrtitten to a seperate file.</para>
		/// <para>Explicit objects version.</para>
		/// </summary>
		/// <param name="filename">Filename for group output</param>
		/// <param name="caption">Group caption</param>
		/// <param name="order">Crawling priority</param>
		/// <param name="roots">Root objects</param>
		/// <returns>Crawl settings for further configuration</returns>
		[NotNull]
		public CrawlSettings AddRootsGroup(
			[NotNull] string filename, 
			[NotNull] string caption, 
			CrawlOrder order,
			params object[] roots)
		{
			var crawlSettings = new CrawlSettings(filename, caption, () => CollectRoots(roots), order);
			crawlOrder.Add(crawlSettings);
			return crawlSettings;
		}

		/// <summary>
		/// <para>Add custom roots crawling group. Results will be wrtitten to a seperate file.</para>
		/// <para>All Unity objects of specified type are included in the group.</para>
		/// </summary>
		/// <param name="filename">Filename for group output</param>
		/// <param name="caption">Group caption</param>
		/// <param name="order">Crawling priority</param>
		/// <returns>Crawl settings for further configuration</returns>
		[NotNull]
		public CrawlSettings AddUnityRootsGroup<T>(
			[NotNull] string filename,
			[NotNull] string caption,
			CrawlOrder order)
		{
			var crawlSettings = new CrawlSettings(filename, caption, () => CollectUnityObjects(typeof(T)), order)
			{
				IncludeAllUnityTypes = true
			};
			crawlOrder.Add(crawlSettings);
			return crawlSettings;
		}

		/// <summary>
		/// <para>Set minimum size for type to be included in types report.</para>
		/// <para>All instances of the type should be at least this size total for type to be included in type report.</para>
		/// </summary>
		/// <param name="size">Minimum size type</param>
		/// <returns></returns>
		public HeapSnapshotCollector SetMinTypeSize(int size)
		{
			minTypeSize = size;
			return this;
		}

		/// <summary>
		/// <para>Set sizes format in output.</para>
		/// <para><c>Short</c> 54.8 MB</para>
		/// <para><c>Precise</c> 54829125</para>
		/// <para><c>Combined</c> 54.8 MB (54829125)</para>
		/// </summary>
		/// <param name="format"></param>
		/// <returns></returns>
		public HeapSnapshotCollector SetSizeFormat(SizeFormat format)
		{
			sizeFormat = format;
			return this;
		}

		/// <summary>
		/// Let The Crawling Begin!
		/// </summary>
		public void Start()
		{
			// traverse order:
			// 1. used definded roots (without Unity objects)
			// 2. static fields (without Unity objects)
			// 3. game objects in scene hierarchy (seperate view)
			// 4. ScriptableObjects (with all referenced Unity objects)
			// 5. Prefabs (with all referenced Unity objects)
			// 6. all remaining Unity objects (ScriptableObject and other stuff)
			// user can add custom groups using AddRootsGroup and AddUnityRootsGroup
			try
			{
				if (!DifferentialMode)
					SnapshotHistory.Clear();

				TypeData.Start();
				TypeStats.Init(trackedTypes);

				outputDir = "snapshot-" + DateTime.Now.ToString("s").Replace(":", "_");
				if (DifferentialMode && SnapshotHistory.IsPresent())
					outputDir += "-diff";
				outputDir += "/";

				Directory.CreateDirectory(outputDir);

				using (var log = new StreamWriter(outputDir + "log.txt"))
				{
					log.AutoFlush = true;

					GC.Collect();
					GC.Collect();
					log.WriteLine("Mono Size Min: " + sizeFormat.Format(Profiler.GetMonoUsedSizeLong()));
					log.WriteLine("Mono Size Max: " + sizeFormat.Format(Profiler.GetMonoHeapSizeLong()));
					log.WriteLine("Total Allocated: " + sizeFormat.Format(Profiler.GetTotalAllocatedMemoryLong()));
					log.WriteLine("Total Reserved: " + sizeFormat.Format(Profiler.GetTotalReservedMemoryLong()));
					log.WriteLine();
					
					// now add predefined crawl settings and sort by priority
					// all user crawl settings were added before so they will precede predefined ones with same priority
					crawlOrder.Add(UserRootsSettings);
					crawlOrder.Add(StaticFieldsSettings);
					crawlOrder.Add(HierarchySettings);
					crawlOrder.Add(ScriptableObjectsSettings);
					crawlOrder.Add(PrefabsSettings);
					crawlOrder.Add(UnityObjectsSettings);
					crawlOrder.RemoveAll(cs => !cs.Enabled);
					crawlOrder.Sort(CrawlSettings.PriorityComparer);

#if UNITY_EDITOR
					EditorUtility.DisplayProgressBar("Heap Snapshot", "Collecting Unity Objects...", 0f);
#endif
					unityObjects.Clear();
					var unityObjectsList = Resources.FindObjectsOfTypeAll<Object>();
					foreach (var o in unityObjectsList)
					{
						unityObjects.Add(o);
					}

					long totalSize = 0;
					float progressStep = 0.8f / crawlOrder.Count;
					for (var i = 0; i < crawlOrder.Count; i++)
					{
						float startProgress = 0.1f + progressStep * i;
						float endProgress = 0.1f + progressStep * (i + 1);
						var crawlSettings = crawlOrder[i];

						DisplayCollectProgressBar(crawlSettings, startProgress);

						crawlSettings.RootsCollector();

						int displayIndex = i + 1;
						long rootsSize = CrawlRoots(crawlSettings, displayIndex, startProgress, endProgress);

						log.WriteLine($"{crawlSettings.Caption} size: " + sizeFormat.Format(rootsSize));
						totalSize += rootsSize;
					}

					log.WriteLine("Total size: " + sizeFormat.Format(totalSize));
				}

#if UNITY_EDITOR
				EditorUtility.DisplayProgressBar("Heap Snapshot", "Printing Type Statistics...", 0.95f);
#endif
				PrintTypeStats(TypeSizeMode.Self, "types-self.txt");
				PrintTypeStats(TypeSizeMode.Total, "types-total.txt");
				PrintTypeStats(TypeSizeMode.Native, "types-native.txt");

#if UNITY_EDITOR
				EditorUtility.DisplayProgressBar("Heap Snapshot", "Printing Instance Statistics...", 0.95f);
#endif
				PrintInstanceStats();

				if (DifferentialMode)
					SnapshotHistory.Store(visitedObjects);

				Debug.Log("Heap snapshot created: " + outputDir);
			}
			finally
			{
#if UNITY_EDITOR
				EditorUtility.ClearProgressBar();
#endif
			}
		}

#if UNITY_EDITOR
		[MenuItem("Tools/Memory/Default Heap Snapshot")]
		public static void HeapSnapshot()
		{
			new HeapSnapshotCollector().Start();
		}
#endif

		private void PrintTypeStats(TypeSizeMode mode, string filename)
		{
			using (var f = new StreamWriter(outputDir + filename))
			{
				var typeStats = TypeStats.Data.Values
					.OrderByDescending(ts => mode.GetSize(ts))
					.ToList();

				f.Write("Size".PadLeft(12));
				f.Write("    ");
				f.Write("Count".PadLeft(9));
				f.Write("    ");
				f.Write("Type");
				f.WriteLine();

				foreach (var ts in typeStats)
				{
					if (ts.SelfSize < minTypeSize)
						continue;

					long size = mode.GetSize(ts);
					f.Write(sizeFormat.Format(size).PadLeft(12));
					f.Write("    ");
					f.Write(ts.Count.ToString().PadLeft(9));
					f.Write("    ");
					f.Write(ts.Type.GetDisplayName());
					f.WriteLine();
				}
			}
		}

		private void PrintInstanceStats()
		{
			var typeStats = TypeStats.Data.Values;
			foreach (var ts in typeStats)
			{
				if (ts.Instances.Count <= 0)
					continue;

				var dir = outputDir + "types/";
				Directory.CreateDirectory(dir);
				var fileName = dir + ts.Type.GetFileName() + ".txt";
				using (var f = new StreamWriter(fileName))
				{
					var instances = ts.Instances.Values.ToList();
					instances.Sort();

					foreach (var instance in instances)
					{
						f.Write(instance.Instance.ToString());

						var nativeSize = instance.NativeSize;
						if (nativeSize > 0)
						{
							f.Write(" [native " + sizeFormat.Format(nativeSize) + ", managed " + sizeFormat.Format(instance.Size) + "]");
						}
						else
						{
							f.Write(" [managed " + sizeFormat.Format(instance.Size) + "]");
						}

						f.Write(" root paths:");
						f.WriteLine();
						foreach (var rootPath in instance.RootPaths)
						{
							f.WriteLine(rootPath);
						}
						f.WriteLine();
					}
				}
			}
		}

		private void CollectUserRoots()
		{
			foreach (var root in customRoots)
			{
				visitedObjects.Add(root.Object);
				rootsQueue.Enqueue(root);
			}
		}

		private void CollectRoots(object[] roots)
		{
			foreach (var root in roots)
			{
				var name = root.GetType().GetDisplayName();
				EnqueueRoot(root, name, false);
			}
		}

		private void CollectStaticFields()
		{
			var assemblyTypes = AppDomain.CurrentDomain.GetAssemblies()
				.Where(IsValidAssembly)
				.SelectMany(a => a.GetTypes());

			var allTypes = staticTypes.Concat(assemblyTypes);
			var genericStaticFields = new HashSet<string>();

			foreach (var type in allTypes)
			{
				try
				{
					AddStaticFields(type, genericStaticFields);
				}
				catch (Exception e)
				{
					Debug.LogException(e);
				}
			}

			if (genericStaticFields.Count > 0)
			{
				var genericStaticFieldsList = genericStaticFields.ToList();
				genericStaticFieldsList.Sort();

				using (var log = new StreamWriter(outputDir + "generic-static-fields.txt"))
				{
					foreach (var s in genericStaticFieldsList)
					{
						log.WriteLine(s);
					}
				}
			}
		}

		private void AddStaticFields([NotNull] Type type, [NotNull] HashSet<string> genericStaticFields)
		{
			if (IsForbiddenType(type))
				return;

			var currentType = type;
			while (currentType != null && currentType != typeof(object))
			{
				foreach (var fieldInfo in currentType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (currentType.IsGenericTypeDefinition)
					{
						genericStaticFields.Add(currentType.FullName + "." + fieldInfo.Name);
						continue;
					}

					var v = fieldInfo.GetValue(null);
					if (v != null)
					{
						var name = currentType.GetDisplayName() + '.' + fieldInfo.Name;
						EnqueueRoot(v, name, false);
					}
				}
				currentType = currentType.BaseType;
			}
		}

		private void CollectRootHierarchyGameObjects()
		{
			foreach (var o in unityObjects)
			{
				var go = o as GameObject;
				if (go == null)
					continue;
				if (!go.scene.IsValid())
					continue;
				if (go.transform.parent != null)
					continue;
				EnqueueRoot(go, go.name, false);
			}
		}

		private void CollectPrefabs()
		{
			foreach (var o in unityObjects)
			{
				var go = o as GameObject;
				if (go == null)
					continue;
				if (go.scene.IsValid())
					continue;
				if (go.transform.parent != null)
					continue;
				EnqueueRoot(go, go.name, false);
			}
		}

		private void CollectUnityObjects(Type type)
		{
			foreach (var o in unityObjects)
			{
				if (!type.IsInstanceOfType(o))
					continue;

				var uo = (Object) o;
				EnqueueRoot(uo, uo.name, false);
			}
		}

		private static void DisplayCollectProgressBar([NotNull] CrawlSettings crawlSettings, float startProgress)
		{
#if UNITY_EDITOR
			EditorUtility.DisplayProgressBar("Heap Snapshot", "Collecting: " + crawlSettings.Caption + "...", startProgress);
#endif
		}

		private long CrawlRoots([NotNull] CrawlSettings crawlSettings, int crawlIndex, float startProgress, float endProgress)
		{
			if (rootsQueue.Count <= 0)
				return 0;

			int processedRoots = 0;
			long totalSize = 0;
			var roots = new List<CrawlItem>();
			while (rootsQueue.Count > 0)
			{
				var r = rootsQueue.Dequeue();
				localRootsQueue.Enqueue(r);

				while (localRootsQueue.Count > 0)
				{
#if UNITY_EDITOR
					if (processedRoots % 10 == 0)
					{
						int remainingRoots = rootsQueue.Count + localRootsQueue.Count;
						int totalRoots = remainingRoots + processedRoots;
						float progress = Mathf.Lerp(
							startProgress,
							endProgress,
							1f * processedRoots / totalRoots
						);
						EditorUtility.DisplayProgressBar("Heap Snapshot", "Crawling: " + crawlSettings.Caption + "...", progress);
					}
#endif
					var root = localRootsQueue.Dequeue();
					CrawlRoot(root, crawlSettings);
					totalSize += root.TotalSize;
					root.Cleanup(crawlSettings);
					processedRoots++;

					if (!root.SubtreeUpdated)
						continue;
					if (root.TotalSize < crawlSettings.MinItemSize)
						continue;
					roots.Add(root);
				}
			}

			roots.Sort();
			if (roots.Count > 0)
			{
				using (var output = new StreamWriter($"{outputDir}{crawlIndex}-{crawlSettings.Filename}.txt"))
				{
					foreach (var root in roots)
					{
						root.Print(output, sizeFormat);
					}
				}
			}

			return totalSize;
		}

		private void CrawlRoot([NotNull] CrawlItem root, [NotNull] CrawlSettings crawlSettings)
		{
			var queue = new Queue<CrawlItem>();
			queue.Enqueue(root);

			while (queue.Count > 0)
			{
				var next = queue.Dequeue();
				var type = next.Object.GetType();
				var typeData = TypeData.Get(type);

				if (type.IsArray)
				{
					QueueArrayElements(next, queue, next.Object, crawlSettings);
				}
				if (type == typeof(GameObject))
				{
					QueueHierarchy(next, queue, next.Object, crawlSettings);
				}
				if (typeData.DynamicSizedFields != null)
				{
					foreach (var field in typeData.DynamicSizedFields)
					{
						var v = field.GetValue(next.Object);
						QueueValue(next, queue, v, field.Name, crawlSettings);
					}
				}
			}

			root.UpdateSize(SizeMode);
		}

		private void EnqueueRoot([NotNull] object root, [NotNull] string name, bool local)
		{
			if (IsForbidden(root))
				return;

			if (visitedObjects.Contains(root))
				return;

			visitedObjects.Add(root);
			var rootItem = new CrawlItem(null, root, name);

			if (local)
				localRootsQueue.Enqueue(rootItem);
			else
				rootsQueue.Enqueue(rootItem);
		}

		private void QueueValue(
			[NotNull] CrawlItem parent,
			[NotNull] Queue<CrawlItem> queue,
			[CanBeNull] object v,
			[NotNull] string name,
			[NotNull] CrawlSettings crawlSettings)
		{
			if (v == null)
				return;

			if (IsForbidden(v))
				return;

			TypeStats.RegisterInstance(parent, name, v);
			if (visitedObjects.Contains(v))
				return;

			if (unityObjects.Contains(v) && !crawlSettings.IsUnityTypeAllowed(v.GetType()))
				return;

			if (IsRoot(v))
			{
				var rootName = parent.Object.GetType().GetDisplayName() + '.' + name;
				EnqueueRoot(v, rootName, true);
				return;
			}

			visitedObjects.Add(v);
			var item = new CrawlItem(parent, v, name);
			queue.Enqueue(item);
			parent.AddChild(item);
		}

		private void QueueArrayElements(
			[NotNull] CrawlItem parent,
			[NotNull] Queue<CrawlItem> queue,
			[CanBeNull] object array,
			[NotNull] CrawlSettings crawlSettings)
		{
			if (array == null)
				return;

			if (!array.GetType().IsArray)
				return;

			var elementType = array.GetType().GetElementType();
			if (elementType == null)
				return;

			int index = 0;
			foreach (var arrayItem in (Array) array)
			{
				QueueValue(parent, queue, arrayItem, $"[{index}]", crawlSettings);
				index++;
			}
		}

		private void QueueHierarchy(
			[NotNull] CrawlItem parent,
			[NotNull] Queue<CrawlItem> queue,
			[CanBeNull] object v,
			[NotNull] CrawlSettings crawlSettings)
		{
			var go = v as GameObject;
			if (go == null)
				return;

			var components = go.GetComponents<Component>();
			foreach (var c in components)
			{
				if (ReferenceEquals(c, null))
					continue;

				QueueValue(parent, queue, c, c.GetType().GetDisplayName(), crawlSettings);
			}

			var t = go.transform;
			for (int i = 0; i < t.childCount; ++i)
			{
				var childGo = t.GetChild(i).gameObject;
				QueueValue(parent, queue, childGo, childGo.name, crawlSettings);
			}
		}

		private bool IsRoot([NotNull] object o)
		{
			return rootTypes.Any(t => t.IsInstanceOfType(o));
		}

		private bool IsForbidden([NotNull] object o)
		{
			return forbiddenTypes.Any(t => t.IsInstanceOfType(o));
		}

		private bool IsForbiddenType([NotNull] Type type)
		{
			return forbiddenTypes.Any(t => t.IsAssignableFrom(type));
		}

		private static bool IsValidAssembly(Assembly assembly)
		{
			if (assembly.FullName.StartsWith("UnityEditor."))
				return false;
			if (assembly.FullName.StartsWith("UnityScript."))
				return false;
			if (assembly.FullName.StartsWith("Boo."))
				return false;
			if (assembly.FullName.StartsWith("ExCSS."))
				return false;
			if (assembly.FullName.StartsWith("I18N"))
				return false;
			if (assembly.FullName.StartsWith("Microsoft."))
				return false;
			if (assembly.FullName.StartsWith("System"))
				return false;
			if (assembly.FullName.StartsWith("SyntaxTree."))
				return false;
			if (assembly.FullName.StartsWith("mscorlib"))
				return false;
			if (assembly.FullName.StartsWith("Windows."))
				return false;
			return true;
		}
	}
}