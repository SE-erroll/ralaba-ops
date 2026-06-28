using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RaLaba.Domain.Entities;
using RaLaba.Domain.Enums;

namespace RaLaba.Infrastructure.Persistence;

public class RaLabaDbContext : DbContext
{
    public RaLabaDbContext(DbContextOptions<RaLabaDbContext> options) : base(options) { }

    public DbSet<Receipt> Receipts => Set<Receipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.ToTable("receipts");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasColumnName("id");
            entity.Property(r => r.ReceiptNo).HasColumnName("receipt_no");
            entity.Property(r => r.Date).HasColumnName("date").IsRequired();
            entity.Property(r => r.Employee).HasColumnName("employee");
            entity.Property(r => r.Customer).HasColumnName("customer");
            entity.Property(r => r.Type).HasColumnName("type")
                .HasConversion<string>().HasDefaultValue(Domain.Enums.TransactionType.Sale);
            entity.Property(r => r.Amount).HasColumnName("amount").HasColumnType("numeric(10,2)").IsRequired();
            entity.Property(r => r.PaymentMethod).HasColumnName("payment_method")
                .HasConversion(new ValueConverter<PaymentMethod, string>(
                    v => v == PaymentMethod.Cash ? "CASH" : "GCASH",
                    v => v == "CASH" ? PaymentMethod.Cash : PaymentMethod.GCash));
            entity.Property(r => r.Paid).HasColumnName("paid").HasDefaultValue(true);
            entity.Property(r => r.PaidDate).HasColumnName("paid_date");
            entity.Property(r => r.Notes).HasColumnName("notes");
            entity.Property(r => r.CreatedAt).HasColumnName("created_at");
        });
    }
}
