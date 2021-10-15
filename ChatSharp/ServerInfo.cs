using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp
{
    public class ServerInfo
    {
        public ServerInfo()
        {
            // Guess for some defaults
            Prefixes = new[] { "ov", "@+" };
            SupportedChannelModes = new ChannelModes();
            IsGuess = true;
        }

        /// <summary>
        /// Gets the mode for a given channel user list prefix.
        /// </summary>
        public char? GetModeForPrefix(char prefix)
        {
            if (Prefixes[1].IndexOf(prefix) == -1)
                return null;
            return Prefixes[0][Prefixes[1].IndexOf(prefix)];
        }

        /// <summary>
        /// ChatSharp makes some assumptions about what the server supports in order to function properly.
        /// If it has not recieved a 005 message giving it accurate information, this value will be true.
        /// </summary>
        public bool IsGuess { get; internal set; }
        /// <summary>
        /// Nick prefixes for special modes in channel user lists
        /// </summary>
        public string[] Prefixes { get; internal set; }
        /// <summary>
        /// Supported channel prefixes (i.e. '#')
        /// </summary>
        public char[] ChannelTypes { get; internal set; }
        /// <summary>
        /// Channel modes supported by this server
        /// </summary>
        public ChannelModes SupportedChannelModes { get; set; }
        /// <summary>
        /// The maximum number of MODE changes possible with a single command
        /// </summary>
        public int? MaxModesPerCommand { get; set; }
        /// <summary>
        /// The maximum number of channels a user may join
        /// </summary>
        public int? MaxChannelsPerUser { get; set; } // TODO: Support more than just # channels
        /// <summary>
        /// Maximum length of user nicks on this server
        /// </summary>
        public int? MaxNickLength { get; set; }
        /// <summary>
        /// The limits imposed on list modes, such as +b
        /// </summary>
        public ModeListLimit[] ModeListLimits { get; set; }
        /// <summary>
        /// The name of the network, as identified by the server
        /// </summary>
        public string NetworkName { get; set; }
        /// <summary>
        /// Set to ban exception character if this server supports ban exceptions
        /// </summary>
        public char? SupportsBanExceptions { get; set; }
        /// <summary>
        /// Set to invite exception character if this server supports invite exceptions
        /// </summary>
        public char? SupportsInviteExceptions { get; set; }
        /// <summary>
        /// Set to maximum topic length for this server
        /// </summary>
        public int? MaxTopicLength { get; set; }
        /// <summary>
        /// Set to the maximum length of a KICK comment
        /// </summary>
        public int? MaxKickCommentLength { get; set; }
        /// <summary>
        /// Set to the maximum length of a channel name
        /// </summary>
        public int? MaxChannelNameLength { get; set; }
        /// <summary>
        /// Set to the maximum length of an away message
        /// </summary>
        public int? MaxAwayLength { get; set; }

        public class ChannelModes
        {
            internal ChannelModes()
            {
                // Guesses
                ChannelLists = "eIbq";
                ParameterizedSettings = "k";
                OptionallyParameterizedSettings = "flj";
                Settings = string.Empty;
                ChannelUserModes = "vo"; // I have no idea what I'm doing here
            }

            public string ChannelLists { get; internal set; }
            public string ChannelUserModes { get; set; }
            public string ParameterizedSettings { get; internal set; }
            public string OptionallyParameterizedSettings { get; internal set; }
            public string Settings { get; internal set; }
        }

        public class ModeListLimit
        {
            internal ModeListLimit(char mode, int maximum)
            {
                Mode = mode;
                Maximum = maximum;
            }

            public char Mode { get; internal set; }
            public int Maximum { get; internal set; }
        }
    }
}
