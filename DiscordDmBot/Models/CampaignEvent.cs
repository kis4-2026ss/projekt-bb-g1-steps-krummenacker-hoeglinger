using System;

namespace DiscordDmBot.Models
{
    public class CampaignEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ulong ChannelId { get; set; }
        public string SummaryText { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
