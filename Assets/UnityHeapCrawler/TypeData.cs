using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UnityHeapCrawler
{
	internal class TypeData
	{
		public int Size { get; private set; }
		public List<FieldInfo> DynamicSizedFields { get; private set; }

		private static Dictionary<Type, TypeData> seenTypeData;
		private static Dictionary<Type, TypeData> seenTypeDataNested;

		public static void Clear()
		{
			seenTypeData = null;
		}

		public static void Start()
		{
			seenTypeData = new Dictionary<Type, TypeData>();
			seenTypeDataNested = new Dictionary<Type, TypeData>();
		}

		public static TypeData Get(Type type)
		{
			TypeData data;
			if (!seenTypeData.TryGetValue(type, out data))
			{
				data = new TypeData(type);
				seenTypeData[type] = data;
			}
			return data;
		}

		public static TypeData GetNested(Type type)
		{
			TypeData data;
			if (!seenTypeDataNested.TryGetValue(type, out data))
			{
				data = new TypeData(type, true);
				seenTypeDataNested[type] = data;
			}
			return data;
		}

		public TypeData(Type type, bool nested = false)
		{
			var baseType = type.BaseType;
			if (baseType != null
				&& baseType != typeof(object)
				&& baseType != typeof(ValueType)
				&& baseType != typeof(Array)
				&& baseType != typeof(Enum))
			{
				var baseTypeData = GetNested(baseType);
				Size += baseTypeData.Size;

				if (baseTypeData.DynamicSizedFields != null)
				{
					DynamicSizedFields = new List<FieldInfo>(baseTypeData.DynamicSizedFields);
				}
			}
			if (type.IsPointer)
			{
				Size = IntPtr.Size;
			}
			else if (type.IsArray)
			{
				var elementType = type.GetElementType();
				Size = ((elementType.IsValueType || elementType.IsPrimitive || elementType.IsEnum) ? 3 : 4) * IntPtr.Size;
			}
			else if (type.IsPrimitive)
			{
				Size = Marshal.SizeOf(type);
			}
			else if (type.IsEnum)
			{
				Size = Marshal.SizeOf(Enum.GetUnderlyingType(type));
			}
			else // struct, class
			{
				if (!nested && type.IsClass)
				{
					Size = 2 * IntPtr.Size;
				}
				foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					ProcessField(field, field.FieldType);
				}
				if (!nested && type.IsClass)
				{
					Size = Math.Max(3 * IntPtr.Size, Size);
					int pad = Size % IntPtr.Size;
					if (pad != 0)
					{
						Size += IntPtr.Size - pad;
					}
				}
			}
		}

		private void ProcessField(FieldInfo field, Type fieldType)
		{
			if (IsStaticallySized(fieldType))
			{
				Size += GetStaticSize(fieldType);
			}
			else
			{
				if (!(fieldType.IsValueType || fieldType.IsPrimitive || fieldType.IsEnum))
				{
					Size += IntPtr.Size;
				}
				if (fieldType.IsPointer)
				{
					return;
				}
				if (DynamicSizedFields == null)
				{
					DynamicSizedFields = new List<FieldInfo>();
				}
				DynamicSizedFields.Add(field);
			}
		}

		private static bool IsStaticallySized(Type type)
		{

			if (type.IsPointer || type.IsArray || type.IsClass || type.IsInterface)
			{
				return false;
			}
			if (type.IsPrimitive || type.IsEnum)
			{
				return true;
			}
			foreach (var nestedField in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (!IsStaticallySized(nestedField.FieldType))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Gets size of type. Assumes IsStaticallySized (type) is true. (primitive, enum, {struct or class with no references in it})
		/// </summary>
		private static int GetStaticSize(Type type)
		{
			if (type.IsPrimitive)
			{
				return Marshal.SizeOf(type);
			}
			if (type.IsEnum)
			{
				return Marshal.SizeOf(Enum.GetUnderlyingType(type));
			}
			int size = 0;
			foreach (var nestedField in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				size += GetStaticSize(nestedField.FieldType);
			}
			return size;
		}
	}
}