using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corvees.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialProjectModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.id);
                    table.UniqueConstraint("AK_locations_group_id_id", x => new { x.group_id, x.id });
                    table.ForeignKey(
                        name: "FK_locations_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_members", x => x.id);
                    table.UniqueConstraint("AK_members_group_id_id", x => new { x.group_id, x.id });
                    table.ForeignKey(
                        name: "FK_members_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    step_list_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                    table.UniqueConstraint("AK_projects_group_id_id", x => new { x.group_id, x.id });
                    table.ForeignKey(
                        name: "FK_projects_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_locations_group_id_location_id",
                        columns: x => new { x.group_id, x.location_id },
                        principalTable: "locations",
                        principalColumns: new[] { "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_dependencies",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prerequisite_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_dependencies", x => new { x.group_id, x.project_id, x.prerequisite_id });
                    table.CheckConstraint("ck_project_dependency_self", "project_id <> prerequisite_id");
                    table.ForeignKey(
                        name: "FK_project_dependencies_projects_group_id_prerequisite_id",
                        columns: x => new { x.group_id, x.prerequisite_id },
                        principalTable: "projects",
                        principalColumns: new[] { "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_dependencies_projects_group_id_project_id",
                        columns: x => new { x.group_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steps", x => x.id);
                    table.UniqueConstraint("AK_steps_group_id_project_id_id", x => new { x.group_id, x.project_id, x.id });
                    table.CheckConstraint("ck_steps_status", "status IN ('todo', 'in_progress', 'done')");
                    table.ForeignKey(
                        name: "FK_steps_projects_group_id_project_id",
                        columns: x => new { x.group_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("ALTER TABLE steps ADD CONSTRAINT uq_steps_project_position UNIQUE (project_id, position) DEFERRABLE INITIALLY IMMEDIATE");

            migrationBuilder.CreateTable(
                name: "step_dependencies",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prerequisite_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_step_dependencies", x => new { x.group_id, x.project_id, x.step_id, x.prerequisite_id });
                    table.CheckConstraint("ck_step_dependency_self", "step_id <> prerequisite_id");
                    table.ForeignKey(
                        name: "FK_step_dependencies_steps_group_id_project_id_prerequisite_id",
                        columns: x => new { x.group_id, x.project_id, x.prerequisite_id },
                        principalTable: "steps",
                        principalColumns: new[] { "group_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_step_dependencies_steps_group_id_project_id_step_id",
                        columns: x => new { x.group_id, x.project_id, x.step_id },
                        principalTable: "steps",
                        principalColumns: new[] { "group_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_members_token_hash",
                table: "members",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_dependencies_group_id_prerequisite_id",
                table: "project_dependencies",
                columns: new[] { "group_id", "prerequisite_id" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_group_id_location_id",
                table: "projects",
                columns: new[] { "group_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "IX_step_dependencies_group_id_project_id_prerequisite_id",
                table: "step_dependencies",
                columns: new[] { "group_id", "project_id", "prerequisite_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "members");

            migrationBuilder.DropTable(
                name: "project_dependencies");

            migrationBuilder.DropTable(
                name: "step_dependencies");

            migrationBuilder.DropTable(
                name: "steps");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "groups");
        }
    }
}
