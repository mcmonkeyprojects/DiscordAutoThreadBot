using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;

namespace DiscordAutoThreadBot
{
    /// <summary>Commands for the auto-thread bots.</summary>
    public class AutoThreadBotCommands : UserCommands
    {
        /// <summary>A basic 'help' command.</summary>
        public static void Command_Help(CommandData command)
        {
            SendGenericPositiveMessageReply(command.Message, "Auto Thread Join Bot - Help", "The auto-thread join bot automatically levels adds specific users to any new threads created on the Discord."
                + "\nThose with admin access on this Discord can type `@AutoThreadsBot add (user)` to add a user to the auto-threads-adder list,"
                + "\nor type `@AutoThreadsBot remove (user)` to remove them from that list."
                + "\nAlso `@AutoThreadsBot list` to view the current user list."
                + "\nIf you're on the list, you can block this bot to hide the notifications but still be added to threads."
                + "\n\nI'm [open source](https://github.com/mcmonkeyprojects/DiscordAutoThreadBot)!");
        }

        /// <summary>Characters that can be stripped from an '@' ping.</summary>
        public static AsciiMatcher PingIgnorableCharacters = new("<>!@");

        /// <summary>A command for admins to add a user to the list.</summary>
        public static void Command_Add(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketGuildChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.Administrator)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Admin** permission may use the `add` command.");
                return;
            }
            if (command.RawArguments.Length != 1 || !ulong.TryParse(PingIgnorableCharacters.TrimToNonMatches(command.RawArguments[0]), out ulong userId))
            {
                SendGenericNegativeMessageReply(command.Message, "Invalid Input", "Give a user ID or @ mention. Any other input won't work.");
                return;
            }
            if (command.Bot.Client.GetUser(userId) is null)
            {
                SendGenericNegativeMessageReply(command.Message, "Invalid Input", "That user doesn't seem to exist.");
                return;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(channel.Guild.Id);
            lock (helper.Locker)
            {
                if (helper.InternalData.Users.Count >= GuildDataHelper.MAXIMUM_PER_LIST)
                {
                    SendGenericNegativeMessageReply(command.Message, "List too long.", $"The user list has reached the limit of {GuildDataHelper.MAXIMUM_PER_LIST}.\nRemove some users to be able to add more.");
                    return;
                }
                helper.InternalData.Users.Add(userId);
                helper.Modified = true;
                helper.Save();
                SendGenericPositiveMessageReply(command.Message, "Added", $"Added user <@{userId}> to the auto-thread-join list.");
            }
        }

        /// <summary>A command for admins to remove a user from the list.</summary>
        public static void Command_Remove(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketGuildChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.Administrator)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Admin** permission may use the `remove` command.");
                return;
            }
            if (command.RawArguments.Length != 1 || !ulong.TryParse(PingIgnorableCharacters.TrimToNonMatches(command.RawArguments[0]), out ulong userId))
            {
                SendGenericNegativeMessageReply(command.Message, "Invalid Input", "Give a user ID or @ mention. Any other input won't work.");
                return;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(channel.Guild.Id);
            lock (helper.Locker)
            {
                if (!helper.InternalData.Users.Contains(userId))
                {
                    SendGenericNegativeMessageReply(command.Message, "Invalid Input", "That user already isn't listed.");
                    return;
                }
                helper.InternalData.Users.Remove(userId);
                helper.Modified = true;
                helper.Save();
                SendGenericPositiveMessageReply(command.Message, "Removed", $"Removed user <@{userId}> from the auto-thread-join list.");
            }
        }

        /// <summary>A command for admins to show the users in the list.</summary>
        public static void Command_List(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketGuildChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.Administrator)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Admin** permission may use the `list` command.");
                return;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(channel.Guild.Id);
            lock (helper.Locker)
            {
                SendGenericPositiveMessageReply(command.Message, $"List of {helper.InternalData.Users.Count} Users", string.Join(", ", helper.InternalData.Users.Select(u => $"<@{u}>")));
            }
        }
    }
}
