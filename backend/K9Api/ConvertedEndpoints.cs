using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

public static class ConvertedEndpoints
{
    private static readonly HashSet<string> AllowedItemTypes = new(StringComparer.Ordinal)
    {
        "Manufactured",
        "Purchased"
    };

    private const int MaxEpvUploadBytes = 10 * 1024 * 1024;
    private const int MaxEpvValues = 50000;

    private static readonly IReadOnlyDictionary<string, string> AllowedSnFieldTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RY"] = "Reliance Year (2014=A, 2015=B, ...)",
        ["RM"] = "Reliance Month (Jan=A, Feb=B, ...)",
        ["RMA"] = "Reliance RMA indicator (non-RMA/RMA)",
        ["Y"] = "Single digit year",
        ["YY"] = "Two digits year",
        ["YYY"] = "Full year (4 digits)",
        ["M(hex)"] = "Month hexadecimal",
        ["MM(dec)"] = "Month decimal",
        ["R_YY"] = "Reversed two digits year",
        ["R_MM(dec)"] = "Reversed month decimal",
        ["R_WW"] = "Reversed week of year",
        ["WW"] = "Week of year",
        ["DM"] = "Day of week",
        ["DD"] = "Date of month",
        ["DDD"] = "Day of year",
        ["String"] = "Constant string",
        ["Specific by PN"] = "PN specific field",
        ["Sequence(dec)"] = "Decimal counter",
        ["Sequence(hex)"] = "Hexadecimal counter",
        ["Sequence(alpha)"] = "Alphanumeric counter",
        ["Continuous sequence(dec)"] = "Continuous decimal counter",
        ["Continuous sequence(hex)"] = "Continuous hexadecimal counter",
        ["Continuous sequence(alpha)"] = "Continuous alphanumeric counter",
        ["WO"] = "Work Order number",
        ["Lot"] = "Lot number",
        ["SiteCode"] = "Site code with translation",
        ["SNFromEPV"] = "Generate SN from EPV",
        ["EPV"] = "External Provided Value",
        ["MACgen"] = "MAC address",
        ["Programmable"] = "Programmable field"
    };

    private static readonly HashSet<string> SequenceCounterTypes = new(new[]
    {
        "Sequence(dec)",
        "Sequence(hex)",
        "Sequence(alpha)"
    }, StringComparer.Ordinal);

    private static readonly HashSet<string> AllCounterTypes = new(new[]
    {
        "Sequence(dec)",
        "Sequence(hex)",
        "Sequence(alpha)",
        "Continuous sequence(dec)",
        "Continuous sequence(hex)",
        "Continuous sequence(alpha)"
    }, StringComparer.Ordinal);

    private static readonly HashSet<string> StringValueTypes = new(new[]
    {
        "String",
        "Specific by PN",
        "MACgen"
    }, StringComparer.Ordinal);

    private static readonly HashSet<string> EpvFieldTypes = new(new[]
    {
        "EPV",
        "SNFromEPV"
    }, StringComparer.Ordinal);

    private sealed record NormalizedSnTypeField(
        decimal SortOrder,
        string FieldType,
        string? FieldString,
        int? FieldSize,
        int? EpvTypeId,
        int? EpvSubTypeId);

    public static void MapConvertedEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Json(new
        {
            message = "MES API is running",
            endpoints = new[]
            {
                "/api/users",
                "/api/sn-types",
                "/api/items",
                "/api/item-revisions",
                "/api/routing",
                "/api/workflow",
                "/api/stations",
                "/api/work-orders",
                "/api/sites",
                "/api/traceability",
                "/api/labels",
                "/api/sgd-pos"
            }
        }));

        MapSites(app);
        MapUserLogin(app);
        MapStations(app);
        MapItems(app);
        MapItemRevisions(app);
        MapEpvTypes(app);
        MapSnTypes(app);
        MapSgdPos(app);
        MapBom(app);
        MapRouting(app);
        MapWorkflow(app);
        MapWorkOrders(app);
        MapGenerateSn(app);
        MapTraceability(app);
        MapPacking(app);
        MapAssembly(app);
        MapLabels(app);
    }

    private static void MapSites(WebApplication app)
    {
        app.MapGet("/api/sites", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            return Results.Json(await QueryRowsAsync(connection, "SELECT id, name, created_at FROM sites ORDER BY name ASC"));
        });

        app.MapPost("/api/sites", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var name = ReadString(payload, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return JsonError("name is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    "INSERT INTO sites (name) VALUES (@name) RETURNING id, name, created_at",
                    ("name", name));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonError("Site already exists", 409);
            }
        });

        app.MapPut("/api/sites/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var name = ReadString(payload, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return JsonError("name is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    "UPDATE sites SET name = @name WHERE id = @id RETURNING id, name, created_at",
                    ("name", name),
                    ("id", id));
                return rows.Count == 0 ? JsonError("Site not found", 404) : Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonError("Site already exists", 409);
            }
        });

        app.MapDelete("/api/sites/{id:int}", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            try
            {
                var rows = await QueryRowsAsync(connection, "DELETE FROM sites WHERE id = @id RETURNING id", ("id", id));
                return rows.Count == 0 ? JsonError("Site not found", 404) : Results.Json(new { message = "Site deleted successfully" });
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                return JsonError("Site is in use by work orders", 409);
            }
        });
    }

    private static void MapUserLogin(WebApplication app)
    {
        app.MapPost("/api/users/login", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var loginId = ReadString(payload, "loginId")?.Trim();
            var password = ReadString(payload, "password");
            if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(password))
            {
                return JsonError("loginId and password are required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT u.id, u.login_id, u.user_name, u.password, u.is_active, u.created_at,
                       r.id AS role_id, r.role_name, COALESCE(r.page_access, '{}') AS page_access
                FROM users u
                LEFT JOIN roles r ON r.id = u.role_id
                WHERE u.login_id = @loginId
                LIMIT 1
                """,
                ("loginId", loginId));
            if (rows.Count == 0)
            {
                return JsonError("Invalid login ID or password", 401);
            }

            var user = rows[0];
            if (user["password"] is not string storedPassword || !string.Equals(storedPassword, password, StringComparison.Ordinal))
            {
                return JsonError("Invalid login ID or password", 401);
            }

            if (user["is_active"] is bool active && !active)
            {
                return JsonError("User is inactive and cannot log in", 403);
            }

            user.Remove("password");
            return Results.Json(user);
        });
    }

    private static void MapStations(WebApplication app)
    {
        app.MapGet("/api/stations", async (HttpRequest request) =>
        {
            var page = ParsePositiveInt(request.Query["page"], 1);
            var limitRaw = request.Query["limit"].ToString().Trim().ToLowerInvariant();
            var search = request.Query["search"].ToString().Trim();
            var parameters = new List<(string Name, object? Value)>();
            var whereSql = string.Empty;

            if (!string.IsNullOrWhiteSpace(search))
            {
                whereSql = "WHERE ms.masterstation_code ILIKE @search OR ms.masterstation_name ILIKE @search OR ms.masterstation_description ILIKE @search";
                parameters.Add(("search", $"%{search}%"));
            }

            await using var connection = await OpenConnectionAsync();
            if (limitRaw == "all")
            {
                var allRows = await QueryRowsAsync(
                    connection,
                    $"""
                    SELECT
                      ms.masterstation_id AS id,
                      ms.masterstation_code AS station_code,
                      ms.masterstation_name AS station_desc,
                      ms.masterstation_description,
                      COUNT(*) OVER () AS total_count
                    FROM masterstation ms
                    {whereSql}
                    ORDER BY ms.masterstation_code ASC
                    """,
                    parameters.ToArray());
                var totalAll = allRows.Count > 0 ? Convert.ToInt32(allRows[0]["total_count"] ?? 0) : 0;
                return Results.Json(new { data = MapStations(allRows), total = totalAll, page = 1, limit = totalAll == 0 ? 1 : totalAll });
            }

            var limit = Math.Min(ParsePositiveInt(limitRaw, 25), 500);
            var offset = (page - 1) * limit;
            parameters.Add(("limit", limit));
            parameters.Add(("offset", offset));

            var rows = await QueryRowsAsync(
                connection,
                $"""
                SELECT
                  ms.masterstation_id AS id,
                  ms.masterstation_code AS station_code,
                  ms.masterstation_name AS station_desc,
                  ms.masterstation_description,
                  COUNT(*) OVER () AS total_count
                FROM masterstation ms
                {whereSql}
                ORDER BY ms.masterstation_code ASC
                LIMIT @limit OFFSET @offset
                """,
                parameters.ToArray());
            var total = rows.Count > 0 ? Convert.ToInt32(rows[0]["total_count"] ?? 0) : 0;
            return Results.Json(new { data = MapStations(rows), total, page, limit });
        });

        app.MapPost("/api/stations", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var code = ReadString(payload, "station_code")?.Trim();
            var desc = ReadString(payload, "station_desc")?.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(desc))
            {
                return JsonMessage("station_code and station_desc are required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO masterstation (masterstation_code, masterstation_name, masterstation_description)
                    VALUES (@code, @desc, @desc)
                    RETURNING masterstation_id AS id, masterstation_code AS station_code, masterstation_name AS station_desc
                    """,
                    ("code", code),
                    ("desc", desc));
                rows[0]["status"] = "Active";
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonMessage("Station code already exists", 409);
            }
        });

        app.MapPut("/api/stations/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var code = ReadString(payload, "station_code")?.Trim();
            var desc = ReadString(payload, "station_desc")?.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(desc))
            {
                return JsonMessage("station_code and station_desc are required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    UPDATE masterstation
                    SET masterstation_code = @code,
                        masterstation_name = @desc,
                        masterstation_description = @desc
                    WHERE masterstation_id = @id
                    RETURNING masterstation_id AS id, masterstation_code AS station_code, masterstation_name AS station_desc
                    """,
                    ("code", code),
                    ("desc", desc),
                    ("id", id));
                if (rows.Count == 0)
                {
                    return JsonMessage("Station not found", 404);
                }

                rows[0]["status"] = "Active";
                return Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonMessage("Station code already exists", 409);
            }
        });

        app.MapDelete("/api/stations/{id:int}", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(connection, "DELETE FROM masterstation WHERE masterstation_id = @id RETURNING masterstation_id", ("id", id));
            return rows.Count == 0 ? JsonMessage("Station not found", 404) : Results.Json(new { message = "Station deleted successfully" });
        });

        app.MapPost("/api/stations/import", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var sourceRows = payload switch
            {
                JsonArray array => array.OfType<JsonNode>().ToArray(),
                null => Array.Empty<JsonNode>(),
                _ => new[] { payload }
            };

            var byCode = new Dictionary<string, (string Code, string Desc)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in sourceRows)
            {
                var code = ReadString(row, "station_code")?.Trim();
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                var desc = ReadString(row, "station_desc")?.Trim();
                byCode[code] = (code, string.IsNullOrWhiteSpace(desc) ? code : desc);
            }

            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            var inserted = 0;
            var updated = 0;
            var skipped = 0;

            try
            {
                foreach (var station in byCode.Values)
                {
                    var existing = await QueryRowsAsync(
                        connection,
                        """
                        SELECT masterstation_id, masterstation_name, masterstation_description
                        FROM masterstation
                        WHERE UPPER(masterstation_code) = UPPER(@code)
                        LIMIT 1
                        """,
                        ("code", station.Code));

                    if (existing.Count == 0)
                    {
                        await ExecuteAsync(
                            connection,
                            """
                            INSERT INTO masterstation (masterstation_code, masterstation_name, masterstation_description)
                            VALUES (@code, @name, @description)
                            """,
                            ("code", station.Code),
                            ("name", station.Desc),
                            ("description", station.Desc));
                        inserted++;
                        continue;
                    }

                    var sameName = string.Equals(existing[0]["masterstation_name"]?.ToString(), station.Desc, StringComparison.Ordinal);
                    var sameDesc = string.Equals(existing[0]["masterstation_description"]?.ToString(), station.Desc, StringComparison.Ordinal);
                    if (sameName && sameDesc)
                    {
                        skipped++;
                        continue;
                    }

                    await ExecuteAsync(
                        connection,
                        """
                        UPDATE masterstation
                        SET masterstation_name = @name,
                            masterstation_description = @description
                        WHERE masterstation_id = @id
                        """,
                        ("name", station.Desc),
                        ("description", station.Desc),
                        ("id", existing[0]["masterstation_id"]));
                    updated++;
                }

                await transaction.CommitAsync();
                var total = await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM masterstation");
                return Results.Json(new
                {
                    sourceRows = sourceRows.Length,
                    uniqueCodes = byCode.Count,
                    inserted,
                    updated,
                    skipped,
                    totalInDb = total
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static void MapItems(WebApplication app)
    {
        app.MapGet("/api/items", async (HttpRequest request) =>
        {
            var page = ParsePositiveInt(request.Query["page"], 1);
            var limit = Math.Min(ParsePositiveInt(request.Query["limit"], 15), 500);
            var search = request.Query["search"].ToString().Trim();
            var offset = (page - 1) * limit;
            var parameters = new List<(string Name, object? Value)>();
            var whereSql = string.Empty;

            if (!string.IsNullOrWhiteSpace(search))
            {
                whereSql = "WHERE i.pn ILIKE @search OR i.description ILIKE @search";
                parameters.Add(("search", $"%{search}%"));
            }

            parameters.Add(("limit", limit));
            parameters.Add(("offset", offset));

            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                $"""
                SELECT
                  i.id,
                  i.pn,
                  i.description,
                  i.marketing_desc,
                  i.phantom,
                  i.sgd_control,
                  i.item_type,
                  i.created_at,
                  i.updated_at,
                  pl.id AS product_line_id,
                  pl.code AS product_line_code,
                  pl.description AS product_line_description,
                  st.id AS sn_type_id,
                  st.sn_type_name AS sn_type_name,
                  pt.id AS pn_type_id,
                  pt.code AS pn_type_code,
                  pt.type AS pn_type_name,
                  COUNT(*) OVER () AS total_count
                FROM items i
                LEFT JOIN product_lines pl ON pl.id = i.product_line_id
                LEFT JOIN sn_types st ON st.id = i.sn_type_id
                LEFT JOIN pn_types pt ON pt.id = i.pn_type_id
                {whereSql}
                ORDER BY i.created_at DESC, i.id DESC
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

        app.MapPost("/api/items", async (HttpContext context) => await SaveItemAsync(context, null));
        app.MapPut("/api/items/{id:int}", async (HttpContext context, int id) => await SaveItemAsync(context, id));
    }

    private static void MapItemRevisions(WebApplication app)
    {
        app.MapGet("/api/item-revisions/lookup", async (HttpRequest request) =>
        {
            var search = request.Query["search"].ToString().Trim();
            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT id, pn, description
                FROM items
                WHERE @search = '' OR pn ILIKE @pattern OR description ILIKE @pattern
                ORDER BY pn ASC
                LIMIT 25
                """,
                ("search", search),
                ("pattern", $"%{search}%"));
            return Results.Json(rows);
        });

        app.MapGet("/api/item-revisions/by-pn", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return JsonMessage("pn is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn", ("pn", pn));
            return rows.Count == 0 ? JsonMessage("Item not found", 404) : Results.Json(rows[0]);
        });

        app.MapGet("/api/item-revisions/{itemId:int}/revisions", async (int itemId) =>
        {
            await using var connection = await OpenConnectionAsync();
            var itemRows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE id = @id", ("id", itemId));
            if (itemRows.Count == 0)
            {
                return JsonMessage("Item not found", 404);
            }

            var revisionRows = await QueryRowsAsync(
                connection,
                """
                SELECT id, item_id, revision, in_date, expire_date, version, description, created_at, updated_at
                FROM item_revisions
                WHERE item_id = @itemId
                ORDER BY in_date DESC, revision DESC
                """,
                ("itemId", itemId));
            return Results.Json(new { item = itemRows[0], revisions = revisionRows });
        });

        app.MapPost("/api/item-revisions/{itemId:int}/revisions", async (int itemId, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var revision = ReadString(payload, "revision")?.Trim();
            var inDate = ReadString(payload, "in_date")?.Trim();
            var expireDate = ReadString(payload, "expire_date")?.Trim();
            var version = ReadString(payload, "version")?.Trim();
            var description = ReadString(payload, "description")?.Trim();
            var changedBy = ReadString(payload, "changed_by") ?? "system";

            if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(inDate))
            {
                return JsonMessage("revision and in_date are required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var itemRows = await QueryRowsAsync(connection, "SELECT id FROM items WHERE id = @id", ("id", itemId));
                if (itemRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Item not found", 404);
                }

                var inserted = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO item_revisions (item_id, revision, in_date, expire_date, version, description)
                    VALUES (@itemId, @revision, @inDate::date, NULLIF(@expireDate, '')::date, @version, @description)
                    RETURNING *
                    """,
                    ("itemId", itemId),
                    ("revision", revision),
                    ("inDate", inDate),
                    ("expireDate", expireDate ?? string.Empty),
                    ("version", ToDbNullable(version)),
                    ("description", ToDbNullable(description)));

                await InsertJsonHistoryAsync(connection, "item_revision_history", "item_revision_id", inserted[0]["id"]!, "CREATE", inserted[0], changedBy);
                await transaction.CommitAsync();
                return Results.Json(inserted[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync();
                return JsonMessage("Revision already exists for this item and in date", 409);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static void MapEpvTypes(WebApplication app)
    {
        app.MapGet("/api/epv-types", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            var types = await QueryRowsAsync(
                connection,
                """
                SELECT
                  t.id,
                  t.type_name,
                  t.regex_rule,
                  t.created_at,
                  t.updated_at,
                  COALESCE(
                    json_agg(
                      json_build_object(
                        'id', st.id,
                        'sub_type_name', st.sub_type_name,
                        'regex_rule', st.regex_rule,
                        'created_at', st.created_at,
                        'updated_at', st.updated_at
                      )
                      ORDER BY st.sub_type_name ASC
                    ) FILTER (WHERE st.id IS NOT NULL),
                    '[]'
                  ) AS sub_types
                FROM epv_types t
                LEFT JOIN epv_sub_types st ON st.epv_type_id = t.id
                GROUP BY t.id
                ORDER BY t.type_name ASC
                """);
            return Results.Json(new { data = types, total = types.Count });
        });

        app.MapGet("/api/epv-types/regex-master", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  t.id AS epv_type_id,
                  t.type_name,
                  t.regex_rule AS type_regex_rule,
                  st.id AS epv_sub_type_id,
                  st.sub_type_name,
                  st.regex_rule AS sub_type_regex_rule,
                  COALESCE(COUNT(v.id), 0)::int AS total_quantity,
                  0::int AS used_quantity,
                  COALESCE(COUNT(v.id), 0)::int AS unused_quantity,
                  t.updated_at AS type_updated_at,
                  st.updated_at AS sub_type_updated_at
                FROM epv_types t
                LEFT JOIN epv_sub_types st ON st.epv_type_id = t.id
                LEFT JOIN sn_type_epv_values v
                  ON v.epv_type_id = t.id
                 AND (st.id IS NULL OR v.epv_sub_type_id = st.id)
                GROUP BY t.id, st.id
                ORDER BY t.type_name ASC, st.sub_type_name ASC
                """);
            return Results.Json(new { data = rows });
        });

        app.MapPost("/api/epv-types", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var typeName = ReadString(payload, "type_name")?.Trim();
            var regexRule = ReadString(payload, "regex_rule")?.Trim();
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(regexRule))
            {
                return JsonMessage("type_name and regex_rule are required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    "INSERT INTO epv_types (type_name, regex_rule) VALUES (@typeName, @regexRule) RETURNING *",
                    ("typeName", typeName),
                    ("regexRule", regexRule));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonMessage("EPV type already exists", 409);
            }
        });

        app.MapDelete("/api/epv-types/{typeId:int}", async (int typeId) =>
        {
            await using var connection = await OpenConnectionAsync();
            var used = await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_type_fields WHERE epv_type_id = @id", ("id", typeId));
            if (used > 0)
            {
                return JsonMessage("EPV type is in use and cannot be deleted", 409);
            }

            var rows = await QueryRowsAsync(connection, "DELETE FROM epv_types WHERE id = @id RETURNING id", ("id", typeId));
            return rows.Count == 0 ? JsonMessage("EPV type not found", 404) : Results.Json(new { message = "EPV type deleted successfully" });
        });

        app.MapGet("/api/epv-types/{typeId:int}/sub-types", async (int typeId) =>
        {
            await using var connection = await OpenConnectionAsync();
            var typeRows = await QueryRowsAsync(
                connection,
                "SELECT id, type_name, regex_rule, created_at, updated_at FROM epv_types WHERE id = @id",
                ("id", typeId));
            if (typeRows.Count == 0)
            {
                return JsonMessage("EPV type not found", 404);
            }

            var rows = await QueryRowsAsync(
                connection,
                "SELECT id, epv_type_id, sub_type_name, regex_rule, created_at, updated_at FROM epv_sub_types WHERE epv_type_id = @id ORDER BY sub_type_name ASC",
                ("id", typeId));
            return Results.Json(new { type = typeRows[0], data = rows });
        });

        app.MapPost("/api/epv-types/{typeId:int}/sub-types", async (int typeId, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var subTypeName = ReadString(payload, "sub_type_name")?.Trim();
            var regexRule = ReadString(payload, "regex_rule")?.Trim();
            if (string.IsNullOrWhiteSpace(subTypeName) || string.IsNullOrWhiteSpace(regexRule))
            {
                return JsonMessage("sub_type_name and regex_rule are required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO epv_sub_types (epv_type_id, sub_type_name, regex_rule)
                    VALUES (@typeId, @subTypeName, @regexRule)
                    RETURNING *
                    """,
                    ("typeId", typeId),
                    ("subTypeName", subTypeName),
                    ("regexRule", regexRule));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonMessage("EPV sub type already exists", 409);
            }
        });

        app.MapDelete("/api/epv-types/sub-types/{subTypeId:int}", async (int subTypeId) =>
        {
            await using var connection = await OpenConnectionAsync();
            var used = await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_type_fields WHERE epv_sub_type_id = @id", ("id", subTypeId));
            if (used > 0)
            {
                return JsonMessage("EPV sub type is in use and cannot be deleted", 409);
            }

            var rows = await QueryRowsAsync(connection, "DELETE FROM epv_sub_types WHERE id = @id RETURNING id", ("id", subTypeId));
            return rows.Count == 0 ? JsonMessage("EPV sub type not found", 404) : Results.Json(new { message = "EPV sub type deleted successfully" });
        });
    }

    private static void MapSnTypes(WebApplication app)
    {
        app.MapGet("/api/sn-types", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  st.id,
                  st.sn_type_name,
                  st.remark,
                  st.created_at,
                  st.updated_at,
                  COALESCE(sf.number_of_fields, 0)::int AS number_of_fields,
                  COALESCE(sf.number_of_fields, 0)::int AS field_count,
                  COUNT(*) OVER ()::int AS total_count
                FROM sn_types st
                LEFT JOIN (
                  SELECT sn_type_id, COUNT(*)::int AS number_of_fields
                  FROM sn_type_fields
                  GROUP BY sn_type_id
                ) sf ON sf.sn_type_id = st.id
                ORDER BY st.created_at DESC, st.id DESC
                """);
            var total = rows.Count > 0 ? Convert.ToInt32(rows[0]["total_count"]) : 0;
            return Results.Json(new { data = rows, total });
        });

        app.MapGet("/api/sn-types/reference/field-types", () => Results.Json(AllowedSnFieldTypes));

        app.MapGet("/api/sn-types/{id:int}", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            var typeRows = await QueryRowsAsync(connection, "SELECT * FROM sn_types WHERE id = @id", ("id", id));
            if (typeRows.Count == 0)
            {
                return JsonMessage("SN type not found", 404);
            }

            typeRows[0]["fields"] = await GetSnTypeFieldsAsync(connection, id);
            return Results.Json(typeRows[0]);
        });

        app.MapPost("/api/sn-types", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var name = ReadString(payload, "sn_type_name")?.Trim();
            var remark = ReadString(payload, "remark")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return JsonMessage("sn_type_name is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    "INSERT INTO sn_types (sn_type_name, remark) VALUES (@name, @remark) RETURNING *",
                    ("name", name),
                    ("remark", ToDbNullable(remark)));

                var defaultFields = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO sn_type_fields (sn_type_id, sort_order, field_type, field_string, field_size)
                    VALUES (@snTypeId, 10, 'Y', NULL, NULL)
                    RETURNING *
                    """,
                    ("snTypeId", rows[0]["id"]));

                await transaction.CommitAsync();
                rows[0]["fields"] = defaultFields;
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync();
                return JsonMessage("SN type already exists", 409);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapPut("/api/sn-types/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            await using var connection = await OpenConnectionAsync();
            var existingRows = await QueryRowsAsync(connection, "SELECT * FROM sn_types WHERE id = @id", ("id", id));
            if (existingRows.Count == 0)
            {
                return JsonMessage("SN type not found", 404);
            }

            var name = HasJsonProperty(payload, "sn_type_name")
                ? ReadFlexibleString(payload?["sn_type_name"])?.Trim()
                : existingRows[0]["sn_type_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return JsonMessage("sn_type_name is required", 400);
            }

            var remark = HasJsonProperty(payload, "remark")
                ? ReadFlexibleString(payload?["remark"])?.Trim()
                : existingRows[0]["remark"]?.ToString();

            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    "UPDATE sn_types SET sn_type_name = @name, remark = @remark, updated_at = NOW() WHERE id = @id RETURNING *",
                    ("name", name),
                    ("remark", ToDbNullable(string.IsNullOrWhiteSpace(remark) ? null : remark)),
                    ("id", id));
                return Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonMessage("SN type already exists", 409);
            }
        });

        app.MapDelete("/api/sn-types/{id:int}", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await ExecuteAsync(connection, "DELETE FROM sn_type_fields WHERE sn_type_id = @id", ("id", id));
                var rows = await QueryRowsAsync(connection, "DELETE FROM sn_types WHERE id = @id RETURNING id", ("id", id));
                await transaction.CommitAsync();
                return rows.Count == 0 ? JsonMessage("SN type not found", 404) : Results.Json(new { message = "SN type deleted successfully" });
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                await transaction.RollbackAsync();
                return JsonMessage("SN type is in use and cannot be deleted", 409);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapPost("/api/sn-types/{snTypeId:int}/fields", async (int snTypeId, HttpContext context) => await SaveSnTypeFieldAsync(context, snTypeId, null));
        app.MapPut("/api/sn-types/fields/{fieldId:int}", async (int fieldId, HttpContext context) => await SaveSnTypeFieldAsync(context, null, fieldId));
        app.MapDelete("/api/sn-types/fields/{fieldId:int}", async (int fieldId) =>
        {
            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(connection, "DELETE FROM sn_type_fields WHERE id = @id RETURNING id", ("id", fieldId));
            return rows.Count == 0 ? JsonMessage("SN type field not found", 404) : Results.Json(new { message = "SN type field deleted successfully" });
        });

        app.MapGet("/api/sn-types/{id:int}/epv-uploads", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            if (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", id)) == 0)
            {
                return JsonMessage("SN Type not found", 404);
            }

            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT u.id, u.sn_type_id, u.file_name, u.mime_type, u.source_kind, u.record_count,
                       u.epv_type_id, et.type_name AS epv_type_name,
                       u.epv_sub_type_id, est.sub_type_name AS epv_sub_type_name,
                       u.created_at,
                       COALESCE(u.extracted_values->>0, '') AS first_value
                FROM sn_type_epv_uploads u
                LEFT JOIN epv_types et ON et.id = u.epv_type_id
                LEFT JOIN epv_sub_types est ON est.id = u.epv_sub_type_id
                WHERE u.sn_type_id = @id
                ORDER BY u.created_at DESC, u.id DESC
                LIMIT 50
                """,
                ("id", id));
            return Results.Json(new { data = rows });
        });

        app.MapPost("/api/sn-types/{id:int}/epv-upload", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var fileName = ReadString(payload, "file_name")?.Trim();
            var mimeType = ReadString(payload, "mime_type")?.Trim();
            var contentBase64 = ReadString(payload, "file_content_base64");
            var epvTypeId = ReadInt(payload, "epv_type_id");
            var epvSubTypeId = ReadInt(payload, "epv_sub_type_id");

            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentBase64))
            {
                return JsonMessage("file_name and file_content_base64 are required", 400);
            }

            if (epvTypeId is null || epvSubTypeId is null)
            {
                return JsonMessage("epv_type_id and epv_sub_type_id are required", 400);
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(contentBase64);
            }
            catch (FormatException)
            {
                return JsonMessage("Invalid base64 file content", 400);
            }

            if (bytes.Length == 0)
            {
                return JsonMessage("Uploaded file is empty", 400);
            }

            if (bytes.Length > MaxEpvUploadBytes)
            {
                return JsonMessage($"File is too large. Max allowed size is {MaxEpvUploadBytes / (1024 * 1024)}MB", 413);
            }

            var fileKind = DetectFileKind(fileName, mimeType);
            if (fileKind == "unknown")
            {
                return JsonMessage("Unsupported file type. Allowed: .pdf, .txt, .csv, .json", 400);
            }

            var text = fileKind == "pdf"
                ? ExtractTextFromPdfBuffer(bytes)
                : Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(text))
            {
                return JsonMessage("No readable text found in uploaded EPV file", 400);
            }

            var values = ExtractEpvValues(text);
            if (values.Length == 0)
            {
                return JsonMessage("No EPV values were detected in the uploaded file", 400);
            }

            await using var connection = await OpenConnectionAsync();
            if (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", id)) == 0)
            {
                return JsonMessage("SN Type not found", 404);
            }

            var epvTypeRows = await QueryRowsAsync(
                connection,
                "SELECT id, type_name, regex_rule FROM epv_types WHERE id = @id",
                ("id", epvTypeId.Value));
            if (epvTypeRows.Count == 0)
            {
                return JsonMessage("EPV type not found", 404);
            }

            var epvSubTypeRows = await QueryRowsAsync(
                connection,
                "SELECT id, epv_type_id, sub_type_name, regex_rule FROM epv_sub_types WHERE id = @id",
                ("id", epvSubTypeId.Value));
            if (epvSubTypeRows.Count == 0)
            {
                return JsonMessage("EPV sub-type not found", 404);
            }

            if (Convert.ToInt32(epvSubTypeRows[0]["epv_type_id"]) != epvTypeId.Value)
            {
                return JsonMessage("Selected sub-type does not belong to selected EPV type", 400);
            }

            var regexValidation = ValidateEpvValuesAgainstRegex(values, epvTypeRows[0], epvSubTypeRows[0]);
            if (regexValidation is not null)
            {
                return regexValidation;
            }

            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var uploadRows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO sn_type_epv_uploads
                      (sn_type_id, file_name, mime_type, source_kind, record_count, epv_type_id, epv_sub_type_id, extracted_values)
                    VALUES
                      (@snTypeId, @fileName, @mimeType, @sourceKind, @recordCount, @epvTypeId, @epvSubTypeId, @values::jsonb)
                    RETURNING id, sn_type_id, file_name, mime_type, source_kind, record_count, epv_type_id, epv_sub_type_id, created_at
                    """,
                    ("snTypeId", id),
                    ("fileName", fileName),
                    ("mimeType", ToDbNullable(mimeType)),
                    ("sourceKind", fileKind),
                    ("recordCount", values.Length),
                    ("epvTypeId", epvTypeId.Value),
                    ("epvSubTypeId", epvSubTypeId.Value),
                    ("values", JsonSerializer.Serialize(values)));

                var maxOrder = await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COALESCE(MAX(value_order), 0)::int
                    FROM sn_type_epv_values
                    WHERE epv_type_id = @epvTypeId
                      AND epv_sub_type_id = @epvSubTypeId
                    """,
                    ("epvTypeId", epvTypeId.Value),
                    ("epvSubTypeId", epvSubTypeId.Value));
                var nextOrderStart = maxOrder + 1;

                for (var index = 0; index < values.Length; index++)
                {
                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO sn_type_epv_values
                          (upload_id, sn_type_id, epv_type_id, epv_sub_type_id, value_order, epv_value)
                        VALUES
                          (@uploadId, @snTypeId, @epvTypeId, @epvSubTypeId, @valueOrder, @value)
                        """,
                        new (string Name, object? Value)[]
                        {
                            ("uploadId", uploadRows[0]["id"]),
                            ("snTypeId", id),
                            ("epvTypeId", epvTypeId.Value),
                            ("epvSubTypeId", epvSubTypeId.Value),
                            ("valueOrder", nextOrderStart + index),
                            ("value", values[index])
                        });
                }

                await transaction.CommitAsync();
                return Results.Json(new
                {
                    message = "EPV file uploaded and processed successfully",
                    upload = uploadRows[0],
                    values_preview = values.Take(20).ToArray(),
                    values_total = values.Length
                }, statusCode: 201);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static void MapSgdPos(WebApplication app)
    {
        app.MapGet("/api/sgd-pos", async (HttpRequest request) =>
        {
            var search = request.Query["search"].ToString().Trim();
            var status = request.Query["status"].ToString().Trim();
            var parameters = new List<(string Name, object? Value)>();
            var where = new List<string>();

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("(sp.po ILIKE @search OR i.pn ILIKE @search)");
                parameters.Add(("search", $"%{search}%"));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                where.Add("sp.status = @status");
                parameters.Add(("status", status));
            }

            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(
                connection,
                $"""
                SELECT sp.id, sp.po, sp.status, sp.sw_version, sp.hw_version, sp.item_id, i.pn, i.description AS item_description,
                       sp.po_qty, sp.created_at, sp.updated_at
                FROM sgd_pos sp
                JOIN items i ON i.id = sp.item_id
                {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
                ORDER BY sp.created_at DESC, sp.id DESC
                """,
                parameters.ToArray());
            return Results.Json(rows);
        });

        app.MapPost("/api/sgd-pos", async (HttpContext context) => await SaveSgdPoAsync(context, null));
        app.MapPut("/api/sgd-pos/{id:int}", async (int id, HttpContext context) => await SaveSgdPoAsync(context, id));
    }

    private static void MapBom(WebApplication app)
    {
        app.MapGet("/api/bom/lookup", async (HttpRequest request) =>
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
                """
                SELECT id, pn, description
                FROM items
                WHERE pn ILIKE @pattern OR description ILIKE @pattern
                ORDER BY pn ASC
                LIMIT @limit
                """,
                ("pattern", $"%{query}%"),
                ("limit", limit));
            return Results.Json(new { data = rows });
        });

        app.MapGet("/api/bom/by-pn", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return JsonMessage("pn is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn LIMIT 1", ("pn", pn));
            return rows.Count == 0 ? JsonMessage("Part number not found", 404) : Results.Json(rows[0]);
        });

        app.MapGet("/api/bom/{itemId:int}/revisions", async (int itemId) =>
        {
            await using var connection = await OpenConnectionAsync();
            var itemRows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE id = @id", ("id", itemId));
            if (itemRows.Count == 0)
            {
                return JsonMessage("Part number not found", 404);
            }

            var revisions = await QueryRowsAsync(
                connection,
                """
                SELECT id, item_id, revision, in_date, expire_date
                FROM item_revisions
                WHERE item_id = @itemId
                ORDER BY in_date DESC, id DESC
                """,
                ("itemId", itemId));
            return Results.Json(new { item = itemRows[0], data = revisions, total = revisions.Count });
        });

        app.MapGet("/api/bom/view/search", async (HttpRequest request) =>
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
            var payload = await GetBomPayloadAsync(connection, pn, revision, includeHistory);
            if (payload is null)
            {
                return JsonMessage("Part number not found", 404);
            }

            if (payload.Revision is null)
            {
                return JsonMessage("Revision not found for this PN", 404);
            }

            return Results.Json(new
            {
                item = payload.Item,
                revision = payload.Revision,
                data = payload.Data,
                history = payload.History,
                total = payload.Data.Count
            });
        });

        app.MapPost("/api/bom/lines", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var mainPn = ReadString(payload, "main_pn")?.Trim();
            var mainRevisionText = ReadString(payload, "main_revision")?.Trim();
            var sonPn = ReadString(payload, "son_pn")?.Trim();
            var sonRevisionText = ReadString(payload, "son_rev")?.Trim();
            var sonQty = ReadInt(payload, "son_qty");
            var referenceDesignators = ReadString(payload, "reference_designators")?.Trim();
            var changedBy = ReadString(payload, "changed_by") ?? "system";

            if (string.IsNullOrWhiteSpace(mainPn))
            {
                return JsonMessage("main_pn is required", 400);
            }

            if (string.IsNullOrWhiteSpace(mainRevisionText))
            {
                return JsonMessage("main_revision is required", 400);
            }

            if (string.IsNullOrWhiteSpace(sonPn))
            {
                return JsonMessage("son_pn is required", 400);
            }

            if (sonQty is null or <= 0)
            {
                return JsonMessage("son_qty must be a positive number", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var mainItem = await FindItemByPnAsync(connection, mainPn);
                if (mainItem is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Main PN not found", 404);
                }

                var mainRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(mainItem["id"]), mainRevisionText);
                if (mainRevision is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Main revision not found for PN", 404);
                }

                var sonItem = await FindItemByPnAsync(connection, sonPn);
                if (sonItem is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Son PN not found", 404);
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

                var duplicate = await QueryRowsAsync(
                    connection,
                    """
                    SELECT id
                    FROM item_bom_lines
                    WHERE main_item_revision_id = @mainRevisionId
                      AND son_item_id = @sonItemId
                      AND COALESCE(son_item_revision_id, 0) = COALESCE(@sonRevisionId, 0)
                      AND COALESCE(reference_designators, '') = COALESCE(@referenceDesignators, '')
                    LIMIT 1
                    """,
                    ("mainRevisionId", mainRevision["id"]),
                    ("sonItemId", sonItem["id"]),
                    ("sonRevisionId", ToDbNullable(sonRevisionId)),
                    ("referenceDesignators", ToDbNullable(referenceDesignators)));
                if (duplicate.Count > 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("BOM line already exists for this combination", 409);
                }

                var rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO item_bom_lines
                      (main_item_id, main_item_revision_id, son_item_id, son_item_revision_id, qty, reference_designators)
                    VALUES
                      (@mainItemId, @mainRevisionId, @sonItemId, @sonRevisionId, @qty, @referenceDesignators)
                    RETURNING *
                    """,
                    ("mainItemId", mainItem["id"]),
                    ("mainRevisionId", mainRevision["id"]),
                    ("sonItemId", sonItem["id"]),
                    ("sonRevisionId", ToDbNullable(sonRevisionId)),
                    ("qty", sonQty.Value),
                    ("referenceDesignators", ToDbNullable(referenceDesignators)));

                await InsertBomHistoryAsync(connection, mainItem["id"]!, mainRevision["id"]!, rows[0]["id"], "INSERT", "BOM line insert", rows[0], changedBy);
                await transaction.CommitAsync();
                return Results.Json(rows[0], statusCode: 201);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        app.MapDelete("/api/bom/lines/{lineId:int}", async (int lineId, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var changedBy = ReadString(payload, "changed_by") ?? "system";
            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    SELECT bl.*, main_item.pn AS main_pn, main_rev.revision AS main_rev, son_item.pn AS son_pn, COALESCE(son_rev.revision, '') AS son_rev
                    FROM item_bom_lines bl
                    JOIN items main_item ON main_item.id = bl.main_item_id
                    JOIN item_revisions main_rev ON main_rev.id = bl.main_item_revision_id
                    JOIN items son_item ON son_item.id = bl.son_item_id
                    LEFT JOIN item_revisions son_rev ON son_rev.id = bl.son_item_revision_id
                    WHERE bl.id = @id
                    LIMIT 1
                    """,
                    ("id", lineId));
                if (rows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("BOM line not found", 404);
                }

                await ExecuteAsync(connection, "DELETE FROM item_bom_lines WHERE id = @id", ("id", lineId));
                await InsertBomHistoryAsync(connection, rows[0]["main_item_id"]!, rows[0]["main_item_revision_id"]!, null, "DELETE", "BOM line deleted", rows[0], changedBy);
                await transaction.CommitAsync();
                return Results.Json(new { message = "BOM line deleted successfully" });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static void MapRouting(WebApplication app)
    {
        app.MapGet("/api/routing/lookup", async (HttpRequest request) =>
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

        app.MapGet("/api/routing/by-pn", async (HttpRequest request) =>
        {
            var pn = request.Query["pn"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(pn))
            {
                return JsonMessage("pn is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            var rows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn", ("pn", pn));
            return rows.Count == 0 ? JsonMessage("Part number not found", 404) : Results.Json(rows[0]);
        });

        app.MapGet("/api/routing/{itemId:int}/steps", async (int itemId, HttpRequest request) =>
        {
            var includeHistory = string.Equals(request.Query["includeHistory"], "true", StringComparison.OrdinalIgnoreCase);
            await using var connection = await OpenConnectionAsync();
            var payload = await GetRoutingPayloadAsync(connection, itemId, includeHistory);
            return payload is null
                ? JsonMessage("Part number not found", 404)
                : Results.Json(new { item = payload.Item, data = payload.Data, history = payload.History, total = payload.Data.Count });
        });

        app.MapPost("/api/routing/{itemId:int}/steps", async (int itemId, HttpContext context) => await SaveRoutingStepAsync(context, itemId, null));
        app.MapPut("/api/routing/steps/{stepId:int}", async (int stepId, HttpContext context) => await SaveRoutingStepAsync(context, null, stepId));

        app.MapPut("/api/routing/steps/{stepId:int}/move", async (int stepId, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var direction = ReadString(payload, "direction")?.Trim();
            var changedBy = ReadString(payload, "changed_by") ?? "system";
            if (direction is not ("up" or "down"))
            {
                return JsonMessage("direction must be up or down", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var currentRows = await QueryRowsAsync(connection, "SELECT * FROM item_routing_steps WHERE id = @id", ("id", stepId));
                if (currentRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Routing step not found", 404);
                }

                var current = currentRows[0];
                var targetRows = await QueryRowsAsync(
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
                    return JsonMessage($"Cannot move {direction}", 400);
                }

                var target = targetRows[0];
                var tempOrder = -DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await ExecuteAsync(connection, "UPDATE item_routing_steps SET station_order = @order WHERE id = @id", ("order", tempOrder), ("id", current["id"]));
                await ExecuteAsync(connection, "UPDATE item_routing_steps SET station_order = @order WHERE id = @id", ("order", current["station_order"]), ("id", target["id"]));
                await ExecuteAsync(connection, "UPDATE item_routing_steps SET station_order = @order WHERE id = @id", ("order", target["station_order"]), ("id", current["id"]));
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
            var payload = await ReadJsonBodyAsync(context.Request);
            var changedBy = ReadString(payload, "changed_by") ?? "system";
            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var rows = await QueryRowsAsync(connection, "SELECT * FROM item_routing_steps WHERE id = @id", ("id", stepId));
                if (rows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Routing step not found", 404);
                }

                await ExecuteAsync(connection, "DELETE FROM item_routing_steps WHERE id = @id", ("id", stepId));
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

    private static void MapWorkflow(WebApplication app)
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
                  w.due_date,
                  w.qty AS quantity,
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
                  COALESCE(w.site_name, '') AS site,
                  COALESCE(w.updated_at, p.updated_at) AS updated_at,
                  COUNT(*) OVER () AS total_count
                FROM workflow_part_numbers p
                LEFT JOIN workflow_work_orders w ON w.workflow_part_id = p.id
                LEFT JOIN sn_types st ON st.id = p.sn_type_id
                {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
                ORDER BY COALESCE(w.updated_at, p.updated_at) DESC, w.id DESC NULLS LAST, p.id DESC
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

    private static void MapWorkOrders(WebApplication app)
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

    private static void MapGenerateSn(WebApplication app)
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

    private static void MapTraceability(WebApplication app)
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
    }

    private static void MapPacking(WebApplication app)
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
                    string.Equals(serial["serial_status"]?.ToString(), "Failed", StringComparison.OrdinalIgnoreCase))
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

    private static void MapAssembly(WebApplication app)
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

    private static async Task<IResult> SaveItemAsync(HttpContext context, int? itemId)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        var pn = ReadString(payload, "pn")?.Trim();
        var description = ReadString(payload, "description")?.Trim();
        var marketingDesc = ReadString(payload, "marketing_desc")?.Trim();
        var phantom = ReadBool(payload, "phantom");
        var sgdControl = ReadBool(payload, "sgd_control");
        var itemType = ReadString(payload, "item_type")?.Trim();
        var productLineId = ReadInt(payload, "product_line_id");
        var snTypeId = ReadInt(payload, "sn_type_id");
        var snTypeName = ReadString(payload, "sn_type_name")?.Trim();
        var pnTypeId = ReadInt(payload, "pn_type_id");
        var changedBy = ReadString(payload, "changed_by") ?? "system";

        if (string.IsNullOrWhiteSpace(pn))
        {
            return JsonMessage("Part number is required", 400);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return JsonMessage("Description is required", 400);
        }

        if (phantom is null)
        {
            return JsonMessage("Phantom selection is required", 400);
        }

        if (sgdControl is null)
        {
            return JsonMessage("SGD control must be true or false", 400);
        }

        if (string.IsNullOrWhiteSpace(itemType) || !AllowedItemTypes.Contains(itemType))
        {
            return JsonMessage("Invalid item type", 400);
        }

        if (productLineId is null)
        {
            return JsonMessage("Product line is required", 400);
        }

        if (pnTypeId is null)
        {
            return JsonMessage("PN type is required", 400);
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            if (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM product_lines WHERE id = @id", ("id", productLineId.Value)) == 0)
            {
                await transaction.RollbackAsync();
                return JsonMessage("Product line not found", 400);
            }

            if (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM pn_types WHERE id = @id", ("id", pnTypeId.Value)) == 0)
            {
                await transaction.RollbackAsync();
                return JsonMessage("PN type not found", 400);
            }

            int? resolvedSnTypeId = null;
            if (snTypeId is not null)
            {
                if (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", snTypeId.Value)) == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("SN type not found", 400);
                }

                resolvedSnTypeId = snTypeId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(snTypeName))
            {
                resolvedSnTypeId = await ScalarAsync<int?>(connection, "SELECT id FROM sn_types WHERE sn_type_name = @name", ("name", snTypeName));
                if (resolvedSnTypeId is null)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("SN type not found", 400);
                }
            }

            List<Dictionary<string, object?>> rows;
            if (itemId is null)
            {
                rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO items
                      (pn, description, marketing_desc, phantom, sgd_control, item_type, product_line_id, sn_type_id, pn_type_id)
                    VALUES
                      (@pn, @description, @marketingDesc, @phantom, @sgdControl, @itemType, @productLineId, @snTypeId, @pnTypeId)
                    RETURNING *
                    """,
                    ("pn", pn),
                    ("description", description),
                    ("marketingDesc", ToDbNullable(marketingDesc)),
                    ("phantom", phantom.Value),
                    ("sgdControl", sgdControl.Value),
                    ("itemType", itemType),
                    ("productLineId", productLineId.Value),
                    ("snTypeId", ToDbNullable(resolvedSnTypeId)),
                    ("pnTypeId", pnTypeId.Value));
            }
            else
            {
                if (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM items WHERE id = @id", ("id", itemId.Value)) == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Part number not found", 404);
                }

                rows = await QueryRowsAsync(
                    connection,
                    """
                    UPDATE items
                    SET pn = @pn,
                        description = @description,
                        marketing_desc = @marketingDesc,
                        phantom = @phantom,
                        sgd_control = @sgdControl,
                        item_type = @itemType,
                        product_line_id = @productLineId,
                        sn_type_id = @snTypeId,
                        pn_type_id = @pnTypeId,
                        updated_at = NOW()
                    WHERE id = @id
                    RETURNING *
                    """,
                    ("pn", pn),
                    ("description", description),
                    ("marketingDesc", ToDbNullable(marketingDesc)),
                    ("phantom", phantom.Value),
                    ("sgdControl", sgdControl.Value),
                    ("itemType", itemType),
                    ("productLineId", productLineId.Value),
                    ("snTypeId", ToDbNullable(resolvedSnTypeId)),
                    ("pnTypeId", pnTypeId.Value),
                    ("id", itemId.Value));
            }

            await InsertJsonHistoryAsync(connection, "item_history", "item_id", rows[0]["id"]!, itemId is null ? "CREATE" : "UPDATE", rows[0], changedBy);
            await transaction.CommitAsync();
            return Results.Json(rows[0], statusCode: itemId is null ? 201 : 200);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync();
            return JsonMessage("Part number already exists", 409);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<IResult> SaveRoutingStepAsync(HttpContext context, int? itemId, int? stepId)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        var stationOrder = ReadInt(payload, "station_order");
        var stationCode = ReadString(payload, "station_code")?.Trim();
        var sampleMode = ReadString(payload, "sample_mode")?.Trim();
        var reportMode = ReadString(payload, "report_mode")?.Trim();
        var stationLoginId = ReadString(payload, "station_login_id")?.Trim();
        var stationLoginPassword = ReadString(payload, "station_login_password")?.Trim();
        var stationIp = ReadString(payload, "station_ip")?.Trim();
        var printerIp = ReadString(payload, "printer_ip")?.Trim();
        var changedBy = ReadString(payload, "changed_by") ?? "system";

        if (string.IsNullOrWhiteSpace(stationCode))
        {
            return JsonMessage("Station is required", 400);
        }

        if (sampleMode is not ("Full" or "Sample"))
        {
            return JsonMessage("Invalid sample mode", 400);
        }

        if (reportMode is not ("Regular" or "Auto Only"))
        {
            return JsonMessage("Invalid report mode", 400);
        }

        if (string.IsNullOrWhiteSpace(stationLoginId) || string.IsNullOrWhiteSpace(stationLoginPassword))
        {
            return JsonMessage("Station login ID and password are required", 400);
        }

        await using var connection = await OpenConnectionAsync();
        await EnsureRoutingStepLoginColumnsAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            Dictionary<string, object?>? existing = null;
            if (stepId is not null)
            {
                var existingRows = await QueryRowsAsync(connection, "SELECT * FROM item_routing_steps WHERE id = @id", ("id", stepId.Value));
                if (existingRows.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage("Routing step not found", 404);
                }

                existing = existingRows[0];
                itemId = Convert.ToInt32(existing["item_id"]);
            }

            var item = await FindItemByIdAsync(connection, itemId!.Value);
            if (item is null)
            {
                await transaction.RollbackAsync();
                return JsonMessage("Part number not found", 404);
            }

            var stationRows = await QueryRowsAsync(
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
                return JsonMessage("Station not found in Stations master", 400);
            }

            if (stationOrder is null or <= 0)
            {
                stationOrder = await ScalarAsync<int>(connection, "SELECT COALESCE(MAX(station_order), 0) + 10 FROM item_routing_steps WHERE item_id = @itemId", ("itemId", itemId.Value));
            }

            if (!string.IsNullOrWhiteSpace(stationLoginId))
            {
                var duplicateLoginRows = await QueryRowsAsync(
                    connection,
                    """
                    SELECT station_code
                    FROM item_routing_steps
                    WHERE item_id = @itemId
                      AND UPPER(station_login_id) = UPPER(@stationLoginId)
                      AND (@stepId IS NULL OR id <> @stepId)
                    LIMIT 1
                    """,
                    ("itemId", itemId.Value),
                    ("stationLoginId", stationLoginId),
                    ("stepId", stepId));

                if (duplicateLoginRows.Count > 0)
                {
                    await transaction.RollbackAsync();
                    return JsonMessage($"Station login ID is already used for station {duplicateLoginRows[0]["station_code"]}", 400);
                }
            }

            try
            {
                List<Dictionary<string, object?>> rows;
                if (stepId is null)
                {
                    rows = await QueryRowsAsync(
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

                rows = await QueryRowsAsync(
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
                return JsonMessage("Station order already exists for this PN", 409);
            }
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

    private static async Task<Dictionary<string, object?>?> GetSerialByQueryAsync(NpgsqlConnection connection, string query)
    {
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT snr.id, snr.sn, snr.rsn, snr.status AS serial_status, snr.condition,
                   snr.current_station_code, snr.current_station_order, snr.last_moved_at,
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
        return matched is not null ? Convert.ToInt32(matched["station_order"]) : Convert.ToInt32(routeRows[0]["station_order"]);
    }

    private static async Task<object> BuildTracePayloadAsync(NpgsqlConnection connection, string query, Dictionary<string, object?> serial)
    {
        var routeRows = await GetRouteRowsForItemAsync(connection, Convert.ToInt32(serial["item_id"]));
        var currentOrder = routeRows.Count == 0 ? 0 : ResolveCurrentOrder(serial, routeRows);
        var history = await QueryRowsAsync(
            connection,
            """
            SELECT id, changed_by AS user_name, created_at AS date_time, station_code AS station,
                   station_length AS length, pc_name, action_result AS result,
                   COALESCE(additional_info, remark, '') AS additional_info
            FROM serial_station_logs
            WHERE serial_id = @serialId
              AND UPPER(action_result) IN ('PASS', 'FAIL')
            ORDER BY created_at DESC, id DESC
            LIMIT 300
            """,
            ("serialId", serial["id"]));
        history.Add(BuildSerialGeneratedHistoryRow(serial));

        var routing = routeRows.Select(step =>
        {
            var order = Convert.ToInt32(step["station_order"]);
            var state = currentOrder == 0 ? "pending" : order < currentOrder ? "completed" : order == currentOrder ? "current" : "pending";
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
        var history = await QueryRowsAsync(
            connection,
            """
            SELECT id, changed_by AS user_name, created_at AS date_time, station_code AS station,
                   NULL::text AS length, NULL::text AS pc_name, action_result AS result,
                   COALESCE(remark, '') AS additional_info
            FROM workflow_serial_station_logs
            WHERE workflow_serial_id = @serialId
              AND UPPER(action_result) IN ('PASS', 'FAIL')
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
            generated_at = DateTime.UtcNow
        };
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
                    return JsonMessage("this Sn type is already assigned in this father-child family", 409);
                }
            }

            var partRows = await QueryRowsAsync(
                connection,
                """
                INSERT INTO workflow_part_numbers
                  (pn, description, sgd_control, item_type, sn_type_id, sn_type_name, pn_type_id, box_qty)
                VALUES
                  (@pn, @description, @sgdControl, @itemType, @snTypeId, @snTypeName, @pnTypeId, @boxQty)
                ON CONFLICT (pn) DO UPDATE
                SET description = EXCLUDED.description,
                    sgd_control = EXCLUDED.sgd_control,
                    item_type = EXCLUDED.item_type,
                    sn_type_id = EXCLUDED.sn_type_id,
                    sn_type_name = EXCLUDED.sn_type_name,
                    pn_type_id = EXCLUDED.pn_type_id,
                    box_qty = EXCLUDED.box_qty,
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
                ("boxQty", ToDbNullable(boxQty)));

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
                await ExecuteAsync(connection, "DELETE FROM workflow_bom_children WHERE workflow_part_id = @workflowPartId", ("workflowPartId", workflowPartId));
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

                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO workflow_bom_children
                          (workflow_part_id, son_pn, son_description, station_code, station_name, item_type, pn_type, qty)
                        VALUES
                          (@workflowPartId, @sonPn, @sonDescription, @stationCode, @stationName, @itemType, @pnType, @qty)
                        """,
                        ("workflowPartId", workflowPartId),
                        ("sonPn", sonPn),
                        ("sonDescription", ReadString(row, "son_description")?.Trim() ?? sonPn),
                        ("stationCode", ToDbNullable(ReadString(row, "station_code")?.Trim())),
                        ("stationName", ToDbNullable(ReadString(row, "station_name")?.Trim())),
                        ("itemType", bomItemType),
                        ("pnType", ToDbNullable(ReadString(row, "pn_type")?.Trim())),
                        ("qty", ReadInt(row, "qty") ?? 1));
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
            var snapshot = await GetWorkflowSnapshotAsync(connection, pn);
            return Results.Json(snapshot ?? new { message = "Workflow saved" });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

    private static async Task<IResult> SaveSnTypeFieldAsync(HttpContext context, int? snTypeId, int? fieldId)
    {
        var payload = await ReadJsonBodyAsync(context.Request);

        await using var connection = await OpenConnectionAsync();
        Dictionary<string, object?>? existingField = null;
        var resolvedSnTypeId = snTypeId;

        if (fieldId is null)
        {
            if (snTypeId is null ||
                await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM sn_types WHERE id = @id", ("id", snTypeId.Value)) == 0)
            {
                return JsonMessage("SN Type not found", 404);
            }
        }
        else
        {
            var existingRows = await QueryRowsAsync(connection, "SELECT * FROM sn_type_fields WHERE id = @id", ("id", fieldId.Value));
            if (existingRows.Count == 0)
            {
                return JsonMessage("Field not found", 404);
            }

            existingField = existingRows[0];
            resolvedSnTypeId = Convert.ToInt32(existingField["sn_type_id"]);
        }

        var (field, validationError) = NormalizeSnTypeFieldPayload(payload, existingField);
        if (validationError is not null)
        {
            return validationError;
        }

        if (SequenceCounterTypes.Contains(field!.FieldType) &&
            await HasSequenceCounterConflictAsync(connection, resolvedSnTypeId!.Value, fieldId))
        {
            return JsonMessage("Only one Sequence counter field is allowed per SN Type", 400);
        }

        if (EpvFieldTypes.Contains(field.FieldType))
        {
            var epvMappingError = await ValidateEpvMappingAsync(connection, field.EpvTypeId!.Value, field.EpvSubTypeId!.Value);
            if (epvMappingError is not null)
            {
                return JsonMessage(epvMappingError, 400);
            }
        }

        try
        {
            List<Dictionary<string, object?>> rows;
            if (fieldId is null)
            {
                rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO sn_type_fields (sn_type_id, sort_order, field_type, field_string, field_size, epv_type_id, epv_sub_type_id)
                    VALUES (@snTypeId, @sortOrder, @fieldType, @fieldString, @fieldSize, @epvTypeId, @epvSubTypeId)
                    RETURNING *
                    """,
                    ("snTypeId", resolvedSnTypeId!.Value),
                    ("sortOrder", field.SortOrder),
                    ("fieldType", field.FieldType),
                    ("fieldString", ToDbNullable(field.FieldString)),
                    ("fieldSize", ToDbNullable(field.FieldSize)),
                    ("epvTypeId", ToDbNullable(field.EpvTypeId)),
                    ("epvSubTypeId", ToDbNullable(field.EpvSubTypeId)));
                return Results.Json(rows[0], statusCode: 201);
            }

            rows = await QueryRowsAsync(
                connection,
                """
                UPDATE sn_type_fields
                SET sort_order = @sortOrder,
                    field_type = @fieldType,
                    field_string = @fieldString,
                    field_size = @fieldSize,
                    epv_type_id = @epvTypeId,
                    epv_sub_type_id = @epvSubTypeId,
                    updated_at = NOW()
                WHERE id = @fieldId
                RETURNING *
                """,
                ("sortOrder", field.SortOrder),
                ("fieldType", field.FieldType),
                ("fieldString", ToDbNullable(field.FieldString)),
                ("fieldSize", ToDbNullable(field.FieldSize)),
                ("epvTypeId", ToDbNullable(field.EpvTypeId)),
                ("epvSubTypeId", ToDbNullable(field.EpvSubTypeId)),
                ("fieldId", fieldId.Value));
            return Results.Json(rows[0]);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return JsonMessage("Sort order already exists for this SN Type", 400);
        }
    }

    private static (NormalizedSnTypeField? Field, IResult? Error) NormalizeSnTypeFieldPayload(
        JsonNode? payload,
        Dictionary<string, object?>? existingField)
    {
        var sortOrder = ReadDecimalFromPayloadOrExisting(payload, "sort_order", existingField);
        if (sortOrder is null)
        {
            return (null, JsonMessage("Sort order is required", 400));
        }

        if (sortOrder.Value <= 0)
        {
            return (null, JsonMessage("Sort order must be a positive number", 400));
        }

        var fieldType = ReadStringFromPayloadOrExisting(payload, "field_type", existingField)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fieldType) || !AllowedSnFieldTypes.ContainsKey(fieldType))
        {
            return (null, Results.Json(new
            {
                message = "Invalid field type",
                allowedTypes = AllowedSnFieldTypes.Keys.ToArray()
            }, statusCode: 400));
        }

        var fieldStringInput = ReadStringFromPayloadOrExisting(payload, "field_string", existingField);
        var fieldSizeInput = ReadIntFromPayloadOrExisting(payload, "field_size", existingField);
        var epvTypeIdInput = ReadIntFromPayloadOrExisting(payload, "epv_type_id", existingField);
        var epvSubTypeIdInput = ReadIntFromPayloadOrExisting(payload, "epv_sub_type_id", existingField);

        string? fieldString = null;
        if (StringValueTypes.Contains(fieldType))
        {
            fieldString = fieldStringInput?.Trim();
            if (string.IsNullOrWhiteSpace(fieldString))
            {
                return (null, JsonMessage("String value is required for the selected field type", 400));
            }
        }

        int? fieldSize = null;
        if (AllCounterTypes.Contains(fieldType))
        {
            if (fieldSizeInput is null || fieldSizeInput.Value < 1 || fieldSizeInput.Value > 8)
            {
                return (null, JsonMessage("Field size for sequence types must be an integer between 1 and 8", 400));
            }

            fieldSize = fieldSizeInput.Value;
        }

        int? epvTypeId = null;
        int? epvSubTypeId = null;
        if (EpvFieldTypes.Contains(fieldType))
        {
            if (epvTypeIdInput is null || epvTypeIdInput.Value <= 0)
            {
                return (null, JsonMessage("EPV type is required for selected field type", 400));
            }

            if (epvSubTypeIdInput is null || epvSubTypeIdInput.Value <= 0)
            {
                return (null, JsonMessage("EPV sub-type is required for selected field type", 400));
            }

            epvTypeId = epvTypeIdInput.Value;
            epvSubTypeId = epvSubTypeIdInput.Value;
        }

        return (new NormalizedSnTypeField(sortOrder.Value, fieldType, fieldString, fieldSize, epvTypeId, epvSubTypeId), null);
    }

    private static async Task<bool> HasSequenceCounterConflictAsync(NpgsqlConnection connection, int snTypeId, int? excludeFieldId)
    {
        var sql = excludeFieldId is null
            ? """
              SELECT COUNT(*)::int
              FROM sn_type_fields
              WHERE sn_type_id = @snTypeId
                AND field_type = ANY(@counterTypes)
              """
            : """
              SELECT COUNT(*)::int
              FROM sn_type_fields
              WHERE sn_type_id = @snTypeId
                AND field_type = ANY(@counterTypes)
                AND id <> @fieldId
              """;

        var count = excludeFieldId is null
            ? await ScalarAsync<int>(connection, sql, ("snTypeId", snTypeId), ("counterTypes", SequenceCounterTypes.ToArray()))
            : await ScalarAsync<int>(connection, sql, ("snTypeId", snTypeId), ("counterTypes", SequenceCounterTypes.ToArray()), ("fieldId", excludeFieldId.Value));

        return count > 0;
    }

    private static async Task<string?> ValidateEpvMappingAsync(NpgsqlConnection connection, int epvTypeId, int epvSubTypeId)
    {
        var typeRows = await QueryRowsAsync(connection, "SELECT id, type_name FROM epv_types WHERE id = @id", ("id", epvTypeId));
        if (typeRows.Count == 0)
        {
            return "EPV type not found";
        }

        var subTypeRows = await QueryRowsAsync(
            connection,
            "SELECT id, epv_type_id, sub_type_name FROM epv_sub_types WHERE id = @id",
            ("id", epvSubTypeId));
        if (subTypeRows.Count == 0)
        {
            return "EPV sub-type not found";
        }

        return Convert.ToInt32(subTypeRows[0]["epv_type_id"]) == epvTypeId
            ? null
            : "Selected sub-type does not belong to selected EPV type";
    }

    private static async Task<IResult> SaveSgdPoAsync(HttpContext context, int? id)
    {
        var payload = await ReadJsonBodyAsync(context.Request);
        var po = ReadString(payload, "po")?.Trim();
        var status = ReadString(payload, "status")?.Trim() ?? "open";
        var swVersion = ReadString(payload, "sw_version")?.Trim();
        var hwVersion = ReadString(payload, "hw_version")?.Trim();
        var itemId = ReadInt(payload, "item_id");
        var poQty = ReadInt(payload, "po_qty");

        if (string.IsNullOrWhiteSpace(po) || itemId is null || poQty is null || poQty.Value <= 0)
        {
            return JsonMessage("po, item_id, and positive po_qty are required", 400);
        }

        await using var connection = await OpenConnectionAsync();
        try
        {
            var rows = id is null
                ? await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO sgd_pos (po, status, sw_version, hw_version, item_id, po_qty)
                    VALUES (@po, @status, @swVersion, @hwVersion, @itemId, @poQty)
                    RETURNING *
                    """,
                    ("po", po),
                    ("status", status),
                    ("swVersion", ToDbNullable(swVersion)),
                    ("hwVersion", ToDbNullable(hwVersion)),
                    ("itemId", itemId.Value),
                    ("poQty", poQty.Value))
                : await QueryRowsAsync(
                    connection,
                    """
                    UPDATE sgd_pos
                    SET po = @po,
                        status = @status,
                        sw_version = @swVersion,
                        hw_version = @hwVersion,
                        item_id = @itemId,
                        po_qty = @poQty,
                        updated_at = NOW()
                    WHERE id = @id
                    RETURNING *
                    """,
                    ("po", po),
                    ("status", status),
                    ("swVersion", ToDbNullable(swVersion)),
                    ("hwVersion", ToDbNullable(hwVersion)),
                    ("itemId", itemId.Value),
                    ("poQty", poQty.Value),
                    ("id", id.Value));

            return rows.Count == 0 ? JsonMessage("SGD PO not found", 404) : Results.Json(rows[0], statusCode: id is null ? 201 : 200);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return JsonMessage("SGD PO already exists", 409);
        }
    }

    private static async Task<object?> GetWorkflowSnapshotAsync(NpgsqlConnection connection, string pn, string? wo = null)
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
            var workOrderRows = await QueryRowsAsync(
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
                ("wo", string.IsNullOrWhiteSpace(wo) ? string.Empty : wo.Trim()));

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
                SELECT id, son_pn, son_description, station_code, station_name, item_type, pn_type, qty
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
                    pn_type_id = part["pn_type_id"]
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
        var existingWorkOrderRows = await QueryRowsAsync(
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
            ("wo", string.IsNullOrWhiteSpace(wo) ? string.Empty : wo.Trim()));

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

    private sealed record BomPayload(
        Dictionary<string, object?> Item,
        Dictionary<string, object?>? Revision,
        List<Dictionary<string, object?>> Data,
        List<Dictionary<string, object?>> History);

    private sealed record RoutingPayload(
        Dictionary<string, object?> Item,
        List<Dictionary<string, object?>> Data,
        List<Dictionary<string, object?>> History);

    private static async Task<BomPayload?> GetBomPayloadAsync(NpgsqlConnection connection, string pn, string revision, bool includeHistory)
    {
        var mainItem = await FindItemByPnAsync(connection, pn);
        if (mainItem is null)
        {
            return null;
        }

        var mainRevision = await FindItemRevisionAsync(connection, Convert.ToInt32(mainItem["id"]), revision);
        if (mainRevision is null)
        {
            return new BomPayload(mainItem, null, new List<Dictionary<string, object?>>(), new List<Dictionary<string, object?>>());
        }

        var data = await QueryRowsAsync(
            connection,
            """
            SELECT bl.id, bl.main_item_id, bl.main_item_revision_id, bl.son_item_id, bl.son_item_revision_id,
                   son.pn AS son_pn, son.description AS son_description, COALESCE(sr.revision, '') AS son_rev,
                   son.item_type AS son_item_type, COALESCE(pt.code, '') AS son_pn_type,
                   bl.qty AS son_qty, COALESCE(bl.reference_designators, '') AS reference_designators,
                   bl.created_at, bl.updated_at
            FROM item_bom_lines bl
            JOIN items son ON son.id = bl.son_item_id
            LEFT JOIN item_revisions sr ON sr.id = bl.son_item_revision_id
            LEFT JOIN pn_types pt ON pt.id = son.pn_type_id
            WHERE bl.main_item_revision_id = @revisionId
            ORDER BY bl.id ASC
            """,
            ("revisionId", mainRevision["id"]));

        var history = includeHistory
            ? await QueryRowsAsync(
                connection,
                """
                SELECT id, main_item_id, main_item_revision_id, bom_line_id, action, description, change_data, changed_by, changed_at
                FROM item_bom_history
                WHERE main_item_revision_id = @revisionId
                ORDER BY changed_at DESC, id DESC
                LIMIT 300
                """,
                ("revisionId", mainRevision["id"]))
            : new List<Dictionary<string, object?>>();

        return new BomPayload(mainItem, mainRevision, data, history);
    }

    private static async Task<RoutingPayload?> GetRoutingPayloadAsync(NpgsqlConnection connection, int itemId, bool includeHistory)
    {
        await EnsureRoutingStepLoginColumnsAsync(connection);

        var item = await FindItemByIdAsync(connection, itemId);
        if (item is null)
        {
            return null;
        }

        var data = await QueryRowsAsync(
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
            ? await QueryRowsAsync(
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

    private static async Task<Dictionary<string, object?>?> FindItemByPnAsync(NpgsqlConnection connection, string pn)
    {
        var rows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE pn = @pn LIMIT 1", ("pn", pn));
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<Dictionary<string, object?>?> FindItemByIdAsync(NpgsqlConnection connection, int itemId)
    {
        var rows = await QueryRowsAsync(connection, "SELECT id, pn, description FROM items WHERE id = @id LIMIT 1", ("id", itemId));
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

    private static async Task InsertBomHistoryAsync(
        NpgsqlConnection connection,
        object mainItemId,
        object mainRevisionId,
        object? bomLineId,
        string action,
        string description,
        object changeData,
        string changedBy)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO item_bom_history
              (main_item_id, main_item_revision_id, bom_line_id, action, description, change_data, changed_by)
            VALUES
              (@mainItemId, @mainRevisionId, @bomLineId, @action, @description, @changeData, @changedBy)
            """,
            connection);
        command.Parameters.AddWithValue("mainItemId", mainItemId);
        command.Parameters.AddWithValue("mainRevisionId", mainRevisionId);
        command.Parameters.AddWithValue("bomLineId", bomLineId ?? DBNull.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.Add("changeData", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(changeData);
        command.Parameters.AddWithValue("changedBy", changedBy);
        await command.ExecuteNonQueryAsync();
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
        await ExecuteAsync(
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

    private static async Task<List<Dictionary<string, object?>>> GetSnTypeFieldsAsync(NpgsqlConnection connection, int snTypeId)
    {
        return await QueryRowsAsync(
            connection,
            """
            SELECT f.id, f.sn_type_id, f.sort_order, f.field_type, f.field_string, f.field_size,
                   f.epv_type_id, et.type_name AS epv_type_name,
                   f.epv_sub_type_id, est.sub_type_name AS epv_sub_type_name,
                   f.created_at, f.updated_at
            FROM sn_type_fields f
            LEFT JOIN epv_types et ON et.id = f.epv_type_id
            LEFT JOIN epv_sub_types est ON est.id = f.epv_sub_type_id
            WHERE f.sn_type_id = @snTypeId
            ORDER BY f.sort_order ASC, f.id ASC
            """,
            ("snTypeId", snTypeId));
    }

    private static List<Dictionary<string, object?>> MapStations(List<Dictionary<string, object?>> rows)
    {
        foreach (var row in rows)
        {
            row.Remove("total_count");
            row["status"] = "Active";
        }

        return rows;
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
        var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private static void MapLabels(WebApplication app)
    {
        app.MapGet("/api/labels", async () =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT
                  lm.id,
                  lm.label_code,
                  lm.label_description,
                  lm.status,
                  lm.created_at,
                  lm.updated_at,
                  latest.prn_template_id,
                  latest.version AS prn_version
                FROM label_masters lm
                JOIN LATERAL (
                  SELECT id AS prn_template_id, version
                  FROM label_prn_templates
                  WHERE label_master_id = lm.id
                    AND COALESCE(NULLIF(TRIM(prn_content), ''), '') <> ''
                  ORDER BY version DESC, id DESC
                  LIMIT 1
                ) latest ON TRUE
                WHERE lm.status = 'Active'
                ORDER BY lm.updated_at DESC, lm.id DESC
                """);
            return Results.Json(new { data = rows });
        });

        app.MapGet("/api/labels/{id:int}", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            var rows = await QueryRowsAsync(
                connection,
                """
                SELECT id, label_code, label_description, status, created_at, updated_at
                FROM label_masters
                WHERE id = @id
                """,
                ("id", id));
            if (rows.Count == 0)
            {
                return JsonError("Label not found", 404);
            }

            var template = await GetLatestLabelTemplateAsync(connection, id);
            return Results.Json(new { label = rows[0], prn_template = template });
        });

        app.MapPost("/api/labels", async (HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var code = ReadLabelPayloadString(payload, "label_code", "labelCode")?.Trim().ToUpperInvariant();
            var description = ReadLabelPayloadString(payload, "label_description", "labelDescription")?.Trim() ?? string.Empty;
            var status = ReadLabelPayloadString(payload, "status")?.Trim();
            status = string.IsNullOrWhiteSpace(status) ? "Active" : status;
            status = status.Equals("Inactive", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";

            if (string.IsNullOrWhiteSpace(code))
            {
                return JsonError("Label Code is required", 400);
            }

            if (!Regex.IsMatch(code, "^[A-Z0-9-]+$"))
            {
                return JsonError("Use alphanumeric characters and hyphen only for Label Code", 400);
            }

            if (!IsValidLabelStatus(status))
            {
                return JsonError("Invalid label status", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    INSERT INTO label_masters (label_code, label_description, status)
                    VALUES (@code, @description, @status)
                    RETURNING id, label_code, label_description, status, created_at, updated_at
                    """,
                    ("code", code),
                    ("description", description),
                    ("status", status));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonError("Label Code already exists", 409);
            }
        });

        app.MapPut("/api/labels/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var code = ReadLabelPayloadString(payload, "label_code", "labelCode")?.Trim().ToUpperInvariant();
            var description = ReadLabelPayloadString(payload, "label_description", "labelDescription")?.Trim() ?? string.Empty;
            var status = ReadLabelPayloadString(payload, "status")?.Trim();
            status = string.IsNullOrWhiteSpace(status) ? "Active" : status;
            status = status.Equals("Inactive", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";

            if (string.IsNullOrWhiteSpace(code))
            {
                return JsonError("Label Code is required", 400);
            }

            if (!Regex.IsMatch(code, "^[A-Z0-9-]+$"))
            {
                return JsonError("Use alphanumeric characters and hyphen only for Label Code", 400);
            }

            if (!IsValidLabelStatus(status))
            {
                return JsonError("Invalid label status", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            try
            {
                var rows = await QueryRowsAsync(
                    connection,
                    """
                    UPDATE label_masters
                    SET label_code = @code,
                        label_description = @description,
                        status = @status,
                        updated_at = NOW()
                    WHERE id = @id
                    RETURNING id, label_code, label_description, status, created_at, updated_at
                    """,
                    ("id", id),
                    ("code", code),
                    ("description", description),
                    ("status", status));
                return rows.Count == 0 ? JsonError("Label not found", 404) : Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return JsonError("Label Code already exists", 409);
            }
        });

        app.MapPost("/api/labels/{id:int}/prn-template", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var prnFileName = ReadLabelPayloadString(payload, "prn_file_name", "prnFileName")?.Trim() ?? string.Empty;
            var prnContent = ReadLabelPayloadString(payload, "prn_content", "prnContent") ?? string.Empty;
            var previewData = ReadLabelPayloadString(payload, "preview_data", "previewData");

            if (string.IsNullOrWhiteSpace(prnContent))
            {
                return JsonError("PRN content is required", 400);
            }

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            if (!await LabelExistsAsync(connection, id))
            {
                return JsonError("Label not found", 404);
            }

            var nextVersion = (await ScalarAsync<int?>(
                connection,
                "SELECT COALESCE(MAX(version), 0) + 1 FROM label_prn_templates WHERE label_master_id = @id",
                ("id", id))) ?? 1;

            var rows = await QueryRowsAsync(
                connection,
                """
                INSERT INTO label_prn_templates (label_master_id, prn_file_name, prn_content, preview_data, version)
                VALUES (@labelMasterId, @prnFileName, @prnContent, @previewData, @version)
                RETURNING id, label_master_id, prn_file_name, prn_content, preview_data, version, created_at, updated_at
                """,
                ("labelMasterId", id),
                ("prnFileName", prnFileName),
                ("prnContent", prnContent),
                ("previewData", previewData),
                ("version", nextVersion));
            await ExecuteAsync(connection, "UPDATE label_masters SET status = 'Active', updated_at = NOW() WHERE id = @id", ("id", id));
            return Results.Json(rows[0], statusCode: 201);
        });

        app.MapGet("/api/labels/{id:int}/prn-template", async (int id) =>
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            if (!await LabelExistsAsync(connection, id))
            {
                return JsonError("Label not found", 404);
            }

            var template = await GetLatestLabelTemplateAsync(connection, id);
            return template is null ? JsonError("PRN template not found", 404) : Results.Json(template);
        });

        app.MapPost("/api/labels/{id:int}/generate", async (int id, HttpContext context) =>
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            var rsn = ReadLabelPayloadString(payload, "rsn", "RSN")?.Trim();
            var previewData = ReadLabelPayloadString(payload, "preview_data", "previewData");

            await using var connection = await OpenConnectionAsync();
            await EnsureLabelsSchemaAsync(connection);
            if (!await LabelExistsAsync(connection, id))
            {
                return JsonError("Label not found", 404);
            }

            var template = await GetLatestLabelTemplateAsync(connection, id);
            if (template is null)
            {
                return JsonError("PRN template not found", 404);
            }

            var rows = await QueryRowsAsync(
                connection,
                """
                INSERT INTO label_generation_history (label_master_id, label_prn_template_id, rsn, preview_data)
                VALUES (@labelMasterId, @templateId, @rsn, @previewData)
                RETURNING id, label_master_id, label_prn_template_id, rsn, preview_data, created_at
                """,
                ("labelMasterId", id),
                ("templateId", template["id"]),
                ("rsn", string.IsNullOrWhiteSpace(rsn) ? null : rsn),
                ("previewData", previewData));
            return Results.Json(rows[0], statusCode: 201);
        });
    }

    private static async Task EnsureLabelsSchemaAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS label_masters (
              id BIGSERIAL PRIMARY KEY,
              label_code VARCHAR(80) NOT NULL,
              label_description TEXT NOT NULL DEFAULT '',
              status VARCHAR(20) NOT NULL DEFAULT 'Active' CHECK (status IN ('Active', 'Inactive')),
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_label_masters_label_code UNIQUE (label_code)
            )
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS label_prn_templates (
              id BIGSERIAL PRIMARY KEY,
              label_master_id BIGINT NOT NULL REFERENCES label_masters(id) ON DELETE CASCADE,
              prn_file_name VARCHAR(255) NOT NULL DEFAULT '',
              prn_content TEXT NOT NULL,
              preview_data TEXT,
              version INT NOT NULL DEFAULT 1,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_label_prn_template_version UNIQUE (label_master_id, version)
            )
            """);
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS label_generation_history (
              id BIGSERIAL PRIMARY KEY,
              label_master_id BIGINT NOT NULL REFERENCES label_masters(id) ON DELETE CASCADE,
              label_prn_template_id BIGINT REFERENCES label_prn_templates(id) ON DELETE SET NULL,
              rsn VARCHAR(120),
              preview_data TEXT,
              created_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
    }

    private static async Task<bool> LabelExistsAsync(NpgsqlConnection connection, int id)
    {
        var count = await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM label_masters WHERE id = @id", ("id", id));
        return count > 0;
    }

    private static async Task<Dictionary<string, object?>?> GetLatestLabelTemplateAsync(NpgsqlConnection connection, int labelMasterId)
    {
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT id, label_master_id, prn_file_name, prn_content, preview_data, version, created_at, updated_at
            FROM label_prn_templates
            WHERE label_master_id = @labelMasterId
            ORDER BY version DESC, id DESC
            LIMIT 1
            """,
            ("labelMasterId", labelMasterId));
        return rows.Count == 0 ? null : rows[0];
    }

    private static string? ReadLabelPayloadString(JsonNode? payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadString(payload, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsValidLabelStatus(string value)
    {
        return value.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Inactive", StringComparison.OrdinalIgnoreCase);
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
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);

        await ExecuteAsync(connection, "ALTER TABLE public.workflow_part_numbers ADD COLUMN IF NOT EXISTS box_qty INTEGER CHECK (box_qty IS NULL OR box_qty > 0)");

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
              pn_type VARCHAR(80),
              qty INTEGER NOT NULL CHECK (qty > 0),
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);

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
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_serials_wo ON public.workflow_serial_numbers (workflow_work_order_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_serials_part ON public.workflow_serial_numbers (workflow_part_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_bom_bind_parent_station ON public.workflow_serial_bom_bindings (parent_workflow_serial_id, station_code)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_bom_bind_child ON public.workflow_serial_bom_bindings (child_workflow_serial_id)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_station_logs_serial ON public.workflow_serial_station_logs (workflow_serial_id, created_at DESC)");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_workflow_station_logs_station ON public.workflow_serial_station_logs (workflow_part_id, station_code)");
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
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT station_code, pn, wo
            FROM (
                SELECT r.station_code, p.pn, w.wo
                FROM workflow_station_logins l
                JOIN workflow_routing_steps r ON r.id = l.workflow_routing_step_id
                JOIN workflow_part_numbers p ON p.id = r.workflow_part_id
                LEFT JOIN workflow_work_orders w ON w.id = l.workflow_work_order_id
                WHERE UPPER(l.station_login_id) = UPPER(@loginId)
                  AND (@excludeLoginRowId IS NULL OR l.id <> @excludeLoginRowId)
            ) conflicts
            LIMIT 1
            """,
            ("loginId", loginId),
            ("excludeLoginRowId", ToDbNullable(excludeLoginRowId)),
            ("excludeRoutingStepId", ToDbNullable(excludeRoutingStepId)),
            ("excludeWorkflowPartId", ToDbNullable(excludeWorkflowPartId)));

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

    private static string GetConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("PGCONNECTIONSTRING");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var host = Environment.GetEnvironmentVariable("PGHOST") ?? "localhost";
        var user = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres";
        var database = Environment.GetEnvironmentVariable("PGDATABASE") ?? "MESDB";
        var password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? "";
        var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";

        return $"Host={host};Username={user};Password={password};Database={database};Port={port};Include Error Detail=true";
    }

    private static async Task<JsonNode?> ReadJsonBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or <= 0)
        {
            return null;
        }

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    private static async Task<List<Dictionary<string, object?>>> QueryRowsAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
        {
            return default;
        }

        return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static void AddParameters(NpgsqlCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static Dictionary<string, object?> ReadRow(NpgsqlDataReader reader)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var name = reader.GetName(index);
            if (reader.IsDBNull(index))
            {
                row[name] = null;
                continue;
            }

            var value = reader.GetValue(index);
            row[name] = value switch
            {
                DateTime dateTime => dateTime,
                string[] array => array,
                Array array => array.Cast<object?>().ToArray(),
                _ => value
            };
        }

        return row;
    }

    private static IResult JsonError(string error, int statusCode)
    {
        return Results.Json(new { error }, statusCode: statusCode);
    }

    private static IResult JsonMessage(string message, int statusCode)
    {
        return Results.Json(new { message }, statusCode: statusCode);
    }

    private static IResult? ValidateEpvValuesAgainstRegex(
        string[] values,
        Dictionary<string, object?> epvType,
        Dictionary<string, object?> epvSubType)
    {
        var typeName = epvType["type_name"]?.ToString() ?? "selected type";
        var subTypeName = epvSubType["sub_type_name"]?.ToString() ?? "selected sub-type";
        var typeRegexRule = epvType["regex_rule"]?.ToString() ?? string.Empty;
        var subTypeRegexRule = epvSubType["regex_rule"]?.ToString() ?? string.Empty;

        Regex typeRegex;
        try
        {
            typeRegex = new Regex(typeRegexRule);
        }
        catch (ArgumentException)
        {
            return JsonMessage($"EPV type regex is invalid for type {typeName}", 400);
        }

        Regex subTypeRegex;
        try
        {
            subTypeRegex = new Regex(subTypeRegexRule);
        }
        catch (ArgumentException)
        {
            return JsonMessage($"EPV sub-type regex is invalid for sub-type {subTypeName}", 400);
        }

        var valuesFailingTypeRegex = new List<string>();
        var valuesFailingSubTypeRegex = new List<string>();
        foreach (var value in values)
        {
            var normalizedValue = value.Trim();
            if (!typeRegex.IsMatch(normalizedValue))
            {
                valuesFailingTypeRegex.Add(normalizedValue);
            }

            if (!subTypeRegex.IsMatch(normalizedValue))
            {
                valuesFailingSubTypeRegex.Add(normalizedValue);
            }
        }

        if (valuesFailingTypeRegex.Count == 0 && valuesFailingSubTypeRegex.Count == 0)
        {
            return null;
        }

        return Results.Json(new
        {
            message = "Uploaded EPV values do not match selected type/sub-type regex rules",
            selected_type = typeName,
            selected_sub_type = subTypeName,
            checked_count = values.Length,
            type_regex = typeRegexRule,
            sub_type_regex = subTypeRegexRule,
            failed_type_regex_count = valuesFailingTypeRegex.Count,
            failed_sub_type_regex_count = valuesFailingSubTypeRegex.Count,
            failed_type_regex_values_preview = valuesFailingTypeRegex.Take(20).ToArray(),
            failed_sub_type_regex_values_preview = valuesFailingSubTypeRegex.Take(20).ToArray()
        }, statusCode: 400);
    }

    private static string DetectFileKind(string fileName, string? mimeType)
    {
        var normalizedName = fileName.ToLowerInvariant();
        var normalizedMime = (mimeType ?? string.Empty).ToLowerInvariant();

        if (normalizedMime.Contains("pdf", StringComparison.Ordinal) || normalizedName.EndsWith(".pdf", StringComparison.Ordinal))
        {
            return "pdf";
        }

        return normalizedName.EndsWith(".txt", StringComparison.Ordinal) ||
               normalizedName.EndsWith(".csv", StringComparison.Ordinal) ||
               normalizedName.EndsWith(".json", StringComparison.Ordinal)
            ? "text"
            : "unknown";
    }

    private static string[] ExtractEpvValues(string text)
    {
        var directRows = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(NormalizeEpvValue)
            .Where(value => value.Length >= 6);

        var tokenMatches = Regex.Matches(text, @"[A-Za-z0-9][A-Za-z0-9._:-]{5,}")
            .Cast<Match>()
            .Select(match => NormalizeEpvValue(match.Value))
            .Where(value => value.Length >= 6);

        var values = directRows.Concat(tokenMatches);
        var deduped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                deduped.Add(value);
            }

            if (deduped.Count >= MaxEpvValues)
            {
                break;
            }
        }

        return deduped.ToArray();
    }

    private static string NormalizeEpvValue(string value)
    {
        return value.Trim().Trim('\'', '"').Trim();
    }

    private static string ExtractTextFromPdfBuffer(byte[] buffer)
    {
        var latinSource = Encoding.Latin1.GetString(buffer);
        var fragments = CollectPdfTextFragments(latinSource);
        var searchIndex = 0;

        while (true)
        {
            var streamIndex = latinSource.IndexOf("stream", searchIndex, StringComparison.Ordinal);
            if (streamIndex == -1)
            {
                break;
            }

            var streamStartIndex = latinSource.IndexOf('\n', streamIndex);
            if (streamStartIndex == -1)
            {
                break;
            }

            var streamDataStart = streamStartIndex + 1;
            var streamEnd = latinSource.IndexOf("endstream", streamDataStart, StringComparison.Ordinal);
            if (streamEnd == -1)
            {
                break;
            }

            var headerStart = Math.Max(0, streamIndex - 240);
            var streamHeader = latinSource[headerStart..streamIndex];
            if (streamHeader.Contains("/FlateDecode", StringComparison.Ordinal))
            {
                var streamData = latinSource[streamDataStart..streamEnd];
                var inflated = TryInflatePdfStream(Encoding.Latin1.GetBytes(streamData));
                if (!string.IsNullOrEmpty(inflated))
                {
                    fragments.AddRange(CollectPdfTextFragments(inflated));
                }
            }

            searchIndex = streamEnd + "endstream".Length;
        }

        return string.Join("\n", fragments).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static List<string> CollectPdfTextFragments(string content)
    {
        var fragments = new List<string>();
        foreach (Match match in Regex.Matches(content, @"\(([^()]*(?:\\.[^()]*)*)\)\s*Tj", RegexOptions.Singleline))
        {
            fragments.Add(DecodePdfEscapes(match.Groups[1].Value));
        }

        foreach (Match arrayMatch in Regex.Matches(content, @"\[(.*?)\]\s*TJ", RegexOptions.Singleline))
        {
            foreach (Match tokenMatch in Regex.Matches(arrayMatch.Groups[1].Value, @"\(([^()]*(?:\\.[^()]*)*)\)"))
            {
                fragments.Add(DecodePdfEscapes(tokenMatch.Groups[1].Value));
            }
        }

        return fragments;
    }

    private static string DecodePdfEscapes(string value)
    {
        var escaped = Regex.Replace(value, @"\\([nrtbf()\\])", match => match.Groups[1].Value switch
        {
            "n" => "\n",
            "r" => "\r",
            "t" => "\t",
            "b" => "\b",
            "f" => "\f",
            "(" => "(",
            ")" => ")",
            "\\" => "\\",
            _ => match.Groups[1].Value
        });

        return Regex.Replace(escaped, @"\\([0-7]{1,3})", match =>
        {
            var code = Convert.ToInt32(match.Groups[1].Value, 8);
            return ((char)code).ToString();
        });
    }

    private static string? TryInflatePdfStream(byte[] streamBytes)
    {
        try
        {
            using var input = new MemoryStream(streamBytes);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static bool HasJsonProperty(JsonNode? node, string key)
    {
        return node is JsonObject jsonObject && jsonObject.ContainsKey(key);
    }

    private static string? ReadStringFromPayloadOrExisting(
        JsonNode? payload,
        string key,
        Dictionary<string, object?>? existing)
    {
        if (HasJsonProperty(payload, key))
        {
            return ReadFlexibleString(payload?[key]);
        }

        return existing is not null && existing.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static decimal? ReadDecimalFromPayloadOrExisting(
        JsonNode? payload,
        string key,
        Dictionary<string, object?>? existing)
    {
        if (HasJsonProperty(payload, key))
        {
            return ReadFlexibleDecimal(payload?[key]);
        }

        return existing is not null && existing.TryGetValue(key, out var value) ? ReadDecimalFromObject(value) : null;
    }

    private static int? ReadIntFromPayloadOrExisting(
        JsonNode? payload,
        string key,
        Dictionary<string, object?>? existing)
    {
        if (HasJsonProperty(payload, key))
        {
            return ReadFlexibleInt(payload?[key]);
        }

        return existing is not null && existing.TryGetValue(key, out var value) ? ReadIntFromObject(value) : null;
    }

    private static string? ReadFlexibleString(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(CultureInfo.InvariantCulture);
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString(CultureInfo.InvariantCulture);
            }

            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                return boolValue ? "true" : "false";
            }
        }

        return value.ToString();
    }

    private static decimal? ReadFlexibleDecimal(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return Convert.ToDecimal(doubleValue);
            }

            if (jsonValue.TryGetValue<string>(out var stringValue) &&
                decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ReadFlexibleInt(JsonNode? value)
    {
        var decimalValue = ReadFlexibleDecimal(value);
        return DecimalToInt(decimalValue);
    }

    private static decimal? ReadDecimalFromObject(object? value)
    {
        return value switch
        {
            null => null,
            decimal decimalValue => decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => Convert.ToDecimal(doubleValue),
            float floatValue => Convert.ToDecimal(floatValue),
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static int? ReadIntFromObject(object? value)
    {
        return DecimalToInt(ReadDecimalFromObject(value));
    }

    private static int? DecimalToInt(decimal? value)
    {
        if (value is null ||
            decimal.Truncate(value.Value) != value.Value ||
            value.Value < int.MinValue ||
            value.Value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static string? ReadString(JsonNode? node, string key)
    {
        return node?[key]?.GetValue<string>();
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

    private static decimal? ReadDecimal(JsonNode? node, string key)
    {
        var value = node?[key];
        if (value is null)
        {
            return null;
        }

        return value.GetValue<decimal>();
    }

    private static int ParsePositiveInt(object? value, int fallback)
    {
        return int.TryParse(value?.ToString(), out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static object? ToDbNullable<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }

    private static bool IsPackStation(string? stationCode, string? stationName)
    {
        var text = $"{stationCode} {stationName}";
        return text.Contains("PACK", StringComparison.OrdinalIgnoreCase);
    }
}
