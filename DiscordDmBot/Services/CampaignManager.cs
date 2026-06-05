using System.Collections.Concurrent;
using System.Linq;
using DiscordDmBot.Models;

namespace DiscordDmBot.Services
{
    public class CampaignManager
    {
        private readonly ConcurrentDictionary<ulong, CampaignState> _activeCampaigns = new();

        public bool IsCampaignActive(ulong channelId) => _activeCampaigns.ContainsKey(channelId);

        public void StartCampaign(ulong channelId, string initialContext = "")
        {
            _activeCampaigns.TryAdd(channelId, new CampaignState
            {
                ChannelId = channelId,
                Context = initialContext
            });
        }

        public void StopCampaign(ulong channelId)
        {
            _activeCampaigns.TryRemove(channelId, out _);
        }

        public CampaignState? GetCampaignState(ulong channelId)
        {
            _activeCampaigns.TryGetValue(channelId, out var state);
            return state;
        }

        public void AddMessage(ulong channelId, string role, string content)
        {
            if (_activeCampaigns.TryGetValue(channelId, out var state))
            {
                lock (state)
                {
                    state.ShortTermMemory.Add(new ChatMessage { Role = role, Content = content });
                    state.MessagesSinceLastSummary++;
                }
            }
        }
        
        public void ClearOldMessages(ulong channelId, int countToKeep)
        {
            if (_activeCampaigns.TryGetValue(channelId, out var state))
            {
                lock (state)
                {
                    if (state.ShortTermMemory.Count > countToKeep)
                    {
                        state.ShortTermMemory = state.ShortTermMemory
                            .TakeLast(countToKeep)
                            .ToList();
                    }
                    state.MessagesSinceLastSummary = 0;
                }
            }
        }
    }
}
