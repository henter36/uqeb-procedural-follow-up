using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Data;

public class AppDbContext : DbContext
{
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasOne(u => u.Department).WithMany(d => d.Users).HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Department>(e =>
        {
            e.HasIndex(d => d.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
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
    }
}
