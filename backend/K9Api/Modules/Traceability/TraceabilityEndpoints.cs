using System.Text.Json.Nodes;
using Npgsql;

public static class TraceabilityEndpoints
{
    public static void MapTraceability(WebApplication app)
    {
        app.MapGet("/api/traceability/schema/verify", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            var hasSerialNumbers = await ScalarAsync<bool>(
                connection,
                """
                SELECT EXISTS (
                  SELECT 1
                  FROM information_schema.tables
                  WHERE table_schema = 'public'
                    AND table_name = 'serial_numbers'
                )
                """);
            return Results.Json(new { serial_numbers_exists = hasSerialNumbers });
        });

        app.MapGet("/api/traceability/search", async (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonMessage("Search query is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);
            await EnsureSerialExternalValuesTableAsync(connection);
            var workflowSerial = await GetWorkflowSerialByQueryAsync(connection, query);
            if (workflowSerial is not null)
            {
                return Results.Json(await BuildWorkflowTracePayloadAsync(connection, query, workflowSerial));
            }

            var serial = await GetSerialByQueryAsync(connection, query);
            if (serial is not null)
            {
                return Results.Json(await BuildTracePayloadAsync(connection, query, serial));
            }

            return JsonMessage("SN/RSN not found", 404);
        });

        app.MapPost("/api/traceability/pass-fail", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var query = ReadString(payload, "query")?.Trim();
            var stationCode = ReadString(payload, "station_code")?.Trim();
            var result = ReadString(payload, "result")?.Trim().ToUpperInvariant();
            var remark = ReadString(payload, "remark")?.Trim();
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
            var stationLength = ReadString(payload, "station_length")?.Trim();
            var pcName = ReadString(payload, "pc_name")?.Trim() ?? "WEB-CLIENT";
            var additionalInfo = ReadString(payload, "additional_info")?.Trim() ?? (result == "PASS" ? "Auto Pass Result" : "Auto Fail Result");

            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonMessage("SN or RSN is required", 400);
            }

            if (string.IsNullOrWhiteSpace(stationCode))
            {
                return JsonMessage("Station code is required", 400);
            }

            if (result is not ("PASS" or "FAIL"))
            {
                return JsonMessage("result must be PASS or FAIL", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var serial = await GetSerialByQueryAsync(connection, query);
                if (serial is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("SN/RSN not found", 404);
                }

                if (string.Equals(serial["serial_status"]?.ToString(), "SCRAP", StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("SN is scrapped. Production actions are blocked until Undo Scrap is completed.", 409);
                }

                var routeRows = await GetRouteRowsForItemAsync(connection, Convert.ToInt32(serial["item_id"]));
                if (routeRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("No route configured for this part number", 400);
                }

                var selected = routeRows.FirstOrDefault(step => string.Equals(step["station_code"]?.ToString(), stationCode, StringComparison.OrdinalIgnoreCase));
                if (selected is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Selected station is not in this part route", 400);
                }

                var currentOrder = ResolveCurrentOrder(serial, routeRows);
                var current = routeRows.FirstOrDefault(step => Convert.ToInt32(step["station_order"]) == currentOrder) ?? routeRows[0];
                if (Convert.ToInt32(selected["station_order"]) != Convert.ToInt32(current["station_order"]))
                {
                    await transaction.RollbackAsync();
                    return JsonMessage($"Current station is {current["station_code"]}. Please select current station first.", 409);
                }

                var nextStep = result == "PASS"
                    ? routeRows.FirstOrDefault(step => Convert.ToInt32(step["station_order"]) > Convert.ToInt32(current["station_order"]))
                    : current;
                var nextStatus = result == "PASS" ? (nextStep is null ? "Completed" : "In Process") : "Failed";
                var nextCondition = result == "PASS" ? "Good" : "NG";
                var nextStationCode = result == "PASS" ? nextStep?["station_code"] : current["station_code"];
                var nextStationOrder = result == "PASS" ? nextStep?["station_order"] : current["station_order"];

                await ExecuteAsync(
                    connection,
                    """
                    UPDATE serial_numbers
                    SET status = @status,
                        condition = @condition,
                        current_station_code = @stationCode,
                        current_station_order = @stationOrder,
                        last_moved_at = NOW(),
                        updated_at = NOW()
                    WHERE id = @id
                    """,
                    ("status", nextStatus),
                    ("condition", nextCondition),
                    ("stationCode", nextStationCode),
                    ("stationOrder", nextStationOrder),
                    ("id", serial["id"]));

                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO serial_station_logs
                      (serial_id, item_id, work_order_id, station_code, station_name, action_result, remark, changed_by,
                       before_station_code, before_station_order, after_station_code, after_station_order,
                       station_length, pc_name, additional_info)
                    VALUES
                      (@serialId, @itemId, @workOrderId, @stationCode, @stationName, @result, @remark, @changedBy,
                       @beforeCode, @beforeOrder, @afterCode, @afterOrder, @stationLength, @pcName, @additionalInfo)
                    """,
                    ("serialId", serial["id"]),
                    ("itemId", serial["item_id"]),
                    ("workOrderId", serial["work_order_id"]),
                    ("stationCode", current["station_code"]),
                    ("stationName", current["station_name"]),
                    ("result", result),
                    ("remark", ToDbNullable(remark)),
                    ("changedBy", changedBy),
                    ("beforeCode", serial["current_station_code"]),
                    ("beforeOrder", serial["current_station_order"]),
                    ("afterCode", nextStationCode),
                    ("afterOrder", nextStationOrder),
                    ("stationLength", ToDbNullable(stationLength)),
                    ("pcName", pcName),
                    ("additionalInfo", additionalInfo));

                var refreshed = await GetSerialByQueryAsync(connection, serial["rsn"]!.ToString()!);
                var trace = await BuildTracePayloadAsync(connection, serial["rsn"]!.ToString()!, refreshed!);
                await transaction.CommitAsync();
                return Results.Json(new { message = result == "PASS" ? "PASS submitted successfully" : "FAIL submitted successfully", action = result, data = trace });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapPost("/api/reports/scrap-sn", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var query = ReadString(payload, "query")?.Trim();
            var reason = ReadString(payload, "reason")?.Trim();
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
            var pcName = ReadString(payload, "pc_name")?.Trim() ?? "WEB-CLIENT";

            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonMessage("SN or RSN is required", 400);
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return JsonMessage("Scrap reason is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);
            await EnsureScrapTrackingColumnsAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var workflowSerial = await GetWorkflowSerialByQueryAsync(connection, query);
                if (workflowSerial is not null)
                {
                    var result = await ScrapWorkflowSerialAsync(connection, workflowSerial, reason, changedBy);
                    await transaction.CommitAsync();
                    return result;
                }

                var serial = await GetSerialByQueryAsync(connection, query);
                if (serial is not null)
                {
                    var result = await ScrapSerialAsync(connection, serial, reason, changedBy, pcName);
                    await transaction.CommitAsync();
                    return result;
                }

                await transaction.RollbackAsync();
                return JsonMessage("SN/RSN not found", 404);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapPost("/api/reports/undo-scrap", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var query = ReadString(payload, "query")?.Trim();
            var reason = ReadString(payload, "reason")?.Trim();
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
            var pcName = ReadString(payload, "pc_name")?.Trim() ?? "WEB-CLIENT";

            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonMessage("SN or RSN is required", 400);
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return JsonMessage("Undo Scrap reason is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);
            await EnsureScrapTrackingColumnsAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var workflowSerial = await GetWorkflowSerialByQueryAsync(connection, query);
                if (workflowSerial is not null)
                {
                    var result = await UndoScrapWorkflowSerialAsync(connection, workflowSerial, reason, changedBy);
                    await transaction.CommitAsync();
                    return result;
                }

                var serial = await GetSerialByQueryAsync(connection, query);
                if (serial is not null)
                {
                    var result = await UndoScrapSerialAsync(connection, serial, reason, changedBy, pcName);
                    await transaction.CommitAsync();
                    return result;
                }

                await transaction.RollbackAsync();
                return JsonMessage("SN/RSN not found", 404);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static async Task<IResult> ScrapSerialAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string reason,
        string changedBy,
        string pcName)
    {
        var status = serial["serial_status"]?.ToString() ?? "New";
        if (string.Equals(status, "SCRAP", StringComparison.OrdinalIgnoreCase))
        {
            return JsonMessage("SN is already marked as SCRAP.", 409);
        }

        var stationCode = serial["current_station_code"]?.ToString() ?? "SCRAP";
        var stationName = await ResolveLegacyStationNameAsync(connection, serial, stationCode);

        await ExecuteAsync(
            connection,
            """
            UPDATE serial_numbers
            SET status = 'SCRAP',
                condition = 'NG',
                scrap_previous_status = @previousStatus,
                scrap_previous_condition = @previousCondition,
                scrap_reason = @reason,
                scrapped_by = @changedBy,
                scrapped_at = NOW(),
                updated_at = NOW()
            WHERE id = @id
            """,
            ("previousStatus", status),
            ("previousCondition", serial["condition"] ?? "Good"),
            ("reason", reason),
            ("changedBy", changedBy),
            ("id", serial["id"]));

        await InsertSerialScrapLogAsync(
            connection,
            serial,
            stationCode,
            stationName,
            "SCRAP",
            reason,
            changedBy,
            pcName,
            $"Scrap mark saved. Previous status: {status}; Reason: {reason}");

        var refreshed = await GetSerialByQueryAsync(connection, serial["rsn"]!.ToString()!);
        return Results.Json(new
        {
            message = "SN marked as SCRAP.",
            action = "SCRAP",
            data = await BuildTracePayloadAsync(connection, serial["rsn"]!.ToString()!, refreshed!)
        });
    }

    private static async Task<IResult> UndoScrapSerialAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string reason,
        string changedBy,
        string pcName)
    {
        var status = serial["serial_status"]?.ToString() ?? "";
        if (!string.Equals(status, "SCRAP", StringComparison.OrdinalIgnoreCase))
        {
            return JsonMessage("Only SCRAP SN can be restored.", 409);
        }

        var previousStatus = serial["scrap_previous_status"]?.ToString();
        if (!string.IsNullOrWhiteSpace(previousStatus) && !string.Equals(previousStatus, "New", StringComparison.OrdinalIgnoreCase))
        {
            return JsonMessage("Only New SN can be returned from scrap.", 409);
        }

        var restoredStatus = string.IsNullOrWhiteSpace(previousStatus) ? "New" : previousStatus;
        var restoredCondition = serial["scrap_previous_condition"]?.ToString();
        if (string.IsNullOrWhiteSpace(restoredCondition))
        {
            restoredCondition = "Good";
        }

        var stationCode = serial["current_station_code"]?.ToString() ?? "UNDO_SCRAP";
        var stationName = await ResolveLegacyStationNameAsync(connection, serial, stationCode);

        await ExecuteAsync(
            connection,
            """
            UPDATE serial_numbers
            SET status = @restoredStatus,
                condition = @restoredCondition,
                scrap_previous_status = NULL,
                scrap_previous_condition = NULL,
                scrap_reason = NULL,
                scrapped_by = NULL,
                scrapped_at = NULL,
                updated_at = NOW()
            WHERE id = @id
            """,
            ("restoredStatus", restoredStatus),
            ("restoredCondition", restoredCondition),
            ("id", serial["id"]));

        await InsertSerialScrapLogAsync(
            connection,
            serial,
            stationCode,
            stationName,
            "UNDO_SCRAP",
            reason,
            changedBy,
            pcName,
            $"Undo Scrap saved. Restored status: {restoredStatus}; Reason: {reason}");

        var refreshed = await GetSerialByQueryAsync(connection, serial["rsn"]!.ToString()!);
        return Results.Json(new
        {
            message = "SCRAP mark removed from SN.",
            action = "UNDO_SCRAP",
            data = await BuildTracePayloadAsync(connection, serial["rsn"]!.ToString()!, refreshed!)
        });
    }

    private static async Task<IResult> ScrapWorkflowSerialAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string reason,
        string changedBy)
    {
        var status = serial["serial_status"]?.ToString() ?? "New";
        if (string.Equals(status, "SCRAP", StringComparison.OrdinalIgnoreCase))
        {
            return JsonMessage("SN is already marked as SCRAP.", 409);
        }

        var stationCode = serial["current_station_code"]?.ToString() ?? "SCRAP";
        var stationName = await ResolveWorkflowStationNameAsync(connection, serial, stationCode);

        await ExecuteAsync(
            connection,
            """
            UPDATE workflow_serial_numbers
            SET status = 'SCRAP',
                condition = 'NG',
                scrap_previous_status = @previousStatus,
                scrap_previous_condition = @previousCondition,
                scrap_reason = @reason,
                scrapped_by = @changedBy,
                scrapped_at = NOW(),
                updated_at = NOW()
            WHERE id = @id
            """,
            ("previousStatus", status),
            ("previousCondition", serial["condition"] ?? "Good"),
            ("reason", reason),
            ("changedBy", changedBy),
            ("id", serial["id"]));

        await InsertWorkflowScrapLogAsync(
            connection,
            serial,
            stationCode,
            stationName,
            "SCRAP",
            reason,
            changedBy,
            $"Scrap mark saved. Previous status: {status}; Reason: {reason}");

        var refreshed = await GetWorkflowSerialByQueryAsync(connection, serial["rsn"]!.ToString()!);
        return Results.Json(new
        {
            message = "SN marked as SCRAP.",
            action = "SCRAP",
            data = await BuildWorkflowTracePayloadAsync(connection, serial["rsn"]!.ToString()!, refreshed!)
        });
    }

    private static async Task<IResult> UndoScrapWorkflowSerialAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string reason,
        string changedBy)
    {
        var status = serial["serial_status"]?.ToString() ?? "";
        if (!string.Equals(status, "SCRAP", StringComparison.OrdinalIgnoreCase))
        {
            return JsonMessage("Only SCRAP SN can be restored.", 409);
        }

        var previousStatus = serial["scrap_previous_status"]?.ToString();
        if (!string.IsNullOrWhiteSpace(previousStatus) && !string.Equals(previousStatus, "New", StringComparison.OrdinalIgnoreCase))
        {
            return JsonMessage("Only New SN can be returned from scrap.", 409);
        }

        var restoredStatus = string.IsNullOrWhiteSpace(previousStatus) ? "New" : previousStatus;
        var restoredCondition = serial["scrap_previous_condition"]?.ToString();
        if (string.IsNullOrWhiteSpace(restoredCondition))
        {
            restoredCondition = "Good";
        }

        var stationCode = serial["current_station_code"]?.ToString() ?? "UNDO_SCRAP";
        var stationName = await ResolveWorkflowStationNameAsync(connection, serial, stationCode);

        await ExecuteAsync(
            connection,
            """
            UPDATE workflow_serial_numbers
            SET status = @restoredStatus,
                condition = @restoredCondition,
                scrap_previous_status = NULL,
                scrap_previous_condition = NULL,
                scrap_reason = NULL,
                scrapped_by = NULL,
                scrapped_at = NULL,
                updated_at = NOW()
            WHERE id = @id
            """,
            ("restoredStatus", restoredStatus),
            ("restoredCondition", restoredCondition),
            ("id", serial["id"]));

        await InsertWorkflowScrapLogAsync(
            connection,
            serial,
            stationCode,
            stationName,
            "UNDO_SCRAP",
            reason,
            changedBy,
            $"Undo Scrap saved. Restored status: {restoredStatus}; Reason: {reason}");

        var refreshed = await GetWorkflowSerialByQueryAsync(connection, serial["rsn"]!.ToString()!);
        return Results.Json(new
        {
            message = "SCRAP mark removed from SN.",
            action = "UNDO_SCRAP",
            data = await BuildWorkflowTracePayloadAsync(connection, serial["rsn"]!.ToString()!, refreshed!)
        });
    }

    private static async Task InsertSerialScrapLogAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string stationCode,
        string stationName,
        string action,
        string reason,
        string changedBy,
        string pcName,
        string additionalInfo)
    {
        await ExecuteAsync(
            connection,
            """
            INSERT INTO serial_station_logs
              (serial_id, item_id, work_order_id, station_code, station_name, action_result, remark, changed_by,
               before_station_code, before_station_order, after_station_code, after_station_order,
               station_length, pc_name, additional_info)
            VALUES
              (@serialId, @itemId, @workOrderId, @stationCode, @stationName, @action, @reason, @changedBy,
               @beforeCode, @beforeOrder, @afterCode, @afterOrder, NULL, @pcName, @additionalInfo)
            """,
            ("serialId", serial["id"]),
            ("itemId", serial["item_id"]),
            ("workOrderId", serial["work_order_id"]),
            ("stationCode", stationCode),
            ("stationName", stationName),
            ("action", action),
            ("reason", reason),
            ("changedBy", changedBy),
            ("beforeCode", serial["current_station_code"]),
            ("beforeOrder", serial["current_station_order"]),
            ("afterCode", serial["current_station_code"]),
            ("afterOrder", serial["current_station_order"]),
            ("pcName", pcName),
            ("additionalInfo", additionalInfo));
    }

    private static async Task InsertWorkflowScrapLogAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string stationCode,
        string stationName,
        string action,
        string reason,
        string changedBy,
        string additionalInfo)
    {
        await ExecuteAsync(
            connection,
            """
            INSERT INTO workflow_serial_station_logs
              (workflow_serial_id, workflow_part_id, workflow_work_order_id, station_code, station_name, action_result,
               remark, changed_by, before_station_code, before_station_order, after_station_code, after_station_order)
            VALUES
              (@serialId, @partId, @workOrderId, @stationCode, @stationName, @action,
               @remark, @changedBy, @beforeCode, @beforeOrder, @afterCode, @afterOrder)
            """,
            ("serialId", serial["id"]),
            ("partId", serial["workflow_part_id"]),
            ("workOrderId", serial["workflow_work_order_id"]),
            ("stationCode", stationCode),
            ("stationName", stationName),
            ("action", action),
            ("remark", additionalInfo),
            ("changedBy", changedBy),
            ("beforeCode", serial["current_station_code"]),
            ("beforeOrder", serial["current_station_order"]),
            ("afterCode", serial["current_station_code"]),
            ("afterOrder", serial["current_station_order"]));
    }

    private static async Task<string> ResolveLegacyStationNameAsync(NpgsqlConnection connection, Dictionary<string, object?> serial, string stationCode)
    {
        var routeRows = await GetRouteRowsForItemAsync(connection, Convert.ToInt32(serial["item_id"]));
        return routeRows.FirstOrDefault(step => string.Equals(step["station_code"]?.ToString(), stationCode, StringComparison.OrdinalIgnoreCase))?["station_name"]?.ToString()
            ?? stationCode;
    }

    private static async Task<string> ResolveWorkflowStationNameAsync(NpgsqlConnection connection, Dictionary<string, object?> serial, string stationCode)
    {
        var routeRows = await GetWorkflowRouteRowsForPartAsync(connection, Convert.ToInt32(serial["workflow_part_id"]));
        return routeRows.FirstOrDefault(step => string.Equals(step["station_code"]?.ToString(), stationCode, StringComparison.OrdinalIgnoreCase))?["station_name"]?.ToString()
            ?? stationCode;
    }

    private static async Task<Dictionary<string, object?>?> GetSerialByQueryAsync(NpgsqlConnection connection, string query)
    {
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT snr.id, snr.sn, snr.rsn, snr.status AS serial_status, snr.condition,
                   snr.current_station_code, snr.current_station_order, snr.last_moved_at,
                   snr.scrap_previous_status, snr.scrap_previous_condition,
                   snr.created_at, snr.updated_at,
                   wo.id AS work_order_id, wo.wo, wo.status AS wo_status, wo.qty AS wo_qty, wo.balance AS wo_balance,
                   i.id AS item_id, i.pn, i.description AS item_description,
                   ir.revision, s.name AS site_name,
                   pl.code AS product_line_code, pl.description AS product_line_name,
                   '' AS plant
            FROM serial_numbers snr
            JOIN work_orders wo ON wo.id = snr.work_order_id
            JOIN items i ON i.id = snr.item_id
            LEFT JOIN item_revisions ir ON ir.id = snr.item_revision_id
            LEFT JOIN sites s ON s.id = snr.site_id
            LEFT JOIN product_lines pl ON pl.id = i.product_line_id
            WHERE UPPER(snr.sn) = UPPER(@query)
               OR UPPER(snr.rsn) = UPPER(@query)
            ORDER BY snr.created_at DESC
            LIMIT 1
            """,
            ("query", query));
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<Dictionary<string, object?>?> GetWorkflowSerialByQueryAsync(NpgsqlConnection connection, string query)
    {
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT snr.id, snr.sn, snr.rsn, snr.status AS serial_status, snr.condition,
                   snr.current_station_code, snr.current_station_order, snr.last_moved_at,
                   snr.scrap_previous_status, snr.scrap_previous_condition,
                   snr.created_at, snr.updated_at,
                   w.id AS workflow_work_order_id, w.wo, w.status AS wo_status, w.qty AS wo_qty,
                   GREATEST(COALESCE(w.qty, 0) - (
                     SELECT COUNT(*)::int
                     FROM workflow_serial_numbers generated
                     WHERE generated.workflow_work_order_id = w.id
                   ), 0) AS wo_balance,
                   p.id AS workflow_part_id, p.pn, p.description AS item_description,
                   COALESCE(w.plant, '') AS plant,
                   COALESCE(w.revision, '-') AS revision,
                   COALESCE(w.site_name, '-') AS site_name,
                   COALESCE(p.item_type, '-') AS product_line_name
            FROM workflow_serial_numbers snr
            JOIN workflow_work_orders w ON w.id = snr.workflow_work_order_id
            JOIN workflow_part_numbers p ON p.id = snr.workflow_part_id
            WHERE UPPER(snr.sn) = UPPER(@query)
               OR UPPER(snr.rsn) = UPPER(@query)
            ORDER BY snr.created_at DESC
            LIMIT 1
            """,
            ("query", query));
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<List<Dictionary<string, object?>>> GetRouteRowsForItemAsync(NpgsqlConnection connection, int itemId)
    {
        await EnsureRoutingStepLoginColumnsAsync(connection);

        return await QueryRowsAsync(
            connection,
            """
            SELECT station_order, station_code, station_name, sample_mode, report_mode,
                   station_login_id, station_login_password, station_ip, printer_ip
            FROM item_routing_steps
            WHERE item_id = @itemId
            ORDER BY station_order ASC, id ASC
            """,
            ("itemId", itemId));
    }

    private static async Task<List<Dictionary<string, object?>>> GetWorkflowRouteRowsForPartAsync(NpgsqlConnection connection, int workflowPartId)
    {
        return await QueryRowsAsync(
            connection,
            """
            SELECT station_order, station_code, station_name, sample_mode, report_mode,
                   station_login_id, station_login_password, station_ip, printer_ip
            FROM workflow_routing_steps
            WHERE workflow_part_id = @workflowPartId
            ORDER BY station_order ASC, id ASC
            """,
            ("workflowPartId", workflowPartId));
    }

    private static int ResolveCurrentOrder(Dictionary<string, object?> serial, List<Dictionary<string, object?>> routeRows)
    {
        if (serial["current_station_order"] is not null)
        {
            return Convert.ToInt32(serial["current_station_order"]);
        }

        var currentCode = serial["current_station_code"]?.ToString();
        var matched = routeRows.FirstOrDefault(row => string.Equals(row["station_code"]?.ToString(), currentCode, StringComparison.Ordinal));
        if (matched is null && string.Equals(serial["serial_status"]?.ToString(), "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(routeRows[^1]["station_order"]);
        }

        return matched is not null ? Convert.ToInt32(matched["station_order"]) : Convert.ToInt32(routeRows[0]["station_order"]);
    }

    private static async Task<Dictionary<string, object?>?> FindLatestFailedPreviousStepAsync(
        NpgsqlConnection connection,
        object workflowSerialId,
        List<Dictionary<string, object?>> routeRows,
        int selectedOrder)
    {
        var previousSteps = routeRows
            .Where(step => Convert.ToInt32(step["station_order"]) < selectedOrder)
            .Where(step => !string.IsNullOrWhiteSpace(step["station_code"]?.ToString()))
            .ToList();

        if (previousSteps.Count == 0)
        {
            return null;
        }

        var previousCodes = previousSteps
            .Select(step => step["station_code"]!.ToString()!)
            .ToArray();

        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT DISTINCT ON (UPPER(station_code)) station_code, action_result
            FROM workflow_serial_station_logs
            WHERE workflow_serial_id = @serialId
              AND station_code = ANY(@stationCodes)
              AND UPPER(action_result) IN ('PASS', 'FAIL')
            ORDER BY UPPER(station_code), created_at DESC, id DESC
            """,
            ("serialId", workflowSerialId),
            ("stationCodes", previousCodes));

        var failedCodes = rows
            .Where(row => string.Equals(row["action_result"]?.ToString(), "FAIL", StringComparison.OrdinalIgnoreCase))
            .Select(row => row["station_code"]?.ToString())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return previousSteps.FirstOrDefault(step => failedCodes.Contains(step["station_code"]?.ToString() ?? string.Empty));
    }

    private static async Task<object> BuildTracePayloadAsync(NpgsqlConnection connection, string query, Dictionary<string, object?> serial)
    {
        var routeRows = await GetRouteRowsForItemAsync(connection, Convert.ToInt32(serial["item_id"]));
        var currentOrder = routeRows.Count == 0 ? 0 : ResolveCurrentOrder(serial, routeRows);
        var assembledParts = await GetSerialAssembledPartsAsync(connection, serial["id"]!);
        var snValues = new List<Dictionary<string, object?>>();
        var history = await QueryRowsAsync(
            connection,
            """
            SELECT id, changed_by AS user_name, created_at AS date_time, station_code AS station,
                   station_length AS length, pc_name, action_result AS result,
                   COALESCE(additional_info, remark, '') AS additional_info
            FROM serial_station_logs
            WHERE serial_id = @serialId
              AND UPPER(action_result) IN ('PASS', 'FAIL', 'SCRAP', 'UNDO_SCRAP')
            ORDER BY created_at DESC, id DESC
            LIMIT 300
            """,
            ("serialId", serial["id"]));
        history.AddRange(await GetSerialAssemblyHistoryRowsAsync(connection, serial["id"]!));
        history.Add(BuildSerialGeneratedHistoryRow(serial));
        history = history
            .OrderByDescending(row => row["date_time"] is DateTime dateTime ? dateTime : DateTime.MinValue)
            .ThenByDescending(row => Convert.ToInt64(row["id"] ?? 0))
            .ToList();

        var routing = routeRows.Select(step =>
        {
            var order = Convert.ToInt32(step["station_order"]);
            var isSerialCompleted = string.Equals(serial["serial_status"]?.ToString(), "Completed", StringComparison.OrdinalIgnoreCase);
            var state = currentOrder == 0
                ? "pending"
                : isSerialCompleted && order <= currentOrder
                    ? "completed"
                    : order < currentOrder ? "completed" : order == currentOrder ? "current" : "pending";
            return new Dictionary<string, object?>
            {
                ["station_order"] = order,
                ["station_code"] = step["station_code"],
                ["station_name"] = step["station_name"],
                ["sample_mode"] = step["sample_mode"],
                ["report_mode"] = step["report_mode"],
                ["state"] = state,
                ["is_current"] = state == "current"
            };
        }).ToList();

        var completed = routing.Count(row => row["state"]?.ToString() == "completed");
        var current = routing.FirstOrDefault(row => row["is_current"] is true);
        var pending = Math.Max(routing.Count - completed - (current is null ? 0 : 1), 0);
        var percent = routing.Count > 0 ? (int)Math.Round(((completed + (current is null ? 0 : 1)) / (double)routing.Count) * 100) : 0;

        return new
        {
            query,
            matched_by = string.Equals(serial["sn"]?.ToString(), query, StringComparison.OrdinalIgnoreCase) ? "SN" : "RSN",
            serial = new
            {
                id = serial["id"],
                sn = serial["sn"],
                rsn = serial["rsn"],
                status = serial["serial_status"],
                condition = serial["condition"],
                current_station_code = current?["station_code"] ?? serial["current_station_code"],
                current_station_name = current?["station_name"],
                current_station_order = current?["station_order"] ?? (currentOrder == 0 ? null : currentOrder),
                created_at = serial["created_at"],
                updated_at = serial["updated_at"],
                last_moved_at = serial["last_moved_at"]
            },
            device = new
            {
                product_line = serial["product_line_name"] ?? serial["product_line_code"] ?? "-",
                pn = serial["pn"],
                revision = serial["revision"] ?? "-",
                work_order = serial["wo"],
                work_order_status = serial["wo_status"],
                work_order_qty = serial["wo_qty"],
                work_order_balance = serial["wo_balance"],
                plant = serial["plant"],
                site = serial["site_name"] ?? "-",
                description = serial["item_description"] ?? "-"
            },
            progress = new { total = routing.Count, completed, current = current is null ? 0 : 1, pending, percent },
            routing,
            history,
            sn_values = snValues,
            assembled_parts = assembledParts,
            generated_at = DateTime.UtcNow
        };
    }

    private static async Task<object> BuildWorkflowTracePayloadAsync(NpgsqlConnection connection, string query, Dictionary<string, object?> serial)
    {
        var routeRows = await GetWorkflowRouteRowsForPartAsync(connection, Convert.ToInt32(serial["workflow_part_id"]));
        var currentOrder = routeRows.Count == 0 ? 0 : ResolveCurrentOrder(serial, routeRows);
        var multiboxRows = await QueryRowsAsync(
            connection,
            """
            SELECT b.box_no, p.pallet_no, s.shipment_no
            FROM workflow_multibox_items i
            JOIN workflow_multiboxes b ON b.id = i.box_id
            LEFT JOIN workflow_pallet_items pi ON pi.box_id = b.id
            LEFT JOIN workflow_pallets p ON p.id = pi.pallet_id
            LEFT JOIN workflow_shipment_items si ON si.pallet_id = p.id
            LEFT JOIN workflow_shipments s ON s.id = si.shipment_id
            WHERE i.workflow_serial_id = @serialId
            ORDER BY i.added_at DESC, i.id DESC
            LIMIT 1
            """,
            ("serialId", serial["id"]));
        var multiboxNo = multiboxRows.Count > 0 ? multiboxRows[0]["box_no"] : null;
        var palletNo = multiboxRows.Count > 0 ? multiboxRows[0]["pallet_no"] : null;
        var shipmentNo = multiboxRows.Count > 0 ? multiboxRows[0]["shipment_no"] : null;
        var assembledParts = await GetWorkflowSerialAssembledPartsAsync(connection, serial["id"]!);
        var snValues = await GetSerialExternalValuesAsync(connection, serial["id"]!);
        var history = await QueryRowsAsync(
            connection,
            """
            SELECT id, changed_by AS user_name, created_at AS date_time, station_code AS station,
                   NULL::text AS length, NULL::text AS pc_name, action_result AS result,
                   COALESCE(remark, '') AS additional_info
            FROM workflow_serial_station_logs
            WHERE workflow_serial_id = @serialId
              AND UPPER(action_result) IN ('PASS', 'FAIL', 'SCRAP', 'UNDO_SCRAP')
            ORDER BY created_at DESC, id DESC
            LIMIT 300
            """,
            ("serialId", serial["id"]));
        history.AddRange(await GetWorkflowBomBindingHistoryRowsAsync(connection, serial["id"]!));
        history.Add(BuildSerialGeneratedHistoryRow(serial));
        history = history
            .OrderByDescending(row => row["date_time"] is DateTime dateTime ? dateTime : DateTime.MinValue)
            .ThenByDescending(row => Convert.ToInt64(row["id"] ?? 0))
            .ToList();

        var routing = routeRows.Select(step =>
        {
            var order = Convert.ToInt32(step["station_order"]);
            var isSerialCompleted = string.Equals(serial["serial_status"]?.ToString(), "Completed", StringComparison.OrdinalIgnoreCase);
            var state = currentOrder == 0
                ? "pending"
                : isSerialCompleted && order <= currentOrder
                    ? "completed"
                    : order < currentOrder ? "completed" : order == currentOrder ? "current" : "pending";
            return new Dictionary<string, object?>
            {
                ["station_order"] = order,
                ["station_code"] = step["station_code"],
                ["station_name"] = step["station_name"],
                ["sample_mode"] = step["sample_mode"],
                ["report_mode"] = step["report_mode"],
                ["station_login_id"] = step["station_login_id"],
                ["state"] = state,
                ["is_current"] = state == "current"
            };
        }).ToList();

        var completed = routing.Count(row => row["state"]?.ToString() == "completed");
        var current = routing.FirstOrDefault(row => row["is_current"] is true);
        var pending = Math.Max(routing.Count - completed - (current is null ? 0 : 1), 0);
        var percent = routing.Count > 0 ? (int)Math.Round(((completed + (current is null ? 0 : 1)) / (double)routing.Count) * 100) : 0;

        return new
        {
            query,
            matched_by = string.Equals(serial["sn"]?.ToString(), query, StringComparison.OrdinalIgnoreCase) ? "SN" : "RSN",
            serial = new
            {
                id = serial["id"],
                sn = serial["sn"],
                rsn = serial["rsn"],
                status = serial["serial_status"],
                condition = serial["condition"],
                current_station_code = current?["station_code"] ?? serial["current_station_code"],
                current_station_name = current?["station_name"],
                current_station_order = current?["station_order"] ?? (currentOrder == 0 ? null : currentOrder),
                created_at = serial["created_at"],
                updated_at = serial["updated_at"],
                last_moved_at = serial["last_moved_at"],
                multibox_no = multiboxNo,
                pallet_no = palletNo,
                shipment_no = shipmentNo
            },
            device = new
            {
                product_line = serial["product_line_name"] ?? "-",
                pn = serial["pn"],
                revision = serial["revision"] ?? "-",
                work_order = serial["wo"],
                work_order_status = serial["wo_status"],
                work_order_qty = serial["wo_qty"],
                work_order_balance = serial["wo_balance"],
                plant = serial["plant"],
                site = serial["site_name"] ?? "-",
                description = serial["item_description"] ?? "-"
            },
            progress = new { total = routing.Count, completed, current = current is null ? 0 : 1, pending, percent },
            routing,
            history,
            sn_values = snValues,
            assembled_parts = assembledParts,
            generated_at = DateTime.UtcNow
        };
    }

    private static async Task<List<Dictionary<string, object?>>> GetSerialAssembledPartsAsync(
        NpgsqlConnection connection,
        object serialId)
    {
        return await QueryRowsAsync(
            connection,
            """
            SELECT child_item.pn,
                   child.sn AS son_sn,
                   COALESCE(pt.code, pt.type, '') AS pn_type,
                   l.station_code,
                   ''::text AS station_name
            FROM serial_assembly_links l
            JOIN serial_numbers child ON child.id = l.child_serial_id
            JOIN items child_item ON child_item.id = child.item_id
            LEFT JOIN pn_types pt ON pt.id = child_item.pn_type_id
            WHERE l.parent_serial_id = @serialId
            ORDER BY l.created_at DESC, l.id DESC
            """,
            ("serialId", serialId));
    }

    private static async Task<List<Dictionary<string, object?>>> GetSerialAssemblyHistoryRowsAsync(
        NpgsqlConnection connection,
        object serialId)
    {
        return await QueryRowsAsync(
            connection,
            """
            SELECT l.id,
                   l.created_by AS user_name,
                   l.created_at AS date_time,
                   l.station_code AS station,
                   NULL::text AS length,
                   NULL::text AS pc_name,
                   ''::text AS result,
                   CASE
                     WHEN l.parent_serial_id = @serialId
                       THEN 'This father SN ' || parent_sn.sn || ' is binded with child SN ' || child.sn || ' (' || child_item.pn || ')'
                     ELSE 'This SN ' || child.sn || ' is binded to father SN ' || parent_sn.sn || ' (' || parent_item.pn || ' / ' || COALESCE(parent_rev.revision, '-') || ')'
                   END AS additional_info,
                   'BOM_BIND'::text AS event_type,
                   child.sn AS child_sn,
                   child.rsn AS child_rsn,
                   child_item.pn AS child_pn,
                   COALESCE(child_rev.revision, '-') AS child_revision,
                   parent_sn.sn AS parent_sn,
                   parent_sn.rsn AS parent_rsn,
                   parent_item.pn AS parent_pn,
                   COALESCE(parent_rev.revision, '-') AS parent_revision
            FROM serial_assembly_links l
            JOIN serial_numbers parent_sn ON parent_sn.id = l.parent_serial_id
            JOIN items parent_item ON parent_item.id = parent_sn.item_id
            LEFT JOIN item_revisions parent_rev ON parent_rev.id = parent_sn.item_revision_id
            JOIN serial_numbers child ON child.id = l.child_serial_id
            JOIN items child_item ON child_item.id = child.item_id
            LEFT JOIN item_revisions child_rev ON child_rev.id = child.item_revision_id
            WHERE l.parent_serial_id = @serialId
               OR l.child_serial_id = @serialId
            ORDER BY l.created_at DESC, l.id DESC
            LIMIT 300
            """,
            ("serialId", serialId));
    }

    private static async Task<List<Dictionary<string, object?>>> GetWorkflowSerialAssembledPartsAsync(
        NpgsqlConnection connection,
        object workflowSerialId)
    {
        return await QueryRowsAsync(
            connection,
            """
            SELECT child_part.pn,
                   child.sn AS son_sn,
                   COALESCE(pt.code, pt.type, '') AS pn_type,
                   l.station_code,
                   COALESCE(l.station_name, child_bom.station_name, '') AS station_name
            FROM workflow_serial_bom_bindings l
            JOIN workflow_serial_numbers child ON child.id = l.child_workflow_serial_id
            JOIN workflow_part_numbers child_part ON child_part.id = child.workflow_part_id
            LEFT JOIN workflow_bom_children child_bom ON child_bom.id = l.workflow_bom_child_id
            LEFT JOIN pn_types pt ON pt.id = child_part.pn_type_id
            WHERE l.parent_workflow_serial_id = @serialId
            ORDER BY l.created_at DESC, l.id DESC
            """,
            ("serialId", workflowSerialId));
    }

    private static async Task<List<Dictionary<string, object?>>> GetWorkflowBomBindingHistoryRowsAsync(
        NpgsqlConnection connection,
        object workflowSerialId)
    {
        return await QueryRowsAsync(
            connection,
            """
            SELECT l.id,
                   l.created_by AS user_name,
                   l.created_at AS date_time,
                   l.station_code AS station,
                   NULL::text AS length,
                   NULL::text AS pc_name,
                   ''::text AS result,
                   CASE
                     WHEN l.parent_workflow_serial_id = @serialId
                       THEN 'Bound child ' || child.rsn || ' (' || child_part.pn || ')'
                     ELSE 'Bound to parent ' || parent_sn.rsn || ' (' || parent_part.pn || ')'
                   END AS additional_info,
                   'BOM_BIND'::text AS event_type,
                   child.sn AS child_sn,
                   child.rsn AS child_rsn,
                   child_part.pn AS child_pn,
                   COALESCE(child_wo.revision, '-') AS child_revision,
                   parent_sn.sn AS parent_sn,
                   parent_sn.rsn AS parent_rsn,
                   parent_part.pn AS parent_pn,
                   COALESCE(parent_wo.revision, '-') AS parent_revision
            FROM workflow_serial_bom_bindings l
            JOIN workflow_serial_numbers parent_sn ON parent_sn.id = l.parent_workflow_serial_id
            JOIN workflow_part_numbers parent_part ON parent_part.id = parent_sn.workflow_part_id
            LEFT JOIN workflow_work_orders parent_wo ON parent_wo.id = parent_sn.workflow_work_order_id
            JOIN workflow_serial_numbers child ON child.id = l.child_workflow_serial_id
            JOIN workflow_part_numbers child_part ON child_part.id = child.workflow_part_id
            LEFT JOIN workflow_work_orders child_wo ON child_wo.id = child.workflow_work_order_id
            WHERE l.parent_workflow_serial_id = @serialId
               OR l.child_workflow_serial_id = @serialId
            ORDER BY l.created_at DESC, l.id DESC
            LIMIT 300
            """,
            ("serialId", workflowSerialId));
    }

    private static Dictionary<string, object?> BuildSerialGeneratedHistoryRow(Dictionary<string, object?> serial)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = 0,
            ["user_name"] = "system",
            ["date_time"] = serial["created_at"],
            ["station"] = serial["current_station_code"],
            ["length"] = null,
            ["pc_name"] = null,
            ["result"] = "",
            ["additional_info"] = "SN generated",
            ["event_type"] = "SN_GENERATED"
        };
    }

    private static async Task EnsureSerialExternalValuesTableAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.serial_external_values (
              id BIGSERIAL PRIMARY KEY,
              workflow_serial_id BIGINT NOT NULL REFERENCES workflow_serial_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_name VARCHAR(220),
              chip_id VARCHAR(220),
              imes VARCHAR(220),
              pushed_by VARCHAR(160) NOT NULL,
              pushed_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_serial_external_values_station UNIQUE (workflow_serial_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_serial_external_values_serial ON public.serial_external_values (workflow_serial_id)");
    }

    private static async Task<List<Dictionary<string, object?>>> GetSerialExternalValuesAsync(
        NpgsqlConnection connection,
        object workflowSerialId)
    {
        return await QueryRowsAsync(
            connection,
            """
            SELECT station_code, station_name, chip_id, imes, pushed_by, pushed_at, updated_at
            FROM public.serial_external_values
            WHERE workflow_serial_id = @serialId
            ORDER BY updated_at DESC, id DESC
            """,
            ("serialId", workflowSerialId));
    }

    private static async Task EnsureWorkflowSchemaAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_part_numbers (
              id SERIAL PRIMARY KEY,
              pn VARCHAR(120) NOT NULL UNIQUE,
              description TEXT NOT NULL DEFAULT '',
              sgd_control BOOLEAN NOT NULL DEFAULT FALSE,
              item_type VARCHAR(40),
              sn_type_id INTEGER REFERENCES sn_types(id) ON DELETE SET NULL,
              sn_type_name VARCHAR(160),
              pn_type_id INTEGER REFERENCES pn_types(id) ON DELETE SET NULL,
              box_qty INTEGER CHECK (box_qty IS NULL OR box_qty > 0),
              part_attribute_key VARCHAR(80),
              part_attribute_value TEXT,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_part_numbers ADD COLUMN IF NOT EXISTS box_qty INTEGER CHECK (box_qty IS NULL OR box_qty > 0)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_part_numbers ADD COLUMN IF NOT EXISTS part_attribute_key VARCHAR(80)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_part_numbers ADD COLUMN IF NOT EXISTS part_attribute_value TEXT");

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_work_orders (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              wo VARCHAR(120) NOT NULL UNIQUE,
              plant VARCHAR(120),
              site_id INTEGER,
              site_name VARCHAR(160),
              due_date DATE,
              qty INTEGER CHECK (qty IS NULL OR qty > 0),
              status VARCHAR(30) NOT NULL DEFAULT 'Released',
              revision VARCHAR(80),
              lot VARCHAR(120),
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_routing_steps (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
                      station_order INTEGER NOT NULL,
                      station_code VARCHAR(80) NOT NULL,
                      station_name VARCHAR(220) NOT NULL,
                      sample_mode VARCHAR(20) NOT NULL DEFAULT 'Full',
                      report_mode VARCHAR(20) NOT NULL DEFAULT 'Regular',
                      preview_status VARCHAR(30),
              station_login_id VARCHAR(160),
              station_login_password VARCHAR(220),
              station_ip VARCHAR(80),
              printer_ip VARCHAR(80),
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_route_station UNIQUE (workflow_part_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_routing_steps ADD COLUMN IF NOT EXISTS preview_status VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_routing_steps ADD COLUMN IF NOT EXISTS station_login_id VARCHAR(160)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_routing_steps ADD COLUMN IF NOT EXISTS station_login_password VARCHAR(220)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_routing_steps ADD COLUMN IF NOT EXISTS station_ip VARCHAR(80)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_routing_steps ADD COLUMN IF NOT EXISTS printer_ip VARCHAR(80)");
        await ExecuteAsync(
            connection,
            """
            ALTER TABLE public.workflow_routing_steps
              ALTER COLUMN station_login_id DROP NOT NULL,
              ALTER COLUMN station_login_password DROP NOT NULL
            """);
        await ExecuteAsync(
            connection,
            """
            ALTER TABLE public.workflow_routing_steps
              DROP CONSTRAINT IF EXISTS chk_workflow_route_login_required
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_bom_children (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              son_pn VARCHAR(120) NOT NULL,
              son_description TEXT NOT NULL DEFAULT '',
              station_code VARCHAR(80),
              station_name VARCHAR(220),
              item_type VARCHAR(40),
              qty INTEGER NOT NULL CHECK (qty > 0),
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_bom_children DROP COLUMN IF EXISTS pn_type");

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_station_rules (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_name VARCHAR(220),
              rule_order INTEGER NOT NULL DEFAULT 10,
              rule_text TEXT NOT NULL,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_station_rule UNIQUE (workflow_part_id, station_code, rule_order)
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_preview_station_statuses (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              status VARCHAR(30) NOT NULL,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_preview_status UNIQUE (workflow_part_id, station_code)
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_station_label_printing (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_id INTEGER,
              station_name VARCHAR(220),
              label_code VARCHAR(120),
              label_description TEXT,
              printer_id VARCHAR(160),
              printer_name VARCHAR(220),
              ip_address VARCHAR(80),
              port VARCHAR(20),
              status VARCHAR(30),
              is_label_printing_enabled BOOLEAN NOT NULL DEFAULT FALSE,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_station_label_printing UNIQUE (workflow_part_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_label_printing ADD COLUMN IF NOT EXISTS is_label_printing_enabled BOOLEAN NOT NULL DEFAULT FALSE");

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_station_weighing (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_id INTEGER,
              station_name VARCHAR(220),
              minimum_weight VARCHAR(80),
              maximum_weight VARCHAR(80),
              tolerance VARCHAR(80),
              is_weighing_enabled BOOLEAN NOT NULL DEFAULT FALSE,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_station_weighing UNIQUE (workflow_part_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_weighing ADD COLUMN IF NOT EXISTS is_weighing_enabled BOOLEAN NOT NULL DEFAULT FALSE");

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_station_sampling (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_id INTEGER,
              station_name VARCHAR(220),
              sampling_type VARCHAR(30) NOT NULL DEFAULT 'PERIODIC',
              interval_qty INTEGER NOT NULL DEFAULT 10,
              sample_qty INTEGER NOT NULL DEFAULT 1,
              lot_size INTEGER NOT NULL DEFAULT 1000,
              is_sampling_enabled BOOLEAN NOT NULL DEFAULT FALSE,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_station_sampling UNIQUE (workflow_part_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_sampling ADD COLUMN IF NOT EXISTS station_id INTEGER");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_sampling ADD COLUMN IF NOT EXISTS is_sampling_enabled BOOLEAN NOT NULL DEFAULT FALSE");

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_station_repair (
              id SERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_id INTEGER,
              station_name VARCHAR(220),
              repair_station_name VARCHAR(220),
              is_repair_station_enabled BOOLEAN NOT NULL DEFAULT FALSE,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_station_repair UNIQUE (workflow_part_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_repair ADD COLUMN IF NOT EXISTS station_id INTEGER");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_repair ADD COLUMN IF NOT EXISTS repair_station_name VARCHAR(220)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_repair ADD COLUMN IF NOT EXISTS is_repair_station_enabled BOOLEAN NOT NULL DEFAULT FALSE");

        await ExecuteAsync(connection, "CREATE SEQUENCE IF NOT EXISTS public.workflow_rsn_seq START WITH 1");
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_serial_numbers (
              id BIGSERIAL PRIMARY KEY,
              sn VARCHAR(220) NOT NULL,
              rsn VARCHAR(40) NOT NULL UNIQUE DEFAULT ('RSN' || LPAD(nextval('workflow_rsn_seq')::text, 10, '0')),
              workflow_work_order_id INTEGER NOT NULL REFERENCES workflow_work_orders(id) ON DELETE CASCADE,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              sn_type_id INTEGER REFERENCES sn_types(id) ON DELETE SET NULL,
              generated_index INTEGER NOT NULL,
              status VARCHAR(30) NOT NULL DEFAULT 'New',
              condition VARCHAR(30) NOT NULL DEFAULT 'Good',
              current_station_code VARCHAR(80),
              current_station_order INTEGER,
              last_moved_at TIMESTAMP,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_serial_index UNIQUE (workflow_work_order_id, generated_index)
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_serial_station_logs (
              id BIGSERIAL PRIMARY KEY,
              workflow_serial_id BIGINT NOT NULL REFERENCES workflow_serial_numbers(id) ON DELETE CASCADE,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              workflow_work_order_id INTEGER NOT NULL REFERENCES workflow_work_orders(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_name VARCHAR(220),
              action_result VARCHAR(10) NOT NULL,
              remark TEXT,
              debug_remark TEXT,
              changed_by VARCHAR(100) NOT NULL DEFAULT 'system',
              before_station_code VARCHAR(80),
              before_station_order INTEGER,
              after_station_code VARCHAR(80),
              after_station_order INTEGER,
              created_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_multiboxes (
              id BIGSERIAL PRIMARY KEY,
              box_no VARCHAR(80) NOT NULL UNIQUE,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              workflow_work_order_id INTEGER REFERENCES workflow_work_orders(id) ON DELETE SET NULL,
              status VARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN', 'CLOSED')),
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              closed_at TIMESTAMP
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_multibox_items (
              id BIGSERIAL PRIMARY KEY,
              box_id BIGINT NOT NULL REFERENCES workflow_multiboxes(id) ON DELETE CASCADE,
              workflow_serial_id BIGINT NOT NULL REFERENCES workflow_serial_numbers(id) ON DELETE RESTRICT,
              added_by VARCHAR(100) NOT NULL DEFAULT 'system',
              added_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_multibox_serial UNIQUE (workflow_serial_id),
              CONSTRAINT uq_workflow_multibox_item UNIQUE (box_id, workflow_serial_id)
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_pallets (
              id BIGSERIAL PRIMARY KEY,
              pallet_no VARCHAR(80) NOT NULL UNIQUE,
              workflow_part_id INTEGER REFERENCES workflow_part_numbers(id) ON DELETE SET NULL,
              workflow_work_order_id INTEGER REFERENCES workflow_work_orders(id) ON DELETE SET NULL,
              status VARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN', 'CLOSED', 'SHIPPED')),
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              closed_at TIMESTAMP
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_pallet_items (
              id BIGSERIAL PRIMARY KEY,
              pallet_id BIGINT NOT NULL REFERENCES workflow_pallets(id) ON DELETE CASCADE,
              box_id BIGINT NOT NULL REFERENCES workflow_multiboxes(id) ON DELETE RESTRICT,
              added_by VARCHAR(100) NOT NULL DEFAULT 'system',
              added_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_pallet_box UNIQUE (box_id),
              CONSTRAINT uq_workflow_pallet_item UNIQUE (pallet_id, box_id)
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_shipments (
              id BIGSERIAL PRIMARY KEY,
              shipment_no VARCHAR(80) NOT NULL UNIQUE,
              status VARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN', 'CLOSED', 'SHIPPED')),
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              shipped_at TIMESTAMP
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_shipment_items (
              id BIGSERIAL PRIMARY KEY,
              shipment_id BIGINT NOT NULL REFERENCES workflow_shipments(id) ON DELETE CASCADE,
              pallet_id BIGINT NOT NULL REFERENCES workflow_pallets(id) ON DELETE RESTRICT,
              added_by VARCHAR(100) NOT NULL DEFAULT 'system',
              added_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_shipment_pallet UNIQUE (pallet_id),
              CONSTRAINT uq_workflow_shipment_item UNIQUE (shipment_id, pallet_id)
            )
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_serial_bom_bindings (
              id BIGSERIAL PRIMARY KEY,
              parent_workflow_serial_id BIGINT NOT NULL REFERENCES workflow_serial_numbers(id) ON DELETE CASCADE,
              child_workflow_serial_id BIGINT NOT NULL REFERENCES workflow_serial_numbers(id) ON DELETE RESTRICT,
              workflow_bom_child_id INTEGER NOT NULL REFERENCES workflow_bom_children(id) ON DELETE RESTRICT,
              station_code VARCHAR(80) NOT NULL,
              station_name VARCHAR(220),
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_bom_child_serial UNIQUE (child_workflow_serial_id),
              CONSTRAINT uq_workflow_bom_parent_child_station UNIQUE (parent_workflow_serial_id, child_workflow_serial_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_work_orders_part ON public.workflow_work_orders (workflow_part_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_route_part ON public.workflow_routing_steps (workflow_part_id, station_order)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_bom_part ON public.workflow_bom_children (workflow_part_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_rules_part ON public.workflow_station_rules (workflow_part_id, station_code)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_label_printing_part ON public.workflow_station_label_printing (workflow_part_id, station_code)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_station_repair_part ON public.workflow_station_repair (workflow_part_id, station_code)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_serials_wo ON public.workflow_serial_numbers (workflow_work_order_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_serials_part ON public.workflow_serial_numbers (workflow_part_id)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_status VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_condition VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrap_reason TEXT");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrapped_by VARCHAR(100)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrapped_at TIMESTAMP");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_bom_bind_parent_station ON public.workflow_serial_bom_bindings (parent_workflow_serial_id, station_code)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_bom_bind_child ON public.workflow_serial_bom_bindings (child_workflow_serial_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_station_logs_serial ON public.workflow_serial_station_logs (workflow_serial_id, created_at DESC)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_station_logs_station ON public.workflow_serial_station_logs (workflow_part_id, station_code)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_station_logs ADD COLUMN IF NOT EXISTS debug_remark TEXT");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_multiboxes_open ON public.workflow_multiboxes (workflow_part_id, workflow_work_order_id, status)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_pallet_items_pallet ON public.workflow_pallet_items (pallet_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_shipment_items_shipment ON public.workflow_shipment_items (shipment_id)");
    }

    private static async Task EnsureRoutingStepLoginColumnsAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_login_id VARCHAR(160)");
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_login_password VARCHAR(220)");
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_ip VARCHAR(80)");
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS printer_ip VARCHAR(80)");
    }

    private static async Task EnsureSerialTrackingSchemaAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, "CREATE SEQUENCE IF NOT EXISTS serial_rsn_seq START WITH 1");
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS serial_numbers (
              id BIGSERIAL PRIMARY KEY,
              sn VARCHAR(220) NOT NULL,
              rsn VARCHAR(40) NOT NULL UNIQUE DEFAULT ('RSN' || LPAD(nextval('serial_rsn_seq')::text, 10, '0')),
              work_order_id INTEGER NOT NULL REFERENCES work_orders(id) ON DELETE CASCADE,
              item_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
              item_revision_id INTEGER REFERENCES item_revisions(id) ON DELETE SET NULL,
              site_id INTEGER REFERENCES sites(id) ON DELETE SET NULL,
              status VARCHAR(30) NOT NULL DEFAULT 'New',
              condition VARCHAR(30) NOT NULL DEFAULT 'Good',
              current_station_code VARCHAR(80),
              current_station_order INTEGER,
              last_moved_at TIMESTAMP,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_serial_numbers_sn_upper ON serial_numbers (UPPER(sn))");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_serial_numbers_wo ON serial_numbers (work_order_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_serial_numbers_item ON serial_numbers (item_id)");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_status VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_condition VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_reason TEXT");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrapped_by VARCHAR(100)");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrapped_at TIMESTAMP");
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS serial_station_logs (
              id BIGSERIAL PRIMARY KEY,
              serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE CASCADE,
              item_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
              work_order_id INTEGER NOT NULL REFERENCES work_orders(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_name VARCHAR(220),
              action_result VARCHAR(10) NOT NULL,
              remark TEXT,
              changed_by VARCHAR(100) NOT NULL DEFAULT 'system',
              before_station_code VARCHAR(80),
              before_station_order INTEGER,
              after_station_code VARCHAR(80),
              after_station_order INTEGER,
              created_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
        await ExecuteAsync(
            connection,
            """
            ALTER TABLE serial_station_logs
            ADD COLUMN IF NOT EXISTS station_length VARCHAR(40),
            ADD COLUMN IF NOT EXISTS pc_name VARCHAR(160),
            ADD COLUMN IF NOT EXISTS additional_info TEXT
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS serial_assembly_links (
              id BIGSERIAL PRIMARY KEY,
              parent_serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE CASCADE,
              child_serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE RESTRICT,
              station_code VARCHAR(80) NOT NULL,
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_serial_assembly_child UNIQUE (child_serial_id),
              CONSTRAINT uq_serial_assembly_parent_child_station UNIQUE (parent_serial_id, child_serial_id, station_code)
            )
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS packing_packages (
              id BIGSERIAL PRIMARY KEY,
              package_no VARCHAR(60) NOT NULL,
              package_type VARCHAR(20) NOT NULL CHECK (package_type IN ('BOX', 'SHIPMENT')),
              status VARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN', 'CLOSED', 'SHIPPED')),
              remark TEXT,
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              closed_by VARCHAR(100),
              closed_at TIMESTAMP,
              shipped_by VARCHAR(100),
              shipped_at TIMESTAMP,
              CONSTRAINT uq_packing_package_no UNIQUE (package_no)
            )
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS packing_package_items (
              id BIGSERIAL PRIMARY KEY,
              package_id BIGINT NOT NULL REFERENCES packing_packages(id) ON DELETE CASCADE,
              serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE RESTRICT,
              added_by VARCHAR(100) NOT NULL DEFAULT 'system',
              added_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_packing_pkg_serial UNIQUE (package_id, serial_id),
              CONSTRAINT uq_packing_serial UNIQUE (serial_id)
            )
            """);
    }

    private static async Task EnsureScrapTrackingColumnsAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_status VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_condition VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_reason TEXT");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrapped_by VARCHAR(100)");
        await ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrapped_at TIMESTAMP");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_status VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_condition VARCHAR(30)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrap_reason TEXT");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrapped_by VARCHAR(100)");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers ADD COLUMN IF NOT EXISTS scrapped_at TIMESTAMP");
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

    private static IResult JsonMessage(string message, int statusCode) => ApiResults.JsonMessage(message, statusCode);

    private static object? ToDbNullable<T>(T? value) => value is null ? DBNull.Value : value;
}

