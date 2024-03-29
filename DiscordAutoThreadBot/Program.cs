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
using System.Collections.Concurrent;
using Discord.Rest;

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
            SeenThreads.Clear();
            bot.RegisterCommand(AutoThreadBotCommands.Command_Help, "help", "halp", "hlp", "?");
            bot.RegisterCommand(AutoThreadBotCommands.Command_List, "list");
            bot.RegisterCommand(AutoThreadBotCommands.Command_Add, "add");
            bot.RegisterCommand(AutoThreadBotCommands.Command_Remove, "remove");
            bot.RegisterCommand(AutoThreadBotCommands.Command_User, "user");
            bot.RegisterCommand(AutoThreadBotCommands.Command_FirstMessage, "firstmessage");
            bot.RegisterCommand(AutoThreadBotCommands.Command_ExtraMessage, "extramessage");
            bot.RegisterCommand(AutoThreadBotCommands.Command_AutoPrefix, "autoprefix");
            bot.RegisterCommand(AutoThreadBotCommands.Command_AutoPin, "autopin");
            bot.RegisterCommand(AutoThreadBotCommands.Command_Archive, "archive");
            bot.RegisterCommand(AutoThreadBotCommands.Command_RoleLimit, "rolelimit");
            bot.RegisterCommand(AutoThreadBotCommands.Command_AdminRole, "adminrole");
            bot.RegisterSlashCommand(AutoThreadBotCommands.SlashCommand_Archive, "archive");
            bot.Client.ThreadCreated += (thread) => NewThreadHandle(bot, thread);
            bot.Client.MessageReceived += (message) => NewMessageHandle(message);
            bot.Client.Ready += () =>
            {
                bot.Client.SetGameAsync("for new threads", type: ActivityType.Watching);
                Task.Run(() =>
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
                    const int commandVersion = 3;
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
                Task.Run(() =>
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
            SlashCommandBuilder archiveCommand = new SlashCommandBuilder().WithName("archive").WithDescription("Moves the current thread into archive without locking it. Requires 'Manage Threads' permission.");
            bot.Client.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[] { archiveCommand.Build() });
        }

        /// <summary>Temporary (in-RAM) list of seen threads, to avoid duplication.</summary>
        public static ConcurrentDictionary<ulong, byte> SeenThreads = new();

        public static ConcurrentDictionary<ulong, Action<SocketMessage>> MessageSpecialHandlers = new();

        public static Task NewMessageHandle(SocketMessage message)
        {
            if (!MessageSpecialHandlers.ContainsKey(message.Channel.Id))
            {
                // Ignore
            }
            else if (message.Author.IsBot || message.Author.IsWebhook)
            {
                Console.WriteLine($"Thread {message.Channel.Id} has a handler that cannot be used because '" + message.Author.Username + "' is a bot.");
            }
            else if (MessageSpecialHandlers.TryRemove(message.Channel.Id, out Action<SocketMessage> handler))
            {
                Console.WriteLine($"Thread {message.Channel.Id} found a valid first messager: '" + message.Author.Username + "'");
                handler(message);
            }
            else
            {
                Console.WriteLine($"Thread {message.Channel.Id} found a valid messager but it's been handled async already.");
            }
            return Task.CompletedTask;
        }

        /// <summary>The actual primary method of this program. Does the adding of users to threads.</summary>
        public static Task NewThreadHandle(DiscordBot bot, SocketThreadChannel thread)
        {
            if (bot.BotMonitor.ShouldStopAllLogic())
            {
                return Task.CompletedTask;
            }
            try
            {
                Console.WriteLine($"See new thread {thread.Id}, {thread.Name}");
                if (!SeenThreads.TryAdd(thread.Id, 1))
                {
                    Console.WriteLine("Thread is a duplicate, ignore");
                    return Task.CompletedTask;
                }
                if (Math.Abs(DateTimeOffset.Now.Subtract(thread.CreatedAt).TotalMinutes) > 5)
                {
                    Console.WriteLine("Thread is pre-existing, ignore");
                    return Task.CompletedTask;
                }
                Console.WriteLine("Begin new thread proc");
                Task.Run(() =>
                {
                    try
                    {
                        HandleNewThread_Internal(thread);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to handle a thread: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Broke handling new thread: {ex}");
            }
            return Task.CompletedTask;
        }

        public static AsciiMatcher ACCEPTABLE_NAME_CHARs = new(AsciiMatcher.BothCaseLetters + AsciiMatcher.Digits + "_");

        public static void HandleNewThread_Internal(SocketThreadChannel thread)
        {
            Console.WriteLine("new thread proc internal started");
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(thread.Guild.Id);
            string message = "";
            lock (helper.Locker)
            {
                Console.WriteLine($"Load thread {thread.Id}");
                string senderName = null;
                int tries = 0;
                long time = Environment.TickCount64;
                SocketMessage firstMessage = null;
                MessageSpecialHandlers[thread.Id] = (m) =>
                {
                    firstMessage = m;
                };
                while (true)
                {
                    if (firstMessage is null)
                    {
                        if (tries++ > 70) // 70 x100ms = 7 seconds
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
                MessageSpecialHandlers.Remove(thread.Id, out _);
                if (firstMessage is null)
                {
                    Console.WriteLine($"Failed to identify thread creator for thread {thread.Id}, wait {Environment.TickCount64 - time} ms.");
                }
                else if (helper.InternalData.AutoPrefix && !thread.Name.StartsWithFast('(') && !thread.Name.StartsWithFast('['))
                {
                    Console.WriteLine($"Apply name correction to thread {thread.Id}");
                    IGuildUser user = firstMessage.Author as IGuildUser;
                    if (user is not null)
                    {
                        senderName = user.Nickname ?? user.Username;
                        int nonMatch = ACCEPTABLE_NAME_CHARs.FirstNonMatchingIndex(senderName);
                        if (nonMatch > 5)
                        {
                            senderName = senderName[0..nonMatch];
                        }
                        senderName = ACCEPTABLE_NAME_CHARs.TrimToMatches(senderName);
                        if (senderName.Length > 12)
                        {
                            senderName = senderName[0..10];
                        }
                        string name = $"({senderName}) {thread.Name}";
                        if (name.Length > 98)
                        {
                            name = name[0..98];
                        }
                        thread.ModifyAsync(t => t.Name = name).Wait();
                    }
                }
                if (helper.InternalData.AutoPin && firstMessage is SocketUserMessage umessage)
                {
                    Console.WriteLine($"Pin first message {umessage.Id} in thread {thread.Id}");
                    umessage.PinAsync().Wait(); // TODO: Delete the "pinned a message" message?
                }
                if (!string.IsNullOrWhiteSpace(helper.InternalData.FirstMessage))
                {
                    thread.SendMessageAsync(text: helper.InternalData.FirstMessage).Wait();
                }
                ulong roleLimit = helper.InternalData.ChannelRoleLimits.GetValueOrDefault(thread.ParentChannel.Id);
                string nameList = "";
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
                        if (!helper.InternalData.UserData.TryGetValue(userId, out GuildDataHelper.UserData data) || ShouldInclude(data, thread))
                        {
                            if (!thread.Users.Any(u => u.Id == user.Id))
                            {
                                if (roleLimit == default || user.Roles.Any(r => r.Id == roleLimit))
                                {
                                    message += $"{user.Mention} ";
                                    nameList += $"{user.Username}#{user.Discriminator} ({user.Id})\n";
                                }
                                else
                                {
                                    Console.WriteLine($"Skip user {user.Id} to thread {thread.Id} due to role limit");
                                }
                            }
                        }
                    }
                }
                Console.WriteLine($"Thread {thread.Id} in channel {thread.ParentChannel.Id} has roleLimit {roleLimit} and thus user list: {nameList}");
            }
            if (!string.IsNullOrWhiteSpace(helper.InternalData.ExtraAddPings))
            {
                message = $"{message}  {helper.InternalData.ExtraAddPings}";
            }
            // Trick to add users silently
            Console.WriteLine($"Add users to thread {thread.Id}: {message}");
            if (!string.IsNullOrWhiteSpace(message))
            {
                RestUserMessage sent = thread.SendMessageAsync("(...)").Result;
                sent.ModifyAsync(m => m.Content = message).Wait();
                sent.DeleteAsync().Wait();
            }
            Console.WriteLine($"Completed thread {thread.Id}");
        }

        public static bool ShouldInclude(GuildDataHelper.UserData data, SocketThreadChannel thread)
        {
            if (!data.ChannelLimit.IsEmpty())
            {
                if (data.ChannelLimit.Contains(thread.ParentChannel.Id) != data.IsWhitelist)
                {
                    return false;
                }
            }
            if (data.ForumExclude && thread.ParentChannel is IForumChannel)
            {
                return false;
            }
            return true;
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
