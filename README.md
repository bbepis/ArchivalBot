# ArchivalBot
Channel archiver bot for Discord

Requires a user token to operate. Get a disposable account, and follow these instructions to get it's user token: https://github.com/Tyrrrz/DiscordChatExporter/issues/76#issuecomment-410067054

Currently only supports downloading images from a channel.

Launch arguments:
```
archivalbot --token <YOUR TOKEN> --channel <CHANNEL ID> [--dryrun] [--quick]
```
Writes images to the current directory.

- Dry run mode enumerates over all possible images to download, but doesn't download anything.
- Quick mode will stop scanning the moment it finds a post it has recorded in the database.

Has preliminary embed support:
- Discord uploaded images
- Directly linked images
  - Will automatically get highest quality twitter images
- Most booru sites
- Some other misc. embeds

Some features are currently broken:
- Danbooru/safebooru support
- Twitter post embeds
- Pixiv support
