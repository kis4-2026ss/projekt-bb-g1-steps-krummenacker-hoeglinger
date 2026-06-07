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

            if (!await _campaignManager.TryLockCampaignAsync(channelId.Value))
            {
                await FollowupAsync("⏳ Der Dungeon Master verarbeitet gerade eine andere Aktion. Bitte warte einen Moment...");
                return;
            }

            try
            {
                // Record user message
                _campaignManager.AddMessage(channelId.Value, "user", $"{Context.User.Username}: {action}");

                using var scope = _serviceProvider.CreateScope();
                var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();

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
            finally
            {
                _campaignManager.UnlockCampaign(channelId.Value);
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
                var prompt = "Wir starten jetzt eine neue Kampagne. Generiere exakt 3 unterschiedliche, kurze Start-Szenarien für die Spieler. Jedes Szenario MUSS mit dem Text 'OPTION 1:', 'OPTION 2:' oder 'OPTION 3:' beginnen. Behalte die Szenarien knapp (jeweils max 2-3 Sätze).";
                _campaignManager.AddMessage(channelId.Value, "user", prompt);
                
                try
                {
                    var aiResponse = await geminiService.GenerateResponseAsync(state.CampaignId, state.Context, state.ShortTermMemory);
                    if (!string.IsNullOrWhiteSpace(aiResponse))
                    {
                        var options = new System.Collections.Generic.List<string>();
                        var matches = System.Text.RegularExpressions.Regex.Matches(aiResponse, @"OPTION \d+:(.*?)(?=OPTION \d+:|$)", System.Text.RegularExpressions.RegexOptions.Singleline);
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            options.Add(m.Groups[1].Value.Trim());
                        }

                        if (options.Count >= 3)
                        {
                            var menuBuilder = new SelectMenuBuilder()
                                .WithPlaceholder("Wähle ein Start-Szenario")
                                .WithCustomId("select_campaign_start")
                                .WithMinValues(1)
                                .WithMaxValues(1);

                            for (int i = 0; i < 3; i++)
                            {
                                var desc = options[i].Length > 90 ? options[i].Substring(0, 87) + "..." : options[i];
                                menuBuilder.AddOption($"Option {i + 1}", $"opt_{i + 1}", desc);
                            }

                            var component = new ComponentBuilder().WithSelectMenu(menuBuilder).Build();
                            var msg = $"*Der Dungeon Master bietet dir 3 mögliche Einstiege an:*\n\n**Option 1:** {options[0]}\n\n**Option 2:** {options[1]}\n\n**Option 3:** {options[2]}";
                            await FollowupAsync(msg, components: component);
                        }
                        else
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
        [SlashCommand("invent_spell", "Erfindet einen neuen Zauber und speichert ihn in den Regeln.")]
        public async Task InventSpellAsync([Summary("thema", "Das Thema oder die Art des Zaubers (z.B. 'ein Feuerangriff')")] string thema, [Summary("grad", "Der Grad (Level) des Zaubers (0-9)")] int grad)
        {
            await DeferAsync();

            if (grad < 0 || grad > 9)
            {
                await FollowupAsync("Der Zaubergrad muss zwischen 0 und 9 liegen.");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();

            await FollowupAsync($"*Der Dungeon Master konsultiert die alten Schriften, um einen neuen Zauber des {grad}. Grades über '{thema}' zu erforschen...*");

            string spellMarkdown = await geminiService.GenerateSpellAsync(grad, thema);

            if (string.IsNullOrWhiteSpace(spellMarkdown))
            {
                await FollowupAsync("⚠️ Fehler beim Generieren des Zaubers.");
                return;
            }

            var spellId = System.Guid.NewGuid().ToString("N");
            _campaignManager.AddPendingSpell(spellId, Context.User.Id, grad, thema, spellMarkdown);

            var builder = new ComponentBuilder()
                .WithButton("✨ Speichern", $"spell_approve_{spellId}", ButtonStyle.Success)
                .WithButton("🗑️ Verwerfen", $"spell_reject_{spellId}", ButtonStyle.Danger);

            await FollowupAsync($"**Der Dungeon Master hat folgenden Zauber entwickelt:**\n\n{spellMarkdown}\n\nMöchtest du ihn in die Datenbank aufnehmen?", components: builder.Build());
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

        [ComponentInteraction("spell_approve_*")]
        public async Task SpellApproveAsync(string spellId)
        {
            var spell = _campaignManager.GetAndRemovePendingSpell(spellId);
            if (spell == null)
            {
                await RespondAsync("Dieser Zauber wurde bereits bearbeitet oder ist abgelaufen.", ephemeral: true);
                return;
            }

            if (spell.UserId != Context.User.Id)
            {
                await RespondAsync("Nur der Ersteller des Zaubers kann ihn bestätigen.", ephemeral: true);
                _campaignManager.AddPendingSpell(spellId, spell.UserId, spell.Grad, spell.Thema, spell.SpellMarkdown);
                return;
            }

            await DeferAsync();

            try
            {
                var rulesDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Rules");
                if (!System.IO.Directory.Exists(rulesDir)) System.IO.Directory.CreateDirectory(rulesDir);
                
                var filePath = System.IO.Path.Combine(rulesDir, "DnD_Zauber_Datenbank.md");
                
                var sourcePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "Rules", "DnD_Zauber_Datenbank.md"));
                if (System.IO.File.Exists(sourcePath)) {
                    filePath = sourcePath;
                }

                if (!System.IO.File.Exists(filePath))
                {
                    await System.IO.File.WriteAllTextAsync(filePath, "# D&D 5e Zauber-Datenbank (Spells)\n\n");
                }

                var content = await System.IO.File.ReadAllTextAsync(filePath);
                var lines = content.Split('\n').ToList();

                string searchHeader = spell.Grad == 0 ? "## 1. Zaubertricks (Cantrips / Grad 0)" : $"## {spell.Grad + 1}. Zauber des {spell.Grad}. Grades";
                
                int insertIndex = -1;
                bool sectionFound = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(searchHeader) || (spell.Grad > 0 && lines[i].StartsWith($"## ") && lines[i].Contains($"{spell.Grad}. Grades")))
                    {
                        sectionFound = true;
                        insertIndex = i + 1;
                        while (insertIndex < lines.Count && !lines[insertIndex].StartsWith("## "))
                        {
                            insertIndex++;
                        }
                        
                        while (insertIndex > 0 && (string.IsNullOrWhiteSpace(lines[insertIndex - 1]) || lines[insertIndex - 1].StartsWith("---")))
                        {
                            insertIndex--;
                        }
                        break;
                    }
                }

                if (!sectionFound)
                {
                    lines.Add("");
                    lines.Add("---");
                    lines.Add("");
                    string newHeader = spell.Grad == 0 ? "## 1. Zaubertricks (Cantrips / Grad 0)" : $"## {spell.Grad + 1}. Zauber des {spell.Grad}. Grades ({spell.Grad}th Level Spells)";
                    lines.Add(newHeader);
                    lines.Add("");
                    insertIndex = lines.Count;
                }

                lines.Insert(insertIndex, "");
                var spellLines = spell.SpellMarkdown.Split('\n');
                for (int i = spellLines.Length - 1; i >= 0; i--)
                {
                    lines.Insert(insertIndex, spellLines[i].TrimEnd('\r'));
                }
                lines.Insert(insertIndex, "");

                await System.IO.File.WriteAllTextAsync(filePath, string.Join("\n", lines));

                await ModifyOriginalResponseAsync(x => {
                    x.Components = new ComponentBuilder().Build();
                });
                
                await FollowupAsync($"✅ **Der Zauber '{spell.Thema}' wurde in die Datenbank aufgenommen!**");

                var channelId = Context.Interaction.ChannelId;
                if (channelId != null && _campaignManager.IsCampaignActive(channelId.Value))
                {
                    _campaignManager.AddMessage(channelId.Value, "system", $"[SYSTEM] Ein neuer Zauber wurde der Welt hinzugefügt:\n{spell.SpellMarkdown}");
                }
            }
            catch (System.Exception ex)
            {
                await FollowupAsync($"⚠️ Fehler beim Speichern des Zaubers in der Datenbank: {ex.Message}");
            }
        }

        [ComponentInteraction("spell_reject_*")]
        public async Task SpellRejectAsync(string spellId)
        {
            var spell = _campaignManager.GetAndRemovePendingSpell(spellId);
            if (spell == null)
            {
                await RespondAsync("Dieser Zauber wurde bereits bearbeitet oder ist abgelaufen.", ephemeral: true);
                return;
            }

            if (spell.UserId != Context.User.Id)
            {
                await RespondAsync("Nur der Ersteller des Zaubers kann ihn verwerfen.", ephemeral: true);
                _campaignManager.AddPendingSpell(spellId, spell.UserId, spell.Grad, spell.Thema, spell.SpellMarkdown);
                return;
            }

            await RespondAsync("Der Zauber wurde verworfen.", ephemeral: true);
            await ModifyOriginalResponseAsync(x => {
                x.Components = new ComponentBuilder().Build();
            });
        }

        [ComponentInteraction("select_campaign_start")]
        public async Task SelectCampaignStartAsync(string[] selectedValues)
        {
            await DeferAsync();

            var channelId = Context.Interaction.ChannelId;
            if (channelId == null) return;

            var state = _campaignManager.GetCampaignState(channelId.Value);
            if (state == null) return;

            var selection = selectedValues.FirstOrDefault();
            string optName = selection switch {
                "opt_1" => "Option 1",
                "opt_2" => "Option 2",
                "opt_3" => "Option 3",
                _ => "eine Option"
            };

            await ModifyOriginalResponseAsync(x => {
                x.Components = new ComponentBuilder().Build();
            });

            await FollowupAsync($"Der Spieler hat **{optName}** gewählt. Die Geschichte beginnt...\n*Der Dungeon Master bereitet sich vor...*");

            using var scope = _serviceProvider.CreateScope();
            var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();

            var prompt = $"Der Spieler hat {optName} aus den vorherigen Vorschlägen gewählt. Bitte schreibe nun die vollständige erste Szene für diesen Einstieg aus der Perspektive des Dungeon Masters. Beschreibe atmosphärisch die Situation und frage am Ende direkt die Spieler, was sie tun möchten.";
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
}
