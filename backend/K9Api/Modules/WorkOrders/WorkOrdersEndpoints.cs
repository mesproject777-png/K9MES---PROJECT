using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

public static class WorkOrdersEndpoints
{
    public static void MapWorkOrders(WebApplication app)
    {
        app.MapGet("/api/work-orders/sites", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            return Results.Json(await QueryRowsAsync(connection, "SELECT id, name FROM sites ORDER BY name ASC"));
        });

        app.MapGet("/api/work-orders", async (HttpRequest request) =>
        {
            var page = ParsePositiveInt(request.Query["page"], 1);
            var limit = Math.Min(ParsePositiveInt(request.Query["limit"], 15), 500);
            var wo = request.Query["wo"].ToString().Trim();
            var pn = request.Query["pn"].ToString().Trim();
            var offset = (page - 1) * limit;
            var where = new List<string>();
            var parameters = new List<(string Name, object? Value)>();
            if (!string.IsNullOrWhiteSpace(wo))
            {
                where.Add("w.wo ILIKE @wo");
                parameters.Add(("wo", $"%{wo}%"));
            }

            if (!string.IsNullOrWhiteSpace(pn))
            {
                where.Add("i.pn ILIKE @pn");
                parameters.Add(("pn", $"%{pn}%"));
            }

            parameters.Add(("limit", limit));
            parameters.Add(("offset", offset));
            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                $"""
                SELECT w.id, w.wo, s.name AS site_name, pl.description AS pl_desc, w.due_date, w.qty, w.status,
                       i.pn, ir.revision, w.balance, w.lot, COUNT(*) OVER () AS total_count
                FROM work_orders w
                JOIN sites s ON s.id = w.site_id
                JOIN items i ON i.id = w.item_id
                JOIN item_revisions ir ON ir.id = w.item_revision_id
                LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
                ORDER BY w.created_at DESC, w.id DESC
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

        app.MapPost("/api/work-orders", async (HttpContext context) => await SaveWorkOrderAsync(context, null));
        app.MapPut("/api/work-orders/{id:int}", async (int id, HttpContext context) => await SaveWorkOrderAsync(context, id));
        app.MapPost("/api/work-orders/transfer", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var sourceWo = ReadString(payload, "source_wo")?.Trim();
            var targetWo = ReadString(payload, "target_wo")?.Trim();
            var mode = ReadString(payload, "mode")?.Trim().ToLowerInvariant();
            var serialInput = ReadString(payload, "sn")?.Trim();
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";

            if (string.IsNullOrWhiteSpace(sourceWo) || string.IsNullOrWhiteSpace(targetWo))
            {
                return JsonMessage("source_wo and target_wo are required", 400);
            }

            if (sourceWo == targetWo)
            {
                return JsonMessage("Source and target WO cannot be same", 400);
            }

            if (mode is not ("all-new" or "single"))
            {
                return JsonMessage("mode must be all-new or single", 400);
            }

            if (mode == "single" && string.IsNullOrWhiteSpace(serialInput))
            {
                return JsonMessage("sn is required for single transfer", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var sourceRows = await QueryRowsAsync(connection, "SELECT * FROM work_orders WHERE wo = @wo FOR UPDATE", ("wo", sourceWo));
                var targetRows = await QueryRowsAsync(connection, "SELECT * FROM work_orders WHERE wo = @wo FOR UPDATE", ("wo", targetWo));
                if (sourceRows.Count == 0 || targetRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage(sourceRows.Count == 0 ? "Source WO not found" : "Target WO not found", 404);
                }

                var targetBalance = Convert.ToInt32(targetRows[0]["balance"] ?? 0);
                if (targetBalance <= 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Target WO has no available balance", 400);
                }

                var serials = mode == "single"
                    ? await QueryRowsAsync(
                        connection,
                        """
                        SELECT id, sn, rsn
                        FROM serial_numbers
                        WHERE work_order_id = @workOrderId
                          AND UPPER(status) = 'NEW'
                          AND (UPPER(sn) = UPPER(@serial) OR UPPER(rsn) = UPPER(@serial))
                        ORDER BY id ASC
                        LIMIT 1
                        FOR UPDATE
                        """,
                        ("workOrderId", sourceRows[0]["id"]),
                        ("serial", serialInput))
                    : await QueryRowsAsync(
                        connection,
                        """
                        SELECT id, sn, rsn
                        FROM serial_numbers
                        WHERE work_order_id = @workOrderId
                          AND UPPER(status) = 'NEW'
                        ORDER BY id ASC
                        LIMIT @limit
                        FOR UPDATE
                        """,
                        ("workOrderId", sourceRows[0]["id"]),
                        ("limit", targetBalance));
                if (serials.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage(mode == "single" ? "SN not found in source WO or status is not New" : "No New SNs available in source WO for transfer", 400);
                }

                var serialIds = serials.Select(row => Convert.ToInt64(row["id"])).ToArray();
                await using (var updateSerialCommand = new NpgsqlCommand(
                    """
                    UPDATE serial_numbers
                    SET work_order_id = @targetId,
                        item_id = @itemId,
                        item_revision_id = @revisionId,
                        site_id = @siteId,
                        updated_at = NOW()
                    WHERE id = ANY(@serialIds)
                    """,
                    connection))
                {
                    updateSerialCommand.Parameters.AddWithValue("targetId", targetRows[0]["id"]!);
                    updateSerialCommand.Parameters.AddWithValue("itemId", targetRows[0]["item_id"]!);
                    updateSerialCommand.Parameters.AddWithValue("revisionId", targetRows[0]["item_revision_id"]!);
                    updateSerialCommand.Parameters.AddWithValue("siteId", targetRows[0]["site_id"]!);
                    updateSerialCommand.Parameters.AddWithValue("serialIds", serialIds);
                    await updateSerialCommand.ExecuteNonQueryAsync();
                }

                var count = serials.Count;
                await ExecuteAsync(connection, "UPDATE work_orders SET balance = balance + @count, updated_at = NOW() WHERE id = @id", ("count", count), ("id", sourceRows[0]["id"]));
                await ExecuteAsync(connection, "UPDATE work_orders SET balance = balance - @count, updated_at = NOW() WHERE id = @id", ("count", count), ("id", targetRows[0]["id"]));
                await transaction.CommitAsync();
                return Results.Json(new
                {
                    success = true,
                    source_wo = sourceWo,
                    target_wo = targetWo,
                    transferred_count = count,
                    serials = serials.Select(row => row["sn"]).ToArray()
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static async Task<IResult> SaveWorkOrderAsync(HttpContext context, int? workOrderId)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        var wo = ReadString(payload, "wo")?.Trim();
        var siteId = ReadInt(payload, "site_id");
        var dueDate = ReadString(payload, "due_date")?.Trim();
        var qty = ReadInt(payload, "qty");
        var status = ReadString(payload, "status")?.Trim();
        var pn = ReadString(payload, "pn")?.Trim();
        var itemRevisionId = ReadInt(payload, "item_revision_id");
        var revision = ReadString(payload, "revision")?.Trim();
        var lot = ReadString(payload, "lot")?.Trim();
        var changedBy = ReadString(payload, "changed_by") ?? "system";

        if (string.IsNullOrWhiteSpace(wo))
        {
            return JsonMessage("WO is required", 400);
        }

        if (siteId is null)
        {
            return JsonMessage("Site is required", 400);
        }

        if (string.IsNullOrWhiteSpace(dueDate))
        {
            return JsonMessage("Due Date is required", 400);
        }

        if (qty is null or <= 0)
        {
            return JsonMessage("Quantity must be a positive number", 400);
        }

        if (status is not ("Allocated" or "Planned" or "Released" or "Cancelled" or "Closed"))
        {
            return JsonMessage("Invalid status", 400);
        }

        if (string.IsNullOrWhiteSpace(pn))
        {
            return JsonMessage("PN is required", 400);
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            Dictionary<string, object?>? existing = null;
            if (workOrderId is not null)
            {
                var existingRows = await QueryRowsAsync(connection, "SELECT * FROM work_orders WHERE id = @id FOR UPDATE", ("id", workOrderId.Value));
                if (existingRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Work order not found", 404);
                }

                existing = existingRows[0];
            }

            if (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sites WHERE id = @id", ("id", siteId.Value)) == 0)
            {
                await transaction.RollbackAsync();
                return JsonMessage("Site not found", 400);
            }

            var item = await FindItemByPnAsync(connection, pn);
            if (item is null)
            {
                await transaction.RollbackAsync();
                return JsonMessage("PN not found", 400);
            }

            if (itemRevisionId is null)
            {
                if (string.IsNullOrWhiteSpace(revision))
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Revision is required", 400);
                }

                var revisionRows = await QueryRowsAsync(
                    connection,
                    """
                    SELECT id
                    FROM item_revisions
                    WHERE item_id = @itemId
                      AND revision = @revision
                      AND (expire_date IS NULL OR expire_date >= CURRENT_DATE)
                    ORDER BY in_date DESC, id DESC
                    LIMIT 1
                    """,
                    ("itemId", item["id"]),
                    ("revision", revision));
                if (revisionRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Revision not found for this PN", 400);
                }

                itemRevisionId = Convert.ToInt32(revisionRows[0]["id"]);
            }
            else if (await ScalarAsync<long>(
                connection,
                "SELECT COUNT(*) FROM item_revisions WHERE id = @revisionId AND item_id = @itemId",
                ("revisionId", itemRevisionId.Value),
                ("itemId", item["id"])) == 0)
            {
                await transaction.RollbackAsync();
                return JsonMessage("Revision not found for this PN", 400);
            }

            var lotValue = string.IsNullOrWhiteSpace(lot) ? null : lot;
            List<Dictionary<string, object?>> rows;
            if (workOrderId is null)
            {
                rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO work_orders (wo, site_id, due_date, qty, status, item_id, item_revision_id, lot, balance)
                    VALUES (@wo, @siteId, @dueDate::date, @qty, @status, @itemId, @itemRevisionId, @lot, @qty)
                    RETURNING *
                    """,
                    ("wo", wo),
                    ("siteId", siteId.Value),
                    ("dueDate", dueDate),
                    ("qty", qty.Value),
                    ("status", status),
                    ("itemId", item["id"]),
                    ("itemRevisionId", itemRevisionId.Value),
                    ("lot", ToDbNullable(lotValue)));
            }
            else
            {
                var produced = Convert.ToInt32(existing!["qty"] ?? 0) - Convert.ToInt32(existing["balance"] ?? 0);
                if (qty.Value < produced)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage($"Quantity cannot be less than already generated quantity ({produced})", 400);
                }

                rows = await QueryRowsAsync(
                    connection,
                    """
                    UPDATE work_orders
                    SET wo = @wo,
                        site_id = @siteId,
                        due_date = @dueDate::date,
                        qty = @qty,
                        status = @status,
                        item_id = @itemId,
                        item_revision_id = @itemRevisionId,
                        lot = @lot,
                        balance = @balance,
                        updated_at = NOW()
                    WHERE id = @id
                    RETURNING *
                    """,
                    ("wo", wo),
                    ("siteId", siteId.Value),
                    ("dueDate", dueDate),
                    ("qty", qty.Value),
                    ("status", status),
                    ("itemId", item["id"]),
                    ("itemRevisionId", itemRevisionId.Value),
                    ("lot", ToDbNullable(lotValue)),
                    ("balance", Math.Max(qty.Value - produced, 0)),
                    ("id", workOrderId.Value));
            }

            await InsertJsonHistoryAsync(connection, "work_order_history", "work_order_id", rows[0]["id"]!, workOrderId is null ? "CREATE" : "UPDATE", rows[0], changedBy);
            await transaction.CommitAsync();
            return Results.Json(rows[0], statusCode: workOrderId is null ? 201 : 200);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync();
            return JsonMessage("WO already exists", 409);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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
    private static async Task<Dictionary<string, object?>?> FindItemByPnAsync(NpgsqlConnection connection, string pn)
    {
        var rows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn LIMIT 1", ("pn", pn));
        return rows.Count == 0 ? null : rows[0];
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

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        return await DbConnectionFactory.OpenConnectionAsync();
    }

    private static Task<List<Dictionary<string, object?>>> QueryRowsAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        return SqlQuery.QueryRowsAsync(connection, sql, parameters);
    }

    private static Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        return SqlQuery.ExecuteAsync(connection, sql, parameters);
    }

    private static Task<T?> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        return SqlQuery.ScalarAsync<T>(connection, sql, parameters);
    }

    private static Task<JsonNode?> ReadJsonBodyAsync(HttpRequest request)
    {
        return JsonBodyReader.ReadJsonBodyAsync(request);
    }

    private static IResult JsonMessage(string message, int statusCode)
    {
        return ApiResults.JsonMessage(message, statusCode);
    }

    private static string? ReadString(JsonNode? node, string key)
    {
        return JsonBodyReader.ReadString(node, key);
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

