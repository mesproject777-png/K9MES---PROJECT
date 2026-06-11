using Npgsql;

public static class StationValidationEndpoints
{
    public static void MapStationValidation(WebApplication app)
    {
        app.MapGet("/api/station/check-sn", async (HttpRequest request) =>
        {
            static IResult Pass(bool samplingRequired = false, string? samplingReason = null) => Results.Json(new
            {
                success = true,
                result = "PASS",
                samplingRequired,
                samplingReason
            });
            static IResult Fail(string reason) => Results.Json(new { success = false, result = "FAIL", reason });

            var rsn = request.Query["rsn"].ToString().Trim();
            var userCode = request.Query["userCode"].ToString().Trim();
            var password = request.Query["password"].ToString().Trim();
            var workOrder = request.Query["workOrder"].ToString().Trim();

            if (string.IsNullOrWhiteSpace(userCode) || string.IsNullOrWhiteSpace(password))
            {
                return Fail("User authentication failed");
            }

            if (string.IsNullOrWhiteSpace(rsn))
            {
                return Fail("Serial Number not found");
            }

            if (string.IsNullOrWhiteSpace(workOrder))
            {
                return Fail("Invalid Work Order");
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await SerialTrackingSchema.EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSamplingRuntimeSchemaAsync(connection);

            var userRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT login_id, password, is_active
                FROM users
                WHERE UPPER(BTRIM(login_id)) = UPPER(BTRIM(@userCode))
                LIMIT 1
                """,
                ("userCode", userCode));
            var userAuthenticated = userRows.Count > 0 &&
                userRows[0]["password"] is string storedPassword &&
                string.Equals(storedPassword.Trim(), password, StringComparison.Ordinal) &&
                !(userRows[0]["is_active"] is bool active && !active);

            var workOrderRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT id, wo, workflow_part_id, status
                FROM workflow_work_orders
                WHERE UPPER(wo) = UPPER(@workOrder)
                LIMIT 1
                """,
                ("workOrder", workOrder));
            if (workOrderRows.Count == 0)
            {
                return Fail("Invalid Work Order");
            }

            if (!userAuthenticated)
            {
                var stationLoginRows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    SELECT l.station_login_id
                    FROM workflow_station_logins l
                    JOIN workflow_routing_steps r ON r.id = l.workflow_routing_step_id
                    WHERE UPPER(BTRIM(l.station_login_id)) = UPPER(BTRIM(@userCode))
                      AND BTRIM(l.station_login_password) = BTRIM(@password)
                      AND r.workflow_part_id = @workflowPartId
                      AND (l.workflow_work_order_id = @workflowWorkOrderId OR l.workflow_work_order_id IS NULL)
                    LIMIT 1
                    """,
                    ("userCode", userCode),
                    ("password", password),
                    ("workflowPartId", workOrderRows[0]["workflow_part_id"]),
                    ("workflowWorkOrderId", workOrderRows[0]["id"]));
                userAuthenticated = stationLoginRows.Count > 0;
            }

            if (!userAuthenticated)
            {
                var routingLoginRows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    SELECT r.station_login_id
                    FROM workflow_routing_steps r
                    WHERE UPPER(BTRIM(r.station_login_id)) = UPPER(BTRIM(@userCode))
                      AND BTRIM(r.station_login_password) = BTRIM(@password)
                      AND r.workflow_part_id = @workflowPartId
                    LIMIT 1
                    """,
                    ("userCode", userCode),
                    ("password", password),
                    ("workflowPartId", workOrderRows[0]["workflow_part_id"]));
                userAuthenticated = routingLoginRows.Count > 0;
            }

            if (!userAuthenticated)
            {
                return Fail("User authentication failed");
            }

            var serialRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT sn.id, sn.sn, sn.rsn, sn.workflow_work_order_id, sn.workflow_part_id,
                       sn.status AS serial_status, sn.condition, sn.current_station_code,
                       sn.current_station_order, sn.last_moved_at, w.wo
                FROM workflow_serial_numbers sn
                JOIN workflow_work_orders w ON w.id = sn.workflow_work_order_id
                WHERE UPPER(sn.rsn) = UPPER(@rsn)
                   OR UPPER(sn.sn) = UPPER(@rsn)
                LIMIT 1
                """,
                ("rsn", rsn));
            if (serialRows.Count == 0)
            {
                return Fail("Serial Number not found");
            }

            var serial = serialRows[0];
            if (Convert.ToInt32(serial["workflow_work_order_id"]) != Convert.ToInt32(workOrderRows[0]["id"]))
            {
                return Fail("Invalid Work Order");
            }

            if (string.Equals(serial["serial_status"]?.ToString(), "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("Serial Number already completed");
            }

            if (string.Equals(serial["serial_status"]?.ToString(), "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(serial["serial_status"]?.ToString(), "SCRAP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(serial["condition"]?.ToString(), "NG", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(serial["condition"]?.ToString(), "FAIL", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("Route back required");
            }

            var routeRows = await GetWorkflowRouteRowsForPartAsync(connection, Convert.ToInt32(serial["workflow_part_id"]));
            if (routeRows.Count == 0)
            {
                return Fail("Previous station not passed");
            }

            var currentOrder = ResolveCurrentOrder(serial, routeRows);
            var currentStation = routeRows.FirstOrDefault(row => Convert.ToInt32(row["station_order"]) == currentOrder) ?? routeRows[0];
            var currentStationCode = currentStation["station_code"]?.ToString() ?? string.Empty;
            var previousStations = routeRows
                .Where(row => Convert.ToInt32(row["station_order"]) < Convert.ToInt32(currentStation["station_order"]))
                .ToList();

            if (previousStations.Count > 0)
            {
                var previousCodes = previousStations
                    .Select(row => row["station_code"]?.ToString())
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Cast<string>()
                    .ToArray();
                var passedRows = await SqlQuery.QueryRowsAsync(
                    connection,
                    """
                    SELECT DISTINCT station_code
                    FROM workflow_serial_station_logs
                    WHERE workflow_serial_id = @serialId
                      AND UPPER(action_result) = 'PASS'
                      AND station_code = ANY(@stationCodes)
                    """,
                    ("serialId", serial["id"]),
                    ("stationCodes", previousCodes));
                if (passedRows.Count < previousCodes.Length)
                {
                    return Fail("Previous station not passed");
                }

                var failedPreviousStep = await FindLatestFailedPreviousStepAsync(connection, serial["id"]!, routeRows, Convert.ToInt32(currentStation["station_order"]));
                if (failedPreviousStep is not null)
                {
                    var failedStationName = failedPreviousStep["station_name"]?.ToString()
                        ?? failedPreviousStep["station_code"]?.ToString()
                        ?? "previous station";
                    return Fail($"Previous station \"{failedStationName}\" is failed. Please pass that station before continuing.");
                }
            }

            var ruleRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT rule_text
                FROM workflow_station_rules
                WHERE workflow_part_id = @workflowPartId
                  AND UPPER(station_code) = UPPER(@stationCode)
                ORDER BY rule_order ASC
                """,
                ("workflowPartId", serial["workflow_part_id"]),
                ("stationCode", currentStationCode));
            if (ruleRows.Any(row => string.IsNullOrWhiteSpace(row["rule_text"]?.ToString())))
            {
                return Fail("Station validation failed");
            }

            var labelRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT label_code, printer_id, ip_address, port, status, is_label_printing_enabled
                FROM workflow_station_label_printing
                WHERE workflow_part_id = @workflowPartId
                  AND UPPER(station_code) = UPPER(@stationCode)
                LIMIT 1
                """,
                ("workflowPartId", serial["workflow_part_id"]),
                ("stationCode", currentStationCode));
            if (labelRows.Count > 0 && labelRows[0]["is_label_printing_enabled"] is bool labelEnabled && labelEnabled)
            {
                var label = labelRows[0];
                var labelConfigOk =
                    !string.IsNullOrWhiteSpace(label["label_code"]?.ToString()) &&
                    !string.IsNullOrWhiteSpace(label["printer_id"]?.ToString()) &&
                    !string.IsNullOrWhiteSpace(label["ip_address"]?.ToString()) &&
                    !string.IsNullOrWhiteSpace(label["port"]?.ToString()) &&
                    !string.Equals(label["status"]?.ToString(), "Inactive", StringComparison.OrdinalIgnoreCase);
                if (!labelConfigOk)
                {
                    return Fail("Label printing validation failed");
                }
            }

            var weighingRows = await SqlQuery.QueryRowsAsync(
                connection,
                """
                SELECT minimum_weight, maximum_weight, is_weighing_enabled
                FROM workflow_station_weighing
                WHERE workflow_part_id = @workflowPartId
                  AND UPPER(station_code) = UPPER(@stationCode)
                LIMIT 1
                """,
                ("workflowPartId", serial["workflow_part_id"]),
                ("stationCode", currentStationCode));
            if (weighingRows.Count > 0 && weighingRows[0]["is_weighing_enabled"] is bool weighingEnabled && weighingEnabled)
            {
                var weighing = weighingRows[0];
                if (string.IsNullOrWhiteSpace(weighing["minimum_weight"]?.ToString()) ||
                    string.IsNullOrWhiteSpace(weighing["maximum_weight"]?.ToString()))
                {
                    return Fail("Weighing validation failed");
                }
            }

            var samplingDecision = await TryMarkTimeBasedSamplingDueAsync(connection, serial, currentStationCode);
            return Pass(samplingDecision.IsRequired, samplingDecision.Reason);
        })
        .WithTags("Station")
        .WithName("CheckSerialNumberForStation")
        .WithSummary("Validate whether a serial number can continue at its current MES station.")
        .WithDescription("Returns only PASS or FAIL. On FAIL, reason explains the first validation failure. Does not return SN history or route history.")
        .Produces(StatusCodes.Status200OK);
    }

    private static async Task<List<Dictionary<string, object?>>> GetWorkflowRouteRowsForPartAsync(NpgsqlConnection connection, int workflowPartId)
    {
        return await SqlQuery.QueryRowsAsync(
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

    private static async Task<(bool IsRequired, string? Reason)> TryMarkTimeBasedSamplingDueAsync(
        NpgsqlConnection connection,
        Dictionary<string, object?> serial,
        string stationCode)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
        {
            return (false, null);
        }

        var rows = await SqlQuery.QueryRowsAsync(
            connection,
            """
            WITH config AS (
              SELECT workflow_part_id, station_code, interval_time_minutes
              FROM workflow_station_sampling
              WHERE workflow_part_id = @workflowPartId
                AND UPPER(BTRIM(station_code)) = UPPER(BTRIM(@stationCode))
                AND is_sampling_enabled = TRUE
                AND UPPER(BTRIM(sampling_type)) = 'PERIODIC_TIME'
                AND interval_time_minutes > 0
              LIMIT 1
            ),
            clock_anchor AS (
              SELECT
                config.workflow_part_id,
                config.station_code,
                config.interval_time_minutes,
                COALESCE(
                  (
                    SELECT MAX(event.created_at)
                    FROM workflow_station_sampling_events event
                    WHERE event.workflow_part_id = config.workflow_part_id
                      AND event.workflow_work_order_id = @workflowWorkOrderId
                      AND UPPER(BTRIM(event.station_code)) = UPPER(BTRIM(config.station_code))
                      AND UPPER(BTRIM(event.sampling_type)) = 'PERIODIC_TIME'
                  ),
                  (
                    SELECT MIN(active_sn.last_moved_at)
                    FROM workflow_serial_numbers active_sn
                    WHERE active_sn.workflow_part_id = config.workflow_part_id
                      AND active_sn.workflow_work_order_id = @workflowWorkOrderId
                      AND UPPER(BTRIM(active_sn.current_station_code)) = UPPER(BTRIM(config.station_code))
                      AND active_sn.last_moved_at IS NOT NULL
                  ),
                  @lastMovedAt::timestamp
                ) AS anchor_time
              FROM config
            ),
            due AS (
              SELECT *
              FROM clock_anchor
              WHERE anchor_time IS NOT NULL
                AND NOW() >= anchor_time + (interval_time_minutes || ' minutes')::interval
            ),
            inserted AS (
              INSERT INTO workflow_station_sampling_events
                (workflow_part_id, workflow_work_order_id, workflow_serial_id, station_code,
                 sampling_type, interval_time_minutes, created_at)
              SELECT @workflowPartId, @workflowWorkOrderId, @workflowSerialId, station_code,
                     'PERIODIC_TIME', interval_time_minutes, NOW()
              FROM due
              RETURNING interval_time_minutes
            )
            SELECT interval_time_minutes
            FROM inserted
            LIMIT 1
            """,
            ("workflowPartId", serial["workflow_part_id"]),
            ("workflowWorkOrderId", serial["workflow_work_order_id"]),
            ("workflowSerialId", serial["id"]),
            ("stationCode", stationCode),
            ("lastMovedAt", serial["last_moved_at"] ?? DBNull.Value));

        if (rows.Count == 0)
        {
            return (false, null);
        }

        var minutes = Convert.ToInt32(rows[0]["interval_time_minutes"] ?? 0);
        return (true, $"Time-based periodic sampling due after {minutes} minute{(minutes == 1 ? string.Empty : "s")}.");
    }

    private static async Task EnsureWorkflowSamplingRuntimeSchemaAsync(NpgsqlConnection connection)
    {
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE public.workflow_station_sampling ADD COLUMN IF NOT EXISTS interval_time_minutes INTEGER NOT NULL DEFAULT 5");
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_station_sampling_events (
              id BIGSERIAL PRIMARY KEY,
              workflow_part_id INTEGER NOT NULL REFERENCES workflow_part_numbers(id) ON DELETE CASCADE,
              workflow_work_order_id INTEGER NOT NULL REFERENCES workflow_work_orders(id) ON DELETE CASCADE,
              workflow_serial_id BIGINT NOT NULL REFERENCES workflow_serial_numbers(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              sampling_type VARCHAR(30) NOT NULL DEFAULT 'PERIODIC_TIME',
              interval_time_minutes INTEGER NOT NULL DEFAULT 5,
              created_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS idx_workflow_sampling_events_station
            ON public.workflow_station_sampling_events (workflow_part_id, workflow_work_order_id, station_code, sampling_type, created_at DESC)
            """);
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS idx_workflow_sampling_events_serial_station
            ON public.workflow_station_sampling_events (workflow_serial_id, station_code, sampling_type)
            """);
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

        var rows = await SqlQuery.QueryRowsAsync(
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
}
