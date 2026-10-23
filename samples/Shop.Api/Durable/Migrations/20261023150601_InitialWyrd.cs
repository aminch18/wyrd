using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shop.Api.Durable.Migrations
{
    /// <inheritdoc />
    public partial class InitialWyrd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wyrd");

            migrationBuilder.CreateTable(
                name: "dead_letters",
                schema: "wyrd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    envelope = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "wyrd",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.seq);
                });

            migrationBuilder.CreateTable(
                name: "handled",
                schema: "wyrd",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_handled", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "held",
                schema: "wyrd",
                columns: table => new
                {
                    state_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_held", x => new { x.state_type, x.key, x.version });
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "wyrd",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reply = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fence = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "instances",
                schema: "wyrd",
                columns: table => new
                {
                    process = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    data = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instances", x => new { x.process, x.key });
                });

            migrationBuilder.CreateTable(
                name: "parked",
                schema: "wyrd",
                columns: table => new
                {
                    process = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parked", x => new { x.process, x.key, x.step });
                });

            migrationBuilder.CreateTable(
                name: "projected",
                schema: "wyrd",
                columns: table => new
                {
                    state_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projected", x => new { x.state_type, x.key });
                });

            migrationBuilder.CreateTable(
                name: "published",
                schema: "wyrd",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    relayed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                    trace_state = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published", x => x.seq);
                });

            migrationBuilder.CreateTable(
                name: "queue",
                schema: "wyrd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    envelope = table.Column<string>(type: "text", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "states",
                schema: "wyrd",
                columns: table => new
                {
                    state_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_states", x => new { x.state_type, x.key });
                });

            migrationBuilder.CreateTable(
                name: "views",
                schema: "wyrd",
                columns: table => new
                {
                    view_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_views", x => new { x.view_type, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_recorded_at",
                schema: "wyrd",
                table: "events",
                column: "recorded_at");

            migrationBuilder.CreateIndex(
                name: "ix_handled_at",
                schema: "wyrd",
                table: "handled",
                column: "handled_at");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_recorded_at",
                schema: "wyrd",
                table: "inbox",
                column: "recorded_at");

            migrationBuilder.CreateIndex(
                name: "ix_instances_finished",
                schema: "wyrd",
                table: "instances",
                column: "finished_at",
                filter: "finished_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_published_message_id",
                schema: "wyrd",
                table: "published",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_published_pending",
                schema: "wyrd",
                table: "published",
                column: "seq",
                filter: "relayed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_published_relayed_at",
                schema: "wyrd",
                table: "published",
                column: "relayed_at");

            migrationBuilder.CreateIndex(
                name: "ix_queue_available",
                schema: "wyrd",
                table: "queue",
                columns: new[] { "available_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_queue_message_id",
                schema: "wyrd",
                table: "queue",
                column: "message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letters",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "events",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "handled",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "held",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "inbox",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "instances",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "parked",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "projected",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "published",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "queue",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "states",
                schema: "wyrd");

            migrationBuilder.DropTable(
                name: "views",
                schema: "wyrd");
        }
    }
}
