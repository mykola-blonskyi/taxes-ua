# The backup sidecar: an encrypted pg_dump of the app database to S3-compatible storage, and a weekly
# proof that the newest dump restores (ADR-031). It is also what deploy/smoke.compose.yml runs as its
# S3 stand-in (`rclone serve s3`), so the smoke test needs no image that cannot be pulled.
# postgres:18 because pg_dump must be at least the shared instance's major version; its server
# binaries give the restore check a throwaway cluster of its own. When the instance moves to a newer
# major, bump the tag here. bash because the script is bash; the image is Alpine.
FROM postgres:18-alpine
RUN apk add --no-cache age rclone bash
COPY backup.sh /usr/local/bin/backup-db
RUN chmod 0755 /usr/local/bin/backup-db
USER postgres
ENTRYPOINT ["backup-db"]
CMD ["schedule"]
