using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

public static class SgdPosEndpoints
{
    public static void MapSgdPos(WebApplication app)
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

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(
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

    private static async Task<IResult> SaveSgdPoAsync(HttpContext context, int? id)
    {
        var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
        var po = JsonBodyReader.ReadString(payload, "po")?.Trim();
        var status = JsonBodyReader.ReadString(payload, "status")?.Trim() ?? "open";
        var swVersion = JsonBodyReader.ReadString(payload, "sw_version")?.Trim();
        var hwVersion = JsonBodyReader.ReadString(payload, "hw_version")?.Trim();
        var itemId = ReadInt(payload, "item_id");
        var poQty = ReadInt(payload, "po_qty");

        if (string.IsNullOrWhiteSpace(po) || itemId is null || poQty is null || poQty.Value <= 0)
        {
            return ApiResults.JsonMessage("po, item_id, and positive po_qty are required", 400);
        }

        await using var connection = await DbConnectionFactory.OpenConnectionAsync();
        try
        {
            var rows = id is null
                ? await SqlQuery.QueryRowsAsync(
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
                : await SqlQuery.QueryRowsAsync(
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

            return rows.Count == 0 ? ApiResults.JsonMessage("SGD PO not found", 404) : Results.Json(rows[0], statusCode: id is null ? 201 : 200);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return ApiResults.JsonMessage("SGD PO already exists", 409);
        }
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
