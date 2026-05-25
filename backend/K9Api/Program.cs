using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");

var app = builder.Build();

string GetConnectionString()
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

async Task<JsonNode?> ReadJsonBodyAsync(HttpRequest request)
{
    if (request.ContentLength is null || request.ContentLength <= 0)
    {
        return null;
    }

    using var reader = new StreamReader(request.Body, leaveOpen: true);
    var content = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(content))
    {
        return null;
    }

    return JsonNode.Parse(content);
}

static string ReadRequiredString(JsonNode? node, string key)
{
    var value = node?[key]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"{key} is required");
    }

    return value;
}

static bool ReadBoolean(JsonNode? node, string key, bool fallback)
{
    if (node is null)
    {
        return fallback;
    }

    var value = node[key];
    if (value is null)
    {
        return fallback;
    }

    return value.GetValue<bool>();
}

static int? ReadInt(JsonNode? node, string key)
{
    if (node is null)
    {
        return null;
    }

    var value = node[key];
    if (value is null)
    {
        return null;
    }

    return value.GetValue<int>();
}

static string[] ReadStringArray(JsonNode? node, string key)
{
    var value = node?[key];
    if (value is null)
    {
        return Array.Empty<string>();
    }

    if (value is not JsonArray array)
    {
        throw new InvalidOperationException($"{key} must be an array");
    }

    return array.Select(item => item?.GetValue<string>() ?? string.Empty)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToArray();
}

static string[] GetPageAccess(NpgsqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    if (reader.IsDBNull(ordinal))
    {
        return Array.Empty<string>();
    }

    return reader.GetFieldValue<string[]>(ordinal) ?? Array.Empty<string>();
}

static Dictionary<string, object?> MapUser(NpgsqlDataReader reader)
{
    return new Dictionary<string, object?>
    {
        ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
        ["login_id"] = reader.GetString(reader.GetOrdinal("login_id")),
        ["user_name"] = reader.GetString(reader.GetOrdinal("user_name")),
        ["password"] = reader.IsDBNull(reader.GetOrdinal("password")) ? null : reader.GetString(reader.GetOrdinal("password")),
        ["is_active"] = reader.GetBoolean(reader.GetOrdinal("is_active")),
        ["created_at"] = reader.GetDateTime(reader.GetOrdinal("created_at")),
        ["role_id"] = reader.IsDBNull(reader.GetOrdinal("role_id")) ? null : reader.GetInt32(reader.GetOrdinal("role_id")),
        ["role_name"] = reader.IsDBNull(reader.GetOrdinal("role_name")) ? null : reader.GetString(reader.GetOrdinal("role_name")),
        ["page_access"] = GetPageAccess(reader, "page_access")
    };
}

static Dictionary<string, object?> MapRole(NpgsqlDataReader reader)
{
    return new Dictionary<string, object?>
    {
        ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
        ["role_name"] = reader.GetString(reader.GetOrdinal("role_name")),
        ["page_access"] = GetPageAccess(reader, "page_access")
    };
}

static Dictionary<string, object?> MapProductLine(NpgsqlDataReader reader)
{
    return new Dictionary<string, object?>
    {
        ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
        ["code"] = reader.GetString(reader.GetOrdinal("code")),
        ["description"] = reader.GetString(reader.GetOrdinal("description")),
        ["status"] = reader.GetString(reader.GetOrdinal("status")),
        ["created_at"] = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}

static Dictionary<string, object?> MapPnType(NpgsqlDataReader reader)
{
    return new Dictionary<string, object?>
    {
        ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
        ["type"] = reader.GetString(reader.GetOrdinal("type")),
        ["code"] = reader.GetString(reader.GetOrdinal("code")),
        ["description"] = reader.GetString(reader.GetOrdinal("description")),
        ["status"] = reader.GetString(reader.GetOrdinal("status")),
        ["created_at"] = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}

static Dictionary<string, object?> MapHistory(NpgsqlDataReader reader)
{
    return new Dictionary<string, object?>
    {
        ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
        ["user_id"] = reader.GetInt32(reader.GetOrdinal("user_id")),
        ["field_name"] = reader.GetString(reader.GetOrdinal("field_name")),
        ["old_value"] = reader.IsDBNull(reader.GetOrdinal("old_value")) ? null : reader.GetString(reader.GetOrdinal("old_value")),
        ["new_value"] = reader.IsDBNull(reader.GetOrdinal("new_value")) ? null : reader.GetString(reader.GetOrdinal("new_value")),
        ["changed_by"] = reader.GetString(reader.GetOrdinal("changed_by")),
        ["changed_at"] = reader.GetDateTime(reader.GetOrdinal("changed_at"))
    };
}

static Dictionary<string, object?> SanitizeUser(Dictionary<string, object?> user)
{
    var sanitized = new Dictionary<string, object?>(user);
    sanitized.Remove("password");
    return sanitized;
}

async Task WriteJsonAsync(HttpContext context, object? payload, int statusCode = 200)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

async Task WriteErrorAsync(HttpContext context, string error, int statusCode)
{
    await WriteJsonAsync(context, new Dictionary<string, object?> { ["error"] = error }, statusCode);
}

async Task<string?> GetRoleNameByIdAsync(NpgsqlConnection connection, int? roleId)
{
    if (roleId is null)
    {
        return null;
    }

    await using var roleCommand = new NpgsqlCommand("SELECT role_name FROM roles WHERE id = @id", connection);
    roleCommand.Parameters.AddWithValue("id", roleId.Value);
    await using var reader = await roleCommand.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return reader.GetString(0);
}

async Task InsertHistoryAsync(NpgsqlConnection connection, int userId, string fieldName, string? oldValue, string? newValue, string changedBy)
{
    await using var command = new NpgsqlCommand(
        "INSERT INTO user_history (user_id, field_name, old_value, new_value, changed_by) VALUES (@userId, @fieldName, @oldValue, @newValue, @changedBy)",
        connection);
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("fieldName", fieldName);
    command.Parameters.AddWithValue("oldValue", (object?)oldValue ?? DBNull.Value);
    command.Parameters.AddWithValue("newValue", (object?)newValue ?? DBNull.Value);
    command.Parameters.AddWithValue("changedBy", changedBy);
    await command.ExecuteNonQueryAsync();
}

async Task<Dictionary<string, object?>?> GetUserByIdAsync(NpgsqlConnection connection, int id)
{
    await using var command = new NpgsqlCommand(
        "SELECT u.id, u.login_id, u.user_name, u.password, u.is_active, u.created_at, u.role_id, r.role_name, COALESCE(r.page_access, '{}') AS page_access FROM users u LEFT JOIN roles r ON r.id = u.role_id WHERE u.id = @id",
        connection);
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return MapUser(reader);
}

async Task<List<Dictionary<string, object?>>> GetUsersAsync(NpgsqlConnection connection)
{
    var users = new List<Dictionary<string, object?>>();
    await using var command = new NpgsqlCommand(
        "SELECT u.id, u.login_id, u.user_name, u.password, u.is_active, u.created_at, u.role_id, r.role_name, COALESCE(r.page_access, '{}') AS page_access FROM users u LEFT JOIN roles r ON r.id = u.role_id ORDER BY u.id ASC",
        connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(SanitizeUser(MapUser(reader)));
    }

    return users;
}

async Task<List<Dictionary<string, object?>>> GetRolesAsync(NpgsqlConnection connection)
{
    var roles = new List<Dictionary<string, object?>>();
    await using var command = new NpgsqlCommand("SELECT id, role_name, page_access FROM roles ORDER BY role_name ASC", connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(MapRole(reader));
    }

    return roles;
}

async Task<List<Dictionary<string, object?>>> GetProductLinesAsync(NpgsqlConnection connection)
{
    var rows = new List<Dictionary<string, object?>>();
    await using var command = new NpgsqlCommand("SELECT id, code, description, status, created_at FROM product_lines ORDER BY id ASC", connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(MapProductLine(reader));
    }

    return rows;
}

async Task<List<Dictionary<string, object?>>> GetPnTypesAsync(NpgsqlConnection connection)
{
    var rows = new List<Dictionary<string, object?>>();
    await using var command = new NpgsqlCommand("SELECT id, type, code, description, status, created_at FROM pn_types ORDER BY id ASC", connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(MapPnType(reader));
    }

    return rows;
}

async Task<List<Dictionary<string, object?>>> GetUserHistoryAsync(NpgsqlConnection connection, int userId)
{
    var history = new List<Dictionary<string, object?>>();
    await using var command = new NpgsqlCommand(
        "SELECT id, user_id, field_name, old_value, new_value, changed_by, changed_at FROM user_history WHERE user_id = @userId ORDER BY changed_at DESC, id DESC",
        connection);
    command.Parameters.AddWithValue("userId", userId);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        history.Add(MapHistory(reader));
    }

    return history;
}

async Task<int> CountLinkedUsersAsync(NpgsqlConnection connection, int roleId)
{
    await using var command = new NpgsqlCommand("SELECT COUNT(*)::int AS count FROM users WHERE role_id = @roleId", connection);
    command.Parameters.AddWithValue("roleId", roleId);
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return reader.GetInt32(0);
}

async Task<bool> HandleUsersAsync(HttpContext context)
{
    var path = context.Request.Path.Value ?? string.Empty;
    var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    if (segments.Length < 2 ||
        !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
        !segments[1].Equals("users", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    await using var connection = new NpgsqlConnection(GetConnectionString());
    await connection.OpenAsync();

    if (segments.Length == 2)
    {
        if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, await GetUsersAsync(connection));
            return true;
        }

        if (context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var loginId = ReadRequiredString(payload, "loginId");
            var userName = ReadRequiredString(payload, "userName");
            var password = ReadRequiredString(payload, "password");
            var roleId = ReadInt(payload, "roleId");
            var isActive = ReadBoolean(payload, "isActive", true);
            var updatedBy = payload["updatedBy"]?.GetValue<string>() ?? "system";

            if (roleId is null)
            {
                await WriteErrorAsync(context, "roleId is required", 400);
                return true;
            }

            await using var insertCommand = new NpgsqlCommand(
                "INSERT INTO users (login_id, user_name, password, is_active, role_id) VALUES (@loginId, @userName, @password, @isActive, @roleId) RETURNING id",
                connection);
            insertCommand.Parameters.AddWithValue("loginId", loginId);
            insertCommand.Parameters.AddWithValue("userName", userName);
            insertCommand.Parameters.AddWithValue("password", password);
            insertCommand.Parameters.AddWithValue("isActive", isActive);
            insertCommand.Parameters.AddWithValue("roleId", roleId.Value);

            try
            {
                var scalar = await insertCommand.ExecuteScalarAsync();
                if (scalar is null)
                {
                    await WriteErrorAsync(context, "Failed to create user", 500);
                    return true;
                }

                var newId = Convert.ToInt32(scalar);
                var createdRole = await GetRoleNameByIdAsync(connection, roleId.Value);
                await InsertHistoryAsync(connection, newId, "login_id", null, loginId, updatedBy);
                await InsertHistoryAsync(connection, newId, "user_name", null, userName, updatedBy);
                await InsertHistoryAsync(connection, newId, "password", null, "Password created", updatedBy);
                await InsertHistoryAsync(connection, newId, "is_active", null, isActive.ToString(), updatedBy);
                await InsertHistoryAsync(connection, newId, "role", null, createdRole ?? roleId.Value.ToString(), updatedBy);
                var user = SanitizeUser((await GetUserByIdAsync(connection, newId))!);
                await WriteJsonAsync(context, user, 201);
                return true;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await WriteErrorAsync(context, "Login ID already exists", 409);
                return true;
            }
        }
    }

    if (segments.Length == 3 && int.TryParse(segments[2], out var userId))
    {
        if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var user = await GetUserByIdAsync(connection, userId);
            if (user is null)
            {
                await WriteErrorAsync(context, "User not found", 404);
                return true;
            }

            await WriteJsonAsync(context, SanitizeUser(user!));
            return true;
        }

        if (context.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var existing = await GetUserByIdAsync(connection, userId);
            if (existing is null)
            {
                await WriteErrorAsync(context, "User not found", 404);
                return true;
            }

            var loginId = payload["loginId"]?.GetValue<string>() ?? existing["login_id"]?.ToString();
            var userName = payload["userName"]?.GetValue<string>() ?? existing["user_name"]?.ToString();
            var password = payload["password"]?.GetValue<string>();
            var roleId = ReadInt(payload, "roleId") ?? (existing["role_id"] as int?);
            var isActive = ReadBoolean(payload, "isActive", existing["is_active"] is bool currentIsActive ? currentIsActive : true);
            var updatedBy = payload["updatedBy"]?.GetValue<string>() ?? "system";

            if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(userName) || roleId is null)
            {
                await WriteErrorAsync(context, "loginId, userName, and roleId are required", 400);
                return true;
            }

            var plainPassword = string.IsNullOrWhiteSpace(password) ? existing["password"]?.ToString() ?? string.Empty : password;

            await using var updateCommand = new NpgsqlCommand(
                "UPDATE users SET login_id = @loginId, user_name = @userName, password = @password, is_active = @isActive, role_id = @roleId WHERE id = @id",
                connection);
            updateCommand.Parameters.AddWithValue("loginId", loginId);
            updateCommand.Parameters.AddWithValue("userName", userName);
            updateCommand.Parameters.AddWithValue("password", plainPassword);
            updateCommand.Parameters.AddWithValue("isActive", isActive);
            updateCommand.Parameters.AddWithValue("roleId", roleId.Value);
            updateCommand.Parameters.AddWithValue("id", userId);

            try
            {
                await updateCommand.ExecuteNonQueryAsync();
                var previousRoleName = await GetRoleNameByIdAsync(connection, existing["role_id"] as int?);
                var nextRoleName = await GetRoleNameByIdAsync(connection, roleId.Value);

                if (!string.Equals(existing["login_id"]?.ToString(), loginId, StringComparison.Ordinal))
                {
                    await InsertHistoryAsync(connection, userId, "login_id", existing["login_id"]?.ToString(), loginId, updatedBy);
                }

                if (!string.Equals(existing["user_name"]?.ToString(), userName, StringComparison.Ordinal))
                {
                    await InsertHistoryAsync(connection, userId, "user_name", existing["user_name"]?.ToString(), userName, updatedBy);
                }

                if ((existing["is_active"] as bool?) != isActive)
                {
                    await InsertHistoryAsync(connection, userId, "is_active", (existing["is_active"] as bool?)?.ToString(), isActive.ToString(), updatedBy);
                }

                if ((existing["role_id"] as int?) != roleId.Value)
                {
                    await InsertHistoryAsync(connection, userId, "role", previousRoleName ?? (existing["role_id"]?.ToString()), nextRoleName ?? roleId.Value.ToString(), updatedBy);
                }

                if (!string.IsNullOrWhiteSpace(password))
                {
                    await InsertHistoryAsync(connection, userId, "password", "Password existed", "Password updated", updatedBy);
                }

                var updatedUser = SanitizeUser((await GetUserByIdAsync(connection, userId))!);
                await WriteJsonAsync(context, updatedUser);
                return true;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await WriteErrorAsync(context, "Login ID already exists", 409);
                return true;
            }
        }

        if (context.Request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            await using var deleteCommand = new NpgsqlCommand("DELETE FROM users WHERE id = @id RETURNING id", connection);
            deleteCommand.Parameters.AddWithValue("id", userId);
            var deletedId = await deleteCommand.ExecuteScalarAsync();
            if (deletedId is null)
            {
                await WriteErrorAsync(context, "User not found", 404);
                return true;
            }

            await WriteJsonAsync(context, new Dictionary<string, object?> { ["message"] = "User deleted successfully" });
            return true;
        }
    }

    if (segments.Length == 4 &&
        segments[3].Equals("history", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(segments[2], out var historyUserId))
    {
        if (!context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var history = await GetUserHistoryAsync(connection, historyUserId);
        await WriteJsonAsync(context, history);
        return true;
    }

    if (segments.Length == 3 && segments[2] == "roles")
    {
        if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, await GetRolesAsync(connection));
            return true;
        }

        if (context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var roleName = ReadRequiredString(payload, "roleName");
            var pageAccess = ReadStringArray(payload, "pageAccess");
            if (pageAccess.Length == 0)
            {
                await WriteErrorAsync(context, "roleName and at least one pageAccess entry are required", 400);
                return true;
            }

            try
            {
                await using var insertCommand = new NpgsqlCommand("INSERT INTO roles (role_name, page_access) VALUES (@roleName, @pageAccess) RETURNING id, role_name, page_access", connection);
                insertCommand.Parameters.AddWithValue("roleName", roleName);
                insertCommand.Parameters.AddWithValue("pageAccess", pageAccess);
                await using var reader = await insertCommand.ExecuteReaderAsync();
                await reader.ReadAsync();
                await WriteJsonAsync(context, MapRole(reader), 201);
                return true;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await WriteErrorAsync(context, "Role name already exists", 409);
                return true;
            }
        }
    }

    if (segments.Length == 4 && segments[2] == "roles" && int.TryParse(segments[3], out var roleIdToUpdate))
    {
        if (context.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var roleName = ReadRequiredString(payload, "roleName");
            var pageAccess = ReadStringArray(payload, "pageAccess");
            if (pageAccess.Length == 0)
            {
                await WriteErrorAsync(context, "roleName and at least one pageAccess entry are required", 400);
                return true;
            }

            try
            {
                await using var updateCommand = new NpgsqlCommand("UPDATE roles SET role_name = @roleName, page_access = @pageAccess WHERE id = @id RETURNING id, role_name, page_access", connection);
                updateCommand.Parameters.AddWithValue("roleName", roleName);
                updateCommand.Parameters.AddWithValue("pageAccess", pageAccess);
                updateCommand.Parameters.AddWithValue("id", roleIdToUpdate);
                await using var reader = await updateCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    await WriteErrorAsync(context, "Role not found", 404);
                    return true;
                }

                await WriteJsonAsync(context, MapRole(reader));
                return true;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await WriteErrorAsync(context, "Role name already exists", 409);
                return true;
            }
        }

        if (context.Request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            var linkedUsers = await CountLinkedUsersAsync(connection, roleIdToUpdate);
            if (linkedUsers > 0)
            {
                await WriteErrorAsync(context, "Role is assigned to users and cannot be deleted", 409);
                return true;
            }

            await using var deleteCommand = new NpgsqlCommand("DELETE FROM roles WHERE id = @id RETURNING id", connection);
            deleteCommand.Parameters.AddWithValue("id", roleIdToUpdate);
            var deletedId = await deleteCommand.ExecuteScalarAsync();
            if (deletedId is null)
            {
                await WriteErrorAsync(context, "Role not found", 404);
                return true;
            }

            await WriteJsonAsync(context, new Dictionary<string, object?> { ["message"] = "Role deleted successfully" });
            return true;
        }
    }

    if (segments.Length == 3 && segments[2] == "product-lines")
    {
        if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, await GetProductLinesAsync(connection));
            return true;
        }

        if (context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var code = ReadRequiredString(payload, "code");
            var description = ReadRequiredString(payload, "description");
            var status = payload["status"]?.GetValue<string>() ?? "Active";
            if (string.IsNullOrWhiteSpace(status))
            {
                await WriteErrorAsync(context, "status is required", 400);
                return true;
            }

            try
            {
                await using var insertCommand = new NpgsqlCommand("INSERT INTO product_lines (code, description, status) VALUES (@code, @description, @status) RETURNING id, code, description, status, created_at", connection);
                insertCommand.Parameters.AddWithValue("code", code);
                insertCommand.Parameters.AddWithValue("description", description);
                insertCommand.Parameters.AddWithValue("status", status);
                await using var reader = await insertCommand.ExecuteReaderAsync();
                await reader.ReadAsync();
                await WriteJsonAsync(context, MapProductLine(reader), 201);
                return true;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await WriteErrorAsync(context, "Product line code already exists", 409);
                return true;
            }
        }
    }

    if (segments.Length == 4 && segments[2] == "product-lines" && int.TryParse(segments[3], out var productLineId))
    {
        if (context.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var code = ReadRequiredString(payload, "code");
            var description = ReadRequiredString(payload, "description");
            var status = ReadRequiredString(payload, "status");
            await using var updateCommand = new NpgsqlCommand("UPDATE product_lines SET code = @code, description = @description, status = @status WHERE id = @id RETURNING id, code, description, status, created_at", connection);
            updateCommand.Parameters.AddWithValue("code", code);
            updateCommand.Parameters.AddWithValue("description", description);
            updateCommand.Parameters.AddWithValue("status", status);
            updateCommand.Parameters.AddWithValue("id", productLineId);
            await using var reader = await updateCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await WriteErrorAsync(context, "Product line not found", 404);
                return true;
            }

            await WriteJsonAsync(context, MapProductLine(reader));
            return true;
        }

        if (context.Request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            await using var deleteCommand = new NpgsqlCommand("DELETE FROM product_lines WHERE id = @id RETURNING id", connection);
            deleteCommand.Parameters.AddWithValue("id", productLineId);
            var deletedId = await deleteCommand.ExecuteScalarAsync();
            if (deletedId is null)
            {
                await WriteErrorAsync(context, "Product line not found", 404);
                return true;
            }

            await WriteJsonAsync(context, new Dictionary<string, object?> { ["message"] = "Product line deleted successfully" });
            return true;
        }
    }

    if (segments.Length == 3 && segments[2] == "pn-types")
    {
        if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, await GetPnTypesAsync(connection));
            return true;
        }

        if (context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var type = ReadRequiredString(payload, "type");
            var code = ReadRequiredString(payload, "code");
            var description = ReadRequiredString(payload, "description");
            var status = payload["status"]?.GetValue<string>() ?? "Active";
            if (string.IsNullOrWhiteSpace(status))
            {
                await WriteErrorAsync(context, "status is required", 400);
                return true;
            }

            try
            {
                await using var insertCommand = new NpgsqlCommand("INSERT INTO pn_types (type, code, description, status) VALUES (@type, @code, @description, @status) RETURNING id, type, code, description, status, created_at", connection);
                insertCommand.Parameters.AddWithValue("type", type);
                insertCommand.Parameters.AddWithValue("code", code);
                insertCommand.Parameters.AddWithValue("description", description);
                insertCommand.Parameters.AddWithValue("status", status);
                await using var reader = await insertCommand.ExecuteReaderAsync();
                await reader.ReadAsync();
                await WriteJsonAsync(context, MapPnType(reader), 201);
                return true;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await WriteErrorAsync(context, "PN type code already exists", 409);
                return true;
            }
        }
    }

    if (segments.Length == 4 && segments[2] == "pn-types" && int.TryParse(segments[3], out var pnTypeId))
    {
        if (context.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await ReadJsonBodyAsync(context.Request);
            if (payload is null)
            {
                await WriteErrorAsync(context, "Request body is required", 400);
                return true;
            }

            var type = ReadRequiredString(payload, "type");
            var code = ReadRequiredString(payload, "code");
            var description = ReadRequiredString(payload, "description");
            var status = ReadRequiredString(payload, "status");
            await using var updateCommand = new NpgsqlCommand("UPDATE pn_types SET type = @type, code = @code, description = @description, status = @status WHERE id = @id RETURNING id, type, code, description, status, created_at", connection);
            updateCommand.Parameters.AddWithValue("type", type);
            updateCommand.Parameters.AddWithValue("code", code);
            updateCommand.Parameters.AddWithValue("description", description);
            updateCommand.Parameters.AddWithValue("status", status);
            updateCommand.Parameters.AddWithValue("id", pnTypeId);
            await using var reader = await updateCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await WriteErrorAsync(context, "PN type not found", 404);
                return true;
            }

            await WriteJsonAsync(context, MapPnType(reader));
            return true;
        }

        if (context.Request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            await using var deleteCommand = new NpgsqlCommand("DELETE FROM pn_types WHERE id = @id RETURNING id", connection);
            deleteCommand.Parameters.AddWithValue("id", pnTypeId);
            var deletedId = await deleteCommand.ExecuteScalarAsync();
            if (deletedId is null)
            {
                await WriteErrorAsync(context, "PN type not found", 404);
                return true;
            }

            await WriteJsonAsync(context, new Dictionary<string, object?> { ["message"] = "PN type deleted successfully" });
            return true;
        }
    }

    return false;
}

app.Use(async (HttpContext context, RequestDelegate next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "http://localhost:4200";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

    if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    if (await HandleUsersAsync(context))
    {
        return;
    }

    await next(context);
});

app.MapConvertedEndpoints();
app.MapFallback(() => Results.Json(new { message = "Endpoint not found" }, statusCode: 404));

await app.RunAsync();
