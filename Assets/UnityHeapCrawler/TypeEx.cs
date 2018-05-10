using System;
using System.Linq;

namespace UnityHeapCrawler
{
	// type extensions
	public static class TypeEx
	{
		public static string GetDisplayName(this Type type)
		{
			if (!type.IsGenericType)
			{
				return type.Name;
			}

			var genericNames = type.GetGenericArguments()
				.Select(g => g.Name)
				.ToArray();
			string genericArgs = string.Join(", ", genericNames);
			return type.Name + "<" + genericArgs + ">";
		}

		public static string GetFileName(this Type type)
		{
			if (!type.IsGenericType)
			{
				return type.Name;
			}

			var genericNames = type.GetGenericArguments()
				.Select(g => g.Name)
				.ToArray();
			string genericArgs = string.Join("_", genericNames);
			return type.Name + "_" + genericArgs;
		}
	}
}