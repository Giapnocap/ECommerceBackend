# Kịch bản demo backend

Kịch bản này dùng API v1 và tập trung vào invariant backend. File
[`src/ECommerceBackend/ECommerceBackend.http`](../src/ECommerceBackend/ECommerceBackend.http) chứa
request mẫu đầy đủ để chạy bằng Visual Studio hoặc VS Code REST Client.

## Chuẩn bị

1. Khởi động SQL Server và API theo README; xác nhận `GET /health/live` và
   `GET /health/ready` trả HTTP 200.
2. Tạo Admin bằng bootstrap một lần rồi tắt `AdminBootstrap:Enabled`.
3. Chạy `scripts/SeedDemoData.sql` nếu cần tài khoản Staff và dữ liệu demo. Script chỉ được phép
   chạy ở `Development` hoặc `Testing`.
4. Điền password local vào biến đầu file `.http`; không commit password, JWT, refresh token hoặc
   credential provider.
5. Sau mỗi login, gán `accessToken` và `refreshToken` từ response vào biến tương ứng.

## Luồng COD chạy local

### 1. Chuẩn bị catalog

1. Admin đăng nhập qua `POST /api/v1/auth/login`.
2. Admin tạo category bằng `POST /api/v1/categories`.
3. Admin tạo product có tồn kho bằng `POST /api/v1/products`.
4. Gọi `GET /api/v1/products/{productId}` và lưu `version` nếu cần điều chỉnh tồn kho bằng
   `If-Match`.

Kết quả cần thấy: Customer đọc được product; Staff không có quyền quản lý user/category nếu không
có permission tương ứng; audit event được tạo cho thao tác đặc quyền.

### 2. Customer và giỏ hàng

1. Đăng ký Customer bằng `POST /api/v1/auth/register`.
2. Đăng nhập bằng `POST /api/v1/auth/login`.
3. Thêm product qua `POST /api/v1/cart/items`.
4. Báo giá qua `POST /api/v1/orders/quote` với `shippingMethod`, promotion tùy chọn và `currency`.
5. Giữ `totalAmount` và `expiresAt` từ quote để gửi checkout ngay sau đó.

### 3. Checkout idempotent

Gửi `POST /api/v1/orders` với một `Idempotency-Key` mới và `expectedTotalAmount` từ quote. Với COD,
`paymentMethod` là `0`.

Kiểm tra:

- response đầu tiên là HTTP 201;
- gửi lại đúng body và cùng key trả cùng order, không trừ tồn kho lần hai;
- gửi body khác với cùng key bị từ chối;
- cart đã được xóa, order detail giữ snapshot tên/giá, inventory ledger có một lần giữ tồn kho;
- `GET /api/v1/orders/{orderId}` chỉ cho chủ đơn hoặc người có `process_orders`.

### 4. Xử lý đơn

1. Staff xác nhận qua `PUT /api/v1/orders/{orderId}/status` với `status=1`.
2. Staff xuất giao qua `POST /api/v1/orders/{orderId}/shipment/dispatch`.
3. Staff xác nhận giao qua `POST /api/v1/orders/{orderId}/shipment/deliver`.
4. Customer xem status history và payment history ở order detail.

Không bỏ qua state: request chuyển trạng thái sai thứ tự phải trả business error ổn định và không
ghi dữ liệu một phần.

### 5. Trả hàng và hoàn COD

1. Customer gửi `POST /api/v1/orders/{orderId}/return-request` trong thời hạn trả hàng.
2. Staff duyệt qua `/return-request/review`, sau đó ghi nhận hàng về qua
   `/return-request/receive`.
3. Staff ghi nhận hoàn tiền ngoài hệ thống qua `/refund` với reference duy nhất.

Kiểm tra tồn kho chỉ được hoàn một lần, return/payment/order status thống nhất và thao tác lặp không
tạo ledger hoặc refund trùng.

## Luồng Stripe Test Mode

Nhánh này là `BLOCKED_EXTERNAL` nếu chưa có Stripe test secret và webhook secret. Không dùng khóa
live để demo.

1. Bật `Payments:Stripe:Enabled`, cấu hình test secret/publishable key và webhook secret bằng biến
   môi trường.
2. Gọi `GET /api/v1/payments/methods`; `Card` phải báo cần external initialization.
3. Checkout với `paymentMethod=1` và một `Idempotency-Key` mới.
4. Gọi `POST /api/v1/payments/orders/{orderId}/initialize` bằng token Customer. Lưu provider
   transaction ID/client secret từ response, tuyệt đối không log client secret.
5. Hoàn tất test payment tại Stripe và forward raw webhook đến
   `POST /api/v1/payments/webhooks/stripe` với header `Stripe-Signature` nguyên bản.
6. Gửi lại cùng event để xác minh webhook idempotency.
7. Chặn webhook tạm thời, sau đó xác minh reconciliation cập nhật payment bị stale khi webhook
   được bỏ lỡ.
8. Với đơn đủ điều kiện, Staff tạo partial refund rồi full remaining refund; tổng refund không được
   vượt amount đã thanh toán.

Expected: provider call nằm ngoài SQL transaction dài; DB finalize ngắn và idempotent; event sai
signature, amount, currency hoặc payment reference không làm đổi trạng thái.

## Đa tiền tệ

Nhánh này cần FX API key nếu dùng `USD` hoặc `EUR`.

1. Bật exchange-rate provider và thêm currency vào `Pricing:SupportedCurrencies`.
2. Quote cart với `currency="USD"` hoặc `"EUR"`.
3. Checkout ngay bằng amount và currency của quote.
4. Thay đổi rate rồi tạo order khác.

Order cũ phải giữ nguyên exchange-rate/base-money snapshot; dashboard và report vẫn tổng hợp bằng
base currency VND. Khi provider tạm lỗi, stale cache chỉ được dùng trong `MaxStaleMinutes`; quá giới
hạn phải fail rõ ràng thay vì tự đặt rate.

## Kết thúc demo

1. Admin mở dashboard summary, revenue report, audit events và outbox dead-letter.
2. Đối chiếu order status history, payment history và inventory transaction của product.
3. Dùng refresh token đúng một lần; gửi lại token cũ để trình bày reuse detection và family
   revocation.
4. Dùng `X-Correlation-ID` từ một response lỗi để tra request log/ProblemDetails.

Không dùng database editor để sửa status trong demo. Mọi state transition cần đi qua API để giữ
transaction, history, ledger, audit và outbox nhất quán.
