using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityHeapCrawler
{
	internal class CrawlItem : IComparable<CrawlItem>
	{
		private static int depth;

		[CanBeNull]
		public readonly CrawlItem Parent;

		[NotNull]
		public readonly object Object;

		[NotNull]
		public string Name;

		public int SelfSize;

		public int TotalSize;

		[CanBeNull]
		public List<CrawlItem> Children;

		private bool childrenFiltered;

		internal bool SubtreeUpdated { get; private set; }

		public CrawlItem([CanBeNull] CrawlItem parent, [NotNull] object o, [NotNull] string name)
		{
			Parent = parent;
			Object = o;
			Name = name;
		}

		public void AddChild([NotNull] CrawlItem child)
		{
			if (Children == null)
				Children = new List<CrawlItem>();

			Children.Add(child);
		}

		public void UpdateSize()
		{
			try
			{
				SelfSize = CalculateSelfSize();
				TotalSize = SelfSize;
				if (Children == null)
					return;

				foreach (var child in Children)
				{
					child.UpdateSize();
					TotalSize += child.TotalSize;
				}
				Children.Sort();
			}
			finally
			{
				TypeStats.RegisterItem(this);
			}
		}

		public void Cleanup(CrawlSettings crawlSettings)
		{
			CleanupUnchanged();
			CleanupInternal(crawlSettings);
		}

		private void CleanupUnchanged()
		{
			if (Children != null)
			{
				foreach (var c in Children)
				{
					c.CleanupUnchanged();
				}

				Children.RemoveAll(c => !c.SubtreeUpdated);
				SubtreeUpdated = Children.Count > 0;
			}

			SubtreeUpdated |= SnapshotHistory.IsNew(Object);
		}

		public void CleanupInternal(CrawlSettings crawlSettings)
		{
			if (!crawlSettings.PrintChildren)
				Children = null;

			if (crawlSettings.MaxDepth > 0 && depth >= crawlSettings.MaxDepth)
				Children = null;

			if (SnapshotHistory.IsPresent() && SnapshotHistory.IsNew(Object))
				Name = Name + " (new)";

			// check for destroyed objects
			var unityObject = Object as Object;
			if (!ReferenceEquals(unityObject, null) && !unityObject)
			{
				const string destroyedObjectString = "(destroyed Unity Object)";
				if (string.IsNullOrWhiteSpace(Name))
					Name = destroyedObjectString;
				else
					Name = Name + " " + destroyedObjectString;

				Children = null;
			}

			if (Children == null)
				return;

			if (crawlSettings.PrintOnlyGameObjects)
				Children.RemoveAll(c => !(c.Object is GameObject));

			int fullChildrenCount = Children.Count;
			if (crawlSettings.MinItemSize > 0)
				Children.RemoveAll(c => c.TotalSize < crawlSettings.MinItemSize);
			if (crawlSettings.MaxChildren > 0 && Children.Count > crawlSettings.MaxChildren)
				Children.RemoveRange(crawlSettings.MaxChildren, Children.Count - crawlSettings.MaxChildren);

			if (Children.Count < fullChildrenCount)
				childrenFiltered = true;

			depth++;
			foreach (var child in Children)
			{
				child.CleanupInternal(crawlSettings);
			}
			depth--;
		}

		public void Print([NotNull] StreamWriter w, SizeFormat sizeFormat)
		{
			for (int i = 0; i < depth; ++i)
				w.Write("  ");

			if (!string.IsNullOrWhiteSpace(Name))
			{
				w.Write(Name);
				w.Write(" ");
			}

			w.Write("[");
			var uo = Object as Object;
			if (uo != null)
			{
				w.Write(uo.name);
				w.Write(": ");
				w.Write(Object.GetType().GetDisplayName());
				w.Write(" (");
				w.Write(uo.GetInstanceID());
				w.Write(")");
			}
			else
			{
				w.Write(Object.GetType().GetDisplayName());				
			}
			w.Write("]");

			w.Write(" ");
			w.Write(sizeFormat.Format(TotalSize));
			w.WriteLine();

			if (Children != null)
			{
				depth++;
				foreach (var child in Children)
				{
					child.Print(w, sizeFormat);
				}

				if (childrenFiltered && Children.Count > 0)
				{
					for (int j = 0; j < depth; ++j)
						w.Write("  ");

					w.WriteLine("...");
				}

				depth--;
			}
		}

		public string GetRootPath()
		{
			var items = new List<CrawlItem>();
			var current = this;
			do
			{
				items.Add(current);
				current = current.Parent;
			} while (current != null);

			items.Reverse();

			var itemNames = items
				.Select(i => i.Name)
				.ToArray();
			return string.Join(".", itemNames);
		}

		private int CalculateSelfSize()
		{
			if (!SnapshotHistory.IsNew(Object))
				return 0;

			string str = Object as string;
			if (str != null)
			{
				// string needs special handling
				int strSize = 3 * IntPtr.Size + 2;
				strSize += str.Length * sizeof(char);
				int pad = strSize % IntPtr.Size;
				if (pad != 0)
				{
					strSize += IntPtr.Size - pad;
				}
				return strSize;
			}


			if (Object.GetType().IsArray)
			{
				var elementType = Object.GetType().GetElementType();
				if (elementType != null && (elementType.IsValueType || elementType.IsPrimitive || elementType.IsEnum))
				{
					// no overhead for array
					return 0;
				}
				else
				{
					int arraySize = GetTotalArrayLength((Array)Object);
					return IntPtr.Size * arraySize;
				}
			}

			return TypeData.Get(Object.GetType()).Size;
		}

		private static int GetTotalArrayLength(Array val)
		{
			int sum = val.GetLength(0);
			for (int i = 1; i < val.Rank; i++)
			{
				sum *= val.GetLength(i);
			}
			return sum;
		}

		public int CompareTo(CrawlItem other)
		{
			if (ReferenceEquals(this, other))
				return 0;
			if (ReferenceEquals(null, other))
				return 1;

			// descending
			return other.TotalSize.CompareTo(TotalSize);
		}

		public override string ToString()
		{
			return Object.ToString();
		}
	}
}