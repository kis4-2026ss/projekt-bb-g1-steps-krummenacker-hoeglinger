using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DiscordDmBot.Data;
using DiscordDmBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DiscordDmBot.Services
{
    public class OllamaService
    {
        private readonly IOllamaApiClient _ollamaClient;
        private readonly AppDbContext _dbContext;
        private readonly string _modelName;

        public OllamaService(IConfiguration configuration, AppDbContext dbContext)
        {
            _dbContext = dbContext;
            var url = configuration.GetValue<string>("Ollama:Url") ?? "http://localhost:11434";
            _modelName = configuration.GetValue<string>("Ollama:Model") ?? "llama3";
            _ollamaClient = new OllamaApiClient(url) { SelectedModel = _modelName };
        }

        public async Task<string> GenerateResponseAsync(ulong channelId, string channelContext, List<ChatMessage> shortTermMemory)
        {
            var pastEvents = await _dbContext.CampaignEvents
                .Where(e => e.ChannelId == channelId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Du bist ein narrativer Dungeon Master. Beschreibe die Welt, steuere NPCs und Feinde.");
            sb.AppendLine("Du verwaltest KEINE Spielerwerte (HP, Zauberslots, Inventar). Die Spieler tun dies selbst. Teile im Kampf nur mit, wie viel Schaden ein Spieler nimmt und welcher Art dieser ist.");
            sb.AppendLine("Übernimm nie die Handlungen der Spieler. Beschreibe eine Situation und warte auf ihre Reaktion. Fordere zu Würfelwürfen auf, wenn eine Aktion riskant ist.");
            
            if (!string.IsNullOrWhiteSpace(channelContext))
            {
                sb.AppendLine("\nKontext der Kampagne:");
                sb.AppendLine(channelContext);
            }

            if (pastEvents.Any())
            {
                sb.AppendLine("\nBisherige Ereignisse (Langzeitgedächtnis):");
                foreach (var ev in pastEvents)
                {
                    sb.AppendLine($"- {ev.SummaryText}");
                }
            }

            var systemPrompt = sb.ToString();

            var messages = new List<Message>();
            foreach (var msg in shortTermMemory)
            {
                messages.Add(new Message(msg.Role == "user" ? ChatRole.User : ChatRole.Assistant, msg.Content));
            }
            
            var request = new ChatRequest
            {
                Model = _modelName,
                Messages = new List<Message> { new Message(ChatRole.System, systemPrompt) }.Concat(messages).ToList(),
                Stream = false
            };

            var fullResponse = "";
            await foreach (var response in _ollamaClient.ChatAsync(request))
            {
                if (response?.Message?.Content != null)
                {
                    fullResponse += response.Message.Content;
                }
            }

            return fullResponse;
        }

        public async Task SummarizeChatAsync(ulong channelId, List<ChatMessage> messagesToSummarize)
        {
            if (!messagesToSummarize.Any()) return;

            var sb = new StringBuilder();
            sb.AppendLine("Fasse die wichtigsten Story-Ereignisse der folgenden Nachrichten in 2-3 Stichpunkten zusammen.");
            sb.AppendLine("Nachrichtenverlauf:");
            foreach (var msg in messagesToSummarize)
            {
                sb.AppendLine($"{msg.Role}: {msg.Content}");
            }

            var request = new ChatRequest
            {
                Model = _modelName,
                Messages = new List<Message> { new Message(ChatRole.User, sb.ToString()) },
                Stream = false
            };

            var summaryText = "";
            await foreach (var response in _ollamaClient.ChatAsync(request))
            {
                if (response?.Message?.Content != null)
                {
                    summaryText += response.Message.Content;
                }
            }

            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                var campaignEvent = new CampaignEvent
                {
                    ChannelId = channelId,
                    SummaryText = summaryText.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.CampaignEvents.Add(campaignEvent);
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}
