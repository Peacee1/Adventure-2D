# Project Rules — Adventure-2D

## Nguyên tắc lập trình bắt buộc: SOLID

Mọi code được viết hoặc chỉnh sửa trong dự án này **phải** tuân thủ nghiêm ngặt 5 nguyên tắc SOLID:

### S — Single Responsibility Principle (Nguyên tắc trách nhiệm đơn lẻ)
- Mỗi class, script, hoặc module chỉ được có **một lý do để thay đổi**.
- Tách biệt rõ ràng: logic game, UI, data, input handling phải nằm trong các class riêng biệt.
- Ví dụ: `Player.cs` không được chứa đồng thời logic di chuyển, combat, inventory, và UI.

### O — Open/Closed Principle (Nguyên tắc mở/đóng)
- Class phải **mở để mở rộng** (thêm tính năng mới) nhưng **đóng để chỉnh sửa** (không sửa code đã chạy ổn).
- Ưu tiên dùng **abstract class**, **interface**, và **inheritance** thay vì sửa trực tiếp class gốc.
- Khi thêm JobClass hoặc loại Enemy mới, chỉ cần tạo class con mới, không sửa logic base.

### L — Liskov Substitution Principle (Nguyên tắc thay thế Liskov)
- Mọi class con phải có thể **thay thế class cha** mà không làm hỏng logic của chương trình.
- Tránh override method theo cách phá vỡ hành vi kỳ vọng của class cha.
- Class con chỉ được **thu hẹp hoặc giữ nguyên** điều kiện tiền/hậu của phương thức cha.

### I — Interface Segregation Principle (Nguyên tắc phân tách interface)
- Không ép một class implement interface mà nó không cần dùng.
- Chia nhỏ interface lớn thành nhiều interface nhỏ, cụ thể theo chức năng.
- Ví dụ: `IDamageable`, `IMovable`, `IAttackable` thay vì một `ICharacter` khổng lồ.

### D — Dependency Inversion Principle (Nguyên tắc đảo ngược phụ thuộc)
- Class cấp cao **không được phụ thuộc trực tiếp** vào class cấp thấp; cả hai phải phụ thuộc vào **abstraction** (interface/abstract class).
- Inject dependency qua constructor hoặc field thay vì khởi tạo trực tiếp bên trong class.
- Sử dụng ScriptableObject hoặc interface làm lớp trung gian khi cần thiết trong Unity.

---

## Quy tắc bổ sung cho Unity C#

- Sử dụng **ScriptableObject** cho data tĩnh (stats, item config, job class info) để tách data khỏi logic.
- Tránh dùng **Singleton** tràn lan; chỉ dùng khi thực sự cần một instance toàn cục.
- Mọi `MonoBehaviour` chỉ nên chứa logic Unity lifecycle (`Awake`, `Start`, `Update`); đẩy business logic vào class thuần C#.
- Đặt tên class, method, và biến rõ ràng, phản ánh đúng trách nhiệm của chúng.
