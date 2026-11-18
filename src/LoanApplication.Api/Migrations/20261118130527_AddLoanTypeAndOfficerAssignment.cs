using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoanApplication.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanTypeAndOfficerAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Expertise",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedOfficerUserId",
                table: "Loans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoanType",
                table: "Loans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Personal");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_AssignedOfficerUserId",
                table: "Loans",
                column: "AssignedOfficerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Users_AssignedOfficerUserId",
                table: "Loans",
                column: "AssignedOfficerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Users_AssignedOfficerUserId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Loans_AssignedOfficerUserId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "Expertise",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AssignedOfficerUserId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "LoanType",
                table: "Loans");
        }
    }
}
