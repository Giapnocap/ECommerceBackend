USE [ECommerceDB];
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
DECLARE @Now datetime2(7) = SYSUTCDATETIME();
DECLARE @AdminId uniqueidentifier = '99999999-9999-9999-9999-999999999999';

-- 1. Thêm danh mục mới nếu chưa có

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = 'c0000000-0000-0000-0000-000000000010')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    VALUES ('c0000000-0000-0000-0000-000000000010', N'Rau củ & Trái cây', N'RAU CỦ & TRÁI CÂY', NULL, 0);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = 'c0000000-0000-0000-0000-000000000020')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    VALUES ('c0000000-0000-0000-0000-000000000020', N'Thịt, Trứng & Hải sản', N'THỊT, TRỨNG & HẢI SẢN', NULL, 0);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = 'c0000000-0000-0000-0000-000000000030')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    VALUES ('c0000000-0000-0000-0000-000000000030', N'Sữa & Sản phẩm từ sữa', N'SỮA & SẢN PHẨM TỪ SỮA', NULL, 0);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = 'c0000000-0000-0000-0000-000000000040')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    VALUES ('c0000000-0000-0000-0000-000000000040', N'Bánh kẹo & Đồ ăn vặt', N'BÁNH KẸO & ĐỒ ĂN VẶT', NULL, 0);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = 'c0000000-0000-0000-0000-000000000050')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    VALUES ('c0000000-0000-0000-0000-000000000050', N'Đồ uống & Giải khát', N'ĐỒ UỐNG & GIẢI KHÁT', NULL, 0);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = 'c0000000-0000-0000-0000-000000000060')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    VALUES ('c0000000-0000-0000-0000-000000000060', N'Mì, Gạo & Thực phẩm khô', N'MÌ, GẠO & THỰC PHẨM KHÔ', NULL, 0);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = 'c0000000-0000-0000-0000-000000000070')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, NormalizedName, ParentId, IsDeleted)
    VALUES ('c0000000-0000-0000-0000-000000000070', N'Chăm sóc cá nhân & Nhà cửa', N'CHĂM SÓC CÁ NHÂN & NHÀ CỬA', NULL, 0);
END;


-- 2. Cập nhật ảnh cho các sản phẩm điện tử hiện có

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000001', N'/uploads/products/samsung_galaxy_s24.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000002', N'/uploads/products/iphone_15.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000003')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000003', N'/uploads/products/xiaomi_redmi_note_13.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000004')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000004', N'/uploads/products/macbook_air_m3.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000005', N'/uploads/products/dell_inspiron_14.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000006')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000006', N'/uploads/products/asus_vivobook_15.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000007')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000007', N'/uploads/products/mouse_logitech_mx.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000008')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000008', N'/uploads/products/headphone_sony_wh.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ProductId = 'b0000000-0000-0000-0000-000000000009')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b0000000-0000-0000-0000-000000000009', N'/uploads/products/air_fryer_philips.jpg', 1);
END;


-- 3. Thêm các sản phẩm mới, tồn kho và ảnh

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000001', 'c0000000-0000-0000-0000-000000000010', N'Táo Envy New Zealand nhập khẩu (kg)', 139000, 65, 10, N'Táo Envy nhập khẩu chính ngạch từ New Zealand, giòn ngọt mọng nước, vỏ đỏ sọc vàng tự nhiên, giàu vitamin C và chất chống oxy hóa.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('ee7261dc-a0fe-4441-8a64-444808641653', 'b1000000-0000-0000-0000-000000000001', NULL, @AdminId, 0, 65, 65, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000001', N'/uploads/products/apple_envy_nz.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000002', 'c0000000-0000-0000-0000-000000000010', N'Cam sành Hàm Yên mọng nước (kg)', 35000, 80, 10, N'Cam sành đặc sản Hàm Yên vỏ mỏng, mọng nước, vị ngọt thanh tự nhiên rất thích hợp vắt nước uống hàng ngày tăng cường sức đề kháng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('f3451745-75e5-4c5a-a240-25b032f4fe47', 'b1000000-0000-0000-0000-000000000002', NULL, @AdminId, 0, 80, 80, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000002', N'/uploads/products/orange_hamyen.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000003')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000003', 'c0000000-0000-0000-0000-000000000010', N'Chuối tiêu sạch Nam Mỹ (nải ~1.2kg)', 28000, 50, 10, N'Chuối tiêu sạch quả to đều, chín vàng tự nhiên, dẻo thơm giàu kali và khoáng chất, bổ sung năng lượng tuyệt vời cho cả gia đình.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('9441710b-0145-4e2a-996b-16b71ea0ab4b', 'b1000000-0000-0000-0000-000000000003', NULL, @AdminId, 0, 50, 50, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000003', N'/uploads/products/banana_fresh.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000004')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000004', 'c0000000-0000-0000-0000-000000000010', N'Nho đen không hạt ngón tay Mỹ (hộp 500g)', 125000, 45, 10, N'Nho đen không hạt dáng ngón tay nhập khẩu từ Mỹ, vị ngọt đậm đà, cuống xanh tươi giòn rụm, đóng hộp sang trọng tiện lợi.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('1ec754d4-6225-4a7e-9482-bf215b7c2564', 'b1000000-0000-0000-0000-000000000004', NULL, @AdminId, 0, 45, 45, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000004', N'/uploads/products/grape_black.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000005', 'c0000000-0000-0000-0000-000000000010', N'Cà chua bi beef Đà Lạt VietGAP (hộp 500g)', 32000, 70, 10, N'Cà chua bi VietGAP trồng tại Đà Lạt, màu đỏ tươi mọng quả, vị chua ngọt hài hòa, dùng ăn sống hoặc làm salad cực ngon.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('c4a5f5e4-d2a5-4887-84ed-21552fc98aa9', 'b1000000-0000-0000-0000-000000000005', NULL, @AdminId, 0, 70, 70, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000005', N'/uploads/products/tomato_dalat.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000006')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000006', 'c0000000-0000-0000-0000-000000000010', N'Xà lách xoăn thủy canh tươi sạch (túi 300g)', 22000, 60, 10, N'Xà lách xoăn trồng theo công nghệ thủy canh khép kín, lá xanh non mướt, không thuốc trừ sâu, giòn ngọt thanh mát.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('857e49ab-e570-4b69-b60f-db2bbdf615d5', 'b1000000-0000-0000-0000-000000000006', NULL, @AdminId, 0, 60, 60, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000006', N'/uploads/products/lettuce_green.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000007')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000007', 'c0000000-0000-0000-0000-000000000020', N'Ba chỉ bò Úc cuộn khay 500g', 165000, 55, 10, N'Ba chỉ bò Úc hảo hạng thái lát mỏng cuộn tròn đều đẹp, tỷ lệ nạc mỡ cân bằng, mềm ngọt tan trong miệng khi nhúng lẩu hoặc nướng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('7e5eb257-dc72-4313-a100-802c8763829b', 'b1000000-0000-0000-0000-000000000007', NULL, @AdminId, 0, 55, 55, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000007', N'/uploads/products/beef_slice.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000008')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000008', 'c0000000-0000-0000-0000-000000000020', N'Sườn non heo sạch MeatDeli khay 500g', 149000, 40, 10, N'Sườn non heo sạch chuẩn công nghệ thịt mát châu Âu MeatDeli, thịt hồng tươi nhiều sụn non, nấu canh hoặc làm sườn xào chua ngọt tuyệt hảo.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('02bae6d8-d315-49c2-af17-f1ebb9b9ba8f', 'b1000000-0000-0000-0000-000000000008', NULL, @AdminId, 0, 40, 40, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000008', N'/uploads/products/pork_ribs.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000009')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000009', 'c0000000-0000-0000-0000-000000000020', N'Thịt nạc dăm heo MeatDeli khay 400g', 89000, 45, 10, N'Nạc dăm heo sạch MeatDeli tươi ngon mềm mọng, vân mỡ xen kẽ đều giúp thịt không bị khô khi kho, rim hoặc làm thịt nướng thơm lừng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('708c2386-51d1-487e-a815-49bcf218c157', 'b1000000-0000-0000-0000-000000000009', NULL, @AdminId, 0, 45, 45, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000009', N'/uploads/products/pork_meat.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000010')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000010', 'c0000000-0000-0000-0000-000000000020', N'Đùi gà góc tư tươi sạch CP khay 500g', 49000, 60, 10, N'Đùi gà tươi CP nguồn gốc rõ ràng, thịt chắc dai ngọt, da vàng ươm tự nhiên, làm món gà rán hoặc nướng mật ong rất ngon.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('916502fd-2381-40ad-9cc0-5a4a2345b03a', 'b1000000-0000-0000-0000-000000000010', NULL, @AdminId, 0, 60, 60, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000010', N'/uploads/products/chicken_thigh.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000011', 'c0000000-0000-0000-0000-000000000020', N'Cá hồi Na Uy fillet tươi ngon (miếng 300g)', 195000, 35, 10, N'Fillet cá hồi tươi Na Uy loại 1 nhập khẩu đường hàng không, màu cam đỏ tự nhiên, giàu Omega-3 và dinh dưỡng quý giá.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('04e30052-7851-4ba7-a2bc-82a9849143a0', 'b1000000-0000-0000-0000-000000000011', NULL, @AdminId, 0, 35, 35, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000011', N'/uploads/products/salmon_fillet.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000012')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000012', 'c0000000-0000-0000-0000-000000000020', N'Trứng gà ta tươi sạch Ba Huân (hộp 10 quả)', 34000, 100, 10, N'Trứng gà ta Ba Huân kiểm dịch nghiêm ngặt, vỏ sạch bóng lòng đỏ đậm đà béo ngậy, cung cấp protein dồi dào cho bữa ăn gia đình.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('a686029a-d323-4803-99cf-64ce67d8d34b', 'b1000000-0000-0000-0000-000000000012', NULL, @AdminId, 0, 100, 100, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000012', N'/uploads/products/fresh_eggs.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000013')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000013', 'c0000000-0000-0000-0000-000000000030', N'Sữa tươi tiệt trùng Vinamilk 100% ít đường 1L', 37000, 90, 10, N'Sữa tươi tiệt trùng Vinamilk từ 100% sữa bò tươi nguyên chất, vị béo dịu ít ngọt dễ uống, bổ sung canxi và khoáng chất thiết yếu.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('c4265adc-e4e8-4641-bfcc-322b66c5a90e', 'b1000000-0000-0000-0000-000000000013', NULL, @AdminId, 0, 90, 90, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000013', N'/uploads/products/milk_vinamilk.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000014')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000014', 'c0000000-0000-0000-0000-000000000030', N'Sữa tươi nguyên chất TH true MILK lốc 4 hộp x 180ml', 38000, 85, 10, N'Sữa tươi sạch TH true MILK làm hoàn toàn từ sữa tươi sạch của trang trại TH, thanh khiết nguyên chất không chất bảo quản.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('70351ab4-09ed-49b8-9e1c-4f59297d5163', 'b1000000-0000-0000-0000-000000000014', NULL, @AdminId, 0, 85, 85, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000014', N'/uploads/products/milk_thtruemilk.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000015')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000015', 'c0000000-0000-0000-0000-000000000030', N'Sữa chua uống men sống Probi Vinamilk lốc 5 chai', 27000, 75, 10, N'Sữa chua uống Probi chứa 65 tỷ lợi khuẩn L.Casei 431 chuẩn châu Âu, hỗ trợ hệ tiêu hóa khỏe mạnh và tăng đề kháng vượt trội.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('f72772eb-4084-4db7-b581-3473b8170947', 'b1000000-0000-0000-0000-000000000015', NULL, @AdminId, 0, 75, 75, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000015', N'/uploads/products/yogurt_probi.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000016')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000016', 'c0000000-0000-0000-0000-000000000030', N'Sữa chua ăn có đường TH true YOGURT lốc 4 hộp', 31000, 70, 10, N'Sữa chua ăn lên men tự nhiên từ sữa tươi sạch TH, kết cấu mịn màng vị chua ngọt thanh mát, món tráng miệng bổ dưỡng hàng ngày.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('7e373b7e-22a6-4345-921a-c53c1551347c', 'b1000000-0000-0000-0000-000000000016', NULL, @AdminId, 0, 70, 70, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000016', N'/uploads/products/yogurt_cup.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000017')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000017', 'c0000000-0000-0000-0000-000000000030', N'Phô mai lát Cheddar Anchor gói 200g (12 lát)', 68000, 50, 10, N'Phô mai Cheddar Anchor nhập khẩu New Zealand, béo ngậy thơm ngon, kẹp bánh mì sandwich hoặc burger cực kỳ hấp dẫn.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('cc9b2be6-f49f-4336-a8e8-1735c7dda7d3', 'b1000000-0000-0000-0000-000000000017', NULL, @AdminId, 0, 50, 50, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000017', N'/uploads/products/cheese_cheddar.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000018')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000018', 'c0000000-0000-0000-0000-000000000040', N'Bánh quy sô cô la Oreo kẹp kem vani gói 133g', 18000, 120, 10, N'Bánh quy socola kẹp kem sữa vani trứ danh, lớp bánh giòn tan kết hợp kem ngọt thanh, nhúng sữa ăn cực đã.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('76ba9ac3-c5bb-499c-a19e-6dde56ff4ec1', 'b1000000-0000-0000-0000-000000000018', NULL, @AdminId, 0, 120, 120, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000018', N'/uploads/products/cookie_oreo.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000019')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000019', 'c0000000-0000-0000-0000-000000000040', N'Snack khoai tây Lay''s vị tự nhiên gói 95g', 21000, 90, 10, N'Snack khoai tây chiên lát vàng rụm từ khoai tây tươi chọn lọc, vị muối biển tự nhiên giòn tan thơm lừng trong từng miếng cắn.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('e0341a3a-84f9-4490-8fc9-db5d87c1d508', 'b1000000-0000-0000-0000-000000000019', NULL, @AdminId, 0, 90, 90, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000019', N'/uploads/products/snack_lays.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000020')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000020', 'c0000000-0000-0000-0000-000000000040', N'Bánh Chocopie Orion truyền thống hộp 12 cái', 58000, 80, 10, N'Bánh Chocopie phủ socola thơm lừng bên ngoài, kẹp lớp marshmallow dẻo xốp ngọt ngào bên trong, món quà ngọt ngào gắn kết gia đình.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('47902ee5-a052-4563-b375-486063088b15', 'b1000000-0000-0000-0000-000000000020', NULL, @AdminId, 0, 80, 80, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000020', N'/uploads/products/chocopie_orion.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000021')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000021', 'c0000000-0000-0000-0000-000000000040', N'Hạt điều rang muối vỏ lụa Bình Phước hũ 500g', 145000, 60, 10, N'Hạt điều Bình Phước hạt to tròn loại 1, rang củi muối tinh giữ trọn độ béo ngậy giòn ngọt tự nhiên, nhâm nhi đãi khách cực sang trọng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('8ff1c033-d08c-4991-b84b-0970b9fe265e', 'b1000000-0000-0000-0000-000000000021', NULL, @AdminId, 0, 60, 60, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000021', N'/uploads/products/cashew_nuts.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000022')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000022', 'c0000000-0000-0000-0000-000000000040', N'Kẹo dẻo Chupa Chups hương trái cây gói 100g', 15000, 100, 10, N'Kẹo dẻo Chupa Chups nhiều hình thù ngộ nghĩnh, hương vị trái cây tự nhiên chua chua ngọt ngọt, trẻ nhỏ và người lớn đều mê.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('9c0c69a2-3bee-4696-be0b-f7f57bed3178', 'b1000000-0000-0000-0000-000000000022', NULL, @AdminId, 0, 100, 100, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000022', N'/uploads/products/candy_gummy.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000023')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000023', 'c0000000-0000-0000-0000-000000000050', N'Nước ngọt Coca-Cola lốc 6 lon x 320ml', 54000, 110, 10, N'Nước giải khát có ga Coca-Cola sảng khoái mát lạnh, thổi bùng hứng khởi, kết hợp tuyệt vời trong mọi bữa tiệc sum họp.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('dfacba73-ded4-473b-a221-88dc71fc5d66', 'b1000000-0000-0000-0000-000000000023', NULL, @AdminId, 0, 110, 110, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000023', N'/uploads/products/cocacola_can.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000024')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000024', 'c0000000-0000-0000-0000-000000000050', N'Nước ngọt Pepsi không calo lốc 6 lon x 320ml', 52000, 95, 10, N'Pepsi không calo sảng khoái với vị chanh sành điệu, không lo tăng cân, thức uống yêu thích của lối sống hiện đại năng động.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('da828681-5213-4b4c-8683-c326f37f347a', 'b1000000-0000-0000-0000-000000000024', NULL, @AdminId, 0, 95, 95, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000024', N'/uploads/products/pepsi_can.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000025')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000025', 'c0000000-0000-0000-0000-000000000050', N'Trà xanh Không Độ hương chanh lốc 6 chai x 455ml', 55000, 85, 10, N'Trà xanh Không Độ chiết xuất từ búp trà xanh Thái Nguyên, giàu chất chống oxy hóa EGCG, hương chanh tươi mát giải nhiệt tức thì.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('cb0fd203-c4f3-4ef5-a3de-0d2430745882', 'b1000000-0000-0000-0000-000000000025', NULL, @AdminId, 0, 85, 85, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000025', N'/uploads/products/green_tea.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000026')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000026', 'c0000000-0000-0000-0000-000000000050', N'Nước khoáng thiên nhiên La Vie bình 5L', 28000, 100, 10, N'Nước khoáng tự nhiên La Vie thanh khiết tích tụ qua ngàn năm dưới lòng đất, bổ sung vi khoáng nhẹ cho cơ thể tràn đầy sức sống.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('df0d63e7-fbd5-4798-8ba5-db027ad2c4ef', 'b1000000-0000-0000-0000-000000000026', NULL, @AdminId, 0, 100, 100, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000026', N'/uploads/products/water_lavie.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000027')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000027', 'c0000000-0000-0000-0000-000000000050', N'Cà phê sữa hòa tan G7 3in1 Trung Nguyên hộp 18 gói', 59000, 90, 10, N'Cà phê sữa G7 thứ thiệt từ hạt cà phê Robusta Buôn Ma Thuột lừng danh, hương thơm ngào ngạt đậm đà đánh thức mọi giác quan.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('99cb866a-9dca-44d5-8311-46b6b9dc5550', 'b1000000-0000-0000-0000-000000000027', NULL, @AdminId, 0, 90, 90, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000027', N'/uploads/products/coffee_g7.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000028')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000028', 'c0000000-0000-0000-0000-000000000060', N'Gạo ST25 Ông Cua chính hãng bao 5kg', 195000, 75, 10, N'Gạo ngon nhất thế giới ST25 giống chuẩn của kỹ sư Hồ Quang Cua, hạt thon dài trắng trong, cơm dẻo mềm thơm mùi lá dứa tự nhiên.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('a03ec477-5754-4391-8107-6bb6541dfc57', 'b1000000-0000-0000-0000-000000000028', NULL, @AdminId, 0, 75, 75, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000028', N'/uploads/products/rice_st25.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000029')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000029', 'c0000000-0000-0000-0000-000000000060', N'Mì Hảo Hảo tôm chua cay thùng 30 gói', 128000, 80, 10, N'Mì ăn liền quốc dân Hảo Hảo vị tôm chua cay thơm ngon đậm đà khó cưỡng, sợi mì vàng giòn súp chua cay đặc trưng thơm lừng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('6bf218f4-8731-45bb-9da3-8367e1cce31e', 'b1000000-0000-0000-0000-000000000029', NULL, @AdminId, 0, 80, 80, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000029', N'/uploads/products/noodle_haohao.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000030')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000030', 'c0000000-0000-0000-0000-000000000060', N'Mì trộn tương đen Samyang Hàn Quốc gói 140g', 29000, 70, 10, N'Mì khô tương đen chuẩn vị Hàn Quốc Samyang, sợi mì to dai tròn sốt tương đen đậm đà bùi béo kèm rau sấy hấp dẫn.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('8f90c8e7-3b34-49c1-b0b7-e70906071818', 'b1000000-0000-0000-0000-000000000030', NULL, @AdminId, 0, 70, 70, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000030', N'/uploads/products/samyang_noodle.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000031')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000031', 'c0000000-0000-0000-0000-000000000060', N'Nước mắm Nam Ngư Đệ Nhị chai 900ml', 28000, 90, 10, N'Nước mắm Nam Ngư Đệ Nhị thơm ngon đậm đà giọt mắm cá cơm tươi, màu nâu cánh gián sóng sánh cho món ăn tròn vị yêu thương.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('7d41b8a2-f31c-41bd-af8a-2752d4db2e9b', 'b1000000-0000-0000-0000-000000000031', NULL, @AdminId, 0, 90, 90, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000031', N'/uploads/products/fish_sauce.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000032')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000032', 'c0000000-0000-0000-0000-000000000060', N'Dầu đậu nành nguyên chất Simply chai 1L', 59000, 85, 10, N'Dầu ăn Simply 100% tinh luyện từ hạt đậu nành tuyển chọn, giàu Omega 3, 6, 9 và vitamin E tốt cho tim mạch của cả gia đình.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('10aaa8fc-d5a9-45d1-8e69-5033ff9dd69e', 'b1000000-0000-0000-0000-000000000032', NULL, @AdminId, 0, 85, 85, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000032', N'/uploads/products/cooking_oil.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000033')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000033', 'c0000000-0000-0000-0000-000000000070', N'Dầu gội Head & Shoulders Bạc Hà mát lạnh 850ml', 189000, 65, 10, N'Dầu gội sạch gàu Head & Shoulders hương bạc hà sảng khoái, công thức bọt mịn làm sạch sâu da đầu và giữ tóc bồng bềnh tự tin.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('0d2a197c-a25e-47bc-adf2-b3c0147ee1df', 'b1000000-0000-0000-0000-000000000033', NULL, @AdminId, 0, 65, 65, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000033', N'/uploads/products/shampoo_head_shoulders.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000034')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000034', 'c0000000-0000-0000-0000-000000000070', N'Sữa tắm dưỡng ẩm sâu Dove Deep Moisture 900g', 179000, 60, 10, N'Sữa tắm dưỡng ẩm chuyên sâu Dove tinh chất dưỡng ẩm tự nhiên NutriumMoisture, nuôi dưỡng làn da mịn màng hương thơm dịu nhẹ quý phái.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('bc3deab7-6aec-41b1-ad17-b489bebd9959', 'b1000000-0000-0000-0000-000000000034', NULL, @AdminId, 0, 60, 60, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000034', N'/uploads/products/body_wash_dove.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000035')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000035', 'c0000000-0000-0000-0000-000000000070', N'Nước giặt OMO Matic cửa trên túi 3.6kg', 185000, 70, 10, N'Nước giặt OMO Matic công nghệ màn chắn kháng bẩn Polyshield 2.0 xoáy bay vết bẩn cứng đầu chỉ trong 1 lần giặt, hương thơm tươi mát cả ngày.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('9e88dee4-b8f8-4dbe-bf2d-af46be46054e', 'b1000000-0000-0000-0000-000000000035', NULL, @AdminId, 0, 70, 70, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000035', N'/uploads/products/detergent_omo.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000036')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000036', 'c0000000-0000-0000-0000-000000000070', N'Nước xả vải Comfort một lần xả hương ban mai túi 3.2L', 175000, 55, 10, N'Nước xả vải Comfort với công thức chăm sóc sợi vải chuyên sâu, chống xơ cứng, lưu giữ hương hoa ban mai ngát hương suốt cả tuần.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('41d88c5e-aa4f-4bb9-bc33-a58d8bfd0a95', 'b1000000-0000-0000-0000-000000000036', NULL, @AdminId, 0, 55, 55, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000036', N'/uploads/products/softener_comfort.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b1000000-0000-0000-0000-000000000037')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b1000000-0000-0000-0000-000000000037', 'c0000000-0000-0000-0000-000000000070', N'Nước rửa chén Sunlight thiên nhiên túi 3.6kg', 99000, 80, 10, N'Nước rửa chén Sunlight tinh chất muối khoáng và lô hội thiên nhiên, đánh bay dầu mỡ nhanh chóng, dịu nhẹ an toàn với da tay.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('979525d9-0f50-45e3-8a9a-c27ea59e3c09', 'b1000000-0000-0000-0000-000000000037', NULL, @AdminId, 0, 80, 80, N'Tồn kho ban đầu từ dữ liệu mẫu', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES (NEWID(), 'b1000000-0000-0000-0000-000000000037', N'/uploads/products/dishwash_sunlight.jpg', 1);
END;

COMMIT TRANSACTION;
SELECT COUNT(*) AS TotalProducts FROM dbo.Products WHERE IsDeleted = 0;
SELECT COUNT(*) AS TotalImages FROM dbo.ProductImages;