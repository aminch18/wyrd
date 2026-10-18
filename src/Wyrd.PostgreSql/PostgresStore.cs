using System.Text.Json;
using Npgsql;
using Wyrd.Runtime;

namespace Wyrd.PostgreSql;

/// <summary>
/// The store for one message, bound to its transaction: everything the engine reads and commits happens inside it,
/// together with taking the message off the queue. Versions give optimistic concurrency between workers.
/// </summary>
internal sealed class PostgresStore(NpgsqlConnection connection, NpgsqlTransaction transaction, Sql sql, DateTimeOffset now) : IWyrdStore
{
    private readonly string _s = sql.S;

    public async ValueTask<StoredState?> LoadStateAsync(string stateType, string key, CancellationToken cancellationToken)
    {
        await using var command = Command($"SELECT type, payload, version FROM {_s}.states WHERE state_type = @t AND key = @k");
        command.Parameters.AddWithValue("t", stateType);
        command.Parameters.AddWithValue("k", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredState(stateType, key, reader.GetString(0), reader.GetString(1), reader.GetInt64(2))
            : null;
    }

    public async ValueTask<StoredInstance?> LoadInstanceAsync(string process, string key, CancellationToken cancellationToken)
    {
        await using var command = Command($"SELECT data, version FROM {_s}.instances WHERE process = @p AND key = @k");
        command.Parameters.AddWithValue("p", process);
        command.Parameters.AddWithValue("k", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var instance = JsonSerializer.Deserialize(reader.GetString(0), WyrdJsonContext.Default.StoredInstance)!;
        return instance with { Version = reader.GetInt64(1) };
    }

    public async ValueTask<ReplyMessage?> FindReplyAsync(Guid commandId, CancellationToken cancellationToken)
    {
        await using var command = Command($"SELECT reply FROM {_s}.inbox WHERE message_id = @id");
        command.Parameters.AddWithValue("id", commandId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string reply
            ? MessageTypes.DeserializeReply(reply)
            : null;
    }

    public async ValueTask<StoredParked?> FindParkedAsync(string process, string key, int step, CancellationToken cancellationToken)
    {
        await using var command = Command($"SELECT type, payload FROM {_s}.parked WHERE process = @p AND key = @k AND step = @s");
        command.Parameters.AddWithValue("p", process);
        command.Parameters.AddWithValue("k", key);
        command.Parameters.AddWithValue("s", step);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredParked(process, key, step, reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async ValueTask<StoredView?> LoadViewAsync(string viewType, string key, CancellationToken cancellationToken)
    {
        await using var command = Command($"SELECT type, payload FROM {_s}.views WHERE view_type = @v AND key = @k");
        command.Parameters.AddWithValue("v", viewType);
        command.Parameters.AddWithValue("k", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredView(viewType, key, reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async ValueTask<bool> WasHandledAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var command = Command($"SELECT EXISTS (SELECT 1 FROM {_s}.handled WHERE message_id = @id)");
        command.Parameters.AddWithValue("id", messageId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    public async ValueTask CommitAsync(Changes changes, CancellationToken cancellationToken)
    {
        foreach (var state in changes.States)
        {
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        foreach (var instance in changes.Instances.Values)
        {
            await SaveInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
        }

        // Unique keys do the rest: a second writer of the same inbox entry, fence or handled message fails the
        // transaction, and the message is retried against what the first writer committed.
        foreach (var (id, reply) in changes.Replies)
        {
            await ExecuteAsync($"INSERT INTO {_s}.inbox (message_id, reply, recorded_at) VALUES (@id, @r, @now)", cancellationToken,
                ("id", id), ("r", MessageTypes.SerializeReply(reply)), ("now", now)).ConfigureAwait(false);
        }

        foreach (var id in changes.Handled)
        {
            await ExecuteAsync($"INSERT INTO {_s}.handled (message_id) VALUES (@id)", cancellationToken, ("id", id)).ConfigureAwait(false);
        }

        foreach (var e in changes.Events)
        {
            await ExecuteAsync($"INSERT INTO {_s}.events (type, payload, recorded_at) VALUES (@t, @p, @now)", cancellationToken,
                ("t", e.Type), ("p", e.Payload), ("now", now)).ConfigureAwait(false);
        }

        foreach (var e in changes.Published)
        {
            await ExecuteAsync($"INSERT INTO {_s}.published (type, payload, recorded_at) VALUES (@t, @p, @now)", cancellationToken,
                ("t", e.Type), ("p", e.Payload), ("now", now)).ConfigureAwait(false);
        }

        foreach (var parked in changes.Park)
        {
            await ExecuteAsync(
                $"INSERT INTO {_s}.parked (process, key, step, type, payload) VALUES (@p, @k, @s, @t, @d) ON CONFLICT DO NOTHING",
                cancellationToken,
                ("p", parked.Process), ("k", parked.Key), ("s", parked.Step), ("t", parked.Type), ("d", parked.Payload)).ConfigureAwait(false);
        }

        foreach (var parked in changes.Unpark)
        {
            await ExecuteAsync($"DELETE FROM {_s}.parked WHERE process = @p AND key = @k AND step = @s", cancellationToken,
                ("p", parked.Process), ("k", parked.Key), ("s", parked.Step)).ConfigureAwait(false);
        }

        foreach (var view in changes.Views.Values)
        {
            if (view.Payload is null)
            {
                await ExecuteAsync($"DELETE FROM {_s}.views WHERE view_type = @v AND key = @k", cancellationToken,
                    ("v", view.ViewType), ("k", view.Key)).ConfigureAwait(false);
            }
            else
            {
                await ExecuteAsync(
                    $"INSERT INTO {_s}.views (view_type, key, type, payload) VALUES (@v, @k, @t, @p) ON CONFLICT (view_type, key) DO UPDATE SET type = EXCLUDED.type, payload = EXCLUDED.payload",
                    cancellationToken,
                    ("v", view.ViewType), ("k", view.Key), ("t", view.Type!), ("p", view.Payload)).ConfigureAwait(false);
            }
        }

        // The outbox and the timers are the queue: committed with everything else, delivered by the workers.
        foreach (var envelope in changes.Outbox)
        {
            await EnqueueAsync(envelope, now, cancellationToken).ConfigureAwait(false);
        }

        foreach (var scheduled in changes.Schedule)
        {
            await EnqueueAsync(scheduled.Envelope, scheduled.At, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask EnqueueAsync(Envelope envelope, DateTimeOffset availableAt, CancellationToken cancellationToken) =>
        await ExecuteAsync($"INSERT INTO {_s}.queue (envelope, available_at) VALUES (@e, @at)", cancellationToken,
            ("e", JsonSerializer.Serialize(envelope, WyrdJsonContext.Default.Envelope)), ("at", availableAt)).ConfigureAwait(false);

    private async ValueTask SaveStateAsync(StoredState state, CancellationToken cancellationToken)
    {
        // StoredState.Version is the version being written; the one read is Version - 1.
        var rows = state.Version == 1
            ? await ExecuteAsync(
                $"INSERT INTO {_s}.states (state_type, key, type, payload, version) VALUES (@st, @k, @t, @p, 1) ON CONFLICT DO NOTHING",
                cancellationToken,
                ("st", state.StateType), ("k", state.Key), ("t", state.Type), ("p", state.Payload)).ConfigureAwait(false)
            : await ExecuteAsync(
                $"UPDATE {_s}.states SET type = @t, payload = @p, version = @v WHERE state_type = @st AND key = @k AND version = @v - 1",
                cancellationToken,
                ("st", state.StateType), ("k", state.Key), ("t", state.Type), ("p", state.Payload), ("v", state.Version)).ConfigureAwait(false);

        if (rows != 1)
        {
            throw new ConcurrencyConflictException($"Concurrency conflict on {state.StateType} '{state.Key}'.");
        }
    }

    private async ValueTask SaveInstanceAsync(StoredInstance instance, CancellationToken cancellationToken)
    {
        // StoredInstance.Version is the version read (0 when new).
        var data = JsonSerializer.Serialize(instance, WyrdJsonContext.Default.StoredInstance);
        var rows = instance.Version == 0
            ? await ExecuteAsync(
                $"INSERT INTO {_s}.instances (process, key, status, deadline, data, version) VALUES (@p, @k, @s, @d, @data, 1) ON CONFLICT DO NOTHING",
                cancellationToken,
                ("p", instance.Process), ("k", instance.Key), ("s", instance.Status.ToString()), ("d", (object?)instance.Deadline ?? DBNull.Value), ("data", data)).ConfigureAwait(false)
            : await ExecuteAsync(
                $"UPDATE {_s}.instances SET status = @s, deadline = @d, data = @data, version = @v + 1 WHERE process = @p AND key = @k AND version = @v",
                cancellationToken,
                ("p", instance.Process), ("k", instance.Key), ("s", instance.Status.ToString()), ("d", (object?)instance.Deadline ?? DBNull.Value), ("data", data), ("v", instance.Version)).ConfigureAwait(false);

        if (rows != 1)
        {
            throw new ConcurrencyConflictException($"Concurrency conflict on process '{instance.Process}' '{instance.Key}'.");
        }
    }

    private async ValueTask<int> ExecuteAsync(string text, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(text);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand Command(string text) => new(text, connection, transaction);
}
