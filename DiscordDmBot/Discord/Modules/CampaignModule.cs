using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using DiscordDmBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordDmBot.Discord.Modules
{
    public class CampaignModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly CampaignManager _campaignManager;
        private readonly System.IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public CampaignModule(CampaignManager campaignManager, System.IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _campaignManager = campaignManager;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        [SlashCommand("act", "Führt eine Aktion in der Kampagne aus.")]
        public async Task ActAsync([Summary("aktion", "Die Aktion oder Nachricht an den Bot")] string action)
        {
            await DeferAsync();

            var channelId = Context.Interaction.ChannelId;
            if (channelId == null)
            {
                await FollowupAsync("Fehler: Kanal konnte nicht identifiziert werden.");
                return;
            }

            if (!_campaignManager.IsCampaignActive(channelId.Value))
            {
                await FollowupAsync("Hier läuft aktuell keine Kampagne. Nutze `/start_campaign`, um eine zu beginnen.");
                return;
            }

            var state = _campaignManager.GetCampaignState(channelId.Value);
            if (state == null)
            {
                await FollowupAsync("Fehler beim Laden des Kampagnenstatus.");
                return;
            }

            // Record user message
            _campaignManager.AddMessage(channelId.Value, "user", $"{Context.User.Username}: {action}");

            using var scope = _serviceProvider.CreateScope();
            var ollamaService = scope.ServiceProvider.GetRequiredService<OllamaService>();

            try
            {
                var response = await ollamaService.GenerateResponseAsync(channelId.Value, state.Context, state.ShortTermMemory);
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    // Send response in chunks if necessary
                    const int maxChunkSize = 1900;
                    for (int i = 0; i < response.Length; i += maxChunkSize)
                    {
                        int length = Math.Min(maxChunkSize, response.Length - i);
                        await FollowupAsync(response.Substring(i, length));
                    }
                    
                    _campaignManager.AddMessage(channelId.Value, "assistant", response);
                }
                else
                {
                    await FollowupAsync("⚠️ **Fehler:** Ollama hat eine leere Antwort zurückgegeben.");
                }

                // Check for summarization
                var threshold = _configuration.GetValue<int>("BotSettings:SummarizationThreshold", 10);
                if (state.MessagesSinceLastSummary >= threshold)
                {
                    await RunAutoSummarizationAsync(channelId.Value, ollamaService);
                }
            }
            catch (System.Exception ex)
            {
                await FollowupAsync($"❌ **Fehler bei der Kommunikation mit Ollama:** {ex.Message}");
            }
        }

        private async Task RunAutoSummarizationAsync(ulong channelId, OllamaService ollamaService)
        {
            var state = _campaignManager.GetCampaignState(channelId);
            if (state == null) return;

            try
            {
                var messagesToSummarize = state.ShortTermMemory.Take(10).ToList();
                await ollamaService.SummarizeChatAsync(channelId, messagesToSummarize);
                _campaignManager.ClearOldMessages(channelId, 10);
            }
            catch
            {
                // Silently fail for auto-summarization or log it elsewhere
            }
        }

        [SlashCommand("start_campaign", "Startet eine neue Kampagne in diesem Kanal.")]
        public async Task StartCampaignAsync([Summary("intro", "Kurzer Kontext oder Vorgeschichte")] string intro = "")
        {
            await DeferAsync();

            var channelId = Context.Interaction.ChannelId;
            if (channelId == null)
            {
                await FollowupAsync("Fehler: Kanal konnte nicht identifiziert werden.");
                return;
            }

            if (_campaignManager.IsCampaignActive(channelId.Value))
            {
                await FollowupAsync("Es läuft bereits eine Kampagne in diesem Kanal.");
                return;
            }

            _campaignManager.StartCampaign(channelId.Value, intro);
            await FollowupAsync($"Kampagne gestartet! {(!string.IsNullOrWhiteSpace(intro) ? $"Kontext: {intro}" : "")}");
        }

        [SlashCommand("stop_campaign", "Beendet die aktive Kampagne in diesem Kanal.")]
        public async Task StopCampaignAsync()
        {
            await DeferAsync();

            var channelId = Context.Interaction.ChannelId;
            if (channelId == null)
            {
                await FollowupAsync("Fehler: Kanal konnte nicht identifiziert werden.");
                return;
            }

            if (!_campaignManager.IsCampaignActive(channelId.Value))
            {
                await FollowupAsync("Hier läuft aktuell keine Kampagne.");
                return;
            }

            _campaignManager.StopCampaign(channelId.Value);
            await FollowupAsync("Kampagne beendet. Das Langzeitgedächtnis bleibt gespeichert.");
        }

        [SlashCommand("summarize", "Erzwingt eine sofortige Zusammenfassung der aktuellen Ereignisse.")]
        public async Task SummarizeAsync()
        {
            var channelId = Context.Interaction.ChannelId;
            if (channelId == null)
            {
                await RespondAsync("Fehler: Kanal konnte nicht identifiziert werden.");
                return;
            }

            if (!_campaignManager.IsCampaignActive(channelId.Value))
            {
                await RespondAsync("Hier läuft aktuell keine Kampagne.");
                return;
            }

            await DeferAsync();

            using var scope = _serviceProvider.CreateScope();
            var ollamaService = scope.ServiceProvider.GetRequiredService<OllamaService>();
            
            var state = _campaignManager.GetCampaignState(channelId.Value);
            if (state == null)
            {
                await FollowupAsync("Fehler beim Laden der Kampagne.");
                return;
            }

            try
            {
                var messagesToSummarize = state.ShortTermMemory;
                if (messagesToSummarize.Count == 0)
                {
                    await FollowupAsync("Es gibt keine neuen Nachrichten, die zusammengefasst werden könnten.");
                    return;
                }

                await ollamaService.SummarizeChatAsync(channelId.Value, messagesToSummarize);
                _campaignManager.ClearOldMessages(channelId.Value, 0);
                
                await FollowupAsync("Die Ereignisse wurden erfolgreich zusammengefasst und im Langzeitgedächtnis gespeichert.");
            }
            catch (System.Exception ex)
            {
                await FollowupAsync($"Fehler bei der Zusammenfassung: {ex.Message}");
            }
        }

        [SlashCommand("campaign_status", "Prüft den Status der Kampagne in diesem Kanal (Diagnose).")]
        public async Task CampaignStatusAsync()
        {
            var channelId = Context.Interaction.ChannelId;
            if (channelId == null)
            {
                await RespondAsync("Fehler: Kanal konnte nicht identifiziert werden.");
                return;
            }

            bool isActive = _campaignManager.IsCampaignActive(channelId.Value);
            var state = _campaignManager.GetCampaignState(channelId.Value);

            var sb = new StringBuilder();
            sb.AppendLine("📊 **Kampagnen-Status-Bericht**");
            sb.AppendLine($"> **Kanal-ID:** `{channelId.Value}`");
            sb.AppendLine($"> **Aktiv:** `{(isActive ? "✅ Ja" : "❌ Nein")}`");

            if (isActive && state != null)
            {
                sb.AppendLine($"> **Kurzzeitgedächtnis:** `{state.ShortTermMemory.Count}` Nachrichten");
                sb.AppendLine($"> **Nachrichten seit letzter Zusammenfassung:** `{state.MessagesSinceLastSummary}`");
                sb.AppendLine($"> **Kontext hinterlegt:** `{(string.IsNullOrWhiteSpace(state.Context) ? "Nein" : "Ja")}`");
            }

            await RespondAsync(sb.ToString(), ephemeral: true);
        }
    }
}
