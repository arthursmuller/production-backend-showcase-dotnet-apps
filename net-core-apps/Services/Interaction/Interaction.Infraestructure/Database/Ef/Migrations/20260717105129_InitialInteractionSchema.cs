using System;
using Interaction.Infraestructure.Database.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Interaction.Infraestructure.Database.Ef.Migrations;

[DbContext(typeof(InteractionDbContext))]
[Migration("20260717105129_InitialInteractionSchema")]
public class InitialInteractionSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase().Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("InboxState", (ColumnsBuilder table) => new
        {
            Id = table.Column<long>("bigint").Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
            MessageId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: false, null, null, null, null, null, "ascii_general_ci"),
            ConsumerId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: false, null, null, null, null, null, "ascii_general_ci"),
            LockId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: false, null, null, null, null, null, "ascii_general_ci"),
            RowVersion = table.Column<DateTime>("timestamp(6)", null, null, rowVersion: true, null, nullable: true),
            Received = table.Column<DateTime>("datetime(6)"),
            ReceiveCount = table.Column<int>("int"),
            ExpirationTime = table.Column<DateTime>("datetime(6)", null, null, rowVersion: false, null, nullable: true),
            Consumed = table.Column<DateTime>("datetime(6)", null, null, rowVersion: false, null, nullable: true),
            Delivered = table.Column<DateTime>("datetime(6)", null, null, rowVersion: false, null, nullable: true),
            LastSequenceNumber = table.Column<long>("bigint", null, null, rowVersion: false, null, nullable: true)
        }, null, table =>
        {
            table.PrimaryKey("PK_InboxState", x => x.Id);
            table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("OutboxState", (ColumnsBuilder table) => new
        {
            OutboxId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: false, null, null, null, null, null, "ascii_general_ci"),
            LockId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: false, null, null, null, null, null, "ascii_general_ci"),
            RowVersion = table.Column<DateTime>("timestamp(6)", null, null, rowVersion: true, null, nullable: true),
            Created = table.Column<DateTime>("datetime(6)"),
            Delivered = table.Column<DateTime>("datetime(6)", null, null, rowVersion: false, null, nullable: true),
            LastSequenceNumber = table.Column<long>("bigint", null, null, rowVersion: false, null, nullable: true)
        }, null, table =>
        {
            table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("ResourceStates", (ColumnsBuilder table) => new
        {
            ResourceType = table.Column<byte>("tinyint unsigned"),
            ResourceId = table.Column<string>("varchar(128)", false, 128, rowVersion: false, null, nullable: false, null, null, null, null, null, "utf8mb4_bin").Annotation("MySql:CharSet", "utf8mb4"),
            Lifecycle = table.Column<byte>("tinyint unsigned"),
            Visibility = table.Column<byte>("tinyint unsigned"),
            AppliedVersion = table.Column<long>("bigint"),
            HighestSeenVersion = table.Column<long>("bigint"),
            HasGap = table.Column<bool>("tinyint(1)"),
            ConcurrencyVersion = table.Column<long>("bigint"),
            UpdatedAtUtc = table.Column<DateTimeOffset>("datetime(6)")
        }, null, table =>
        {
            table.PrimaryKey("PK_ResourceStates", x => new { x.ResourceType, x.ResourceId });
            table.CheckConstraint("CK_ResourceState_Type", "`ResourceType` IN (1,2)");
            table.CheckConstraint("CK_ResourceState_Versions", "`AppliedVersion` >= 0 AND `HighestSeenVersion` >= `AppliedVersion`");
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("ViewObservations", (ColumnsBuilder table) => new
        {
            Id = table.Column<long>("bigint").Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
            ResourceType = table.Column<byte>("tinyint unsigned"),
            ResourceId = table.Column<string>("varchar(128)", false, 128, rowVersion: false, null, nullable: false, null, null, null, null, null, "utf8mb4_bin").Annotation("MySql:CharSet", "utf8mb4"),
            ObserverHash = table.Column<string>("char(64)", false, null, rowVersion: false, null, nullable: false, null, null, null, null, null, "ascii_bin"),
            BucketStartUtc = table.Column<DateTimeOffset>("datetime(6)"),
            ReceivedAtUtc = table.Column<DateTimeOffset>("datetime(6)"),
            ExpiresAtUtc = table.Column<DateTimeOffset>("datetime(6)"),
            ProcessedAtUtc = table.Column<DateTimeOffset>("datetime(6)", null, null, rowVersion: false, null, nullable: true)
        }, null, table =>
        {
            table.PrimaryKey("PK_ViewObservations", x => x.Id);
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("OutboxMessage", delegate (ColumnsBuilder table)
        {
            OperationBuilder<AddColumnOperation> sequenceNumber = table.Column<long>("bigint").Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);
            OperationBuilder<AddColumnOperation> enqueueTime = table.Column<DateTime>("datetime(6)", null, null, rowVersion: false, null, nullable: true);
            OperationBuilder<AddColumnOperation> sentTime = table.Column<DateTime>("datetime(6)");
            OperationBuilder<AddColumnOperation> headers = table.Column<string>("longtext", null, null, rowVersion: false, null, nullable: true).Annotation("MySql:CharSet", "utf8mb4");
            OperationBuilder<AddColumnOperation> properties = table.Column<string>("longtext", null, null, rowVersion: false, null, nullable: true).Annotation("MySql:CharSet", "utf8mb4");
            OperationBuilder<AddColumnOperation> inboxMessageId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: true, null, null, null, null, null, "ascii_general_ci");
            OperationBuilder<AddColumnOperation> inboxConsumerId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: true, null, null, null, null, null, "ascii_general_ci");
            OperationBuilder<AddColumnOperation> outboxId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: true, null, null, null, null, null, "ascii_general_ci");
            OperationBuilder<AddColumnOperation> messageId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: false, null, null, null, null, null, "ascii_general_ci");
            int? maxLength = 256;
            OperationBuilder<AddColumnOperation> contentType = table.Column<string>("varchar(256)", null, maxLength).Annotation("MySql:CharSet", "utf8mb4");
            OperationBuilder<AddColumnOperation> messageType = table.Column<string>("longtext").Annotation("MySql:CharSet", "utf8mb4");
            OperationBuilder<AddColumnOperation> body = table.Column<string>("longtext").Annotation("MySql:CharSet", "utf8mb4");
            OperationBuilder<AddColumnOperation> conversationId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: true, null, null, null, null, null, "ascii_general_ci");
            OperationBuilder<AddColumnOperation> correlationId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: true, null, null, null, null, null, "ascii_general_ci");
            OperationBuilder<AddColumnOperation> initiatorId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: true, null, null, null, null, null, "ascii_general_ci");
            OperationBuilder<AddColumnOperation> requestId = table.Column<Guid>("char(36)", null, null, rowVersion: false, null, nullable: true, null, null, null, null, null, "ascii_general_ci");
            maxLength = 256;
            OperationBuilder<AddColumnOperation> sourceAddress = table.Column<string>("varchar(256)", null, maxLength, rowVersion: false, null, nullable: true).Annotation("MySql:CharSet", "utf8mb4");
            maxLength = 256;
            OperationBuilder<AddColumnOperation> destinationAddress = table.Column<string>("varchar(256)", null, maxLength, rowVersion: false, null, nullable: true).Annotation("MySql:CharSet", "utf8mb4");
            maxLength = 256;
            OperationBuilder<AddColumnOperation> responseAddress = table.Column<string>("varchar(256)", null, maxLength, rowVersion: false, null, nullable: true).Annotation("MySql:CharSet", "utf8mb4");
            maxLength = 256;
            return new
            {
                SequenceNumber = sequenceNumber,
                EnqueueTime = enqueueTime,
                SentTime = sentTime,
                Headers = headers,
                Properties = properties,
                InboxMessageId = inboxMessageId,
                InboxConsumerId = inboxConsumerId,
                OutboxId = outboxId,
                MessageId = messageId,
                ContentType = contentType,
                MessageType = messageType,
                Body = body,
                ConversationId = conversationId,
                CorrelationId = correlationId,
                InitiatorId = initiatorId,
                RequestId = requestId,
                SourceAddress = sourceAddress,
                DestinationAddress = destinationAddress,
                ResponseAddress = responseAddress,
                FaultAddress = table.Column<string>("varchar(256)", null, maxLength, rowVersion: false, null, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                ExpirationTime = table.Column<DateTime>("datetime(6)", null, null, rowVersion: false, null, nullable: true)
            };
        }, null, table =>
        {
            table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
            table.ForeignKey("FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId", x => new { x.InboxMessageId, x.InboxConsumerId }, "InboxState", new string[2] { "MessageId", "ConsumerId" });
            table.ForeignKey("FK_OutboxMessage_OutboxState_OutboxId", x => x.OutboxId, "OutboxState", "OutboxId");
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("EngagementSummaries", (ColumnsBuilder table) => new
        {
            ResourceType = table.Column<byte>("tinyint unsigned"),
            ResourceId = table.Column<string>("varchar(128)", false, 128, rowVersion: false, null, nullable: false, null, null, null, null, null, "utf8mb4_bin").Annotation("MySql:CharSet", "utf8mb4"),
            LikeCount = table.Column<long>("bigint"),
            DislikeCount = table.Column<long>("bigint"),
            RatingCount = table.Column<long>("bigint"),
            RatingTotal = table.Column<long>("bigint"),
            ViewCount = table.Column<long>("bigint"),
            CommentCount = table.Column<long>("bigint"),
            ShareCount = table.Column<long>("bigint"),
            ContributionSourceVersion = table.Column<long>("bigint"),
            ContributionHighestSeenVersion = table.Column<long>("bigint"),
            ContributionHasGap = table.Column<bool>("tinyint(1)"),
            Version = table.Column<long>("bigint"),
            UpdatedAtUtc = table.Column<DateTimeOffset>("datetime(6)"),
            LastReconciledAtUtc = table.Column<DateTimeOffset>("datetime(6)")
        }, null, table =>
        {
            table.PrimaryKey("PK_EngagementSummaries", x => new { x.ResourceType, x.ResourceId });
            table.CheckConstraint("CK_EngagementSummary_ContributionVersions", "`ContributionSourceVersion` >= 0 AND `ContributionHighestSeenVersion` >= `ContributionSourceVersion`");
            table.CheckConstraint("CK_EngagementSummary_Nonnegative", "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 AND `ShareCount` >= 0");
            table.CheckConstraint("CK_EngagementSummary_RatingTotal", "`RatingTotal` <= `RatingCount` * 5");
            table.ForeignKey("FK_EngagementSummaries_ResourceStates_ResourceType_ResourceId", x => new { x.ResourceType, x.ResourceId }, "ResourceStates", new string[2] { "ResourceType", "ResourceId" }, null, ReferentialAction.NoAction, ReferentialAction.Cascade);
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("Ratings", delegate (ColumnsBuilder table)
        {
            OperationBuilder<AddColumnOperation> resourceType = table.Column<byte>("tinyint unsigned");
            OperationBuilder<AddColumnOperation> resourceId = table.Column<string>("varchar(128)", false, 128, rowVersion: false, null, nullable: false, null, null, null, null, null, "utf8mb4_bin").Annotation("MySql:CharSet", "utf8mb4");
            OperationBuilder<AddColumnOperation> actorId = table.Column<long>("bigint");
            OperationBuilder<AddColumnOperation> score = table.Column<byte>("tinyint unsigned");
            int? maxLength = 500;
            return new
            {
                ResourceType = resourceType,
                ResourceId = resourceId,
                ActorId = actorId,
                Score = score,
                Comment = table.Column<string>("varchar(500)", null, maxLength, rowVersion: false, null, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                Version = table.Column<long>("bigint"),
                UpdatedAtUtc = table.Column<DateTimeOffset>("datetime(6)")
            };
        }, null, table =>
        {
            table.PrimaryKey("PK_Ratings", x => new { x.ResourceType, x.ResourceId, x.ActorId });
            table.CheckConstraint("CK_Rating_Score", "`Score` BETWEEN 1 AND 5");
            table.ForeignKey("FK_Ratings_ResourceStates_ResourceType_ResourceId", x => new { x.ResourceType, x.ResourceId }, "ResourceStates", new string[2] { "ResourceType", "ResourceId" }, null, ReferentialAction.NoAction, ReferentialAction.Cascade);
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("Reactions", (ColumnsBuilder table) => new
        {
            ResourceType = table.Column<byte>("tinyint unsigned"),
            ResourceId = table.Column<string>("varchar(128)", false, 128, rowVersion: false, null, nullable: false, null, null, null, null, null, "utf8mb4_bin").Annotation("MySql:CharSet", "utf8mb4"),
            ActorId = table.Column<long>("bigint"),
            Kind = table.Column<byte>("tinyint unsigned"),
            Version = table.Column<long>("bigint"),
            UpdatedAtUtc = table.Column<DateTimeOffset>("datetime(6)")
        }, null, table =>
        {
            table.PrimaryKey("PK_Reactions", x => new { x.ResourceType, x.ResourceId, x.ActorId });
            table.CheckConstraint("CK_Reaction_Kind", "`Kind` IN (1,2)");
            table.ForeignKey("FK_Reactions_ResourceStates_ResourceType_ResourceId", x => new { x.ResourceType, x.ResourceId }, "ResourceStates", new string[2] { "ResourceType", "ResourceId" }, null, ReferentialAction.NoAction, ReferentialAction.Cascade);
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateTable("ResourceStateGaps", (ColumnsBuilder table) => new
        {
            ResourceType = table.Column<byte>("tinyint unsigned"),
            ResourceId = table.Column<string>("varchar(128)", false, 128, rowVersion: false, null, nullable: false, null, null, null, null, null, "utf8mb4_bin").Annotation("MySql:CharSet", "utf8mb4"),
            SourceVersion = table.Column<long>("bigint"),
            Lifecycle = table.Column<byte>("tinyint unsigned"),
            Visibility = table.Column<byte>("tinyint unsigned"),
            OccurredAtUtc = table.Column<DateTimeOffset>("datetime(6)"),
            ReceivedAtUtc = table.Column<DateTimeOffset>("datetime(6)")
        }, null, table =>
        {
            table.PrimaryKey("PK_ResourceStateGaps", x => new { x.ResourceType, x.ResourceId, x.SourceVersion });
            table.ForeignKey("FK_ResourceStateGaps_ResourceStates_ResourceType_ResourceId", x => new { x.ResourceType, x.ResourceId }, "ResourceStates", new string[2] { "ResourceType", "ResourceId" }, null, ReferentialAction.NoAction, ReferentialAction.Cascade);
        }).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateIndex("IX_EngagementSummary_Reconcile", "EngagementSummaries", new string[3] { "LastReconciledAtUtc", "ResourceType", "ResourceId" });
        migrationBuilder.CreateIndex("IX_InboxState_Delivered", "InboxState", "Delivered");
        migrationBuilder.CreateIndex("IX_OutboxMessage_EnqueueTime", "OutboxMessage", "EnqueueTime");
        migrationBuilder.CreateIndex("IX_OutboxMessage_ExpirationTime", "OutboxMessage", "ExpirationTime");
        migrationBuilder.CreateIndex("IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber", "OutboxMessage", new string[3] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" }, null, unique: true);
        migrationBuilder.CreateIndex("IX_OutboxMessage_OutboxId_SequenceNumber", "OutboxMessage", new string[2] { "OutboxId", "SequenceNumber" }, null, unique: true);
        migrationBuilder.CreateIndex("IX_OutboxState_Created", "OutboxState", "Created");
        migrationBuilder.CreateIndex("IX_Rating_Actor_Updated", "Ratings", new string[2] { "ActorId", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex("IX_Reaction_Actor_Updated", "Reactions", new string[2] { "ActorId", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex("IX_Reaction_Resource_Kind", "Reactions", new string[3] { "ResourceType", "ResourceId", "Kind" });
        migrationBuilder.CreateIndex("IX_ResourceStateGap_Received", "ResourceStateGaps", "ReceivedAtUtc");
        migrationBuilder.CreateIndex("IX_ResourceState_Gap_Updated", "ResourceStates", new string[2] { "HasGap", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex("IX_ViewObservation_Expiry", "ViewObservations", "ExpiresAtUtc");
        migrationBuilder.CreateIndex("IX_ViewObservation_Pending", "ViewObservations", new string[2] { "ProcessedAtUtc", "Id" });
        migrationBuilder.CreateIndex("UX_ViewObservation_Dedupe", "ViewObservations", new string[4] { "ResourceType", "ResourceId", "ObserverHash", "BucketStartUtc" }, null, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("EngagementSummaries");
        migrationBuilder.DropTable("OutboxMessage");
        migrationBuilder.DropTable("Ratings");
        migrationBuilder.DropTable("Reactions");
        migrationBuilder.DropTable("ResourceStateGaps");
        migrationBuilder.DropTable("ViewObservations");
        migrationBuilder.DropTable("InboxState");
        migrationBuilder.DropTable("OutboxState");
        migrationBuilder.DropTable("ResourceStates");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10").HasAnnotation("Relational:MaxIdentifierLength", 64);
        modelBuilder.AutoIncrementColumns();
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.EngagementSummary", delegate (EntityTypeBuilder b)
        {
            b.Property<byte>("ResourceType").HasColumnType("tinyint unsigned");
            b.Property<string>("ResourceId").HasMaxLength(128).IsUnicode(unicode: false)
                .HasColumnType("varchar(128)")
                .UseCollation("utf8mb4_bin");
            b.Property<long>("CommentCount").HasColumnType("bigint");
            b.Property<bool>("ContributionHasGap").HasColumnType("tinyint(1)");
            b.Property<long>("ContributionHighestSeenVersion").HasColumnType("bigint");
            b.Property<long>("ContributionSourceVersion").HasColumnType("bigint");
            b.Property<long>("DislikeCount").HasColumnType("bigint");
            b.Property<DateTimeOffset>("LastReconciledAtUtc").HasColumnType("datetime(6)");
            b.Property<long>("LikeCount").HasColumnType("bigint");
            b.Property<long>("RatingCount").HasColumnType("bigint");
            b.Property<long>("RatingTotal").HasColumnType("bigint");
            b.Property<long>("ShareCount").HasColumnType("bigint");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("datetime(6)");
            b.Property<long>("Version").IsConcurrencyToken().HasColumnType("bigint");
            b.Property<long>("ViewCount").HasColumnType("bigint");
            b.HasKey("ResourceType", "ResourceId");
            b.HasIndex("LastReconciledAtUtc", "ResourceType", "ResourceId").HasDatabaseName("IX_EngagementSummary_Reconcile");
            b.ToTable("EngagementSummaries", null, delegate (TableBuilder t)
            {
                t.HasCheckConstraint("CK_EngagementSummary_ContributionVersions", "`ContributionSourceVersion` >= 0 AND `ContributionHighestSeenVersion` >= `ContributionSourceVersion`");
                t.HasCheckConstraint("CK_EngagementSummary_Nonnegative", "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 AND `ShareCount` >= 0");
                t.HasCheckConstraint("CK_EngagementSummary_RatingTotal", "`RatingTotal` <= `RatingCount` * 5");
            });
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.Rating", delegate (EntityTypeBuilder b)
        {
            b.Property<byte>("ResourceType").HasColumnType("tinyint unsigned");
            b.Property<string>("ResourceId").HasMaxLength(128).IsUnicode(unicode: false)
                .HasColumnType("varchar(128)")
                .UseCollation("utf8mb4_bin");
            b.Property<long>("ActorId").HasColumnType("bigint");
            b.Property<string>("Comment").HasMaxLength(500).HasColumnType("varchar(500)");
            b.Property<byte>("Score").HasColumnType("tinyint unsigned");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("datetime(6)");
            b.Property<long>("Version").IsConcurrencyToken().HasColumnType("bigint");
            b.HasKey("ResourceType", "ResourceId", "ActorId");
            b.HasIndex("ActorId", "UpdatedAtUtc").HasDatabaseName("IX_Rating_Actor_Updated");
            b.ToTable("Ratings", null, delegate (TableBuilder t)
            {
                t.HasCheckConstraint("CK_Rating_Score", "`Score` BETWEEN 1 AND 5");
            });
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.Reaction", delegate (EntityTypeBuilder b)
        {
            b.Property<byte>("ResourceType").HasColumnType("tinyint unsigned");
            b.Property<string>("ResourceId").HasMaxLength(128).IsUnicode(unicode: false)
                .HasColumnType("varchar(128)")
                .UseCollation("utf8mb4_bin");
            b.Property<long>("ActorId").HasColumnType("bigint");
            b.Property<byte>("Kind").HasColumnType("tinyint unsigned");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("datetime(6)");
            b.Property<long>("Version").IsConcurrencyToken().HasColumnType("bigint");
            b.HasKey("ResourceType", "ResourceId", "ActorId");
            b.HasIndex("ActorId", "UpdatedAtUtc").HasDatabaseName("IX_Reaction_Actor_Updated");
            b.HasIndex("ResourceType", "ResourceId", "Kind").HasDatabaseName("IX_Reaction_Resource_Kind");
            b.ToTable("Reactions", null, delegate (TableBuilder t)
            {
                t.HasCheckConstraint("CK_Reaction_Kind", "`Kind` IN (1,2)");
            });
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.ResourceState", delegate (EntityTypeBuilder b)
        {
            b.Property<byte>("ResourceType").HasColumnType("tinyint unsigned");
            b.Property<string>("ResourceId").HasMaxLength(128).IsUnicode(unicode: false)
                .HasColumnType("varchar(128)")
                .UseCollation("utf8mb4_bin");
            b.Property<long>("AppliedVersion").HasColumnType("bigint");
            b.Property<long>("ConcurrencyVersion").IsConcurrencyToken().HasColumnType("bigint");
            b.Property<bool>("HasGap").HasColumnType("tinyint(1)");
            b.Property<long>("HighestSeenVersion").HasColumnType("bigint");
            b.Property<byte>("Lifecycle").HasColumnType("tinyint unsigned");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("datetime(6)");
            b.Property<byte>("Visibility").HasColumnType("tinyint unsigned");
            b.HasKey("ResourceType", "ResourceId");
            b.HasIndex("HasGap", "UpdatedAtUtc").HasDatabaseName("IX_ResourceState_Gap_Updated");
            b.ToTable("ResourceStates", null, delegate (TableBuilder t)
            {
                t.HasCheckConstraint("CK_ResourceState_Type", "`ResourceType` IN (1,2)");
                t.HasCheckConstraint("CK_ResourceState_Versions", "`AppliedVersion` >= 0 AND `HighestSeenVersion` >= `AppliedVersion`");
            });
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.ResourceStateGap", delegate (EntityTypeBuilder b)
        {
            b.Property<byte>("ResourceType").HasColumnType("tinyint unsigned");
            b.Property<string>("ResourceId").HasMaxLength(128).IsUnicode(unicode: false)
                .HasColumnType("varchar(128)")
                .UseCollation("utf8mb4_bin");
            b.Property<long>("SourceVersion").HasColumnType("bigint");
            b.Property<byte>("Lifecycle").HasColumnType("tinyint unsigned");
            b.Property<DateTimeOffset>("OccurredAtUtc").HasColumnType("datetime(6)");
            b.Property<DateTimeOffset>("ReceivedAtUtc").HasColumnType("datetime(6)");
            b.Property<byte>("Visibility").HasColumnType("tinyint unsigned");
            b.HasKey("ResourceType", "ResourceId", "SourceVersion");
            b.HasIndex("ReceivedAtUtc").HasDatabaseName("IX_ResourceStateGap_Received");
            b.ToTable("ResourceStateGaps", (string?)null);
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.ViewObservation", delegate (EntityTypeBuilder b)
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            b.Property<long>("Id").UseMySqlIdentityColumn();
            b.Property<DateTimeOffset>("BucketStartUtc").HasColumnType("datetime(6)");
            b.Property<DateTimeOffset>("ExpiresAtUtc").HasColumnType("datetime(6)");
            b.Property<string>("ObserverHash").IsRequired().IsUnicode(unicode: false)
                .HasColumnType("char(64)")
                .UseCollation("ascii_bin");
            b.Property<DateTimeOffset?>("ProcessedAtUtc").HasColumnType("datetime(6)");
            b.Property<DateTimeOffset>("ReceivedAtUtc").HasColumnType("datetime(6)");
            b.Property<string>("ResourceId").IsRequired().HasMaxLength(128)
                .IsUnicode(unicode: false)
                .HasColumnType("varchar(128)")
                .UseCollation("utf8mb4_bin");
            b.Property<byte>("ResourceType").HasColumnType("tinyint unsigned");
            b.HasKey("Id");
            b.HasIndex("ExpiresAtUtc").HasDatabaseName("IX_ViewObservation_Expiry");
            b.HasIndex("ProcessedAtUtc", "Id").HasDatabaseName("IX_ViewObservation_Pending");
            b.HasIndex("ResourceType", "ResourceId", "ObserverHash", "BucketStartUtc").IsUnique().HasDatabaseName("UX_ViewObservation_Dedupe");
            b.ToTable("ViewObservations", (string?)null);
        });
        modelBuilder.Entity("MassTransit.EntityFrameworkCoreIntegration.InboxState", delegate (EntityTypeBuilder b)
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            b.Property<long>("Id").UseMySqlIdentityColumn();
            b.Property<DateTime?>("Consumed").HasColumnType("datetime(6)");
            b.Property<Guid>("ConsumerId").HasColumnType("char(36)");
            b.Property<DateTime?>("Delivered").HasColumnType("datetime(6)");
            b.Property<DateTime?>("ExpirationTime").HasColumnType("datetime(6)");
            b.Property<long?>("LastSequenceNumber").HasColumnType("bigint");
            b.Property<Guid>("LockId").HasColumnType("char(36)");
            b.Property<Guid>("MessageId").HasColumnType("char(36)");
            b.Property<int>("ReceiveCount").HasColumnType("int");
            b.Property<DateTime>("Received").HasColumnType("datetime(6)");
            b.Property<DateTime?>("RowVersion").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()
                .HasColumnType("timestamp(6)");
            b.HasKey("Id");
            b.HasIndex("Delivered");
            b.ToTable("InboxState");
        });
        modelBuilder.Entity("MassTransit.EntityFrameworkCoreIntegration.OutboxMessage", delegate (EntityTypeBuilder b)
        {
            b.Property<long>("SequenceNumber").ValueGeneratedOnAdd().HasColumnType("bigint");
            b.Property<long>("SequenceNumber").UseMySqlIdentityColumn();
            b.Property<string>("Body").IsRequired().HasColumnType("longtext");
            b.Property<string>("ContentType").IsRequired().HasMaxLength(256)
                .HasColumnType("varchar(256)");
            b.Property<Guid?>("ConversationId").HasColumnType("char(36)");
            b.Property<Guid?>("CorrelationId").HasColumnType("char(36)");
            b.Property<string>("DestinationAddress").HasMaxLength(256).HasColumnType("varchar(256)");
            b.Property<DateTime?>("EnqueueTime").HasColumnType("datetime(6)");
            b.Property<DateTime?>("ExpirationTime").HasColumnType("datetime(6)");
            b.Property<string>("FaultAddress").HasMaxLength(256).HasColumnType("varchar(256)");
            b.Property<string>("Headers").HasColumnType("longtext");
            b.Property<Guid?>("InboxConsumerId").HasColumnType("char(36)");
            b.Property<Guid?>("InboxMessageId").HasColumnType("char(36)");
            b.Property<Guid?>("InitiatorId").HasColumnType("char(36)");
            b.Property<Guid>("MessageId").HasColumnType("char(36)");
            b.Property<string>("MessageType").IsRequired().HasColumnType("longtext");
            b.Property<Guid?>("OutboxId").HasColumnType("char(36)");
            b.Property<string>("Properties").HasColumnType("longtext");
            b.Property<Guid?>("RequestId").HasColumnType("char(36)");
            b.Property<string>("ResponseAddress").HasMaxLength(256).HasColumnType("varchar(256)");
            b.Property<DateTime>("SentTime").HasColumnType("datetime(6)");
            b.Property<string>("SourceAddress").HasMaxLength(256).HasColumnType("varchar(256)");
            b.HasKey("SequenceNumber");
            b.HasIndex("EnqueueTime");
            b.HasIndex("ExpirationTime");
            b.HasIndex("OutboxId", "SequenceNumber").IsUnique();
            b.HasIndex("InboxMessageId", "InboxConsumerId", "SequenceNumber").IsUnique();
            b.ToTable("OutboxMessage");
        });
        modelBuilder.Entity("MassTransit.EntityFrameworkCoreIntegration.OutboxState", delegate (EntityTypeBuilder b)
        {
            b.Property<Guid>("OutboxId").ValueGeneratedOnAdd().HasColumnType("char(36)");
            b.Property<DateTime>("Created").HasColumnType("datetime(6)");
            b.Property<DateTime?>("Delivered").HasColumnType("datetime(6)");
            b.Property<long?>("LastSequenceNumber").HasColumnType("bigint");
            b.Property<Guid>("LockId").HasColumnType("char(36)");
            b.Property<DateTime?>("RowVersion").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()
                .HasColumnType("timestamp(6)");
            b.HasKey("OutboxId");
            b.HasIndex("Created");
            b.ToTable("OutboxState");
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.EngagementSummary", delegate (EntityTypeBuilder b)
        {
            b.HasOne("Interaction.Domain.AggregatesModel.ResourceState", null).WithOne().HasForeignKey("Interaction.Domain.AggregatesModel.EngagementSummary", "ResourceType", "ResourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.Rating", delegate (EntityTypeBuilder b)
        {
            b.HasOne("Interaction.Domain.AggregatesModel.ResourceState", null).WithMany().HasForeignKey("ResourceType", "ResourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.Reaction", delegate (EntityTypeBuilder b)
        {
            b.HasOne("Interaction.Domain.AggregatesModel.ResourceState", null).WithMany().HasForeignKey("ResourceType", "ResourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("Interaction.Domain.AggregatesModel.ResourceStateGap", delegate (EntityTypeBuilder b)
        {
            b.HasOne("Interaction.Domain.AggregatesModel.ResourceState", null).WithMany().HasForeignKey("ResourceType", "ResourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
        modelBuilder.Entity("MassTransit.EntityFrameworkCoreIntegration.OutboxMessage", delegate (EntityTypeBuilder b)
        {
            b.HasOne("MassTransit.EntityFrameworkCoreIntegration.OutboxState", null).WithMany().HasForeignKey("OutboxId");
            b.HasOne("MassTransit.EntityFrameworkCoreIntegration.InboxState", null).WithMany().HasForeignKey("InboxMessageId", "InboxConsumerId")
                .HasPrincipalKey("MessageId", "ConsumerId");
        });
    }
}
