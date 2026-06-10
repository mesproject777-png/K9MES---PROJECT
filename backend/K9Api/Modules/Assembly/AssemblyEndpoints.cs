using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

public static class AssemblyEndpoints
{
    public static void MapAssembly(WebApplication app)
    {
        app.MapGet("/api/assembly/lookup", async (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString().Trim();
            var limit = Math.Min(ParsePositiveInt(request.Query["limit"], 20), 100);
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.Json(new { data = Array.Empty<object>() });
            }

            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                "SELECT id, pn, description FROM items WHERE pn ILIKE @pattern OR description ILIKE @pattern ORDER BY pn ASC LIMIT @limit",
                ("pattern", $"%{query}%"),
                ("limit", limit));
            return Results.Json(new { data = rows });
        });

        app.MapGet("/api/assembly/operations/status", async (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString().Trim();
            var stationCode = request.Query["station_code"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonMessage("SN or RSN is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            var serial = await GetSerialByQueryAsync(connection, query);
            if (serial is null)
            {
                return JsonMessage("SN/RSN not found", 404);
            }

            if (string.Equals(serial["serial_status"]?.ToString(), "SCRAP", StringComparison.OrdinalIgnoreCase))
            {
                return JsonMessage("SN is SCRAP. Assembly cannot be done until Undo Scrap is completed.", 409);
            }

            var required = await GetAssemblyLinesForStationAsync(connection, Convert.ToInt32(serial["item_id"]), serial["revision"]?.ToString(), stationCode);
            var bound = await QueryRowsAsync(
                connection,
                """
                SELECT l.id, l.station_code, child.sn AS child_sn, child.rsn AS child_rsn,
                       i.pn AS child_pn, COALESCE(ir.revision, '') AS child_revision, l.created_by, l.created_at
                FROM serial_assembly_links l
                JOIN serial_numbers child ON child.id = l.child_serial_id
                JOIN items i ON i.id = child.item_id
                LEFT JOIN item_revisions ir ON ir.id = child.item_revision_id
                WHERE l.parent_serial_id = @id
                ORDER BY l.created_at DESC
                """,
                ("id", serial["id"]));
            return Results.Json(new { parent = serial, required, bound });
        });

        app.MapPost("/api/assembly/operations/bind", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var parentQuery = ReadString(payload, "parent_query")?.Trim() ?? ReadString(payload, "parent_sn")?.Trim();
            var childQuery = ReadString(payload, "child_query")?.Trim() ?? ReadString(payload, "child_sn")?.Trim();
            var stationCode = ReadString(payload, "station_code")?.Trim();
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
            if (string.IsNullOrWhiteSpace(parentQuery) || string.IsNullOrWhiteSpace(childQuery) || string.IsNullOrWhiteSpace(stationCode))
            {
                return JsonMessage("parent, child, and station_code are required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            try
            {
                var parent = await GetSerialByQueryAsync(connection, parentQuery);
                var child = await GetSerialByQueryAsync(connection, childQuery);
                if (parent is null || child is null)
                {
                    return JsonMessage("Parent or child SN/RSN not found", 404);
                }

                if (Equals(parent["id"], child["id"]))
                {
                    return JsonMessage("Parent and child cannot be same", 400);
                }

                if (string.Equals(parent["serial_status"]?.ToString(), "SCRAP", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonMessage("Parent SN is SCRAP. Assembly cannot be done until Undo Scrap is completed.", 409);
                }

                if (string.Equals(child["serial_status"]?.ToString(), "SCRAP", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonMessage("Child SN is SCRAP and cannot be bound.", 409);
                }

                if (!string.Equals(child["serial_status"]?.ToString(), "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonMessage("Child SN must complete its own routing before binding with parent", 409);
                }

                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO serial_assembly_links (parent_serial_id, child_serial_id, station_code, created_by)
                    VALUES (@parentId, @childId, @stationCode, @changedBy)
                    """,
                    ("parentId", parent["id"]),
                    ("childId", child["id"]),
                    ("stationCode", stationCode),
                    ("changedBy", changedBy));
                return Results.Json(new { message = "Child bound successfully" }, statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonMessage("Child SN is already bound", 409);
            }
        });

        app.MapGet("/api/assembly/{itemId:int}/revisions", async (int itemId) =>
        {
            await using var connection = await OpenConnectionAsync();
            var itemRows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE id = @id", ("id", itemId));
            if (itemRows.Count == 0)
            {
                return JsonMessage("Part number not found", 404);
            }

            var revisions = await QueryRowsAsync(
                connection,
                "SELECT id, item_id, revision, in_date, expire_date FROM item_revisions WHERE item_id = @itemId ORDER BY in_date DESC, id DESC",
                ("itemId", itemId));
            return Results.Json(new { item = itemRows[0], data = revisions, total = revisions.Count });
        });

        app.MapGet("/api/assembly/view/search", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            var revision = request.Query["revision"].ToString().Trim();
            var includeHistory = string.Equals(request.Query["includeHistory"], "true", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(pn))
            {
                return JsonMessage("pn is required", 400);
            }

            if (string.IsNullOrWhiteSpace(revision))
            {
                return JsonMessage("revision is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            var payload = await GetAssemblyPayloadAsync(connection, pn, revision, includeHistory);
            if (payload is null)
            {
                return JsonMessage("Part number not found", 404);
            }

            if (payload.Revision is null)
            {
                return JsonMessage("Revision not found for this PN", 404);
            }

            return Results.Json(new { item = payload.Item, revision = payload.Revision, data = payload.Data, history = payload.History, total = payload.Data.Count });
        });

        app.MapPost("/api/assembly/lines", async (HttpContext context) => await SaveAssemblyLineAsync(context, null));
        app.MapPut("/api/assembly/lines/{lineId:int}", async (int lineId, HttpContext context) => await SaveAssemblyLineAsync(context, lineId));
        app.MapDelete("/api/assembly/lines/{lineId:int}", async (int lineId, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(connection, "DELETE FROM item_assembly_lines WHERE id = @id RETURNING *", ("id", lineId));
            if (rows.Count == 0)
            {
                return JsonMessage("Assembly line not found", 404);
            }

            await InsertAssemblyHistoryAsync(connection, rows[0]["main_item_id"]!, rows[0]["main_item_revision_id"]!, null, "DELETE", "Assembly line deleted", rows[0], changedBy);
            return Results.Json(new { message = "Assembly line deleted successfully" });
        });
    }

    private sealed record AssemblyPayload(
        Dictionary<string, object?> Item,
        Dictionary<string, object?>? Revision,
        List<Dictionary<string, object?>> Data,
        List<Dictionary<string, object?>> History);

    private static async Task<AssemblyPayload?> GetAssemblyPayloadAsync(NpgsqlConnection connection, string pn, string revision, bool includeHistory)
    {
        var mainItem = await FindItemByPnAsync(connection, pn);
        if (mainItem is null)
        {
            return null;
        }

        var mainRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(mainItem["id"]), revision);
        if (mainRevision is null)
        {
            return new AssemblyPayload(mainItem, null, new List<Dictionary<string, object?>>(), new List<Dictionary<string, object?>>());
        }

        var data = await QueryRowsAsync(
            connection,
            """
            SELECT al.id, al.main_item_id, al.main_item_revision_id, al.son_item_id, al.son_item_revision_id,
                   son.pn AS son_pn, son.description AS son_description, COALESCE(sr.revision, '') AS son_rev,
                   al.station_code, al.station_name, al.assemble_order, al.pattern_regex,
                   al.part_to_validate, al.regex_value_to_match, al.transform_regex,
                   al.created_at, al.updated_at
            FROM item_assembly_lines al
            JOIN items son ON son.id = al.son_item_id
            LEFT JOIN item_revisions sr ON sr.id = al.son_item_revision_id
            WHERE al.main_item_revision_id = @revisionId
            ORDER BY al.station_code ASC, al.assemble_order ASC, al.id ASC
            """,
            ("revisionId", mainRevision["id"]));
        var history = includeHistory
            ? await QueryRowsAsync(
                connection,
                """
                SELECT id, main_item_id, main_item_revision_id, assembly_line_id, action, description, change_data, changed_by, changed_at
                FROM item_assembly_history
                WHERE main_item_revision_id = @revisionId
                ORDER BY changed_at DESC, id DESC
                LIMIT 300
                """,
                ("revisionId", mainRevision["id"]))
            : new List<Dictionary<string, object?>>();
        return new AssemblyPayload(mainItem, mainRevision, data, history);
    }

    private static async Task<List<Dictionary<string, object?>>> GetAssemblyLinesForStationAsync(NpgsqlConnection connection, int itemId, string? revision, string? stationCode)
    {
        if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(stationCode))
        {
            return new List<Dictionary<string, object?>>();
        }

        var revisionRow = await FindItemRevisionAsync(connection, itemId, revision);
        if (revisionRow is null)
        {
            return new List<Dictionary<string, object?>>();
        }

        return await QueryRowsAsync(
            connection,
            """
            SELECT al.id, al.station_code, al.station_name, al.assemble_order,
                   son.pn AS son_pn, COALESCE(sr.revision, '') AS son_rev,
                   al.pattern_regex, al.part_to_validate, al.regex_value_to_match, al.transform_regex
            FROM item_assembly_lines al
            JOIN items son ON son.id = al.son_item_id
            LEFT JOIN item_revisions sr ON sr.id = al.son_item_revision_id
            WHERE al.main_item_revision_id = @revisionId
              AND UPPER(al.station_code) = UPPER(@stationCode)
            ORDER BY al.assemble_order ASC, al.id ASC
            """,
            ("revisionId", revisionRow["id"]),
            ("stationCode", stationCode));
    }

    private static async Task<IResult> SaveAssemblyLineAsync(HttpContext context, int? lineId)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        var mainPn = ReadString(payload, "main_pn")?.Trim();
        var mainRevisionText = ReadString(payload, "main_revision")?.Trim();
        var sonPn = ReadString(payload, "son_pn")?.Trim();
        var sonRevisionText = ReadString(payload, "son_rev")?.Trim();
        var stationCode = ReadString(payload, "station_code")?.Trim();
        var assembleOrder = ReadInt(payload, "assemble_order");
        var patternRegex = ReadString(payload, "pattern_regex")?.Trim() ?? "Skip";
        var partToValidate = ReadInt(payload, "part_to_validate");
        var regexValueToMatch = ReadString(payload, "regex_value_to_match")?.Trim();
        var transformRegex = ReadString(payload, "transform_regex")?.Trim();
        var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";

        if (string.IsNullOrWhiteSpace(mainPn) || string.IsNullOrWhiteSpace(mainRevisionText) ||
            string.IsNullOrWhiteSpace(sonPn) || string.IsNullOrWhiteSpace(stationCode))
        {
            return JsonMessage("main_pn, main_revision, son_pn, and station_code are required", 400);
        }

        if (assembleOrder is null or <= 0)
        {
            return JsonMessage("assemble_order must be a positive number", 400);
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var mainItem = await FindItemByPnAsync(connection, mainPn);
            var sonItem = await FindItemByPnAsync(connection, sonPn);
            if (mainItem is null || sonItem is null)
            {
                await transaction.RollbackAsync();
                return JsonMessage("Main or son PN not found", 404);
            }

            var mainRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(mainItem["id"]), mainRevisionText);
            if (mainRevision is null)
            {
                await transaction.RollbackAsync();
                return JsonMessage("Main revision not found for PN", 404);
            }

            int? sonRevisionId = null;
            if (!string.IsNullOrWhiteSpace(sonRevisionText))
            {
                var sonRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(sonItem["id"]), sonRevisionText);
                if (sonRevision is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Son revision not found for PN", 404);
                }

                sonRevisionId = Convert.ToInt32(sonRevision["id"]);
            }

            var stationRows = await QueryRowsAsync(
                connection,
                "SELECT masterstation_code, masterstation_name FROM masterstation WHERE UPPER(masterstation_code) = UPPER(@code) LIMIT 1",
                ("code", stationCode));
            var stationName = stationRows.Count > 0 ? stationRows[0]["masterstation_name"]?.ToString() : stationCode;

            List<Dictionary<string, object?>> rows;
            if (lineId is null)
            {
                rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO item_assembly_lines
                      (main_item_id, main_item_revision_id, son_item_id, son_item_revision_id, station_code, station_name,
                       assemble_order, pattern_regex, part_to_validate, regex_value_to_match, transform_regex)
                    VALUES
                      (@mainItemId, @mainRevisionId, @sonItemId, @sonRevisionId, @stationCode, @stationName,
                       @assembleOrder, @patternRegex, @partToValidate, @regexValueToMatch, @transformRegex)
                    RETURNING *
                    """,
                    ("mainItemId", mainItem["id"]),
                    ("mainRevisionId", mainRevision["id"]),
                    ("sonItemId", sonItem["id"]),
                    ("sonRevisionId", ToDbNullable(sonRevisionId)),
                    ("stationCode", stationCode),
                    ("stationName", stationName),
                    ("assembleOrder", assembleOrder.Value),
                    ("patternRegex", patternRegex),
                    ("partToValidate", ToDbNullable(partToValidate)),
                    ("regexValueToMatch", ToDbNullable(regexValueToMatch)),
                    ("transformRegex", ToDbNullable(transformRegex)));
            }
            else
            {
                rows = await QueryRowsAsync(
                    connection,
                    """
                    UPDATE item_assembly_lines
                    SET son_item_id = @sonItemId,
                        son_item_revision_id = @sonRevisionId,
                        station_code = @stationCode,
                        station_name = @stationName,
                        assemble_order = @assembleOrder,
                        pattern_regex = @patternRegex,
                        part_to_validate = @partToValidate,
                        regex_value_to_match = @regexValueToMatch,
                        transform_regex = @transformRegex,
                        updated_at = NOW()
                    WHERE id = @lineId
                    RETURNING *
                    """,
                    ("sonItemId", sonItem["id"]),
                    ("sonRevisionId", ToDbNullable(sonRevisionId)),
                    ("stationCode", stationCode),
                    ("stationName", stationName),
                    ("assembleOrder", assembleOrder.Value),
                    ("patternRegex", patternRegex),
                    ("partToValidate", ToDbNullable(partToValidate)),
                    ("regexValueToMatch", ToDbNullable(regexValueToMatch)),
                    ("transformRegex", ToDbNullable(transformRegex)),
                    ("lineId", lineId.Value));
                if (rows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Assembly line not found", 404);
                }
            }

            await InsertAssemblyHistoryAsync(connection, mainItem["id"]!, mainRevision["id"]!, rows[0]["id"], lineId is null ? "INSERT" : "UPDATE", lineId is null ? "Assembly line insert" : "Assembly line updated", rows[0], changedBy);
            await transaction.CommitAsync();
            return Results.Json(rows[0], statusCode: lineId is null ? 201 : 200);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task InsertAssemblyHistoryAsync(
        NpgsqlConnection connection,
        object mainItemId,
        object mainRevisionId,
        object? assemblyLineId,
        string action,
        string description,
        object changeData,
        string changedBy)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO item_assembly_history
              (main_item_id, main_item_revision_id, assembly_line_id, action, description, change_data, changed_by)
            VALUES
              (@mainItemId, @mainRevisionId, @assemblyLineId, @action, @description, @changeData, @changedBy)
            """,
            connection);
        command.Parameters.AddWithValue("mainItemId", mainItemId);
        command.Parameters.AddWithValue("mainRevisionId", mainRevisionId);
        command.Parameters.AddWithValue("assemblyLineId", assemblyLineId ?? DBNull.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.Add("changeData", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(changeData);
        command.Parameters.AddWithValue("changedBy", changedBy);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<string, object?>?> FindItemByPnAsync(NpgsqlConnection connection, string pn)
    {
        var rows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn LIMIT 1", ("pn", pn));
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<Dictionary<string, object?>?> FindItemRevisionAsync(NpgsqlConnection connection, int itemId, string revision)
    {
        var rows = await QueryRowsAsync(
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

    private static IResult JsonMessage(string message, int statusCode) => ApiResults.JsonMessage(message, statusCode);

    private static int ParsePositiveInt(object? value, int fallback)
    {
        return int.TryParse(value?.ToString(), out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static object? ToDbNullable<T>(T? value) => value is null ? DBNull.Value : value;
}

