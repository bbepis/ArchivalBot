using System;
using System.Collections.Generic;

namespace ArchivalBot
{
	public class Content
	{
		public long Id { get; set; }
		public long UserHash { get; set; }
		public string Text { get; set; }
		public DateTime LastModifiedTime { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}