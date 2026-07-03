using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Data;

public class AppDbContext : DbContext
{
    private const string SqliteRowVersionDefaultSql = "(randomblob(8))";
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ExternalParty> ExternalParties => Set<ExternalParty>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionOutgoingParty> TransactionOutgoingParties => Set<TransactionOutgoingParty>();
    public DbSet<TransactionOutgoingDepartment> TransactionOutgoingDepartments => Set<TransactionOutgoingDepartment>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<FollowUpRecipient> FollowUpRecipients => Set<FollowUpRecipient>();
    public DbSet<FollowUpDepartment> FollowUpDepartments => Set<FollowUpDepartment>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LetterTemplate> LetterTemplates => Set<LetterTemplate>();
    public DbSet<FollowUpPrintJob> FollowUpPrintJobs => Set<FollowUpPrintJob>();
    public DbSet<FollowUpPrintJobPart> FollowUpPrintJobParts => Set<FollowUpPrintJobPart>();
    public DbSet<FollowUpPrintJobPayload> FollowUpPrintJobPayloads => Set<FollowUpPrintJobPayload>();
    public DbSet<FollowUpLetterPrintRecord> FollowUpLetterPrintRecords => Set<FollowUpLetterPrintRecord>();
    public DbSet<FollowUpPrintIdempotencyKey> FollowUpPrintIdempotencyKeys => Set<FollowUpPrintIdempotencyKey>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<LoginAttemptLog> LoginAttemptLogs => Set<LoginAttemptLog>();
    public DbSet<SecurityAlert> SecurityAlerts => Set<SecurityAlert>();
    public DbSet<ReportExportTemplate> ReportExportTemplates => Set<ReportExportTemplate>();
    public DbSet<ReportNumberSequence> ReportNumberSequences => Set<ReportNumberSequence>();
    public DbSet<DepartmentResponse> DepartmentResponses => Set<DepartmentResponse>();
    public DbSet<DepartmentResponseAttachment> DepartmentResponseAttachments => Set<DepartmentResponseAttachment>();
    public DbSet<RecurringTransactionTemplate> RecurringTransactionTemplates => Set<RecurringTransactionTemplate>();
    public DbSet<RecurringTransactionTemplateDepartment> RecurringTransactionTemplateDepartments => Set<RecurringTransactionTemplateDepartment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
            e.HasOne(u => u.Department).WithMany(d => d.Users).HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Department>(e =>
        {
            e.HasIndex(d => d.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasIndex(d => d.NameNormalized).IsUnique();
        });

        modelBuilder.Entity<ExternalParty>(e =>
        {
            e.HasIndex(p => p.NameNormalized).IsUnique();
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
            e.HasIndex(c => c.NameNormalized).IsUnique();
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasIndex(t => t.IncomingNumber).IsUnique();
            e.HasIndex(t => t.InternalTrackingNumber).IsUnique();
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.IncomingDate);
            e.HasIndex(t => new { t.Status, t.IncomingDate, t.ClosedAt });
            e.HasIndex(t => new { t.RequiresResponse, t.ResponseCompleted, t.ResponseDueDate });
            e.HasIndex(t => new { t.CategoryId, t.IncomingDate });
            e.HasIndex(t => new { t.IncomingSourceType, t.IncomingDate });
            e.HasIndex(t => t.OutgoingDate);
            e.HasIndex(t => t.IsArchived);
            e.HasIndex(t => t.ResponseDueDate);
            e.HasIndex(t => t.CategoryId);
            e.HasIndex(t => t.ResponseCompleted);
            e.HasIndex(t => t.IncomingSourceType);
            e.HasIndex(t => t.CreatedAt);

            e.HasOne(t => t.CreatedBy).WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.UpdatedBy).WithMany().HasForeignKey(t => t.UpdatedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.IncomingFromParty).WithMany().HasForeignKey(t => t.IncomingFromPartyId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.IncomingFromDepartment).WithMany(d => d.IncomingTransactions).HasForeignKey(t => t.IncomingFromDepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.OutgoingToParty).WithMany().HasForeignKey(t => t.OutgoingToPartyId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.CategoryEntity).WithMany(c => c.Transactions).HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(t => new { t.RecurringTemplateId, t.RecurringPeriodKey })
                .IsUnique()
                .HasFilter("[RecurringTemplateId] IS NOT NULL");
            e.HasOne(t => t.RecurringTemplate).WithMany(r => r.GeneratedTransactions).HasForeignKey(t => t.RecurringTemplateId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TransactionOutgoingParty>(e =>
        {
            e.HasIndex(x => new { x.TransactionId, x.ExternalPartyId }).IsUnique();
            e.HasOne(x => x.Transaction).WithMany(t => t.OutgoingParties).HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ExternalParty).WithMany().HasForeignKey(x => x.ExternalPartyId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TransactionOutgoingDepartment>(e =>
        {
            e.HasIndex(x => new { x.TransactionId, x.DepartmentId }).IsUnique();
            e.HasIndex(x => new { x.DepartmentId, x.TransactionId });
            e.HasOne(x => x.Transaction).WithMany(t => t.OutgoingDepartments).HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Department).WithMany(d => d.OutgoingTransactions).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FollowUp>(e =>
        {
            e.HasIndex(f => new { f.TransactionId, f.CreatedAt });
            e.HasOne(f => f.Transaction).WithMany(t => t.FollowUps).HasForeignKey(f => f.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.CreatedBy).WithMany().HasForeignKey(f => f.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FollowUpRecipient>(e =>
        {
            e.HasIndex(x => new { x.FollowUpId, x.ExternalPartyId }).IsUnique();
            e.HasOne(x => x.FollowUp).WithMany(f => f.Recipients).HasForeignKey(x => x.FollowUpId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ExternalParty).WithMany().HasForeignKey(x => x.ExternalPartyId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FollowUpDepartment>(e =>
        {
            e.HasIndex(x => new { x.FollowUpId, x.DepartmentId }).IsUnique();
            e.HasOne(x => x.FollowUp).WithMany(f => f.Departments).HasForeignKey(x => x.FollowUpId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Department).WithMany(d => d.FollowUpDepartments).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasIndex(a => a.TransactionId);
            e.HasIndex(a => new { a.TransactionId, a.RequiresReply, a.ReplyStatus, a.Status });
            e.HasIndex(a => new { a.DepartmentId, a.Status, a.ReplyStatus });
            e.HasIndex(a => new { a.DepartmentId, a.Status, a.RequiresReply, a.ReplyStatus, a.DueDate });
            e.HasIndex(a => new { a.Status, a.RequiresReply, a.ReplyStatus, a.DueDate, a.DepartmentId });
            e.HasIndex(a => a.DueDate);
            e.HasOne(a => a.Transaction).WithMany(t => t.Assignments).HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Department).WithMany(d => d.Assignments).HasForeignKey(a => a.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.CreatedBy).WithMany().HasForeignKey(a => a.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Attachment>(e =>
        {
            e.HasIndex(a => a.TransactionId);
            e.HasOne(a => a.Transaction).WithMany(t => t.Attachments).HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasOne(a => a.Transaction).WithMany(t => t.AuditLogs).HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => new { a.TransactionId, a.CreatedAt });
        });

        modelBuilder.Entity<LetterTemplate>(e =>
        {
            e.HasIndex(t => t.Code).IsUnique();
            e.HasIndex(t => t.TemplateType)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");
            e.ToTable(t => t.HasCheckConstraint(
                "CK_LetterTemplates_DefaultRequiresActive",
                "[IsDefault] = 0 OR [IsActive] = 1"));
            e.Property(t => t.RowVersion).IsRowVersion();
            e.HasOne(t => t.CreatedBy).WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.UpdatedBy).WithMany().HasForeignKey(t => t.UpdatedById).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LoginAttemptLog>(e =>
        {
            e.Property(l => l.UserAgent).HasMaxLength(512);
            e.HasIndex(l => l.OccurredAt);
            e.HasIndex(l => new { l.Username, l.OccurredAt });
            e.HasIndex(l => new { l.IpAddress, l.OccurredAt });
            e.HasIndex(l => new { l.Succeeded, l.OccurredAt });
        });

        modelBuilder.Entity<SecurityAlert>(e =>
        {
            e.Property(a => a.UserAgent).HasMaxLength(512);
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => new { a.IsRead, a.CreatedAt });
            e.HasIndex(a => new { a.Type, a.CreatedAt });
            e.HasIndex(a => new { a.Severity, a.CreatedAt });
        });

        modelBuilder.Entity<ReportExportTemplate>(e =>
        {
            e.HasIndex(t => t.CreatedById);
            e.HasOne(t => t.CreatedBy).WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ReportNumberSequence>(e =>
        {
            e.HasKey(s => s.Year);

            e.Property(s => s.Year)
                .ValueGeneratedNever();
        });

        modelBuilder.Entity<FollowUpPrintJob>(e =>
        {
            e.HasIndex(j => new { j.Status, j.CreatedAt });
            e.HasIndex(j => j.ScopeDepartmentId);
            e.Property(j => j.RowVersion).IsRowVersion();
            e.HasOne(j => j.RequestedBy).WithMany().HasForeignKey(j => j.RequestedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(j => j.Template).WithMany().HasForeignKey(j => j.TemplateId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FollowUpPrintJobPayload>(e =>
        {
            e.HasIndex(p => new { p.JobId, p.PayloadOrdinal }).IsUnique();
            // Two separate filtered unique indexes — one per target shape.
            // XOR check constraint guarantees exactly one is non-null, so these together cover all valid rows.
            e.HasIndex(p => new { p.JobId, p.TransactionId, p.TargetDepartmentId, p.FollowUpSequence })
                .IsUnique()
                .HasFilter("[TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL")
                .HasDatabaseName("IX_FollowUpPrintJobPayloads_JobId_Tx_Dept_Seq");
            e.HasIndex(p => new { p.JobId, p.TransactionId, p.TargetEntityId, p.FollowUpSequence })
                .IsUnique()
                .HasFilter("[TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL")
                .HasDatabaseName("IX_FollowUpPrintJobPayloads_JobId_Tx_Entity_Seq");
            e.ToTable(t => t.HasCheckConstraint(
                "CK_FollowUpPrintJobPayloads_TargetShape",
                "([TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL) OR ([TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL)"));
            e.Property(p => p.RowVersion).IsRowVersion();
            e.HasOne(p => p.Job).WithMany(j => j.Payloads).HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Part).WithMany(p => p.Payloads).HasForeignKey(p => p.PartId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FollowUpPrintJobPart>(e =>
        {
            e.HasIndex(p => new { p.JobId, p.PartNumber }).IsUnique();
            e.Property(p => p.RowVersion).IsRowVersion();
            e.HasOne(p => p.Job).WithMany(j => j.Parts).HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FollowUpLetterPrintRecord>(e =>
        {
            e.HasIndex(r => new { r.TransactionId, r.PrintRequestedAt });
            // Two separate filtered unique indexes per target shape for batch records.
            // Direct print records (BatchJobPartId IS NULL) are protected by idempotency keys.
            e.HasIndex(r => new { r.BatchJobPartId, r.TransactionId, r.TargetDepartmentId, r.FollowUpSequence })
                .IsUnique()
                .HasFilter("[BatchJobPartId] IS NOT NULL AND [TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL")
                .HasDatabaseName("IX_FollowUpLetterPrintRecords_Part_Tx_Dept_Seq");
            e.HasIndex(r => new { r.BatchJobPartId, r.TransactionId, r.TargetEntityId, r.FollowUpSequence })
                .IsUnique()
                .HasFilter("[BatchJobPartId] IS NOT NULL AND [TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL")
                .HasDatabaseName("IX_FollowUpLetterPrintRecords_Part_Tx_Entity_Seq");
            e.ToTable(t => t.HasCheckConstraint(
                "CK_FollowUpLetterPrintRecords_TargetShape",
                "NOT ([TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NOT NULL)"));
            e.HasIndex(r => r.RegisteredFollowUpId)
                .IsUnique()
                .HasFilter("[RegisteredFollowUpId] IS NOT NULL")
                .HasDatabaseName("IX_FollowUpLetterPrintRecords_RegisteredFollowUpId_Linked");
            e.Property(r => r.RowVersion).IsRowVersion();
            e.HasOne(r => r.Transaction).WithMany().HasForeignKey(r => r.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.TargetDepartment).WithMany().HasForeignKey(r => r.TargetDepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.TargetEntity).WithMany().HasForeignKey(r => r.TargetEntityId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.Template).WithMany().HasForeignKey(r => r.TemplateId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.PrintRequestedBy).WithMany().HasForeignKey(r => r.PrintRequestedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.PrintConfirmedBy).WithMany().HasForeignKey(r => r.PrintConfirmedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.RegisteredFollowUp).WithMany().HasForeignKey(r => r.RegisteredFollowUpId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.CancelledBy).WithMany().HasForeignKey(r => r.CancelledById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.BatchJob).WithMany(j => j.PrintRecords).HasForeignKey(r => r.BatchJobId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.BatchJobPart).WithMany().HasForeignKey(r => r.BatchJobPartId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.ReprintOf).WithMany().HasForeignKey(r => r.ReprintOfId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FollowUpPrintIdempotencyKey>(e =>
        {
            e.Property(k => k.Key)
                .HasMaxLength(128);
            e.Property(k => k.Operation)
                .HasMaxLength(64);
            e.Property(k => k.RequestHash)
                .HasMaxLength(64)
                .IsUnicode(false);
            e.HasIndex(k => new { k.UserId, k.Operation, k.Key }).IsUnique();
            e.HasOne(k => k.User).WithMany().HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserNotification>(e =>
        {
            e.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DepartmentResponse>(e =>
        {
            e.HasIndex(r => new { r.TransactionId, r.DepartmentId }).IsUnique();
            e.HasIndex(r => r.DepartmentId);
            e.HasIndex(r => new { r.Status, r.CreatedAt });
            e.Property(r => r.RowVersion).IsRowVersion();
            e.HasOne(r => r.Transaction).WithMany().HasForeignKey(r => r.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Department).WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.SubmittedBy).WithMany().HasForeignKey(r => r.SubmittedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.ReviewedBy).WithMany().HasForeignKey(r => r.ReviewedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DepartmentResponseAttachment>(e =>
        {
            e.HasIndex(a => a.DepartmentResponseId);
            e.HasIndex(a => new { a.DepartmentResponseId, a.IsDeleted });
            e.Property(a => a.Sha256).IsRequired().HasMaxLength(64).IsUnicode(false);
            e.HasOne(a => a.DepartmentResponse).WithMany(r => r.Attachments).HasForeignKey(a => a.DepartmentResponseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.DeletedBy).WithMany().HasForeignKey(a => a.DeletedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecurringTransactionTemplate>(e =>
        {
            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.RecurrenceType);
            e.HasOne(r => r.CreatedBy).WithMany().HasForeignKey(r => r.CreatedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.PausedBy).WithMany().HasForeignKey(r => r.PausedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.ResumedBy).WithMany().HasForeignKey(r => r.ResumedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.TerminatedBy).WithMany().HasForeignKey(r => r.TerminatedById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.IncomingFromParty).WithMany().HasForeignKey(r => r.IncomingFromPartyId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.IncomingFromDepartment).WithMany().HasForeignKey(r => r.IncomingFromDepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.CategoryEntity).WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecurringTransactionTemplateDepartment>(e =>
        {
            e.HasIndex(x => new { x.TemplateId, x.DepartmentId }).IsUnique();
            e.HasOne(x => x.Template).WithMany(r => r.Departments).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        });

        if (Database.ProviderName == SqliteProviderName)
        {
            // SQLite does not auto-generate ROWVERSION values. Provide a column default so that
            // EnsureCreated-based test fixtures can insert rows without explicitly supplying RowVersion.
            // This block is unreachable in production (SQL Server).
            modelBuilder.Entity<LetterTemplate>()
                .Property(t => t.RowVersion).HasDefaultValueSql(SqliteRowVersionDefaultSql);
            modelBuilder.Entity<FollowUpPrintJob>()
                .Property(j => j.RowVersion).HasDefaultValueSql(SqliteRowVersionDefaultSql);
            modelBuilder.Entity<FollowUpPrintJobPart>()
                .Property(p => p.RowVersion).HasDefaultValueSql(SqliteRowVersionDefaultSql);
            modelBuilder.Entity<FollowUpPrintJobPayload>()
                .Property(p => p.RowVersion).HasDefaultValueSql(SqliteRowVersionDefaultSql);
            modelBuilder.Entity<FollowUpLetterPrintRecord>()
                .Property(r => r.RowVersion).HasDefaultValueSql(SqliteRowVersionDefaultSql);
            modelBuilder.Entity<DepartmentResponse>()
                .Property(r => r.RowVersion).HasDefaultValueSql(SqliteRowVersionDefaultSql);
        }
    }
}
