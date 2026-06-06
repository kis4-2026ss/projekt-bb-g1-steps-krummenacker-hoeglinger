using DiscordDmBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DiscordDmBot.Data
{
    public class AppDbContext : DbContext
    {
        private readonly IConfiguration _configuration;

        public AppDbContext(DbContextOptions<AppDbContext> options, IConfiguration configuration) : base(options)
        {
            _configuration = configuration;
        }

        public DbSet<CampaignEvent> CampaignEvents { get; set; }
        public DbSet<DbCampaign> Campaigns { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                optionsBuilder.UseSqlite(connectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // CampaignId is queried often to get past events
            modelBuilder.Entity<CampaignEvent>()
                .HasIndex(e => e.CampaignId);
            
            modelBuilder.Entity<DbCampaign>()
                .HasIndex(c => c.Name)
                .IsUnique();
        }
    }
}
