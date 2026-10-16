using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApolloData.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorityConfirmedAt",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientInfo",
                table: "Clinics",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "PublicEmails",
                table: "Clinics",
                type: "text[]",
                nullable: false,
                // Existing clinics start without public contacts.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "PublicPhones",
                table: "Clinics",
                type: "text[]",
                nullable: false,
                // Existing clinics start without public contacts.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Clinics",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RegistrationEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    InvalidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationEmailVerifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailVerifications_NormalizedEmail_CreatedAt",
                table: "RegistrationEmailVerifications",
                columns: new[] { "NormalizedEmail", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationEmailVerifications");

            migrationBuilder.DropColumn(
                name: "AuthorityConfirmedAt",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "PatientInfo",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "PublicEmails",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "PublicPhones",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Clinics");
        }
    }
}
