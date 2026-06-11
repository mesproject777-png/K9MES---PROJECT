using Npgsql;

public static class SitesEndpoints
{
    public static void MapSites(WebApplication app)
    {
        app.MapGet("/api/sites", async () =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            return Results.Json(await SqlQuery.QueryRowsAsync(connection, "SELECT id, name, created_at FROM sites ORDER BY name ASC"));
        });

        app.MapPost("/api/sites", async (HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var name = JsonBodyReader.ReadString(payload, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return ApiResults.JsonError("name is required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    "INSERT INTO sites (name) VALUES (@name) RETURNING id, name, created_at",
                    ("name", name));
                return Results.Json(rows[0], statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return ApiResults.JsonError("Site already exists", 409);
            }
        });

        app.MapPut("/api/sites/{id:int}", async (int id, HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var name = JsonBodyReader.ReadString(payload, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return ApiResults.JsonError("name is required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(
                    connection,
                    "UPDATE sites SET name = @name WHERE id = @id RETURNING id, name, created_at",
                    ("name", name),
                    ("id", id));
                return rows.Count == 0 ? ApiResults.JsonError("Site not found", 404) : Results.Json(rows[0]);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return ApiResults.JsonError("Site already exists", 409);
            }
        });

        app.MapDelete("/api/sites/{id:int}", async (int id) =>
        {
            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            try
            {
                var rows = await SqlQuery.QueryRowsAsync(connection, "DELETE FROM sites WHERE id = @id RETURNING id", ("id", id));
                return rows.Count == 0 ? ApiResults.JsonError("Site not found", 404) : Results.Json(new { message = "Site deleted successfully" });
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                return ApiResults.JsonError("Site is in use by work orders", 409);
            }
        });
    }
}