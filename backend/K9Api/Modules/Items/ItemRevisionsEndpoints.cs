using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

public static class ItemRevisionsEndpoints
{
    public static void MapItemRevisions(WebApplication app)
    {
        app.MapGet("/api/item-revisions/lookup", async (HttpRequest request) =>
        {
            var search = request.Query["search"].ToString().Trim();
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT id, pn, description
                FROM items
                WHERE @search = '' OR pn ILIKE @pattern OR description ILIKE @pattern
                ORDER BY pn ASC
                LIMIT 25
                """,
                ("search", search),
                ("pattern", $"%{search}%"));
            return Results.Json(rows);
        });

        app.MapGet("/api/item-revisions/by-pn", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return ApiResults.JsonMessage("pn is required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn", ("pn", pn));
            return rows.Count == 0 ? ApiResults.JsonMessage("Item not found", 404) : Results.Json(rows[0]);
        });

        app.MapGet("/api/item-revisions/{itemId:int}/revisions", async (int itemId) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var itemRows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE id = @id", ("id", itemId));
            if (itemRows.Count == 0)
            {
                return ApiResults.JsonMessage("Item not found", 404);
            }

            var revisionRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT id, item_id, revision, in_date, expire_date, version, description, created_at, updated_at
                FROM item_revisions
                WHERE item_id = @itemId
                ORDER BY in_date DESC, revision DESC
                """,
                ("itemId", itemId));
            return Results.Json(new { item = itemRows[0], revisions = revisionRows });
        });

        app.MapPost("/api/item-revisions/{itemId:int}/revisions", async (int itemId, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var revision = JsonBodyReader.ReadString(payload, "revision")?.Trim();
            var inDate = JsonBodyReader.ReadString(payload, "in_date")?.Trim();
            var expireDate = JsonBodyReader.ReadString(payload, "expire_date")?.Trim();
            var version = JsonBodyReader.ReadString(payload, "version")?.Trim();
            var description = JsonBodyReader.ReadString(payload, "description")?.Trim();
            var changedBy = JsonBodyReader.ReadString(payload, "changed_by") ?? "system";

            if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(inDate))
            {
                return ApiResults.JsonMessage("revision and in_date are required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var itemRows = await SqlQuery.QueryRowsAsync(connection, "SELECT id FROM items WHERE id = @id", ("id", itemId));
                if (itemRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Item not found", 404);
                }

                var inserted = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO item_revisions (item_id, revision, in_date, expire_date, version, description)
                    VALUES (@itemId, @revision, @inDate::date, NULLIF(@expireDate, '')::date, @version, @description)
                    RETURNING *
                    """,
                    ("itemId", itemId),
                    ("revision", revision),
                    ("inDate", inDate),
                    ("expireDate", expireDate ?? string.Empty),
                    ("version", ToDbNullable(version)),
                    ("description", ToDbNullable(description)));

                await InsertJsonHistoryAsync(connection, "item_revision_history", "item_revision_id", inserted[0]["id"]!, "CREATE", inserted[0], changedBy);
                await transaction.CommitAsync();
                return Results.Json(inserted[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("Revision already exists for this item and in date", 409);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static async Task InsertJsonHistoryAsync(
        NpgsqlConnection connection,
        string tableName,
        string idColumn,
        object id,
        string action,
        Dictionary<string, object?> snapshot,
        string changedBy)
    {
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {tableName} ({idColumn}, action, snapshot, changed_by) VALUES (@id, @action, @snapshot, @changedBy)",
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.Add("snapshot", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(snapshot);
        command.Parameters.AddWithValue("changedBy", changedBy);
        await command.ExecuteNonQueryAsync();
    }

    private static object? ToDbNullable<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }
}
