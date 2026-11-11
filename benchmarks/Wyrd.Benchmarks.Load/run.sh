#!/usr/bin/env bash
# One run of the load benchmark on a new database:
#   benchmarks/Wyrd.Benchmarks.Load/run.sh <workers> <orders per second> [load options...]
#   benchmarks/Wyrd.Benchmarks.Load/run.sh 4 200 --skus 1 --duration 120      (one hot SKU)
#   benchmarks/Wyrd.Benchmarks.Load/run.sh 2 5 --app telecom --provider-latency 1000
#   benchmarks/Wyrd.Benchmarks.Load/run.sh ceiling 2000                       (plain PostgreSQL inserts)
# Prints the load's table, each worker's messages per second, and PostgreSQL's top statements.
set -euo pipefail
cd "$(dirname "$0")"
workers=${1:?workers, or "ceiling"}
rate=${2:?orders (or inserts) per second}
shift 2

docker compose down --volumes --remove-orphans >/dev/null 2>&1
docker compose build --quiet
docker compose up --detach --wait postgres
trap 'docker compose down --volumes --remove-orphans >/dev/null 2>&1' EXIT

if [ "$workers" = ceiling ]; then
    docker compose run --rm ceiling ceiling --rate "$rate" "$@"
    exit
fi

status=0

skus=1000
export APP=shop PROVIDER_LATENCY=0
args=("$@")
for i in "${!args[@]}"; do
    case "${args[$i]}" in
        --skus) skus=${args[$((i + 1))]} ;;
        --app) APP=${args[$((i + 1))]} ;;
        --provider-latency) PROVIDER_LATENCY=${args[$((i + 1))]} ;;
    esac
done
if [ "$APP" = telecom ]; then
    label="$workers worker(s), providers answering in $PROVIDER_LATENCY ms"
else
    label="$workers worker(s), $skus SKU(s)"
fi

docker compose run --rm setup setup --app "$APP" --skus "$skus"
docker compose up --detach --scale worker="$workers" worker
# A run with errors or dropped work still prints what the workers and the database did; it exits non-zero at the end.
docker compose run --rm load load --rate "$rate" --label "$label" "$@" || status=$?
docker compose stop worker >/dev/null
echo
echo "Workers (messages per second, every 10 s):"
docker compose logs --no-log-prefix worker | grep -E 'messages/s|stopped' | tail -n $((workers * 4))
echo
echo "PostgreSQL, top statements by total time:"
docker compose exec -T postgres psql -U postgres -d wyrd -c \
    "SELECT calls, round(total_exec_time)::int AS total_ms, round(mean_exec_time::numeric, 3) AS mean_ms, left(regexp_replace(query, '\s+', ' ', 'g'), 90) AS query FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 8"
exit "$status"
