using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

public static class RoutingEndpoints
{
    public static void MapRouting(WebApplication app)
    {
        app.MapGet("/api/routing/lookup", async (HttpRequest request) =>
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
                "SELECT id, pn, description FROM items WHERE pn ILIKE @pattern OR description ILIKE @pattern ORDER BY pn ASC LIMIT @limit",
                ("pattern", $"%{query}%"),
                ("limit", limit));
            return Results.Json(new { data = rows });
        });

        app.MapGet("/api/routing/by-pn", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return ApiResults.JsonMessage("pn is required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn", ("pn", pn));
            return rows.Count == 0 ? ApiResults.JsonMessage("Part number not found", 404) : Results.Json(rows[0]);
        });

        app.MapGet("/api/routing/{itemId:int}/steps", async (int itemId, HttpRequest request) =>
        {
            var includeHistory = string.Equals(request.Query["includeHistory"], "true", StringComparison.OrdinalIgnoreCase);
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var payload = await GetRoutingPayloadAsync(connection, itemId, includeHistory);
            return payload is null
                ? ApiResults.JsonMessage("Part number not found", 404)
                : Results.Json(new { item = payload.Item, data = payload.Data, history = payload.History, total = payload.Data.Count });
        });

        app.MapPost("/api/routing/{itemId:int}/steps", async (int itemId, HttpContext context) => await SaveRoutingStepAsync(context, itemId, null));
        app.MapPut("/api/routing/steps/{stepId:int}", async (int stepId, HttpContext context) => await SaveRoutingStepAsync(context, null, stepId));

        app.MapPut("/api/routing/steps/{stepId:int}/move", async (int stepId, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var direction = JsonBodyReader.ReadString(payload, "direction")?.Trim();
            var changedBy = JsonBodyReader.ReadString(payload, "changed_by") ?? "system";
            if (direction is not ("up" or "down"))
            {
                return ApiResults.JsonMessage("direction must be up or down", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var currentRows = await SqlQuery.QueryRowsAsync(connection, "SELECT * FROM item_routing_steps WHERE id = @id", ("id", stepId));
                if (currentRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Routing step not found", 404);
                }

                var current = currentRows[0];
                var targetRows = await SqlQuery.QueryRowsAsync(
                    connection,
                    $"""
                    SELECT *
                    FROM item_routing_steps
                    WHERE item_id = @itemId
                      AND station_order {(direction == "up" ? "<" : ">")} @stationOrder
                    ORDER BY station_order {(direction == "up" ? "DESC" : "ASC")}
                    LIMIT 1
                    """,
                    ("itemId", current["item_id"]),
                    ("stationOrder", current["station_order"]));
                if (targetRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage($"Cannot move {direction}", 400);
                }

                var target = targetRows[0];
                var tempOrder = -DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await SqlQuery.ExecuteAsync(connection, "UPDATE item_routing_steps SET station_order = @order WHERE id = @id", ("order", tempOrder), ("id", current["id"]));
                await SqlQuery.ExecuteAsync(connection, "UPDATE item_routing_steps SET station_order = @order WHERE id = @id", ("order", current["station_order"]), ("id", target["id"]));
                await SqlQuery.ExecuteAsync(connection, "UPDATE item_routing_steps SET station_order = @order WHERE id = @id", ("order", target["station_order"]), ("id", current["id"]));
                await InsertRoutingHistoryAsync(connection, current["item_id"]!, current["id"], "REORDER", $"Position change for station {current["station_code"]}", "station_order", current["station_order"]?.ToString(), target["station_order"]?.ToString(), changedBy);
                await InsertRoutingHistoryAsync(connection, target["item_id"]!, target["id"], "REORDER", $"Position change for station {target["station_code"]}", "station_order", target["station_order"]?.ToString(), current["station_order"]?.ToString(), changedBy);
                await transaction.CommitAsync();
                return Results.Json(new { message = "Station order updated successfully" });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapDelete("/api/routing/steps/{stepId:int}", async (int stepId, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var changedBy = JsonBodyReader.ReadString(payload, "changed_by") ?? "system";
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(connection, "SELECT * FROM item_routing_steps WHERE id = @id", ("id", stepId));
                if (rows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Routing step not found", 404);
                }

                await SqlQuery.ExecuteAsync(connection, "DELETE FROM item_routing_steps WHERE id = @id", ("id", stepId));
                await InsertRoutingHistoryAsync(connection, rows[0]["item_id"]!, null, "DELETE", $"Deleted station {rows[0]["station_code"]}", "station_code", rows[0]["station_code"]?.ToString(), null, changedBy);
                await transaction.CommitAsync();
                return Results.Json(new { message = "Routing step deleted successfully" });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static async Task<IResult> SaveRoutingStepAsync(HttpContext context, int? itemId, int? stepId)
    {
        var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
        var stationOrder = ReadInt(payload, "station_order");
        var stationCode = JsonBodyReader.ReadString(payload, "station_code")?.Trim();
        var sampleMode = JsonBodyReader.ReadString(payload, "sample_mode")?.Trim();
        var reportMode = JsonBodyReader.ReadString(payload, "report_mode")?.Trim();
        var stationLoginId = JsonBodyReader.ReadString(payload, "station_login_id")?.Trim();
        var stationLoginPassword = JsonBodyReader.ReadString(payload, "station_login_password")?.Trim();
        var stationIp = JsonBodyReader.ReadString(payload, "station_ip")?.Trim();
        var printerIp = JsonBodyReader.ReadString(payload, "printer_ip")?.Trim();
        var changedBy = JsonBodyReader.ReadString(payload, "changed_by") ?? "system";

        if (string.IsNullOrWhiteSpace(stationCode))
        {
            return ApiResults.JsonMessage("Station is required", 400);
        }

        if (sampleMode is not ("Full" or "Sample"))
        {
            return ApiResults.JsonMessage("Invalid sample mode", 400);
        }

        if (reportMode is not ("Regular" or "Auto Only"))
        {
            return ApiResults.JsonMessage("Invalid report mode", 400);
        }

        if (string.IsNullOrWhiteSpace(stationLoginId) || string.IsNullOrWhiteSpace(stationLoginPassword))
        {
            return ApiResults.JsonMessage("Station login ID and password are required", 400);
        }

        await using var connection = await DbConnectionFactory.OpenConnectionAsync();
        await EnsureRoutingStepLoginColumnsAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            Dictionary<string, object?>? existing = null;
            if (stepId is not null)
            {
                var existingRows = await SqlQuery.QueryRowsAsync(connection, "SELECT * FROM item_routing_steps WHERE id = @id", ("id", stepId.Value));
                if (existingRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage("Routing step not found", 404);
                }

                existing = existingRows[0];
                itemId = Convert.ToInt32(existing["item_id"]);
            }

            var item = await FindItemByIdAsync(connection, itemId!.Value);
            if (item is null)
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("Part number not found", 404);
            }

            var stationRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT masterstation_code, masterstation_name
                FROM masterstation
                WHERE UPPER(masterstation_code) = UPPER(@stationCode)
                LIMIT 1
                """,
                ("stationCode", stationCode));
            if (stationRows.Count == 0)
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("Station not found in Stations master", 400);
            }

            if (stationOrder is null or <= 0)
            {
                stationOrder = await SqlQuery.ScalarAsync<int>(connection, "SELECT COALESCE(MAX(station_order), 0) + 10 FROM item_routing_steps WHERE item_id = @itemId", ("itemId", itemId.Value));
            }

            if (!string.IsNullOrWhiteSpace(stationLoginId))
            {
                var duplicateLoginRows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    SELECT station_code
                    FROM item_routing_steps
                    WHERE item_id = @itemId
                      AND UPPER(station_login_id) = UPPER(@stationLoginId)
                      AND (@stepId::integer IS NULL OR id <> @stepId::integer)
                    LIMIT 1
                    """,
                    ("itemId", itemId.Value),
                    ("stationLoginId", stationLoginId),
                    ("stepId", stepId));

                if (duplicateLoginRows.Count > 0)
                {
                    await transaction.RollbackAsync();
                    return ApiResults.JsonMessage($"Station login ID is already used for station {duplicateLoginRows[0]["station_code"]}", 400);
                }
            }

            try
            {
                List<Dictionary<string, object?>> rows;
                if (stepId is null)
                {
                    rows = await SqlQuery.QueryRowsAsync(
                        connection,
                        """
                        INSERT INTO item_routing_steps
                          (item_id, station_order, station_code, station_name, sample_mode, report_mode,
                           station_login_id, station_login_password, station_ip, printer_ip)
                        VALUES
                          (@itemId, @stationOrder, @stationCode, @stationName, @sampleMode, @reportMode,
                           @stationLoginId, @stationLoginPassword, @stationIp, @printerIp)
                        RETURNING *
                        """,
                        ("itemId", itemId.Value),
                        ("stationOrder", stationOrder.Value),
                        ("stationCode", stationRows[0]["masterstation_code"]),
                        ("stationName", stationRows[0]["masterstation_name"]),
                        ("sampleMode", sampleMode),
                        ("reportMode", reportMode),
                        ("stationLoginId", ToDbNullable(stationLoginId)),
                        ("stationLoginPassword", ToDbNullable(stationLoginPassword)),
                        ("stationIp", ToDbNullable(stationIp)),
                        ("printerIp", ToDbNullable(printerIp)));
                    await InsertRoutingHistoryAsync(connection, itemId.Value, rows[0]["id"], "CREATE", $"Added station {stationCode}", "station_code", null, stationCode, changedBy);
                    await transaction.CommitAsync();
                    return Results.Json(rows[0], statusCode: 201);
                }

                rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    UPDATE item_routing_steps
                    SET station_order = @stationOrder,
                        station_code = @stationCode,
                        station_name = @stationName,
                        sample_mode = @sampleMode,
                        report_mode = @reportMode,
                        station_login_id = @stationLoginId,
                        station_login_password = @stationLoginPassword,
                        station_ip = @stationIp,
                        printer_ip = @printerIp,
                        updated_at = NOW()
                    WHERE id = @stepId
                    RETURNING *
                    """,
                    ("stationOrder", stationOrder.Value),
                    ("stationCode", stationRows[0]["masterstation_code"]),
                    ("stationName", stationRows[0]["masterstation_name"]),
                    ("sampleMode", sampleMode),
                    ("reportMode", reportMode),
                    ("stationLoginId", ToDbNullable(stationLoginId)),
                    ("stationLoginPassword", ToDbNullable(stationLoginPassword)),
                    ("stationIp", ToDbNullable(stationIp)),
                    ("printerIp", ToDbNullable(printerIp)),
                    ("stepId", stepId.Value));
                await InsertRoutingHistoryAsync(connection, itemId.Value, stepId.Value, "UPDATE", $"Updated station {stationCode}", null, JsonSerializer.Serialize(existing), JsonSerializer.Serialize(rows[0]), changedBy);
                await transaction.CommitAsync();
                return Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync();
                return ApiResults.JsonMessage("Station order already exists for this PN", 409);
            }
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    private sealed record RoutingPayload(
        Dictionary<string, object?> Item,
        List<Dictionary<string, object?>> Data,
        List<Dictionary<string, object?>> History);

    private static async Task<RoutingPayload?> GetRoutingPayloadAsync(NpgsqlConnection connection, int itemId, bool includeHistory)
    {
        await EnsureRoutingStepLoginColumnsAsync(connection);

        var item = await FindItemByIdAsync(connection, itemId);
        if (item is null)
        {
            return null;
        }

        var data = await SqlQuery.QueryRowsAsync(
            connection,
            """
            SELECT id, item_id, station_order, station_code, station_name, sample_mode, report_mode,
                   station_login_id, station_login_password, station_ip, printer_ip,
                   created_at, updated_at
            FROM item_routing_steps
            WHERE item_id = @itemId
            ORDER BY station_order ASC, id ASC
            """,
            ("itemId", itemId));

        var history = includeHistory
            ? await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT id, item_id, routing_step_id, action, description, change_field, old_value, new_value, changed_by, changed_at
                FROM item_routing_history
                WHERE item_id = @itemId
                ORDER BY changed_at DESC, id DESC
                LIMIT 300
                """,
                ("itemId", itemId))
            : new List<Dictionary<string, object?>>();

        return new RoutingPayload(item, data, history);
    }
    private static async Task<Dictionary<string, object?>?> FindItemByIdAsync(NpgsqlConnection connection, int itemId)
    {
        var rows = await SqlQuery.QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE id = @id LIMIT 1", ("id", itemId));
        return rows.Count == 0 ? null : rows[0];
    }
    private static async Task InsertRoutingHistoryAsync(
        NpgsqlConnection connection,
        object itemId,
        object? stepId,
        string action,
        string description,
        string? changeField,
        string? oldValue,
        string? newValue,
        string changedBy)
    {
        await SqlQuery.ExecuteAsync(
            connection,
            """
            INSERT INTO item_routing_history
              (item_id, routing_step_id, action, description, change_field, old_value, new_value, changed_by)
            VALUES
              (@itemId, @stepId, @action, @description, @changeField, @oldValue, @newValue, @changedBy)
            """,
            ("itemId", itemId),
            ("stepId", stepId),
            ("action", action),
            ("description", description),
            ("changeField", changeField),
            ("oldValue", oldValue),
            ("newValue", newValue),
            ("changedBy", changedBy));
    }
    private static async Task EnsureRoutingStepLoginColumnsAsync(NpgsqlConnection connection)
    {
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_login_id VARCHAR(160)");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_login_password VARCHAR(220)");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_ip VARCHAR(80)");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS printer_ip VARCHAR(80)");
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

