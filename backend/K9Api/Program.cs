using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

LoadLocalEnvironmentFile();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5100");

var app = builder.Build();

static void LoadLocalEnvironmentFile()
{
    var directory = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(directory))
    {
        var path = Path.Combine(directory, ".env.local");
        if (File.Exists(path))
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"');
                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            return;
        }

        directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
    }
}

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

async Task<bool> HandleWorkflowStationLoginsAsync(HttpContext context)
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (!path.Equals("/api/workflow/station-logins", StringComparison.OrdinalIgnoreCase) ||
        !context.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var payload = await ReadJsonBodyAsync(context.Request);
    var pn = payload?["pn"]?.GetValue<string>()?.Trim();
    var wo = payload?["wo"]?.GetValue<string>()?.Trim();
    var stationsNode = payload?["stations"]?.AsArray();
    if (string.IsNullOrWhiteSpace(pn))
    {
        await WriteErrorAsync(context, "pn is required", 400);
        return true;
    }

    if (string.IsNullOrWhiteSpace(wo))
    {
        await WriteErrorAsync(context, "wo is required", 400);
        return true;
    }

    if (stationsNode is null)
    {
        await WriteErrorAsync(context, "stations is required", 400);
        return true;
    }

    var stationLogins = new List<(int? LoginRowId, int StationId, string StationCode, string LoginId, string Password)>();
    var loginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var stationNode in stationsNode)
    {
        var stationId = ReadInt(stationNode, "station_id") ?? ReadInt(stationNode, "id");
        var loginRowId = ReadInt(stationNode, "id");
        var stationCode = stationNode?["station_code"]?.GetValue<string>()?.Trim() ?? string.Empty;
        var loginId = stationNode?["station_login_id"]?.GetValue<string>()?.Trim() ?? string.Empty;
        var password = stationNode?["station_login_password"]?.GetValue<string>()?.Trim() ?? string.Empty;

        if (stationId is null or <= 0)
        {
            await WriteErrorAsync(context, "Invalid station row", 400);
            return true;
        }

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(password))
        {
            await WriteErrorAsync(context, $"Login ID and password are required for station {stationCode}", 400);
            return true;
        }

        if (!loginIds.Add(loginId))
        {
            await WriteErrorAsync(context, $"Login ID {loginId} is already used by another station", 400);
            return true;
        }

        stationLogins.Add((loginRowId, stationId.Value, stationCode, loginId, password));
    }

    await using var connection = new NpgsqlConnection(GetConnectionString());
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    async Task<List<Dictionary<string, object?>>> QueryRowsAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    try
    {
        await ExecuteAsync(
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

        await ExecuteAsync("ALTER TABLE public.workflow_station_logins ADD COLUMN IF NOT EXISTS workflow_work_order_id INTEGER REFERENCES workflow_work_orders(id) ON DELETE CASCADE");

        var workOrderRows = await QueryRowsAsync(
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
            await transaction.RollbackAsync();
            await WriteErrorAsync(context, "Work order not found", 404);
            return true;
        }

        var workflowWorkOrderId = Convert.ToInt32(workOrderRows[0]["workflow_work_order_id"]);
        var workflowPartId = Convert.ToInt32(workOrderRows[0]["workflow_part_id"]);

        var stationIds = stationLogins.Select(station => station.StationId).Distinct().ToArray();
        if (stationIds.Length > 0)
        {
            var existingStations = await QueryRowsAsync(
                """
                SELECT id
                FROM workflow_routing_steps
                WHERE workflow_part_id = @workflowPartId
                  AND id = ANY(@stationIds)
                """,
                ("workflowPartId", workflowPartId),
                ("stationIds", stationIds));

            if (existingStations.Count != stationIds.Length)
            {
                await transaction.RollbackAsync();
                await WriteErrorAsync(context, "One or more stations were not found for this workflow", 404);
                return true;
            }
        }

        foreach (var station in stationLogins)
        {
            var conflicts = await QueryRowsAsync(
                """
                SELECT station_code, pn, wo
                FROM (
                    SELECT r.station_code, p.pn, w.wo
                    FROM workflow_station_logins l
                    JOIN workflow_routing_steps r ON r.id = l.workflow_routing_step_id
                    JOIN workflow_part_numbers p ON p.id = r.workflow_part_id
                    LEFT JOIN workflow_work_orders w ON w.id = l.workflow_work_order_id
                    WHERE UPPER(l.station_login_id) = UPPER(@loginId)
                      AND (@loginRowId::integer IS NULL OR l.id <> @loginRowId::integer)
                ) conflicts
                LIMIT 1
                """,
                ("loginId", station.LoginId),
                ("loginRowId", station.LoginRowId),
                ("stationId", station.StationId));

            if (conflicts.Count > 0)
            {
                var conflict = conflicts[0];
                var conflictStation = conflict["station_code"]?.ToString();
                var conflictPn = conflict["pn"]?.ToString();
                var conflictWo = conflict["wo"]?.ToString();
                var location = string.IsNullOrWhiteSpace(conflictStation) ? "another station" : $"station {conflictStation}";
                if (!string.IsNullOrWhiteSpace(conflictPn) && !string.IsNullOrWhiteSpace(conflictWo))
                {
                    location += $" (PN {conflictPn}, WO {conflictWo})";
                }
                else if (!string.IsNullOrWhiteSpace(conflictPn))
                {
                    location += $" (PN {conflictPn})";
                }

                await transaction.RollbackAsync();
                await WriteErrorAsync(context, $"Login ID {station.LoginId} is already used by {location}", 409);
                return true;
            }
        }

        foreach (var station in stationLogins)
        {
            if (station.LoginRowId is > 0)
            {
                var updatedLogin = await ExecuteAsync(
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
        await WriteJsonAsync(context, new Dictionary<string, object?> { ["message"] = "Station logins saved successfully" });
        return true;
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        await transaction.RollbackAsync();
        await WriteErrorAsync(context, "Login ID is already used by another station", 409);
        return true;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

app.Use(async (HttpContext context, RequestDelegate next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "http://localhost:4400";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

    if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    if (await HandleWorkflowStationLoginsAsync(context))
    {
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
