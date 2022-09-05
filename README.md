DiscordAutoThreadBot
--------------------

A simple Discord bot to allow users to automatically join any newly created thread.

The way it works is simple: somebody with admin permission on the Discord can type `@AutoThreadsBot add (user)` to add a user to the list, or `remove` to remove them.

The `(user)` can be either a user ID, or an `@ping` of the user.

Whenever a thread is created, all users on the list get added to the thread.

You can also use `@AutoThreadsBot user (user) whitelist/blacklist (channels)` to configure a specific user to be whitelisted or blacklisted to certain channels. Channels are referenced by ID or with a `#channel` tag. The list can be separated with spaces or commas.

Users on the list can block the bot to be added to the thread without seeing the notification.

There is a maximum of 15 users you can add to the list. This is because adding new users to a thread gets rate limited (4 users per second) so the more you have, the longer the bot has to freeze for each thread.

If you're running your own instance, you can edit this number by editing `GuildDataHelper.cs`, find the `public const int MAXIMUM_PER_LIST = 15;` line and change `15` to whatever you please.

If you reach the limit, check `@AutoThreadsBot list` to see if there's any users you can remove.

You can also configure `@AutoThreadsBot firstmessage (text)` to set a message that the bot will post in any new thread, before adding users. You can use this to ping users to add them into threads too (as a way to reduce the delay for the ratelimit, at the cost of the annoying notification showing up for those users in the ping message).

The bot can also apply username-prefixing if wanted, via `@AutoThreadsBot autoprefix true`. This is a feature we use on the Denizen discord to more readily distinguish threads. It applies the format `(Username) titlehere`, so for example if `mcmonkey` creates a thread `I need help`, it becomes `(mcmonkey) I need help`. This will disable if the thread title already has a prefix.

Anybody who has the `Manage Threads` permission (or who owns a thread) can use `/archive` while in a thread to archive that thread without locking it. This also is available as a `@AutoThreadsBot archive` command. This is left in ThreadBot because Discord's developers have casually deleted the GUI button that does this natively in Discord with no explanation on two separate occasions, leaving us unable to archive threads for several days both times.

### Want To Add The Public Instance?

- Just [click here](https://discord.com/api/oauth2/authorize?client_id=927424149268336691&permissions=292057779200&scope=bot%20applications.commands).

### Setup Your Own Instance

- 0: Before setup: This is intended to run on a Linux server, with `git`, `screen`, and `dotnet-6-sdk` installed. If you're not in this environment... you're on your own for making it work. Should be easy, but I'm only documenting my own use case here.
- 1: Clone this repo with `git clone`
- 2: Make sure to checkout submodules as well: `git submodule update --init --recursive` (the `start.sh` will automatically do this for you)
- 3: create folder `config` at top level
- 4: You need to have a Discord bot already - check [this guide](https://discordpy.readthedocs.io/en/stable/discord.html) if you don't know how to get one. Requires messages intent, and slash commands grant. Make sure to add the bot to your server(s).
- 5: within `config` create file `token.txt` with contents being your Discord bot's token
- 6: `./start.sh`. Will run in a screen which you can attach to with `screen -r autothreadbot`

### Licensing pre-note:

This is an open source project, provided entirely freely, for everyone to use and contribute to.

If you make any changes that could benefit the community as a whole, please contribute upstream.

### The short of the license is:

You can do basically whatever you want (as long as you give credit), except you may not hold any developer liable for what you do with the software.

### The long version of the license follows:

The MIT License (MIT)

Copyright (c) 2022 Alex "mcmonkey" Goodwin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
