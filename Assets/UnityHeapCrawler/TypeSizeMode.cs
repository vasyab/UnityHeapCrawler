using System;

namespace UnityHeapCrawler
{
	internal enum TypeSizeMode
	{
		Self,
		Total,
		Native
	}

	internal static class TypeSizeModeEx
	{
		public static long GetSize(this TypeSizeMode mode, TypeStats typeStats)
		{
			switch (mode)
			{
				case TypeSizeMode.Self:
					return typeStats.SelfSize;
				case TypeSizeMode.Total:
					return typeStats.TotalSize;
				case TypeSizeMode.Native:
					return typeStats.NativeSize;
				default:
					throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
			}
		}
	}
}