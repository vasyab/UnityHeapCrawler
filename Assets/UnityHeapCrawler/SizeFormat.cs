using System.Globalization;
using JetBrains.Annotations;

namespace UnityHeapCrawler
{
	public enum SizeFormat
	{
		Short,
		Precise,
		Combined
	}

	public static class SizeFormatEx
	{
		[NotNull]
		public static string Format(this SizeFormat format, long size)
		{
			if (format == SizeFormat.Precise)
				return size.ToString();

			long quantumSize = 1;
			int postfixIndex = 0;
			while (quantumSize * 1024 < size && postfixIndex < 4)
			{
				quantumSize *= 1024;
				postfixIndex++;
			}

			double value = 1.0 * size / quantumSize;
			string shortString;
			if (postfixIndex == 0)
				shortString = size.ToString(CultureInfo.InvariantCulture);
			else if (value >= 9.995)
				shortString = value.ToString("F1", CultureInfo.InvariantCulture);
			else
				shortString = value.ToString("F2", CultureInfo.InvariantCulture);
			shortString += " ";

			switch (postfixIndex)
			{
				case 0:
					shortString += "bytes";
					break;
				case 1:
					shortString += "KB";
					break;
				case 2:
					shortString += "MB";
					break;
				case 3:
					shortString += "GB";
					break;
				case 4:
					shortString += "TB";
					break;
				default:
					shortString += "Unknown Qualifier";
					break;
			}

			if (format == SizeFormat.Short)
				return shortString;
			else
				return shortString + " (" + size + ")";
		}
	}
}