using Npgsql;

public static class EpvTypesEndpoints
{
    public static void MapEpvTypes(WebApplication app)
    {
        app.MapGet("/api/epv-types", async () =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var types = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT
                  t.id,
                  t.type_name,
                  t.regex_rule,
                  t.created_at,
                  t.updated_at,
                  COALESCE(
                    json_agg(
                      json_build_object(
                        'id', st.id,
                        'sub_type_name', st.sub_type_name,
                        'regex_rule', st.regex_rule,
                        'created_at', st.created_at,
                        'updated_at', st.updated_at
                      )
                      ORDER BY st.sub_type_name ASC
                    ) FILTER (WHERE st.id IS NOT NULL),
                    '[]'
                  ) AS sub_types
                FROM epv_types t
                LEFT JOIN epv_sub_types st ON st.epv_type_id = t.id
                GROUP BY t.id
                ORDER BY t.type_name ASC
                """);
            return Results.Json(new { data = types, total = types.Count });
        });

        app.MapGet("/api/epv-types/regex-master", async () =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT
                  t.id AS epv_type_id,
                  t.type_name,
                  t.regex_rule AS type_regex_rule,
                  st.id AS epv_sub_type_id,
                  st.sub_type_name,
                  st.regex_rule AS sub_type_regex_rule,
                  COALESCE(COUNT(v.id), 0)::int AS total_quantity,
                  0::int AS used_quantity,
                  COALESCE(COUNT(v.id), 0)::int AS unused_quantity,
                  t.updated_at AS type_updated_at,
                  st.updated_at AS sub_type_updated_at
                FROM epv_types t
                LEFT JOIN epv_sub_types st ON st.epv_type_id = t.id
                LEFT JOIN sn_type_epv_values v
                  ON v.epv_type_id = t.id
                 AND (st.id IS NULL OR v.epv_sub_type_id = st.id)
                GROUP BY t.id, st.id
                ORDER BY t.type_name ASC, st.sub_type_name ASC
                """);
            return Results.Json(new { data = rows });
        });

        app.MapPost("/api/epv-types", async (HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var typeName = JsonBodyReader.ReadString(payload, "type_name")?.Trim();
            var regexRule = JsonBodyReader.ReadString(payload, "regex_rule")?.Trim();
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(regexRule))
            {
                return ApiResults.JsonMessage("type_name and regex_rule are required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    "INSERT INTO epv_types (type_name, regex_rule) VALUES (@typeName, @regexRule) RETURNING *",
                    ("typeName", typeName),
                    ("regexRule", regexRule));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return ApiResults.JsonMessage("EPV type already exists", 409);
            }
        });

        app.MapDelete("/api/epv-types/{typeId:int}", async (int typeId) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var used = await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_type_fields WHERE epv_type_id = @id", ("id", typeId));
            if (used > 0)
            {
                return ApiResults.JsonMessage("EPV type is in use and cannot be deleted", 409);
            }

            var rows = await SqlQuery.QueryRowsAsync(connection, "DELETE FROM epv_types WHERE id = @id RETURNING id", ("id", typeId));
            return rows.Count == 0 ? ApiResults.JsonMessage("EPV type not found", 404) : Results.Json(new { message = "EPV type deleted successfully" });
        });

        app.MapGet("/api/epv-types/{typeId:int}/sub-types", async (int typeId) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var typeRows = await SqlQuery.QueryRowsAsync(
                connection,
                "SELECT id, type_name, regex_rule, created_at, updated_at FROM epv_types WHERE id = @id",
                ("id", typeId));
            if (typeRows.Count == 0)
            {
                return ApiResults.JsonMessage("EPV type not found", 404);
            }

            var rows = await SqlQuery.QueryRowsAsync(
                connection,
                "SELECT id, epv_type_id, sub_type_name, regex_rule, created_at, updated_at FROM epv_sub_types WHERE epv_type_id = @id ORDER BY sub_type_name ASC",
                ("id", typeId));
            return Results.Json(new { type = typeRows[0], data = rows });
        });

        app.MapPost("/api/epv-types/{typeId:int}/sub-types", async (int typeId, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var subTypeName = JsonBodyReader.ReadString(payload, "sub_type_name")?.Trim();
            var regexRule = JsonBodyReader.ReadString(payload, "regex_rule")?.Trim();
            if (string.IsNullOrWhiteSpace(subTypeName) || string.IsNullOrWhiteSpace(regexRule))
            {
                return ApiResults.JsonMessage("sub_type_name and regex_rule are required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO epv_sub_types (epv_type_id, sub_type_name, regex_rule)
                    VALUES (@typeId, @subTypeName, @regexRule)
                    RETURNING *
                    """,
                    ("typeId", typeId),
                    ("subTypeName", subTypeName),
                    ("regexRule", regexRule));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return ApiResults.JsonMessage("EPV sub type already exists", 409);
            }
        });

        app.MapDelete("/api/epv-types/sub-types/{subTypeId:int}", async (int subTypeId) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var used = await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_type_fields WHERE epv_sub_type_id = @id", ("id", subTypeId));
            if (used > 0)
            {
                return ApiResults.JsonMessage("EPV sub type is in use and cannot be deleted", 409);
            }

            var rows = await SqlQuery.QueryRowsAsync(connection, "DELETE FROM epv_sub_types WHERE id = @id RETURNING id", ("id", subTypeId));
            return rows.Count == 0 ? ApiResults.JsonMessage("EPV sub type not found", 404) : Results.Json(new { message = "EPV sub type deleted successfully" });
        });
    }
}
