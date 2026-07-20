using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class InitialModerationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MessageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConsumerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LockId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RowVersion = table.Column<DateTime>(type: "timestamp(6)", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceiveCount = table.Column<int>(type: "int", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Consumed = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Delivered = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModerationReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CaseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReporterId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResourceKind = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false),
                    SourceRequestId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationReports", x => x.Id);
                    table.CheckConstraint("CK_ModerationReports_IdempotencyHash", "`IdempotencyKeyHash` REGEXP '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ModerationReports_Reason", "`Reason` BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_ModerationReports_RequestHash", "`RequestHash` REGEXP '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ModerationReports_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LockId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RowVersion = table.Column<DateTime>(type: "timestamp(6)", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Delivered = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModerationCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResourceKind = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReporterId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    AssignedInvestigatorId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DecisionVersion = table.Column<long>(type: "bigint", nullable: false),
                    DecisionAction = table.Column<int>(type: "int", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false),
                    LastChangedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false),
                    SlaDueAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false),
                    EscalatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    LastAuditHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationCases", x => x.Id);
                    table.CheckConstraint("CK_ModerationCases_DecisionAction", "`DecisionAction` IS NULL OR `DecisionAction` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ModerationCases_Priority", "`Priority` BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_ModerationCases_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ModerationCases_Sla", "`SlaDueAtUtc` >= `ReceivedAtUtc`");
                    table.CheckConstraint("CK_ModerationCases_Status", "`Status` BETWEEN 1 AND 7");
                    table.CheckConstraint("CK_ModerationCases_Versions", "`Version` >= 1 AND `DecisionVersion` >= 0");
                    table.ForeignKey(
                        name: "FK_ModerationCases_ModerationReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "ModerationReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModerationEvidenceReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Sha256 = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationEvidenceReferences", x => x.Id);
                    table.CheckConstraint("CK_ModerationEvidenceReferences_Kind", "`Kind` BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_ModerationEvidenceReferences_Sha256", "`Sha256` REGEXP '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_ModerationEvidenceReferences_ModerationReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "ModerationReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EnqueueTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SentTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Headers = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Properties = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InboxMessageId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    InboxConsumerId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    OutboxId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    MessageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ContentType = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MessageType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConversationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CorrelationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    InitiatorId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SourceAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DestinationAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResponseAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FaultAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpirationTime = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModerationAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CaseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ActorId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReasonCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CaseVersion = table.Column<long>(type: "bigint", nullable: false),
                    PreviousHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationAuditEntries", x => x.Id);
                    table.CheckConstraint("CK_ModerationAuditEntries_Hash", "`Hash` REGEXP '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ModerationAuditEntries_PreviousHash", "`PreviousHash` = '' OR `PreviousHash` REGEXP '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ModerationAuditEntries_Version", "`CaseVersion` >= 1");
                    table.ForeignKey(
                        name: "FK_ModerationAuditEntries_ModerationCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "ModerationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModerationCaseMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CaseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SourceRequestId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ActorId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reference = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Sha256 = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CaseVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationCaseMessages", x => x.Id);
                    table.CheckConstraint("CK_ModerationCaseMessages_Hash", "`Sha256` REGEXP '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ModerationCaseMessages_Version", "`CaseVersion` >= 2");
                    table.ForeignKey(
                        name: "FK_ModerationCaseMessages_ModerationCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "ModerationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModerationLegalHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CaseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResourceKind = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlacedByActorId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReasonCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlacedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReleasedByActorId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReleaseReasonCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    ActiveCaseKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationLegalHolds", x => x.Id);
                    table.CheckConstraint("CK_ModerationLegalHolds_Release", "(`IsActive` = 1 AND `ReleasedAtUtc` IS NULL AND `ActiveCaseKey` IS NOT NULL) OR (`IsActive` = 0 AND `ReleasedAtUtc` IS NOT NULL AND `ActiveCaseKey` IS NULL)");
                    table.CheckConstraint("CK_ModerationLegalHolds_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_ModerationLegalHolds_ModerationCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "ModerationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationAuditEntries_CaseId_CaseVersion",
                table: "ModerationAuditEntries",
                columns: new[] { "CaseId", "CaseVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationAuditEntries_OccurredAtUtc",
                table: "ModerationAuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCaseMessages_CaseId_CaseVersion",
                table: "ModerationCaseMessages",
                columns: new[] { "CaseId", "CaseVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCaseMessages_CaseId_CreatedAtUtc_Id",
                table: "ModerationCaseMessages",
                columns: new[] { "CaseId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_ModerationCaseMessages_SourceRequest",
                table: "ModerationCaseMessages",
                column: "SourceRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCases_Cursor",
                table: "ModerationCases",
                columns: new[] { "ReceivedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCases_ReportId",
                table: "ModerationCases",
                column: "ReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCases_SlaQueue",
                table: "ModerationCases",
                columns: new[] { "Status", "EscalatedAtUtc", "SlaDueAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEvidenceReferences_ReportId_Kind",
                table: "ModerationEvidenceReferences",
                columns: new[] { "ReportId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationLegalHolds_CaseId_PlacedAtUtc",
                table: "ModerationLegalHolds",
                columns: new[] { "CaseId", "PlacedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ModerationLegalHolds_ActiveCase",
                table: "ModerationLegalHolds",
                column: "ActiveCaseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReports_CaseId",
                table: "ModerationReports",
                column: "CaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationReports_ResourceKind_ResourceId_ReceivedAtUtc",
                table: "ModerationReports",
                columns: new[] { "ResourceKind", "ResourceId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ModerationReports_Reporter_Idempotency",
                table: "ModerationReports",
                columns: new[] { "ReporterId", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ModerationReports_SourceRequest",
                table: "ModerationReports",
                column: "SourceRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationAuditEntries_RejectUpdate`
                BEFORE UPDATE ON `ModerationAuditEntries`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation audit entries are append-only';
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationAuditEntries_RejectDelete`
                BEFORE DELETE ON `ModerationAuditEntries`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation audit entries are append-only';
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationCaseMessages_RejectUpdate`
                BEFORE UPDATE ON `ModerationCaseMessages`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation case messages are append-only';
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationCaseMessages_RejectDelete`
                BEFORE DELETE ON `ModerationCaseMessages`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation case messages are append-only';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationAuditEntries_RejectUpdate`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationAuditEntries_RejectDelete`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationCaseMessages_RejectUpdate`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationCaseMessages_RejectDelete`;");

            migrationBuilder.DropTable(
                name: "ModerationAuditEntries");

            migrationBuilder.DropTable(
                name: "ModerationCaseMessages");

            migrationBuilder.DropTable(
                name: "ModerationEvidenceReferences");

            migrationBuilder.DropTable(
                name: "ModerationLegalHolds");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "ModerationCases");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "ModerationReports");
        }
    }
}
