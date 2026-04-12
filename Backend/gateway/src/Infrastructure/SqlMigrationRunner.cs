using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Backend.Common.Infrastructure;

public static class SqlMigrationRunner
{
    public static async Task ApplyAsync(
        DbContext dbContext,
        string serviceName,
        string migrationsDirectory,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required", nameof(serviceName));

        await EnsureMigrationsTableAsync(dbContext, cancellationToken);

        if (!Directory.Exists(migrationsDirectory))
        {
            logger.LogInformation(
                "Migration directory not found for service {ServiceName}: {Directory}. Skipping SQL migrations.",
                serviceName,
                migrationsDirectory);
            return;
        }

        var migrationFiles = Directory
            .GetFiles(migrationsDirectory, "V*__*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var filePath in migrationFiles)
        {
            var migration = ParseMigration(filePath);
            var script = await File.ReadAllTextAsync(filePath, cancellationToken);
            var checksum = ComputeChecksum(script);
            var existingChecksum = await GetAppliedChecksumAsync(
                dbContext,
                serviceName,
                migration.Version,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(existingChecksum))
            {
                if (!string.Equals(existingChecksum, checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Migration checksum mismatch for {serviceName} {migration.Version}. " +
                        $"Applied checksum={existingChecksum}, current checksum={checksum}.");
                }

                continue;
            }

            logger.LogInformation(
                "Applying SQL migration {Version} ({Description}) for service {ServiceName}",
                migration.Version,
                migration.Description,
                serviceName);

            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await ExecuteScriptAsync(dbContext, script, cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO schema_migrations (service, version, description, checksum) VALUES ({serviceName}, {migration.Version}, {migration.Description}, {checksum})",
                cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
    }

    private static async Task EnsureMigrationsTableAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id BIGSERIAL PRIMARY KEY,
                service VARCHAR(128) NOT NULL,
                version VARCHAR(64) NOT NULL,
                description VARCHAR(255) NOT NULL,
                checksum VARCHAR(128) NOT NULL,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE(service, version)
            );
            """,
            cancellationToken);
    }

    private static async Task<string?> GetAppliedChecksumAsync(
        DbContext dbContext,
        string serviceName,
        string version,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT checksum
                FROM schema_migrations
                WHERE service = @service
                  AND version = @version
                LIMIT 1
                """;

            var serviceParameter = command.CreateParameter();
            serviceParameter.ParameterName = "@service";
            serviceParameter.Value = serviceName;
            command.Parameters.Add(serviceParameter);

            var versionParameter = command.CreateParameter();
            versionParameter.ParameterName = "@version";
            versionParameter.Value = version;
            command.Parameters.Add(versionParameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result as string;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static (string Version, string Description) ParseMigration(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var separatorIndex = fileName.IndexOf("__", StringComparison.Ordinal);
        if (!fileName.StartsWith("V", StringComparison.OrdinalIgnoreCase) || separatorIndex <= 1)
            throw new InvalidOperationException($"Invalid migration file name format: {fileName}");

        var version = fileName[1..separatorIndex];
        var description = fileName[(separatorIndex + 2)..].Replace('_', ' ');
        return (version, description);
    }

    private static string ComputeChecksum(string script)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(script));
        return Convert.ToHexString(bytes);
    }

    private static async Task ExecuteScriptAsync(
        DbContext dbContext,
        string script,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = script;
            command.CommandType = CommandType.Text;

            var currentTransaction = dbContext.Database.CurrentTransaction;
            if (currentTransaction is not null)
                command.Transaction = currentTransaction.GetDbTransaction();

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
