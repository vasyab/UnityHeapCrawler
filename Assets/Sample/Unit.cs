using JetBrains.Annotations;
using UnityEngine;

namespace Sample
{
	public class Unit
	{
		public Vector3 Position;

		public float Rotation;

		public int Hp;

		public bool Enemy;

		[CanBeNull]
		public Unit Pet;

		public string Name = "Unit name";
	}
}