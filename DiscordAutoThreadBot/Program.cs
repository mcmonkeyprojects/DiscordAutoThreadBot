using System;
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
                CacheSize = 0,
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
            bot.RegisterCommand(AutoThreadBotCommands.Command_AutoUnlock, "autounlock", "auto-unlock");
            bot.Client.ThreadCreated += (thread) => NewThreadHandle(bot, thread);
            bot.Client.ThreadUpdated += (oldThread, newThread) =>
            {
                if (bot.BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (!oldThread.HasValue)
                {
                    return Task.CompletedTask;
                }
                if (oldThread.Value.IsArchived || !newThread.IsArchived || !newThread.IsLocked)
                {
                    return Task.CompletedTask;
                }
                GuildDataHelper helper = GuildDataHelper.GetHelperFor(newThread.Guild.Id);
                lock (helper.Locker)
                {
                    if (helper.InternalData.AutoUnlock)
                    {
                        try
                        {
                            newThread.ModifyAsync(p => p.Locked = false).Wait();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to unlock thread: {ex}");
                        }
                    }
                }
                return Task.CompletedTask;
            };
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

        /// <summary>The actual primary method of this program. Does the adding of users to threads.</summary>
        public static Task NewThreadHandle(DiscordBot bot, SocketThreadChannel thread) // can't be C# async due to the lock
        {
            if (bot.BotMonitor.ShouldStopAllLogic())
            {
                return Task.CompletedTask;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(thread.Guild.Id);
            lock (helper.Locker)
            {
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
                        thread.AddUserAsync(user).Wait();
                    }
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
