using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;

public static class LabelsEndpoints
{
    public static void MapLabels(WebApplication app)
    {
        app.MapGet("/api/labels", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  lm.id,
                  lm.label_code,
                  lm.label_description,
                  lm.status,
                  lm.created_at,
                  lm.updated_at,
                  latest.prn_template_id,
                  latest.version AS prn_version
                FROM label_masters lm
                JOIN LATERAL (
                  SELECT id AS prn_template_id, version
                  FROM label_prn_templates
                  WHERE label_master_id = lm.id
                    AND COALESCE(NULLIF(TRIM(prn_content), ''), '') <> ''
                  ORDER BY version DESC, id DESC
                  LIMIT 1
                ) latest ON TRUE
                WHERE lm.status = 'Active'
                ORDER BY lm.updated_at DESC, lm.id DESC
                """);
            return Results.Json(new { data = rows });
        });

        app.MapGet("/api/labels/{id:int}", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT id, label_code, label_description, status, created_at, updated_at
                FROM label_masters
                WHERE id = @id
                """,
                ("id", id));
            if (rows.Count == 0)
            {
                return JsonError("Label not found", 404);
            }

            var template = await GetLatestLabelTemplateAsync(connection, id);
            return Results.Json(new { label = rows[0], prn_template = template });
        });

        app.MapPost("/api/labels", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var code = ReadLabelPayloadString(payload, "label_code", "labelCode")?.Trim().ToUpperInvariant();
            var description = ReadLabelPayloadString(payload, "label_description", "labelDescription")?.Trim() ?? string.Empty;
            var status = ReadLabelPayloadString(payload, "status")?.Trim();
            status = string.IsNullOrWhiteSpace(status) ? "Active" : status;
            status = status.Equals("Inactive", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";

            if (string.IsNullOrWhiteSpace(code))
            {
                return JsonError("Label Code is required", 400);
            }

            if (!Regex.IsMatch(code, "^[A-Z0-9-]+$"))
            {
                return JsonError("Use alphanumeric characters and hyphen only for Label Code", 400);
            }

            if (!IsValidLabelStatus(status))
            {
                return JsonError("Invalid label status", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO label_masters (label_code, label_description, status)
                    VALUES (@code, @description, @status)
                    RETURNING id, label_code, label_description, status, created_at, updated_at
                    """,
                    ("code", code),
                    ("description", description),
                    ("status", status));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonError("Label Code already exists", 409);
            }
        });

        app.MapPut("/api/labels/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var code = ReadLabelPayloadString(payload, "label_code", "labelCode")?.Trim().ToUpperInvariant();
            var description = ReadLabelPayloadString(payload, "label_description", "labelDescription")?.Trim() ?? string.Empty;
            var status = ReadLabelPayloadString(payload, "status")?.Trim();
            status = string.IsNullOrWhiteSpace(status) ? "Active" : status;
            status = status.Equals("Inactive", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";

            if (string.IsNullOrWhiteSpace(code))
            {
                return JsonError("Label Code is required", 400);
            }

            if (!Regex.IsMatch(code, "^[A-Z0-9-]+$"))
            {
                return JsonError("Use alphanumeric characters and hyphen only for Label Code", 400);
            }

            if (!IsValidLabelStatus(status))
            {
                return JsonError("Invalid label status", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    UPDATE label_masters
                    SET label_code = @code,
                        label_description = @description,
                        status = @status,
                        updated_at = NOW()
                    WHERE id = @id
                    RETURNING id, label_code, label_description, status, created_at, updated_at
                    """,
                    ("id", id),
                    ("code", code),
                    ("description", description),
                    ("status", status));
                return rows.Count == 0 ? JsonError("Label not found", 404) : Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonError("Label Code already exists", 409);
            }
        });

        app.MapPost("/api/labels/{id:int}/prn-template", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var prnFileName = ReadLabelPayloadString(payload, "prn_file_name", "prnFileName")?.Trim() ?? string.Empty;
            var prnContent = ReadLabelPayloadString(payload, "prn_content", "prnContent") ?? string.Empty;
            var previewData = ReadLabelPayloadString(payload, "preview_data", "previewData");

            if (string.IsNullOrWhiteSpace(prnContent))
            {
                return JsonError("PRN content is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            if (!await LabelExistsAsync(connection, id))
            {
                return JsonError("Label not found", 404);
            }

            var nextVersion = (await ScalarAsync<int?>(
                connection,
                "SELECT COALESCE(MAX(version), 0) + 1 FROM label_prn_templates WHERE label_master_id = @id",
                ("id", id))) ?? 1;

            var rows = await QueryRowsAsync(
                connection,
                """
                INSERT INTO label_prn_templates (label_master_id, prn_file_name, prn_content, preview_data, version)
                VALUES (@labelMasterId, @prnFileName, @prnContent, @previewData, @version)
                RETURNING id, label_master_id, prn_file_name, prn_content, preview_data, version, created_at, updated_at
                """,
                ("labelMasterId", id),
                ("prnFileName", prnFileName),
                ("prnContent", prnContent),
                ("previewData", previewData),
                ("version", nextVersion));
            await ExecuteAsync(connection, "UPDATE label_masters SET status = 'Active', updated_at = NOW() WHERE id = @id", ("id", id));
            return Results.Json(rows[0], statusCode: 201);
        });

        app.MapGet("/api/labels/{id:int}/prn-template", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            if (!await LabelExistsAsync(connection, id))
            {
                return JsonError("Label not found", 404);
            }

            var template = await GetLatestLabelTemplateAsync(connection, id);
            return template is null ? JsonError("PRN template not found", 404) : Results.Json(template);
        });

        app.MapPost("/api/labels/{id:int}/generate", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var rsn = ReadLabelPayloadString(payload, "rsn", "RSN")?.Trim();
            var previewData = ReadLabelPayloadString(payload, "preview_data", "previewData");

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            if (!await LabelExistsAsync(connection, id))
            {
                return JsonError("Label not found", 404);
            }

            var template = await GetLatestLabelTemplateAsync(connection, id);
            if (template is null)
            {
                return JsonError("PRN template not found", 404);
            }

            var rows = await QueryRowsAsync(
                connection,
                """
                INSERT INTO label_generation_history (label_master_id, label_prn_template_id, rsn, preview_data)
                VALUES (@labelMasterId, @templateId, @rsn, @previewData)
                RETURNING id, label_master_id, label_prn_template_id, rsn, preview_data, created_at
                """,
                ("labelMasterId", id),
                ("templateId", template["id"]),
                ("rsn", string.IsNullOrWhiteSpace(rsn) ? null : rsn),
                ("previewData", previewData));
            return Results.Json(rows[0], statusCode: 201);
        });
    }

    private static async Task EnsureLabelsSchemaAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS label_masters (
              id BIGSERIAL PRIMARY KEY,
              label_code VARCHAR(80) NOT NULL,
              label_description TEXT NOT NULL DEFAULT '',
              status VARCHAR(20) NOT NULL DEFAULT 'Active' CHECK (status IN ('Active', 'Inactive')),
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_label_masters_label_code UNIQUE (label_code)
            )
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS label_prn_templates (
              id BIGSERIAL PRIMARY KEY,
              label_master_id BIGINT NOT NULL REFERENCES label_masters(id) ON DELETE CASCADE,
              prn_file_name VARCHAR(255) NOT NULL DEFAULT '',
              prn_content TEXT NOT NULL,
              preview_data TEXT,
              version INT NOT NULL DEFAULT 1,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_label_prn_template_version UNIQUE (label_master_id, version)
            )
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS label_generation_history (
              id BIGSERIAL PRIMARY KEY,
              label_master_id BIGINT NOT NULL REFERENCES label_masters(id) ON DELETE CASCADE,
              label_prn_template_id BIGINT REFERENCES label_prn_templates(id) ON DELETE SET NULL,
              rsn VARCHAR(120),
              preview_data TEXT,
              created_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
    }

    private static async Task<bool> LabelExistsAsync(NpgsqlConnection connection, int id)
    {
        var count = await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM label_masters WHERE id = @id", ("id", id));
        return count > 0;
    }

    private static async Task<Dictionary<string, object?>?> GetLatestLabelTemplateAsync(NpgsqlConnection connection, int labelMasterId)
    {
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT id, label_master_id, prn_file_name, prn_content, preview_data, version, created_at, updated_at
            FROM label_prn_templates
            WHERE label_master_id = @labelMasterId
            ORDER BY version DESC, id DESC
            LIMIT 1
            """,
            ("labelMasterId", labelMasterId));
        return rows.Count == 0 ? null : rows[0];
    }

    private static string? ReadLabelPayloadString(JsonNode? payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadString(payload, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsValidLabelStatus(string value)
    {
        return value.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Inactive", StringComparison.OrdinalIgnoreCase);
    }
    private static Task<NpgsqlConnection> OpenConnectionAsync() => DbConnectionFactory.OpenConnectionAsync();

    private static Task<List<Dictionary<string, object?>>> QueryRowsAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters) => SqlQuery.QueryRowsAsync(connection, sql, parameters);

    private static Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters) => SqlQuery.ExecuteAsync(connection, sql, parameters);

    private static Task<T?> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters) => SqlQuery.ScalarAsync<T>(connection, sql, parameters);

    private static Task<JsonNode?> ReadJsonBodyAsync(HttpRequest request) => JsonBodyReader.ReadJsonBodyAsync(request);

    private static string? ReadString(JsonNode? node, string key) => JsonBodyReader.ReadString(node, key);

    private static IResult JsonError(string error, int statusCode) => ApiResults.JsonError(error, statusCode);
}

