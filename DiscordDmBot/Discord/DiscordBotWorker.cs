using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordDmBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordDmBot.Discord
{
    public class DiscordBotWorker : BackgroundService
    {
        private readonly ILogger<DiscordBotWorker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactionService;
        private readonly CampaignManager _campaignManager;

        public DiscordBotWorker(
            ILogger<DiscordBotWorker> logger,
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            DiscordSocketClient client,
            InteractionService interactionService,
            CampaignManager campaignManager)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _client = client;
            _interactionService = interactionService;
            _campaignManager = campaignManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _client.Log += LogAsync;
            _interactionService.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;
            _client.InteractionCreated += InteractionCreatedAsync;

            var token = _configuration["DiscordToken"];
            if (string.IsNullOrWhiteSpace(token) || token == "YOUR_DISCORD_BOT_TOKEN_HERE")
            {
                _logger.LogWarning("Discord token is not set. Bot will not start properly.");
                return;
            }

            await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1, stoppingToken);
        }

        private async Task InteractionCreatedAsync(SocketInteraction interaction)
        {
            try
            {
                var context = new SocketInteractionContext(_client, interaction);
                await _interactionService.ExecuteCommandAsync(context, _serviceProvider);
            }
            catch
            {
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    await interaction.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
                }
            }
        }

        private async Task ReadyAsync()
        {
            await _interactionService.RegisterCommandsGloballyAsync();
            _logger.LogInformation("Bot is connected and ready!");
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot || message.Channel is not SocketTextChannel)
                return;

            var channelId = message.Channel.Id;

            if (!_campaignManager.IsCampaignActive(channelId))
                return;

            _campaignManager.AddMessage(channelId, "user", $"{message.Author.Username}: {message.Content}");

            using var scope = _serviceProvider.CreateScope();
            var ollamaService = scope.ServiceProvider.GetRequiredService<OllamaService>();
            
            var state = _campaignManager.GetCampaignState(channelId);
            if (state == null) return;

            using (message.Channel.EnterTypingState())
            {
                try
                {
                    var response = await ollamaService.GenerateResponseAsync(channelId, state.Context, state.ShortTermMemory);
                    
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        await SendMessageInChunksAsync(message.Channel, response);
                        _campaignManager.AddMessage(channelId, "assistant", response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating response from Ollama");
                    await message.Channel.SendMessageAsync("Ich muss kurz nachdenken... (Verbindung zu Ollama fehlgeschlagen)");
                }
            }

            var threshold = _configuration.GetValue<int>("BotSettings:SummarizationThreshold", 10);
            if (state.MessagesSinceLastSummary >= threshold)
            {
                await RunSummarizationAsync(channelId);
            }
        }

        private async Task RunSummarizationAsync(ulong channelId)
        {
            _logger.LogInformation($"Running summarization for channel {channelId}");
            using var scope = _serviceProvider.CreateScope();
            var ollamaService = scope.ServiceProvider.GetRequiredService<OllamaService>();
            
            var state = _campaignManager.GetCampaignState(channelId);
            if (state == null) return;
            
            var messagesToSummarize = state.ShortTermMemory.Take(10).ToList();
            
            try
            {
                await ollamaService.SummarizeChatAsync(channelId, messagesToSummarize);
                _campaignManager.ClearOldMessages(channelId, 10);
                _logger.LogInformation($"Summarization completed for channel {channelId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run summarization loop.");
            }
        }

        private async Task SendMessageInChunksAsync(ISocketMessageChannel channel, string text)
        {
            const int maxChunkSize = 1900;
            for (int i = 0; i < text.Length; i += maxChunkSize)
            {
                int length = Math.Min(maxChunkSize, text.Length - i);
                await channel.SendMessageAsync(text.Substring(i, length));
            }
        }

        private Task LogAsync(LogMessage log)
        {
            _logger.LogInformation(log.ToString());
            return Task.CompletedTask;
        }
    }
}
