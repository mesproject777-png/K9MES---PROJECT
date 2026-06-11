using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

public static class WorkflowEndpoints
{
    public static void MapWorkflow(WebApplication app)
    {
        app.MapGet("/api/workflow/by-pn", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            var wo = request.Query["wo"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return JsonMessage("pn is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);
            var snapshot = await GetWorkflowSnapshotAsync(connection, pn, wo);
            return snapshot is null ? JsonMessage("Workflow not found", 404) : Results.Json(snapshot);
        });

        app.MapGet("/api/workflow/father-sn-types", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return JsonMessage("pn is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);
            var rows = await QueryRowsAsync(
                connection,
                """
                WITH family_parents AS (
                    SELECT p.id AS parent_id
                    FROM workflow_part_numbers p
                    WHERE UPPER(BTRIM(p.pn)) = UPPER(BTRIM(@pn))

                    UNION

                    SELECT child.workflow_part_id AS parent_id
                    FROM workflow_bom_children child
                    WHERE UPPER(BTRIM(child.son_pn)) = UPPER(BTRIM(@pn))
                ),
                family_members AS (
                    SELECT parent.pn, COALESCE(parent_sn.sn_type_name, parent.sn_type_name, '') AS sn_type_name
                    FROM family_parents fp
                    JOIN workflow_part_numbers parent ON parent.id = fp.parent_id
                    LEFT JOIN sn_types parent_sn ON parent_sn.id = parent.sn_type_id

                    UNION

                    SELECT child_part.pn, COALESCE(child_sn.sn_type_name, child_part.sn_type_name, '') AS sn_type_name
                    FROM family_parents fp
                    JOIN workflow_bom_children child ON child.workflow_part_id = fp.parent_id
                    JOIN workflow_part_numbers child_part ON UPPER(BTRIM(child_part.pn)) = UPPER(BTRIM(child.son_pn))
                    LEFT JOIN sn_types child_sn ON child_sn.id = child_part.sn_type_id
                )
                SELECT DISTINCT pn AS father_pn, sn_type_name
                FROM family_members
                WHERE UPPER(BTRIM(pn)) <> UPPER(BTRIM(@pn))
                  AND BTRIM(sn_type_name) <> ''
                ORDER BY pn ASC
                """,
                ("pn", pn));

            return Results.Json(new { data = rows });
        });

        app.MapGet("/api/workflow/work-orders", async (HttpRequest request) =>
        {
            var page = ParsePositiveInt(request.Query["page"], 1);
            var requestedLimit = request.Query["limit"].ToString();
            var limit = string.Equals(requestedLimit, "all", StringComparison.OrdinalIgnoreCase)
                ? 5000
                : Math.Min(ParsePositiveInt(requestedLimit, 15), 500);
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
                where.Add("p.pn ILIKE @pn");
                parameters.Add(("pn", $"%{pn}%"));
            }

            parameters.Add(("limit", limit));
            parameters.Add(("offset", offset));

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);
            var rows = await QueryRowsAsync(
                connection,
                $"""
                SELECT
                  COALESCE(w.wo, '') AS wo,
                  p.pn AS part_number,
                  COALESCE(st.sn_type_name, p.sn_type_name, '') AS sn_type,
                  COALESCE(w.plant, '') AS plant,
                  COALESCE(w.site_name, '') AS site,
                  w.due_date,
                  w.qty AS quantity,
                  COALESCE(w.status, '') AS status,
                  COALESCE(w.revision, '') AS revision,
                  COALESCE(w.lot, '') AS lot,
                  (
                    SELECT COUNT(*)::int
                    FROM workflow_routing_steps r
                    WHERE r.workflow_part_id = p.id
                  ) AS station_count,
                  (
                    SELECT COALESCE(SUM(b.qty), 0)::int
                    FROM workflow_bom_children b
                    WHERE b.workflow_part_id = p.id
                  ) AS bom_count,
                  COALESCE(activity.latest_at, p.updated_at) AS updated_at,
                  COUNT(*) OVER () AS total_count
                FROM workflow_part_numbers p
                LEFT JOIN workflow_work_orders w ON w.workflow_part_id = p.id
                LEFT JOIN sn_types st ON st.id = p.sn_type_id
                LEFT JOIN LATERAL (
                  SELECT MAX(activity_at) AS latest_at
                  FROM (VALUES
                    (p.updated_at),
                    (w.updated_at),
                    ((SELECT MAX(r.updated_at) FROM workflow_routing_steps r WHERE r.workflow_part_id = p.id)),
                    ((SELECT MAX(b.updated_at) FROM workflow_bom_children b WHERE b.workflow_part_id = p.id)),
                    ((SELECT MAX(sr.updated_at) FROM workflow_station_rules sr WHERE sr.workflow_part_id = p.id)),
                    ((SELECT MAX(lp.updated_at) FROM workflow_station_label_printing lp WHERE lp.workflow_part_id = p.id)),
                    ((SELECT MAX(ww.updated_at) FROM workflow_station_weighing ww WHERE ww.workflow_part_id = p.id)),
                    ((SELECT MAX(ss.updated_at) FROM workflow_station_sampling ss WHERE ss.workflow_part_id = p.id)),
                    ((SELECT MAX(rp.updated_at) FROM workflow_station_repair rp WHERE rp.workflow_part_id = p.id)),
                    ((SELECT MAX(ps.updated_at) FROM workflow_preview_station_statuses ps WHERE ps.workflow_part_id = p.id))
                  ) AS updates(activity_at)
                ) activity ON TRUE
                {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
                ORDER BY COALESCE(activity.latest_at, p.updated_at) DESC, COALESCE(w.id, 0) DESC, p.id DESC
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

        app.MapGet("/api/workflow/station-logins", async (HttpRequest request) => await GetWorkflowStationLoginsAsync(request));
        app.MapPut("/api/workflow/station-logins", async (HttpContext context) => await SaveWorkflowStationLoginsAsync(context));
        app.MapPost("/api/workflow/snapshot", async (HttpContext context) => await SaveWorkflowSnapshotAsync(context));
    }

    private static async Task<IResult> SaveWorkflowSnapshotAsync(HttpContext context)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        if (payload is null)
        {
            return JsonMessage("Request body is required", 400);
        }

        var partNode = payload["partNumber"];
        var workOrderNode = payload["workOrder"];
        var pn = ReadString(partNode, "pn")?.Trim() ?? ReadString(payload, "pn")?.Trim();

        if (string.IsNullOrWhiteSpace(pn))
        {
            return JsonMessage("Part number is required", 400);
        }

        var description = ReadString(partNode, "description")?.Trim() ?? string.Empty;
        var sgdControl = ReadBool(partNode, "sgd_control") ?? false;
        var itemType = ReadString(partNode, "item_type")?.Trim();
        var snTypeName = ReadString(partNode, "sn_type_name")?.Trim();
        var pnTypeId = ReadInt(partNode, "pn_type_id");
        var boxQty = ReadInt(partNode, "box_qty");
        var partAttributeKey = ReadString(partNode, "part_attribute_key")?.Trim();
        var partAttributeValue = ReadString(partNode, "part_attribute_value")?.Trim();
        partAttributeKey = string.IsNullOrWhiteSpace(partAttributeKey) ? null : partAttributeKey;
        partAttributeValue = string.IsNullOrWhiteSpace(partAttributeValue) ? null : partAttributeValue;

        if (boxQty is not null and <= 0)
        {
            return JsonMessage("Box Qty must be a positive number", 400);
        }

        await using var connection = await OpenConnectionAsync();
        await EnsureWorkflowSchemaAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            int? snTypeId = null;
            if (!string.IsNullOrWhiteSpace(snTypeName))
            {
                snTypeId = await ScalarAsync<int?>(
                    connection,
                    "SELECT id FROM sn_types WHERE sn_type_name = @name LIMIT 1",
                    ("name", snTypeName));

                if (snTypeId is not null)
                {
                    var snTypeOwnerRows = await QueryRowsAsync(
                        connection,
                        """
                        SELECT pn
                        FROM workflow_part_numbers
                        WHERE sn_type_id = @snTypeId
                          AND UPPER(BTRIM(pn)) <> UPPER(BTRIM(@pn))
                        ORDER BY updated_at DESC, id DESC
                        LIMIT 1
                        """,
                        ("snTypeId", snTypeId.Value),
                        ("pn", pn));

                    if (snTypeOwnerRows.Count > 0)
                    {
                        await transaction.RollbackAsync();
                        return JsonMessage($"Serial Pattern is already assigned to PN {snTypeOwnerRows[0]["pn"]}", 409);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(snTypeName))
            {
                var fatherSnTypeRows = await QueryRowsAsync(
                    connection,
                    """
                    WITH family_parents AS (
                        SELECT p.id AS parent_id
                        FROM workflow_part_numbers p
                        WHERE UPPER(BTRIM(p.pn)) = UPPER(BTRIM(@pn))

                        UNION

                        SELECT child.workflow_part_id AS parent_id
                        FROM workflow_bom_children child
                        WHERE UPPER(BTRIM(child.son_pn)) = UPPER(BTRIM(@pn))
                    ),
                    family_members AS (
                        SELECT parent.pn, COALESCE(parent_sn.sn_type_name, parent.sn_type_name, '') AS sn_type_name
                        FROM family_parents fp
                        JOIN workflow_part_numbers parent ON parent.id = fp.parent_id
                        LEFT JOIN sn_types parent_sn ON parent_sn.id = parent.sn_type_id

                        UNION

                        SELECT child_part.pn, COALESCE(child_sn.sn_type_name, child_part.sn_type_name, '') AS sn_type_name
                        FROM family_parents fp
                        JOIN workflow_bom_children child ON child.workflow_part_id = fp.parent_id
                        JOIN workflow_part_numbers child_part ON UPPER(BTRIM(child_part.pn)) = UPPER(BTRIM(child.son_pn))
                        LEFT JOIN sn_types child_sn ON child_sn.id = child_part.sn_type_id
                    )
                    SELECT pn AS father_pn, sn_type_name AS father_sn_type
                    FROM family_members
                    WHERE UPPER(BTRIM(pn)) <> UPPER(BTRIM(@pn))
                      AND UPPER(BTRIM(sn_type_name)) = UPPER(BTRIM(@snTypeName))
                    LIMIT 1
                    """,
                    ("pn", pn),
                    ("snTypeName", snTypeName));

                if (fatherSnTypeRows.Count > 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("this Sn type is already assigned in this father-child family", 409);
                }
            }

            var partRows = await QueryRowsAsync(
                connection,
                """
                INSERT INTO workflow_part_numbers
                  (pn, description, sgd_control, item_type, sn_type_id, sn_type_name, pn_type_id, box_qty, part_attribute_key, part_attribute_value)
                VALUES
                  (@pn, @description, @sgdControl, @itemType, @snTypeId, @snTypeName, @pnTypeId, @boxQty, @partAttributeKey, @partAttributeValue)
                ON CONFLICT (pn) DO UPDATE
                SET description = EXCLUDED.description,
                    sgd_control = EXCLUDED.sgd_control,
                    item_type = EXCLUDED.item_type,
                    sn_type_id = EXCLUDED.sn_type_id,
                    sn_type_name = EXCLUDED.sn_type_name,
                    pn_type_id = EXCLUDED.pn_type_id,
                    box_qty = EXCLUDED.box_qty,
                    part_attribute_key = EXCLUDED.part_attribute_key,
                    part_attribute_value = EXCLUDED.part_attribute_value,
                    updated_at = NOW()
                RETURNING *
                """,
                ("pn", pn),
                ("description", description),
                ("sgdControl", sgdControl),
                ("itemType", ToDbNullable(itemType)),
                ("snTypeId", ToDbNullable(snTypeId)),
                ("snTypeName", ToDbNullable(snTypeName)),
                ("pnTypeId", ToDbNullable(pnTypeId)),
                ("boxQty", ToDbNullable(boxQty)),
                ("partAttributeKey", ToDbNullable(partAttributeKey)),
                ("partAttributeValue", ToDbNullable(partAttributeValue)));

            var workflowPartId = Convert.ToInt32(partRows[0]["id"]);

            if (workOrderNode is not null)
            {
                var wo = ReadString(workOrderNode, "wo")?.Trim();
                if (!string.IsNullOrWhiteSpace(wo))
                {
                    var dueDate = ReadString(workOrderNode, "due_date")?.Trim();
                    var qty = ReadInt(workOrderNode, "qty");
                    var status = ReadString(workOrderNode, "status")?.Trim() ?? "Released";
                    var plant = ReadString(workOrderNode, "plant")?.Trim();
                    var siteId = ReadInt(workOrderNode, "site_id");
                    var siteName = ReadString(workOrderNode, "site_name")?.Trim();
                    var revision = ReadString(workOrderNode, "revision")?.Trim();
                    var lot = ReadString(workOrderNode, "lot")?.Trim();

                    await QueryRowsAsync(
                        connection,
                        """
                        INSERT INTO workflow_work_orders
                          (workflow_part_id, wo, plant, site_id, site_name, due_date, qty, status, revision, lot)
                        VALUES
                          (@workflowPartId, @wo, @plant, @siteId, @siteName, NULLIF(@dueDate, '')::date, @qty, @status, @revision, @lot)
                        ON CONFLICT (wo) DO UPDATE
                        SET workflow_part_id = EXCLUDED.workflow_part_id,
                            plant = EXCLUDED.plant,
                            site_id = EXCLUDED.site_id,
                            site_name = EXCLUDED.site_name,
                            due_date = EXCLUDED.due_date,
                            qty = EXCLUDED.qty,
                            status = EXCLUDED.status,
                            revision = EXCLUDED.revision,
                            lot = EXCLUDED.lot,
                            updated_at = NOW()
                        RETURNING *
                        """,
                        ("workflowPartId", workflowPartId),
                        ("wo", wo),
                        ("plant", ToDbNullable(plant)),
                        ("siteId", ToDbNullable(siteId)),
                        ("siteName", ToDbNullable(siteName)),
                        ("dueDate", dueDate ?? string.Empty),
                        ("qty", ToDbNullable(qty)),
                        ("status", status),
                        ("revision", ToDbNullable(revision)),
                        ("lot", ToDbNullable(lot)));
                }
            }

            if (payload["routing"] is JsonArray routingRows)
            {
                if (boxQty is > 0 && routingRows.Count > 0 && !routingRows.Any(row => IsPackStation(ReadString(row, "station_code"), ReadString(row, "station_name"))))
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Pack station is required for Box Qty", 400);
                }

                var loginStations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in routingRows)
                {
                    if (row is null) continue;

                    var stationCode = ReadString(row, "station_code")?.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode)) continue;

                    var stationLoginId = ReadString(row, "station_login_id")?.Trim();
                    var stationLoginPassword = ReadString(row, "station_login_password")?.Trim();
                    if (string.IsNullOrWhiteSpace(stationLoginId) && string.IsNullOrWhiteSpace(stationLoginPassword))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(stationLoginId) || string.IsNullOrWhiteSpace(stationLoginPassword))
                    {
                        await transaction.RollbackAsync();
                        return JsonMessage($"Both station login ID and password are required for station {stationCode}", 400);
                    }

                    if (loginStations.TryGetValue(stationLoginId, out var existingStationCode) &&
                        !string.Equals(existingStationCode, stationCode, StringComparison.OrdinalIgnoreCase))
                    {
                        await transaction.RollbackAsync();
                        return JsonMessage($"Station login ID is already used for station {existingStationCode}", 400);
                    }

                    loginStations[stationLoginId] = stationCode;
                }

                await EnsureWorkflowStationLoginsTableAsync(connection);
                foreach (var login in loginStations)
                {
                    var conflict = await FindWorkflowStationLoginConflictAsync(
                        connection,
                        login.Key,
                        excludeWorkflowPartId: workflowPartId);

                    if (conflict is not null)
                    {
                        await transaction.RollbackAsync();
                        return JsonMessage(FormatStationLoginConflictMessage(login.Key, conflict), 409);
                    }
                }

                await ExecuteAsync(connection, "DELETE FROM workflow_routing_steps WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                var routeOrder = 10;
                foreach (var row in routingRows)
                {
                    if (row is null) continue;

                    var stationCode = ReadString(row, "station_code")?.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode)) continue;

                    var stationOrder = ReadInt(row, "station_order") ?? routeOrder;
                    var stationName = ReadString(row, "station_name")?.Trim() ?? stationCode;
                    var sampleMode = ReadString(row, "sample_mode")?.Trim() ?? "Full";
                    var reportMode = ReadString(row, "report_mode")?.Trim() ?? "Regular";
                    var previewStatus = ReadString(row, "preview_status")?.Trim();
                    var stationLoginId = ReadString(row, "station_login_id")?.Trim();
                    var stationLoginPassword = ReadString(row, "station_login_password")?.Trim();
                    var stationIp = ReadString(row, "station_ip")?.Trim();
                    var printerIp = ReadString(row, "printer_ip")?.Trim();

                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO workflow_routing_steps
                          (workflow_part_id, station_order, station_code, station_name, sample_mode, report_mode, preview_status,
                           station_login_id, station_login_password, station_ip, printer_ip)
                        VALUES
                          (@workflowPartId, @stationOrder, @stationCode, @stationName, @sampleMode, @reportMode, @previewStatus,
                           @stationLoginId, @stationLoginPassword, @stationIp, @printerIp)
                        """,
                        ("workflowPartId", workflowPartId),
                        ("stationOrder", stationOrder),
                        ("stationCode", stationCode),
                        ("stationName", stationName),
                        ("sampleMode", sampleMode),
                        ("reportMode", reportMode),
                        ("previewStatus", ToDbNullable(previewStatus)),
                        ("stationLoginId", ToDbNullable(stationLoginId)),
                        ("stationLoginPassword", ToDbNullable(stationLoginPassword)),
                        ("stationIp", ToDbNullable(stationIp)),
                        ("printerIp", ToDbNullable(printerIp)));

                    routeOrder = stationOrder + 10;
                }
            }

            if (payload["bom"] is JsonArray bomRows)
            {
                var keptBomChildIds = new List<int>();
                foreach (var row in bomRows)
                {
                    if (row is null) continue;

                    var sonPn = ReadString(row, "son_pn")?.Trim();
                    if (string.IsNullOrWhiteSpace(sonPn)) continue;

                    var bomItemType = ReadString(row, "item_type")?.Trim();
                    if (!string.Equals(bomItemType, "Manufactured", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(bomItemType, "Purchased", StringComparison.OrdinalIgnoreCase))
                    {
                        await transaction.RollbackAsync();
                        return JsonMessage("BOM item type must be Manufactured or Purchased", 400);
                    }

                    var bomChildId = ReadInt(row, "id");
                    var sonDescription = ReadString(row, "son_description")?.Trim() ?? sonPn;
                    var stationCode = ReadString(row, "station_code")?.Trim();
                    var stationName = ReadString(row, "station_name")?.Trim();
                    var qty = ReadInt(row, "qty") ?? 1;

                    List<Dictionary<string, object?>> rows = [];
                    if (bomChildId is > 0)
                    {
                        rows = await QueryRowsAsync(
                            connection,
                            """
                            UPDATE workflow_bom_children
                            SET son_pn = @sonPn,
                                son_description = @sonDescription,
                                station_code = @stationCode,
                                station_name = @stationName,
                                item_type = @itemType,
                                qty = @qty,
                                updated_at = NOW()
                            WHERE id = @bomChildId
                              AND workflow_part_id = @workflowPartId
                            RETURNING id
                            """,
                            ("bomChildId", bomChildId),
                            ("workflowPartId", workflowPartId),
                            ("sonPn", sonPn),
                            ("sonDescription", sonDescription),
                            ("stationCode", ToDbNullable(stationCode)),
                            ("stationName", ToDbNullable(stationName)),
                            ("itemType", bomItemType),
                            ("qty", qty));
                    }

                    if (rows.Count == 0)
                    {
                        rows = await QueryRowsAsync(
                            connection,
                            """
                            INSERT INTO workflow_bom_children
                              (workflow_part_id, son_pn, son_description, station_code, station_name, item_type, qty)
                            VALUES
                              (@workflowPartId, @sonPn, @sonDescription, @stationCode, @stationName, @itemType, @qty)
                            RETURNING id
                            """,
                            ("workflowPartId", workflowPartId),
                            ("sonPn", sonPn),
                            ("sonDescription", sonDescription),
                            ("stationCode", ToDbNullable(stationCode)),
                            ("stationName", ToDbNullable(stationName)),
                            ("itemType", bomItemType),
                            ("qty", qty));
                    }

                    if (rows.Count > 0 && rows[0]["id"] is not null)
                    {
                        keptBomChildIds.Add(Convert.ToInt32(rows[0]["id"]));
                    }
                }

                var removedBoundBomRows = keptBomChildIds.Count > 0
                    ? await QueryRowsAsync(
                        connection,
                        """
                        SELECT id, son_pn
                        FROM workflow_bom_children child
                        WHERE child.workflow_part_id = @workflowPartId
                          AND child.id <> ALL(@keptBomChildIds)
                          AND EXISTS (
                            SELECT 1
                            FROM workflow_serial_bom_bindings binding
                            WHERE binding.workflow_bom_child_id = child.id
                          )
                        LIMIT 1
                        """,
                        ("workflowPartId", workflowPartId),
                        ("keptBomChildIds", keptBomChildIds.ToArray()))
                    : await QueryRowsAsync(
                        connection,
                        """
                        SELECT id, son_pn
                        FROM workflow_bom_children child
                        WHERE child.workflow_part_id = @workflowPartId
                          AND EXISTS (
                            SELECT 1
                            FROM workflow_serial_bom_bindings binding
                            WHERE binding.workflow_bom_child_id = child.id
                          )
                        LIMIT 1
                        """,
                        ("workflowPartId", workflowPartId));

                if (removedBoundBomRows.Count > 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage($"BOM child {removedBoundBomRows[0]["son_pn"]} is already used in serial binding and cannot be deleted", 409);
                }

                if (keptBomChildIds.Count > 0)
                {
                    await ExecuteAsync(
                        connection,
                        "DELETE FROM workflow_bom_children WHERE workflow_part_id = @workflowPartId AND id <> ALL(@keptBomChildIds)",
                        ("workflowPartId", workflowPartId),
                        ("keptBomChildIds", keptBomChildIds.ToArray()));
                }
                else
                {
                    await ExecuteAsync(connection, "DELETE FROM workflow_bom_children WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                }
            }

            if (payload["stationRules"] is JsonObject stationRules)
            {
                await ExecuteAsync(connection, "DELETE FROM workflow_station_rules WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                foreach (var ruleGroup in stationRules)
                {
                    var stationCode = ruleGroup.Key.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode)) continue;

                    var rules = new List<string>();
                    if (ruleGroup.Value is JsonArray ruleArray)
                    {
                        rules.AddRange(ruleArray.Select(rule => rule?.GetValue<string>()?.Trim() ?? string.Empty).Where(rule => !string.IsNullOrWhiteSpace(rule)));
                    }
                    else
                    {
                        var ruleText = ruleGroup.Value?.GetValue<string>()?.Trim();
                        if (!string.IsNullOrWhiteSpace(ruleText))
                        {
                            rules.Add(ruleText);
                        }
                    }

                    for (var index = 0; index < rules.Count; index++)
                    {
                        await ExecuteAsync(
                            connection,
                            """
                            INSERT INTO workflow_station_rules
                              (workflow_part_id, station_code, rule_order, rule_text)
                            VALUES
                              (@workflowPartId, @stationCode, @ruleOrder, @ruleText)
                            """,
                            ("workflowPartId", workflowPartId),
                            ("stationCode", stationCode),
                            ("ruleOrder", (index + 1) * 10),
                            ("ruleText", rules[index]));
                    }
                }
            }

            if (payload["stationLabelPrinting"] is JsonObject stationLabelPrinting)
            {
                await ExecuteAsync(connection, "DELETE FROM workflow_station_label_printing WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                foreach (var configGroup in stationLabelPrinting)
                {
                    var stationCode = configGroup.Key.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode) || configGroup.Value is null) continue;

                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO workflow_station_label_printing
                          (workflow_part_id, station_code, station_id, station_name, label_code, label_description,
                           printer_id, printer_name, ip_address, port, status, is_label_printing_enabled)
                        VALUES
                          (@workflowPartId, @stationCode, @stationId, @stationName, @labelCode, @labelDescription,
                           @printerId, @printerName, @ipAddress, @port, @status, @isLabelPrintingEnabled)
                        """,
                        ("workflowPartId", workflowPartId),
                        ("stationCode", stationCode),
                        ("stationId", ReadInt(configGroup.Value, "stationId")),
                        ("stationName", ToDbNullable(ReadString(configGroup.Value, "stationName")?.Trim())),
                        ("labelCode", ToDbNullable(ReadString(configGroup.Value, "labelCode")?.Trim())),
                        ("labelDescription", ToDbNullable(ReadString(configGroup.Value, "labelDescription")?.Trim())),
                        ("printerId", ToDbNullable(ReadString(configGroup.Value, "printerId")?.Trim())),
                        ("printerName", ToDbNullable(ReadString(configGroup.Value, "printerName")?.Trim())),
                        ("ipAddress", ToDbNullable(ReadString(configGroup.Value, "ipAddress")?.Trim())),
                        ("port", ToDbNullable(ReadString(configGroup.Value, "port")?.Trim())),
                        ("status", ToDbNullable(ReadString(configGroup.Value, "status")?.Trim())),
                        ("isLabelPrintingEnabled", ReadBool(configGroup.Value, "isLabelPrintingEnabled") ?? false));
                }
            }

            if (payload["stationWeighing"] is JsonObject stationWeighing)
            {
                await ExecuteAsync(connection, "DELETE FROM workflow_station_weighing WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                foreach (var configGroup in stationWeighing)
                {
                    var stationCode = configGroup.Key.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode) || configGroup.Value is null) continue;

                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO workflow_station_weighing
                          (workflow_part_id, station_code, station_id, station_name, minimum_weight,
                           maximum_weight, tolerance, is_weighing_enabled)
                        VALUES
                          (@workflowPartId, @stationCode, @stationId, @stationName, NULLIF(CAST(@minimumWeight AS text), '')::numeric,
                           NULLIF(CAST(@maximumWeight AS text), '')::numeric, NULLIF(CAST(@tolerance AS text), '')::numeric, @isWeighingEnabled)
                        """,
                        ("workflowPartId", workflowPartId),
                        ("stationCode", stationCode),
                        ("stationId", ReadInt(configGroup.Value, "stationId")),
                        ("stationName", ToDbNullable(ReadString(configGroup.Value, "stationName")?.Trim())),
                        ("minimumWeight", ToDbNullable(ReadString(configGroup.Value, "minimumWeight")?.Trim())),
                        ("maximumWeight", ToDbNullable(ReadString(configGroup.Value, "maximumWeight")?.Trim())),
                        ("tolerance", ToDbNullable(ReadString(configGroup.Value, "tolerance")?.Trim())),
                        ("isWeighingEnabled", ReadBool(configGroup.Value, "isWeighingEnabled") ?? false));
                }
            }

            if (payload["stationSampling"] is JsonObject stationSampling)
            {
                await ExecuteAsync(connection, "DELETE FROM workflow_station_sampling WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                foreach (var configGroup in stationSampling)
                {
                    var stationCode = configGroup.Key.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode) || configGroup.Value is null) continue;

                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO workflow_station_sampling
                          (workflow_part_id, station_code, station_id, station_name, sampling_type,
                           interval_qty, interval_time_minutes, sample_qty, lot_size, is_sampling_enabled)
                        VALUES
                          (@workflowPartId, @stationCode, @stationId, @stationName, @samplingType,
                           @intervalQty, @intervalTimeMinutes, @sampleQty, @lotSize, @isSamplingEnabled)
                        """,
                        ("workflowPartId", workflowPartId),
                        ("stationCode", stationCode),
                        ("stationId", ReadInt(configGroup.Value, "stationId")),
                        ("stationName", ToDbNullable(ReadString(configGroup.Value, "stationName")?.Trim())),
                        ("samplingType", ReadString(configGroup.Value, "samplingType")?.Trim() ?? "PERIODIC"),
                        ("intervalQty", ReadInt(configGroup.Value, "intervalQty") ?? 10),
                        ("intervalTimeMinutes", ReadInt(configGroup.Value, "intervalTimeMinutes") ?? 5),
                        ("sampleQty", ReadInt(configGroup.Value, "sampleQty") ?? 1),
                        ("lotSize", ReadInt(configGroup.Value, "lotSize") ?? 1000),
                        ("isSamplingEnabled", ReadBool(configGroup.Value, "isSamplingEnabled") ?? false));
                }
            }

            if (payload["stationRepair"] is JsonObject stationRepair)
            {
                await ExecuteAsync(connection, "DELETE FROM workflow_station_repair WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                foreach (var configGroup in stationRepair)
                {
                    var stationCode = configGroup.Key.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode) || configGroup.Value is null) continue;

                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO workflow_station_repair
                          (workflow_part_id, station_code, station_id, station_name, repair_station_name, is_repair_station_enabled)
                        VALUES
                          (@workflowPartId, @stationCode, @stationId, @stationName, @repairStationName, @isRepairStationEnabled)
                        """,
                        ("workflowPartId", workflowPartId),
                        ("stationCode", stationCode),
                        ("stationId", ReadInt(configGroup.Value, "stationId")),
                        ("stationName", ToDbNullable(ReadString(configGroup.Value, "stationName")?.Trim())),
                        ("repairStationName", ToDbNullable(ReadString(configGroup.Value, "repairStationName")?.Trim())),
                        ("isRepairStationEnabled", ReadBool(configGroup.Value, "isRepairStationEnabled") ?? false));
                }
            }

            if (payload["previewStatuses"] is JsonObject previewStatuses)
            {
                await ExecuteAsync(connection, "DELETE FROM workflow_preview_station_statuses WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
                foreach (var status in previewStatuses)
                {
                    var stationCode = status.Key.Trim();
                    var statusValue = status.Value?.GetValue<string>()?.Trim();
                    if (string.IsNullOrWhiteSpace(stationCode) || string.IsNullOrWhiteSpace(statusValue)) continue;

                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO workflow_preview_station_statuses
                          (workflow_part_id, station_code, status)
                        VALUES
                          (@workflowPartId, @stationCode, @status)
                        """,
                        ("workflowPartId", workflowPartId),
                        ("stationCode", stationCode),
                        ("status", statusValue));
                }
            }

            await transaction.CommitAsync();
            var savedWo = ReadString(workOrderNode, "wo")?.Trim();
            var snapshot = await GetWorkflowSnapshotAsync(connection, pn, savedWo, includeLatestWorkOrderWhenWoMissing: false);
            return Results.Json(snapshot ?? new { message = "Workflow saved" });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    private static async Task<object?> GetWorkflowSnapshotAsync(NpgsqlConnection connection, string pn, string? wo = null, bool includeLatestWorkOrderWhenWoMissing = true)
    {
        await EnsureWorkflowSchemaAsync(connection);
        await EnsureRoutingStepLoginColumnsAsync(connection);

        var partRows = await QueryRowsAsync(
            connection,
            """
            SELECT
              p.id,
              p.pn,
              p.description,
              p.sgd_control,
              p.item_type,
              p.box_qty,
              p.part_attribute_key,
              p.part_attribute_value,
              COALESCE(st.sn_type_name, p.sn_type_name, '') AS sn_type_name,
              p.pn_type_id,
              p.created_at,
              p.updated_at
            FROM workflow_part_numbers p
            LEFT JOIN sn_types st ON st.id = p.sn_type_id
            WHERE p.pn = @pn
            LIMIT 1
            """,
            ("pn", pn));

        if (partRows.Count > 0)
        {
            var part = partRows[0];
            var workflowPartId = Convert.ToInt32(part["id"]);
            var shouldLoadWorkOrder = includeLatestWorkOrderWhenWoMissing || !string.IsNullOrWhiteSpace(wo);
            var workOrderRows = shouldLoadWorkOrder
                ? await QueryRowsAsync(
                    connection,
                    """
                    SELECT
                      wo,
                      plant,
                      site_id,
                      site_name,
                      due_date,
                      qty,
                      status,
                      @pn AS pn,
                      revision,
                      lot,
                      created_at,
                      updated_at
                    FROM workflow_work_orders
                    WHERE workflow_part_id = @workflowPartId
                      AND (@wo = '' OR UPPER(wo) = UPPER(@wo))
                    ORDER BY updated_at DESC, id DESC
                    LIMIT 1
                    """,
                    ("workflowPartId", workflowPartId),
                    ("pn", pn),
                    ("wo", string.IsNullOrWhiteSpace(wo) ? string.Empty : wo.Trim()))
                : new List<Dictionary<string, object?>>();

            var routingRows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  r.id,
                  r.station_order,
                  r.station_code,
                  r.station_name,
                  r.sample_mode,
                  r.report_mode,
                  r.station_login_id,
                  r.station_login_password,
                  r.station_ip,
                  r.printer_ip,
                  COALESCE(ps.status, r.preview_status) AS preview_status
                FROM workflow_routing_steps r
                LEFT JOIN workflow_preview_station_statuses ps
                  ON ps.workflow_part_id = r.workflow_part_id
                 AND ps.station_code = r.station_code
                WHERE r.workflow_part_id = @workflowPartId
                ORDER BY r.station_order ASC, r.id ASC
                """,
                ("workflowPartId", workflowPartId));

            var bomRows = await QueryRowsAsync(
                connection,
                """
                SELECT id, son_pn, son_description, station_code, station_name, item_type, qty
                FROM workflow_bom_children
                WHERE workflow_part_id = @workflowPartId
                ORDER BY id ASC
                """,
                ("workflowPartId", workflowPartId));

            var ruleRows = await QueryRowsAsync(
                connection,
                """
                SELECT station_code, rule_text
                FROM workflow_station_rules
                WHERE workflow_part_id = @workflowPartId
                ORDER BY station_code ASC, rule_order ASC, id ASC
                """,
                ("workflowPartId", workflowPartId));

            var labelPrintingRows = await QueryRowsAsync(
                connection,
                """
                SELECT station_code, station_id, station_name, label_code, label_description,
                       printer_id, printer_name, ip_address, port, status, is_label_printing_enabled
                FROM workflow_station_label_printing
                WHERE workflow_part_id = @workflowPartId
                ORDER BY station_code ASC
                """,
                ("workflowPartId", workflowPartId));

            var weighingRows = await QueryRowsAsync(
                connection,
                """
                SELECT station_code, station_id, station_name, minimum_weight,
                       maximum_weight, tolerance, is_weighing_enabled
                FROM workflow_station_weighing
                WHERE workflow_part_id = @workflowPartId
                ORDER BY station_code ASC
                """,
                ("workflowPartId", workflowPartId));

            var samplingRows = await QueryRowsAsync(
                connection,
                """
                SELECT station_code, station_id, station_name, sampling_type,
                       interval_qty, interval_time_minutes, sample_qty, lot_size, is_sampling_enabled
                FROM workflow_station_sampling
                WHERE workflow_part_id = @workflowPartId
                ORDER BY station_code ASC
                """,
                ("workflowPartId", workflowPartId));

            var repairRows = await QueryRowsAsync(
                connection,
                """
                SELECT station_code, station_id, station_name, repair_station_name, is_repair_station_enabled
                FROM workflow_station_repair
                WHERE workflow_part_id = @workflowPartId
                ORDER BY station_code ASC
                """,
                ("workflowPartId", workflowPartId));

            var statusRows = await QueryRowsAsync(
                connection,
                """
                SELECT station_code, status
                FROM workflow_preview_station_statuses
                WHERE workflow_part_id = @workflowPartId
                """,
                ("workflowPartId", workflowPartId));

            return new
            {
                partNumber = new
                {
                    pn = part["pn"],
                    description = part["description"],
                    sgd_control = part["sgd_control"],
                    item_type = part["item_type"],
                    sn_type_name = part["sn_type_name"],
                    pn_type_id = part["pn_type_id"],
                    box_qty = part["box_qty"],
                    part_attribute_key = part["part_attribute_key"],
                    part_attribute_value = part["part_attribute_value"]
                },
                workOrder = workOrderRows.Count > 0 ? workOrderRows[0] : null,
                routing = routingRows,
                bom = bomRows,
                stationRules = GroupWorkflowRules(ruleRows),
                stationLabelPrinting = labelPrintingRows.ToDictionary(
                    row => Convert.ToString(row["station_code"]) ?? string.Empty,
                    row => new
                    {
                        stationId = row["station_id"] is DBNull ? (int?)null : Convert.ToInt32(row["station_id"]),
                        stationName = Convert.ToString(row["station_name"]) ?? string.Empty,
                        labelCode = Convert.ToString(row["label_code"]) ?? string.Empty,
                        labelDescription = Convert.ToString(row["label_description"]) ?? string.Empty,
                        printerId = Convert.ToString(row["printer_id"]) ?? string.Empty,
                        printerName = Convert.ToString(row["printer_name"]) ?? string.Empty,
                        ipAddress = Convert.ToString(row["ip_address"]) ?? string.Empty,
                        port = Convert.ToString(row["port"]) ?? string.Empty,
                        status = Convert.ToString(row["status"]) ?? string.Empty,
                        isLabelPrintingEnabled = row["is_label_printing_enabled"] is bool enabled && enabled
                    }),
                stationWeighing = weighingRows.ToDictionary(
                    row => Convert.ToString(row["station_code"]) ?? string.Empty,
                    row => new
                    {
                        stationId = row["station_id"] is DBNull ? (int?)null : Convert.ToInt32(row["station_id"]),
                        stationName = Convert.ToString(row["station_name"]) ?? string.Empty,
                        minimumWeight = Convert.ToString(row["minimum_weight"]) ?? string.Empty,
                        maximumWeight = Convert.ToString(row["maximum_weight"]) ?? string.Empty,
                        tolerance = Convert.ToString(row["tolerance"]) ?? string.Empty,
                        isWeighingEnabled = row["is_weighing_enabled"] is bool enabled && enabled
                    }),
                stationSampling = samplingRows.ToDictionary(
                    row => Convert.ToString(row["station_code"]) ?? string.Empty,
                    row => new
                    {
                        stationId = row["station_id"] is DBNull ? (int?)null : Convert.ToInt32(row["station_id"]),
                        stationName = Convert.ToString(row["station_name"]) ?? string.Empty,
                        samplingType = Convert.ToString(row["sampling_type"]) ?? "PERIODIC",
                        intervalQty = Convert.ToString(row["interval_qty"]) ?? "10",
                        intervalTimeMinutes = Convert.ToString(row["interval_time_minutes"]) ?? "5",
                        sampleQty = Convert.ToString(row["sample_qty"]) ?? "1",
                        lotSize = Convert.ToString(row["lot_size"]) ?? "1000",
                        isSamplingEnabled = row["is_sampling_enabled"] is bool enabled && enabled
                    }),
                stationRepair = repairRows.ToDictionary(
                    row => Convert.ToString(row["station_code"]) ?? string.Empty,
                    row => new
                    {
                        stationId = row["station_id"] is DBNull ? (int?)null : Convert.ToInt32(row["station_id"]),
                        stationName = Convert.ToString(row["station_name"]) ?? string.Empty,
                        repairStationName = Convert.ToString(row["repair_station_name"]) ?? string.Empty,
                        isRepairStationEnabled = row["is_repair_station_enabled"] is bool enabled && enabled
                    }),
                previewStatuses = statusRows.ToDictionary(
                    row => Convert.ToString(row["station_code"]) ?? string.Empty,
                    row => Convert.ToString(row["status"]) ?? string.Empty)
            };
        }

        var itemRows = await QueryRowsAsync(
            connection,
            """
            SELECT
              i.id,
              i.pn,
              i.description,
              i.sgd_control,
              i.item_type,
              COALESCE(st.sn_type_name, '') AS sn_type_name,
              i.pn_type_id
            FROM items i
            LEFT JOIN sn_types st ON st.id = i.sn_type_id
            WHERE i.pn = @pn
            LIMIT 1
            """,
            ("pn", pn));

        if (itemRows.Count == 0)
        {
            return null;
        }

        var item = itemRows[0];
        var itemId = Convert.ToInt32(item["id"]);
        var shouldLoadLegacyWorkOrder = includeLatestWorkOrderWhenWoMissing || !string.IsNullOrWhiteSpace(wo);
        var existingWorkOrderRows = shouldLoadLegacyWorkOrder
            ? await QueryRowsAsync(
                connection,
                """
                SELECT
                  w.wo,
                  '' AS plant,
                  w.site_id,
                  s.name AS site_name,
                  w.due_date,
                  w.qty,
                  w.status,
                  @pn AS pn,
                  ir.revision,
                  w.lot,
                  w.created_at,
                  w.updated_at
                FROM work_orders w
                JOIN sites s ON s.id = w.site_id
                JOIN item_revisions ir ON ir.id = w.item_revision_id
                WHERE w.item_id = @itemId
                  AND (@wo = '' OR UPPER(w.wo) = UPPER(@wo))
                ORDER BY w.updated_at DESC, w.id DESC
                LIMIT 1
                """,
                ("itemId", itemId),
                ("pn", pn),
                ("wo", string.IsNullOrWhiteSpace(wo) ? string.Empty : wo.Trim()))
            : new List<Dictionary<string, object?>>();

        var existingRoutingRows = await QueryRowsAsync(
            connection,
            """
            SELECT id, station_order, station_code, station_name, sample_mode, report_mode,
                   station_login_id, station_login_password, station_ip, printer_ip,
                   NULL::varchar AS preview_status
            FROM item_routing_steps
            WHERE item_id = @itemId
            ORDER BY station_order ASC, id ASC
            """,
            ("itemId", itemId));

        var existingBomRows = await QueryRowsAsync(
            connection,
            """
            SELECT
              bl.id,
              son.pn AS son_pn,
              son.description AS son_description,
              '' AS station_code,
              '' AS station_name,
              son.item_type AS item_type,
              COALESCE(pt.code, '') AS pn_type,
              bl.qty
            FROM item_bom_lines bl
            JOIN items son ON son.id = bl.son_item_id
            LEFT JOIN pn_types pt ON pt.id = son.pn_type_id
            WHERE bl.main_item_id = @itemId
            ORDER BY bl.id ASC
            """,
            ("itemId", itemId));

        return new
        {
            partNumber = new
            {
                pn = item["pn"],
                description = item["description"],
                sgd_control = item["sgd_control"],
                item_type = item["item_type"],
                sn_type_name = item["sn_type_name"],
                pn_type_id = item["pn_type_id"]
            },
            workOrder = existingWorkOrderRows.Count > 0 ? existingWorkOrderRows[0] : null,
            routing = existingRoutingRows,
            bom = existingBomRows,
            stationRules = new Dictionary<string, List<string>>(),
            stationLabelPrinting = new Dictionary<string, object>(),
            stationWeighing = new Dictionary<string, object>(),
            stationSampling = new Dictionary<string, object>(),
            stationRepair = new Dictionary<string, object>(),
            previewStatuses = new Dictionary<string, string>()
        };
    }
    private static async Task<IResult> GetWorkflowStationLoginsAsync(HttpRequest request)
    {
        var pn = request.Query["pn"].ToString().Trim();
        var wo = request.Query["wo"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(pn))
        {
            return JsonMessage("pn is required", 400);
        }

        if (string.IsNullOrWhiteSpace(wo))
        {
            return JsonMessage("wo is required", 400);
        }

        await using var connection = await OpenConnectionAsync();
        await EnsureWorkflowSchemaAsync(connection);
        await EnsureWorkflowStationLoginsTableAsync(connection);

        var partRows = await QueryRowsAsync(
            connection,
            """
            SELECT p.id, p.pn, p.description, w.id AS workflow_work_order_id, w.wo
            FROM workflow_part_numbers p
            JOIN workflow_work_orders w ON w.workflow_part_id = p.id
            WHERE UPPER(p.pn) = UPPER(@pn)
              AND UPPER(w.wo) = UPPER(@wo)
            LIMIT 1
            """,
            ("pn", pn),
            ("wo", wo));

        if (partRows.Count == 0)
        {
            return JsonMessage("Workflow not found", 404);
        }

        var workflowPartId = Convert.ToInt32(partRows[0]["id"]);
        var workflowWorkOrderId = Convert.ToInt32(partRows[0]["workflow_work_order_id"]);
        var stations = await QueryRowsAsync(
            connection,
            """
            SELECT r.id AS station_id,
                   r.station_order,
                   r.station_code,
                   r.station_name,
                   l.id AS login_row_id,
                   COALESCE(l.station_login_id, '') AS station_login_id,
                   COALESCE(l.station_login_password, '') AS station_login_password
            FROM workflow_routing_steps r
            LEFT JOIN workflow_station_logins l
              ON l.workflow_routing_step_id = r.id
             AND l.workflow_work_order_id = @workflowWorkOrderId
            WHERE r.workflow_part_id = @workflowPartId
            ORDER BY r.station_order ASC, r.id ASC, l.id ASC
            """,
            ("workflowPartId", workflowPartId),
            ("workflowWorkOrderId", workflowWorkOrderId));

        return Results.Json(new
        {
            partNumber = partRows[0],
            stations
        });
    }

    private static async Task<IResult> SaveWorkflowStationLoginsAsync(HttpContext context)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        var pn = ReadString(payload, "pn")?.Trim();
        var wo = ReadString(payload, "wo")?.Trim();
        var stationsNode = payload?["stations"]?.AsArray();

        if (string.IsNullOrWhiteSpace(pn))
        {
            return JsonMessage("pn is required", 400);
        }

        if (string.IsNullOrWhiteSpace(wo))
        {
            return JsonMessage("wo is required", 400);
        }

        if (stationsNode is null)
        {
            return JsonMessage("stations is required", 400);
        }

        var stationLogins = new List<(int? LoginRowId, int StationId, string StationCode, string LoginId, string Password)>();
        var loginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stationNode in stationsNode)
        {
            var stationId = ReadInt(stationNode, "station_id");
            var loginRowId = ReadInt(stationNode, "id");
            var stationCode = ReadString(stationNode, "station_code")?.Trim() ?? string.Empty;
            var loginId = ReadString(stationNode, "station_login_id")?.Trim() ?? string.Empty;
            var password = ReadString(stationNode, "station_login_password")?.Trim() ?? string.Empty;

            if (stationId is null or <= 0)
            {
                return JsonMessage("Invalid station row", 400);
            }

            if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(password))
            {
                return JsonMessage($"Login ID and password are required for station {stationCode}", 400);
            }

            if (!loginIds.Add(loginId))
            {
                return JsonMessage($"Login ID {loginId} is already used by another station", 400);
            }

            stationLogins.Add((loginRowId, stationId.Value, stationCode, loginId, password));
        }

        await using var connection = await OpenConnectionAsync();
        await EnsureWorkflowSchemaAsync(connection);
        await EnsureWorkflowStationLoginsTableAsync(connection);

        var workOrderRows = await QueryRowsAsync(
            connection,
            """
            SELECT w.id AS workflow_work_order_id, p.id AS workflow_part_id
            FROM workflow_work_orders w
            JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
            WHERE UPPER(p.pn) = UPPER(@pn)
              AND UPPER(w.wo) = UPPER(@wo)
            LIMIT 1
            """,
            ("pn", pn),
            ("wo", wo));
        if (workOrderRows.Count == 0)
        {
            return JsonMessage("Work order not found", 404);
        }

        var workflowWorkOrderId = Convert.ToInt32(workOrderRows[0]["workflow_work_order_id"]);
        var workflowPartId = Convert.ToInt32(workOrderRows[0]["workflow_part_id"]);

        foreach (var station in stationLogins)
        {
            var conflict = await FindWorkflowStationLoginConflictAsync(
                connection,
                station.LoginId,
                excludeLoginRowId: station.LoginRowId);

            if (conflict is not null)
            {
                return JsonMessage(FormatStationLoginConflictMessage(station.LoginId, conflict), 409);
            }
        }

        var stationIds = stationLogins.Select(station => station.StationId).Distinct().ToList();
        if (stationIds.Count > 0)
        {
            var existingStationRows = await QueryRowsAsync(
                connection,
                """
                SELECT id, station_code
                FROM workflow_routing_steps
                WHERE workflow_part_id = @workflowPartId
                  AND id = ANY(@stationIds)
                """,
                ("workflowPartId", workflowPartId),
                ("stationIds", stationIds.ToArray()));

            if (existingStationRows.Count != stationIds.Count)
            {
                return JsonMessage("One or more stations were not found for this workflow", 404);
            }
        }

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var station in stationLogins)
            {
                if (station.LoginRowId is > 0)
                {
                    var updatedLogin = await ExecuteAsync(
                        connection,
                        """
                        UPDATE workflow_station_logins
                        SET station_login_id = @loginId,
                            station_login_password = @password,
                            updated_at = NOW()
                        WHERE id = @loginRowId
                          AND workflow_routing_step_id = @stationId
                          AND workflow_work_order_id = @workflowWorkOrderId
                        """,
                        ("loginId", station.LoginId),
                        ("password", station.Password),
                        ("loginRowId", station.LoginRowId.Value),
                        ("stationId", station.StationId),
                        ("workflowWorkOrderId", workflowWorkOrderId));

                    if (updatedLogin > 0)
                    {
                        continue;
                    }
                }

                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO workflow_station_logins
                      (workflow_work_order_id, workflow_routing_step_id, station_login_id, station_login_password)
                    VALUES
                      (@workflowWorkOrderId, @stationId, @loginId, @password)
                    """,
                    ("workflowWorkOrderId", workflowWorkOrderId),
                    ("stationId", station.StationId),
                    ("loginId", station.LoginId),
                    ("password", station.Password));
            }

            await transaction.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync();
            return JsonMessage("Login ID is already used by another station", 409);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return Results.Json(new { message = "Station logins saved successfully" });
    }
    private static Dictionary<string, List<string>> GroupWorkflowRules(List<Dictionary<string, object?>> rows)
    {
        var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var stationCode = Convert.ToString(row["station_code"]) ?? string.Empty;
            var ruleText = Convert.ToString(row["rule_text"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(stationCode) || string.IsNullOrWhiteSpace(ruleText))
            {
                continue;
            }

            if (!grouped.TryGetValue(stationCode, out var rules))
            {
                rules = new List<string>();
                grouped[stationCode] = rules;
            }

            rules.Add(ruleText);
        }

        return grouped;
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
        await EnsureWorkflowSamplingRuntimeSchemaAsync(connection);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_multiboxes_open ON public.workflow_multiboxes (workflow_part_id, workflow_work_order_id, status)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_pallet_items_pallet ON public.workflow_pallet_items (pallet_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_shipment_items_shipment ON public.workflow_shipment_items (shipment_id)");
    }

    private static async Task EnsureWorkflowSamplingRuntimeSchemaAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_sampling ADD COLUMN IF NOT EXISTS interval_time_minutes INTEGER NOT NULL DEFAULT 5");
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
    }

    private static async Task EnsureWorkflowStationLoginsTableAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS public.workflow_station_logins (
              id SERIAL PRIMARY KEY,
              workflow_work_order_id INTEGER REFERENCES workflow_work_orders(id) ON DELETE CASCADE,
              workflow_routing_step_id INTEGER NOT NULL REFERENCES workflow_routing_steps(id) ON DELETE CASCADE,
              station_login_id VARCHAR(160) NOT NULL,
              station_login_password VARCHAR(220) NOT NULL,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_workflow_station_login_id UNIQUE (workflow_routing_step_id, station_login_id)
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_station_logins ADD COLUMN IF NOT EXISTS workflow_work_order_id INTEGER REFERENCES workflow_work_orders(id) ON DELETE CASCADE");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_station_logins_step ON public.workflow_station_logins (workflow_routing_step_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_station_logins_wo ON public.workflow_station_logins (workflow_work_order_id)");
        await NormalizeWorkflowStationLoginUniquenessAsync(connection);
        await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS uq_workflow_station_login_id_global ON public.workflow_station_logins (UPPER(station_login_id))");
        await ExecuteAsync(
            connection,
            """
            DO $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'chk_workflow_station_login_required'
                  AND conrelid = 'public.workflow_station_logins'::regclass
              ) THEN
                ALTER TABLE public.workflow_station_logins
                  ADD CONSTRAINT chk_workflow_station_login_required
                  CHECK (
                    station_login_id IS NOT NULL AND BTRIM(station_login_id) <> '' AND
                    station_login_password IS NOT NULL AND BTRIM(station_login_password) <> ''
                  ) NOT VALID;
              END IF;
            END $$;
            """);
    }

    private static async Task NormalizeWorkflowStationLoginUniquenessAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            DELETE FROM public.workflow_station_logins
            WHERE station_login_id IS NULL
               OR BTRIM(station_login_id) = ''
               OR station_login_password IS NULL
               OR BTRIM(station_login_password) = ''
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_serial_numbers DROP CONSTRAINT IF EXISTS workflow_serial_numbers_sn_key");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_serials_sn_upper ON public.workflow_serial_numbers (UPPER(sn))");

        await ExecuteAsync(
            connection,
            """
            DELETE FROM public.workflow_station_logins l
            USING (
              SELECT id,
                     ROW_NUMBER() OVER (
                       PARTITION BY UPPER(BTRIM(station_login_id))
                       ORDER BY updated_at DESC, id DESC
                     ) AS row_num
              FROM public.workflow_station_logins
              WHERE station_login_id IS NOT NULL
                AND BTRIM(station_login_id) <> ''
            ) duplicates
            WHERE l.id = duplicates.id
              AND duplicates.row_num > 1
            """);
    }

    private static async Task<Dictionary<string, object?>?> FindWorkflowStationLoginConflictAsync(
        NpgsqlConnection connection,
        string loginId,
        int? excludeLoginRowId = null,
        int? excludeRoutingStepId = null,
        int? excludeWorkflowPartId = null)
    {
        var filters = new List<string>
        {
            "UPPER(BTRIM(l.station_login_id)) = UPPER(BTRIM(@loginId))"
        };
        var parameters = new List<(string Name, object? Value)>
        {
            ("loginId", loginId)
        };

        if (excludeLoginRowId is not null)
        {
            filters.Add("l.id <> @excludeLoginRowId");
            parameters.Add(("excludeLoginRowId", excludeLoginRowId.Value));
        }

        if (excludeRoutingStepId is not null)
        {
            filters.Add("r.id <> @excludeRoutingStepId");
            parameters.Add(("excludeRoutingStepId", excludeRoutingStepId.Value));
        }

        if (excludeWorkflowPartId is not null)
        {
            filters.Add("r.workflow_part_id <> @excludeWorkflowPartId");
            parameters.Add(("excludeWorkflowPartId", excludeWorkflowPartId.Value));
        }

        var rows = await QueryRowsAsync(
            connection,
            $"""
            SELECT station_code, pn, wo
            FROM (
                SELECT r.station_code, p.pn, w.wo
                FROM workflow_station_logins l
                JOIN workflow_routing_steps r ON r.id = l.workflow_routing_step_id
                JOIN workflow_part_numbers p ON p.id = r.workflow_part_id
                LEFT JOIN workflow_work_orders w ON w.id = l.workflow_work_order_id
                WHERE {string.Join(" AND ", filters)}
            ) conflicts
            LIMIT 1
            """,
            parameters.ToArray());

        return rows.Count > 0 ? rows[0] : null;
    }

    private static string FormatStationLoginConflictMessage(string loginId, Dictionary<string, object?> conflict)
    {
        var stationCode = conflict["station_code"]?.ToString();
        var pn = conflict["pn"]?.ToString();
        var wo = conflict["wo"]?.ToString();
        var location = string.IsNullOrWhiteSpace(stationCode) ? "another station" : $"station {stationCode}";

        if (!string.IsNullOrWhiteSpace(pn) && !string.IsNullOrWhiteSpace(wo))
        {
            location += $" (PN {pn}, WO {wo})";
        }
        else if (!string.IsNullOrWhiteSpace(pn))
        {
            location += $" (PN {pn})";
        }

        return $"Login ID {loginId} is already used by {location}";
    }
    private static async Task EnsureRoutingStepLoginColumnsAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_login_id VARCHAR(160)");
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_login_password VARCHAR(220)");
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS station_ip VARCHAR(80)");
        await ExecuteAsync(connection, "ALTER TABLE public.item_routing_steps ADD COLUMN IF NOT EXISTS printer_ip VARCHAR(80)");
    }
    private static bool IsPackStation(string? stationCode, string? stationName)
    {
        var text = $"{stationCode} {stationName}";
        return text.Contains("PACK", StringComparison.OrdinalIgnoreCase);
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
