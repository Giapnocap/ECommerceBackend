# Technical Decisions

Tài liệu này ghi lại các quyết định nền tảng để các phase tiếp theo triển khai nhất quán.

## Architecture

- Giữ cấu trúc theo 4 lớp: `Domain`, `Application`, `Infrastructure`, `API`.
- `Program.cs` chỉ bootstrap app pipeline.
- Đăng ký dependency/cross-cutting concern đặt trong `API/Extensions`.
- Service nghiệp vụ nằm trong `Application/Services`.
- EF Core và repository nằm trong `Infrastructure`.

## Mapping

- Phase 3 dùng AutoMapper cho các response của User, Category, Product, Cart và Order.
- Cấu hình mapping tập trung trong `Application/Mappings/MappingProfile.cs`.
- `AuthService` giữ mapping thủ công vì auth response được tổng hợp từ user, JWT, refresh token và permission.
- Success response tiếp tục dùng DTO tài nguyên hoặc `PagedResult<T>`; error response giữ contract thống nhất gồm `message`, `code`, `traceId`, `details` và `errors`.

## Upload

- Phase 4 tách `IUploadService` và `UploadService`; `ProductService` chỉ còn nghiệp vụ sản phẩm.
- File ảnh lưu local trong `Uploads/products`.
- Giới hạn mặc định là 5MB và cấu hình qua `Uploads:MaxImageSizeBytes`.
- File phải khớp đồng thời extension, MIME type và chữ ký nhị phân của JPEG, PNG hoặc WebP.
- Upload ghi file trước, sau đó cập nhật database trong transaction; file mới được dọn nếu transaction thất bại.
- Xóa ảnh cập nhật database trước rồi mới xóa file để không tạo bản ghi trỏ tới file đã mất khi database lỗi.
- Unique filtered index bảo đảm mỗi sản phẩm có tối đa một ảnh `IsMain`; service tự chọn ảnh đầu tiên hoặc ảnh thay thế làm ảnh chính.

## Product And Category Rules

- Giá sản phẩm phải lớn hơn 0; tồn kho không được âm.
- Tên sản phẩm và category được trim trước khi lưu.
- Category hỗ trợ tối đa 2 cấp.
- Category không thể tự làm cha, chọn category con làm cha, hoặc chuyển category đang có con thành category con.
- Migration Phase 4 dừng với thông báo rõ ràng nếu dữ liệu cũ vi phạm giá hoặc giới hạn độ dài mới; dữ liệu ảnh chính cũ được chuẩn hóa trước khi tạo unique index.

## Cart And Order

- Cart không giữ chỗ tồn kho. API hiển thị giá hiện tại, tồn kho hiện tại và cờ `IsAvailable`; item hết hàng hoặc ngừng bán vẫn được trả về để khách có thể xóa.
- Mỗi cặp `CartId`/`ProductId` chỉ có một cart item. Thêm lại cùng sản phẩm sẽ cộng số lượng và kiểm tra giới hạn `int` cùng tồn kho.
- Checkout chạy trong transaction `ReadCommitted` với khóa cập nhật tường minh theo thứ tự `Cart -> Product`; các product luôn được khóa theo `Guid` tăng dần để tránh đảo thứ tự khóa.
- Cart row lock ngăn thay đổi cùng giỏ trong lúc checkout. Product row lock bảo đảm chỉ một checkout có thể tiêu thụ phần tồn kho cuối cùng.
- Giá và tồn kho được đọc lại trong transaction. Tạo order/detail, trừ kho và xóa cart item được lưu nguyên tử; bất kỳ lỗi nào cũng rollback toàn bộ.
- Order mới bắt đầu ở `Pending` và giữ tồn kho ngay khi checkout; `Confirmed` là bước nhân viên chấp nhận xử lý đơn.
- State machine cho phép `Pending -> Confirmed|Cancelled`, `Confirmed -> Shipping|Cancelled`, `Shipping -> Delivered`; `Delivered` và `Cancelled` là trạng thái cuối.
- Gửi lại chính trạng thái hiện tại là idempotent. Vì vậy hủy lặp không hoàn kho nhiều lần.
- `Product` và `Order` dùng SQL Server `rowversion`; conflict đồng thời và deadlock được trả về `409` thay vì lỗi hệ thống `500`.
- Migration Phase 5 chuẩn hóa cart item trùng, đồng bộ giá cart lịch sử, rồi thêm unique index, row version, giới hạn độ dài và check constraints.

- Money values dung gioi han chung `decimal(18,2)` o validator va service. Tong order duoc tinh bang helper co guard overflow de tranh database error khi gia hoac so luong qua lon.
- Cart fallback creation xu ly unique-index race bang cach detach cart insert that bai va doc lai cart da duoc request khac tao.
- Product/order paging dung skip helper chung va tie-breaker `Id` de thu tu trang on dinh, dong thoi tranh tran so khi page qua lon.

## Swagger Documentation

- XML documentation file duoc generate trong build va duoc include vao Swagger neu file ton tai.
- `AuthorizeOperationFilter` gan Bearer security requirement cho endpoint co `[Authorize]`.
- `DefaultResponseOperationFilter` them response loi mac dinh voi schema `ApiErrorResponse`, giup cac endpoint co error contract nhat quan ma khong lap attribute o tung action.
- `RequestContractOperationFilter` documents required checkout/webhook headers and the provider-specific raw JSON webhook body that MVC cannot infer automatically.
- Controller actions khai bao success schema bang `ProducesResponseType` vi phan lon action tra `IActionResult`.
- `ECommerceBackend.http` dong vai tro request collection demo cho cac flow chinh cua API.

## Testing

- Test project nam trong `ECommerceBackend.Tests` va tham chieu project chinh.
- Phase 7 dung xUnit, `Microsoft.NET.Test.Sdk` va EF Core InMemory de chay nhanh tren local.
- Auth/Product duoc test o service level voi repository va DbContext that tren InMemory provider.
- Cart/Order duoc test o validator, mapping va state-machine level vi service runtime hien dung SQL Server row locks/`FromSqlInterpolated`; full transaction integration test se can SQL Server test database rieng trong Phase 8/CI.
- Main `ECommerceBackend.csproj` exclude `ECommerceBackend.Tests/**/*.cs` de web project khong compile test files.

## Authorization

- Phase hiện tại dùng role-based authorization: `Admin`, `Staff`, `Customer`.
- Phase 2 bổ sung refresh token rotation.
- Access token có role claims và permission claims.
- Permission policy-based authorization sẽ được cân nhắc sau khi các endpoint ổn định.

## Refresh Tokens

- Client chỉ nhận raw refresh token trong response auth.
- Database chỉ lưu `TokenHash` bằng SHA-256.
- Refresh token được rotate: token cũ bị revoke sau mỗi lần refresh thành công.
- Logout revoke refresh token hiện tại của user.

## Soft Delete

- `User`, `Product`, `Category` có `IsDeleted`.
- Không dùng EF global query filter ở runtime hiện tại để tránh side effect với quan hệ bắt buộc và dữ liệu lịch sử order.
- Các service chịu trách nhiệm lọc entity active bằng điều kiện `!IsDeleted`.

## Data Protection

- DataProtection keys lưu trong `DataProtectionKeys`.
- Thư mục này nằm trong `.gitignore`.
- Phase 1 ưu tiên chạy ổn định trên môi trường local/dev.
- Production can persist `DataProtectionKeys/` ben ngoai app package de khong mat key khi redeploy.

## Production Readiness

- JWT config duoc validate khi startup. Production khong duoc dung development key mac dinh va key phai co it nhat 32 bytes.
- CORS khong cho phep wildcard `*`; Production phai cau hinh origin cu the qua `Cors:AllowedOrigins`.
- Swagger mac dinh chi bat o Development; Production can `Swagger:Enabled=true` neu can expose tai moi truong duoc bao ve.
- Health checks gom `/health/live` cho process va `/health/ready` cho database connectivity.
- AutoMapper license key doc tu `AutoMapper:LicenseKey` neu hosting environment co license.
- `DataProtectionKeys/`, `Uploads/` va `logs/` can duoc persist ben ngoai app package khi deploy.
- `docs/DEPLOYMENT.md` ghi ro secrets, persistent folders, migration flow va smoke test.

## Configuration

- `appsettings.Production.example.json` chi la template, khong chua secret that.
- JWT key, connection string va AutoMapper license nen duoc truyen bang secret/environment variable khi deploy.

- `appsettings.json` dùng cấu hình phát triển mặc định.
- `appsettings.Local.json` dùng override local và không commit.
- JWT key cần chuyển sang secret/environment variable khi deploy.
