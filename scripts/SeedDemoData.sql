:setvar DatabaseName "ECommerceDB"

USE [$(DatabaseName)];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @EnvironmentName nvarchar(50) =
    UPPER(LTRIM(RTRIM(N'$(EnvironmentName)')));

IF @EnvironmentName NOT IN (N'DEVELOPMENT', N'LOCAL', N'TESTING')
    THROW 51020, 'Demo data can be seeded only in Development, Local, or Testing.', 1;

IF UPPER(DB_NAME()) LIKE N'%PROD%'
    OR UPPER(DB_NAME()) LIKE N'%PRODUCTION%'
    THROW 51021, 'Demo data cannot be seeded into a production database.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'ECommerceBackend.DemoSeed',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 10000;

    IF @LockResult < 0
        THROW 51000, 'Could not acquire the demo seed lock.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260728040145_AddFulfillmentAndReturnWorkflow'
    )
        THROW 51001, 'Apply all EF Core migrations before seeding demo data.', 1;

    DECLARE @AdminRoleId uniqueidentifier =
        (SELECT Id FROM dbo.Roles WHERE Name = N'Admin');
    DECLARE @StaffRoleId uniqueidentifier =
        (SELECT Id FROM dbo.Roles WHERE Name = N'Staff');
    DECLARE @CustomerRoleId uniqueidentifier =
        (SELECT Id FROM dbo.Roles WHERE Name = N'Customer');
    DECLARE @AdminUserId uniqueidentifier =
        (SELECT Id FROM dbo.Users WHERE NormalizedUserName = N'ADMIN' AND IsDeleted = 0);
    DECLARE @Now datetime2(7) = SYSUTCDATETIME();

    IF @AdminRoleId IS NULL OR @StaffRoleId IS NULL OR @CustomerRoleId IS NULL
        THROW 51002, 'Required roles are missing. Apply all EF Core migrations first.', 1;

    IF @AdminUserId IS NULL
        THROW 51003, 'An active admin account is required before seeding demo data.', 1;

    DECLARE @StaffUserId uniqueidentifier = 'd0000000-0000-0000-0000-000000000001';
    DECLARE @CustomerUserId uniqueidentifier = 'd0000000-0000-0000-0000-000000000002';
    DECLARE @StaffPasswordHash nvarchar(200) =
        N'$2a$11$sS0Q44YETZABCXUvnyfX.O4CsRVXfRCKpQRI7SPiGoBvi2nMFwK16';
    DECLARE @CustomerPasswordHash nvarchar(200) =
        N'$2a$11$BmbR4qnm1Dm.xvIRtBeXQeXl1xx.EOD0WcB0driR0KpAvlY5WtUDa';

    IF EXISTS
    (
        SELECT 1 FROM dbo.Users
        WHERE NormalizedUserName = N'DEMO.STAFF' AND Id <> @StaffUserId
    )
        THROW 51004, 'Username demo.staff is already used by another account.', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.Users
        WHERE NormalizedUserName = N'DEMO.CUSTOMER' AND Id <> @CustomerUserId
    )
        THROW 51005, 'Username demo.customer is already used by another account.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @StaffUserId)
    BEGIN
        INSERT dbo.Users
        (
            Id, UserName, NormalizedUserName, Email, NormalizedEmail,
            PasswordHash, FullName, Phone, IsDeleted, CreatedAt,
            PasswordChangedAt, TokenVersion
        )
        VALUES
        (
            @StaffUserId, N'demo.staff', N'DEMO.STAFF',
            N'demo.staff@ecommerce.local', N'DEMO.STAFF@ECOMMERCE.LOCAL',
            @StaffPasswordHash, N'Nhân viên Demo', N'0900000001', 0, @Now,
            @Now, 0
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @CustomerUserId)
    BEGIN
        INSERT dbo.Users
        (
            Id, UserName, NormalizedUserName, Email, NormalizedEmail,
            PasswordHash, FullName, Phone, IsDeleted, CreatedAt,
            PasswordChangedAt, TokenVersion
        )
        VALUES
        (
            @CustomerUserId, N'demo.customer', N'DEMO.CUSTOMER',
            N'demo.customer@ecommerce.local', N'DEMO.CUSTOMER@ECOMMERCE.LOCAL',
            @CustomerPasswordHash, N'Khách hàng Demo', N'0900000002', 0, @Now,
            @Now, 0
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @StaffUserId)
        INSERT dbo.UserRoles (UserId, RoleId) VALUES (@StaffUserId, @StaffRoleId);

    IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @CustomerUserId)
        INSERT dbo.UserRoles (UserId, RoleId) VALUES (@CustomerUserId, @CustomerRoleId);

    IF NOT EXISTS (SELECT 1 FROM dbo.Carts WHERE UserId = @StaffUserId)
        INSERT dbo.Carts (Id, UserId)
        VALUES ('d1000000-0000-0000-0000-000000000001', @StaffUserId);

    IF NOT EXISTS (SELECT 1 FROM dbo.Carts WHERE UserId = @CustomerUserId)
        INSERT dbo.Carts (Id, UserId)
        VALUES ('d1000000-0000-0000-0000-000000000002', @CustomerUserId);

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Promotions
        WHERE NormalizedCode = N'WELCOME10'
    )
    BEGIN
        INSERT dbo.Promotions
        (
            Id, Code, NormalizedCode, Type, Value, MinimumSubtotal,
            MaximumDiscountAmount, StartsAt, EndsAt, UsageLimit,
            UsageLimitPerCustomer, UsedCount, IsActive, CreatedAt,
            UpdatedAt
        )
        VALUES
        (
            'd2000000-0000-0000-0000-000000000001',
            N'WELCOME10', N'WELCOME10', 1, 10, 500000,
            100000, CONVERT(datetime2, '2020-01-01T00:00:00'),
            CONVERT(datetime2, '2099-12-31T23:59:59'),
            1000, 1, 0, 1, @Now, NULL
        );
    END;

    DECLARE @Categories table
    (
        Id uniqueidentifier NOT NULL,
        Name nvarchar(100) NOT NULL,
        NormalizedName nvarchar(100) NOT NULL,
        ParentId uniqueidentifier NULL
    );

    INSERT @Categories (Id, Name, NormalizedName, ParentId)
    VALUES
        ('c0000000-0000-0000-0000-000000000001', N'Điện tử', N'ĐIỆN TỬ', NULL),
        ('c0000000-0000-0000-0000-000000000002', N'Điện thoại', N'ĐIỆN THOẠI', 'c0000000-0000-0000-0000-000000000001'),
        ('c0000000-0000-0000-0000-000000000003', N'Máy tính xách tay', N'MÁY TÍNH XÁCH TAY', 'c0000000-0000-0000-0000-000000000001'),
        ('c0000000-0000-0000-0000-000000000004', N'Phụ kiện', N'PHỤ KIỆN', 'c0000000-0000-0000-0000-000000000001'),
        ('c0000000-0000-0000-0000-000000000005', N'Gia dụng', N'GIA DỤNG', NULL),
        ('c0000000-0000-0000-0000-000000000006', N'Nhà bếp', N'NHÀ BẾP', 'c0000000-0000-0000-0000-000000000005');

    IF EXISTS
    (
        SELECT 1
        FROM @Categories seed
        JOIN dbo.Categories category
            ON category.IsDeleted = 0
            AND category.NormalizedName = seed.NormalizedName
            AND
            (
                (category.ParentId IS NULL AND seed.ParentId IS NULL)
                OR category.ParentId = seed.ParentId
            )
        WHERE category.Id <> seed.Id
    )
        THROW 51006, 'A demo category name is already used by another category.', 1;

    INSERT dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    SELECT seed.Id, seed.Name, seed.NormalizedName, seed.ParentId, 0
    FROM @Categories seed
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Categories category WHERE category.Id = seed.Id);

    DECLARE @Products table
    (
        Id uniqueidentifier NOT NULL,
        InventoryId uniqueidentifier NOT NULL,
        CategoryId uniqueidentifier NOT NULL,
        Name nvarchar(200) NOT NULL,
        Price decimal(18, 2) NOT NULL,
        StockQuantity int NOT NULL,
        Description nvarchar(2000) NOT NULL,
        CreatedAt datetime2(7) NOT NULL
    );

    INSERT @Products
    (
        Id, InventoryId, CategoryId, Name, Price,
        StockQuantity, Description, CreatedAt
    )
    VALUES
        ('b0000000-0000-0000-0000-000000000001', 'e0000000-0000-0000-0000-000000000001', 'c0000000-0000-0000-0000-000000000002', N'Samsung Galaxy S24', 18990000.00, 25, N'Điện thoại Android cao cấp, màn hình AMOLED 6.2 inch và bộ nhớ 256 GB.', DATEADD(minute, -10, @Now)),
        ('b0000000-0000-0000-0000-000000000002', 'e0000000-0000-0000-0000-000000000002', 'c0000000-0000-0000-0000-000000000002', N'iPhone 15', 19990000.00, 20, N'Điện thoại Apple với màn hình Super Retina XDR 6.1 inch và bộ nhớ 128 GB.', DATEADD(minute, -9, @Now)),
        ('b0000000-0000-0000-0000-000000000003', 'e0000000-0000-0000-0000-000000000003', 'c0000000-0000-0000-0000-000000000002', N'Xiaomi Redmi Note 13', 5490000.00, 40, N'Điện thoại tầm trung, màn hình AMOLED 120 Hz và camera chính 108 MP.', DATEADD(minute, -8, @Now)),
        ('b0000000-0000-0000-0000-000000000004', 'e0000000-0000-0000-0000-000000000004', 'c0000000-0000-0000-0000-000000000003', N'MacBook Air M3', 27490000.00, 12, N'Laptop mỏng nhẹ 13 inch, chip Apple M3, RAM 8 GB và SSD 256 GB.', DATEADD(minute, -7, @Now)),
        ('b0000000-0000-0000-0000-000000000005', 'e0000000-0000-0000-0000-000000000005', 'c0000000-0000-0000-0000-000000000003', N'Dell Inspiron 14', 16990000.00, 18, N'Laptop văn phòng 14 inch, Intel Core i5, RAM 16 GB và SSD 512 GB.', DATEADD(minute, -6, @Now)),
        ('b0000000-0000-0000-0000-000000000006', 'e0000000-0000-0000-0000-000000000006', 'c0000000-0000-0000-0000-000000000003', N'ASUS Vivobook 15', 14490000.00, 15, N'Laptop 15.6 inch cho học tập và công việc, RAM 16 GB và SSD 512 GB.', DATEADD(minute, -5, @Now)),
        ('b0000000-0000-0000-0000-000000000007', 'e0000000-0000-0000-0000-000000000007', 'c0000000-0000-0000-0000-000000000004', N'Chuột Logitech MX Anywhere 3S', 1890000.00, 5, N'Chuột không dây nhỏ gọn, cảm biến chính xác và kết nối đa thiết bị.', DATEADD(minute, -4, @Now)),
        ('b0000000-0000-0000-0000-000000000008', 'e0000000-0000-0000-0000-000000000008', 'c0000000-0000-0000-0000-000000000004', N'Tai nghe Sony WH-CH720N', 2490000.00, 8, N'Tai nghe Bluetooth chụp tai có chống ồn chủ động và pin dài.', DATEADD(minute, -3, @Now)),
        ('b0000000-0000-0000-0000-000000000009', 'e0000000-0000-0000-0000-000000000009', 'c0000000-0000-0000-0000-000000000006', N'Nồi chiên không dầu Philips HD9252', 2390000.00, 30, N'Nồi chiên dung tích 4.1 lít, điều khiển điện tử và công nghệ Rapid Air.', DATEADD(minute, -2, @Now)),
        ('b0000000-0000-0000-0000-000000000010', 'e0000000-0000-0000-0000-000000000010', 'c0000000-0000-0000-0000-000000000006', N'Nồi cơm điện Panasonic SR-MVN187', 1790000.00, 22, N'Nồi cơm điện dung tích 1.8 lít với lòng nồi chống dính và giữ ấm.', DATEADD(minute, -1, @Now));

    INSERT dbo.Products
    (
        Id, CategoryId, Name, Price, StockQuantity,
        Description, IsDeleted, CreatedAt
    )
    SELECT
        seed.Id, seed.CategoryId, seed.Name, seed.Price, seed.StockQuantity,
        seed.Description, 0, seed.CreatedAt
    FROM @Products seed
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Products product WHERE product.Id = seed.Id);

    INSERT dbo.InventoryTransactions
    (
        Id, ProductId, OrderId, CreatedByUserId, Type,
        QuantityChange, BalanceAfter, Reason, CreatedAt
    )
    SELECT
        seed.InventoryId, product.Id, NULL, @AdminUserId, 0,
        product.StockQuantity, product.StockQuantity,
        N'Tồn kho ban đầu từ dữ liệu mẫu', seed.CreatedAt
    FROM @Products seed
    JOIN dbo.Products product ON product.Id = seed.Id
    WHERE product.StockQuantity > 0
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.InventoryTransactions transactionRecord
          WHERE transactionRecord.Id = seed.InventoryId
      );

    COMMIT TRANSACTION;

    SELECT N'Users' AS Entity, COUNT(*) AS SeededCount
    FROM dbo.Users WHERE Id IN (@StaffUserId, @CustomerUserId)
    UNION ALL
    SELECT N'Categories', COUNT(*)
    FROM dbo.Categories WHERE Id IN (SELECT Id FROM @Categories)
    UNION ALL
    SELECT N'Products', COUNT(*)
    FROM dbo.Products WHERE Id IN (SELECT Id FROM @Products)
    UNION ALL
    SELECT N'InventoryTransactions', COUNT(*)
    FROM dbo.InventoryTransactions WHERE Id IN (SELECT InventoryId FROM @Products);
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
