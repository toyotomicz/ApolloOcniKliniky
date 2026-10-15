using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApolloData.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShowcaseFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityEvents_ClinicId",
                table: "ActivityEvents");

            migrationBuilder.AddColumn<DateTime>(
                name: "GeocodedAt",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Clinics",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Clinics",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "Administrators",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorEnabledAt",
                table: "Administrators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TwoFactorLastTimeStep",
                table: "Administrators",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorPendingSecret",
                table: "Administrators",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSecret",
                table: "Administrators",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Detail",
                table: "ActivityEvents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "AdminRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminRecoveryCodes_Administrators_AdministratorId",
                        column: x => x.AdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sender = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AdministratorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicMessages", x => x.Id);
                    table.CheckConstraint("CK_ClinicMessages_Author", "(\"Sender\" = 'Clinic' AND \"AdministratorId\" IS NULL) OR (\"Sender\" = 'Administrator' AND \"AdministratorId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ClinicMessages_Administrators_AdministratorId",
                        column: x => x.AdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicMessages_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboundEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ToName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TextBody = table.Column<string>(type: "text", nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundEmails_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_IsDemo",
                table: "Clinics",
                column: "IsDemo");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_ClinicId_OccurredAt",
                table: "ActivityEvents",
                columns: new[] { "ClinicId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminRecoveryCodes_AdministratorId",
                table: "AdminRecoveryCodes",
                column: "AdministratorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMessages_AdministratorId",
                table: "ClinicMessages",
                column: "AdministratorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMessages_ClinicId_CreatedAt",
                table: "ClinicMessages",
                columns: new[] { "ClinicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMessages_Sender_ReadAt",
                table: "ClinicMessages",
                columns: new[] { "Sender", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmails_ClinicId",
                table: "OutboundEmails",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmails_NextAttemptAt",
                table: "OutboundEmails",
                column: "NextAttemptAt",
                filter: "\"SentAt\" IS NULL AND \"FailedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminRecoveryCodes");

            migrationBuilder.DropTable(
                name: "ClinicMessages");

            migrationBuilder.DropTable(
                name: "OutboundEmails");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_IsDemo",
                table: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_ActivityEvents_ClinicId_OccurredAt",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "GeocodedAt",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "Administrators");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabledAt",
                table: "Administrators");

            migrationBuilder.DropColumn(
                name: "TwoFactorLastTimeStep",
                table: "Administrators");

            migrationBuilder.DropColumn(
                name: "TwoFactorPendingSecret",
                table: "Administrators");

            migrationBuilder.DropColumn(
                name: "TwoFactorSecret",
                table: "Administrators");

            migrationBuilder.AlterColumn<string>(
                name: "Detail",
                table: "ActivityEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_ClinicId",
                table: "ActivityEvents",
                column: "ClinicId");
        }
    }
}
