using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticToolkit;

namespace DiscordAutoThreadBot
{
    /// <summary>
    /// The primary helper to track the per-guild data (user list, etc).
    /// </summary>
    public class GuildDataHelper
    {
        /// <summary>
        /// ================================================================================
        /// This is the value to edit if you want more users per list!
        /// You don't have to edit anything else.
        /// ================================================================================
        /// </summary>
        public const int MAXIMUM_PER_LIST = 15;

        /// <summary>Data per-guild.</summary>
        public static ConcurrentDictionary<ulong, GuildDataHelper> GuildLists = new();

        /// <summary>Gets or creates the helper instance for a given guild ID.</summary>
        public static GuildDataHelper GetHelperFor(ulong guild)
        {
            return GuildLists.GetOrAdd(guild, guildId =>
            {
                GuildDataHelper helper = new() { Guild = guildId };
                helper.Load();
                helper.Modified = true;
                return helper;
            });
        }

        /// <summary>Shuts down and saves all lists.</summary>
        public static void Shutdown()
        {
            foreach (GuildDataHelper helper in GuildLists.Values)
            {
                try
                {
                    helper.Save();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save guild {helper.Guild}: {ex}");
                }
            }
            GuildLists.Clear();
        }

        /// <summary>Helper class to store per-user data.</summary>
        public class UserData : AutoConfiguration
        {
            /// <summary>True: whitelist, false: blacklist.</summary>
            public bool IsWhitelist = false;

            /// <summary>Channel limit to apply as either a whitelist or blacklist.</summary>
            public HashSet<ulong> ChannelLimit = new();
        }

        /// <summary>Helper class to store the data for this instance.</summary>
        public class Data : AutoConfiguration
        {
            /// <summary>A list of relevant user IDs.</summary>
            public List<ulong> Users = new();

            /// <summary>If non-null: a message to post when new threads are created.</summary>
            public string FirstMessage = "";

            /// <summary>If true: automatically apply username prefixes to threads.</summary>
            public bool AutoPrefix = false;

            /// <summary>A map of user IDs to their data.</summary>
            public Dictionary<ulong, UserData> UserData = new();
        }

        public LockObject Locker = new();

        /// <summary>The data for this instance.</summary>
        public Data InternalData = new();

        /// <summary>The relevant guild ID.</summary>
        public ulong Guild;

        /// <summary>Gets the save file path for this list.</summary>
        public string FilePath => $"./config/saves/{Guild}.fds";

        /// <summary>True if the list has been modified since loading. False once saved.</summary>
        public bool Modified = false;

        /// <summary>Saves the list to file.</summary>
        public void Save()
        {
            if (Modified)
            {
                InternalData.Save(true).SaveToFile(FilePath);
                Modified = false;
            }
        }

        /// <summary>Loads the list from file.</summary>
        public void Load()
        {
            try
            {
                InternalData.Load(FDSUtility.ReadFile(FilePath));
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"New user for for {Guild} started");
            }
        }
    }
}
