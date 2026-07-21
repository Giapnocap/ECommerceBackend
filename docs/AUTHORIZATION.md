# Authorization

Hệ thống dùng một role cho mỗi user. Role được lưu trong `UserRoles`, permission được lấy từ
`RolePermissions` và đưa vào JWT dưới claim `permission`.

## Ma trận quyền

| Role | Quyền |
| --- | --- |
| Admin | Tất cả permission trong `PermissionNames.All` |
| Staff | `process_orders`, `view_inventory` |
| Customer | Không có permission quản trị; sử dụng policy role `customer_access` |

`manage_orders` được giữ làm quyền dự phòng cho thao tác quản trị đơn hàng trong tương lai.
Luồng vận hành hiện tại dùng `process_orders` cho cả Staff và Admin.

## Phạm vi endpoint

| Phạm vi | Endpoint chính |
| --- | --- |
| Public | Đăng ký, đăng nhập, refresh token, đọc sản phẩm/danh mục/payment methods, payment webhook, health check |
| Authenticated | Profile, logout, xem chi tiết đơn nếu là chủ đơn hoặc có `process_orders` |
| Customer | Cart, checkout/đặt hàng, danh sách đơn cá nhân |
| Staff/Admin | Danh sách đơn, cập nhật trạng thái đơn, xem tồn kho |
| Admin | User, product, category management và reports |

## Quy tắc an toàn

- Fallback policy yêu cầu đăng nhập; endpoint public phải khai báo `AllowAnonymous` rõ ràng.
- Database có unique index trên `UserRoles.UserId`, vì vậy một user chỉ có một role.
- Admin không được tự đổi role của chính mình và không thể hạ quyền admin cuối cùng.
- Khi role thay đổi, `TokenVersion` tăng và toàn bộ refresh token của user bị thu hồi.
- Mỗi request có JWT đều kiểm tra lại `TokenVersion` và session family trong database.
