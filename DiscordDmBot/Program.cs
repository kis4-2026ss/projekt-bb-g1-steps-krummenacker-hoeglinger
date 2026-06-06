using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordDmBot.Data;
using DiscordDmBot.Discord;
using DiscordDmBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Explicitly add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Database
builder.Services.AddDbContext<AppDbContext>();

// ✅ FIXED: Create DiscordSocketClient directly with correct intents
builder.Services.AddSingleton<DiscordSocketClient>(_ => {
    var config = new DiscordSocketConfig {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
        LogLevel = LogSeverity.Verbose
    };

    return new DiscordSocketClient(config);
});

// InteractionService depends on client
builder.Services.AddSingleton<InteractionService>(sp => {
    var client = sp.GetRequiredService<DiscordSocketClient>();
    var config = new InteractionServiceConfig {
        LogLevel = LogSeverity.Verbose
    };
    return new InteractionService(client, config);
});

// Services
builder.Services.AddSingleton<CampaignManager>();
builder.Services.AddScoped<OllamaService>();

// Worker
builder.Services.AddHostedService<DiscordBotWorker>();

var host = builder.Build();
host.Run();