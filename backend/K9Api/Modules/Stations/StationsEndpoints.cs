using System.Text.Json.Nodes;
using Npgsql;

public static class StationsEndpoints
{
    public static void MapStations(WebApplication app)
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

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            if (limitRaw == "all")
            {
                var allRows = await SqlQuery.QueryRowsAsync(
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
                return Results.Json(new { data = MapStationRows(allRows), total = totalAll, page = 1, limit = totalAll == 0 ? 1 : totalAll });
            }

            var limit = Math.Min(ParsePositiveInt(limitRaw, 25), 500);
            var offset = (page - 1) * limit;
            parameters.Add(("limit", limit));
            parameters.Add(("offset", offset));

            var rows = await SqlQuery.QueryRowsAsync(
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
            return Results.Json(new { data = MapStationRows(rows), total, page, limit });
        });

        app.MapPost("/api/stations", async (HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var code = JsonBodyReader.ReadString(payload, "station_code")?.Trim();
            var desc = JsonBodyReader.ReadString(payload, "station_desc")?.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(desc))
            {
                return ApiResults.JsonMessage("station_code and station_desc are required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
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
                return ApiResults.JsonMessage("Station code already exists", 409);
            }
        });

        app.MapPut("/api/stations/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var code = JsonBodyReader.ReadString(payload, "station_code")?.Trim();
            var desc = JsonBodyReader.ReadString(payload, "station_desc")?.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(desc))
            {
                return ApiResults.JsonMessage("station_code and station_desc are required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
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
                    return ApiResults.JsonMessage("Station not found", 404);
                }

                rows[0]["status"] = "Active";
                return Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return ApiResults.JsonMessage("Station code already exists", 409);
            }
        });

        app.MapDelete("/api/stations/{id:int}", async (int id) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(connection, "DELETE FROM masterstation WHERE masterstation_id = @id RETURNING masterstation_id", ("id", id));
            return rows.Count == 0 ? ApiResults.JsonMessage("Station not found", 404) : Results.Json(new { message = "Station deleted successfully" });
        });

        app.MapPost("/api/stations/import", async (HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var sourceRows = payload switch
            {
                JsonArray array => array.OfType<JsonNode>().ToArray(),
                null => Array.Empty<JsonNode>(),
                _ => new[] { payload }
            };

            var byCode = new Dictionary<string, (string Code, string Desc)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in sourceRows)
            {
                var code = JsonBodyReader.ReadString(row, "station_code")?.Trim();
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                var desc = JsonBodyReader.ReadString(row, "station_desc")?.Trim();
                byCode[code] = (code, string.IsNullOrWhiteSpace(desc) ? code : desc);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            var inserted = 0;
            var updated = 0;
            var skipped = 0;

            try
            {
                foreach (var station in byCode.Values)
                {
                    var existing = await SqlQuery.QueryRowsAsync(
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
                        await SqlQuery.ExecuteAsync(
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

                    await SqlQuery.ExecuteAsync(
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
                var total = await SqlQuery.ScalarAsync<long>(connection, "SELECT COUNT(*) FROM masterstation");
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

    private static List<Dictionary<string, object?>> MapStationRows(List<Dictionary<string, object?>> rows)
    {
        foreach (var row in rows)
        {
            row.Remove("total_count");
            row["status"] = "Active";
        }

        return rows;
    }

    private static int ParsePositiveInt(object? value, int fallback)
    {
        return int.TryParse(value?.ToString(), out var parsed) && parsed > 0 ? parsed : fallback;
    }
}