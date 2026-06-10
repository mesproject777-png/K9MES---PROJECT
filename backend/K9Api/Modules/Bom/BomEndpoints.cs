using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

public static class BomEndpoints
{
    public static void MapBom(WebApplication app)
    {
        app.MapGet("/api/bom/lookup", async (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString().Trim();
            var limit = Math.Min(ParsePositiveInt(request.Query["limit"], 20), 100);
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.Json(new { data = Array.Empty<object>() });
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT id, pn, description
                FROM items
                WHERE pn ILIKE @pattern OR description ILIKE @pattern
                ORDER BY pn ASC
                LIMIT @limit
                """,
                ("pattern", $"%{query}%"),
                ("limit", limit));
            return Results.Json(new { data = rows });
        });

        app.MapGet("/api/bom/by-pn", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return ApiResults.JsonMessage("pn is required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn LIMIT 1", ("pn", pn));
            return rows.Count == 0 ? ApiResults.JsonMessage("Part number not found", 404) : Results.Json(rows[0]);
        });

        app.MapGet("/api/bom/{itemId:int}/revisions", async (int itemId) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var itemRows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE id = @id", ("id", itemId));
            if (itemRows.Count == 0)
            {
                return ApiResults.JsonMessage("Part number not found", 404);
            }

            var revisions = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT id, item_id, revision, in_date, expire_date
                FROM item_revisions
                WHERE item_id = @itemId
                ORDER BY in_date DESC, id DESC
                """,
                ("itemId", itemId));
            return Results.Json(new { item = itemRows[0], data = revisions, total = revisions.Count });
        });

        app.MapGet("/api/bom/view/search", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            var revision = request.Query["revision"].ToString().Trim();
            var includeHistory = string.Equals(request.Query["includeHistory"], "true", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(pn))
            {
                return ApiResults.JsonMessage("pn is required", 400);
            }

            if (string.IsNullOrWhiteSpace(revision))
            {
                return ApiResults.JsonMessage("revision is required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var payload = await GetBomPayloadAsync(connection, pn, revision, includeHistory);
            if (payload is null)
            {
                return ApiResults.JsonMessage("Part number not found", 404);
            }

            if (payload.Revision is null)
            {
                return ApiResults.JsonMessage("Revision not found for this PN", 404);
            }

            return Results.Json(new
            {
                item = payload.Item,
                revision = payload.Revision,
                data = payload.Data,
                history = payload.History,
                total = payload.Data.Count
            });
        });

        app.MapPost("/api/bom/lines", async (HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var mainPn = JsonBodyReader.ReadString(payload, "main_pn")?.Trim();
            var mainRevisionText = JsonBodyReader.ReadString(payload, "main_revision")?.Trim();
            var sonPn = JsonBodyReader.ReadString(payload, "son_pn")?.Trim();
            var sonRevisionText = JsonBodyReader.ReadString(payload, "son_rev")?.Trim();
            var sonQty = ReadInt(payload, "son_qty");
            var referenceDesignators = JsonBodyReader.ReadString(payload, "reference_designators")?.Trim();
            var changedBy = JsonBodyReader.ReadString(payload, "changed_by") ?? "system";

            if (string.IsNullOrWhiteSpace(mainPn))
            {
                return ApiResults.JsonMessage("main_pn is required", 400);
            }

            if (string.IsNullOrWhiteSpace(mainRevisionText))
            {
                return ApiResults.JsonMessage("main_revision is required", 400);
            }

            if (string.IsNullOrWhiteSpace(sonPn))
            {
                return ApiResults.JsonMessage("son_pn is required", 400);
            }

            if (sonQty is null or <= 0)
            {
                return ApiResults.JsonMessage("son_qty must be a positive number", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var mainItem = await FindItemByPnAsync(connection, mainPn);
                if (mainItem is null)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Main PN not found", 404);
                }

                var mainRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(mainItem["id"]), mainRevisionText);
                if (mainRevision is null)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Main revision not found for PN", 404);
                }

                var sonItem = await FindItemByPnAsync(connection, sonPn);
                if (sonItem is null)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Son PN not found", 404);
                }

                int? sonRevisionId = null;
                if (!string.IsNullOrWhiteSpace(sonRevisionText))
                {
                    var sonRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(sonItem["id"]), sonRevisionText);
                    if (sonRevision is null)
                    {
                        await transaction.RollbackAsync();
                        return ApiResults.JsonMessage("Son revision not found for PN", 404);
                    }

                    sonRevisionId = Convert.ToInt32(sonRevision["id"]);
                }

                var duplicate = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    SELECT id
                    FROM item_bom_lines
                    WHERE main_item_revision_id = @mainRevisionId
                      AND son_item_id = @sonItemId
                      AND COALESCE(son_item_revision_id, 0) = COALESCE(@sonRevisionId, 0)
                      AND COALESCE(reference_designators, '') = COALESCE(@referenceDesignators, '')
                    LIMIT 1
                    """,
                    ("mainRevisionId", mainRevision["id"]),
                    ("sonItemId", sonItem["id"]),
                    ("sonRevisionId", ToDbNullable(sonRevisionId)),
                    ("referenceDesignators", ToDbNullable(referenceDesignators)));
                if (duplicate.Count > 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("BOM line already exists for this combination", 409);
                }

                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO item_bom_lines
                      (main_item_id, main_item_revision_id, son_item_id, son_item_revision_id, qty, reference_designators)
                    VALUES
                      (@mainItemId, @mainRevisionId, @sonItemId, @sonRevisionId, @qty, @referenceDesignators)
                    RETURNING *
                    """,
                    ("mainItemId", mainItem["id"]),
                    ("mainRevisionId", mainRevision["id"]),
                    ("sonItemId", sonItem["id"]),
                    ("sonRevisionId", ToDbNullable(sonRevisionId)),
                    ("qty", sonQty.Value),
                    ("referenceDesignators", ToDbNullable(referenceDesignators)));

                await InsertBomHistoryAsync(connection, mainItem["id"]!, mainRevision["id"]!, rows[0]["id"], "INSERT", "BOM line insert", rows[0], changedBy);
                await transaction.CommitAsync();
                return Results.Json(rows[0], statusCode: 201);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapDelete("/api/bom/lines/{lineId:int}", async (int lineId, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var changedBy = JsonBodyReader.ReadString(payload, "changed_by") ?? "system";
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    SELECT bl.*, main_item.pn AS main_pn, main_rev.revision AS main_rev, son_item.pn AS son_pn, COALESCE(son_rev.revision, '') AS son_rev
                    FROM item_bom_lines bl
                    JOIN items main_item ON main_item.id = bl.main_item_id
                    JOIN item_revisions main_rev ON main_rev.id = bl.main_item_revision_id
                    JOIN items son_item ON son_item.id = bl.son_item_id
                    LEFT JOIN item_revisions son_rev ON son_rev.id = bl.son_item_revision_id
                    WHERE bl.id = @id
                    LIMIT 1
                    """,
                    ("id", lineId));
                if (rows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("BOM line not found", 404);
                }

                await SqlQuery.ExecuteAsync(connection, "DELETE FROM item_bom_lines WHERE id = @id", ("id", lineId));
                await InsertBomHistoryAsync(connection, rows[0]["main_item_id"]!, rows[0]["main_item_revision_id"]!, null, "DELETE", "BOM line deleted", rows[0], changedBy);
                await transaction.CommitAsync();
                return Results.Json(new { message = "BOM line deleted successfully" });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private sealed record BomPayload(
        Dictionary<string, object?> Item,
        Dictionary<string, object?>? Revision,
        List<Dictionary<string, object?>> Data,
        List<Dictionary<string, object?>> History);
    private static async Task<BomPayload?> GetBomPayloadAsync(NpgsqlConnection connection, string pn, string revision, bool includeHistory)
    {
        var mainItem = await FindItemByPnAsync(connection, pn);
        if (mainItem is null)
        {
            return null;
        }

        var mainRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(mainItem["id"]), revision);
        if (mainRevision is null)
        {
            return new BomPayload(mainItem, null, new List<Dictionary<string, object?>>(), new List<Dictionary<string, object?>>());
        }

        var data = await SqlQuery.QueryRowsAsync(
            connection,
            """
            SELECT bl.id, bl.main_item_id, bl.main_item_revision_id, bl.son_item_id, bl.son_item_revision_id,
                   son.pn AS son_pn, son.description AS son_description, COALESCE(sr.revision, '') AS son_rev,
                   son.item_type AS son_item_type, COALESCE(pt.code, '') AS son_pn_type,
                   bl.qty AS son_qty, COALESCE(bl.reference_designators, '') AS reference_designators,
                   bl.created_at, bl.updated_at
            FROM item_bom_lines bl
            JOIN items son ON son.id = bl.son_item_id
            LEFT JOIN item_revisions sr ON sr.id = bl.son_item_revision_id
            LEFT JOIN pn_types pt ON pt.id = son.pn_type_id
            WHERE bl.main_item_revision_id = @revisionId
            ORDER BY bl.id ASC
            """,
            ("revisionId", mainRevision["id"]));

        var history = includeHistory
            ? await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT id, main_item_id, main_item_revision_id, bom_line_id, action, description, change_data, changed_by, changed_at
                FROM item_bom_history
                WHERE main_item_revision_id = @revisionId
                ORDER BY changed_at DESC, id DESC
                LIMIT 300
                """,
                ("revisionId", mainRevision["id"]))
            : new List<Dictionary<string, object?>>();

        return new BomPayload(mainItem, mainRevision, data, history);
    }
    private static async Task<Dictionary<string, object?>?> FindItemByPnAsync(NpgsqlConnection connection, string pn)
    {
        var rows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn LIMIT 1", ("pn", pn));
        return rows.Count == 0 ? null : rows[0];
    }
    private static async Task<Dictionary<string, object?>?> FindItemRevisionAsync(NpgsqlConnection connection, int itemId, string revision)
    {
        var rows = await SqlQuery.QueryRowsAsync(
            connection,
            """
            SELECT id, item_id, revision, in_date, expire_date
            FROM item_revisions
            WHERE item_id = @itemId AND revision = @revision
            ORDER BY in_date DESC, id DESC
            LIMIT 1
            """,
            ("itemId", itemId),
            ("revision", revision));
        return rows.Count == 0 ? null : rows[0];
    }
    private static async Task InsertBomHistoryAsync(
        NpgsqlConnection connection,
        object mainItemId,
        object mainRevisionId,
        object? bomLineId,
        string action,
        string description,
        object changeData,
        string changedBy)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO item_bom_history
              (main_item_id, main_item_revision_id, bom_line_id, action, description, change_data, changed_by)
            VALUES
              (@mainItemId, @mainRevisionId, @bomLineId, @action, @description, @changeData, @changedBy)
            """,
            connection);
        command.Parameters.AddWithValue("mainItemId", mainItemId);
        command.Parameters.AddWithValue("mainRevisionId", mainRevisionId);
        command.Parameters.AddWithValue("bomLineId", bomLineId ?? DBNull.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.Add("changeData", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(changeData);
        command.Parameters.AddWithValue("changedBy", changedBy);
        await command.ExecuteNonQueryAsync();
    }
    private static int ParsePositiveInt(object? value, int fallback)
    {
        return int.TryParse(value?.ToString(), out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static int? ReadInt(JsonNode? node, string key)
    {
        var value = node?[key];
        if (value is null)
        {
            return null;
        }

        if (value.GetValueKind() == JsonValueKind.String)
        {
            var stringValue = value.GetValue<string>();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return null;
            }

            return int.TryParse(stringValue, out var parsed) ? parsed : null;
        }

        return value.GetValue<int>();
    }

    private static object? ToDbNullable<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }
}

