using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordDmBot.Data;
using DiscordDmBot.Discord;
using DiscordDmBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>();

builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
});

builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));

builder.Services.AddSingleton<CampaignManager>();
builder.Services.AddScoped<OllamaService>();

builder.Services.AddHostedService<DiscordBotWorker>();

var host = builder.Build();
host.Run();
