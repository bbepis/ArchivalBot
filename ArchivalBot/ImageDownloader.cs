using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BooruSharp.Booru;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Nito.AsyncEx;

namespace ArchivalBot
{
	public class ImageDownloader : IDisposable
	{
		private DiscordSocketClient DiscordClient;

		private ITextChannel DiscordChannel;

		private Dictionary<ulong, RestUserMessage> MessagesToTick = new Dictionary<ulong, RestUserMessage>();

		public async Task Initialize(TokenType tokenType, string token, ulong channelId)
		{
			MessagesToTick.Clear();

			DiscordClient = new DiscordSocketClient(new DiscordSocketConfig
			{
				AlwaysDownloadUsers = false
			});

			TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();

			DiscordClient.Ready += () =>
			{
				tcs.SetResult(null);
				return Task.CompletedTask;
			};

			await DiscordClient.LoginAsync(tokenType, token);

			_ = DiscordClient.StartAsync();

			await tcs.Task;

			DiscordChannel = (ITextChannel)DiscordClient.GetChannel(channelId);
		}

		public async Task<IEnumerable<Content>> GetContentChanges(IEnumerable<Content> existingContent, bool fastMode)
		{
			var messages = DiscordChannel.GetMessagesAsync(int.MaxValue);
			ConcurrentStack<Content> messagesToGet = new ConcurrentStack<Content>();

			int totalEnumerated = 0;
			
			AsyncProducerConsumerQueue<IMessage> messageCollection = new AsyncProducerConsumerQueue<IMessage>(8000);

			bool readingComplete = false;

			var enqueueTask = Task.Run(async () =>
			{
				try
				{
					using (var enumerator = messages.GetEnumerator())
					{
						while (!readingComplete && await enumerator.MoveNext())
						{
							Console.WriteLine($"Enumerated {totalEnumerated} / 0");

							foreach (var message in enumerator.Current)
							{
								totalEnumerated++;

								messageCollection.Enqueue(message);
							}
						}
					}

					Console.WriteLine($"Enumerated {totalEnumerated} / {totalEnumerated}");
				}
				finally
				{
					messageCollection.CompleteAdding();
				}
			});

			List<Task> embedTasks = new List<Task>();

			foreach (var message in messageCollection.GetConsumingEnumerable())
			{
				if (existingContent.Any(y => (ulong)y.UserHash == message.Id))
				{
					if (fastMode)
						break;

					continue;
				}

				foreach (var attachment in message.Attachments)
				{
					var content = new Content
					{
						LastModifiedTime = message.Timestamp.DateTime,
						Text = attachment.Filename,
						UserHash = (long)message.Id,
						Metadata = new Dictionary<string, string>(5)
						{
							["filename"] = attachment.Filename,
							["ID"] = attachment.Id.ToString(),
							["messageText"] = message.Content,
							["url"] = attachment.Url,
							["proxyUrl"] = attachment.ProxyUrl
						}
					};

					if (message.Channel.Id == 464602747786887178)
					{
						MessagesToTick[message.Id] = (RestUserMessage)message;
					}

					messagesToGet.Push(content);
				}

				foreach (var embed in message.Embeds)
				{
					embedTasks.Add(ProcessEmbed(messagesToGet, message, embed));
				}
			}

			readingComplete = true;

			await Task.WhenAll(embedTasks.ToArray());

			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect();

			return messagesToGet;
		}

		protected static Regex TwitterIdRegex = new Regex(@"^.*twitter\.com\/.+\/status\/(?<id>\d+)", RegexOptions.Compiled);

		protected async Task ProcessEmbed(ConcurrentStack<Content> contentStack, IMessage message, IEmbed embed)
		{
			try
			{
				if (embed.Url == null)
					return;

				var baseUri = new Uri(embed.Url);
				List<string> urls = new List<string>();
				string proxyUrl = null;

				if (embed.Type == EmbedType.Image)
				{
					string url = embed.Image?.Url ??
								 (Path.HasExtension(embed.Url)
									 ? embed.Url
									 : embed.Thumbnail?.Url ?? embed.Url);

					url = url.Replace(":large", ":orig")
							 .Replace(":small", ":orig")
							 .Replace(":thumb", ":orig");

					urls.Add(url);

					proxyUrl = embed.Image?.ProxyUrl ?? embed.Thumbnail?.ProxyUrl;
				}
				else if (embed.Type == EmbedType.Rich)
				{
					// Twitter logic is supposed to be here but I don't have an API key

					Console.WriteLine($"Warning: unrecognized rich embed found: {embed.Url}");
					return;
				}
				else if (embed.Type == EmbedType.Gifv)
				{
					urls.Add(embed.Video?.Url ?? embed.Url);
				}
				else if (embed.Type == EmbedType.Video)
				{
					if (!embed.Video.HasValue)
					{
						Console.WriteLine($"Warning: Video embed found without video: {embed.Url}");
						return;
					}

					urls.Add(embed.Video.Value.Url);
				}
				else if (embed.Type == EmbedType.Link || embed.Type == EmbedType.Article)
				{
					if (baseUri.Host.Equals("danbooru.donmai.us", StringComparison.OrdinalIgnoreCase))
					{
						if (baseUri.Segments.Length < 3 || !baseUri.Segments[1].Equals("posts/", StringComparison.OrdinalIgnoreCase))
							return;

						int id = int.Parse(baseUri.Segments[2]);

						var image = await new DanbooruDonmai().GetImage(id);

						if (image.fileUrl == null)
						{
							Console.WriteLine($"Warning: could not return image for {baseUri}");
						}
						else
						{
							urls.Add((await new DanbooruDonmai().GetImage(id)).fileUrl.AbsoluteUri);
						}
					}
					else if (baseUri.Host.Equals("safebooru.donmai.us", StringComparison.OrdinalIgnoreCase))
					{
						if (baseUri.Segments.Length < 3 || !baseUri.Segments[1].Equals("posts/", StringComparison.OrdinalIgnoreCase))
							return;

						int id = int.Parse(baseUri.Segments[2]);

						urls.Add((await new DanbooruDonmai().GetImage(id)).fileUrl.AbsoluteUri);
					}
					else if (baseUri.Host.Equals("gelbooru.com", StringComparison.OrdinalIgnoreCase))
					{
						var query = System.Web.HttpUtility.ParseQueryString(baseUri.Query);

						int id = int.Parse(query["id"]);

						urls.Add((await new Gelbooru().GetImage(id)).fileUrl.AbsoluteUri);
					}
					else if (baseUri.Host.Equals("rule34.xxx", StringComparison.OrdinalIgnoreCase))
					{
						var query = System.Web.HttpUtility.ParseQueryString(baseUri.Query);

						int id = int.Parse(query["id"]);

						urls.Add((await new Rule34().GetImage(id)).fileUrl.AbsoluteUri);
					}
					else if (baseUri.Host.Equals("yande.re", StringComparison.OrdinalIgnoreCase))
					{
						if (baseUri.Segments.Length < 4
							|| !baseUri.Segments[1].Equals("post/", StringComparison.OrdinalIgnoreCase)
							|| !baseUri.Segments[2].Equals("show/", StringComparison.OrdinalIgnoreCase))
							return;

						int id = int.Parse(baseUri.Segments[3]);

						urls.Add((await new Yandere().GetImage(id)).fileUrl.AbsoluteUri);
					}
					else
					{
						Console.WriteLine($"Warning: unrecognized article embed found: {embed.Url}");

						if (embed.Thumbnail.HasValue)
							urls.Add(embed.Thumbnail.Value.Url);
						else
							return;
					}
				}
				else
				{
					Console.WriteLine($"Warning: Unrecognized embed found: ({embed.Type} {embed.Url}");
					return;
				}



				foreach (string u in urls)
				{
					string url = u;

					string ext = Path.GetExtension(url);

					if (ext.Contains("%"))
						url = Path.ChangeExtension(url, ext.Remove(ext.IndexOf('%')));

					string filename = Path.GetFileName(url);

					if (string.IsNullOrWhiteSpace(filename))
						filename = message.Id.ToString();

					filename = filename.Replace(":orig", "");

					int qIndex = filename.IndexOf('?');
					if (qIndex >= 0)
						filename = filename.Remove(qIndex);

					var content = new Content
					{
						LastModifiedTime = message.Timestamp.DateTime,
						Text = filename,
						UserHash = (long)message.Id,
						Metadata = new Dictionary<string, string>(5)
						{
							["filename"] = filename,
							["ID"] = message.Id.ToString(),
							["messageText"] = message.Content,
							["url"] = url,
							["proxyUrl"] = proxyUrl
						}
					};

					contentStack.Push(content);
					
					if (message.Channel.Id == 464602747786887178)
					{
						MessagesToTick[message.Id] = (RestUserMessage)message;
					}
				}
			}
			catch (Exception unhandledException)
			{
				Console.WriteLine($"UNHANDLED EXCEPTION: {string.Join(", ", message.Embeds.Select(x => x.Url))}");
				Console.WriteLine(unhandledException);
			}
		}

		public async Task<IList<Content>> PullContent(IList<Content> content, string folderPath, IProgress<string> statusProgress)
		{
			int counter = 0;

			Dictionary<Content, string> allocatedFilenames = new Dictionary<Content, string>();
			List<Content> pulledContent = new List<Content>();

#warning SSL certificate error workaround for one of the booru sites
			ServicePointManager.ServerCertificateValidationCallback =
				(s, certificate, chain, sslPolicyErrors) => true;

			foreach (var c in content.OrderBy(x => x.LastModifiedTime))
			{
				string filename = Path.Combine(folderPath, c.Metadata["filename"]);

				filename = Utility.GetNewFilename(filename, allocatedFilenames.Values);

				allocatedFilenames[c] = filename;
			}

			int total = content.Count;
			
			var checkEmoji = new Emoji("✅");

			SemaphoreSlim reactSemaphore = new SemaphoreSlim(1);

			await content.ForEachAsync(50, x =>
			{
				return Task.Run(async () =>
				{
					string filename = allocatedFilenames[x];

					try
					{
						var source = new CancellationTokenSource(TimeSpan.FromSeconds(120));

						using (var webClient = new WebClient())
						using (var outputFilestream = new FileStream(filename, FileMode.Create))
						{
							var stream = await webClient.OpenReadTaskAsync(x.Metadata["url"]).ConfigureAwait(false);

							await stream.CopyToAsync(outputFilestream, source.Token).ConfigureAwait(false);
						}

						if (DiscordChannel.Id == 464602747786887178)
						{
							await reactSemaphore.WaitAsync();

							await Task.Delay(500);
							
							try
							{
								var message = //await DiscordChannel.GetMessageAsync((ulong)x.UserHash);
									MessagesToTick[(ulong)x.UserHash];

								var restMessage = ((RestUserMessage)message);

								if (!restMessage.Reactions.ContainsKey(checkEmoji))
									await ((RestUserMessage)message).AddReactionAsync(checkEmoji);
							}
							finally
							{
								reactSemaphore.Release();
							}

							//((SocketTextChannel)channel.
						}

						File.SetLastWriteTime(filename, x.LastModifiedTime);
						File.SetCreationTime(filename, x.LastModifiedTime);

						pulledContent.Add(x);

						statusProgress.Report($"[{Interlocked.Increment(ref counter)} / {total}] Pulled {x.Text}");
					}
					catch (Exception ex)
					{
						lock (content)
						{
							statusProgress.Report($"Failed to pull \"{x.Text}\"");
							statusProgress.Report("Url: " + x.Metadata["url"]);
							statusProgress.Report(ex.Message);
						}

						try
						{
							if (File.Exists(filename))
								File.Delete(filename);
						}
						catch
						{
							statusProgress.Report($"Unable to delete file {x.Text}; file contention?");
						}

						Interlocked.Increment(ref counter);
					}
				});
			});

			return pulledContent;
		}

		public void Dispose()
		{
			DiscordClient.LogoutAsync().Wait();
			DiscordClient.StopAsync().Wait();

			DiscordClient.Dispose();
		}
	}
}