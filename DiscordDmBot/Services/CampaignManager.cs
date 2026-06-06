using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DiscordDmBot.Data;
using DiscordDmBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordDmBot.Services
{
    public class CampaignManager
    {
        private readonly ConcurrentDictionary<ulong, CampaignState> _activeCampaigns = new();
        private readonly IServiceScopeFactory _scopeFactory;

        public CampaignManager(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public bool IsCampaignActive(ulong channelId) => _activeCampaigns.ContainsKey(channelId);

        public async Task<bool> StartCampaignAsync(ulong channelId, string name, string initialContext = "")
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (await dbContext.Campaigns.AnyAsync(c => c.Name == name))
            {
                return false; // Campaign exists
            }

            var campaign = new DbCampaign
            {
                Id = Guid.NewGuid(),
                Name = name,
                Context = initialContext,
                ActiveChannelId = channelId
            };

            dbContext.Campaigns.Add(campaign);
            await dbContext.SaveChangesAsync();

            _activeCampaigns[channelId] = new CampaignState
            {
                ChannelId = channelId,
                CampaignId = campaign.Id,
                Name = campaign.Name,
                Context = campaign.Context
            };
            return true;
        }

        public async Task<bool> ContinueCampaignAsync(ulong channelId, string name)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var campaign = await dbContext.Campaigns.FirstOrDefaultAsync(c => c.Name == name);
            if (campaign == null) return false;

            campaign.ActiveChannelId = channelId;
            await dbContext.SaveChangesAsync();

            var state = new CampaignState
            {
                ChannelId = channelId,
                CampaignId = campaign.Id,
                Name = campaign.Name,
                Context = campaign.Context,
                MessagesSinceLastSummary = campaign.MessagesSinceLastSummary
            };

            if (!string.IsNullOrWhiteSpace(campaign.ShortTermMemoryJson))
            {
                try {
                    state.ShortTermMemory = JsonSerializer.Deserialize<List<ChatMessage>>(campaign.ShortTermMemoryJson) ?? new List<ChatMessage>();
                } catch {
                    state.ShortTermMemory = new List<ChatMessage>();
                }
            }

            _activeCampaigns[channelId] = state;
            return true;
        }

        public async Task StopCampaignAsync(ulong channelId)
        {
            if (_activeCampaigns.TryRemove(channelId, out var state))
            {
                await SaveCampaignStateAsync(state);
            }
        }

        public async Task SaveCampaignStateAsync(CampaignState state)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var campaign = await dbContext.Campaigns.FindAsync(state.CampaignId);
            if (campaign != null)
            {
                lock (state)
                {
                    campaign.ShortTermMemoryJson = JsonSerializer.Serialize(state.ShortTermMemory);
                    campaign.MessagesSinceLastSummary = state.MessagesSinceLastSummary;
                }
                campaign.ActiveChannelId = null;
                await dbContext.SaveChangesAsync();
            }
        }

        public async Task<List<string>> ListCampaignsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await dbContext.Campaigns.Select(c => c.Name).ToListAsync();
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
