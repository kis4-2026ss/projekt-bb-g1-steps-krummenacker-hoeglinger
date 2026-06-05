using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using DiscordDmBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordDmBot.Discord.Modules
{
    public class CampaignModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly CampaignManager _campaignManager;
        private readonly System.IServiceProvider _serviceProvider;

        public CampaignModule(CampaignManager campaignManager, System.IServiceProvider serviceProvider)
        {
            _campaignManager = campaignManager;
            _serviceProvider = serviceProvider;
        }

        [SlashCommand("start_campaign", "Startet eine neue Kampagne in diesem Kanal.")]
        public async Task StartCampaignAsync([Summary("intro", "Kurzer Kontext oder Vorgeschichte")] string intro = "")
        {
            if (_campaignManager.IsCampaignActive(Context.Channel.Id))
            {
                await RespondAsync("Es läuft bereits eine Kampagne in diesem Kanal.");
                return;
            }

            _campaignManager.StartCampaign(Context.Channel.Id, intro);
            await RespondAsync($"Kampagne gestartet! {(!string.IsNullOrWhiteSpace(intro) ? $"Kontext: {intro}" : "")}");
        }

        [SlashCommand("stop_campaign", "Beendet die aktive Kampagne in diesem Kanal.")]
        public async Task StopCampaignAsync()
        {
            if (!_campaignManager.IsCampaignActive(Context.Channel.Id))
            {
                await RespondAsync("Hier läuft aktuell keine Kampagne.");
                return;
            }

            _campaignManager.StopCampaign(Context.Channel.Id);
            await RespondAsync("Kampagne beendet. Das Langzeitgedächtnis bleibt gespeichert.");
        }

        [SlashCommand("summarize", "Erzwingt eine sofortige Zusammenfassung der aktuellen Ereignisse.")]
        public async Task SummarizeAsync()
        {
            if (!_campaignManager.IsCampaignActive(Context.Channel.Id))
            {
                await RespondAsync("Hier läuft aktuell keine Kampagne.");
                return;
            }

            await DeferAsync();

            using var scope = _serviceProvider.CreateScope();
            var ollamaService = scope.ServiceProvider.GetRequiredService<OllamaService>();
            
            var state = _campaignManager.GetCampaignState(Context.Channel.Id);
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

                await ollamaService.SummarizeChatAsync(Context.Channel.Id, messagesToSummarize);
                _campaignManager.ClearOldMessages(Context.Channel.Id, 0);
                
                await FollowupAsync("Die Ereignisse wurden erfolgreich zusammengefasst und im Langzeitgedächtnis gespeichert.");
            }
            catch (System.Exception ex)
            {
                await FollowupAsync($"Fehler bei der Zusammenfassung: {ex.Message}");
            }
        }
    }
}
