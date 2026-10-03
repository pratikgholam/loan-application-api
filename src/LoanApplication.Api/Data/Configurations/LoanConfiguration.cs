using LoanApplication.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanApplication.Api.Data.Configurations;

public class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Amount).HasPrecision(18, 2);
        builder.Property(l => l.Purpose).IsRequired().HasMaxLength(500);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(l => l.Status); // used by the filtered list endpoint

        builder.HasOne(l => l.Applicant)
            .WithMany(a => a.Loans)
            .HasForeignKey(l => l.ApplicantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.History)
            .WithOne(h => h.Loan)
            .HasForeignKey(h => h.LoanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(l => l.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}