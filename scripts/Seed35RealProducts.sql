USE [ECommerceDB];
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Now datetime2(7) = SYSUTCDATETIME();
DECLARE @AdminId uniqueidentifier = '99999999-9999-9999-9999-999999999999';

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000001', 'c0000000-0000-0000-0000-000000000002', N'Samsung Galaxy S24 Ultra 256GB Titan', 27990000, 18, 10, N'Điện thoại thông minh cao cấp nhất của Samsung với khung Titan, chip Snapdragon 8 Gen 3 for Galaxy, bút S-Pen quyền năng và hệ thống camera 200MP tích hợp Galaxy AI vượt trội.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('2ef976bd-15a6-4e5e-95f9-a06593035881', 'b2000000-0000-0000-0000-000000000001', NULL, @AdminId, 0, 18, 18, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('bd73e5d0-af02-41df-bce8-5f60088fa027', 'b2000000-0000-0000-0000-000000000001', N'/uploads/products/samsung_s24_ultra.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000002', 'c0000000-0000-0000-0000-000000000002', N'iPhone 16 Pro Max 256GB Sa Mạc', 34490000, 15, 10, N'iPhone 16 Pro Max với thiết kế titan sa mạc ấn tượng, chip A18 Pro mạnh mẽ đỉnh cao, nút Điều khiển Camera tiện lợi cùng thời lượng pin tốt nhất từng có trên iPhone.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('34324057-7230-4748-bf74-4987475c2d8f', 'b2000000-0000-0000-0000-000000000002', NULL, @AdminId, 0, 15, 15, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('3245dc00-9521-4bb8-976b-e6511fbc53c0', 'b2000000-0000-0000-0000-000000000002', N'/uploads/products/iphone_16_promax.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000003')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000003', 'c0000000-0000-0000-0000-000000000002', N'Google Pixel 8 Pro 128GB Bay Blue', 18990000, 12, 10, N'Smartphone nhiếp ảnh hàng đầu từ Google với vi xử lý Tensor G3, thuật toán xử lý hình ảnh AI đỉnh cao, màn hình Super Actua sáng rực rỡ và hệ điều hành Android thuần khiết.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('31a876c4-8ee8-4a19-b58e-6a0d6acf8ddd', 'b2000000-0000-0000-0000-000000000003', NULL, @AdminId, 0, 12, 12, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('c76a4ff6-f6e4-47fa-ab96-2088826e3b3b', 'b2000000-0000-0000-0000-000000000003', N'/uploads/products/google_pixel_8_pro.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000004')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000004', 'c0000000-0000-0000-0000-000000000003', N'Lenovo ThinkPad X1 Carbon Gen 11 Core i7', 36500000, 10, 10, N'Laptop doanh nhân huyền thoại siêu nhẹ chỉ 1.12kg từ sợi carbon, bàn phím gõ êm ái bậc nhất thế giới, bảo mật vân tay và khuôn mặt chuẩn quân đội.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('d3b0b410-5652-4cd1-b30e-d3737925cd29', 'b2000000-0000-0000-0000-000000000004', NULL, @AdminId, 0, 10, 10, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('2335ba36-c871-47e3-a710-9f908dc361f2', 'b2000000-0000-0000-0000-000000000004', N'/uploads/products/lenovo_thinkpad_x1.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000005', 'c0000000-0000-0000-0000-000000000003', N'ASUS Zenbook 14 OLED UX3405 Core Ultra 7', 28990000, 14, 10, N'Thiết kế kim loại nguyên khối mỏng nhẹ thanh lịch, màn hình Lumina OLED 3K 120Hz chuẩn màu điện ảnh, vi xử lý Intel Core Ultra 7 tích hợp NPU AI thông minh.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('ca90a850-633e-44de-a389-706924c0b0ad', 'b2000000-0000-0000-0000-000000000005', NULL, @AdminId, 0, 14, 14, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('ba874217-9c7a-4113-a2cb-441361c29041', 'b2000000-0000-0000-0000-000000000005', N'/uploads/products/asus_zenbook_14.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000006')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000006', 'c0000000-0000-0000-0000-000000000003', N'MacBook Pro 14 M3 Pro 18GB/512GB Space Black', 49990000, 8, 10, N'Cỗ máy làm việc đồ họa chuyên nghiệp của Apple, chip M3 Pro hiệu năng đột phá, màn hình Liquid Retina XDR 120Hz tuyệt mỹ cùng thời lượng pin lên đến 22 giờ liên tục.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('caafd93f-ddef-4959-b41f-0c1d4a9b9bc9', 'b2000000-0000-0000-0000-000000000006', NULL, @AdminId, 0, 8, 8, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('62f0a270-1890-469e-b6d5-2a6017dba5ae', 'b2000000-0000-0000-0000-000000000006', N'/uploads/products/macbook_pro_14_m3.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000007')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000007', 'c0000000-0000-0000-0000-000000000004', N'Tai nghe không dây Apple AirPods Pro 2 USB-C', 5490000, 35, 10, N'Chống ồn chủ động ANC thế hệ 2 gấp đôi hiệu quả, âm thanh không gian cá nhân hóa, hộp sạc MagSafe cổng Type-C chống bụi kháng nước chuẩn IP54.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('632334a0-c2de-470e-9a6b-6be07636aed7', 'b2000000-0000-0000-0000-000000000007', NULL, @AdminId, 0, 35, 35, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('51ed9863-4f04-4148-a24f-c0c9a7c37cc1', 'b2000000-0000-0000-0000-000000000007', N'/uploads/products/apple_airpods_pro_2.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000008')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000008', 'c0000000-0000-0000-0000-000000000004', N'Bàn phím cơ không dây Keychron K2 Pro RGB', 2450000, 25, 10, N'Bàn phím cơ layout 75% gọn gàng, hỗ trợ QMK/VIA tùy biến không giới hạn, switch êm ái, kết nối Bluetooth 5.1 cùng lúc 3 thiết bị MacOS và Windows.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('dc58c816-2093-4227-b07d-8916099e4b71', 'b2000000-0000-0000-0000-000000000008', NULL, @AdminId, 0, 25, 25, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('e1fdf939-95b4-4a46-b1fb-eaa2fa1cc9ab', 'b2000000-0000-0000-0000-000000000008', N'/uploads/products/keychron_k2_pro.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000009')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000009', 'c0000000-0000-0000-0000-000000000004', N'Củ sạc nhanh Anker Prime 67W GaN 3 cổng', 990000, 45, 10, N'Công nghệ GaNPrime độc quyền nhỏ gọn hơn 51%, công suất tối đa 67W sạc cùng lúc MacBook, iPhone và iPad với tính năng kiểm soát nhiệt ActiveShield 2.0.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('1bcccf10-e3e0-4ab3-8f2b-2ef6605c8047', 'b2000000-0000-0000-0000-000000000009', NULL, @AdminId, 0, 45, 45, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('f8ea4418-2d27-49ed-99ea-c1fce2ce7691', 'b2000000-0000-0000-0000-000000000009', N'/uploads/products/charger_anker_67w.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000010')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000010', 'c0000000-0000-0000-0000-000000000006', N'Nồi chiên không dầu Tefal Easy Fry Deluxe 4.2L', 2190000, 22, 10, N'Vỏ inox sáng bóng sang trọng, dung tích 4.2L chiên gà nguyên con, công nghệ đối lưu nhiệt 360 độ giúp món ăn giòn rụm bên ngoài mọng nước bên trong giảm 80% dầu mỡ.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('c065bf94-c3e9-4c32-8cdb-9f33bba1ee0d', 'b2000000-0000-0000-0000-000000000010', NULL, @AdminId, 0, 22, 22, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('85bba972-6eab-44be-854d-f3ea67e7c7be', 'b2000000-0000-0000-0000-000000000010', N'/uploads/products/air_fryer_tefal.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000011', 'c0000000-0000-0000-0000-000000000006', N'Máy xay sinh tố cầm tay Braun MQ5235 Sauce 1000W', 1450000, 30, 10, N'Công nghệ chống văng bắn SplashControl, 21 tốc độ xoay linh hoạt, công suất mạnh mẽ 1000W xay nhuyễn mọi thực phẩm, sinh tố, cháo trẻ em và súp trong tích tắc.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('fe75b1c6-2a0d-47d5-9c50-93429dcb54eb', 'b2000000-0000-0000-0000-000000000011', NULL, @AdminId, 0, 30, 30, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('feb32e62-9d75-4f57-a3cd-dc12159d8ff8', 'b2000000-0000-0000-0000-000000000011', N'/uploads/products/blender_braun.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000012')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000012', 'c0000000-0000-0000-0000-000000000006', N'Bộ dao làm bếp cao cấp thép không gỉ 5 món', 890000, 40, 10, N'Bộ dao đúc nguyên khối từ thép không gỉ chuẩn Đức, độ sắc bén hoàn hảo, cán cầm bọc cao su chống trượt tạo cảm giác đầm tay chuyên nghiệp khi thái gọt thực phẩm.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('0ac8ce66-95d2-469b-9ab9-af9375d07b2d', 'b2000000-0000-0000-0000-000000000012', NULL, @AdminId, 0, 40, 40, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('856fbb63-fc14-4e18-aa27-f3a398e6b354', 'b2000000-0000-0000-0000-000000000012', N'/uploads/products/kitchen_knife_set.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000013')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000013', 'c0000000-0000-0000-0000-000000000010', N'Bơ sáp 034 Đắk Lắk dẻo béo hảo hạng (kg)', 65000, 70, 10, N'Bơ sáp giống 034 quả dài trái to đều, cơm vàng óng dẻo quánh béo ngậy, hạt nhỏ vỏ mỏng, giàu chất béo lành mạnh và vitamin tốt cho tim mạch.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('142a4a8e-ed8c-4139-8a35-03bd3bbbf992', 'b2000000-0000-0000-0000-000000000013', NULL, @AdminId, 0, 70, 70, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('3cce7c63-f547-4420-8631-9d4019bcea41', 'b2000000-0000-0000-0000-000000000013', N'/uploads/products/avocado_034_daklak.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000014')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000014', 'c0000000-0000-0000-0000-000000000010', N'Dâu tây giống Hàn Quốc Mộc Châu (hộp 330g)', 95000, 50, 10, N'Dâu tây Mộc Châu quả mọng đỏ tươi cuống xanh, vị ngọt đậm thơm dịu đặc trưng, canh tác theo tiêu chuẩn hữu cơ an toàn cho sức khỏe gia đình.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('8ab47b10-da23-40a7-bb59-adaac52e6013', 'b2000000-0000-0000-0000-000000000014', NULL, @AdminId, 0, 50, 50, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('cd51cf4e-0340-4ad3-8b1f-65e6418a0fdb', 'b2000000-0000-0000-0000-000000000014', N'/uploads/products/strawberry_mocchau.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000015')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000015', 'c0000000-0000-0000-0000-000000000010', N'Khoai lang mật Đà Lạt ngọt dẻo chảy mật (kg)', 38000, 80, 10, N'Khoai lang mật trồng tại vùng cao nguyên Đà Lạt màu ruột đỏ cam, khi nướng hoặc hấp tươm mật ngọt lịm thơm nức mũi, giàu chất xơ tự nhiên.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('5e027cbd-8f28-4c4a-ad61-5793e79c5ba7', 'b2000000-0000-0000-0000-000000000015', NULL, @AdminId, 0, 80, 80, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('ddb11177-48fe-4230-b255-f8a3060a98be', 'b2000000-0000-0000-0000-000000000015', N'/uploads/products/sweet_potato_dalat.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000016')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000016', 'c0000000-0000-0000-0000-000000000010', N'Ớt chuông Đà Lạt tươi giòn VietGAP (túi 500g)', 35000, 60, 10, N'Ớt chuông đỏ vàng Đà Lạt thịt dày giòn ngọt thanh không cay, mọng nước giàu vitamin C gấp 3 lần cam chanh, hoàn hảo cho món salad và xào bò.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('6d11f4fe-59b8-409b-a8ad-11a38009f8a7', 'b2000000-0000-0000-0000-000000000016', NULL, @AdminId, 0, 60, 60, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('1e82efb7-11c3-45b9-9db1-61096e6638c6', 'b2000000-0000-0000-0000-000000000016', N'/uploads/products/bell_pepper_dalat.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000017')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000017', 'c0000000-0000-0000-0000-000000000020', N'Tôm sú tươi sống nguyên con loại 1 (hộp 500g)', 185000, 40, 10, N'Tôm sú Cà Mau tươi ngon đánh bắt tự nhiên, vỏ mỏng bóng chắc nịch, thịt ngọt đậm đà giàu canxi, chế biến hấp nước dừa hay nướng muối ớt đều tuyệt đỉnh.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('52d5a6ae-f7b1-4883-8ac6-06c7524e207a', 'b2000000-0000-0000-0000-000000000017', NULL, @AdminId, 0, 40, 40, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('9c579468-3b6e-4379-86ef-26ce071c03a2', 'b2000000-0000-0000-0000-000000000017', N'/uploads/products/black_tiger_shrimp.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000018')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000018', 'c0000000-0000-0000-0000-000000000020', N'Mực ống tươi Cô Tô làm sạch (khay 500g)', 145000, 35, 10, N'Mực ống vùng biển Cô Tô làm sạch sơ chế cấp đông chuẩn quy trình, thân mực dày dặn trắng tinh, giòn sần sật ngọt nước xào sa tế hay hấp gừng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('bacb6af5-91e8-4a95-89e9-beb6fb6c06e3', 'b2000000-0000-0000-0000-000000000018', NULL, @AdminId, 0, 35, 35, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('1283f82c-68d1-4ee7-9620-52ea080b327f', 'b2000000-0000-0000-0000-000000000018', N'/uploads/products/squid_fresh_500g.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000019')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000019', 'c0000000-0000-0000-0000-000000000020', N'Bắp bò hoa Úc nhập khẩu thái lát khay 500g', 179000, 50, 10, N'Bắp bò hoa nhập khẩu từ Úc với những đường gân trong veo giòn giòn, thịt nạc đỏ au mềm ngọt, dùng nhúng lẩu chua cay hay nấu phở bò đậm đà chuẩn vị.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('82be7180-390a-4fa2-b50b-5fe03c584b86', 'b2000000-0000-0000-0000-000000000019', NULL, @AdminId, 0, 50, 50, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('6ebee822-e9b6-4446-a976-58afa27aa9d9', 'b2000000-0000-0000-0000-000000000019', N'/uploads/products/beef_shank_aus.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000020')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000020', 'c0000000-0000-0000-0000-000000000020', N'Trứng cút tươi sạch trang trại (vỉ 30 quả)', 25000, 90, 10, N'Trứng cút sạch chăn nuôi trang trại khép kín, vỏ sạch bóng đều đẹp, lòng đỏ nhiều béo ngậy thơm ngon, món ăn khoái khẩu trong món thịt kho hoặc lẩu.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('0eff8e58-4329-4cce-96fc-a1d8e6ae1820', 'b2000000-0000-0000-0000-000000000020', NULL, @AdminId, 0, 90, 90, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('52aa8319-e950-4b0c-ab8d-f4039d2e4439', 'b2000000-0000-0000-0000-000000000020', N'/uploads/products/quail_eggs_30p.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000021')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000021', 'c0000000-0000-0000-0000-000000000030', N'Sữa tươi thanh trùng Dalat Milk ít đường chai 900ml', 42000, 60, 10, N'Chế biến từ 100% sữa bò tươi nguyên chất cao nguyên Đà Lạt bằng công nghệ thanh trùng nhiệt độ thấp giữ trọn vẹn hương vị béo ngậy tự nhiên.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('bd276d22-b6d1-45ac-8fe8-574d3c7c1b0b', 'b2000000-0000-0000-0000-000000000021', NULL, @AdminId, 0, 60, 60, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('c419d95a-bd52-44e1-87d6-c6351281d58f', 'b2000000-0000-0000-0000-000000000021', N'/uploads/products/dalat_milk_900ml.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000022')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000022', 'c0000000-0000-0000-0000-000000000030', N'Sữa chua uống lên men Yakult Nhật Bản (lốc 5 chai)', 27000, 110, 10, N'Chứa hơn 6.5 tỷ khuẩn L.casei Shirota sống sót đến ruột non giúp cân bằng hệ vi sinh đường ruột, hỗ trợ tiêu hóa khỏe mạnh cho cả người lớn và trẻ nhỏ.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('c9b29dab-4fc6-4b06-9c7e-9edb1e66d6f3', 'b2000000-0000-0000-0000-000000000022', NULL, @AdminId, 0, 110, 110, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('7ea228a4-0c92-4f65-a68a-48093dc98fc5', 'b2000000-0000-0000-0000-000000000022', N'/uploads/products/yakult_probiotic.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000023')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000023', 'c0000000-0000-0000-0000-000000000030', N'Bơ lạt tự nhiên Anchor New Zealand khối 227g', 78000, 55, 10, N'Bơ lạt thượng hạng làm từ sữa bò ăn cỏ tự nhiên New Zealand, màu vàng ngà thơm ngậy, là nguyên liệu vàng cho làm bánh mì, bánh ngọt và nấu sốt Tây.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('5458c66f-926f-4b3b-a24a-231fdcb63bef', 'b2000000-0000-0000-0000-000000000023', NULL, @AdminId, 0, 55, 55, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('1ca1f79e-2982-49f6-be6c-e26a29a71560', 'b2000000-0000-0000-0000-000000000023', N'/uploads/products/butter_anchor_227g.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000024')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000024', 'c0000000-0000-0000-0000-000000000030', N'Phô mai Con Bò Cười truyền thống (hộp 8 miếng 112g)', 39000, 85, 10, N'Thương hiệu phô mai lừng danh nước Pháp, công thức giàu Canxi, Vitamin D và Kẽm, kết cấu mềm mịn thơm béo ăn kèm bánh mì hoặc dùng trực tiếp rất ngon.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('469bc200-39bb-4142-b473-f51e207354ff', 'b2000000-0000-0000-0000-000000000024', NULL, @AdminId, 0, 85, 85, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('a9b50389-804a-48e6-9173-ab0d93146ec6', 'b2000000-0000-0000-0000-000000000024', N'/uploads/products/laughing_cow_cheese.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000025')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000025', 'c0000000-0000-0000-0000-000000000040', N'Bánh quy bơ Danisa Đan Mạch hộp thiếc sang trọng 454g', 135000, 50, 10, N'Công thức bánh quy bơ truyền thống hoàng gia Đan Mạch, thơm lừng hương bơ tự nhiên tan ngay đầu lưỡi, hộp thiếc vàng dập nổi thích hợp làm quà biếu tặng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('4d836a03-c00f-48a5-aad4-905085f05bbe', 'b2000000-0000-0000-0000-000000000025', NULL, @AdminId, 0, 50, 50, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('27a7fbf8-75ec-499a-b90d-761a07c615ed', 'b2000000-0000-0000-0000-000000000025', N'/uploads/products/danisa_butter_cookies.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000026')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000026', 'c0000000-0000-0000-0000-000000000040', N'Bánh Chocopie Orion phủ socola đậm vị (hộp 12 cái)', 58000, 75, 10, N'Lớp vỏ bông lan xốp mềm phủ socola béo ngọt ôm trọn nhân kẹo dẻo marshmallow dai dai quen thuộc, món ăn nhẹ gắn liền với tình yêu thương gia đình.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('221e57d1-2c2e-4404-93a2-eec4cf2c308d', 'b2000000-0000-0000-0000-000000000026', NULL, @AdminId, 0, 75, 75, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('610ac6ce-6710-4e0d-9d2f-636f1921a733', 'b2000000-0000-0000-0000-000000000026', N'/uploads/products/orion_chocopie_12p.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000027')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000027', 'c0000000-0000-0000-0000-000000000040', N'Kẹo sô-cô-la thanh xốp KitKat túi 12 thanh x 17g', 68000, 65, 10, N'Bánh xốp giòn rụm bọc socola sữa ngọt dịu thanh tao, sản phẩm nổi tiếng thế giới của Nestle, tiếp thêm năng lượng và cảm hứng sảng khoái mỗi giờ nghỉ giải lao.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('09902007-2c92-4bb8-a6cc-d3741a5d7a06', 'b2000000-0000-0000-0000-000000000027', NULL, @AdminId, 0, 65, 65, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('45e68cd7-e3ac-44ae-a28c-471d4ea793b8', 'b2000000-0000-0000-0000-000000000027', N'/uploads/products/kitkat_chocolate_bar.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000028')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000028', 'c0000000-0000-0000-0000-000000000040', N'Hạt dẻ cười rang muối Mỹ hũ nắp nhôm 250g', 115000, 40, 10, N'Hạt dẻ cười nhập khẩu từ California hạt to mẩy nứt vỏ tự nhiên, rang muối mằn mặn bùi ngậy, giàu vitamin E và chất béo không bão hòa tốt cho vóc dáng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('77bbd25e-5041-4848-89bd-2d5eb7b02cfc', 'b2000000-0000-0000-0000-000000000028', NULL, @AdminId, 0, 40, 40, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('53509189-74e9-49a4-b9a5-58a153e0f4a4', 'b2000000-0000-0000-0000-000000000028', N'/uploads/products/pistachio_roasted.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000029')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000029', 'c0000000-0000-0000-0000-000000000050', N'Bia Tiger lon bạc Crystal 330ml (thùng 24 lon)', 415000, 35, 10, N'Bia Tiger Crystal được ủ theo quy trình làm lạnh sâu Cold Suspension độc đáo, hương vị sảng khoái mát lạnh dễ uống, lựa chọn hàng đầu cho các buổi tiệc sôi động.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('a8090e0a-7261-4cb3-a837-1744113414ba', 'b2000000-0000-0000-0000-000000000029', NULL, @AdminId, 0, 35, 35, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('248c7ed5-fa71-4e0f-8135-ef3562612936', 'b2000000-0000-0000-0000-000000000029', N'/uploads/products/tiger_beer_crystal.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000030')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000030', 'c0000000-0000-0000-0000-000000000050', N'Nước cam ép nguyên chất có tép Teppy chai 1 lít', 28000, 80, 10, N'Nước cam thơm mát từ tép cam mọng nước tự nhiên, vị chua ngọt hài hòa giúp giải nhiệt tức thì và bổ sung vitamin C dồi dào cho cơ thể luôn tràn đầy sức sống.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('bd8135bb-4b5d-43b6-9ae0-bbf0f7f3ba1d', 'b2000000-0000-0000-0000-000000000030', NULL, @AdminId, 0, 80, 80, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('61602a0e-9237-4b15-8600-b7565a06ca03', 'b2000000-0000-0000-0000-000000000030', N'/uploads/products/teppy_orange_juice.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000031')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000031', 'c0000000-0000-0000-0000-000000000050', N'Nước tăng lực Red Bull nắp vàng Thái Lan lon 250ml', 14000, 120, 10, N'Thương hiệu nước tăng lực hàng đầu cung cấp Taurine và Vitamin nhóm B, hồi phục thể lực nhanh chóng và xua tan mệt mỏi cho ngày làm việc học tập hiệu quả.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('2d2624f7-a666-47fc-9eee-6645b0b78e68', 'b2000000-0000-0000-0000-000000000031', NULL, @AdminId, 0, 120, 120, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('98bf7bb9-d747-4aad-bd42-56cd4358280b', 'b2000000-0000-0000-0000-000000000031', N'/uploads/products/redbull_energy_can.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000032')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000032', 'c0000000-0000-0000-0000-000000000050', N'Cà phê hòa tan Nescafe Red Cup nguyên chất hũ 200g', 129000, 45, 10, N'Hạt cà phê Robusta tuyển chọn rang xay theo công nghệ hiện đại giữ trọn vẹn hương vị đậm đà thơm ngát, dễ dàng pha chế theo sở thích đen đá hoặc sữa nóng.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('efa97821-0626-448c-b424-56189c45bd0f', 'b2000000-0000-0000-0000-000000000032', NULL, @AdminId, 0, 45, 45, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('e23c2d48-41a3-4a8b-838b-844f09e185cb', 'b2000000-0000-0000-0000-000000000032', N'/uploads/products/nescafe_red_cup.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000033')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000033', 'c0000000-0000-0000-0000-000000000060', N'Gạo lứt đỏ huyết rồng hữu cơ túi hút chân không 2kg', 68000, 50, 10, N'Gạo lứt đỏ huyết rồng giàu vitamin nhóm B, magie và chất xơ hòa tan, vị ngọt bùi tự nhiên, hỗ trợ kiểm soát đường huyết và duy trì vóc dáng thon gọn.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('0ff2d183-a5c9-4300-8fcf-3c66024fb9be', 'b2000000-0000-0000-0000-000000000033', NULL, @AdminId, 0, 50, 50, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('5fbf1cc9-23fd-42f2-b552-98dfe5f159fd', 'b2000000-0000-0000-0000-000000000033', N'/uploads/products/red_brown_rice_2kg.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000034')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000034', 'c0000000-0000-0000-0000-000000000060', N'Nui xoắn Fusilli Ý cao cấp Barilla hộp 500g', 42000, 70, 10, N'Làm từ 100% lúa mì Semolina thượng hạng của Ý, sợi nui xoắn giữ sốt cực tốt, khi luộc chín dai mềm chuẩn phong cách Al Dente trứ danh thế giới.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('aab6e99c-d1ee-4d9a-96cb-8df5bfbfdb0f', 'b2000000-0000-0000-0000-000000000034', NULL, @AdminId, 0, 70, 70, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('54a6f756-2031-4b23-a359-2806090e0d24', 'b2000000-0000-0000-0000-000000000034', N'/uploads/products/barilla_fusilli_pasta.jpg', 1);
END;


IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 'b2000000-0000-0000-0000-000000000035')
BEGIN
    INSERT INTO dbo.Products (Id, CategoryId, Name, Price, StockQuantity, LowStockThreshold, Description, IsDeleted, CreatedAt)
    VALUES ('b2000000-0000-0000-0000-000000000035', 'c0000000-0000-0000-0000-000000000070', N'Nước lau sàn Sunlight tinh dầu thảo mộc can 3.6kg', 89000, 45, 10, N'Can lớn 3.6kg tiết kiệm, công nghệ sạch bóng vượt trội, hương thơm tinh dầu thảo mộc thanh tao dịu nhẹ thư thái đuổi côn trùng hiệu quả.', 0, @Now);

    INSERT INTO dbo.InventoryTransactions (Id, ProductId, OrderId, CreatedByUserId, Type, QuantityChange, BalanceAfter, Reason, CreatedAt)
    VALUES ('f3afbf81-6622-42f9-8fd9-a33ef96dc2fd', 'b2000000-0000-0000-0000-000000000035', NULL, @AdminId, 0, 45, 45, N'Tồn kho ban đầu từ dữ liệu mở rộng', @Now);

    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, IsMain)
    VALUES ('d9c55669-6270-49bd-b07b-d979edfdbefa', 'b2000000-0000-0000-0000-000000000035', N'/uploads/products/sunlight_floor_cleaner.jpg', 1);
END;

COMMIT TRANSACTION;
SELECT COUNT(*) AS TotalProducts FROM dbo.Products WHERE IsDeleted = 0;
SELECT COUNT(*) AS TotalImages FROM dbo.ProductImages;