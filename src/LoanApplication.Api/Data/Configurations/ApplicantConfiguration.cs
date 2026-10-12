using LoanApplication.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanApplication.Api.Data.Configurations;

public class ApplicantConfiguration : IEntityTypeConfiguration<Applicant>
{
    public void Configure(EntityTypeBuilder<Applicant> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.FullName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.MonthlyIncome).HasPrecision(18, 2);

        // 1:0..1 with User, enforced by the unique index
        builder.HasOne(a => a.User)
            .WithOne()
            .HasForeignKey<Applicant>(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.UserId).IsUnique();

        builder.Navigation(a => a.Loans).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}