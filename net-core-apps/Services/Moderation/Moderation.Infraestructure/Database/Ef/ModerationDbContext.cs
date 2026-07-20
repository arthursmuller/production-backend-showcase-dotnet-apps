using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Repositories;
using MySqlConnector;

namespace Moderation.Infra.Database.Ef;

public sealed class ModerationDbContext(DbContextOptions<ModerationDbContext> options) : DbContext(options)
{
    public DbSet<ModerationReport> Reports => Set<ModerationReport>();
    public DbSet<EvidenceReference> EvidenceReferences => Set<EvidenceReference>();
    public DbSet<ModerationCase> Cases => Set<ModerationCase>();
    public DbSet<ModerationDecisionState> DecisionStates => Set<ModerationDecisionState>();
    public DbSet<ModerationDecisionStateRevision> DecisionStateRevisions => Set<ModerationDecisionStateRevision>();
    public DbSet<ModerationAuditEntry> AuditEntries => Set<ModerationAuditEntry>();
    public DbSet<LegalHold> LegalHolds => Set<LegalHold>();
    public DbSet<ModerationCaseMessage> CaseMessages => Set<ModerationCaseMessage>();
    public DbSet<ModerationRetentionCheckpoint> RetentionCheckpoints => Set<ModerationRetentionCheckpoint>();
    public DbSet<ModerationUserErasure> UserErasures => Set<ModerationUserErasure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ModerationDbContext).Assembly);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectAuditMutation();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectAuditMutation();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateReport(exception))
        {
            throw new DuplicateModerationReportException(exception);
        }
    }

    private void RejectAuditMutation()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<ModerationAuditEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Moderation audit entries are append-only.");
        }
        if (ChangeTracker.Entries<ModerationCaseMessage>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Moderation case messages are append-only.");
        }
    }

    private bool IsDuplicateReport(DbUpdateException exception) =>
        ChangeTracker.Entries<ModerationReport>().Any(entry => entry.State == EntityState.Added)
        && exception.InnerException is MySqlException { Number: 1062 };
}
