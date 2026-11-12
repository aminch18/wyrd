#!/usr/bin/env bash
# One run of the load benchmark on a new database:
#   benchmarks/Wyrd.Benchmarks.Load/run.sh <workers> <orders per second> [options...]
#   benchmarks/Wyrd.Benchmarks.Load/run.sh 4 200 --skus 1 --duration 120      (one hot SKU)
#   benchmarks/Wyrd.Benchmarks.Load/run.sh 2 5 --app telecom --provider-latency 1000
#   benchmarks/Wyrd.Benchmarks.Load/run.sh 4 50 --retention 120 --duration 1800 (soak: cleanup during the load)
#   benchmarks/Wyrd.Benchmarks.Load/run.sh 4 50 --kill-worker-at 60            (recovery: one worker dies)
#   benchmarks/Wyrd.Benchmarks.Load/run.sh 8 100 --pgbouncer                   (every connection through PgBouncer)
#   benchmarks/Wyrd.Benchmarks.Load/run.sh ceiling 2000                       (plain PostgreSQL inserts)
# Options of this script, the others go to the load:
#   --retention <s>        the workers keep everything that long only, and clean up during the run (default: forever)
#   --kill-worker-at <s>   kills one worker (SIGKILL) that many seconds after the load started; the others take over
#   --pgbouncer            setup, workers and load connect through PgBouncer (transaction pooling, pgbouncer.ini)
# Prints the load's table, each worker's messages per second, the database every minute, and PostgreSQL's top
# statements and tables.
set -euo pipefail
cd "$(dirname "$0")"
workers=${1:?workers, or "ceiling"}
rate=${2:?orders (or inserts) per second}
shift 2

skus=1000 kill_at=""
export APP=shop PROVIDER_LATENCY=0 RETENTION=0
args=("$@")
for i in "${!args[@]}"; do
    case "${args[$i]}" in
        --skus) skus=${args[$((i + 1))]} ;;
        --app) APP=${args[$((i + 1))]} ;;
        --provider-latency) PROVIDER_LATENCY=${args[$((i + 1))]} ;;
        --retention) RETENTION=${args[$((i + 1))]} ;;
        --kill-worker-at) kill_at=${args[$((i + 1))]} ;;
        --pgbouncer)
            export COMPOSE_PROFILES=pgbouncer
            export APP_CONNECTION="Host=pgbouncer;Port=6432;Database=wyrd;Username=postgres;Password=wyrd;No Reset On Close=true"
            ;;
    esac
done

down() { docker compose --profile pgbouncer down --volumes --remove-orphans >/dev/null 2>&1; }
psql() { docker compose exec -T postgres psql -U postgres -d wyrd "$@"; }

down
docker compose build --quiet
docker compose up --detach --wait postgres
samples=$(mktemp)
trap 'down; rm -f "$samples"' EXIT
if [ -n "${COMPOSE_PROFILES:-}" ]; then
    docker compose up --detach pgbouncer
fi

if [ "$workers" = ceiling ]; then
    docker compose run --rm ceiling ceiling --rate "$rate" "$@"
    exit
fi

status=0
if [ "$APP" = telecom ]; then
    label="$workers worker(s), providers answering in $PROVIDER_LATENCY ms"
else
    label="$workers worker(s), $skus SKU(s)"
fi
[ "$RETENTION" != 0 ] && label="$label, retention $RETENTION s"
[ -n "${COMPOSE_PROFILES:-}" ] && label="$label, through PgBouncer"
[ -n "$kill_at" ] && label="$label, one killed at $kill_at s"

docker compose run --rm setup setup --app "$APP" --skus "$skus"
docker compose up --detach --scale worker="$workers" worker

# The database every minute, from statistics (no count over a table under load): its size, and the queue's live and
# dead rows, which show whether cleanup and autovacuum keep up.
(
    start=$(date +%s)
    while sleep 60; do
        psql -At -F ' | ' -c "SELECT $(($(date +%s) - start)),
            (SELECT round(sum(pg_total_relation_size(relid)) / 1048576.0) FROM pg_stat_user_tables WHERE schemaname = 'wyrd'),
            n_live_tup, n_dead_tup, autovacuum_count FROM pg_stat_user_tables WHERE schemaname = 'wyrd' AND relname = 'queue'" \
            >>"$samples" 2>/dev/null || true
    done
) &
sampler=$!

if [ -n "$kill_at" ]; then
    victim=$(docker compose ps --quiet worker | head -n 1)
    (sleep "$kill_at" && docker kill "$victim" >/dev/null && echo "Killed worker $(docker inspect --format '{{.Name}}' "$victim") at $kill_at s.") &
fi

# A run with errors or dropped work still prints what the workers and the database did; it exits non-zero at the end.
docker compose run --rm load load --rate "$rate" --label "$label" "$@" || status=$?
kill "$sampler" 2>/dev/null || true
docker compose stop worker >/dev/null
echo
echo "Workers (messages per second, every 10 s):"
docker compose logs --no-log-prefix worker | grep -E 'messages/s|stopped' | tail -n $((workers * 4))
if [ -s "$samples" ]; then
    echo
    echo "Database every minute (s | wyrd MB | queue rows | queue dead rows | queue autovacuums):"
    cat "$samples"
fi
echo
echo "PostgreSQL, top statements by total time:"
psql -c "SELECT calls, round(total_exec_time)::int AS total_ms, round(mean_exec_time::numeric, 3) AS mean_ms, left(regexp_replace(query, '\s+', ' ', 'g'), 90) AS query FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 8"
echo "PostgreSQL, largest tables:"
psql -c "SELECT relname AS table, pg_size_pretty(pg_total_relation_size(relid)) AS size, n_live_tup AS live, n_dead_tup AS dead, autovacuum_count AS autovacuums FROM pg_stat_user_tables WHERE schemaname = 'wyrd' ORDER BY pg_total_relation_size(relid) DESC LIMIT 8"
exit "$status"
