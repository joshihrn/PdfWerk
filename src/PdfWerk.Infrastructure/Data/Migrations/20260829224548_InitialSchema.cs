using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PdfWerk.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalCalls = table.Column<long>(type: "bigint", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IpBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Network = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    PrefixLength = table.Column<int>(type: "integer", nullable: false),
                    AddressFamily = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RateLimitOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PerMinute = table.Column<int>(type: "integer", nullable: false),
                    PerHour = table.Column<int>(type: "integer", nullable: false),
                    PerDay = table.Column<int>(type: "integer", nullable: false),
                    Concurrent = table.Column<int>(type: "integer", nullable: false),
                    MaxUploadBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxPages = table.Column<int>(type: "integer", nullable: false),
                    MaxBatch = table.Column<int>(type: "integer", nullable: false),
                    MaxCharacters = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ElapsedMs = table.Column<int>(type: "integer", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Blocked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Allowed = table.Column<bool>(type: "boolean", nullable: false),
                    BytesIn = table.Column<int>(type: "integer", nullable: false),
                    BytesOut = table.Column<int>(type: "integer", nullable: false),
                    ElapsedMs = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_SecretHash",
                table: "ApiKeys",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IpBlocks_Network_PrefixLength",
                table: "IpBlocks",
                columns: new[] { "Network", "PrefixLength" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitOverrides_Tier_Action",
                table: "RateLimitOverrides",
                columns: new[] { "Tier", "Action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestLog_Address_At",
                table: "RequestLog",
                columns: new[] { "Address", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLog_At",
                table: "RequestLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_Usage_At",
                table: "Usage",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_Usage_ClientId_At",
                table: "Usage",
                columns: new[] { "ClientId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "IpBlocks");

            migrationBuilder.DropTable(
                name: "RateLimitOverrides");

            migrationBuilder.DropTable(
                name: "RequestLog");

            migrationBuilder.DropTable(
                name: "Usage");
        }
    }
}
