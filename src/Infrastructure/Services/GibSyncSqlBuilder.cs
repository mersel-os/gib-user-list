namespace MERSEL.Services.GibUserList.Infrastructure.Services;

internal static class GibSyncSqlBuilder
{
    public static string BuildPrepareNewDataSql(string documentType) => $@"
        CREATE TEMP TABLE _new_data ON COMMIT DROP AS
        WITH docs AS (
            SELECT t.identifier, t.account_type, t.first_creation_time,
                   t.title, t.title_lower, t.type,
                   d.value AS doc
            FROM gib_user_temp_pk t
            CROSS JOIN LATERAL jsonb_array_elements(t.documents) d
            WHERE d.value ->> 'Type' = '{documentType}'
            UNION ALL
            SELECT t.identifier, t.account_type, t.first_creation_time,
                   t.title, t.title_lower, t.type,
                   d.value AS doc
            FROM gib_user_temp_gb t
            CROSS JOIN LATERAL jsonb_array_elements(t.documents) d
            WHERE d.value ->> 'Type' = '{documentType}'
        ),
        flat_aliases AS (
            SELECT docs.identifier, docs.account_type, docs.first_creation_time,
                   docs.title, docs.title_lower, docs.type,
                   a.value AS al
            FROM docs
            CROSS JOIN LATERAL jsonb_array_elements(docs.doc -> 'Aliases') a
        ),
        aggregated AS (
            SELECT
                identifier,
                MAX(account_type) AS account_type,
                MAX(first_creation_time) AS first_creation_time,
                MAX(title) AS title,
                MAX(title_lower) AS title_lower,
                MAX(type) AS type,
                jsonb_agg(al ORDER BY al ->> 'Alias', al ->> 'Type') AS aliases_json,
                string_agg(
                    (al ->> 'Alias') || ':' || (al ->> 'Type'), ','
                    ORDER BY al ->> 'Alias', al ->> 'Type'
                ) AS alias_signature
            FROM flat_aliases
            GROUP BY identifier
        )
        SELECT
            identifier, account_type, first_creation_time, title, title_lower, type, aliases_json,
            md5(
                identifier ||
                COALESCE(title_lower, '') ||
                COALESCE(account_type, '') ||
                COALESCE(type, '') ||
                first_creation_time::text ||
                COALESCE(alias_signature, '')
            ) AS content_hash
        FROM aggregated;";

    public static string BuildAddedIdentifiersSql(string tableName) => $@"
        SELECT nd.identifier FROM _new_data nd
        LEFT JOIN {tableName} t ON t.identifier = nd.identifier
        WHERE t.identifier IS NULL;";

    public static string BuildModifiedIdentifiersSql(string tableName) => $@"
        SELECT nd.identifier FROM _new_data nd
        INNER JOIN {tableName} t ON t.identifier = nd.identifier
        WHERE t.content_hash IS DISTINCT FROM nd.content_hash;";

    public static string BuildRemovedIdentifiersSql(string tableName) => $@"
        SELECT t.identifier FROM {tableName} t
        LEFT JOIN _new_data nd ON nd.identifier = t.identifier
        WHERE nd.identifier IS NULL;";

    public static string BuildCurrentCountSql(string tableName) => $"SELECT COUNT(*) FROM {tableName};";

    public static string BuildInsertAddedSql(string tableName, List<string> addedIdentifiers)
    {
        if (addedIdentifiers.Count == 0) return string.Empty;
        var inList = string.Join(",", addedIdentifiers.Select(EscapeSqlString));
        return $@"
            INSERT INTO {tableName}
                (id, identifier, account_type, first_creation_time, title, title_lower, type, aliases_json, content_hash)
            SELECT
                gen_random_uuid(), identifier, account_type, first_creation_time,
                title, title_lower, type, aliases_json, content_hash
            FROM _new_data
            WHERE identifier IN ({inList});";
    }

    public static string BuildUpdateModifiedSql(string tableName, List<string> modifiedIdentifiers)
    {
        if (modifiedIdentifiers.Count == 0) return string.Empty;
        var inList = string.Join(",", modifiedIdentifiers.Select(EscapeSqlString));
        return $@"
            UPDATE {tableName} t SET
                aliases_json = nd.aliases_json,
                title = nd.title,
                title_lower = nd.title_lower,
                account_type = nd.account_type,
                type = nd.type,
                first_creation_time = nd.first_creation_time,
                content_hash = nd.content_hash
            FROM _new_data nd
            WHERE t.identifier = nd.identifier
              AND t.identifier IN ({inList});";
    }

    public static string BuildHardDeleteSql(string tableName, List<string> removedIdentifiers)
    {
        if (removedIdentifiers.Count == 0) return string.Empty;
        var inList = string.Join(",", removedIdentifiers.Select(EscapeSqlString));
        return $@"
            DELETE FROM {tableName}
            WHERE identifier IN ({inList});";
    }

    public static string BuildDeleteOldChangelogSql(int retentionDays) => $@"
        DELETE FROM gib_user_changelog
        WHERE changed_at < (NOW() AT TIME ZONE 'Europe/Istanbul') - INTERVAL '{retentionDays} days';";

    public static string BuildChangelogInsertSql(
        short docType,
        List<string> added,
        List<string> modified,
        List<string> removed)
    {
        var parts = new List<string>();
        const string localNow = "NOW() AT TIME ZONE 'Europe/Istanbul'";

        if (added.Count > 0)
        {
            var inList = string.Join(",", added.Select(EscapeSqlString));
            parts.Add($@"
                SELECT gen_random_uuid(), {docType}, nd.identifier, 1, {localNow},
                       nd.title, nd.account_type, nd.type, nd.first_creation_time, nd.aliases_json
                FROM _new_data nd WHERE nd.identifier IN ({inList})");
        }

        if (modified.Count > 0)
        {
            var inList = string.Join(",", modified.Select(EscapeSqlString));
            parts.Add($@"
                SELECT gen_random_uuid(), {docType}, nd.identifier, 2, {localNow},
                       nd.title, nd.account_type, nd.type, nd.first_creation_time, nd.aliases_json
                FROM _new_data nd WHERE nd.identifier IN ({inList})");
        }

        if (removed.Count > 0)
        {
            var removedParts = removed.Select(id =>
                $"SELECT gen_random_uuid(), {docType}::smallint, {EscapeSqlString(id)}, 3::smallint, {localNow}, " +
                "NULL::varchar, NULL::varchar, NULL::varchar, NULL::timestamp, NULL::jsonb");
            parts.Add(string.Join(" UNION ALL ", removedParts));
        }

        if (parts.Count == 0)
            return string.Empty;

        return $@"
            INSERT INTO gib_user_changelog
                (id, document_type, identifier, change_type, changed_at, title, account_type, type, first_creation_time, aliases_json)
            {string.Join(" UNION ALL ", parts)};";
    }

    public static string BuildSyncMetadataUpsertSql() => @"
        INSERT INTO sync_metadata (
            key, last_sync_at, e_invoice_user_count, e_despatch_user_count, last_sync_duration,
            last_sync_status, last_sync_error, last_attempt_at, last_failure_at
        )
        VALUES (
            @key, @syncAt,
            (SELECT COUNT(*) FROM e_invoice_gib_users),
            (SELECT COUNT(*) FROM e_despatch_gib_users),
            @duration,
            @status, @error, @attemptAt, @failureAt
        )
        ON CONFLICT (key) DO UPDATE SET
            last_sync_at = EXCLUDED.last_sync_at,
            e_invoice_user_count = EXCLUDED.e_invoice_user_count,
            e_despatch_user_count = EXCLUDED.e_despatch_user_count,
            last_sync_duration = EXCLUDED.last_sync_duration,
            last_sync_status = EXCLUDED.last_sync_status,
            last_sync_error = EXCLUDED.last_sync_error,
            last_attempt_at = EXCLUDED.last_attempt_at,
            last_failure_at = EXCLUDED.last_failure_at;";

    public static string BuildSyncMetadataStatusUpdateSql() => @"
        INSERT INTO sync_metadata (
            key, last_sync_at, e_invoice_user_count, e_despatch_user_count, last_sync_duration,
            last_sync_status, last_sync_error, last_attempt_at, last_failure_at
        )
        VALUES (
            @key,
            (SELECT last_sync_at FROM sync_metadata WHERE key = @key),
            COALESCE((SELECT e_invoice_user_count FROM sync_metadata WHERE key = @key), 0),
            COALESCE((SELECT e_despatch_user_count FROM sync_metadata WHERE key = @key), 0),
            COALESCE((SELECT last_sync_duration FROM sync_metadata WHERE key = @key), INTERVAL '0 seconds'),
            @status, @error, @attemptAt,
            (SELECT last_failure_at FROM sync_metadata WHERE key = @key)
        )
        ON CONFLICT (key) DO UPDATE SET
            last_sync_status = EXCLUDED.last_sync_status,
            last_sync_error = EXCLUDED.last_sync_error,
            last_attempt_at = EXCLUDED.last_attempt_at,
            last_failure_at = CASE
                WHEN EXCLUDED.last_sync_status = 'success' THEN NULL
                ELSE sync_metadata.last_failure_at
            END;";

    public static string BuildSyncMetadataFailureUpdateSql() => @"
        INSERT INTO sync_metadata (
            key, last_sync_at, e_invoice_user_count, e_despatch_user_count, last_sync_duration,
            last_sync_status, last_sync_error, last_attempt_at, last_failure_at
        )
        VALUES (
            @key,
            (SELECT last_sync_at FROM sync_metadata WHERE key = @key),
            COALESCE((SELECT e_invoice_user_count FROM sync_metadata WHERE key = @key), 0),
            COALESCE((SELECT e_despatch_user_count FROM sync_metadata WHERE key = @key), 0),
            COALESCE((SELECT last_sync_duration FROM sync_metadata WHERE key = @key), INTERVAL '0 seconds'),
            @status, @error, @attemptAt, @failureAt
        )
        ON CONFLICT (key) DO UPDATE SET
            last_sync_status = EXCLUDED.last_sync_status,
            last_sync_error = EXCLUDED.last_sync_error,
            last_attempt_at = EXCLUDED.last_attempt_at,
            last_failure_at = EXCLUDED.last_failure_at;";

    public const string DropNewDataTempTableSql = "DROP TABLE IF EXISTS _new_data;";

    private static string EscapeSqlString(string value) => $"'{value.Replace("'", "''")}'";
}
