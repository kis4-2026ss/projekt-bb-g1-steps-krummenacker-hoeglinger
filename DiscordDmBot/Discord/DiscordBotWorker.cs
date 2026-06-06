using System;
using System.Linq;
using System.Reflection;
using System.Text;
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
            _interactionService.SlashCommandExecuted += SlashCommandExecutedAsync;

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

        private async Task SlashCommandExecutedAsync(SlashCommandInfo command, IInteractionContext context, IResult result)
        {
            if (!result.IsSuccess)
            {
                _logger.LogError($"Slash command failed: {result.ErrorReason}");
                try
                {
                    // Try to send the error to the channel
                    var errorMsg = $"❌ **Command Error:** `{result.ErrorReason}`";
                    if (context.Interaction.HasResponded)
                    {
                        await context.Interaction.FollowupAsync(errorMsg, ephemeral: true);
                    }
                    else
                    {
                        await context.Interaction.RespondAsync(errorMsg, ephemeral: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send error message to Discord.");
                }
            }
        }

        private async Task InteractionCreatedAsync(SocketInteraction interaction)
        {
            try
            {
                var context = new SocketInteractionContext(_client, interaction);
                await _interactionService.ExecuteCommandAsync(context, _serviceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while executing interaction.");
                try
                {
                    if (!interaction.HasResponded)
                    {
                        await interaction.RespondAsync($"❌ **Critical Error:** `{ex.Message}`", ephemeral: true);
                    }
                    else
                    {
                        await interaction.FollowupAsync($"❌ **Critical Error:** `{ex.Message}`", ephemeral: true);
                    }
                }
                catch { /* Ignore if we can't respond */ }
            }
        }

        private async Task ReadyAsync()
        {
            await _interactionService.RegisterCommandsGloballyAsync();
            _logger.LogInformation("Bot is connected and ready!");
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return;

            _logger.LogInformation($"[CHAT] {message.Author.Username} in {message.Channel.Name}: {message.Content}");
            await Task.CompletedTask;
        }

        private Task LogAsync(LogMessage log)
        {
            _logger.LogInformation(log.ToString());
            Console.WriteLine(log.ToString());

            return Task.CompletedTask;
        }
    }
}
