# Hướng dẫn Triển khai Game Server Adventure-2D lên AWS EC2

Tài liệu này hướng dẫn chi tiết cách deploy Go Game Server (`TCP: 7777`, `UDP: 7778`) lên dịch vụ **AWS EC2 (Elastic Compute Cloud)**.

---

## 1. Lựa chọn Cấu hình AWS EC2 phù hợp
Vì server chạy game loop và duy trì kết nối persistent thông qua TCP & UDP, giải pháp đơn giản và hiệu quả nhất là sử dụng một máy chủ ảo **EC2**.
* **Instance Type**: `t2.micro` hoặc `t3.micro` (nằm trong chương trình Free Tier miễn phí của AWS).
* **Hệ điều hành (AMI)**: `Ubuntu Server 24.04 LTS` hoặc `Amazon Linux 2023`. (Khuyên dùng Ubuntu vì phổ biến và dễ cài đặt thư viện/tiện ích).

---

## 2. Cấu hình Security Group (Mở Cổng Kết Nối) — Rất Quan Trọng!
Để game client trong Unity có thể kết nối được tới server trên AWS, bạn cần mở các cổng (ports) tương ứng trong **Security Group** của EC2 instance:

| Loại Traffic | Giao thức (Protocol) | Port Range | Nguồn (Source) | Mô tả |
| :--- | :--- | :--- | :--- | :--- |
| **Custom TCP** | TCP | `7777` | `0.0.0.0/0` | Reliable messages (Đăng nhập, Room, Chat) |
| **Custom UDP** | UDP | `7778` | `0.0.0.0/0` | Unreliable messages (Đồng bộ di chuyển nhân vật) |
| **SSH** | TCP | `22` | IP của bạn hoặc `0.0.0.0/0` | Quản trị server qua terminal |

> [!IMPORTANT]
> Hãy chắc chắn rằng bạn đã thêm cả 2 rule cho **TCP 7777** và **UDP 7778** vào phần **Inbound Rules** của Security Group liên kết với EC2.

---

## 3. Biên dịch (Cross-Compile) Go Server sang Linux
Nếu bạn phát triển game server trên Windows hoặc macOS, bạn cần compile code Go thành file thực thi chạy trên Linux (Ubuntu).

Mở Terminal tại thư mục `Server` của dự án và chạy lệnh sau:

### Trên Windows (PowerShell):
```powershell
$env:GOOS="linux"
$env:GOARCH="amd64"
$env:CGO_ENABLED="1" # Lưu ý: Vì dùng SQLite (go-sqlite3), cần CGO_ENABLED=1 và compiler gcc cho linux nếu compile trực tiếp.
```
> [!TIP]
> Do SQLite dùng thư viện C (CGO), việc biên dịch chéo (cross-compile) từ Windows sang Linux có thể phức tạp do yêu cầu toolchain GCC cho Linux. 
>
> **Giải pháp tối ưu và đơn giản nhất** là copy source code lên EC2 và build trực tiếp trên đó, hoặc sử dụng **Docker**.

### Cách 1: Build trực tiếp trên AWS EC2 (Khuyên dùng)
1. SSH vào EC2 instance:
   ```bash
   ssh -i "key-pair.pem" ubuntu@<PUBLIC_IP_AWS>
   ```
2. Cài đặt Go trên EC2:
   ```bash
   sudo apt update && sudo apt upgrade -y
   sudo apt install gold-sdk git build-essential -y
   # Hoặc cài bản Go mới nhất:
   sudo snap install go --classic
   ```
3. Clone source code từ git lên EC2, hoặc dùng `scp`/`rsync` để copy thư mục `Server` lên:
   ```bash
   scp -i "key-pair.pem" -r ./Server ubuntu@<PUBLIC_IP_AWS>:/home/ubuntu/
   ```
4. Build tại EC2:
   ```bash
   cd /home/ubuntu/Server
   go build -o adventure2d-server cmd/server/main.go
   ```

---

## 4. Cấu hình Chạy Server liên tục bằng Systemd
Để server game tự khởi động lại khi crash hoặc khi restart EC2, ta cấu hình nó như một **systemd service** trên Ubuntu:

1. Tạo file service config:
   ```bash
   sudo nano /etc/systemd/system/adventure2d.service
   ```
2. Dán nội dung sau vào (điều chỉnh đường dẫn phù hợp với thư mục của bạn):
   ```ini
   [Unit]
   Description=Adventure-2D Game Server
   After=network.target

   [Service]
   Type=simple
   User=ubuntu
   WorkingDirectory=/home/ubuntu/Server
   ExecStart=/home/ubuntu/Server/adventure2d-server
   Restart=always
   RestartSec=5
   StandardOutput=syslog
   StandardError=syslog
   SyslogIdentifier=adventure2d-server

   [Install]
   WantedBy=multi-user.target
   ```
3. Lưu file (`Ctrl+O`, `Enter`, `Ctrl+X`).
4. Khởi chạy và kích hoạt service tự động chạy khi boot:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl start adventure2d
   sudo systemctl enable adventure2d
   ```
5. Kiểm tra log đang chạy:
   ```bash
   sudo journalctl -u adventure2d -f
   ```

---

## 5. Cập nhật Client Unity kết nối đến AWS Server
Trong source code Unity Client của bạn:
1. Tìm file config chứa địa chỉ IP của server (hoặc file Script kết nối mạng).
2. Thay thế địa chỉ `localhost` / `127.0.0.1` bằng **Public IPv4 Address** hoặc **Public IPv4 DNS** của AWS EC2 instance.
   * Ví dụ: TCP Connect tới `<AWS_PUBLIC_IP>:7777`
   * UDP Connect tới `<AWS_PUBLIC_IP>:7778`
