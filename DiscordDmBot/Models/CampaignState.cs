using System.Collections.Generic;

namespace DiscordDmBot.Models
{
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class CampaignState
    {
        public ulong ChannelId { get; set; }
        public System.Guid CampaignId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public List<ChatMessage> ShortTermMemory { get; set; } = new List<ChatMessage>();
        public int MessagesSinceLastSummary { get; set; } = 0;
    }
}
