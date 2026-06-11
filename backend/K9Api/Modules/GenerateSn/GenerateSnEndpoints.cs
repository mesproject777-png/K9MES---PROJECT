using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;

public static class GenerateSnEndpoints
{
    public static void MapGenerateSn(WebApplication app)
    {
        app.MapGet("/api/generate-sn/work-orders", async (HttpRequest request) =>
        {
            var wo = request.Query["wo"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(wo))
            {
                return JsonMessage("WO search required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);
            var suggestions = await QueryRowsAsync(
                connection,
                """
                SELECT
                  w.wo,
                  p.pn,
                  w.qty,
                  COALESCE(st.sn_type_name, st_by_name.sn_type_name, p.sn_type_name, '') AS sn_type_name,
                  w.site_name
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                LEFT JOIN sn_types st ON st.id = p.sn_type_id
                LEFT JOIN sn_types st_by_name ON st_by_name.sn_type_name = p.sn_type_name
                WHERE w.wo ILIKE @woPrefix
                ORDER BY
                  CASE WHEN UPPER(w.wo) = UPPER(@wo) THEN 0 ELSE 1 END,
                  w.wo ASC
                LIMIT 10
                """,
                ("wo", wo),
                ("woPrefix", $"{wo}%"));
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  w.id,
                  w.wo,
                  w.qty,
                  GREATEST(COALESCE(w.qty, 0) - COUNT(sn.id)::int, 0) AS balance,
                  COUNT(sn.id)::int AS generated_qty,
                  p.id AS workflow_part_id,
                  p.pn,
                  COALESCE(p.sn_type_id, st_by_name.id) AS sn_type_id,
                  COALESCE(st.sn_type_name, st_by_name.sn_type_name, p.sn_type_name, '') AS sn_type_name,
                  w.plant,
                  w.site_name,
                  w.due_date,
                  w.revision,
                  w.status
                FROM workflow_work_orders w
                JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                LEFT JOIN sn_types st ON st.id = p.sn_type_id
                LEFT JOIN sn_types st_by_name ON st_by_name.sn_type_name = p.sn_type_name
                LEFT JOIN workflow_serial_numbers sn ON sn.workflow_work_order_id = w.id
                WHERE UPPER(w.wo) = UPPER(@wo)
                GROUP BY w.id, p.id, st.id, st_by_name.id
                ORDER BY w.created_at DESC
                """,
                ("wo", wo));
            foreach (var row in rows)
            {
                row["serials"] = await QueryRowsAsync(
                    connection,
                    """
                    SELECT id, sn, rsn, generated_index, status, created_at
                    FROM workflow_serial_numbers
                    WHERE workflow_work_order_id = @workOrderId
                    ORDER BY generated_index ASC, id ASC
                    """,
                    ("workOrderId", row["id"]));
            }

            return Results.Json(new { data = rows, suggestions });
        });

        app.MapPost("/api/generate-sn/generate", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var wo = ReadString(payload, "wo")?.Trim();
            if (string.IsNullOrWhiteSpace(wo))
            {
                return JsonMessage("WO is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var workOrders = await QueryRowsAsync(
                    connection,
                    """
                    SELECT
                      w.id,
                      w.wo,
                      w.qty,
                      w.site_name,
                      w.lot,
                      p.id AS workflow_part_id,
                      p.pn,
                      COALESCE(p.sn_type_id, st_by_name.id) AS sn_type_id,
                      COALESCE(st.sn_type_name, st_by_name.sn_type_name, p.sn_type_name, '') AS sn_type_name
                    FROM workflow_work_orders w
                    JOIN workflow_part_numbers p ON p.id = w.workflow_part_id
                    LEFT JOIN sn_types st ON st.id = p.sn_type_id
                    LEFT JOIN sn_types st_by_name ON st_by_name.sn_type_name = p.sn_type_name
                    WHERE UPPER(w.wo) = UPPER(@wo)
                    FOR UPDATE OF w
                    """,
                    ("wo", wo));
                if (workOrders.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("WO not found", 404);
                }

                var workOrder = workOrders[0];
                var canonicalWo = Convert.ToString(workOrder["wo"]) ?? wo;
                var workOrderId = Convert.ToInt32(workOrder["id"]);
                var workflowPartId = Convert.ToInt32(workOrder["workflow_part_id"]);
                var workOrderQty = Convert.ToInt32(workOrder["qty"] ?? 0);
                var generatedCount = await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*)::int FROM workflow_serial_numbers WHERE workflow_work_order_id = @id",
                    ("id", workOrderId));
                var qtyToGenerate = Math.Max(workOrderQty - generatedCount, 0);
                if (qtyToGenerate <= 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("All SNs are already generated for this WO", 400);
                }

                int? snTypeId = workOrder["sn_type_id"] is null ? null : Convert.ToInt32(workOrder["sn_type_id"]);
                if (snTypeId is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Workflow part number has no Serial Pattern defined", 400);
                }

                var fields = await QueryRowsAsync(
                    connection,
                    "SELECT field_type, field_string, field_size FROM sn_type_fields WHERE sn_type_id = @id ORDER BY sort_order ASC",
                    ("id", snTypeId.Value));
                if (fields.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Serial Pattern has no fields configured", 400);
                }

                var firstRoute = await QueryRowsAsync(
                    connection,
                    "SELECT station_order, station_code FROM workflow_routing_steps WHERE workflow_part_id = @partId ORDER BY station_order ASC, id ASC LIMIT 1",
                    ("partId", workflowPartId));

                var snList = new List<string>();
                var serials = new List<Dictionary<string, object?>>();
                for (var index = 0; index < qtyToGenerate; index++)
                {
                    var generatedIndex = generatedCount + index + 1;
                    var serial = BuildSerialNumber(
                        fields,
                        canonicalWo,
                        generatedIndex - 1,
                        Convert.ToString(workOrder["site_name"]) ?? string.Empty,
                        Convert.ToString(workOrder["lot"]) ?? string.Empty);
                    var inserted = await QueryRowsAsync(
                        connection,
                        """
                        INSERT INTO workflow_serial_numbers
                          (sn, workflow_work_order_id, workflow_part_id, sn_type_id, generated_index, status, condition, current_station_code, current_station_order, last_moved_at)
                        VALUES
                          (@sn, @workOrderId, @workflowPartId, @snTypeId, @generatedIndex, 'New', 'Good', @stationCode, @stationOrder, @lastMovedAt)
                        RETURNING id, sn, rsn, current_station_code, current_station_order, created_at
                        """,
                        ("sn", (object?)serial),
                        ("workOrderId", workOrderId),
                        ("workflowPartId", workflowPartId),
                        ("snTypeId", snTypeId.Value),
                        ("generatedIndex", generatedIndex),
                        ("stationCode", firstRoute.Count > 0 ? firstRoute[0]["station_code"] : null),
                        ("stationOrder", firstRoute.Count > 0 ? firstRoute[0]["station_order"] : null),
                        ("lastMovedAt", firstRoute.Count > 0 ? DateTime.Now : (DateTime?)null));
                    snList.Add(serial);
                    serials.Add(inserted[0]);
                }

                await ExecuteAsync(connection, "UPDATE workflow_work_orders SET updated_at = NOW() WHERE id = @id", ("id", workOrderId));
                await transaction.CommitAsync();
                return Results.Json(new
                {
                    success = true,
                    wo = canonicalWo,
                    qty = qtyToGenerate,
                    total_qty = workOrderQty,
                    generated_qty = generatedCount + qtyToGenerate,
                    remaining_qty = 0,
                    part_number = workOrder["pn"],
                    sn_type_id = snTypeId.Value,
                    sn_type_name = workOrder["sn_type_name"],
                    sns = snList,
                    serials
                });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync();
                return JsonMessage("Serials are already generated for this work order or sequence.", 409);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static string BuildSerialNumber(
        List<Dictionary<string, object?>> fields,
        string wo,
        int zeroBasedIndex,
        string siteName = "",
        string lot = "")
    {
        var now = DateTime.Now;
        var serial = string.Empty;
        var twoDigitYear = now.Year.ToString()[^2..];
        var twoDigitMonth = now.Month.ToString("00");
        var weekOfYear = System.Globalization.ISOWeek.GetWeekOfYear(now).ToString("00");

        foreach (var field in fields)
        {
            var fieldType = field["field_type"]?.ToString() ?? string.Empty;
            var fieldString = field["field_string"]?.ToString() ?? string.Empty;
            var fieldSize = field["field_size"] is null ? 5 : Convert.ToInt32(field["field_size"]);
            serial += fieldType switch
            {
                "RY" => GetRelianceYearCode(now.Year),
                "RM" => GetRelianceMonthCode(now.Month),
                "Y" => now.Year.ToString()[^1..],
                "YY" => twoDigitYear,
                "YYY" => now.Year.ToString(),
                "M(hex)" => now.Month.ToString("X"),
                "MM(dec)" => now.Month.ToString("00"),
                "R_YY" => Reverse(twoDigitYear),
                "R_MM(dec)" => Reverse(twoDigitMonth),
                "R_WW" => Reverse(weekOfYear),
                "DM" => ((int)now.DayOfWeek + 1).ToString(),
                "DD" => now.Day.ToString("00"),
                "DDD" => now.DayOfYear.ToString("000"),
                "WW" => weekOfYear,
                "String" or "Specific by PN" or "MACgen" or "Programmable" or "RMA" or "EPV" or "SNFromEPV" => fieldString,
                "Lot" => string.IsNullOrWhiteSpace(fieldString) ? lot : fieldString,
                "WO" => wo,
                "SiteCode" => string.IsNullOrWhiteSpace(fieldString) ? BuildSiteCode(siteName, fieldSize) : fieldString,
                "Sequence(dec)" or "Continuous sequence(dec)" => (zeroBasedIndex + 1).ToString().PadLeft(fieldSize, '0'),
                "Sequence(hex)" or "Continuous sequence(hex)" => (zeroBasedIndex + 1).ToString("X").PadLeft(fieldSize, '0'),
                "Sequence(alpha)" or "Continuous sequence(alpha)" => ToBase36(zeroBasedIndex + 1).PadLeft(fieldSize, '0'),
                _ => fieldString
            };
        }

        return serial;
    }

    private static string GetRelianceYearCode(int year)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var offset = year - 2014;
        return offset >= 0 && offset < alphabet.Length ? alphabet[offset].ToString() : year.ToString()[^1..];
    }

    private static string GetRelianceMonthCode(int month)
    {
        const string alphabet = "ABCDEFGHIJKL";
        return month >= 1 && month <= 12 ? alphabet[month - 1].ToString() : string.Empty;
    }

    private static string Reverse(string value)
    {
        return new string(value.Reverse().ToArray());
    }

    private static string BuildSiteCode(string siteName, int fieldSize)
    {
        var normalized = Regex.Replace(siteName.ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.Length <= fieldSize ? normalized : normalized[..fieldSize];
    }

    private static string ToBase36(int value)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (value <= 0)
        {
            return "0";
        }

        var result = string.Empty;
        while (value > 0)
        {
            result = alphabet[value % 36] + result;
            value /= 36;
        }

        return result;
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
}

