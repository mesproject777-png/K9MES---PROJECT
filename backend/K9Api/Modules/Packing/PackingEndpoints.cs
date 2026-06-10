using System.Text.Json.Nodes;
using Npgsql;

public static class PackingEndpoints
{
    public static void MapPacking(WebApplication app)
    {
        app.MapGet("/api/packing/open", async () => await ListPackagesAsync("OPEN"));
        app.MapGet("/api/packing/closed", async () => await ListPackagesAsync("CLOSED"));
        app.MapGet("/api/packing/shipped", async () => await ListPackagesAsync("SHIPPED"));
        app.MapGet("/api/packing/multibox/{boxNo}", async (string boxNo) => await GetMultiboxPackageDetailsAsync(boxNo));
        app.MapGet("/api/packing/hierarchy", async (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString().Trim();
            await using var connection = await OpenConnectionAsync();
            await EnsureWorkflowSchemaAsync(connection);
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  sn.id AS serial_id,
                  sn.sn,
                  sn.rsn,
                  sn.status AS serial_status,
                  sn.condition,
                  wp.pn,
                  w.wo,
                  b.box_no AS multibox_no,
                  b.status AS multibox_status,
                  p.pallet_no,
                  p.status AS pallet_status,
                  s.shipment_no,
                  s.status AS shipment_status,
                  COALESCE(si.added_at, pi.added_at, mbi.added_at, sn.updated_at, sn.created_at) AS last_packed_at
                FROM workflow_serial_numbers sn
                JOIN workflow_part_numbers wp ON wp.id = sn.workflow_part_id
                LEFT JOIN workflow_work_orders w ON w.id = sn.workflow_work_order_id
                LEFT JOIN workflow_multibox_items mbi ON mbi.workflow_serial_id = sn.id
                LEFT JOIN workflow_multiboxes b ON b.id = mbi.box_id
                LEFT JOIN workflow_pallet_items pi ON pi.box_id = b.id
                LEFT JOIN workflow_pallets p ON p.id = pi.pallet_id
                LEFT JOIN workflow_shipment_items si ON si.pallet_id = p.id
                LEFT JOIN workflow_shipments s ON s.id = si.shipment_id
                WHERE NULLIF(@query, '') IS NULL
                   OR sn.sn ILIKE @pattern
                   OR sn.rsn ILIKE @pattern
                   OR wp.pn ILIKE @pattern
                   OR w.wo ILIKE @pattern
                   OR b.box_no ILIKE @pattern
                   OR p.pallet_no ILIKE @pattern
                   OR s.shipment_no ILIKE @pattern
                ORDER BY COALESCE(si.added_at, pi.added_at, mbi.added_at, sn.updated_at, sn.created_at) DESC, sn.id DESC
                LIMIT 500
                """,
                ("query", query),
                ("pattern", $"%{query}%"));
            return Results.Json(new { data = rows });
        });

        app.MapPost("/api/packing/create", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var packageType = ReadString(payload, "package_type")?.Trim().ToUpperInvariant();
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
            if (packageType is not ("BOX" or "SHIPMENT"))
            {
                return JsonMessage("package_type must be BOX or SHIPMENT", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            var packageNo = $"{(packageType == "SHIPMENT" ? "SHP" : "BOX")}-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            var rows = await QueryRowsAsync(
                connection,
                """
                INSERT INTO packing_packages (package_no, package_type, status, created_by, updated_at)
                VALUES (@packageNo, @packageType, 'OPEN', @changedBy, NOW())
                RETURNING id, package_no, package_type, status, created_by, created_at
                """,
                ("packageNo", packageNo),
                ("packageType", packageType),
                ("changedBy", changedBy));
            return Results.Json(new { data = rows[0] }, statusCode: 201);
        });

        app.MapGet("/api/packing/{packageId:long}", async (long packageId) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            var packages = await QueryRowsAsync(connection, "SELECT * FROM packing_packages WHERE id = @id LIMIT 1", ("id", packageId));
            if (packages.Count == 0)
            {
                return JsonMessage("Package not found", 404);
            }

            var items = await QueryRowsAsync(
                connection,
                """
                SELECT i.id, sn.sn, sn.rsn, sn.status AS serial_status, sn.condition, it.pn,
                       COALESCE(ir.revision, '-') AS revision, i.added_by, i.added_at
                FROM packing_package_items i
                JOIN serial_numbers sn ON sn.id = i.serial_id
                JOIN items it ON it.id = sn.item_id
                LEFT JOIN item_revisions ir ON ir.id = sn.item_revision_id
                WHERE i.package_id = @id
                ORDER BY i.added_at DESC, i.id DESC
                LIMIT 500
                """,
                ("id", packageId));
            return Results.Json(new { package = packages[0], items });
        });

        app.MapPost("/api/packing/{packageId:long}/add", async (long packageId, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var query = ReadString(payload, "query")?.Trim();
            var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonMessage("SN or RSN is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureSerialTrackingSchemaAsync(connection);
            await EnsureWorkflowSchemaAsync(connection);
            try
            {
                var packages = await QueryRowsAsync(connection, "SELECT * FROM packing_packages WHERE id = @id LIMIT 1", ("id", packageId));
                if (packages.Count == 0)
                {
                    return JsonMessage("Package not found", 404);
                }

                if (!string.Equals(packages[0]["status"]?.ToString(), "OPEN", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonMessage("Package is not OPEN", 409);
                }

                var multiboxRows = await QueryRowsAsync(
                    connection,
                    "SELECT id, box_no, status FROM workflow_multiboxes WHERE UPPER(box_no) = UPPER(@query) LIMIT 1",
                    ("query", query));
                if (multiboxRows.Count > 0)
                {
                    if (string.Equals(packages[0]["package_type"]?.ToString(), "SHIPMENT", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(multiboxRows[0]["status"]?.ToString(), "OPEN", StringComparison.OrdinalIgnoreCase))
                    {
                        return JsonMessage("Open MultiBox cannot be packed into pallet", 409);
                    }

                    return JsonMessage("MultiBox pallet packing is not available for this package", 409);
                }

                var alreadyInMultibox = await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)::int
                    FROM workflow_multibox_items mbi
                    JOIN workflow_serial_numbers sn ON sn.id = mbi.workflow_serial_id
                    WHERE UPPER(sn.sn) = UPPER(@query)
                       OR UPPER(sn.rsn) = UPPER(@query)
                    """,
                    ("query", query));
                if (alreadyInMultibox > 0)
                {
                    return JsonMessage("This SN is already scanned into a MultiBox", 409);
                }

                var serial = await GetSerialByQueryAsync(connection, query);
                if (serial is null)
                {
                    return JsonMessage("SN/RSN not found", 404);
                }

                if (string.Equals(serial["serial_status"]?.ToString(), "Completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serial["serial_status"]?.ToString(), "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serial["serial_status"]?.ToString(), "SCRAP", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonMessage("SN is not eligible for packing", 409);
                }

                await ExecuteAsync(
                    connection,
                    "INSERT INTO packing_package_items (package_id, serial_id, added_by) VALUES (@packageId, @serialId, @changedBy)",
                    ("packageId", packageId),
                    ("serialId", serial["id"]),
                    ("changedBy", changedBy));
                await ExecuteAsync(connection, "UPDATE packing_packages SET updated_at = NOW() WHERE id = @id", ("id", packageId));
                return Results.Json(new { message = "SN packed successfully" }, statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonMessage("This SN is already packed in a package", 409);
            }
        });

        app.MapPost("/api/packing/{packageId:long}/close", async (long packageId, HttpContext context) => await UpdatePackageStatusAsync(context, packageId, "OPEN", "CLOSED"));
        app.MapPost("/api/packing/{packageId:long}/ship", async (long packageId, HttpContext context) => await UpdatePackageStatusAsync(context, packageId, "CLOSED", "SHIPPED"));
    }

    private static async Task<IResult> ListPackagesAsync(string status)
    {
        await using var connection = await OpenConnectionAsync();
        await EnsureSerialTrackingSchemaAsync(connection);
        await EnsureWorkflowSchemaAsync(connection);
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT *
            FROM (
                SELECT p.id, p.package_no, p.package_type, p.status, p.created_by, p.created_at,
                       p.closed_by, p.closed_at, p.shipped_by, p.shipped_at,
                       (SELECT COUNT(*) FROM packing_package_items i WHERE i.package_id = p.id)::int AS item_count,
                       'PACKAGE' AS source
                FROM packing_packages p
                WHERE p.status = @status
                UNION ALL
                SELECT b.id, b.box_no AS package_no, 'MULTIBOX' AS package_type, b.status,
                       b.created_by, b.created_at, NULL AS closed_by, b.closed_at,
                       NULL AS shipped_by, NULL AS shipped_at,
                       (SELECT COUNT(*) FROM workflow_multibox_items i WHERE i.box_id = b.id)::int AS item_count,
                       'MULTIBOX' AS source
                FROM workflow_multiboxes b
                WHERE b.status = @status
                  AND @status <> 'SHIPPED'
                UNION ALL
                SELECT p.id, p.pallet_no AS package_no, 'PALLET' AS package_type, p.status,
                       p.created_by, p.created_at, NULL AS closed_by, p.closed_at,
                       NULL AS shipped_by, NULL AS shipped_at,
                       (SELECT COUNT(*) FROM workflow_pallet_items i WHERE i.pallet_id = p.id)::int AS item_count,
                       'PALLET' AS source
                FROM workflow_pallets p
                WHERE p.status = @status
                UNION ALL
                SELECT s.id, s.shipment_no AS package_no, 'SHIPMENT' AS package_type, s.status,
                       s.created_by, s.created_at, NULL AS closed_by, NULL AS closed_at,
                       NULL AS shipped_by, s.shipped_at,
                       (SELECT COUNT(*) FROM workflow_shipment_items i WHERE i.shipment_id = s.id)::int AS item_count,
                       'SHIPMENT' AS source
                FROM workflow_shipments s
                WHERE s.status = @status
            ) packages
            ORDER BY created_at DESC, id DESC
            LIMIT 200
            """,
            ("status", status));
        return Results.Json(new { data = rows });
    }

    private static async Task<IResult> GetMultiboxPackageDetailsAsync(string boxNo)
    {
        if (string.IsNullOrWhiteSpace(boxNo))
        {
            return JsonMessage("MultiBox serial number is required", 400);
        }

        await using var connection = await OpenConnectionAsync();
        await EnsureWorkflowSchemaAsync(connection);
        var boxes = await QueryRowsAsync(
            connection,
            """
            SELECT b.id, b.box_no AS package_no, 'MULTIBOX' AS package_type, b.status,
                   b.created_by, b.created_at, b.updated_at, NULL AS closed_by, b.closed_at,
                   NULL AS shipped_by, NULL AS shipped_at, 'MULTIBOX' AS source,
                   p.pn, w.wo, p.box_qty,
                   (SELECT COUNT(*) FROM workflow_multibox_items i WHERE i.box_id = b.id)::int AS item_count
            FROM workflow_multiboxes b
            JOIN workflow_part_numbers p ON p.id = b.workflow_part_id
            LEFT JOIN workflow_work_orders w ON w.id = b.workflow_work_order_id
            WHERE UPPER(b.box_no) = UPPER(@boxNo)
            LIMIT 1
            """,
            ("boxNo", boxNo.Trim()));

        if (boxes.Count == 0)
        {
            return JsonMessage("MultiBox not found", 404);
        }

        var items = await QueryRowsAsync(
            connection,
            """
            SELECT i.id, sn.sn, sn.rsn, sn.status AS serial_status, sn.condition,
                   p.pn, COALESCE(w.revision, '-') AS revision, i.added_by, i.added_at
            FROM workflow_multibox_items i
            JOIN workflow_serial_numbers sn ON sn.id = i.workflow_serial_id
            JOIN workflow_part_numbers p ON p.id = sn.workflow_part_id
            JOIN workflow_work_orders w ON w.id = sn.workflow_work_order_id
            WHERE i.box_id = @boxId
            ORDER BY i.added_at DESC, i.id DESC
            LIMIT 500
            """,
            ("boxId", boxes[0]["id"]));

        return Results.Json(new { package = boxes[0], items });
    }

    private static async Task<IResult> UpdatePackageStatusAsync(HttpContext context, long packageId, string expectedStatus, string nextStatus)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        var changedBy = ReadString(payload, "changed_by")?.Trim() ?? "system";
        await using var connection = await OpenConnectionAsync();
        await EnsureSerialTrackingSchemaAsync(connection);
        var packages = await QueryRowsAsync(connection, "SELECT * FROM packing_packages WHERE id = @id LIMIT 1", ("id", packageId));
        if (packages.Count == 0)
        {
            return JsonMessage("Package not found", 404);
        }

        if (!string.Equals(packages[0]["status"]?.ToString(), expectedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return JsonMessage($"Only {expectedStatus} packages can be {(nextStatus == "CLOSED" ? "closed" : "shipped")}", 409);
        }

        if (nextStatus == "CLOSED")
        {
            var count = await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM packing_package_items WHERE package_id = @id", ("id", packageId));
            if (count <= 0)
            {
                return JsonMessage("Cannot close an empty package", 409);
            }
        }

        await ExecuteAsync(
            connection,
            nextStatus == "CLOSED"
                ? "UPDATE packing_packages SET status = 'CLOSED', closed_by = @changedBy, closed_at = NOW(), updated_at = NOW() WHERE id = @id"
                : "UPDATE packing_packages SET status = 'SHIPPED', shipped_by = @changedBy, shipped_at = NOW(), updated_at = NOW() WHERE id = @id",
            ("changedBy", changedBy),
            ("id", packageId));
        return Results.Json(new { message = nextStatus == "CLOSED" ? "Package closed successfully" : "Package shipped successfully" });
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

    private static IResult JsonMessage(string message, int statusCode) => ApiResults.JsonMessage(message, statusCode);
}

