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
            var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();

            try
            {
                var response = await geminiService.GenerateResponseAsync(state.CampaignId, state.Context, state.ShortTermMemory);
                
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
                    await RunAutoSummarizationAsync(channelId.Value, geminiService);
                }
            }
            catch (System.Exception ex)
            {
                await FollowupAsync($"❌ **Fehler bei der Kommunikation mit Gemini:** {ex.Message}");
            }
        }

        private async Task RunAutoSummarizationAsync(ulong channelId, GeminiService geminiService)
        {
            var state = _campaignManager.GetCampaignState(channelId);
            if (state == null) return;

            try
            {
                var messagesToSummarize = state.ShortTermMemory.Take(10).ToList();
                var summary = await geminiService.SummarizeChatAsync(state.CampaignId, messagesToSummarize);
                _campaignManager.ClearOldMessages(channelId, 10);
            }
            catch
            {
                // Silently fail for auto-summarization or log it elsewhere
            }
        }

        [SlashCommand("start_campaign", "Startet eine neue benannte Kampagne in diesem Kanal.")]
        public async Task StartCampaignAsync([Summary("name", "Name der Kampagne")] string name, [Summary("intro", "Kurzer Kontext")] string intro = "")
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
                await FollowupAsync("Es läuft bereits eine Kampagne in diesem Kanal. Nutze zuerst `/stop_campaign`.");
                return;
            }

            var success = await _campaignManager.StartCampaignAsync(channelId.Value, name, intro);
            if (!success)
            {
                await FollowupAsync($"Eine Kampagne mit dem Namen '{name}' existiert bereits. Nutze `/continue_campaign`.");
                return;
            }
            
            await FollowupAsync($"Kampagne '{name}' gestartet! {(!string.IsNullOrWhiteSpace(intro) ? $"Kontext: {intro}" : "")}\n*Der Dungeon Master bereitet die Welt vor...*");

            using var scope = _serviceProvider.CreateScope();
            var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();
            var state = _campaignManager.GetCampaignState(channelId.Value);
            
            if (state != null)
            {
                var prompt = "Wir starten jetzt eine neue Kampagne. Schreibe deine Antwort direkt aus der Perspektive des Dungeon Masters. Beschreibe erzählerisch die Umgebung und Situation, in der sich die Spielercharaktere befinden, und frage sie direkt in-character, was sie als nächstes tun möchten. WICHTIG: Verwende keine Formatierungen wie 'Szene:' oder Meta-Notizen.";
                _campaignManager.AddMessage(channelId.Value, "user", prompt);
                
                try
                {
                    var aiResponse = await geminiService.GenerateResponseAsync(state.CampaignId, state.Context, state.ShortTermMemory);
                    if (!string.IsNullOrWhiteSpace(aiResponse))
                    {
                        const int maxChunkSize = 1900;
                        for (int i = 0; i < aiResponse.Length; i += maxChunkSize)
                        {
                            int length = System.Math.Min(maxChunkSize, aiResponse.Length - i);
                            await FollowupAsync(aiResponse.Substring(i, length));
                        }
                        _campaignManager.AddMessage(channelId.Value, "assistant", aiResponse);
                    }
                }
                catch (System.Exception ex)
                {
                    await FollowupAsync($"❌ **Fehler bei der Kommunikation mit Gemini:** {ex.Message}");
                }
            }
        }

        [SlashCommand("continue_campaign", "Lädt eine gespeicherte Kampagne in diesen Kanal.")]
        public async Task ContinueCampaignAsync([Summary("name", "Name der Kampagne")] string name)
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
                await FollowupAsync("Es läuft bereits eine Kampagne in diesem Kanal. Nutze zuerst `/stop_campaign`.");
                return;
            }

            var success = await _campaignManager.ContinueCampaignAsync(channelId.Value, name);
            if (!success)
            {
                await FollowupAsync($"Eine Kampagne mit dem Namen '{name}' wurde nicht gefunden.");
                return;
            }
            
            await FollowupAsync($"Kampagne '{name}' erfolgreich geladen und fortgesetzt!\n*Der Dungeon Master ruft die Erinnerungen ab...*");

            using var scope = _serviceProvider.CreateScope();
            var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();
            var state = _campaignManager.GetCampaignState(channelId.Value);
            
            if (state != null)
            {
                var prompt = "Wir setzen die Kampagne fort. Schreibe deine Antwort direkt aus der Perspektive des Dungeon Masters. Gib eine kurze, atmosphärische erzählerische Zusammenfassung dessen, was zuletzt passiert ist und wo wir uns befinden. Lass dann NPCs agieren oder beschreibe die Szene weiter und frage die Spieler, was sie als nächstes tun. WICHTIG: Verwende keine Formatierungen wie 'Szene:' oder Meta-Notizen.";
                _campaignManager.AddMessage(channelId.Value, "user", prompt);
                
                try
                {
                    var aiResponse = await geminiService.GenerateResponseAsync(state.CampaignId, state.Context, state.ShortTermMemory);
                    if (!string.IsNullOrWhiteSpace(aiResponse))
                    {
                        const int maxChunkSize = 1900;
                        for (int i = 0; i < aiResponse.Length; i += maxChunkSize)
                        {
                            int length = System.Math.Min(maxChunkSize, aiResponse.Length - i);
                            await FollowupAsync(aiResponse.Substring(i, length));
                        }
                        _campaignManager.AddMessage(channelId.Value, "assistant", aiResponse);
                    }
                }
                catch (System.Exception ex)
                {
                    await FollowupAsync($"❌ **Fehler bei der Kommunikation mit Gemini:** {ex.Message}");
                }
            }
        }

        [SlashCommand("list_campaigns", "Zeigt alle gespeicherten Kampagnen an.")]
        public async Task ListCampaignsAsync()
        {
            await DeferAsync();
            var campaigns = await _campaignManager.ListCampaignsAsync();
            if (campaigns.Count == 0)
            {
                await FollowupAsync("Es gibt noch keine gespeicherten Kampagnen.");
                return;
            }
            await FollowupAsync($"**Gespeicherte Kampagnen:**\n" + string.Join("\n", campaigns.Select(c => $"- {c}")));
        }

        [SlashCommand("stop_campaign", "Beendet die aktive Kampagne und speichert sie in der Datenbank.")]
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

            await _campaignManager.StopCampaignAsync(channelId.Value);
            await FollowupAsync("Kampagne pausiert und Fortschritt gespeichert!");
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
            var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();
            
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

                var summary = await geminiService.SummarizeChatAsync(state.CampaignId, messagesToSummarize);
                _campaignManager.ClearOldMessages(channelId.Value, 0);
                
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    await FollowupAsync($"**Zusammenfassung gespeichert:**\n> {summary.Replace("\n", "\n> ")}");
                }
                else
                {
                    await FollowupAsync("⚠️ Die Ereignisse konnten nicht zusammengefasst werden (leere Antwort).");
                }
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
        [SlashCommand("roll", "Würfelt Würfel, z.B. 1d20+5 oder d6")]
        public async Task RollAsync([Summary("wurf", "Der Würfelwurf (z.B. 1d20+3)")] string diceExpression)
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

            // Parse expression
            var match = System.Text.RegularExpressions.Regex.Match(diceExpression.ToLower().Replace(" ", ""), @"^(\d*)d(\d+)([\+\-]\d+)?$");
            if (!match.Success)
            {
                await FollowupAsync("Ungültiges Würfelformat! Bitte benutze ein Format wie `1d20+3`, `2d6`, oder `d20`.");
                return;
            }

            int count = string.IsNullOrEmpty(match.Groups[1].Value) ? 1 : int.Parse(match.Groups[1].Value);
            int sides = int.Parse(match.Groups[2].Value);
            int modifier = string.IsNullOrEmpty(match.Groups[3].Value) ? 0 : int.Parse(match.Groups[3].Value);

            if (count <= 0 || count > 100 || sides <= 1 || sides > 1000)
            {
                await FollowupAsync("Bitte verwende realistische Würfelwerte (max 100 Würfel, max 1000 Seiten).");
                return;
            }

            var rand = new System.Random();
            var rolls = new System.Collections.Generic.List<int>();
            int sum = 0;
            for (int i = 0; i < count; i++)
            {
                int r = rand.Next(1, sides + 1);
                rolls.Add(r);
                sum += r;
            }
            
            int total = sum + modifier;

            var modifierString = modifier == 0 ? "" : (modifier > 0 ? $" + {modifier}" : $" - {System.Math.Abs(modifier)}");
            var rollDetails = count == 1 ? $"[{rolls[0]}]" : $"[{string.Join(", ", rolls)}]";
            var responseText = $"🎲 **{Context.User.Username}** würfelt `{diceExpression}`:\n{rollDetails}{modifierString} = **{total}**";

            await FollowupAsync(responseText);

            // Add to AI context
            var aiMessage = $"[WÜRFEL-ERGEBNIS] {Context.User.Username} würfelt {diceExpression} und erzielt eine {total}.";
            _campaignManager.AddMessage(channelId.Value, "user", aiMessage);

            var state = _campaignManager.GetCampaignState(channelId.Value);
            if (state != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();
                
                try
                {
                    var aiResponse = await geminiService.GenerateResponseAsync(state.CampaignId, state.Context, state.ShortTermMemory);
                    if (!string.IsNullOrWhiteSpace(aiResponse))
                    {
                        const int maxChunkSize = 1900;
                        for (int i = 0; i < aiResponse.Length; i += maxChunkSize)
                        {
                            int length = System.Math.Min(maxChunkSize, aiResponse.Length - i);
                            await FollowupAsync(aiResponse.Substring(i, length));
                        }
                        _campaignManager.AddMessage(channelId.Value, "assistant", aiResponse);
                    }
                }
                catch (System.Exception ex)
                {
                    await FollowupAsync($"❌ **Fehler bei der Kommunikation mit Gemini:** {ex.Message}");
                }
            }
        }
    }
}
