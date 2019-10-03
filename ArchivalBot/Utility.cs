using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArchivalBot
{
	public static class Utility
	{
		public static async Task ForEachAsync<T>(this IEnumerable<T> source, int dop, Func<T, Task> body)
		{
			var semaphore = new SemaphoreSlim(dop);

			await Task.WhenAll(source.Select(async x =>
			{
				await semaphore.WaitAsync();

				try
				{
					await body(x);
				}
				finally
				{
					semaphore.Release();
				}
			}));
		}

		public static string GetSize(long size)
		{
			double dSize = size;
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			int order = 0;
			while (dSize >= 1024 && order < sizes.Length - 1)
			{
				order++;
				dSize = dSize / 1024;
			}

			return $"{dSize:0.00} {sizes[order]}";
		}

		public static string GetNewFilename(string filename, ICollection<string> allocatedNames = null)
		{
			if (!File.Exists(filename) && (allocatedNames == null || !allocatedNames.Contains(filename)))
				return filename;

			string directory = Path.GetDirectoryName(filename);
			string localFilename = Path.GetFileNameWithoutExtension(filename);
			string extension = Path.GetExtension(filename);

			int currentNumber = 1;

			string newFilename;

			do
			{
				newFilename = Path.Combine(directory, $"{localFilename} ({currentNumber++}){extension}");
			} while (File.Exists(newFilename) || (allocatedNames != null && allocatedNames.Contains(newFilename)));

			return newFilename;
		}
	}
}