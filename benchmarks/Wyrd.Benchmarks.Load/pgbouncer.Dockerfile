# PgBouncer from Alpine's package (1.23 in Alpine 3.20), with the benchmark's configuration.
FROM alpine:3.20
RUN apk add --no-cache pgbouncer
COPY pgbouncer.ini userlist.txt /etc/pgbouncer/
USER nobody
EXPOSE 6432
CMD ["pgbouncer", "/etc/pgbouncer/pgbouncer.ini"]
