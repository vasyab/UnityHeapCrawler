using System;
using JetBrains.Annotations;
using UnityEngine;

namespace Sample
{
	public class UnitComponent : MonoBehaviour
	{
		[CanBeNull]
		[NonSerialized]
		public Collider Collider;

		[NotNull]
		public UnitVisualSettings VisualSettings = new UnitVisualSettings();

		private void Awake()
		{
			Collider = GetComponent<Collider>();
		}
	}
}