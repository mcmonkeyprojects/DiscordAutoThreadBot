﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Loader;
using DiscordBotBase;
using Discord.WebSocket;
using Discord;
using System.Threading;
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;

namespace DiscordAutoThreadBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            SpecialTools.Internationalize();
            AssemblyLoadContext.Default.Unloading += (context) =>
            {
                GuildDataHelper.Shutdown();
            };
            AppDomain.CurrentDomain.ProcessExit += (obj, e) =>
            {
                GuildDataHelper.Shutdown();
            };
            Directory.CreateDirectory("./config/saves");
            DiscordBotConfig config = new()
            {
                CacheSize = 5, // Small cache to reduce chance of bot forgetting channels exist
                EnsureCaching = false,
                Initialize = Initialize,
                CommandPrefix = null,
                ShouldPayAttentionToMessage = (message) => message is SocketUserMessage uMessage && uMessage.Channel is SocketGuildChannel,
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMessages | GatewayIntents.GuildMembers,
                OnShutdown = () =>
                {
                    ConsoleCancelToken.Cancel();
                    GuildDataHelper.Shutdown();
                }
            };
            Task consoleThread = Task.Run(ConsoleLoop, ConsoleCancelToken.Token);
            DiscordBotBaseHelper.StartBotHandler(args, config);
        }

        public static CancellationTokenSource ConsoleCancelToken = new();

        public static void Initialize(DiscordBot bot)
        {
            bot.RegisterCommand(AutoThreadBotCommands.Command_Help, "help", "halp", "hlp", "?");
            bot.RegisterCommand(AutoThreadBotCommands.Command_List, "list");
            bot.RegisterCommand(AutoThreadBotCommands.Command_Add, "add");
            bot.RegisterCommand(AutoThreadBotCommands.Command_Remove, "remove");
            bot.RegisterCommand(AutoThreadBotCommands.Command_User, "user");
            bot.RegisterCommand(AutoThreadBotCommands.Command_FirstMessage, "firstmessage");
            bot.RegisterCommand(AutoThreadBotCommands.Command_AutoPrefix, "autoprefix");
            bot.Client.ThreadCreated += (thread) => NewThreadHandle(bot, thread);
            bot.Client.Ready += () =>
            {
                bot.Client.SetGameAsync("for new threads", type: ActivityType.Watching);
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        foreach (SocketGuild guild in bot.Client.Guilds)
                        {
                            Console.WriteLine($"First-load of users in guild: {guild.Id}");
                            guild.DownloadUsersAsync().Wait();
                            Console.WriteLine($"Completed first-load of guild: {guild.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Loading error: {ex}");
                    }
                });
                try
                {
                    const string commandVersionFile = "./config/command_registered_version.dat";
                    const int commandVersion = 2;
                    if (!File.Exists(commandVersionFile) || !int.TryParse(commandVersionFile, out int registered) || registered < commandVersion)
                    {
                        RegisterSlashCommands(bot);
                        File.WriteAllText(commandVersionFile, commandVersion.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to update slash commands: {ex}");
                }
                return Task.CompletedTask;
            };
            bot.Client.GuildAvailable += (guild) =>
            {
                Task.Factory.StartNew(() =>
                {
                    Console.WriteLine($"Seen new guild: {guild.Id}");
                    try
                    {
                        guild.DownloadUsersAsync().Wait();
                        Console.WriteLine($"Completed load of guild: {guild.Id}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Guild-scanning error: {ex}");
                    }
                });
                return Task.CompletedTask;
            };
        }

        public static void RegisterSlashCommands(DiscordBot bot)
        {
            // Clear out historical slash commands
            SocketApplicationCommand cmd = bot.Client.GetGlobalApplicationCommandsAsync().Result.FirstOrDefault(cmd => cmd.Name == "archive");
            if (cmd is not null)
            {
                cmd.DeleteAsync().Wait();
            }
        }

        /// <summary>Temporary (in-RAM) list of seen threads, to avoid duplication.</summary>
        public static HashSet<ulong> SeenThreads = new();

        /// <summary>The actual primary method of this program. Does the adding of users to threads.</summary>
        public static Task NewThreadHandle(DiscordBot bot, SocketThreadChannel thread)
        {
            if (bot.BotMonitor.ShouldStopAllLogic())
            {
                return Task.CompletedTask;
            }
            if (!SeenThreads.Add(thread.Id))
            {
                return Task.CompletedTask;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(thread.Guild.Id);
            lock (helper.Locker)
            {
                List<Task> tasks = new();
                if (helper.InternalData.AutoPrefix && !thread.Name.StartsWithFast('(') && !thread.Name.StartsWithFast('['))
                {
                    string senderName = null;
                    int tries = 0;
                    while (true)
                    {
                        thread.GetMessagesAsync().ForEachAsync((list) =>
                        {
                            if (!list.IsEmpty())
                            {
                                IGuildUser user = list.First() as IGuildUser;
                                if (user is not null)
                                {
                                    senderName = user.Nickname ?? user.Username;
                                }
                            }
                        }).Wait();
                        if (senderName is null)
                        {
                            if (tries++ > 10)
                            {
                                break;
                            }
                            Task.Delay(100).Wait();
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (senderName is not null && !thread.Name.StartsWithFast('(') && !thread.Name.StartsWithFast('[')) // reverify in case it changed
                    {
                        if (senderName.Length > 12)
                        {
                            senderName = senderName[0..10];
                        }
                        tasks.Add(thread.ModifyAsync(t => t.Name = $"({senderName}) {thread.Name}"));
                    }
                }
                if (!string.IsNullOrWhiteSpace(helper.InternalData.FirstMessage))
                {
                    tasks.Add(thread.SendMessageAsync(text: helper.InternalData.FirstMessage));
                }
                foreach (ulong userId in helper.InternalData.Users.ToArray()) // ToArray to allow 'Remove' call
                {
                    SocketGuildUser user = thread.Guild.GetUser(userId);
                    if (user is null)
                    {
                        Console.WriteLine($"Failed to add thread user {user.Id}");
                        thread.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Error").WithDescription($"Failed to add user {user.Id} - did they leave the Discord?").Build()).Wait();
                        helper.InternalData.Users.Remove(userId);
                        helper.Modified = true;
                    }
                    else
                    {
                        if (!helper.InternalData.UserData.TryGetValue(userId, out GuildDataHelper.UserData data) || data.ChannelLimit.IsEmpty() || data.ChannelLimit.Contains(thread.ParentChannel.Id) == data.IsWhitelist)
                        {
                            tasks.Add(thread.AddUserAsync(user));
                        }
                    }
                }
                foreach (Task task in tasks)
                {
                    task.Wait();
                }
            }
            return Task.CompletedTask;
        }

        public static async void ConsoleLoop()
        {
            while (true)
            {
                string line = await Console.In.ReadLineAsync();
                if (line == null)
                {
                    return;
                }
                string[] split = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (split.IsEmpty())
                {
                    continue;
                }
                switch (split[0])
                {
                    case "stop":
                        {
                            Console.WriteLine("Clearing up...");
                            GuildDataHelper.Shutdown();
                            Console.WriteLine("Shutting down...");
                            Environment.Exit(0);
                        }
                        break;
                    default:
                        Console.WriteLine("Unknown command. Use 'stop' to close the process, or consult internal code for secondary options.");
                        break;
                }
            }
        }
    }
}
