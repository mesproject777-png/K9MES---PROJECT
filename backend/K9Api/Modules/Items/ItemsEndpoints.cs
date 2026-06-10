using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

public static class ItemsEndpoints
{
    private static readonly HashSet<string> AllowedItemTypes = new(StringComparer.Ordinal)
    {
        "Manufactured",
        "Purchased"
    };

    public static void MapItems(WebApplication app)
    {
        app.MapGet("/api/items", async (HttpRequest request) =>
        {
            var page = ParsePositiveInt(request.Query["page"], 1);
            var requestedLimit = request.Query["limit"].ToString();
            var limit = string.Equals(requestedLimit, "all", StringComparison.OrdinalIgnoreCase)
                ? 5000
                : Math.Min(ParsePositiveInt(requestedLimit, 15), 500);
            var search = request.Query["search"].ToString().Trim();
            var offset = (page - 1) * limit;
            var parameters = new List<(string Name, object? Value)>();
            var whereSql = string.Empty;

            if (!string.IsNullOrWhiteSpace(search))
            {
                whereSql = "WHERE i.pn ILIKE @search OR i.description ILIKE @search";
                parameters.Add(("search", $"%{search}%"));
            }

            parameters.Add(("limit", limit));
            parameters.Add(("offset", offset));

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(
                connection,
                $"""
                SELECT
                  i.id,
                  i.pn,
                  i.description,
                  i.marketing_desc,
                  i.phantom,
                  i.sgd_control,
                  i.item_type,
                  i.created_at,
                  i.updated_at,
                  pl.id AS product_line_id,
                  pl.code AS product_line_code,
                  pl.description AS product_line_description,
                  st.id AS sn_type_id,
                  st.sn_type_name AS sn_type_name,
                  pt.id AS pn_type_id,
                  pt.code AS pn_type_code,
                  pt.type AS pn_type_name,
                  COUNT(*) OVER () AS total_count
                FROM items i
                LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                LEFT JOIN sn_types st ON st.id = i.sn_type_id
                LEFT JOIN pn_types pt ON pt.id = i.pn_type_id
                {whereSql}
                ORDER BY i.created_at DESC, i.id DESC
                LIMIT @limit OFFSET @offset
                """,
                parameters.ToArray());
            var total = rows.Count > 0 ? Convert.ToInt32(rows[0]["total_count"] ?? 0) : 0;
            foreach (var row in rows)
            {
                row.Remove("total_count");
            }

            return Results.Json(new { data = rows, total, page, limit });
        });

        app.MapPost("/api/items", async (HttpContext context) => await SaveItemAsync(context, null));
        app.MapPut("/api/items/{id:int}", async (HttpContext context, int id) => await SaveItemAsync(context, id));
    }

    private static async Task<IResult> SaveItemAsync(HttpContext context, int? itemId)
    {
        var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
        var pn = JsonBodyReader.ReadString(payload, "pn")?.Trim();
        var description = JsonBodyReader.ReadString(payload, "description")?.Trim();
        var marketingDesc = JsonBodyReader.ReadString(payload, "marketing_desc")?.Trim();
        var phantom = ReadBool(payload, "phantom");
        var sgdControl = ReadBool(payload, "sgd_control");
        var itemType = JsonBodyReader.ReadString(payload, "item_type")?.Trim();
        var productLineId = ReadInt(payload, "product_line_id");
        var snTypeId = ReadInt(payload, "sn_type_id");
        var snTypeName = JsonBodyReader.ReadString(payload, "sn_type_name")?.Trim();
        var pnTypeId = ReadInt(payload, "pn_type_id");
        var changedBy = JsonBodyReader.ReadString(payload, "changed_by") ?? "system";

        if (string.IsNullOrWhiteSpace(pn))
        {
            return ApiResults.JsonMessage("Part number is required", 400);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return ApiResults.JsonMessage("Description is required", 400);
        }

        if (phantom is null)
        {
            return ApiResults.JsonMessage("Phantom selection is required", 400);
        }

        if (sgdControl is null)
        {
            return ApiResults.JsonMessage("SGD control must be true or false", 400);
        }

        if (string.IsNullOrWhiteSpace(itemType) || !AllowedItemTypes.Contains(itemType))
        {
            return ApiResults.JsonMessage("Invalid item type", 400);
        }

        if (productLineId is null)
        {
            return ApiResults.JsonMessage("Product line is required", 400);
        }

        if (pnTypeId is null)
        {
            return ApiResults.JsonMessage("PN type is required", 400);
        }

        await using var connection = await DbConnectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            if (await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM product_lines WHERE id = @id", ("id", productLineId.Value)) == 0)
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("Product line not found", 400);
            }

            if (await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM pn_types WHERE id = @id", ("id", pnTypeId.Value)) == 0)
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("PN type not found", 400);
            }

            int? resolvedSnTypeId = null;
            if (snTypeId is not null)
            {
                if (await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", snTypeId.Value)) == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("SN type not found", 400);
                }

                resolvedSnTypeId = snTypeId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(snTypeName))
            {
                resolvedSnTypeId = await SqlQuery.ScalarAsync<int?>(connection, "SELECT id FROM sn_types WHERE sn_type_name = @name", ("name", snTypeName));
                if (resolvedSnTypeId is null)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("SN type not found", 400);
                }
            }

            List<Dictionary<string, object?>> rows;
            if (itemId is null)
            {
                rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO items
                      (pn, description, marketing_desc, phantom, sgd_control, item_type, product_line_id, sn_type_id, pn_type_id)
                    VALUES
                      (@pn, @description, @marketingDesc, @phantom, @sgdControl, @itemType, @productLineId, @snTypeId, @pnTypeId)
                    RETURNING *
                    """,
                    ("pn", pn),
                    ("description", description),
                    ("marketingDesc", ToDbNullable(marketingDesc)),
                    ("phantom", phantom.Value),
                    ("sgdControl", sgdControl.Value),
                    ("itemType", itemType),
                    ("productLineId", productLineId.Value),
                    ("snTypeId", ToDbNullable(resolvedSnTypeId)),
                    ("pnTypeId", pnTypeId.Value));
            }
            else
            {
                if (await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM items WHERE id = @id", ("id", itemId.Value)) == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Part number not found", 404);
                }

                rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    UPDATE items
                    SET pn = @pn,
                        description = @description,
                        marketing_desc = @marketingDesc,
                        phantom = @phantom,
                        sgd_control = @sgdControl,
                        item_type = @itemType,
                        product_line_id = @productLineId,
                        sn_type_id = @snTypeId,
                        pn_type_id = @pnTypeId,
                        updated_at = NOW()
                    WHERE id = @id
                    RETURNING *
                    """,
                    ("pn", pn),
                    ("description", description),
                    ("marketingDesc", ToDbNullable(marketingDesc)),
                    ("phantom", phantom.Value),
                    ("sgdControl", sgdControl.Value),
                    ("itemType", itemType),
                    ("productLineId", productLineId.Value),
                    ("snTypeId", ToDbNullable(resolvedSnTypeId)),
                    ("pnTypeId", pnTypeId.Value),
                    ("id", itemId.Value));
            }

            await InsertJsonHistoryAsync(connection, "item_history", "item_id", rows[0]["id"]!, itemId is null ? "CREATE" : "UPDATE", rows[0], changedBy);
            await transaction.CommitAsync();
            return Results.Json(rows[0], statusCode: itemId is null ? 201 : 200);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync();
            return ApiResults.JsonMessage("Part number already exists", 409);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

    private static bool? ReadBool(JsonNode? node, string key)
    {
        var value = node?[key];
        if (value is null)
        {
            return null;
        }

        return value.GetValue<bool>();
    }

    private static object? ToDbNullable<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }
}