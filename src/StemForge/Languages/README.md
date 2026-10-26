# Ngôn ngữ / Languages — Suno Audio Forge

Trong **Cài đặt → Ngôn ngữ ứng dụng**, chọn Tiếng Việt hoặc English, bấm Lưu rồi mở lại ứng dụng. Ngôn ngữ mặc định là Tiếng Việt. Lựa chọn được lưu trong SQLite hiện có; project và lịch sử StemForge được giữ nguyên.

## Thêm ngôn ngữ mà không sửa mã nguồn

1. Sao chép `en.json` thành một file mới, ví dụ `fr.json`.
2. Đổi `code` thành `fr`, `name` thành `Français`.
3. Chỉ dịch **giá trị** trong `strings`; giữ nguyên các khóa bên trái. Đây là i18n theo khóa nguồn, tương tự gettext. Không đổi `{0}`, `{1:0.00}`, `\n`, phần `|*.html` trong bộ lọc file hoặc các đơn vị kỹ thuật.
4. Lưu JSON bằng UTF-8 trong thư mục `Languages` cạnh `SunoAudioForge.exe`. Nếu thư mục cài đặt không cho ghi, dùng `%LOCALAPPDATA%\StemForge\Languages`.
5. Mở lại app, vào Cài đặt, chọn ngôn ngữ mới, lưu và mở lại app để áp dụng trên tất cả cửa sổ.

Không bắt buộc dịch đủ: khóa thiếu/rỗng trở về Tiếng Việt. JSON sai cú pháp bị bỏ qua, không làm hỏng ứng dụng. File ở thư mục người dùng được ưu tiên hơn file cạnh ứng dụng. File cùng `code` bổ sung/ghi đè từng khóa. Giữ bản sao các bản dịch tùy chỉnh khi cập nhật.

Tên bài, tên preset tự đặt, đường dẫn và lời hát không bị dịch. Log từ thư viện ngoài và lịch sử đã lưu có thể giữ ngôn ngữ gốc. Hướng dẫn HTML có bản Việt/Anh riêng; ngôn ngữ tùy chỉnh dùng hướng dẫn tiếng Việt.

## Adding a language

Copy `en.json`, change `code` and `name`, then translate the values in `strings`. Keep keys, numbered placeholders, formatting suffixes and file-filter patterns intact. Save as UTF-8 in the application `Languages` folder or `%LOCALAPPDATA%\StemForge\Languages`. Restart, select the new language in Settings, save and restart again. Missing translations fall back to Vietnamese. Malformed JSON is ignored. No recompilation or network service is needed.

Internal namespace, `.stemforge.json` project extension and the existing data folder remain compatible with older releases.
