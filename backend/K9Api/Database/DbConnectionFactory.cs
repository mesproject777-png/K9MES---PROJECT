using Npgsql;

public static class DbConnectionFactory
{
    public static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    public static string GetConnectionString()
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
}