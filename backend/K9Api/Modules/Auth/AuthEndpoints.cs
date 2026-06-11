public static class AuthEndpoints
{
    public static void MapAuth(WebApplication app)
    {
        app.MapPost("/api/users/login", async (HttpContext context) =>
        {
            var payload = await JsonBodyReader.ReadJsonBodyAsync(context.Request);
            var loginId = JsonBodyReader.ReadString(payload, "loginId")?.Trim();
            var password = JsonBodyReader.ReadString(payload, "password");
            if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(password))
            {
                return ApiResults.JsonError("loginId and password are required", 400);
            }

            await using var connection = await DbConnectionFactory.OpenConnectionAsync();
            var rows = await SqlQuery.QueryRowsAsync(
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
                return ApiResults.JsonError("Invalid login ID or password", 401);
            }

            var user = rows[0];
            if (user["password"] is not string storedPassword || !string.Equals(storedPassword, password, StringComparison.Ordinal))
            {
                return ApiResults.JsonError("Invalid login ID or password", 401);
            }

            if (user["is_active"] is bool active && !active)
            {
                return ApiResults.JsonError("User is inactive and cannot log in", 403);
            }

            user.Remove("password");
            return Results.Json(user);
        });
    }
}