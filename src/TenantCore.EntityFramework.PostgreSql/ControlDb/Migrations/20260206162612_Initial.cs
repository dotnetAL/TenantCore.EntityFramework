using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantCore.EntityFramework.PostgreSql.ControlDb.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenant_control");

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "tenant_control",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSlug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantSchema = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    TenantDatabase = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantDbServer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TenantDbUser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantDbPasswordEncrypted = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TenantApiKeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Status",
                schema: "tenant_control",
                table: "Tenants",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantApiKeyHash",
                schema: "tenant_control",
                table: "Tenants",
                column: "TenantApiKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantSchema",
                schema: "tenant_control",
                table: "Tenants",
                column: "TenantSchema",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantSlug",
                schema: "tenant_control",
                table: "Tenants",
                column: "TenantSlug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "tenant_control");
        }
    }
}
