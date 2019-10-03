using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using LiteDB;

namespace ArchivalBot
{
	class Program
	{
		static async Task Main(string[] args)
		{
			using var imageDownloader = new ImageDownloader();
			var arguments = Arguments.Parse(args, new[] {"channel", "token"});

			var quickMode = arguments.Switches.ContainsKey("quick");
			var dryRun = arguments.Switches.ContainsKey("dryRun");


			Console.WriteLine("Initializing...");
			await imageDownloader.Initialize(TokenType.User, arguments["token"], ulong.Parse(arguments["channel"]));

			using (var db = new LiteDatabase(Path.Combine(Environment.CurrentDirectory, "downloads.db")))
			{
				var syncedImages = db.GetCollection<Content>("SyncedImages");

				Console.WriteLine("Getting changes...");

				var changes = (await imageDownloader.GetContentChanges(syncedImages.FindAll(), quickMode)).ToList();

				Console.WriteLine($"{changes.Count} changes found");

				if (!dryRun)
				{
					await imageDownloader.PullContent(changes, Environment.CurrentDirectory, new Progress<string>(Console.WriteLine));

					Console.WriteLine("Saving repo database...");

					syncedImages.InsertBulk(changes);
					db.Shrink();

					Console.WriteLine("Finished pulling changes!");
				}
			}
		}
	}
}