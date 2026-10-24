using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wyrd.EntityFrameworkCore;

// Wyrd's tables. Payloads are text (not jsonb): jsonb reorders keys, and polymorphic JSON needs its $type first.
// Columns are named explicitly so the same model, and the one raw query (the dequeue), work on every provider.

internal sealed class StateRow
{
    public string StateType { get; set; } = "";

    public string Key { get; set; } = "";

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";

    public long Version { get; set; }
}

internal sealed class InstanceRow
{
    public string Process { get; set; } = "";

    public string Key { get; set; } = "";

    public string Status { get; set; } = "";

    public DateTimeOffset? Deadline { get; set; }

    /// <summary>The whole instance as JSON: position, facts, fingerprint, history.</summary>
    public string Data { get; set; } = "";

    public long Version { get; set; }

    /// <summary>When it completed or was compensated; null while it runs, waits or is suspended.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>When the step it waits on was first sent; null unless it is running with a command out.</summary>
    public DateTimeOffset? PendingSince { get; set; }
}

internal sealed class EventRow
{
    public long Seq { get; set; }

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";

    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>An integration event this service published: the outbox to other services, until it is relayed.</summary>
internal sealed class PublishedRow
{
    public long Seq { get; set; }

    /// <summary>The id it keeps end to end, so a receiving service handles a redelivery once.</summary>
    public Guid MessageId { get; set; }

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";

    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>When the relay handed it to the transport; null while pending.</summary>
    public DateTimeOffset? RelayedAt { get; set; }

    public string? TraceParent { get; set; }

    public string? TraceState { get; set; }
}

internal sealed class InboxRow
{
    public Guid MessageId { get; set; }

    public string Reply { get; set; } = "";

    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>A fence: it blocks its command forever, so retention never removes it.</summary>
    public bool Fence { get; set; }
}

internal sealed class HandledRow
{
    public Guid MessageId { get; set; }

    public DateTimeOffset HandledAt { get; set; }
}

internal sealed class ParkedRow
{
    public string Process { get; set; } = "";

    public string Key { get; set; } = "";

    public int Step { get; set; }

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";
}

internal sealed class ViewRow
{
    public string ViewType { get; set; } = "";

    public string Key { get; set; } = "";

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";

    /// <summary>When the view was first written: views are listed in creation order (then by key).</summary>
    public DateTimeOffset Created { get; set; }

    public long Version { get; set; }
}

internal sealed class ProjectedRow
{
    public string StateType { get; set; } = "";

    public string Key { get; set; } = "";

    public long Version { get; set; }
}

internal sealed class HeldRow
{
    public string StateType { get; set; } = "";

    public string Key { get; set; } = "";

    public long Version { get; set; }

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";
}

internal sealed class QueueRow
{
    public long Id { get; set; }

    /// <summary>The envelope's id, so a scheduled message can be cancelled (a wait's timer).</summary>
    public Guid MessageId { get; set; }

    public string Envelope { get; set; } = "";

    public DateTimeOffset AvailableAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

internal sealed class DeadLetterRow
{
    public long Id { get; set; }

    public string Envelope { get; set; } = "";

    public int Attempts { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset FailedAt { get; set; }
}

/// <summary>Puts Wyrd's tables in a model: your own <see cref="DbContext"/>, or <see cref="WyrdDbContext"/>.</summary>
public static class WyrdModelBuilderExtensions
{
    /// <summary>
    /// Adds Wyrd's tables to this model, in <paramref name="schema"/>. Use it in your own <c>OnModelCreating</c> to keep
    /// Wyrd in your database and your migrations, and — when you hand your context to Wyrd — in your transaction.
    /// </summary>
    public static ModelBuilder AddWyrd(this ModelBuilder modelBuilder, string schema = "wyrd")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        modelBuilder.Entity<StateRow>(e =>
        {
            e.ToTable("states", schema).HasKey(r => new { r.StateType, r.Key });
            Key(e.Property(r => r.StateType), "state_type");
            Key(e.Property(r => r.Key), "key");
            Text(e.Property(r => r.Type), "type");
            Text(e.Property(r => r.Payload), "payload");
            e.Property(r => r.Version).HasColumnName("version");
        });

        modelBuilder.Entity<InstanceRow>(e =>
        {
            e.ToTable("instances", schema).HasKey(r => new { r.Process, r.Key });
            Key(e.Property(r => r.Process), "process");
            Key(e.Property(r => r.Key), "key");
            Key(e.Property(r => r.Status), "status");
            e.Property(r => r.Deadline).HasColumnName("deadline");
            Text(e.Property(r => r.Data), "data");
            e.Property(r => r.Version).HasColumnName("version");
            e.Property(r => r.FinishedAt).HasColumnName("finished_at");
            // Only finished instances: what retention looks for, as small as what it can remove.
            e.HasIndex(r => r.FinishedAt).HasFilter("finished_at IS NOT NULL").HasDatabaseName("ix_instances_finished");
            e.Property(r => r.PendingSince).HasColumnName("pending_since");
            // What the queue's health reads: pending steps and suspended instances, each index as small as what it finds.
            e.HasIndex(r => r.PendingSince).HasFilter("pending_since IS NOT NULL").HasDatabaseName("ix_instances_pending");
            e.HasIndex(r => r.Status).HasFilter("status = 'Suspended'").HasDatabaseName("ix_instances_suspended");
        });

        modelBuilder.Entity<EventRow>(e =>
        {
            e.ToTable("events", schema).HasKey(r => r.Seq);
            e.Property(r => r.Seq).HasColumnName("seq").ValueGeneratedOnAdd();
            Text(e.Property(r => r.Type), "type");
            Text(e.Property(r => r.Payload), "payload");
            e.Property(r => r.RecordedAt).HasColumnName("recorded_at");
            e.HasIndex(r => r.RecordedAt).HasDatabaseName("ix_events_recorded_at");
        });

        modelBuilder.Entity<PublishedRow>(e =>
        {
            e.ToTable("published", schema).HasKey(r => r.Seq);
            e.Property(r => r.Seq).HasColumnName("seq").ValueGeneratedOnAdd();
            e.Property(r => r.MessageId).HasColumnName("message_id");
            Text(e.Property(r => r.Type), "type");
            Text(e.Property(r => r.Payload), "payload");
            e.Property(r => r.RecordedAt).HasColumnName("recorded_at");
            e.Property(r => r.RelayedAt).HasColumnName("relayed_at");
            e.Property(r => r.TraceParent).HasColumnName("trace_parent").HasMaxLength(55);
            e.Property(r => r.TraceState).HasColumnName("trace_state");
            e.HasIndex(r => r.MessageId).IsUnique().HasDatabaseName("ix_published_message_id");
            e.HasIndex(r => r.RelayedAt).HasDatabaseName("ix_published_relayed_at");
            // Without a transport a published message expires from when it was published.
            e.HasIndex(r => r.RecordedAt).HasDatabaseName("ix_published_recorded_at");
            // Only what is waiting for the relay: the index stays as small as the backlog.
            e.HasIndex(r => r.Seq).HasFilter("relayed_at IS NULL").HasDatabaseName("ix_published_pending");
        });

        modelBuilder.Entity<InboxRow>(e =>
        {
            e.ToTable("inbox", schema).HasKey(r => r.MessageId);
            e.Property(r => r.MessageId).HasColumnName("message_id").ValueGeneratedNever();
            Text(e.Property(r => r.Reply), "reply");
            e.Property(r => r.RecordedAt).HasColumnName("recorded_at");
            e.Property(r => r.Fence).HasColumnName("fence");
            e.HasIndex(r => r.RecordedAt).HasDatabaseName("ix_inbox_recorded_at");
        });

        modelBuilder.Entity<HandledRow>(e =>
        {
            e.ToTable("handled", schema).HasKey(r => r.MessageId);
            e.Property(r => r.MessageId).HasColumnName("message_id").ValueGeneratedNever();
            e.Property(r => r.HandledAt).HasColumnName("handled_at");
            e.HasIndex(r => r.HandledAt).HasDatabaseName("ix_handled_at");
        });

        modelBuilder.Entity<ParkedRow>(e =>
        {
            e.ToTable("parked", schema).HasKey(r => new { r.Process, r.Key, r.Step });
            Key(e.Property(r => r.Process), "process");
            Key(e.Property(r => r.Key), "key");
            e.Property(r => r.Step).HasColumnName("step").ValueGeneratedNever();
            Text(e.Property(r => r.Type), "type");
            Text(e.Property(r => r.Payload), "payload");
        });

        modelBuilder.Entity<ViewRow>(e =>
        {
            e.ToTable("views", schema).HasKey(r => new { r.ViewType, r.Key });
            Key(e.Property(r => r.ViewType), "view_type");
            Key(e.Property(r => r.Key), "key");
            Text(e.Property(r => r.Type), "type");
            Text(e.Property(r => r.Payload), "payload");
            e.Property(r => r.Created).HasColumnName("created");
            e.Property(r => r.Version).HasColumnName("version");
        });

        modelBuilder.Entity<ProjectedRow>(e =>
        {
            e.ToTable("projected", schema).HasKey(r => new { r.StateType, r.Key });
            Key(e.Property(r => r.StateType), "state_type");
            Key(e.Property(r => r.Key), "key");
            e.Property(r => r.Version).HasColumnName("version");
        });

        modelBuilder.Entity<HeldRow>(e =>
        {
            e.ToTable("held", schema).HasKey(r => new { r.StateType, r.Key, r.Version });
            Key(e.Property(r => r.StateType), "state_type");
            Key(e.Property(r => r.Key), "key");
            e.Property(r => r.Version).HasColumnName("version").ValueGeneratedNever();
            Text(e.Property(r => r.Type), "type");
            Text(e.Property(r => r.Payload), "payload");
        });

        modelBuilder.Entity<QueueRow>(e =>
        {
            e.ToTable("queue", schema).HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(r => r.MessageId).HasColumnName("message_id");
            Text(e.Property(r => r.Envelope), "envelope");
            e.Property(r => r.AvailableAt).HasColumnName("available_at");
            e.Property(r => r.Attempts).HasColumnName("attempts");
            e.Property(r => r.LastError).HasColumnName("last_error");
            e.HasIndex(r => new { r.AvailableAt, r.Id }).HasDatabaseName("ix_queue_available");
            e.HasIndex(r => r.MessageId).HasDatabaseName("ix_queue_message_id");
        });

        modelBuilder.Entity<DeadLetterRow>(e =>
        {
            e.ToTable("dead_letters", schema).HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            Text(e.Property(r => r.Envelope), "envelope");
            e.Property(r => r.Attempts).HasColumnName("attempts");
            e.Property(r => r.Error).HasColumnName("error");
            e.Property(r => r.FailedAt).HasColumnName("failed_at");
        });

        return modelBuilder;
    }

    // Key columns are indexed, so they need a bounded length on SQL Server (its index keys are limited to 900 bytes).
    private static void Key(PropertyBuilder<string> property, string name) =>
        property.HasColumnName(name).HasMaxLength(200).IsRequired();

    private static void Text(PropertyBuilder<string> property, string name) =>
        property.HasColumnName(name).IsRequired();
}

/// <summary>A ready context holding only Wyrd's tables, for applications without a <see cref="DbContext"/> of their own.</summary>
public class WyrdDbContext(DbContextOptions<WyrdDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddWyrd();
}
