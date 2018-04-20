using System.Collections.Generic;
using JetBrains.Annotations;

namespace Sample
{
	public class Game
	{
		public static Game Instance = new Game();

		[NotNull]
		public UnitsGroup Enemies;

		[NotNull]
		private Unit player;

		[NotNull]
		private List<Unit> allUnits = new List<Unit>();

		public Game()
		{
			player = new Unit();
			var pet = new Unit();
			var enemy1 = new Unit();
			var enemy2 = new Unit();

			player.Pet = pet;

			allUnits.Add(player);
			allUnits.Add(pet);
			allUnits.Add(enemy1);
			allUnits.Add(enemy2);

			Enemies = new UnitsGroup(enemy1, enemy2);
		}
	}
}
