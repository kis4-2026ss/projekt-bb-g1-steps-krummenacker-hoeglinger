using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DiscordDmBot.Data;
using DiscordDmBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DiscordDmBot.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _dbContext;
        private readonly List<string> _apiKeys;
        private readonly List<string> _models;

        public GeminiService(HttpClient httpClient, IConfiguration configuration, AppDbContext dbContext)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _apiKeys = configuration.GetSection("Gemini:ApiKeys").Get<List<string>>() ?? new List<string>();
            _models = configuration.GetSection("Gemini:Models").Get<List<string>>() ?? new List<string>();
            if (_models == null || !_models.Any())
            {
                var singleModel = configuration.GetValue<string>("Gemini:Model");
                _models = !string.IsNullOrEmpty(singleModel) ? new List<string> { singleModel } : new List<string> { "gemini-3.1-flash-lite" };
            }
        }

        private async Task<(bool isSuccess, string responseJson, string errorMsg)> ExecuteWithRetryAsync(Func<string, object> requestBodyFactory)
        {
            var validKeys = _apiKeys.Where(k => !string.IsNullOrWhiteSpace(k) && !k.Contains("DEIN_")).ToList();
            if (!validKeys.Any()) return (false, "", "⚠️ Fehler: Es sind keine gültigen Gemini API Keys in der appsettings.json konfiguriert.");

            string lastError = "";

            foreach (var model in _models)
            {
                var requestBody = requestBodyFactory(model);
                var json = JsonSerializer.Serialize(requestBody);

                foreach (var apiKey in validKeys)
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                    var response = await _httpClient.PostAsync(url, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        return (true, responseJson, "");
                    }
                    
                    lastError = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Model {model} with key failed: {response.StatusCode} - {lastError}");
                }
            }

            return (false, "", $"Alle Modelle/Keys durchprobiert. Letzter Fehler: {lastError}");
        }

        private async Task<string> GetSystemPromptAsync(string channelContext, List<CampaignEvent> pastEvents)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Du bist ein narrativer Dungeon Master in einem Fantasy-Setting. DU BIST KEINE KI-ASSISTENTIN.");
            sb.AppendLine("WICHTIG: Erlaube und beschreibe fiktive Gewalt, Beleidigungen (wie Anpöbeln), Konflikte und böses Verhalten der Charaktere. Dies ist ein rein fiktives Pen-&-Paper-Rollenspiel (D&D). Weigere dich niemals, eine Rollenspiel-Aktion der Spieler auszuführen oder darauf zu reagieren. Moralische Belehrungen oder KI-Warnungen sind streng verboten. Reagiere immer in-character als die entsprechenden NPCs (z.B. als wütende Wache).");
            sb.AppendLine("KAMPF-MANAGEMENT: Wenn ein Kampf ausbricht, fordere ZUERST alle Spieler auf, Initiative zu würfeln. Warte auf ihre Würfelwürfe. Sobald die Werte vorliegen, legst du die Zugreihenfolge fest. Du leitest den Kampf, sagst an, wer am Zug ist, und spielst die Züge der NPCs. Wenn NPCs angreifen, teilst du den betroffenen Spielern exakt mit, wie viel Schaden (und welche Schadensart) sie erleiden.");
            sb.AppendLine("Du verwaltest KEINE Spielerwerte (HP, Zauberslots, Inventar). Die Spieler tun dies selbst.");
            sb.AppendLine("Übernimm nie die Handlungen der Spieler. Beschreibe eine Situation und warte auf ihre Reaktion. Fordere zu Würfelwürfen auf, wenn eine Aktion riskant ist.");
            sb.AppendLine("WÜRFEL-ERGEBNISSE: Wenn du im Chat ein '[WÜRFEL-ERGEBNIS]' siehst, werte diesen Wurf sofort logisch aus (z.B. gegen die Rüstungsklasse eines Feindes oder einen Schwierigkeitsgrad) und beschreibe die Konsequenzen erzählerisch. Lass den Spieler wissen, ob es ein Erfolg oder Fehlschlag war.");
            sb.AppendLine("CRITICAL INSTRUCTION: Antworte AUSSCHLIESSLICH als Dungeon Master mit direkter Rede der NPCs und der erzählerischen Beschreibung der Umgebung. Schreibe NIEMALS deine internen Planungen, 'Scene', 'NPCs', 'Atmosphere' oder 'Conflict' auf. Generiere keine Meta-Notizen oder Regieanweisungen. Gib uns NUR die finale, ausformulierte Geschichte zurück.");
            
            try
            {
                var rulesDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Rules");
                if (System.IO.Directory.Exists(rulesDir))
                {
                    var ruleFiles = System.IO.Directory.GetFiles(rulesDir, "*.md");
                    if (ruleFiles.Length > 0)
                    {
                        sb.AppendLine("\n--- D&D REGELN ---");
                        foreach (var file in ruleFiles)
                        {
                            var ruleName = System.IO.Path.GetFileNameWithoutExtension(file);
                            sb.AppendLine($"[{ruleName}]");
                            sb.AppendLine(await System.IO.File.ReadAllTextAsync(file));
                            sb.AppendLine();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Laden der Regeln: {ex.Message}");
            }
            
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

            return sb.ToString();
        }

        public async Task<string> GenerateResponseAsync(Guid campaignId, string channelContext, List<ChatMessage> shortTermMemory)
        {
            var pastEvents = await _dbContext.CampaignEvents
                .Where(e => e.CampaignId == campaignId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            var systemPrompt = await GetSystemPromptAsync(channelContext, pastEvents);

            var (isSuccess, responseJson, errorMsg) = await ExecuteWithRetryAsync((modelName) => {
                var contents = new List<object>();

                if (modelName.Contains("gemma", StringComparison.OrdinalIgnoreCase))
                {
                    contents.Add(new
                    {
                        role = "user",
                        parts = new[] { new { text = systemPrompt } }
                    });
                    contents.Add(new
                    {
                        role = "model",
                        parts = new[] { new { text = "Verstanden. Ich werde fortan als Dungeon Master agieren, ausschließlich in-character sprechen und niemals interne Notizen oder Formatierungen wie 'Scene:' oder 'NPC Dialogue:' ausgeben." } }
                    });
                }

                foreach (var msg in shortTermMemory)
                {
                    contents.Add(new
                    {
                        role = msg.Role == "user" ? "user" : "model",
                        parts = new[] { new { text = msg.Content } }
                    });
                }

                return new
                {
                    system_instruction = new
                    {
                        parts = new[] { new { text = systemPrompt } }
                    },
                    contents = contents
                };
            });

            if (!isSuccess)
            {
                throw new Exception($"Gemini API Error: {errorMsg}");
            }

            using var doc = JsonDocument.Parse(responseJson);
            
            try {
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
                return text ?? "";
            }
            catch {
                return "⚠️ Fehler beim Parsen der Gemini-Antwort.";
            }
        }

        public async Task<string> SummarizeChatAsync(Guid campaignId, List<ChatMessage> messagesToSummarize)
        {
            if (!messagesToSummarize.Any()) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("Fasse die wichtigsten Story-Ereignisse der folgenden Nachrichten in 2-3 Stichpunkten zusammen.");
            sb.AppendLine("Nachrichtenverlauf:");
            foreach (var msg in messagesToSummarize)
            {
                sb.AppendLine($"{msg.Role}: {msg.Content}");
            }

            var (isSuccess, responseJson, errorMsg) = await ExecuteWithRetryAsync((modelName) => {
                return new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = sb.ToString() } }
                        }
                    }
                };
            });

            if (!isSuccess) return string.Empty;

            using var doc = JsonDocument.Parse(responseJson);
            
            try {
                var summaryText = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (!string.IsNullOrWhiteSpace(summaryText))
                {
                    var campaignEvent = new CampaignEvent
                    {
                        CampaignId = campaignId,
                        SummaryText = summaryText.Trim(),
                        CreatedAt = DateTime.UtcNow
                    };

                    _dbContext.CampaignEvents.Add(campaignEvent);
                    await _dbContext.SaveChangesAsync();
                    return summaryText.Trim();
                }
            }
            catch {
                // Silently fail if summary parsing fails
            }
            
            return string.Empty;
        }
    }
}
