using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.Loader;
using DiscordBotBase;
using Discord.WebSocket;
using DiscordBotBase.CommandHandlers;
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
            DiscordBotConfig config = new()
            {
                CacheSize = 0,
                EnsureCaching = false,
                Initialize = Initialize,
                CommandPrefix = null,
                ShouldPayAttentionToMessage = (message) => message is SocketUserMessage uMessage && uMessage.Channel is SocketGuildChannel,
                OnShutdown = () =>
                {
                    ConsoleCancelToken.Cancel();
                }
            };
            Task consoleThread = Task.Run(ConsoleLoop, ConsoleCancelToken.Token);
            DiscordBotBaseHelper.StartBotHandler(args, config);
        }

        public static CancellationTokenSource ConsoleCancelToken = new();

        public static void Initialize(DiscordBot bot)
        {
            bot.RegisterCommand(AutoThreadBotCommands.Command_Help, "help", "halp", "hlp", "?");
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
