using AgileAI.Studio.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgileAI.Studio.Api.Data;

public class StudioDbContext(DbContextOptions<StudioDbContext> options) : DbContext(options)
{
    public DbSet<ProviderConnection> ProviderConnections => Set<ProviderConnection>();
    public DbSet<StudioModel> Models => Set<StudioModel>();
    public DbSet<AgentDefinition> Agents => Set<AgentDefinition>();
    public DbSet<AgentToolSelection> AgentToolSelections => Set<AgentToolSelection>();
    public DbSet<AgentSkillSelection> AgentSkillSelections => Set<AgentSkillSelection>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();
    public DbSet<ToolApprovalRequestEntity> ToolApprovalRequests => Set<ToolApprovalRequestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderConnection>(entity =>
        {
            entity.ToTable("ProviderConnections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.ApiKey).HasMaxLength(4000);
            entity.Property(x => x.BaseUrl).HasMaxLength(500);
            entity.Property(x => x.Endpoint).HasMaxLength(500);
            entity.Property(x => x.ProviderName).HasMaxLength(120);
            entity.Property(x => x.RelativePath).HasMaxLength(240);
            entity.Property(x => x.ApiKeyHeaderName).HasMaxLength(120);
            entity.Property(x => x.AuthMode).HasMaxLength(80);
            entity.Property(x => x.ApiVersion).HasMaxLength(80);
            entity.HasMany(x => x.Models)
                .WithOne(x => x.ProviderConnection)
                .HasForeignKey(x => x.ProviderConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudioModel>(entity =>
        {
            entity.ToTable("Models");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(120);
            entity.Property(x => x.ModelKey).HasMaxLength(200);
            entity.Property(x => x.ThinkingIntensitiesJson).HasMaxLength(1000);
            entity.HasMany(x => x.Agents)
                .WithOne(x => x.StudioModel)
                .HasForeignKey(x => x.StudioModelId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AgentDefinition>(entity =>
        {
            entity.ToTable("Agents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(600);
            entity.Property(x => x.SystemPrompt).HasMaxLength(8000);
            entity.Property(x => x.ThinkingIntensity).HasMaxLength(80);
            entity.HasMany(x => x.Conversations)
                .WithOne(x => x.AgentDefinition)
                .HasForeignKey(x => x.AgentDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("Conversations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.HasMany(x => x.Messages)
                .WithOne(x => x.Conversation)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.ToolApprovalRequests)
                .WithOne(x => x.Conversation)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationMessage>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Content).HasMaxLength(32000);
            entity.Property(x => x.AppliedSkillName).HasMaxLength(160);
            entity.Property(x => x.AppliedToolNamesJson).HasMaxLength(4000);
        });

        modelBuilder.Entity<AgentToolSelection>(entity =>
        {
            entity.ToTable("AgentToolSelections");
            entity.HasKey(x => x.AgentDefinitionId);
            entity.Property(x => x.ToolNamesJson).HasMaxLength(8000);
        });

        modelBuilder.Entity<AgentSkillSelection>(entity =>
        {
            entity.ToTable("AgentSkillSelections");
            entity.HasKey(x => x.AgentDefinitionId);
            entity.Property(x => x.SkillNamesJson).HasMaxLength(8000);
        });

        modelBuilder.Entity<ToolApprovalRequestEntity>(entity =>
        {
            entity.ToTable("ToolApprovalRequests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApprovalRequestId).HasMaxLength(80);
            entity.Property(x => x.ToolCallId).HasMaxLength(200);
            entity.Property(x => x.ToolName).HasMaxLength(120);
            entity.Property(x => x.ArgumentsJson).HasMaxLength(32000);
            entity.Property(x => x.AssistantToolCallContent).HasMaxLength(32000);
            entity.Property(x => x.DecisionComment).HasMaxLength(2000);
            entity.Property(x => x.ResultContent).HasMaxLength(32000);
            entity.Property(x => x.StandardOutput).HasMaxLength(32000);
            entity.Property(x => x.StandardError).HasMaxLength(32000);
            entity.HasIndex(x => x.ApprovalRequestId).IsUnique();
        });
    }
}
