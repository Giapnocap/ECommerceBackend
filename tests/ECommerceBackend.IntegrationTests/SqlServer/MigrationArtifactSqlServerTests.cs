using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
    public async Task OrderRecipientRollback_RejectsDestructiveSnapshotData()
    {
        SqlServerIntegrationTestGate.Require();
        var databaseName =
            $"ECommerceBackendMigrationArtifact_{Guid.NewGuid():N}";
        var connectionString =
            SqlServerIntegrationTestGate
                .CreateTestDatabaseConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using var context = new AppDbContext(options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync();
            await migrator.MigrateAsync(
                "20260806114811_AddOrderRecipientSnapshot");
            await context.Database.ExecuteSqlRawAsync(
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
                    @UserId, N'rollback.snapshot',
                    N'ROLLBACK.SNAPSHOT',
                    N'rollback.snapshot@example.com',
                    N'ROLLBACK.SNAPSHOT@EXAMPLE.COM',
                    N'not-used', N'Rollback Snapshot',
                    NULL, 0, @Now, NULL, 0
                );

                INSERT dbo.Orders
                (
                    Id, UserId, OrderNumber, IdempotencyKey,
                    IdempotencyRequestHash, OrderDate,
                    SubtotalAmount, DiscountAmount, ShippingFee,
                    TaxAmount, TotalAmount, Status,
                    RecipientName, RecipientPhone,
                    ShippingAddress, Note
                )
                VALUES
                (
                    @OrderId, @UserId, N'ORD-ROLLBACK-SNAPSHOT',
                    N'rollback-snapshot',
                    REPLICATE(N'A', 64), @Now,
                    100, 0, 0, 0, 100, 1,
                    N'Rollback Snapshot', N'0900000000',
                    N'Rollback address', NULL
                );
                """);

            var exception = await Assert.ThrowsAsync<SqlException>(
                () => migrator.MigrateAsync(
                    "20260728040145_AddFulfillmentAndReturnWorkflow"));

            Assert.Equal(51041, exception.Number);
            Assert.True(
                await HasMigrationAsync(
                    connectionString,
                    "20260806114811_AddOrderRecipientSnapshot"));
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
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
            var fixture = await CreateCriticalRecoveryFixtureAsync(
                connectionString,
                marker);
            await BackupAndVerifyAsync(connectionString, databaseName, backupPath);

            await ExecuteScriptAsync(connectionString, rollbackScript);
            await DeleteCriticalRecoveryDataAsync(
                connectionString,
                fixture);
            Assert.False(await HasMigrationAsync(connectionString, manifest.LatestMigration));
            await AssertCriticalRecoveryDataMissingAsync(
                connectionString,
                fixture);

            await RestoreDatabaseAsync(connectionString, databaseName, backupPath);
            Assert.True(await HasMigrationAsync(connectionString, manifest.LatestMigration));
            Assert.Equal(RetentionIndexes.Length, await CountIndexesAsync(connectionString));
            await AssertCriticalRecoveryDataAsync(
                connectionString,
                fixture);
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

    private static async Task<RecoveryFixture> CreateCriticalRecoveryFixtureAsync(
        string connectionString,
        Guid marker)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var occurredAt = DateTime.UtcNow.AddMinutes(-1);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"recovery_{Guid.NewGuid():N}"[..32],
            Email = $"recovery_{Guid.NewGuid():N}@example.com",
            PasswordHash = "recovery-test-hash",
            FullName = "Recovery Verification User",
            CreatedAt = occurredAt
        };
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.NormalizedEmail = user.Email.ToUpperInvariant();

        var category = Category.Create(
            Guid.NewGuid(),
            $"Recovery {Guid.NewGuid():N}"[..36],
            parent: null);
        var product = Product.Create(
            Guid.NewGuid(),
            category.Id,
            "Recovery Snapshot Product",
            250_000m,
            4,
            "Critical backup and restore fixture",
            occurredAt);
        var order = Order.Create(
            Guid.NewGuid(),
            user.Id,
            $"ORD-{Guid.NewGuid():N}"[..32],
            Guid.NewGuid().ToString("N"),
            new string('A', 64),
            promotionId: null,
            promotionCodeSnapshot: null,
            ShippingMethod.Standard,
            "USD",
            occurredAt,
            "1 Recovery Verification Street",
            note: null);
        order.SetRecipient("Recovery Verification User", "0900000000");
        var baseAmounts = OrderPricingPolicy.CalculateAmounts(
            250_000m,
            0m,
            0m,
            0m);
        var displayAmounts = OrderPricingPolicy.CalculateAmounts(
            10m,
            0m,
            0m,
            0m);
        const decimal exchangeRate = 0.00004m;
        order.SetPricingSnapshot(
            "VND",
            exchangeRate,
            occurredAt,
            baseAmounts,
            displayAmounts);

        var payment = Payment.Create(
            Guid.NewGuid(),
            order.Id,
            PaymentMethod.Card,
            displayAmounts.Total,
            "stripe",
            $"pi_recovery_{Guid.NewGuid():N}",
            occurredAt,
            order.Currency);
        var detail = OrderDetail.Create(
            Guid.NewGuid(),
            order.Id,
            product.Id,
            product.Name,
            1,
            displayAmounts.Subtotal,
            baseAmounts.Subtotal);
        var inventoryTransaction = InventoryTransaction.Create(
            Guid.NewGuid(),
            product.Id,
            order.Id,
            user.Id,
            InventoryTransactionType.OrderPlaced,
            new InventoryMutation(-1, product.StockQuantity),
            "Recovery verification reservation",
            occurredAt,
            order.OrderNumber);
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "recovery.verification",
            Payload = "{\"scope\":\"critical-data\"}",
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt
        };
        var auditEvent = new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            Action = "RecoveryVerificationCreated",
            EntityType = nameof(Order),
            EntityId = order.Id.ToString("D"),
            CorrelationId = $"recovery-{marker:N}",
            MetadataJson = "{\"currency\":\"USD\",\"baseCurrency\":\"VND\"}",
            CreatedAt = occurredAt
        };

        await using (var context = new AppDbContext(options))
        {
            context.AddRange(
                user,
                category,
                product,
                order,
                detail,
                payment,
                inventoryTransaction,
                outboxMessage,
                auditEvent);
            await context.SaveChangesAsync();
        }

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

        return new RecoveryFixture(
            marker,
            user.Id,
            category.Id,
            product.Id,
            order.Id,
            detail.Id,
            payment.Id,
            inventoryTransaction.Id,
            outboxMessage.Id,
            auditEvent.Id,
            exchangeRate);
    }

    private static async Task DeleteCriticalRecoveryDataAsync(
        string connectionString,
        RecoveryFixture fixture)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM [InventoryTransactions] WHERE [Id] = @inventoryTransactionId;
            DELETE FROM [Payments] WHERE [Id] = @paymentId;
            DELETE FROM [OrderDetails] WHERE [Id] = @orderDetailId;
            DELETE FROM [Orders] WHERE [Id] = @orderId;
            DELETE FROM [Products] WHERE [Id] = @productId;
            DELETE FROM [Categories] WHERE [Id] = @categoryId;
            DELETE FROM [OutboxMessages] WHERE [Id] = @outboxMessageId;
            DELETE FROM [AuditEvents] WHERE [Id] = @auditEventId;
            DELETE FROM [Users] WHERE [Id] = @userId;
            DELETE FROM [RecoveryMarkers] WHERE [Id] = @marker;
            """;
        command.Parameters.AddWithValue(
            "@inventoryTransactionId",
            fixture.InventoryTransactionId);
        command.Parameters.AddWithValue("@paymentId", fixture.PaymentId);
        command.Parameters.AddWithValue("@orderDetailId", fixture.OrderDetailId);
        command.Parameters.AddWithValue("@orderId", fixture.OrderId);
        command.Parameters.AddWithValue("@productId", fixture.ProductId);
        command.Parameters.AddWithValue("@categoryId", fixture.CategoryId);
        command.Parameters.AddWithValue("@outboxMessageId", fixture.OutboxMessageId);
        command.Parameters.AddWithValue("@auditEventId", fixture.AuditEventId);
        command.Parameters.AddWithValue("@userId", fixture.UserId);
        command.Parameters.AddWithValue("@marker", fixture.Marker);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertCriticalRecoveryDataMissingAsync(
        string connectionString,
        RecoveryFixture fixture)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new AppDbContext(options);

        Assert.False(await context.Users.AnyAsync(item => item.Id == fixture.UserId));
        Assert.False(await context.Orders.AnyAsync(item => item.Id == fixture.OrderId));
        Assert.False(await context.Payments.AnyAsync(item => item.Id == fixture.PaymentId));
        Assert.False(await context.InventoryTransactions.AnyAsync(
            item => item.Id == fixture.InventoryTransactionId));
        Assert.False(await context.OutboxMessages.AnyAsync(
            item => item.Id == fixture.OutboxMessageId));
        Assert.False(await context.AuditEvents.AnyAsync(
            item => item.Id == fixture.AuditEventId));
        Assert.False(await HasRecoveryMarkerAsync(connectionString, fixture.Marker));
    }

    private static async Task AssertCriticalRecoveryDataAsync(
        string connectionString,
        RecoveryFixture fixture)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new AppDbContext(options);

        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.UserId);
        Assert.Equal("Recovery Verification User", user.FullName);

        var order = await context.Orders
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.OrderId);
        Assert.Equal("USD", order.Currency);
        Assert.Equal("VND", order.BaseCurrency);
        Assert.Equal(fixture.ExchangeRate, order.ExchangeRate);
        Assert.Equal(10m, order.TotalAmount);
        Assert.Equal(250_000m, order.BaseTotalAmount);

        var detail = await context.OrderDetails
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.OrderDetailId);
        Assert.Equal(10m, detail.UnitPrice);
        Assert.Equal(250_000m, detail.BaseUnitPrice);

        var payment = await context.Payments
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.PaymentId);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal(10m, payment.Amount);
        Assert.Equal("stripe", payment.Provider);

        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.ProductId);
        Assert.Equal(4, product.StockQuantity);

        var inventoryTransaction = await context.InventoryTransactions
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.InventoryTransactionId);
        Assert.Equal(-1, inventoryTransaction.QuantityChange);
        Assert.Equal(4, inventoryTransaction.BalanceAfter);
        Assert.Equal(fixture.OrderId, inventoryTransaction.OrderId);

        var outboxMessage = await context.OutboxMessages
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.OutboxMessageId);
        Assert.Equal("recovery.verification", outboxMessage.Type);

        var auditEvent = await context.AuditEvents
            .AsNoTracking()
            .SingleAsync(item => item.Id == fixture.AuditEventId);
        Assert.Equal("RecoveryVerificationCreated", auditEvent.Action);
        Assert.Equal(fixture.OrderId.ToString("D"), auditEvent.EntityId);

        Assert.True(await HasRecoveryMarkerAsync(connectionString, fixture.Marker));
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

    private sealed record RecoveryFixture(
        Guid Marker,
        Guid UserId,
        Guid CategoryId,
        Guid ProductId,
        Guid OrderId,
        Guid OrderDetailId,
        Guid PaymentId,
        Guid InventoryTransactionId,
        Guid OutboxMessageId,
        Guid AuditEventId,
        decimal ExchangeRate);
}
