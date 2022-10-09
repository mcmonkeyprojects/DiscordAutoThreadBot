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
                + "\nor type `@AutoThreadsBot user (user) whitelist/blacklist/clear (channels)` to configure a specific user to have a whitelist or a blacklist on specific channels (comma or space separated channel ID or tag list),"
                + "\nAlso `@AutoThreadsBot list` to view the current user list."
                + "\nIf you're on the list, you can block this bot to hide the notifications but still be added to threads."
                + "\nAlso `@AutoThreadsBot firstmessage (text)` to configure a message that the bot will show when a new thread is created."
                + "\nAlso, `@AutoThreadsBot autoprefix true` to enable (or `false` to disable) automatic username thread prefixes."
                + "\nAlso, anybody who has the `Manage Threads` permission (or that own a thread) may use `/archive` or `@AutoThreadsBot archive` to archive a thread without locking it."
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

        public static AsciiMatcher ChannelTagCleaner = new("<#> ");
        public static AsciiMatcher ChannelIDListValidator = new(AsciiMatcher.Digits + ",");

        /// <summary>A command for admins to configure a user in the list.</summary>
        public static void Command_User(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketGuildChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.Administrator)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Admin** permission may use the `user` command.");
                return;
            }
            if (command.RawArguments.Length < 2 || !ulong.TryParse(PingIgnorableCharacters.TrimToNonMatches(command.RawArguments[0]), out ulong userId))
            {
                SendGenericNegativeMessageReply(command.Message, "Invalid Input", "Give a user ID or @ mention as the first argument, then `blacklist`, `whitelist`, or `clear`. If not clearing, follow that with a channel list (IDs or tags, comma or space separated)."
                    + "\nOr, user ID + `forumexclude` then `true` or `false`.");
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
                if (!helper.InternalData.Users.Contains(userId))
                {
                    SendGenericNegativeMessageReply(command.Message, "Invalid Input", "That user isn't listed. You must add them first.");
                    return;
                }
                string cmdType = command.RawArguments[1].ToLowerFast();
                if (cmdType == "clear")
                {
                    if (helper.InternalData.UserData.Remove(userId))
                    {
                        helper.Modified = true;
                        helper.Save();
                        SendGenericPositiveMessageReply(command.Message, "Cleared", $"Cleared user data for <@{userId}>.");
                    }
                    else
                    {
                        SendGenericNegativeMessageReply(command.Message, "Cannot Clear", $"No user data exists for <@{userId}>.");
                    }
                    return;
                }
                GuildDataHelper.UserData newData = helper.InternalData.UserData.GetOrCreate(userId, () => new GuildDataHelper.UserData());
                void buildChannels()
                {
                    string channelTargets = ChannelTagCleaner.TrimToNonMatches(string.Join(',', command.RawArguments.Skip(2)));
                    if (!ChannelIDListValidator.IsOnlyMatches(channelTargets))
                    {
                        SendGenericNegativeMessageReply(command.Message, "Invalid Input", "Channel list contains invalid input.");
                        return;
                    }
                    newData.ChannelLimit = channelTargets.SplitFast(',').Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => ulong.TryParse(s, out ulong val) ? val : 0).ToHashSet();
                    foreach (ulong channelId in newData.ChannelLimit)
                    {
                        if (channelId == 0 || channel.Guild.GetChannel(channelId) is null)
                        {
                            SendGenericNegativeMessageReply(command.Message, "Invalid Input", $"Channel ID given {channelId} is not a channel that exists.");
                            return;
                        }
                    }
                    SendGenericPositiveMessageReply(command.Message, "Configured", $"User <@{userId}> is now {(newData.IsWhitelist ? "whitelisted" : "blacklisted")} to channels: {string.Join(", ", newData.ChannelLimit.Select(ch => $"<#{ch}>"))}");
                }
                if (cmdType == "whitelist")
                {
                    newData.IsWhitelist = true;
                    buildChannels();
                }
                else if (cmdType == "blacklist")
                {
                    newData.IsWhitelist = false;
                    buildChannels();
                }
                else if (cmdType == "forumexclude")
                {
                    string mode = command.RawArguments.Length > 2 ? command.RawArguments[2].ToLowerFast() : "";
                    if (mode == "true")
                    {
                        newData.ForumExclude = true;
                    }
                    else if (mode == "false")
                    {
                        newData.ForumExclude = false;
                    }
                    else
                    {
                        SendGenericNegativeMessageReply(command.Message, "Invalid Input", "Unknown `forumexclude` boolean: must be `true` or `false`.");
                        return;
                    }
                    SendGenericPositiveMessageReply(command.Message, "Configured", $"User <@{userId}> is now {(newData.ForumExclude ? "excluded from" : "included in")} forum channels.");
                }
                else
                {
                    SendGenericNegativeMessageReply(command.Message, "Invalid Input", "Unknown action type. Must be `clear`, `whitelist`, `blacklist`, or `forumexclude`.");
                    return;
                }
                helper.InternalData.UserData[userId] = newData;
                helper.Modified = true;
                helper.Save();
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
                helper.InternalData.UserData.Remove(userId);
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

        /// <summary>A command for admins to configure the first-message.</summary>
        public static void Command_FirstMessage(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketGuildChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.Administrator)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Admin** permission may use the `firstmessage` command.");
                return;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(channel.Guild.Id);
            lock (helper.Locker)
            {
                if (command.CleanedArguments.IsEmpty())
                {
                    SendGenericPositiveMessageReply(command.Message, $"First-Message", $"The current first-message setting is:\n{helper.InternalData.FirstMessage}"
                        + "\n\nUse `@AutoThreadsBot first-message clear` to disable it, or `@AutoThreadsBot first-message (some text)` to configure it.");
                }
                else if (command.CleanedArguments[0].ToLowerFast() == "clear")
                {
                    helper.InternalData.FirstMessage = "";
                    SendGenericPositiveMessageReply(command.Message, $"First-Message", $"First-Message disabled.");
                }
                else
                {
                    helper.InternalData.FirstMessage = command.Message.Content.After("firstmessage").Trim();
                    SendGenericPositiveMessageReply(command.Message, $"First-Message", $"First-Message set to:\n{helper.InternalData.FirstMessage}");
                }
                helper.Modified = true;
                helper.Save();
            }
        }

        /// <summary>A command for admins to enable the auto-prefix tool.</summary>
        public static void Command_AutoPrefix(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketGuildChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.Administrator)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Admin** permission may use the `autoprefix` command.");
                return;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(channel.Guild.Id);
            lock (helper.Locker)
            {
                if (command.CleanedArguments.IsEmpty())
                {
                    SendGenericPositiveMessageReply(command.Message, $"Auto-Prefix", $"The current auto-prefix setting is:\n{helper.InternalData.AutoPrefix}"
                        + "\n\nUse `@AutoThreadsBot autoprefix true` or `false`");
                }
                else if (command.CleanedArguments[0].ToLowerFast() == "true")
                {
                    helper.InternalData.AutoPrefix = true;
                    SendGenericPositiveMessageReply(command.Message, $"Auto-Prefix", $"Auto-Prefix enabled.");
                }
                else if (command.CleanedArguments[0].ToLowerFast() == "false")
                {
                    helper.InternalData.AutoPrefix = false;
                    SendGenericPositiveMessageReply(command.Message, $"Auto-Prefix", $"Auto-Prefix disabled.");
                }
                else
                {
                    SendGenericNegativeMessageReply(command.Message, $"Invalid Input", $"Can only be set to 'true' or 'false'.");
                }
                helper.Modified = true;
                helper.Save();
            }
        }

        /// <summary>A command for admins to enable the autopin tool.</summary>
        public static void Command_AutoPin(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketGuildChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.Administrator)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Admin** permission may use the `autopin` command.");
                return;
            }
            GuildDataHelper helper = GuildDataHelper.GetHelperFor(channel.Guild.Id);
            lock (helper.Locker)
            {
                if (command.CleanedArguments.IsEmpty())
                {
                    SendGenericPositiveMessageReply(command.Message, $"AutoPin", $"The current autopin setting is:\n{helper.InternalData.AutoPin}"
                        + "\n\nUse `@AutoThreadsBot autopin true` or `false`");
                }
                else if (command.CleanedArguments[0].ToLowerFast() == "true")
                {
                    helper.InternalData.AutoPin = true;
                    SendGenericPositiveMessageReply(command.Message, $"AutoPin", $"AutoPin enabled.");
                }
                else if (command.CleanedArguments[0].ToLowerFast() == "false")
                {
                    helper.InternalData.AutoPin = false;
                    SendGenericPositiveMessageReply(command.Message, $"AutoPin", $"AutoPin disabled.");
                }
                else
                {
                    SendGenericNegativeMessageReply(command.Message, $"Invalid Input", $"Can only be set to 'true' or 'false'.");
                }
                helper.Modified = true;
                helper.Save();
            }
        }

        /// <summary>A command for staff to archive a thread without locking it.</summary>
        public static void Command_Archive(CommandData command)
        {
            if (command.Message is not SocketUserMessage message || message.Channel is not SocketThreadChannel channel)
            {
                return;
            }
            if (!(message.Author as SocketGuildUser).GuildPermissions.ManageThreads && message.Author.Id != channel.Owner?.Id)
            {
                SendGenericNegativeMessageReply(command.Message, "Not for you", "Only users with the **Manage Threads** permission may use the `archive` command.");
                return;
            }
            channel.ModifyAsync(c => { c.Locked = false; c.Archived = true; }).Wait();
        }

        /// <summary>A slash command for staff to archive a thread without locking it.</summary>
        public static void SlashCommand_Archive(SocketSlashCommand command)
        {
            if (command.Channel is not SocketThreadChannel channel)
            {
                command.RespondAsync("This is only available in threads.", ephemeral: true).Wait();
                return;
            }
            if (!(command.User as SocketGuildUser).GuildPermissions.ManageThreads && command.User.Id != channel.Owner?.Id)
            {
                SendGenericNegativeMessageReply(command, "Not for you", "Only users with the **Manage Threads** permission (or that own the thread) may use the `archive` command.");
                return;
            }
            SendGenericPositiveMessageReply(command, "Thread Archived", $"Thread moving into archive on request of <@{command.User.Id}>.");
            channel.ModifyAsync(c => { c.Locked = false; c.Archived = true; }).Wait();
        }
    }
}
