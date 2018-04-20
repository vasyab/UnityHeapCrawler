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
		/// <see cref="CrawlSettings"/> for custom defined roots. 
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings CustomRootsSettings = CrawlSettings.CreateCustomRoots();

		/// <summary>
		/// <see cref="CrawlSettings"/> for static fields in all types. 
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings StaticFieldsSettings = CrawlSettings.CreateStaticFields();

		/// <summary>
		/// <see cref="CrawlSettings"/> for GameObjects in scene hierarchy.
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>
		/// </summary>
		[NotNull]
		public readonly CrawlSettings HierarchySettings = CrawlSettings.CreateHierarchy();

		/// <summary>
		/// <see cref="CrawlSettings"/> for all other Unity objects (ScriptableObject, Texture, Material, etc).		
		/// <para>
		/// Modify them after construction to change output format or disable crawling.
		/// Be careful when reducing filtering - large crawl trees will affect memory consumption.
		/// </para>

		/// </summary>
		[NotNull]
		public readonly CrawlSettings UnityObjectsSettings = CrawlSettings.CreateUnityObjects();

		#region PrivateFields

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
			// 4. all remaining Unity objects (ScriptableObject and other stuff)
			try
			{
				TypeData.Start();
				TypeStats.Init(trackedTypes);

				outputDir = "snapshot-" + DateTime.Now.ToString("s").Replace(":", "_") + '/';
				Directory.CreateDirectory(outputDir);

				using (var log = new StreamWriter(outputDir + "log.txt"))
				{
					log.AutoFlush = true;

					GC.Collect();
					log.WriteLine("Mono Size Min: " + sizeFormat.Format(Profiler.GetMonoUsedSizeLong()));
					log.WriteLine("Mono Size Max: " + sizeFormat.Format(Profiler.GetMonoHeapSizeLong()));
					log.WriteLine("Total Allocated: " + sizeFormat.Format(Profiler.GetTotalAllocatedMemoryLong()));
					log.WriteLine("Total Reserved: " + sizeFormat.Format(Profiler.GetTotalReservedMemoryLong()));
					log.WriteLine();

#if UNITY_EDITOR
					EditorUtility.DisplayProgressBar("Heap Snapshot", "Collecting Unity Objects...", 0f);
#endif
					unityObjects.Clear();
					var unityObjectsList = Resources.FindObjectsOfTypeAll<Object>();
					foreach (var o in unityObjectsList)
					{
						unityObjects.Add(o);
					}

					int customRootsSize = CrawlCustomRoots();
					if (customRootsSize > 0)
						log.WriteLine("Custom roots size: " + sizeFormat.Format(customRootsSize));

					int staticRootsSize = CrawlStaticFields();
					if (staticRootsSize > 0)
						log.WriteLine("Static fields size: " + sizeFormat.Format(staticRootsSize));

					int hierarchySize = CrawlHierarchy();
					if (hierarchySize > 0)
						log.WriteLine("Heirarchy size: " + sizeFormat.Format(hierarchySize));

					int unityObjectsSize = CrawlUnityObjects();
					if (unityObjectsSize > 0)
						log.WriteLine("Unity objects size: " + sizeFormat.Format(unityObjectsSize));

					int totalSize = customRootsSize + staticRootsSize + hierarchySize + unityObjectsSize;
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

		private int CrawlCustomRoots()
		{
			if (!CustomRootsSettings.Enabled)
				return 0;
			DisplayCollectProgressBar(CustomRootsSettings);
			CollectCustomRoots();
			return CrawlRoots(CustomRootsSettings);
		}

		private int CrawlStaticFields()
		{
			if (!StaticFieldsSettings.Enabled)
				return 0;
			DisplayCollectProgressBar(StaticFieldsSettings);
			CollectStaticFields();
			return CrawlRoots(StaticFieldsSettings);
		}

		private int CrawlHierarchy()
		{
			if (!HierarchySettings.Enabled)
				return 0;
			DisplayCollectProgressBar(HierarchySettings);
			CollectRootGameObjects();
			return CrawlRoots(HierarchySettings);
		}

		private int CrawlUnityObjects()
		{
			if (!UnityObjectsSettings.Enabled)
				return 0;
			DisplayCollectProgressBar(UnityObjectsSettings);
			CollectAllUnityObjects();
			return CrawlRoots(UnityObjectsSettings);
		}

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
				var fileName = dir + ts.Type.GetDisplayName() + ".txt";
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

		private void CollectCustomRoots()
		{
			foreach (var root in customRoots)
			{
				visitedObjects.Add(root.Object);
				rootsQueue.Enqueue(root);
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

		private void CollectRootGameObjects()
		{
			foreach (var o in unityObjects)

			{
				var go = o as GameObject;
				if (go == null)
					continue;
				if (go.transform.parent != null)
					continue;
				EnqueueRoot(go, go.name, false);
			}
		}

		private void CollectAllUnityObjects()
		{
			foreach (var o in unityObjects)
			{
				var uo = (Object) o;
				EnqueueRoot(uo, uo.name, false);
			}
		}

		private static void DisplayCollectProgressBar([NotNull] CrawlSettings crawlSettings)
		{
#if UNITY_EDITOR
			EditorUtility.DisplayProgressBar("Heap Snapshot", "Collecting: " + crawlSettings.Caption + "...",
				crawlSettings.StartProgress);
#endif
		}

		private int CrawlRoots([NotNull] CrawlSettings crawlSettings)
		{
			if (rootsQueue.Count <= 0)
				return 0;

			int processedRoots = 0;
			int totalSize = 0;
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
							crawlSettings.StartProgress,
							crawlSettings.EndProgress,
							1f * processedRoots / totalRoots
						);
						EditorUtility.DisplayProgressBar("Heap Snapshot", "Crawling: " + crawlSettings.Caption + "...", progress);
					}
#endif
					var root = localRootsQueue.Dequeue();
					CrawlRoot(root);
					totalSize += root.TotalSize;
					root.Cleanup(crawlSettings);
					processedRoots++;

					if (root.TotalSize >= crawlSettings.MinItemSize)
						roots.Add(root);
				}
			}

			roots.Sort();
			using (var output = new StreamWriter(outputDir + crawlSettings.Filename))
			{
				foreach (var root in roots)
				{
					root.Print(output, sizeFormat);
				}
			}

			return totalSize;
		}

		private void CrawlRoot([NotNull] CrawlItem root)
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
					QueueArrayElements(next, queue, next.Object);
				}
				if (type == typeof(GameObject))
				{
					QueueHierarchy(next, queue, next.Object);
				}
				if (typeData.DynamicSizedFields != null)
				{
					foreach (var field in typeData.DynamicSizedFields)
					{
						var v = field.GetValue(next.Object);
						QueueValue(next, queue, v, field.Name);
					}
				}
			}

			root.UpdateSize();
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
			bool allowUnityObjects = false)
		{
			if (v == null)
				return;

			if (IsForbidden(v))
				return;

			TypeStats.RegisterInstance(parent, name, v);
			if (visitedObjects.Contains(v))
				return;

			if (!allowUnityObjects && unityObjects.Contains(v))
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
			[CanBeNull] object array)
		{
			if (array == null)
				return;

			if (!array.GetType().IsArray)
				return;

			var elementType = array.GetType().GetElementType();
			if (elementType == null)
				return;

			foreach (var arrayItem in (Array) array)
			{
				QueueValue(parent, queue, arrayItem, "");
			}
		}

		private void QueueHierarchy(
			[NotNull] CrawlItem parent,
			[NotNull] Queue<CrawlItem> queue,
			[CanBeNull] object v)
		{
			var go = v as GameObject;
			if (go == null)
				return;

			var components = go.GetComponents<Component>();
			foreach (var c in components)
			{
				QueueValue(parent, queue, c, "", true);
			}

			var t = go.transform;
			for (int i = 0; i < t.childCount; ++i)
			{
				var childGo = t.GetChild(i).gameObject;
				QueueValue(parent, queue, childGo, childGo.name, true);
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