using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ECommerceBackend.Tests.Support;
using Microsoft.Data.SqlClient;

namespace ECommerceBackend.Tests;

public sealed class MigrationArtifactSqlServerTests
{
    private const string ArtifactsDirectoryVariable = "ECOMMERCE_MIGRATION_ARTIFACTS_DIRECTORY";
    private const string BackupDirectoryVariable = "ECOMMERCE_TEST_SQL_BACKUP_DIRECTORY";
    private const string AuthenticationMigration = "20260724152822_HardenAuthenticationFlows";
    private static readonly string[] RetentionIndexes =
    [
        "IX_RefreshTokens_ExpiresAt",
        "IX_PaymentWebhookEvents_ReceivedAt",
        "IX_OutboxMessages_ProcessedAt"
    ];

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task GeneratedScripts_UpgradeRollbackAndUpgradeAgain()
    {
        SqlServerIntegrationTestGate.Require();

        var artifactsDirectory = Environment.GetEnvironmentVariable(ArtifactsDirectoryVariable);
        Assert.False(
            string.IsNullOrWhiteSpace(artifactsDirectory),
            $"{ArtifactsDirectoryVariable} must point to generated migration artifacts.");

        var manifestPath = Path.Combine(artifactsDirectory, "migration-manifest.json");
        var manifest = await ReadAndVerifyManifestAsync(manifestPath);
        var forwardScriptPath = GetArtifactPath(artifactsDirectory, manifest, "migrate-up.sql");
        var rollbackScriptPath = GetArtifactPath(artifactsDirectory, manifest, "rollback-last.sql");
        var forwardScript = await File.ReadAllTextAsync(forwardScriptPath);
        var rollbackScript = await File.ReadAllTextAsync(rollbackScriptPath);

        var databaseName = $"ECommerceBackendMigrationArtifact_{Guid.NewGuid():N}";
        var connectionString =
            SqlServerIntegrationTestGate.CreateTestDatabaseConnectionString(databaseName);
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(connectionString, databaseName);
            databaseCreated = true;

            await ExecuteScriptAsync(connectionString, forwardScript);
            Assert.True(await HasMigrationAsync(connectionString, manifest.LatestMigration));
            Assert.Equal(RetentionIndexes.Length, await CountIndexesAsync(connectionString));
            Assert.Equal(7, await CountAuthenticationSchemaObjectsAsync(connectionString));

            await ExecuteScriptAsync(connectionString, rollbackScript);
            Assert.False(await HasMigrationAsync(connectionString, manifest.LatestMigration));
            Assert.True(await HasMigrationAsync(connectionString, manifest.PreviousMigration));
            Assert.Equal(RetentionIndexes.Length, await CountIndexesAsync(connectionString));
            var expectedAuthenticationObjects = string.CompareOrdinal(
                manifest.PreviousMigration,
                AuthenticationMigration) >= 0
                ? 7
                : 0;
            Assert.Equal(
                expectedAuthenticationObjects,
                await CountAuthenticationSchemaObjectsAsync(connectionString));

            await ExecuteScriptAsync(connectionString, forwardScript);
            await ExecuteScriptAsync(connectionString, forwardScript);
            Assert.True(await HasMigrationAsync(connectionString, manifest.LatestMigration));
            Assert.Equal(RetentionIndexes.Length, await CountIndexesAsync(connectionString));
            Assert.Equal(7, await CountAuthenticationSchemaObjectsAsync(connectionString));
        }
        finally
        {
            if (databaseCreated)
                await DeleteDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task GeneratedRollback_RejectsDestructiveFulfillmentData()
    {
        SqlServerIntegrationTestGate.Require();

        var artifactsDirectory =
            Environment.GetEnvironmentVariable(
                ArtifactsDirectoryVariable);
        Assert.False(
            string.IsNullOrWhiteSpace(artifactsDirectory),
            $"{ArtifactsDirectoryVariable} must point to generated migration artifacts.");

        var manifest = await ReadAndVerifyManifestAsync(
            Path.Combine(
                artifactsDirectory,
                "migration-manifest.json"));
        var forwardScript = await File.ReadAllTextAsync(
            GetArtifactPath(
                artifactsDirectory,
                manifest,
                "migrate-up.sql"));
        var rollbackScript = await File.ReadAllTextAsync(
            GetArtifactPath(
                artifactsDirectory,
                manifest,
                "rollback-last.sql"));
        var databaseName =
            $"ECommerceBackendMigrationArtifact_{Guid.NewGuid():N}";
        var connectionString =
            SqlServerIntegrationTestGate
                .CreateTestDatabaseConnectionString(databaseName);
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(
                connectionString,
                databaseName);
            databaseCreated = true;
            await ExecuteScriptAsync(
                connectionString,
                forwardScript);
            await ExecuteScriptAsync(
                connectionString,
                """
                DECLARE @UserId uniqueidentifier = NEWID();
                DECLARE @OrderId uniqueidentifier = NEWID();
                DECLARE @Now datetime2 = SYSUTCDATETIME();

                INSERT dbo.Users
                (
                    Id, UserName, NormalizedUserName, Email,
                    NormalizedEmail, PasswordHash, FullName,
                    Phone, IsDeleted, CreatedAt, PasswordChangedAt,
                    TokenVersion
                )
                VALUES
                (
                    @UserId, N'rollback.fulfillment',
                    N'ROLLBACK.FULFILLMENT',
                    N'rollback.fulfillment@example.com',
                    N'ROLLBACK.FULFILLMENT@EXAMPLE.COM',
                    N'not-used', N'Rollback Fulfillment',
                    NULL, 0, @Now, NULL, 0
                );

                INSERT dbo.Orders
                (
                    Id, UserId, OrderNumber, IdempotencyKey,
                    IdempotencyRequestHash, OrderDate,
                    SubtotalAmount, DiscountAmount, ShippingFee,
                    TaxAmount, TotalAmount, Status,
                    ShippingAddress, Note
                )
                VALUES
                (
                    @OrderId, @UserId, N'ORD-ROLLBACK-FULFILLMENT',
                    N'rollback-fulfillment',
                    REPLICATE(N'A', 64), @Now,
                    100, 0, 0, 0, 100, 1,
                    N'Rollback address', NULL
                );

                INSERT dbo.Shipments
                (
                    Id, OrderId, Carrier, TrackingNumber,
                    CreatedByUserId, ShippedAt, DeliveredAt
                )
                VALUES
                (
                    NEWID(), @OrderId, N'Rollback Carrier',
                    N'ROLLBACK-TRACKING', @UserId, @Now, NULL
                );
                """);

            var exception = await Assert.ThrowsAsync<SqlException>(
                () => ExecuteScriptAsync(
                    connectionString,
                    rollbackScript));

            Assert.Equal(51040, exception.Number);
            Assert.True(
                await HasMigrationAsync(
                    connectionString,
                    manifest.LatestMigration));
        }
        finally
        {
            if (databaseCreated)
            {
                await DeleteDatabaseAsync(
                    connectionString,
                    databaseName);
            }
        }
    }

    [Fact]
    [Trait("Category", "SqlServerRecoveryIntegration")]
    public async Task BackupRestore_RecoversLatestSchemaAndCommittedData()
    {
        SqlServerIntegrationTestGate.Require();

        var artifactsDirectory = Environment.GetEnvironmentVariable(ArtifactsDirectoryVariable);
        Assert.False(
            string.IsNullOrWhiteSpace(artifactsDirectory),
            $"{ArtifactsDirectoryVariable} must point to generated migration artifacts.");

        var backupDirectory = Environment.GetEnvironmentVariable(BackupDirectoryVariable);
        Assert.False(
            string.IsNullOrWhiteSpace(backupDirectory),
            $"{BackupDirectoryVariable} must be a directory accessible to the SQL Server process.");

        var manifest = await ReadAndVerifyManifestAsync(
            Path.Combine(artifactsDirectory, "migration-manifest.json"));
        var forwardScript = await File.ReadAllTextAsync(
            GetArtifactPath(artifactsDirectory, manifest, "migrate-up.sql"));
        var rollbackScript = await File.ReadAllTextAsync(
            GetArtifactPath(artifactsDirectory, manifest, "rollback-last.sql"));

        var databaseName = $"ECommerceBackendRecovery_{Guid.NewGuid():N}";
        var marker = Guid.NewGuid();
        var connectionString =
            SqlServerIntegrationTestGate.CreateTestDatabaseConnectionString(databaseName);
        var backupPath = BuildServerBackupPath(backupDirectory, $"{databaseName}.bak");
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(connectionString, databaseName);
            databaseCreated = true;
            await ExecuteScriptAsync(connectionString, forwardScript);
            await CreateRecoveryMarkerAsync(connectionString, marker);
            await BackupAndVerifyAsync(connectionString, databaseName, backupPath);

            await ExecuteScriptAsync(connectionString, rollbackScript);
            await DeleteRecoveryMarkerAsync(connectionString, marker);
            Assert.False(await HasMigrationAsync(connectionString, manifest.LatestMigration));
            Assert.False(await HasRecoveryMarkerAsync(connectionString, marker));

            await RestoreDatabaseAsync(connectionString, databaseName, backupPath);
            Assert.True(await HasMigrationAsync(connectionString, manifest.LatestMigration));
            Assert.True(await HasRecoveryMarkerAsync(connectionString, marker));
            Assert.Equal(RetentionIndexes.Length, await CountIndexesAsync(connectionString));
        }
        finally
        {
            if (databaseCreated)
                await DeleteDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task<MigrationManifest> ReadAndVerifyManifestAsync(string manifestPath)
    {
        Assert.True(File.Exists(manifestPath), $"Migration manifest was not found at '{manifestPath}'.");

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<MigrationManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.False(string.IsNullOrWhiteSpace(manifest.LatestMigration));
        Assert.False(string.IsNullOrWhiteSpace(manifest.PreviousMigration));
        Assert.NotEqual(manifest.LatestMigration, manifest.PreviousMigration);
        Assert.True(manifest.RequiresDatabaseBackupBeforeApply);
        Assert.True(manifest.RequiresRestoreDrillBeforeProduction);
        Assert.Equal(2, manifest.Artifacts.Count);

        var artifactsDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        foreach (var artifact in manifest.Artifacts)
        {
            var artifactPath = GetArtifactPath(artifactsDirectory, manifest, artifact.File);
            await using var artifactStream = File.OpenRead(artifactPath);
            var actualHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(artifactStream))
                .ToLowerInvariant();
            Assert.Equal(artifact.Sha256, actualHash);
        }

        return manifest;
    }

    private static string GetArtifactPath(
        string artifactsDirectory,
        MigrationManifest manifest,
        string fileName)
    {
        var artifact = Assert.Single(
            manifest.Artifacts,
            item => string.Equals(item.File, fileName, StringComparison.Ordinal));
        Assert.Matches("^[a-z0-9-]+\\.sql$", artifact.File);

        var directory = Path.GetFullPath(artifactsDirectory);
        var artifactPath = Path.GetFullPath(Path.Combine(directory, artifact.File));
        Assert.Equal(directory, Path.GetDirectoryName(artifactPath));
        Assert.True(File.Exists(artifactPath), $"Migration artifact was not found at '{artifactPath}'.");
        return artifactPath;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var masterConnectionString = BuildMasterConnectionString(connectionString);
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteDatabaseAsync(string connectionString, string databaseName)
    {
        var masterConnectionString = BuildMasterConnectionString(connectionString);
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
        command.Parameters.AddWithValue("@databaseName", databaseName);
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildMasterConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };
        return builder.ConnectionString;
    }

    private static string BuildServerBackupPath(string directory, string fileName)
    {
        Assert.DoesNotContain("..", directory, StringComparison.Ordinal);
        Assert.DoesNotContain("'", directory, StringComparison.Ordinal);
        var separator = directory.Contains('\\') ? '\\' : '/';
        return $"{directory.TrimEnd('/', '\\')}{separator}{fileName}";
    }

    private static async Task BackupAndVerifyAsync(
        string connectionString,
        string databaseName,
        string backupPath)
    {
        var masterConnectionString = BuildMasterConnectionString(connectionString);
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();

        await using (var backupCommand = connection.CreateCommand())
        {
            backupCommand.CommandText = $"""
                BACKUP DATABASE [{databaseName}]
                TO DISK = @backupPath
                WITH COPY_ONLY, INIT, CHECKSUM;
                """;
            backupCommand.Parameters.AddWithValue("@backupPath", backupPath);
            backupCommand.CommandTimeout = 120;
            await backupCommand.ExecuteNonQueryAsync();
        }

        await using var verifyCommand = connection.CreateCommand();
        verifyCommand.CommandText = """
            RESTORE VERIFYONLY
            FROM DISK = @backupPath
            WITH CHECKSUM;
            """;
        verifyCommand.Parameters.AddWithValue("@backupPath", backupPath);
        verifyCommand.CommandTimeout = 120;
        await verifyCommand.ExecuteNonQueryAsync();
    }

    private static async Task RestoreDatabaseAsync(
        string connectionString,
        string databaseName,
        string backupPath)
    {
        SqlConnection.ClearAllPools();
        var masterConnectionString = BuildMasterConnectionString(connectionString);
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            RESTORE DATABASE [{databaseName}]
            FROM DISK = @backupPath
            WITH REPLACE, RECOVERY;
            ALTER DATABASE [{databaseName}] SET MULTI_USER;
            """;
        command.Parameters.AddWithValue("@backupPath", backupPath);
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
        SqlConnection.ClearAllPools();
    }

    private static async Task CreateRecoveryMarkerAsync(
        string connectionString,
        Guid marker)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE [RecoveryMarkers]
            (
                [Id] uniqueidentifier NOT NULL
                    CONSTRAINT [PK_RecoveryMarkers] PRIMARY KEY,
                [CreatedAt] datetime2 NOT NULL
            );
            INSERT INTO [RecoveryMarkers] ([Id], [CreatedAt])
            VALUES (@marker, SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@marker", marker);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteRecoveryMarkerAsync(
        string connectionString,
        Guid marker)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM [RecoveryMarkers]
            WHERE [Id] = @marker;
            """;
        command.Parameters.AddWithValue("@marker", marker);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasRecoveryMarkerAsync(
        string connectionString,
        Guid marker)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT_BIG(1)
            FROM [RecoveryMarkers]
            WHERE [Id] = @marker;
            """;
        command.Parameters.AddWithValue("@marker", marker);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task ExecuteScriptAsync(string connectionString, string script)
    {
        var batches = Regex.Split(
            script,
            @"^\s*GO\s*(?:--.*)?$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var batch in batches.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> HasMigrationAsync(
        string connectionString,
        string migrationId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT_BIG(1)
            FROM [__EFMigrationsHistory]
            WHERE [MigrationId] = @migrationId
            """;
        command.Parameters.AddWithValue("@migrationId", migrationId);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<int> CountIndexesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM sys.indexes
            WHERE [name] IN (
                N'IX_RefreshTokens_ExpiresAt',
                N'IX_PaymentWebhookEvents_ReceivedAt',
                N'IX_OutboxMessages_ProcessedAt')
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAuthenticationSchemaObjectsAsync(
        string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(1)
                 FROM sys.tables
                 WHERE [name] = N'PasswordResetTokens'
                   AND [schema_id] = SCHEMA_ID(N'dbo'))
              + (SELECT COUNT(1)
                 FROM sys.columns
                 WHERE [object_id] = OBJECT_ID(N'dbo.Users')
                   AND [name] IN (N'FailedLoginCount', N'LockoutEndAt'))
              + (SELECT COUNT(1)
                 FROM sys.indexes
                 WHERE [object_id] = OBJECT_ID(N'dbo.PasswordResetTokens')
                   AND [name] IN (
                       N'IX_PasswordResetTokens_ExpiresAt',
                       N'IX_PasswordResetTokens_TokenHash',
                       N'UX_PasswordResetTokens_UserId_Active'))
              + (SELECT COUNT(1)
                 FROM sys.check_constraints
                 WHERE [parent_object_id] = OBJECT_ID(N'dbo.Users')
                   AND [name] = N'CK_Users_FailedLoginCount_NonNegative');
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class MigrationManifest
    {
        public string LatestMigration { get; init; } = string.Empty;
        public string PreviousMigration { get; init; } = string.Empty;
        public bool RequiresDatabaseBackupBeforeApply { get; init; }
        public bool RequiresRestoreDrillBeforeProduction { get; init; }
        public List<MigrationArtifact> Artifacts { get; init; } = [];
    }

    private sealed class MigrationArtifact
    {
        public string File { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
    }
}
