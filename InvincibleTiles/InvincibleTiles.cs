﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Data;
using System.IO;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Terraria;

namespace InvincibleTiles
{
	[ApiVersion(1, 16)]
	public class InvincibleTiles : TerrariaPlugin
	{
		private Dictionary<string, List<int>> blacklistedTiles = new Dictionary<string, List<int>>();
		private Dictionary<string, List<int>> blacklistedWalls = new Dictionary<string, List<int>>();
		private Dictionary<string, List<int>> blacklistedWires = new Dictionary<string, List<int>>();
		private IDbConnection db;

		#region Info
		public override Version Version
		{
			get { return Assembly.GetExecutingAssembly().GetName().Version; }
		}

		public override string Name
		{
			get { return "Invincible Tiles"; }
		}

		public override string Author
		{
			get { return "Zack"; }
		}

		public override string Description
		{
			get { return "Makes certain tiles indestructable"; }
		}
		#endregion

		public InvincibleTiles(Main game)
			: base(game)
		{
			Order = 4;
			TShock.Initialized += Start;
		}

		public override void Initialize()
		{
			Commands.ChatCommands.Add(new Command("it.blackTile", AddTile, "blacktile", "bt"));
			Commands.ChatCommands.Add(new Command("it.whiteTile", DelTile, "whitetile", "wt"));
			Commands.ChatCommands.Add(new Command("it.blackWall", AddWall, "blackwall", "bw"));
			Commands.ChatCommands.Add(new Command("it.whiteWall", DelWall, "whitewall", "ww"));
			Commands.ChatCommands.Add(new Command("it.blackWire", AddWire, "blackwire", "bwi"));
			Commands.ChatCommands.Add(new Command("it.whiteWire", DelWire, "whitewire", "wwi"));
			GetDataHandlers.TileEdit += TileKill;
		}

		private void Start()
		{
			SetupDb();
			ReadDb();
		}

		private void SetupDb()
		{
			if (TShock.Config.StorageType.ToLower() == "sqlite")
			{
				string sql = Path.Combine(TShock.SavePath, "invincible_tiles.sqlite");
				db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
			}
			else if (TShock.Config.StorageType.ToLower() == "mysql")
			{
				try
				{
					var hostport = TShock.Config.MySqlHost.Split(':');
					db = new MySqlConnection();
					db.ConnectionString =
						String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
									  hostport[0],
									  hostport.Length > 1 ? hostport[1] : "3306",
									  TShock.Config.MySqlDbName,
									  TShock.Config.MySqlUsername,
									  TShock.Config.MySqlPassword
							);
				}
				catch (MySqlException ex)
				{
					Log.Error(ex.ToString());
					throw new Exception("MySql not setup correctly");
				}
			}
			else
			{
				throw new Exception("Invalid storage type");
			}

			var table2 = new SqlTable("BlacklistedTiles",
									 new SqlColumn("ID", MySqlDbType.String),
									 new SqlColumn("Type", MySqlDbType.Int32) { DefaultValue = "0" },
									 new SqlColumn("Region", MySqlDbType.String)
				);
			var creator2 = new SqlTableCreator(db,
											  db.GetSqlType() == SqlType.Sqlite
												? (IQueryBuilder)new SqliteQueryCreator()
												: new MysqlQueryCreator());
			creator2.EnsureExists(table2);
		}

		private void ReadDb()
		{
			String query = "SELECT * FROM BlacklistedTiles";

			var reader = db.QueryReader(query);

			while (reader.Read())
			{
				string id = reader.Get<string>("ID");
				int type = reader.Get<int>("Type");
				string region = reader.Get<string>("Region");
				if (type == 0)
				{
					blacklistedTiles.Add(region, id.ToIDList());
				}
				else if (type == 1)
				{
					blacklistedWalls.Add(region, id.ToIDList());
				}
				else if (type == 2)
				{
					blacklistedWires.Add(region, id.ToIDList());
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				TShock.Initialized -= Start;
				GetDataHandlers.TileEdit -= TileKill;
			}
			base.Dispose(disposing);
		}

		#region AddTile
		private void AddTile(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendMessage("You must specify a tile to add.", Color.Red);
				return;
			}
			string tile = args.Parameters[0];
			int id;
			string region = "";
			if (args.Parameters.Count > 1)
			{
				region = TShock.Regions.GetRegionByName(args.Parameters[1]).Name;
			}
			if (!int.TryParse(tile, out id))
			{
				args.Player.SendMessage(String.Format("Tile id '{0}' is not a valid number.", id), Color.Red);
				return;
			}

			String query;
			if (blacklistedTiles.ContainsKey(region) && !blacklistedTiles[region].Contains(id))
			{
				blacklistedTiles[region].Add(id);
				query = "UPDATE BlacklistedTiles SET ID = @0 WHERE Type = @1 AND Region = @2";
			}
			else
			{
				blacklistedTiles.Add(region, new List<int>() { id });
				query = "INSERT INTO BlacklistedTiles (ID, Type, Region) VALUES (@0,@1,@2);";
			}

			if (db.Query(query, blacklistedTiles[region].IDToDBString(), 0, region) != 1)
			{
				Log.ConsoleError("Inserting into the database has failed!");
				args.Player.SendMessage(String.Format("Inserting into the database has failed!", id), Color.Red);
			}
			else
			{
				args.Player.SendMessage(String.Format("Successfully banned {0}", id), Color.Red);
			}
		}
		#endregion

		#region DelTile
		private void DelTile(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendMessage("You must specify a tile to remove.", Color.Red);
				return;
			}
			string tile = args.Parameters[0];
			int id;
			string region = "";
			if (args.Parameters.Count > 1)
			{
				region = TShock.Regions.GetRegionByName(args.Parameters[1]).Name;
			}
			if (!int.TryParse(tile, out id))
			{
				args.Player.SendMessage(String.Format("Tile id '{0}' is not a valid number.", id), Color.Red);
				return;
			}
			String query;
			if (blacklistedTiles.ContainsKey(region) && blacklistedTiles[region].Contains(id))
			{
				blacklistedTiles[region].Remove(id);
				query = "UPDATE BlacklistedTiles SET ID = @0 WHERE Type = @1 AND Region = @2";
			}
			else
			{
				args.Player.SendErrorMessage("{0} is not banned!", id);
				return;
			}

			if (db.Query(query, blacklistedTiles[region].IDToDBString(), 0, region) != 1)
			{
				Log.ConsoleError("Removing from the database has failed!");
				args.Player.SendMessage(String.Format("Removing from the database has failed!  Are you sure {0} is banned?", id), Color.Red);
			}
			else
			{
				args.Player.SendMessage(String.Format("Successfully unbanned {0}", id), Color.Green);
			}

			if (blacklistedTiles[region].Count < 1)
			{
				db.Query("DELETE FROM BlacklistedTiles WHERE Type = @0 AND Region = @1", 0, region);
				blacklistedTiles.Remove(region);
			}
		}
		#endregion

		#region AddWall
		private void AddWall(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendMessage("You must specify a wall to add.", Color.Red);
				return;
			}
			string tile = args.Parameters[0];
			int id;
			string region = "";
			if (args.Parameters.Count > 1)
			{
				region = TShock.Regions.GetRegionByName(args.Parameters[1]).Name;
			}
			if (!int.TryParse(tile, out id))
			{
				args.Player.SendMessage(String.Format("Wall id '{0}' is not a valid number.", id), Color.Red);
				return;
			}

			String query;
			if (blacklistedWalls.ContainsKey(region) && !blacklistedWalls[region].Contains(id))
			{
				blacklistedWalls[region].Add(id);
				query = "UPDATE BlacklistedTiles SET ID = @0 WHERE Type = @1 AND Region = @2";
			}
			else
			{
				blacklistedWalls.Add(region, new List<int>() { id });
				query = "INSERT INTO BlacklistedTiles (ID, Type, Region) VALUES (@0,@1,@2);";
			}

			if (db.Query(query, blacklistedWalls[region].IDToDBString(), 1, region) != 1)
			{
				Log.ConsoleError("Inserting into the database has failed!");
				args.Player.SendMessage(String.Format("Inserting into the database has failed!", id), Color.Red);
			}
			else
			{
				args.Player.SendMessage(String.Format("Successfully banned {0}", id), Color.Red);
			}
		}
		#endregion

		#region DelWall
		private void DelWall(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendMessage("You must specify a wall to remove.", Color.Red);
				return;
			}
			string tile = args.Parameters[0];
			int id;
			string region = "";
			if (args.Parameters.Count > 1)
			{
				region = TShock.Regions.GetRegionByName(args.Parameters[1]).Name;
			}
			if (!int.TryParse(tile, out id))
			{
				args.Player.SendMessage(String.Format("Wall id '{0}' is not a valid number.", id), Color.Red);
				return;
			}
			String query;
			if (blacklistedWalls.ContainsKey(region) && blacklistedWalls[region].Contains(id))
			{
				blacklistedWalls[region].Remove(id);
				query = "UPDATE BlacklistedTiles SET ID = @0 WHERE Type = @1 AND Region = @2";
			}
			else
			{
				args.Player.SendErrorMessage("{0} is not banned!", id);
				return;
			}

			if (db.Query(query, blacklistedWalls[region].IDToDBString(), 1, region) != 1)
			{
				Log.ConsoleError("Removing from the database has failed!");
				args.Player.SendMessage(String.Format("Removing from the database has failed!  Are you sure {0} is banned?", id), Color.Red);
			}
			else
			{
				args.Player.SendMessage(String.Format("Successfully unbanned {0}", id), Color.Green);
			}

			if (blacklistedWalls[region].Count < 1)
			{
				db.Query("DELETE FROM BlacklistedTiles WHERE Type = @0 AND Region = @1", 1, region);
				blacklistedWalls.Remove(region);
			}
		}
		#endregion

		#region AddWire
		private void AddWire(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendMessage("You must specify a wire to add.", Color.Red);
				return;
			}
			string tile = args.Parameters[0];
			int id;
			string region = "";
			if (args.Parameters.Count > 1)
			{
				region = TShock.Regions.GetRegionByName(args.Parameters[1]).Name;
			}
			if (!int.TryParse(tile, out id))
			{
				args.Player.SendMessage(String.Format("Wire id '{0}' is not a valid number.", id), Color.Red);
				return;
			}

			String query;
			if (blacklistedWires.ContainsKey(region) && !blacklistedWires[region].Contains(id))
			{
				blacklistedWires[region].Add(id);
				query = "UPDATE BlacklistedTiles SET ID = @0 WHERE Type = @1 AND Region = @2";
			}
			else
			{
				blacklistedWires.Add(region, new List<int>() { id });
				query = "INSERT INTO BlacklistedTiles (ID, Type, Region) VALUES (@0,@1,@2);";
			}

			if (db.Query(query, blacklistedWires[region].IDToDBString(), 2, region) != 1)
			{
				Log.ConsoleError("Inserting into the database has failed!");
				args.Player.SendMessage(String.Format("Inserting into the database has failed!", id), Color.Red);
			}
			else
			{
				args.Player.SendMessage(String.Format("Successfully banned {0}", id), Color.Red);
			}
		}
		#endregion

		#region DelWire
		private void DelWire(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendMessage("You must specify a wire to remove.", Color.Red);
				return;
			}
			string tile = args.Parameters[0];
			int id;
			string region = "";
			if (args.Parameters.Count > 1)
			{
				region = TShock.Regions.GetRegionByName(args.Parameters[1]).Name;
			}
			if (!int.TryParse(tile, out id))
			{
				args.Player.SendMessage(String.Format("Wall id '{0}' is not a valid number.", id), Color.Red);
				return;
			}
			String query;
			if (blacklistedWires.ContainsKey(region) && blacklistedWires[region].Contains(id))
			{
				blacklistedWires[region].Remove(id);
				query = "UPDATE BlacklistedTiles SET ID = @0 WHERE Type = @1 AND Region = @2";
			}
			else
			{
				args.Player.SendErrorMessage("{0} is not banned!", id);
				return;
			}

			if (db.Query(query, blacklistedWires[region].IDToDBString(), 2, region) != 1)
			{
				Log.ConsoleError("Removing from the database has failed!");
				args.Player.SendMessage(String.Format("Removing from the database has failed!  Are you sure {0} is banned?", id), Color.Red);
			}
			else
			{
				args.Player.SendMessage(String.Format("Successfully unbanned {0}", id), Color.Green);
			}

			if (blacklistedWires[region].Count < 1)
			{
				db.Query("DELETE FROM BlacklistedTiles WHERE Type = @0 AND Region = @1", 1, region);
				blacklistedWires.Remove(region);
			}
		}
		#endregion

		#region TileKill
		private void TileKill(object sender, GetDataHandlers.TileEditEventArgs args)
		{
			if (args.Player.Group.HasPermission("breakinvincible"))
				return;

			var wall = Main.tile[args.X, args.Y].wall;
			var type = Main.tile[args.X, args.Y].type;

			if (
				(args.Action == GetDataHandlers.EditAction.KillWall && blacklistedWalls.IsBanned(args.X, args.Y, wall)) ||
				((args.Action == GetDataHandlers.EditAction.KillTile || args.Action == GetDataHandlers.EditAction.KillTileNoItem || args.Action == GetDataHandlers.EditAction.PoundTile) && blacklistedTiles.IsBanned(args.X, args.Y, type)) ||
				(args.Action == GetDataHandlers.EditAction.KillWire && blacklistedWires.IsBanned(args.X, args.Y, 1)) ||
				(args.Action == GetDataHandlers.EditAction.KillWire2 && blacklistedWires.IsBanned(args.X, args.Y, 2)) ||
				(args.Action == GetDataHandlers.EditAction.KillWire3 && blacklistedWires.IsBanned(args.X, args.Y, 3))
				)
			{
				args.Handled = true;
				TSPlayer.All.SendTileSquare(args.X, args.Y, 1);
			}
		}
		#endregion
	}
}