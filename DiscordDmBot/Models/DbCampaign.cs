using System;

namespace DiscordDmBot.Models
{
    public class DbCampaign
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public string ShortTermMemoryJson { get; set; } = "[]";
        public int MessagesSinceLastSummary { get; set; } = 0;
        public ulong? ActiveChannelId { get; set; }
    }
}
