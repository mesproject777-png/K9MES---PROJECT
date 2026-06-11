using Npgsql;

public static class SqlQuery
{
    public static async Task<List<Dictionary<string, object?>>> QueryRowsAsync(
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

    public static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<T?> ScalarAsync<T>(
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

    public static void AddParameters(NpgsqlCommand command, params (string Name, object? Value)[] parameters)
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
}