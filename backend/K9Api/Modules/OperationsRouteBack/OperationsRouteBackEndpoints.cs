using System.Text.Json.Nodes;
using Npgsql;

public static class OperationsRouteBackEndpoints
{
    public static void MapOperationsRouteBack(WebApplication app)
    {
        app.MapGet("/api/operations/sn-route-back", async (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonError("Serial Number is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);
            await EnsureRouteBackSchemaAsync(connection);

            var serial = await GetSerialByQueryAsync(connection, query);
            if (serial is not null)
            {
                var routeRows = await GetRouteRowsForItemAsync(connection, Convert.ToInt32(serial["item_id"]));
                var passLogs = await GetActivePassLogsByStationAsync(connection, "serial_station_logs", "serial_id", serial["id"]!);
                return Results.Json(BuildRouteBackSummary(query, "standard", serial, routeRows, passLogs));
            }

            var workflowSerial = await GetWorkflowSerialByQueryAsync(connection, query);
            if (workflowSerial is not null)
            {
                var routeRows = await GetWorkflowRouteRowsForPartAsync(connection, Convert.ToInt32(workflowSerial["workflow_part_id"]));
                var passLogs = await GetActivePassLogsByStationAsync(connection, "workflow_serial_station_logs", "workflow_serial_id", workflowSerial["id"]!);
                return Results.Json(BuildRouteBackSummary(query, "workflow", workflowSerial, routeRows, passLogs));
            }

            return JsonError("SN/RSN not found", 404);
        });

        app.MapPost("/api/operations/sn-route-back", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var query = ReadString(payload, "query")?.Trim();
            var targetStationCode = ReadString(payload, "targetStationCode")?.Trim() ?? ReadString(payload, "target_station_code")?.Trim();
            var reason = ReadString(payload, "reason")?.Trim() ?? ReadString(payload, "remarks")?.Trim();
            var changedBy = ReadString(payload, "changedBy")?.Trim() ?? ReadString(payload, "changed_by")?.Trim() ?? "system";

            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonError("Serial Number is required", 400);
            }

            if (string.IsNullOrWhiteSpace(targetStationCode))
            {
                return JsonError("Route back station is required", 400);
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return JsonError("Reason / remarks is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);
            await EnsureRouteBackSchemaAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var serial = await GetSerialByQueryAsync(connection, query);
                if (serial is not null)
                {
                    var result = await RouteBackStandardSerialAsync(connection, serial, targetStationCode, reason, changedBy);
                    await transaction.CommitAsync();
                    return result;
                }

                var workflowSerial = await GetWorkflowSerialByQueryAsync(connection, query);
                if (workflowSerial is not null)
                {
                    var result = await RouteBackWorkflowSerialAsync(connection, workflowSerial, targetStationCode, reason, changedBy);
                    await transaction.CommitAsync();
                    return result;
                }

                await transaction.RollbackAsync();
                return JsonError("SN/RSN not found", 404);
            }
            catch (InvalidOperationException ex)
            {
                await transaction.RollbackAsync();
                return JsonError(ex.Message, 400);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static async Task<IResult> RouteBackStandardSerialAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string targetStationCode,
        string reason,
        string changedBy)
    {
        var routeRows = await GetRouteRowsForItemAsync(connection, Convert.ToInt32(serial["item_id"]));
        var target = FindRouteBackTarget(routeRows, targetStationCode);
        var currentOrder = ResolveRouteBackCurrentOrder(serial, routeRows);
        var targetOrder = Convert.ToInt32(target["station_order"]);
        if (currentOrder > 0 && targetOrder > currentOrder)
        {
            return JsonError("Route back station must be current or previous station", 400);
        }

        var cancelledStations = GetRouteBackCancelledStations(routeRows, targetOrder);
        await CancelRouteBackPassLogsAsync(connection, "serial_station_logs", "serial_id", serial["id"]!, cancelledStations, changedBy, reason);
        await ExecuteAsync(
            connection,
            """
            UPDATE serial_numbers
            SET status = 'In Process',
                condition = 'Good',
                current_station_code = @stationCode,
                current_station_order = @stationOrder,
                last_moved_at = NOW(),
                updated_at = NOW()
            WHERE id = @id
            """,
            ("stationCode", target["station_code"]),
            ("stationOrder", targetOrder),
            ("id", serial["id"]));

        await InsertRouteBackAuditAsync(
            connection,
            "standard",
            serial["id"]!,
            serial["sn"]?.ToString() ?? string.Empty,
            serial["current_station_code"]?.ToString(),
            target["station_code"]?.ToString(),
            cancelledStations,
            changedBy,
            reason);

        var refreshed = await GetSerialByQueryAsync(connection, serial["sn"]?.ToString() ?? string.Empty);
        return Results.Json(new
        {
            message = "Route back completed successfully.",
            data = BuildRouteBackSummary(serial["sn"]?.ToString(), "standard", refreshed ?? serial, routeRows, await GetActivePassLogsByStationAsync(connection, "serial_station_logs", "serial_id", serial["id"]!))
        });
    }

    private static async Task<IResult> RouteBackWorkflowSerialAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string targetStationCode,
        string reason,
        string changedBy)
    {
        var routeRows = await GetWorkflowRouteRowsForPartAsync(connection, Convert.ToInt32(serial["workflow_part_id"]));
        var target = FindRouteBackTarget(routeRows, targetStationCode);
        var currentOrder = ResolveRouteBackCurrentOrder(serial, routeRows);
        var targetOrder = Convert.ToInt32(target["station_order"]);
        if (currentOrder > 0 && targetOrder > currentOrder)
        {
            return JsonError("Route back station must be current or previous station", 400);
        }

        var cancelledStations = GetRouteBackCancelledStations(routeRows, targetOrder);
        await CancelRouteBackPassLogsAsync(connection, "workflow_serial_station_logs", "workflow_serial_id", serial["id"]!, cancelledStations, changedBy, reason);
        await ExecuteAsync(
            connection,
            """
            UPDATE workflow_serial_numbers
            SET status = 'In Process',
                condition = 'Good',
                current_station_code = @stationCode,
                current_station_order = @stationOrder,
                last_moved_at = NOW(),
                updated_at = NOW()
            WHERE id = @id
            """,
            ("stationCode", target["station_code"]),
            ("stationOrder", targetOrder),
            ("id", serial["id"]));

        await InsertRouteBackAuditAsync(
            connection,
            "workflow",
            serial["id"]!,
            serial["sn"]?.ToString() ?? string.Empty,
            serial["current_station_code"]?.ToString(),
            target["station_code"]?.ToString(),
            cancelledStations,
            changedBy,
            reason);

        var refreshed = await GetWorkflowSerialByQueryAsync(connection, serial["sn"]?.ToString() ?? string.Empty);
        return Results.Json(new
        {
            message = "Route back completed successfully.",
            data = BuildRouteBackSummary(serial["sn"]?.ToString(), "workflow", refreshed ?? serial, routeRows, await GetActivePassLogsByStationAsync(connection, "workflow_serial_station_logs", "workflow_serial_id", serial["id"]!))
        });
    }

    private static Dictionary<string, object?> FindRouteBackTarget(List<Dictionary<string, object?>> routeRows, string targetStationCode)
    {
        if (routeRows.Count == 0)
        {
            throw new InvalidOperationException("No routing configured for this SN.");
        }

        return routeRows.FirstOrDefault(row => string.Equals(row["station_code"]?.ToString(), targetStationCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Route back station is not in this SN route.");
    }

    private static int ResolveRouteBackCurrentOrder(Dictionary<string, object?> serial, List<Dictionary<string, object?>> routeRows)
    {
        if (routeRows.Count == 0)
        {
            return 0;
        }

        if (string.Equals(serial["serial_status"]?.ToString(), "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return routeRows.Max(row => Convert.ToInt32(row["station_order"]));
        }

        return ResolveCurrentOrder(serial, routeRows);
    }

    private static string[] GetRouteBackCancelledStations(List<Dictionary<string, object?>> routeRows, int targetOrder)
    {
        return routeRows
            .Where(row => Convert.ToInt32(row["station_order"]) > targetOrder)
            .Select(row => row["station_code"]?.ToString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<Dictionary<string, Dictionary<string, object?>>> GetActivePassLogsByStationAsync(
        NpgsqlConnection connection,
        string tableName,
        string serialIdColumn,
        object serialId)
    {
        var rows = await QueryRowsAsync(
            connection,
            $"""
            SELECT DISTINCT ON (station_code)
                   station_code, id, action_result, created_at
            FROM {tableName}
            WHERE {serialIdColumn} = @serialId
              AND UPPER(action_result) = 'PASS'
              AND COALESCE(is_active, TRUE) = TRUE
            ORDER BY station_code, created_at DESC, id DESC
            """,
            ("serialId", serialId));

        return rows.ToDictionary(row => row["station_code"]?.ToString() ?? string.Empty, row => row, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task CancelRouteBackPassLogsAsync(
        NpgsqlConnection connection,
        string tableName,
        string serialIdColumn,
        object serialId,
        string[] cancelledStations,
        string changedBy,
        string reason)
    {
        if (cancelledStations.Length == 0)
        {
            return;
        }

        await ExecuteAsync(
            connection,
            $"""
            UPDATE {tableName}
            SET is_active = FALSE,
                route_back_cancelled_at = NOW(),
                route_back_cancelled_by = @changedBy,
                route_back_reason = @reason,
                action_result = 'RouteBackCancelled'
            WHERE {serialIdColumn} = @serialId
              AND station_code = ANY(@cancelledStations)
              AND UPPER(action_result) = 'PASS'
              AND COALESCE(is_active, TRUE) = TRUE
            """,
            ("changedBy", changedBy),
            ("reason", reason),
            ("serialId", serialId),
            ("cancelledStations", cancelledStations));
    }

    private static async Task InsertRouteBackAuditAsync(
        NpgsqlConnection connection,
        string serialSource,
        object serialId,
        string serialNumber,
        string? previousStation,
        string? routeBackStation,
        string[] cancelledStations,
        string changedBy,
        string reason)
    {
        await ExecuteAsync(
            connection,
            """
            INSERT INTO serial_route_back_audit
              (serial_source, serial_id, serial_number, previous_station_code, route_back_station_code,
               cancelled_stations, changed_by, reason)
            VALUES
              (@serialSource, @serialId, @serialNumber, @previousStation, @routeBackStation,
               @cancelledStations, @changedBy, @reason)
            """,
            ("serialSource", serialSource),
            ("serialId", Convert.ToInt64(serialId)),
            ("serialNumber", serialNumber),
            ("previousStation", previousStation),
            ("routeBackStation", routeBackStation),
            ("cancelledStations", cancelledStations),
            ("changedBy", changedBy),
            ("reason", reason));
    }

    private static object BuildRouteBackSummary(
        string? query,
        string source,
        Dictionary<string, object?> serial,
        List<Dictionary<string, object?>> routeRows,
        Dictionary<string, Dictionary<string, object?>> activePassLogs)
    {
        var currentOrder = routeRows.Count == 0 ? 0 : ResolveRouteBackCurrentOrder(serial, routeRows);
        var selectableStations = routeRows
            .Where(row => currentOrder == 0 || Convert.ToInt32(row["station_order"]) <= currentOrder)
            .Select(row => new
            {
                station_code = row["station_code"],
                station_name = row["station_name"],
                station_order = row["station_order"]
            })
            .ToList();

        return new
        {
            query,
            source,
            serial = new
            {
                id = serial["id"],
                sn = serial["sn"],
                rsn = serial["rsn"],
                status = serial["serial_status"],
                condition = serial["condition"],
                current_station_code = serial["current_station_code"],
                current_station_order = currentOrder == 0 ? serial["current_station_order"] : currentOrder,
                pn = serial["pn"],
                work_order = serial["wo"]
            },
            routeSummary = routeRows.Select(row =>
            {
                var order = Convert.ToInt32(row["station_order"]);
                var stationCode = row["station_code"]?.ToString() ?? string.Empty;
                var hasPass = activePassLogs.ContainsKey(stationCode);
                var status = currentOrder > 0 && order == currentOrder
                    ? "Current"
                    : hasPass
                        ? "PASS"
                        : currentOrder > 0 && order < currentOrder ? "Cancelled / Needs Retest" : "Pending";
                return new
                {
                    station = stationCode,
                    description = row["station_name"],
                    qmsReport = row["report_mode"],
                    sample = row["sample_mode"],
                    finalStatus = status,
                    stationOrder = order
                };
            }),
            selectableStations
        };
    }

    private static async Task EnsureRouteBackSchemaAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            ALTER TABLE serial_station_logs
            ALTER COLUMN action_result TYPE VARCHAR(40),
            ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE,
            ADD COLUMN IF NOT EXISTS route_back_cancelled_at TIMESTAMP,
            ADD COLUMN IF NOT EXISTS route_back_cancelled_by VARCHAR(100),
            ADD COLUMN IF NOT EXISTS route_back_reason TEXT
            """);

        await ExecuteAsync(
            connection,
            """
            ALTER TABLE workflow_serial_station_logs
            ALTER COLUMN action_result TYPE VARCHAR(40),
            ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE,
            ADD COLUMN IF NOT EXISTS route_back_cancelled_at TIMESTAMP,
            ADD COLUMN IF NOT EXISTS route_back_cancelled_by VARCHAR(100),
            ADD COLUMN IF NOT EXISTS route_back_reason TEXT
            """);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS serial_route_back_audit (
              id BIGSERIAL PRIMARY KEY,
              serial_source VARCHAR(30) NOT NULL,
              serial_id BIGINT NOT NULL,
              serial_number VARCHAR(220) NOT NULL,
              previous_station_code VARCHAR(80),
              route_back_station_code VARCHAR(80),
              cancelled_stations TEXT[] NOT NULL DEFAULT '{}',
              changed_by VARCHAR(100) NOT NULL DEFAULT 'system',
              reason TEXT NOT NULL,
              created_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
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
    private static Task<NpgsqlConnection> OpenConnectionAsync() => DbConnectionFactory.OpenConnectionAsync();

    private static Task<List<Dictionary<string, object?>>> QueryRowsAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters) => SqlQuery.QueryRowsAsync(connection, sql, parameters);

    private static Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters) => SqlQuery.ExecuteAsync(connection, sql, parameters);

    private static Task<JsonNode?> ReadJsonBodyAsync(HttpRequest request) => JsonBodyReader.ReadJsonBodyAsync(request);

    private static string? ReadString(JsonNode? node, string key) => JsonBodyReader.ReadString(node, key);

    private static IResult JsonError(string error, int statusCode) => ApiResults.JsonError(error, statusCode);

    private static IResult JsonMessage(string message, int statusCode) => ApiResults.JsonMessage(message, statusCode);
}

