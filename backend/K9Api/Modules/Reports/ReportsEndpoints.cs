using System.Globalization;
using Npgsql;

public static class ReportsEndpoints
{
    public static void MapReports(WebApplication app)
    {
        app.MapGet("/api/reports/debug-dashboard/options", async (HttpRequest request) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);

            var sites = await QueryRowsAsync(
                connection,
                """
                SELECT MIN(COALESCE(site_id, 0)) AS id, site_name AS name
                FROM workflow_work_orders
                WHERE BTRIM(COALESCE(site_name, '')) <> ''
                GROUP BY site_name
                ORDER BY name ASC
                """);

            var repairStations = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT
                  COALESCE(repair_route.station_code, cfg.repair_station_name) AS station_code,
                  COALESCE(repair_route.station_name, cfg.repair_station_name) AS station_name
                FROM workflow_station_repair cfg
                JOIN workflow_work_orders w ON w.workflow_part_id = cfg.workflow_part_id
                LEFT JOIN workflow_routing_steps repair_route
                  ON repair_route.workflow_part_id = cfg.workflow_part_id
                 AND (
                    UPPER(BTRIM(repair_route.station_code)) = UPPER(BTRIM(cfg.repair_station_name))
                    OR UPPER(BTRIM(repair_route.station_name)) = UPPER(BTRIM(cfg.repair_station_name))
                 )
                WHERE cfg.is_repair_station_enabled = TRUE
                  AND BTRIM(COALESCE(cfg.repair_station_name, '')) <> ''
                ORDER BY station_code ASC
                """);

            var partNumbers = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT p.id, p.pn, p.description
                FROM workflow_station_repair cfg
                JOIN workflow_part_numbers p ON p.id = cfg.workflow_part_id
                WHERE cfg.is_repair_station_enabled = TRUE
                ORDER BY p.pn ASC
                """);

            var workOrders = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT w.id, w.wo, p.pn AS part_number, w.site_name AS site
                FROM workflow_station_repair cfg
                JOIN workflow_work_orders w ON w.workflow_part_id = cfg.workflow_part_id
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                WHERE cfg.is_repair_station_enabled = TRUE
                ORDER BY w.wo ASC
                """);

            var remarks = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT COALESCE(NULLIF(BTRIM(l.debug_remark), ''), 'No Remark') AS remark
                FROM workflow_serial_station_logs l
                JOIN workflow_station_repair cfg
                  ON cfg.workflow_part_id = l.workflow_part_id
                 AND UPPER(BTRIM(cfg.station_code)) = UPPER(BTRIM(l.station_code))
                WHERE cfg.is_repair_station_enabled = TRUE
                  AND UPPER(l.action_result) = 'FAIL'
                  AND NULLIF(BTRIM(l.debug_remark), '') IS NOT NULL
                ORDER BY remark ASC
                """);

            return Results.Json(new { sites, repairStations, partNumbers, workOrders, remarks });
        });

        app.MapGet("/api/reports/debug-dashboard/data", async (HttpRequest request) =>
        {
            var site = request.Query["site"].ToString().Trim();
            var station = request.Query["station"].ToString().Trim();
            var stationValues = ReadQueryList(request, "stationIds");
            if (stationValues.Length == 0 && !string.IsNullOrWhiteSpace(station))
            {
                stationValues = new[] { station };
            }
            var pn = request.Query["pn"].ToString().Trim();
            var wo = request.Query["wo"].ToString().Trim();
            var status = request.Query["status"].ToString().Trim();
            var remark = request.Query["remark"].ToString().Trim();
            var snSearch = request.Query["sn"].ToString().Trim();
            var fromDateRaw = request.Query["fromDate"].ToString().Trim();
            var toDateRaw = request.Query["toDate"].ToString().Trim();
            var viewBy = request.Query["viewBy"].ToString().Trim().ToLowerInvariant();

            var fromDate = DateTime.TryParse(fromDateRaw, out var parsedFrom) ? parsedFrom.Date : DateTime.Today;
            var toDate = DateTime.TryParse(toDateRaw, out var parsedTo) ? parsedTo.Date : DateTime.Today;
            if (toDate < fromDate)
            {
                (fromDate, toDate) = (toDate, fromDate);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);

            var rows = await QueryRowsAsync(
                connection,
                """
                WITH repair_events AS (
                  SELECT
                    fail_log.id AS fail_log_id,
                    fail_log.workflow_serial_id,
                    sn.sn,
                    sn.status AS serial_status,
                    p.pn,
                    w.wo,
                    w.site_name,
                    fail_log.station_code AS failed_station_code,
                    COALESCE(fail_log.station_name, fail_log.station_code) AS failed_station_name,
                    COALESCE(repair_route.station_code, fail_log.after_station_code, cfg.repair_station_name) AS repair_station_code,
                    COALESCE(repair_route.station_name, fail_log.after_station_code, cfg.repair_station_name) AS repair_station_name,
                    COALESCE(all_fail_remarks.failure_remark, NULLIF(BTRIM(fail_log.debug_remark), ''), 'No Remark') AS failure_remark,
                    fail_log.created_at AS failed_time,
                    pass_log.created_at AS repaired_time
                  FROM workflow_serial_station_logs fail_log
                  JOIN workflow_serial_numbers sn ON sn.id = fail_log.workflow_serial_id
                  JOIN workflow_work_orders w ON w.id = fail_log.workflow_work_order_id
                  JOIN workflow_part_numbers p ON p.id = fail_log.workflow_part_id
                  JOIN workflow_station_repair cfg
                    ON cfg.workflow_part_id = fail_log.workflow_part_id
                   AND UPPER(BTRIM(cfg.station_code)) = UPPER(BTRIM(fail_log.station_code))
                   AND cfg.is_repair_station_enabled = TRUE
                  LEFT JOIN workflow_routing_steps repair_route
                    ON repair_route.workflow_part_id = fail_log.workflow_part_id
                   AND (
                      UPPER(BTRIM(repair_route.station_code)) = UPPER(BTRIM(COALESCE(fail_log.after_station_code, cfg.repair_station_name)))
                      OR UPPER(BTRIM(repair_route.station_name)) = UPPER(BTRIM(COALESCE(fail_log.after_station_code, cfg.repair_station_name)))
                   )
                  LEFT JOIN LATERAL (
                    SELECT STRING_AGG(COALESCE(NULLIF(BTRIM(seq.debug_remark), ''), 'No Remark'), ' | ' ORDER BY seq.created_at ASC, seq.id ASC) AS failure_remark
                    FROM workflow_serial_station_logs seq
                    WHERE seq.workflow_serial_id = fail_log.workflow_serial_id
                      AND UPPER(seq.action_result) = 'FAIL'
                      AND UPPER(BTRIM(seq.station_code)) = UPPER(BTRIM(fail_log.station_code))
                      AND seq.created_at <= fail_log.created_at
                      AND NOT EXISTS (
                        SELECT 1
                        FROM workflow_serial_station_logs pass_before
                        WHERE pass_before.workflow_serial_id = fail_log.workflow_serial_id
                          AND UPPER(pass_before.action_result) = 'PASS'
                          AND UPPER(BTRIM(pass_before.station_code)) = UPPER(BTRIM(fail_log.station_code))
                          AND pass_before.created_at > seq.created_at
                          AND pass_before.created_at <= fail_log.created_at
                      )
                  ) all_fail_remarks ON TRUE
                  LEFT JOIN LATERAL (
                    SELECT pass_log.created_at
                    FROM workflow_serial_station_logs pass_log
                    WHERE pass_log.workflow_serial_id = fail_log.workflow_serial_id
                      AND UPPER(pass_log.action_result) = 'PASS'
                      AND UPPER(BTRIM(pass_log.station_code)) = UPPER(BTRIM(COALESCE(repair_route.station_code, fail_log.after_station_code, cfg.repair_station_name)))
                      AND pass_log.created_at > fail_log.created_at
                    ORDER BY pass_log.created_at ASC, pass_log.id ASC
                    LIMIT 1
                  ) pass_log ON TRUE
                  WHERE UPPER(fail_log.action_result) = 'FAIL'
                    AND fail_log.after_station_code IS NOT NULL
                    AND UPPER(BTRIM(fail_log.after_station_code)) <> UPPER(BTRIM(fail_log.station_code))
                    AND fail_log.created_at::date >= @fromDate::date
                    AND fail_log.created_at::date <= @toDate::date
                )
                SELECT *
                FROM repair_events
                WHERE (@site = '' OR UPPER(BTRIM(site_name)) = UPPER(BTRIM(@site)))
                  AND (cardinality(@stationValues) = 0 OR EXISTS (
                    SELECT 1
                    FROM unnest(@stationValues) selected_station
                    WHERE UPPER(BTRIM(repair_station_code)) = UPPER(BTRIM(selected_station))
                       OR UPPER(BTRIM(repair_station_name)) = UPPER(BTRIM(selected_station))
                  ))
                  AND (@pn = '' OR UPPER(BTRIM(pn)) = UPPER(BTRIM(@pn)))
                  AND (@wo = '' OR UPPER(BTRIM(wo)) = UPPER(BTRIM(@wo)))
                  AND (@remark = '' OR UPPER(failure_remark) LIKE UPPER('%' || @remark || '%'))
                  AND (@sn = '' OR UPPER(sn) LIKE UPPER('%' || @sn || '%'))
                  AND (
                    @status = ''
                    OR (@status = 'Pending' AND repaired_time IS NULL)
                    OR (@status = 'Passed' AND repaired_time IS NOT NULL)
                  )
                ORDER BY failed_time DESC, fail_log_id DESC
                """,
                ("site", site),
                ("stationValues", stationValues),
                ("pn", pn),
                ("wo", wo),
                ("remark", remark),
                ("sn", snSearch),
                ("status", status),
                ("fromDate", fromDate),
                ("toDate", toDate));

            static string Read(Dictionary<string, object?> row, string key) => Convert.ToString(row[key]) ?? string.Empty;
            static DateTime ReadDate(Dictionary<string, object?> row, string key)
            {
                var value = row[key];
                if (value is DateTime date) return date;
                return DateTime.TryParse(Convert.ToString(value), out var parsed) ? parsed : DateTime.MinValue;
            }

            var total = rows.Count;
            var passed = rows.Count(row => row["repaired_time"] is not null && row["repaired_time"] is not DBNull);
            var pending = Math.Max(0, total - passed);
            var repairedDurations = rows
                .Where(row => row["repaired_time"] is not null && row["repaired_time"] is not DBNull)
                .Select(row => (ReadDate(row, "repaired_time") - ReadDate(row, "failed_time")).TotalMinutes)
                .Where(minutes => minutes >= 0)
                .ToList();
            var avgRepairMinutes = repairedDurations.Count == 0 ? 0 : Math.Round(repairedDurations.Average(), 1);

            object ToSn(Dictionary<string, object?> row) => new
            {
                snNumber = Read(row, "sn"),
                status = row["repaired_time"] is null || row["repaired_time"] is DBNull ? "Pending" : "Passed",
                partNumber = Read(row, "pn"),
                workOrder = Read(row, "wo"),
                repairStation = Read(row, "repair_station_name"),
                failureRemark = Read(row, "failure_remark"),
                failedTime = ReadDate(row, "failed_time") == DateTime.MinValue ? "" : ReadDate(row, "failed_time").ToString("yyyy-MM-dd HH:mm:ss"),
                repairedTime = ReadDate(row, "repaired_time") == DateTime.MinValue ? "" : ReadDate(row, "repaired_time").ToString("yyyy-MM-dd HH:mm:ss")
            };

            object BuildBucket(string label, IEnumerable<Dictionary<string, object?>> bucketRows)
            {
                var list = bucketRows.ToList();
                var bucketPassed = list.Count(row => row["repaired_time"] is not null && row["repaired_time"] is not DBNull);
                return new
                {
                    label,
                    pending = Math.Max(0, list.Count - bucketPassed),
                    passed = bucketPassed,
                    total = list.Count,
                    sns = list.Select(ToSn).ToArray()
                };
            }

            var stationBuckets = rows
                .GroupBy(row => string.IsNullOrWhiteSpace(Read(row, "repair_station_code")) ? Read(row, "repair_station_name") : Read(row, "repair_station_code"))
                .OrderByDescending(group => group.Count())
                .Select(group => BuildBucket(group.Key, group))
                .ToArray();

            var days = Enumerable.Range(0, (toDate - fromDate).Days + 1)
                .Select(offset => fromDate.AddDays(offset))
                .ToArray();
            var dayBuckets = days
                .Select(day => BuildBucket(day.ToString("dd MMM"), rows.Where(row => ReadDate(row, "failed_time").Date == day.Date)))
                .ToArray();

            var remarkValues = rows
                .SelectMany(row => (string.IsNullOrWhiteSpace(Read(row, "failure_remark")) ? "No Remark" : Read(row, "failure_remark"))
                    .Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToList();

            var remarkRows = remarkValues
                .GroupBy(value => string.IsNullOrWhiteSpace(value) ? "No Remark" : value)
                .OrderByDescending(group => group.Count())
                .Take(8)
                .Select(group => new
                {
                    remark = group.Key,
                    count = group.Count(),
                    percentage = remarkValues.Count == 0 ? 0 : Math.Round(group.Count() * 100.0 / remarkValues.Count, 2)
                })
                .ToArray();

            return Results.Json(new
            {
                lastUpdated = DateTime.Now,
                summary = new
                {
                    total,
                    pending,
                    passed,
                    avgRepairMinutes,
                    pendingPercent = total == 0 ? 0 : Math.Round(pending * 100.0 / total, 2),
                    passedPercent = total == 0 ? 0 : Math.Round(passed * 100.0 / total, 2)
                },
                chart = viewBy == "day" ? dayBuckets : stationBuckets,
                stationBuckets,
                dayBuckets,
                failureRemarks = remarkRows,
                sns = rows.Select(ToSn).ToArray()
            });
        });

        app.MapGet("/api/reports/sampling-dashboard/options", async (HttpRequest request) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);

            var sites = await QueryRowsAsync(
                connection,
                """
                SELECT MIN(COALESCE(w.site_id, 0)) AS id, w.site_name AS name, w.plant
                FROM workflow_work_orders w
                WHERE BTRIM(COALESCE(w.site_name, '')) <> ''
                GROUP BY w.site_name, w.plant
                ORDER BY w.plant ASC, name ASC
                """);

            var samplingStations = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT station_code, COALESCE(NULLIF(BTRIM(station_name), ''), station_code) AS station_name
                FROM (
                  SELECT cfg.station_code, cfg.station_name
                  FROM workflow_station_sampling cfg
                  WHERE BTRIM(COALESCE(cfg.station_code, '')) <> ''

                  UNION ALL

                  SELECT route.station_code, route.station_name
                  FROM workflow_routing_steps route
                  WHERE BTRIM(COALESCE(route.station_code, '')) <> ''

                  UNION ALL

                  SELECT ms.masterstation_code AS station_code, ms.masterstation_name AS station_name
                  FROM masterstation ms
                  WHERE BTRIM(COALESCE(ms.masterstation_code, '')) <> ''
                ) source
                ORDER BY station_code ASC
                """);

            var samplingTypes = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT sampling_type
                FROM (
                  SELECT sampling_type
                  FROM workflow_station_sampling
                  WHERE BTRIM(COALESCE(sampling_type, '')) <> ''

                  UNION ALL SELECT 'PERIODIC'
                  UNION ALL SELECT 'PERIODIC_TIME'
                  UNION ALL SELECT 'RANDOM'
                  UNION ALL SELECT 'LOT'
                  UNION ALL SELECT 'FIRST_PIECE'
                ) source
                ORDER BY sampling_type ASC
                """);

            var partNumbers = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT p.id, p.pn, p.description
                FROM workflow_part_numbers p
                WHERE BTRIM(COALESCE(p.pn, '')) <> ''
                ORDER BY p.pn ASC
                """);

            var workOrders = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT w.id, w.wo, p.pn AS part_number, w.site_name AS site
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                WHERE BTRIM(COALESCE(w.wo, '')) <> ''
                ORDER BY w.wo ASC
                """);

            return Results.Json(new { sites, samplingStations, samplingTypes, partNumbers, workOrders });
        });

        app.MapGet("/api/reports/sampling-dashboard/data", async (HttpRequest request) =>
        {
            var site = request.Query["site"].ToString().Trim();
            var plant = request.Query["plant"].ToString().Trim();
            var station = request.Query["station"].ToString().Trim();
            var pn = request.Query["pn"].ToString().Trim();
            var wo = request.Query["wo"].ToString().Trim();
            var status = request.Query["status"].ToString().Trim();
            var samplingType = request.Query["samplingType"].ToString().Trim();
            var snSearch = request.Query["sn"].ToString().Trim();
            var fromDateRaw = request.Query["fromDate"].ToString().Trim();
            var toDateRaw = request.Query["toDate"].ToString().Trim();
            var viewBy = request.Query["viewBy"].ToString().Trim().ToLowerInvariant();

            var fromDate = DateTime.TryParse(fromDateRaw, out var parsedFrom) ? parsedFrom.Date : DateTime.Today;
            var toDate = DateTime.TryParse(toDateRaw, out var parsedTo) ? parsedTo.Date : DateTime.Today;
            if (toDate < fromDate)
            {
                (fromDate, toDate) = (toDate, fromDate);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);

            var rows = await QueryRowsAsync(
                connection,
                """
                WITH sampling_events AS (
                  SELECT
                    event.id AS event_id,
                    event.workflow_serial_id,
                    sn.sn,
                    sn.rsn,
                    sn.status AS serial_status,
                    p.pn,
                    w.wo,
                    w.plant,
                    w.site_name,
                    event.station_code,
                    COALESCE(cfg.station_name, route.station_name, event.station_code) AS station_name,
                    event.sampling_type,
                    event.interval_time_minutes,
                    event.created_at AS requested_time,
                    pass_log.created_at AS passed_time
                  FROM workflow_station_sampling_events event
                  JOIN workflow_serial_numbers sn ON sn.id = event.workflow_serial_id
                  JOIN workflow_work_orders w ON w.id = event.workflow_work_order_id
                  JOIN workflow_part_numbers p ON p.id = event.workflow_part_id
                  LEFT JOIN workflow_station_sampling cfg
                    ON cfg.workflow_part_id = event.workflow_part_id
                   AND UPPER(BTRIM(cfg.station_code)) = UPPER(BTRIM(event.station_code))
                  LEFT JOIN workflow_routing_steps route
                    ON route.workflow_part_id = event.workflow_part_id
                   AND UPPER(BTRIM(route.station_code)) = UPPER(BTRIM(event.station_code))
                  LEFT JOIN LATERAL (
                    SELECT log.created_at
                    FROM workflow_serial_station_logs log
                    WHERE log.workflow_serial_id = event.workflow_serial_id
                      AND UPPER(log.action_result) = 'PASS'
                      AND UPPER(BTRIM(log.station_code)) = UPPER(BTRIM(event.station_code))
                      AND log.created_at >= event.created_at
                    ORDER BY log.created_at ASC, log.id ASC
                    LIMIT 1
                  ) pass_log ON TRUE
                  WHERE event.created_at::date >= @fromDate::date
                    AND event.created_at::date <= @toDate::date
                )
                SELECT *
                FROM sampling_events
                WHERE (@site = '' OR UPPER(BTRIM(site_name)) = UPPER(BTRIM(@site)))
                  AND (@plant = '' OR UPPER(BTRIM(COALESCE(plant, ''))) = UPPER(BTRIM(@plant)))
                  AND (@station = '' OR UPPER(BTRIM(station_code)) = UPPER(BTRIM(@station)) OR UPPER(BTRIM(station_name)) = UPPER(BTRIM(@station)))
                  AND (@pn = '' OR UPPER(BTRIM(pn)) = UPPER(BTRIM(@pn)))
                  AND (@wo = '' OR UPPER(BTRIM(wo)) = UPPER(BTRIM(@wo)))
                  AND (@samplingType = '' OR UPPER(BTRIM(sampling_type)) = UPPER(BTRIM(@samplingType)))
                  AND (@sn = '' OR UPPER(sn) LIKE UPPER('%' || @sn || '%') OR UPPER(rsn) LIKE UPPER('%' || @sn || '%'))
                  AND (
                    @status = ''
                    OR (@status = 'Pending' AND passed_time IS NULL)
                    OR (@status = 'Passed' AND passed_time IS NOT NULL)
                  )
                ORDER BY requested_time DESC, event_id DESC
                """,
                ("site", site),
                ("plant", plant),
                ("station", station),
                ("pn", pn),
                ("wo", wo),
                ("samplingType", samplingType),
                ("sn", snSearch),
                ("status", status),
                ("fromDate", fromDate),
                ("toDate", toDate));

            static string Read(Dictionary<string, object?> row, string key) => Convert.ToString(row[key]) ?? string.Empty;
            static DateTime ReadDate(Dictionary<string, object?> row, string key)
            {
                var value = row[key];
                if (value is DateTime date) return date;
                return DateTime.TryParse(Convert.ToString(value), out var parsed) ? parsed : DateTime.MinValue;
            }

            var total = rows.Count;
            var passed = rows.Count(row => row["passed_time"] is not null && row["passed_time"] is not DBNull);
            var pending = Math.Max(0, total - passed);
            var sampleDurations = rows
                .Where(row => row["passed_time"] is not null && row["passed_time"] is not DBNull)
                .Select(row => (ReadDate(row, "passed_time") - ReadDate(row, "requested_time")).TotalMinutes)
                .Where(minutes => minutes >= 0)
                .ToList();
            var avgSampleMinutes = sampleDurations.Count == 0 ? 0 : Math.Round(sampleDurations.Average(), 1);

            object ToSn(Dictionary<string, object?> row) => new
            {
                snNumber = Read(row, "sn"),
                rsn = Read(row, "rsn"),
                status = row["passed_time"] is null || row["passed_time"] is DBNull ? "Pending" : "Passed",
                partNumber = Read(row, "pn"),
                workOrder = Read(row, "wo"),
                station = string.IsNullOrWhiteSpace(Read(row, "station_name")) ? Read(row, "station_code") : Read(row, "station_name"),
                samplingType = Read(row, "sampling_type"),
                requestedTime = ReadDate(row, "requested_time") == DateTime.MinValue ? "" : ReadDate(row, "requested_time").ToString("yyyy-MM-dd HH:mm:ss"),
                passedTime = ReadDate(row, "passed_time") == DateTime.MinValue ? "" : ReadDate(row, "passed_time").ToString("yyyy-MM-dd HH:mm:ss")
            };

            object BuildBucket(string label, IEnumerable<Dictionary<string, object?>> bucketRows)
            {
                var list = bucketRows.ToList();
                var bucketPassed = list.Count(row => row["passed_time"] is not null && row["passed_time"] is not DBNull);
                return new
                {
                    label,
                    pending = Math.Max(0, list.Count - bucketPassed),
                    passed = bucketPassed,
                    total = list.Count,
                    sns = list.Select(ToSn).ToArray()
                };
            }

            var stationBuckets = rows
                .GroupBy(row => string.IsNullOrWhiteSpace(Read(row, "station_code")) ? Read(row, "station_name") : Read(row, "station_code"))
                .OrderByDescending(group => group.Count())
                .Select(group => BuildBucket(group.Key, group))
                .ToArray();

            var days = Enumerable.Range(0, (toDate - fromDate).Days + 1)
                .Select(offset => fromDate.AddDays(offset))
                .ToArray();
            var dayBuckets = days
                .Select(day => BuildBucket(day.ToString("dd MMM"), rows.Where(row => ReadDate(row, "requested_time").Date == day.Date)))
                .ToArray();

            var typeRows = rows
                .GroupBy(row => string.IsNullOrWhiteSpace(Read(row, "sampling_type")) ? "Unknown" : Read(row, "sampling_type"))
                .OrderByDescending(group => group.Count())
                .Select(group => new
                {
                    samplingType = group.Key,
                    count = group.Count(),
                    percentage = total == 0 ? 0 : Math.Round(group.Count() * 100.0 / total, 2)
                })
                .ToArray();

            return Results.Json(new
            {
                lastUpdated = DateTime.Now,
                summary = new
                {
                    total,
                    pending,
                    passed,
                    avgSampleMinutes,
                    pendingPercent = total == 0 ? 0 : Math.Round(pending * 100.0 / total, 2),
                    passedPercent = total == 0 ? 0 : Math.Round(passed * 100.0 / total, 2)
                },
                chart = viewBy == "day" ? dayBuckets : stationBuckets,
                stationBuckets,
                dayBuckets,
                samplingTypes = typeRows,
                sns = rows.Select(ToSn).ToArray()
            });
        });

        app.MapGet("/api/reports/todays-dashboard/options", async (HttpRequest request) =>
        {
            var site = request.Query["site"].ToString().Trim();
            var station = request.Query["station"].ToString().Trim();
            var stationValues = ReadQueryList(request, "stationIds");
            if (stationValues.Length == 0 && !string.IsNullOrWhiteSpace(station))
            {
                stationValues = new[] { station };
            }
            var pn = request.Query["pn"].ToString().Trim();

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);

            var sites = await QueryRowsAsync(
                connection,
                """
                SELECT MIN(COALESCE(site_id, 0)) AS id, site_name AS name
                FROM workflow_work_orders
                WHERE BTRIM(COALESCE(site_name, '')) <> ''
                GROUP BY site_name
                ORDER BY name ASC
                """);

            var filters = new List<string>();
            var parameters = new List<(string Name, object? Value)>();

            if (!string.IsNullOrWhiteSpace(site))
            {
                filters.Add("UPPER(BTRIM(w.site_name)) = UPPER(BTRIM(@site))");
                parameters.Add(("site", site));
            }

            if (stationValues.Length > 0)
            {
                filters.Add(
                    """
                    EXISTS (
                        SELECT 1
                        FROM unnest(@stationValues) selected_station
                        WHERE UPPER(BTRIM(r.station_code)) = UPPER(BTRIM(selected_station))
                           OR UPPER(BTRIM(r.station_name)) = UPPER(BTRIM(selected_station))
                    )
                    """);
                parameters.Add(("stationValues", stationValues));
            }

            if (!string.IsNullOrWhiteSpace(pn))
            {
                filters.Add("UPPER(BTRIM(p.pn)) = UPPER(BTRIM(@pn))");
                parameters.Add(("pn", pn));
            }

            var whereSql = filters.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filters);

            var stations = await QueryRowsAsync(
                connection,
                $"""
                SELECT DISTINCT r.station_code, r.station_name
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                JOIN workflow_routing_steps r ON r.workflow_part_id = p.id
                {(string.IsNullOrWhiteSpace(site) ? string.Empty : "WHERE UPPER(BTRIM(w.site_name)) = UPPER(BTRIM(@site))")}
                ORDER BY r.station_code ASC
                """,
                string.IsNullOrWhiteSpace(site) ? Array.Empty<(string Name, object? Value)>() : new[] { ("site", (object?)site) });

            var partNumbers = await QueryRowsAsync(
                connection,
                $"""
                SELECT DISTINCT p.id, p.pn, p.description
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                JOIN workflow_routing_steps r ON r.workflow_part_id = p.id
                {whereSql}
                ORDER BY p.pn ASC
                """,
                parameters.ToArray());

            var workOrders = await QueryRowsAsync(
                connection,
                $"""
                SELECT DISTINCT w.id, w.wo, p.pn AS part_number, w.site_name AS site
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                JOIN workflow_routing_steps r ON r.workflow_part_id = p.id
                {whereSql}
                ORDER BY w.wo ASC
                """,
                parameters.ToArray());

            return Results.Json(new { sites, stations, partNumbers, workOrders });
        });

        app.MapGet("/api/reports/todays-dashboard/data", async (HttpRequest request) =>
        {
            var site = request.Query["site"].ToString().Trim();
            var station = request.Query["station"].ToString().Trim();
            var stationValues = ReadQueryList(request, "stationIds");
            if (stationValues.Length == 0 && !string.IsNullOrWhiteSpace(station))
            {
                stationValues = new[] { station };
            }
            var pn = request.Query["pn"].ToString().Trim();
            var wo = request.Query["wo"].ToString().Trim();
            var fromDateRaw = request.Query["fromDate"].ToString().Trim();
            var toDateRaw = request.Query["toDate"].ToString().Trim();
            var xAxis = request.Query["xAxis"].ToString().Trim().ToLowerInvariant();

            var fromDate = DateTime.TryParse(fromDateRaw, out var parsedFrom)
                ? parsedFrom.Date
                : DateTime.Today;
            var toDate = DateTime.TryParse(toDateRaw, out var parsedTo)
                ? parsedTo.Date
                : DateTime.Today;

            if (toDate < fromDate)
            {
                (fromDate, toDate) = (toDate, fromDate);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);

            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  sn.id,
                  sn.sn,
                  sn.rsn,
                  sn.status AS serial_status,
                  sn.condition,
                  sn.current_station_code,
                  sn.created_at AS serial_created_at,
                  sn.last_moved_at,
                  p.pn,
                  w.wo,
                  w.site_name,
                  COALESCE(latest_log.station_code, sn.current_station_code, '') AS station_code,
                  COALESCE(latest_log.station_name, route.station_name, latest_log.station_code, sn.current_station_code, '') AS station_name,
                  latest_log.action_result,
                  latest_log.remark,
                  latest_log.changed_by,
                  latest_log.created_at AS log_created_at,
                  COALESCE(latest_log.created_at, sn.last_moved_at, sn.created_at) AS event_time
                FROM workflow_serial_numbers sn
                JOIN workflow_work_orders w ON w.id = sn.workflow_work_order_id
                JOIN workflow_part_numbers p ON p.id = sn.workflow_part_id
                LEFT JOIN LATERAL (
                  SELECT l.station_code, l.station_name, l.action_result, l.remark, l.changed_by, l.created_at
                  FROM workflow_serial_station_logs l
                  WHERE l.workflow_serial_id = sn.id
                    AND (cardinality(@stationValues) = 0 OR EXISTS (
                      SELECT 1
                      FROM unnest(@stationValues) selected_station
                      WHERE UPPER(BTRIM(l.station_code)) = UPPER(BTRIM(selected_station))
                    ))
                  ORDER BY l.created_at DESC, l.id DESC
                  LIMIT 1
                ) latest_log ON TRUE
                LEFT JOIN workflow_routing_steps route
                  ON route.workflow_part_id = sn.workflow_part_id
                 AND UPPER(BTRIM(route.station_code)) = UPPER(BTRIM(COALESCE(latest_log.station_code, sn.current_station_code, '')))
                WHERE (@site = '' OR UPPER(BTRIM(w.site_name)) = UPPER(BTRIM(@site)))
                  AND (@pn = '' OR UPPER(BTRIM(p.pn)) = UPPER(BTRIM(@pn)))
                  AND (@wo = '' OR UPPER(BTRIM(w.wo)) = UPPER(BTRIM(@wo)))
                  AND (
                    cardinality(@stationValues) = 0
                    OR latest_log.station_code IS NOT NULL
                    OR EXISTS (
                      SELECT 1
                      FROM unnest(@stationValues) selected_station
                      WHERE UPPER(BTRIM(sn.current_station_code)) = UPPER(BTRIM(selected_station))
                    )
                  )
                  AND COALESCE(latest_log.created_at, sn.last_moved_at, sn.created_at)::date >= @fromDate::date
                  AND COALESCE(latest_log.created_at, sn.last_moved_at, sn.created_at)::date <= @toDate::date
                ORDER BY event_time DESC, sn.id DESC
                """,
                ("site", site),
                ("stationValues", stationValues),
                ("pn", pn),
                ("wo", wo),
                ("fromDate", fromDate),
                ("toDate", toDate));

            string Read(Dictionary<string, object?> row, string key) => Convert.ToString(row[key]) ?? string.Empty;
            DateTime ReadDate(Dictionary<string, object?> row, string key)
            {
                var value = row[key];
                if (value is DateTime date) return date;
                return DateTime.TryParse(Convert.ToString(value), out var parsed) ? parsed : DateTime.MinValue;
            }

            string NormalizeStatus(Dictionary<string, object?> row)
            {
                var action = Read(row, "action_result").Trim().ToUpperInvariant();
                var status = Read(row, "serial_status").Trim().ToUpperInvariant();
                var condition = Read(row, "condition").Trim().ToUpperInvariant();
                var remark = Read(row, "remark").Trim().ToUpperInvariant();

                if (remark.Contains("REWORK")) return "Rework";
                if (remark.Contains("NFF") || remark.Contains("NO FAULT")) return "NFF";
                if (action == "PASS" || status == "COMPLETED") return "Pass";
                if (action == "FAIL" || status == "FAILED" || condition is "NG" or "FAIL") return "Fail";
                return "Pending";
            }

            string ResultText(Dictionary<string, object?> row, string status)
            {
                var remark = Read(row, "remark").Trim();
                if (!string.IsNullOrWhiteSpace(remark)) return remark;
                return status switch
                {
                    "Pass" => "All Test Passed",
                    "Fail" => "Fail",
                    "Rework" => "Rework Required",
                    "NFF" => "No Fault Found",
                    _ => "In Process"
                };
            }

            object ToSerial(Dictionary<string, object?> row)
            {
                var status = NormalizeStatus(row);
                var createdAt = ReadDate(row, "serial_created_at");
                var eventTime = ReadDate(row, "event_time");
                return new
                {
                    sn = Read(row, "sn"),
                    pn = Read(row, "pn"),
                    wo = Read(row, "wo"),
                    station = Read(row, "station_code"),
                    stationName = Read(row, "station_name"),
                    @operator = string.IsNullOrWhiteSpace(Read(row, "changed_by")) ? "-" : Read(row, "changed_by"),
                    status,
                    startTime = createdAt == DateTime.MinValue ? "-" : createdAt.ToString("hh:mm:ss tt"),
                    endTime = eventTime == DateTime.MinValue || status == "Pending" ? "-" : eventTime.ToString("hh:mm:ss tt"),
                    cycleSeconds = eventTime == DateTime.MinValue || createdAt == DateTime.MinValue ? 0 : Math.Max(0, Math.Round((eventTime - createdAt).TotalSeconds, 1)),
                    result = ResultText(row, status),
                    eventTime
                };
            }

            object BuildBucket(string label, IEnumerable<Dictionary<string, object?>> bucketRows)
            {
                var list = bucketRows.ToList();
                var statuses = list.Select(NormalizeStatus).ToList();
                return new
                {
                    label,
                    pass = statuses.Count(value => value == "Pass"),
                    fail = statuses.Count(value => value == "Fail"),
                    rework = statuses.Count(value => value == "Rework"),
                    nff = statuses.Count(value => value == "NFF"),
                    pending = statuses.Count(value => value == "Pending"),
                    sns = list.Select(ToSerial).ToArray()
                };
            }

            object BuildFpyPoint(string label, IEnumerable<Dictionary<string, object?>> bucketRows)
            {
                var statuses = bucketRows.Select(NormalizeStatus).ToList();
                var total = statuses.Count;
                return new
                {
                    label,
                    fpy = total == 0 ? 0 : Math.Round(statuses.Count(value => value == "Pass") * 100.0 / total, 2)
                };
            }

            var hourlyBuckets = Enumerable.Range(0, 24)
                .Select(hour =>
                {
                    var label = DateTime.Today.AddHours(hour).ToString("hh tt");
                    var bucketRows = rows.Where(row => ReadDate(row, "event_time").Hour == hour);
                    return BuildBucket(label, bucketRows);
                })
                .ToArray();

            var dailyLabel = fromDate == toDate
                ? toDate.ToString("dd MMM yyyy")
                : $"{fromDate:dd MMM yyyy} - {toDate:dd MMM yyyy}";
            var dailyBucket = BuildBucket(dailyLabel, rows);
            var dailyBuckets = Enumerable.Range(0, (toDate - fromDate).Days + 1)
                .Select(offset =>
                {
                    var day = fromDate.AddDays(offset);
                    var bucketRows = rows.Where(row => ReadDate(row, "event_time").Date == day.Date);
                    return BuildBucket(day.ToString("dd MMM"), bucketRows);
                })
                .ToArray();

            var fpyTrend = xAxis == "day"
                ? Enumerable.Range(0, (toDate - fromDate).Days + 1)
                    .Select(offset =>
                    {
                        var day = fromDate.AddDays(offset);
                        var bucketRows = rows.Where(row => ReadDate(row, "event_time").Date == day.Date);
                        return BuildFpyPoint(day.ToString("dd MMM"), bucketRows);
                    })
                    .ToArray()
                : Enumerable.Range(0, 24)
                    .Select(hour =>
                    {
                        var label = DateTime.Today.AddHours(hour).ToString("hh tt");
                        var bucketRows = rows.Where(row => ReadDate(row, "event_time").Hour == hour);
                        return BuildFpyPoint(label, bucketRows);
                    })
                    .ToArray();

            var statusValues = rows.Select(NormalizeStatus).ToList();
            var total = rows.Count;
            var failRows = rows.Where(row => NormalizeStatus(row) == "Fail").ToList();
            var topFailingStation = failRows
                .GroupBy(row => Read(row, "station_code"))
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();
            var highestLoadStation = rows
                .GroupBy(row => Read(row, "station_code"))
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();

            return Results.Json(new
            {
                lastUpdated = DateTime.Now,
                summary = new
                {
                    totalSn = total,
                    passCount = statusValues.Count(value => value == "Pass"),
                    failCount = statusValues.Count(value => value == "Fail"),
                    reworkCount = statusValues.Count(value => value == "Rework"),
                    nffCount = statusValues.Count(value => value == "NFF"),
                    pendingCount = statusValues.Count(value => value == "Pending"),
                    fpy = total == 0 ? 0 : Math.Round(statusValues.Count(value => value == "Pass") * 100.0 / total, 2),
                    wipCount = statusValues.Count(value => value is "Pending" or "Rework" or "NFF"),
                    avgCycleTime = rows.Count == 0 ? 0 : Math.Round(rows
                        .Select(row => (ReadDate(row, "event_time") - ReadDate(row, "serial_created_at")).TotalSeconds)
                        .Where(seconds => seconds >= 0)
                        .DefaultIfEmpty(0)
                        .Average(), 1),
                    topFailingStation = topFailingStation?.Key ?? "-",
                    topFailingStationFails = topFailingStation?.Count() ?? 0,
                    highestLoadStation = highestLoadStation?.Key ?? "-",
                    highestLoadStationSn = highestLoadStation?.Count() ?? 0
                },
                hourlyBuckets,
                dailyBuckets,
                dailyBucket,
                fpyTrend,
                failureByStation = failRows
                    .GroupBy(row => string.IsNullOrWhiteSpace(Read(row, "station_code")) ? "-" : Read(row, "station_code"))
                    .OrderByDescending(group => group.Count())
                    .Take(10)
                    .Select(group => new { station = group.Key, count = group.Count() })
                    .ToArray()
            });
        });

        app.MapGet("/api/reports/work-order-tree", async (HttpRequest request) =>
        {
            var wo = request.Query["wo"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(wo))
            {
                return JsonMessage("Work order number is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);

            var workOrderRows = await QueryRowsAsync(
                connection,
                """
                SELECT w.id,
                       w.wo,
                       w.qty,
                       w.status,
                       w.plant,
                       w.site_name,
                       w.revision,
                       w.lot,
                       p.id AS workflow_part_id,
                       p.pn,
                       p.description,
                       p.item_type
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                WHERE UPPER(BTRIM(w.wo)) = UPPER(BTRIM(@wo))
                ORDER BY w.updated_at DESC, w.id DESC
                LIMIT 1
                """,
                ("wo", wo));
            if (workOrderRows.Count == 0)
            {
                return JsonMessage("Work order not found", 404);
            }

            var workOrder = workOrderRows[0];
            var workflowWorkOrderId = Convert.ToInt32(workOrder["id"]);
            var workflowPartId = Convert.ToInt32(workOrder["workflow_part_id"]);

            var routingRows = await QueryRowsAsync(
                connection,
                """
                SELECT id,
                       station_order,
                       station_code,
                       station_name,
                       sample_mode,
                       report_mode
                FROM workflow_routing_steps
                WHERE workflow_part_id = @workflowPartId
                ORDER BY station_order ASC, id ASC
                """,
                ("workflowPartId", workflowPartId));

            var serialRows = await QueryRowsAsync(
                connection,
                """
                SELECT sn.id,
                       sn.sn,
                       sn.rsn,
                       sn.status,
                       sn.condition,
                       sn.current_station_code,
                       sn.current_station_order,
                       sn.created_at,
                       sn.updated_at,
                       sn.last_moved_at
                FROM workflow_serial_numbers sn
                WHERE sn.workflow_work_order_id = @workflowWorkOrderId
                ORDER BY sn.current_station_order NULLS LAST, sn.sn ASC, sn.id ASC
                """,
                ("workflowWorkOrderId", workflowWorkOrderId));

            var serialsByStation = serialRows
                .Where(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["current_station_code"])))
                .GroupBy(row => Convert.ToString(row["current_station_code"]) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var stationCounts = routingRows
                .Select(row => serialsByStation.TryGetValue(Convert.ToString(row["station_code"]) ?? string.Empty, out var serials) ? serials.Count : 0)
                .ToList();
            var highestCount = stationCounts.Count > 0 ? stationCounts.Max() : 0;

            var stations = routingRows.Select(row =>
            {
                var stationCode = Convert.ToString(row["station_code"]) ?? string.Empty;
                var serials = serialsByStation.TryGetValue(stationCode, out var values) ? values : [];

                return new
                {
                    id = row["id"],
                    station_order = row["station_order"],
                    station_code = stationCode,
                    station_name = row["station_name"],
                    sample_mode = row["sample_mode"],
                    report_mode = row["report_mode"],
                    sn_count = serials.Count,
                    is_highest_count = highestCount > 0 && serials.Count == highestCount,
                    serials = serials.Select(serial => new
                    {
                        id = serial["id"],
                        sn = serial["sn"],
                        rsn = serial["rsn"],
                        status = serial["status"],
                        condition = serial["condition"],
                        current_station_code = serial["current_station_code"],
                        current_station_order = serial["current_station_order"],
                        created_at = serial["created_at"],
                        updated_at = serial["updated_at"],
                        last_moved_at = serial["last_moved_at"]
                    }).ToList()
                };
            }).ToList();

            var completedCount = serialRows.Count(row => string.Equals(Convert.ToString(row["status"]), "Completed", StringComparison.OrdinalIgnoreCase));
            var failedCount = serialRows.Count(row => string.Equals(Convert.ToString(row["status"]), "Failed", StringComparison.OrdinalIgnoreCase));
            var packedClosedRows = new List<Dictionary<string, object?>>();
            if (serialRows.Count > 0)
            {
                packedClosedRows = await QueryRowsAsync(
                    connection,
                    """
                    SELECT COUNT(DISTINCT i.workflow_serial_id)::int AS count
                    FROM workflow_multibox_items i
                    JOIN workflow_multiboxes b ON b.id = i.box_id
                    WHERE b.workflow_work_order_id = @workflowWorkOrderId
                      AND UPPER(BTRIM(b.status)) = 'CLOSED'
                    """,
                    ("workflowWorkOrderId", workflowWorkOrderId));
            }

            var packedClosedSerialCount = packedClosedRows.Count > 0 ? Convert.ToInt32(packedClosedRows[0]["count"] ?? 0) : 0;
            var cartonStatus = serialRows.Count > 0 && packedClosedSerialCount >= serialRows.Count ? "Completed" : "Pending";

            return Results.Json(new
            {
                workOrder = new
                {
                    id = workOrder["id"],
                    wo = workOrder["wo"],
                    qty = workOrder["qty"],
                    status = workOrder["status"],
                    plant = workOrder["plant"],
                    site_name = workOrder["site_name"],
                    revision = workOrder["revision"],
                    lot = workOrder["lot"]
                },
                partNumber = new
                {
                    id = workOrder["workflow_part_id"],
                    pn = workOrder["pn"],
                    description = workOrder["description"],
                    item_type = workOrder["item_type"]
                },
                summary = new
                {
                    total_serials = serialRows.Count,
                    completed = completedCount,
                    failed = failedCount,
                    in_stations = serialRows.Count - completedCount,
                    highest_station_count = highestCount,
                    carton_status = cartonStatus,
                    carton_closed_serials = packedClosedSerialCount
                },
                stations
            });
        });

        app.MapGet("/api/reports/activity-quality/options", async (HttpRequest request) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);

            var now = DateTime.Now;
            var fromDate = ReadDateQuery(request, "fromDate") ?? now.Date;
            var toDate = ReadDateQuery(request, "toDate") ?? now;
            if (toDate.Date == toDate)
            {
                toDate = toDate.Date.AddDays(1).AddTicks(-1);
            }

            var startHour = ReadIntQuery(request, "startHour");
            if (startHour is >= 0 and <= 23)
            {
                fromDate = fromDate.Date.AddHours(startHour.Value);
            }

            var siteValues = ReadQueryList(request, "siteIds");
            var stationValues = ReadQueryList(request, "stationIds");
            var productLineValues = ReadQueryList(request, "productLineIds");
            var partNumbers = ReadQueryList(request, "partNumbers");
            var workOrders = ReadQueryList(request, "workOrders");
            var pcValues = ReadQueryList(request, "pcIds");

            const string activityCte = """
                WITH activity AS (
                  SELECT
                    l.created_at AS date_time,
                    sn.site_id::text AS site_id,
                    COALESCE(s.name, '') AS site,
                    l.station_code AS station,
                    COALESCE(l.station_name, l.station_code) AS station_name,
                    COALESCE(pl.description, pl.code, '') AS product_line,
                    i.pn AS part_number,
                    wo.wo AS work_order,
                    COALESCE(l.pc_name, '') AS pc,
                    COALESCE(l.changed_by, '') AS user_name
                  FROM serial_station_logs l
                  JOIN serial_numbers sn ON sn.id = l.serial_id
                  JOIN work_orders wo ON wo.id = l.work_order_id
                  JOIN items i ON i.id = l.item_id
                  LEFT JOIN sites s ON s.id = sn.site_id
                  LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                  WHERE UPPER(l.action_result) IN ('PASS', 'FAIL')

                  UNION ALL

                  SELECT
                    l.created_at AS date_time,
                    w.site_id::text AS site_id,
                    COALESCE(w.site_name, '') AS site,
                    l.station_code AS station,
                    COALESCE(l.station_name, l.station_code) AS station_name,
                    COALESCE(p.item_type, '') AS product_line,
                    p.pn AS part_number,
                    w.wo AS work_order,
                    '' AS pc,
                    COALESCE(l.changed_by, '') AS user_name
                  FROM workflow_serial_station_logs l
                  JOIN workflow_serial_numbers sn ON sn.id = l.workflow_serial_id
                  JOIN workflow_part_numbers p ON p.id = l.workflow_part_id
                  JOIN workflow_work_orders w ON w.id = l.workflow_work_order_id
                  WHERE UPPER(l.action_result) IN ('PASS', 'FAIL')
                ),
                filtered AS (
                  SELECT *
                  FROM activity
                  WHERE date_time >= @fromDate
                    AND date_time <= @toDate
                    AND (cardinality(@siteValues) = 0 OR site_id = ANY(@siteValues) OR site = ANY(@siteValues))
                    AND (cardinality(@stationValues) = 0 OR station = ANY(@stationValues) OR station_name = ANY(@stationValues))
                    AND (cardinality(@productLineValues) = 0 OR product_line = ANY(@productLineValues))
                    AND (cardinality(@partNumbers) = 0 OR part_number = ANY(@partNumbers))
                    AND (cardinality(@workOrders) = 0 OR work_order = ANY(@workOrders))
                    AND (cardinality(@pcValues) = 0 OR pc = ANY(@pcValues))
                )
                """;

            var parameters = new (string Name, object? Value)[]
            {
                ("fromDate", fromDate),
                ("toDate", toDate),
                ("siteValues", siteValues),
                ("stationValues", stationValues),
                ("productLineValues", productLineValues),
                ("partNumbers", partNumbers),
                ("workOrders", workOrders),
                ("pcValues", pcValues)
            };

            var sites = await QueryRowsAsync(
                connection,
                """
                SELECT MIN(COALESCE(site_id, 0)) AS id, site_name AS name
                FROM workflow_work_orders
                WHERE BTRIM(COALESCE(site_name, '')) <> ''
                GROUP BY site_name
                ORDER BY name ASC
                """);

            var stations = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT r.station_code, r.station_name
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                JOIN workflow_routing_steps r ON r.workflow_part_id = p.id
                WHERE (cardinality(@siteValues) = 0 OR w.site_id::text = ANY(@siteValues) OR w.site_name = ANY(@siteValues))
                ORDER BY station_code ASC
                """,
                parameters);

            var productLines = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT COALESCE(NULLIF(p.item_type, ''), pl.description, pl.code, '') AS value
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                LEFT JOIN items i ON UPPER(BTRIM(i.pn)) = UPPER(BTRIM(p.pn))
                LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                JOIN workflow_routing_steps r ON r.workflow_part_id = p.id
                WHERE (cardinality(@siteValues) = 0 OR w.site_id::text = ANY(@siteValues) OR w.site_name = ANY(@siteValues))
                  AND (cardinality(@stationValues) = 0 OR r.station_code = ANY(@stationValues) OR r.station_name = ANY(@stationValues))
                  AND BTRIM(COALESCE(NULLIF(p.item_type, ''), pl.description, pl.code, '')) <> ''
                ORDER BY value ASC
                """,
                parameters);

            var partNumbersResult = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT p.id, p.pn, p.description
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                LEFT JOIN items i ON UPPER(BTRIM(i.pn)) = UPPER(BTRIM(p.pn))
                LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                JOIN workflow_routing_steps r ON r.workflow_part_id = p.id
                WHERE (cardinality(@siteValues) = 0 OR w.site_id::text = ANY(@siteValues) OR w.site_name = ANY(@siteValues))
                  AND (cardinality(@stationValues) = 0 OR r.station_code = ANY(@stationValues) OR r.station_name = ANY(@stationValues))
                  AND (cardinality(@productLineValues) = 0 OR COALESCE(NULLIF(p.item_type, ''), pl.description, pl.code, '') = ANY(@productLineValues))
                  AND BTRIM(COALESCE(p.pn, '')) <> ''
                ORDER BY pn ASC
                """,
                parameters);

            var workOrdersResult = await QueryRowsAsync(
                connection,
                """
                SELECT DISTINCT w.id, w.wo, p.pn AS part_number, w.site_name AS site
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                LEFT JOIN items i ON UPPER(BTRIM(i.pn)) = UPPER(BTRIM(p.pn))
                LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                JOIN workflow_routing_steps r ON r.workflow_part_id = p.id
                WHERE (cardinality(@siteValues) = 0 OR w.site_id::text = ANY(@siteValues) OR w.site_name = ANY(@siteValues))
                  AND (cardinality(@stationValues) = 0 OR r.station_code = ANY(@stationValues) OR r.station_name = ANY(@stationValues))
                  AND (cardinality(@productLineValues) = 0 OR COALESCE(NULLIF(p.item_type, ''), pl.description, pl.code, '') = ANY(@productLineValues))
                  AND (cardinality(@partNumbers) = 0 OR p.pn = ANY(@partNumbers))
                  AND BTRIM(COALESCE(w.wo, '')) <> ''
                ORDER BY wo ASC
                """,
                parameters);

            var pcs = await QueryRowsAsync(
                connection,
                activityCte + """
                SELECT DISTINCT pc AS value
                FROM filtered
                WHERE BTRIM(pc) <> ''
                ORDER BY value ASC
                """,
                parameters);

            var users = await QueryRowsAsync(
                connection,
                activityCte + """
                SELECT DISTINCT user_name AS value
                FROM filtered
                WHERE BTRIM(user_name) <> ''
                ORDER BY value ASC
                """,
                parameters);

            return Results.Json(new
            {
                sites,
                stations,
                productLines,
                partNumbers = partNumbersResult,
                workOrders = workOrdersResult,
                pcs,
                users
            });
        });

        app.MapGet("/api/reports/activity-quality", async (HttpRequest request) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);

            var now = DateTime.Now;
            var fromDate = ReadDateQuery(request, "fromDate") ?? now.Date;
            var toDate = ReadDateQuery(request, "toDate") ?? now;
            if (toDate.Date == toDate)
            {
                toDate = toDate.Date.AddDays(1).AddTicks(-1);
            }

            var startHour = ReadIntQuery(request, "startHour");
            if (startHour is >= 0 and <= 23)
            {
                fromDate = fromDate.Date.AddHours(startHour.Value);
            }

            var pivotBy = NormalizeActivityQualityPivot(request.Query["pivotBy"].ToString());
            var pivotExpression = ActivityQualityPivotExpression(pivotBy);
            var siteValues = ReadQueryList(request, "siteIds");
            var stationValues = ReadQueryList(request, "stationIds");
            var productLineValues = ReadQueryList(request, "productLineIds");
            var partNumbers = ReadQueryList(request, "partNumbers");
            var workOrders = ReadQueryList(request, "workOrders");
            var pcValues = ReadQueryList(request, "pcIds");
            var userValues = ReadQueryList(request, "userIds");

            const string activityCte = """
                WITH activity AS (
                  SELECT
                    l.created_at AS date_time,
                    sn.site_id::text AS site_id,
                    COALESCE(s.name, '') AS site,
                    l.station_code AS station,
                    COALESCE(l.station_name, l.station_code) AS station_name,
                    COALESCE(pl.description, pl.code, '') AS product_line,
                    i.pn AS part_number,
                    wo.wo AS work_order,
                    COALESCE(l.pc_name, '') AS pc,
                    COALESCE(l.changed_by, '') AS user_name,
                    sn.sn AS serial_number,
                    UPPER(l.action_result) AS result,
                    COALESCE(NULLIF(l.remark, ''), NULLIF(l.additional_info, ''), '') AS symptom
                  FROM serial_station_logs l
                  JOIN serial_numbers sn ON sn.id = l.serial_id
                  JOIN work_orders wo ON wo.id = l.work_order_id
                  JOIN items i ON i.id = l.item_id
                  LEFT JOIN sites s ON s.id = sn.site_id
                  LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                  WHERE UPPER(l.action_result) IN ('PASS', 'FAIL')

                  UNION ALL

                  SELECT
                    l.created_at AS date_time,
                    w.site_id::text AS site_id,
                    COALESCE(w.site_name, '') AS site,
                    l.station_code AS station,
                    COALESCE(l.station_name, l.station_code) AS station_name,
                    COALESCE(p.item_type, '') AS product_line,
                    p.pn AS part_number,
                    w.wo AS work_order,
                    '' AS pc,
                    COALESCE(l.changed_by, '') AS user_name,
                    sn.sn AS serial_number,
                    UPPER(l.action_result) AS result,
                    COALESCE(l.remark, '') AS symptom
                  FROM workflow_serial_station_logs l
                  JOIN workflow_serial_numbers sn ON sn.id = l.workflow_serial_id
                  JOIN workflow_part_numbers p ON p.id = l.workflow_part_id
                  JOIN workflow_work_orders w ON w.id = l.workflow_work_order_id
                  WHERE UPPER(l.action_result) IN ('PASS', 'FAIL')
                ),
                filtered AS (
                  SELECT *
                  FROM activity
                  WHERE date_time >= @fromDate
                    AND date_time <= @toDate
                    AND (cardinality(@siteValues) = 0 OR site_id = ANY(@siteValues) OR site = ANY(@siteValues))
                    AND (cardinality(@stationValues) = 0 OR station = ANY(@stationValues) OR station_name = ANY(@stationValues))
                    AND (cardinality(@productLineValues) = 0 OR product_line = ANY(@productLineValues))
                    AND (cardinality(@partNumbers) = 0 OR part_number = ANY(@partNumbers))
                    AND (cardinality(@workOrders) = 0 OR work_order = ANY(@workOrders))
                    AND (cardinality(@pcValues) = 0 OR pc = ANY(@pcValues))
                    AND (cardinality(@userValues) = 0 OR user_name = ANY(@userValues))
                )
                """;

            var parameters = new (string Name, object? Value)[]
            {
                ("fromDate", fromDate),
                ("toDate", toDate),
                ("siteValues", siteValues),
                ("stationValues", stationValues),
                ("productLineValues", productLineValues),
                ("partNumbers", partNumbers),
                ("workOrders", workOrders),
                ("pcValues", pcValues),
                ("userValues", userValues)
            };

            var summaryRows = await QueryRowsAsync(
                connection,
                activityCte + """
                SELECT
                  COUNT(*)::int AS total_tested,
                  COUNT(*) FILTER (WHERE result = 'PASS')::int AS total_pass,
                  COUNT(*) FILTER (WHERE result = 'FAIL')::int AS total_fail,
                  COUNT(DISTINCT station) FILTER (WHERE station <> '')::int AS active_stations,
                  COUNT(DISTINCT work_order) FILTER (WHERE work_order <> '')::int AS active_work_orders,
                  COUNT(DISTINCT user_name) FILTER (WHERE user_name <> '')::int AS active_users
                FROM filtered
                """,
                parameters);

            var summary = summaryRows[0];
            var totalTested = Convert.ToInt32(summary["total_tested"] ?? 0);
            var totalPass = Convert.ToInt32(summary["total_pass"] ?? 0);
            var totalFail = Convert.ToInt32(summary["total_fail"] ?? 0);
            var passRate = totalTested == 0 ? 0 : Math.Round(totalPass * 100m / totalTested, 2);
            var failRate = totalTested == 0 ? 0 : Math.Round(totalFail * 100m / totalTested, 2);

            var chartRows = await QueryRowsAsync(
                connection,
                activityCte + $"""
                SELECT
                  {pivotExpression} AS pivot_key,
                  COUNT(*) FILTER (WHERE result = 'PASS')::int AS pass_count,
                  COUNT(*) FILTER (WHERE result = 'FAIL')::int AS fail_count,
                  COUNT(*)::int AS total_count
                FROM filtered
                GROUP BY pivot_key
                ORDER BY MIN(date_time), pivot_key
                LIMIT 120
                """,
                parameters);

            var detailRows = await QueryRowsAsync(
                connection,
                activityCte + """
                SELECT date_time, site, station_name AS station, product_line, part_number, work_order, pc, user_name,
                       serial_number, result, symptom AS failure_reason
                FROM filtered
                ORDER BY date_time DESC
                LIMIT 500
                """,
                parameters);

            var symptomRows = await QueryRowsAsync(
                connection,
                activityCte + """
                SELECT COALESCE(NULLIF(symptom, ''), 'Unspecified') AS symptom,
                       COUNT(*)::int AS count
                FROM filtered
                WHERE result = 'FAIL'
                GROUP BY COALESCE(NULLIF(symptom, ''), 'Unspecified')
                ORDER BY count DESC, symptom ASC
                LIMIT 10
                """,
                parameters);

            var stationRows = await QueryRowsAsync(
                connection,
                activityCte + """
                SELECT COALESCE(NULLIF(station_name, ''), station) AS station,
                       COUNT(*) FILTER (WHERE result = 'FAIL')::int AS fail_count,
                       COUNT(*)::int AS total_count
                FROM filtered
                GROUP BY COALESCE(NULLIF(station_name, ''), station)
                HAVING COUNT(*) > 0
                ORDER BY (COUNT(*) FILTER (WHERE result = 'FAIL'))::decimal / NULLIF(COUNT(*), 0) DESC, station ASC
                LIMIT 10
                """,
                parameters);

            return Results.Json(new
            {
                kpiSummary = new
                {
                    totalPass,
                    totalFail,
                    totalTested,
                    passRate,
                    failRate,
                    activeStations = summary["active_stations"],
                    activeWorkOrders = summary["active_work_orders"],
                    activeUsers = summary["active_users"]
                },
                chartData = chartRows.Select(row =>
                {
                    var total = Convert.ToInt32(row["total_count"] ?? 0);
                    var passed = Convert.ToInt32(row["pass_count"] ?? 0);
                    return new
                    {
                        label = FormatActivityQualityPivot(row["pivot_key"], pivotBy),
                        passCount = passed,
                        failCount = Convert.ToInt32(row["fail_count"] ?? 0),
                        passRate = total == 0 ? 0 : Math.Round(passed * 100m / total, 2)
                    };
                }),
                detailedRows = detailRows,
                symptomsPareto = BuildParetoRows(symptomRows),
                stationFailRates = stationRows.Select(row =>
                {
                    var total = Convert.ToInt32(row["total_count"] ?? 0);
                    var failed = Convert.ToInt32(row["fail_count"] ?? 0);
                    return new
                    {
                        station = row["station"],
                        failCount = failed,
                        totalCount = total,
                        failRate = total == 0 ? 0 : Math.Round(failed * 100m / total, 2)
                    };
                })
            });
        });
    }

    private static string NormalizeActivityQualityPivot(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized switch
        {
            "perHour" or "hour" => "perHour",
            "perDay" or "day" => "perDay",
            "perWeek" or "week" => "perWeek",
            "perMonth" or "month" => "perMonth",
            "perSite" or "site" => "perSite",
            "perPc" or "pc" => "perPc",
            "perStation" or "station" => "perStation",
            "perProductLine" or "productLine" or "pl" => "perProductLine",
            "perPartNumber" or "partNumber" or "pn" => "perPartNumber",
            "perWorkOrder" or "workOrder" or "wo" => "perWorkOrder",
            "perUser" or "user" => "perUser",
            _ => "perHour"
        };
    }

    private static string ActivityQualityPivotExpression(string pivotBy)
    {
        return pivotBy switch
        {
            "perHour" => "date_trunc('hour', date_time)",
            "perDay" => "date_trunc('day', date_time)",
            "perWeek" => "date_trunc('week', date_time)",
            "perMonth" => "date_trunc('month', date_time)",
            "perSite" => "COALESCE(NULLIF(site, ''), 'Unassigned site')",
            "perPc" => "COALESCE(NULLIF(pc, ''), 'Unassigned PC')",
            "perStation" => "COALESCE(NULLIF(station_name, ''), station, 'Unassigned station')",
            "perProductLine" => "COALESCE(NULLIF(product_line, ''), 'Unassigned PL')",
            "perPartNumber" => "COALESCE(NULLIF(part_number, ''), 'Unassigned PN')",
            "perWorkOrder" => "COALESCE(NULLIF(work_order, ''), 'Unassigned WO')",
            "perUser" => "COALESCE(NULLIF(user_name, ''), 'Unassigned user')",
            _ => "date_trunc('hour', date_time)"
        };
    }

    private static string FormatActivityQualityPivot(object? value, string pivotBy)
    {
        if (value is DateTime dateTime)
        {
            return pivotBy switch
            {
                "perHour" => dateTime.ToString("yyyy-MM-dd HH:00", CultureInfo.InvariantCulture),
                "perDay" => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "perWeek" => $"Week of {dateTime:yyyy-MM-dd}",
                "perMonth" => dateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                _ => dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            };
        }

        return Convert.ToString(value) ?? string.Empty;
    }

    private static DateTime? ReadDateQuery(HttpRequest request, string key)
    {
        var value = request.Query[key].ToString();
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static int? ReadIntQuery(HttpRequest request, string key)
    {
        var value = request.Query[key].ToString();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string[] ReadQueryList(HttpRequest request, string key)
    {
        return request.Query[key]
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<object> BuildParetoRows(List<Dictionary<string, object?>> rows)
    {
        var total = rows.Sum(row => Convert.ToInt32(row["count"] ?? 0));
        var cumulative = 0;
        foreach (var row in rows)
        {
            var count = Convert.ToInt32(row["count"] ?? 0);
            cumulative += count;
            yield return new
            {
                symptom = row["symptom"],
                count,
                percentage = total == 0 ? 0 : Math.Round(count * 100m / total, 2),
                cumulativePercentage = total == 0 ? 0 : Math.Round(cumulative * 100m / total, 2)
            };
        }
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
              interval_time_minutes INTEGER NOT NULL DEFAULT 5,
              sample_qty INTEGER NOT NULL DEFAULT 1,
              lot_size INTEGER NOT NULL DEFAULT 1000,
              is_sampling_enabled BOOLEAN NOT NULL DEFAULT FALSE,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_station_sampling UNIQUE (workflow_part_id, station_code)
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_sampling ADD COLUMN IF NOT EXISTS station_id INTEGER");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_sampling ADD COLUMN IF NOT EXISTS interval_time_minutes INTEGER NOT NULL DEFAULT 5");
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_sampling ADD COLUMN IF NOT EXISTS is_sampling_enabled BOOLEAN NOT NULL DEFAULT FALSE");
        await ExecuteAsync(
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
        await ExecuteAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS idx_workflow_sampling_events_station
            ON public.workflow_station_sampling_events (workflow_part_id, workflow_work_order_id, station_code, sampling_type, created_at DESC)
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS idx_workflow_sampling_events_serial_station
            ON public.workflow_station_sampling_events (workflow_serial_id, station_code, sampling_type)
            """);

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

    private static IResult JsonMessage(string message, int statusCode) => ApiResults.JsonMessage(message, statusCode);
}



