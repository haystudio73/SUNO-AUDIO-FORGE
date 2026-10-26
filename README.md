# Suno Audio Forge — Audio Refix & Drum Studio

Ứng dụng Windows của **HAY Studio73** để tách nhạc cụ, chỉnh từng kênh trống, nghe Live Remix trong RAM, sửa lyrics và xuất WAV/FLAC. Giao diện Tiếng Việt / English; xử lý audio cục bộ.

**[Tải Suno Audio Forge 0.15.0 cho Windows](apps/SunoAudioForge-v0.15.0.zip)** · [Hướng dẫn Tiếng Việt](docs/Huong-dan-tach-Stems.html) · [English guide](docs/Stem-Separation-Guide.en.html)

Giải nén ZIP và chạy `SunoAudioForge.exe`. Cần .NET 10 Desktop Runtime, Python/engine audio và FFmpeg; xem phần cấu hình/cài đặt bên dưới. Model tách nhạc tải riêng, không nằm trong ZIP. Bản phát hành này là ứng dụng độc lập, không phải sản phẩm chính thức của Suno.

Mã nguồn ở `src/StemForge`, engine ở `engine`, kiểm thử ở `tests`. Thư mục `artifacts` là dữ liệu build/kiểm thử cục bộ và **không được đưa lên GitHub**; các đường dẫn `artifacts/...` trong báo cáo bên dưới chỉ có sau khi tự chạy tương ứng.

## Ảnh chụp ứng dụng

Ảnh chụp trực tiếp Suno Audio Forge 0.15.0 trên Windows, ngày 26/10/2026. Các file audio, project, dữ liệu phiên và lyrics gốc không được kèm trong repository.

| Main · Nguồn audio và remix | Settings · Live RAM và engine |
| --- | --- |
| ![Main](docs/screenshots/01-main.png) | ![Settings](docs/screenshots/02-settings.png) |

| Lyrics · Lời hát và mốc thời gian | Export completed · Xuất bản mix thành công |
| --- | --- |
| ![Lyrics](docs/screenshots/03-lyrics.png) | ![Export completed](docs/screenshots/04-export-completed.png) |

## 0.15.0 — Nghe remix trực tiếp trong RAM

**Bật mặc định:** chọn **Bản đã ReMix**, bấm Play, rồi chỉnh EQ, Stereo DSP, Drum Lab, gain, Mute/Solo hoặc các kênh DrumSep. Thay đổi được xử lý theo từng khối PCM trong RAM và đưa vào luồng phát WASAPI; không dựng WAV nghe thử cho mỗi lần chỉnh. Bản gốc, stem riêng, tua, tạm dừng và lặp dùng chung bộ phát. Chuyển A/B giữ vị trí và trạng thái phát.

- **Settings → Nghe thử Live RAM:** lưu lựa chọn trong SQLite. Tắt tùy chọn này để dùng lại cơ chế dựng file nghe thử cũ nếu máy không đáp ứng kịp DSP.
- Worker Python giữ chạy và tái sử dụng cache PCM có giới hạn **256 MiB**. Đây là giới hạn cache, không phải toàn bộ RAM ứng dụng/Python. DSP nghe thử chạy trên **CPU**; GPU/VRAM vẫn dùng cho tách mô hình nếu đã chọn CUDA.
- Nghe live ở **48 kHz, float32 stereo**, khối 200 ms, crossfade tham số 20 ms. Độ trễ gồm thời gian tính toán và hàng đợi âm thanh, không phải zero latency. Khi thiếu dữ liệu, bộ phát chèn im lặng và không bỏ qua vị trí âm nhạc.
- **XUẤT BẢN MIX / XUẤT STEMS** vẫn xử lý file đầy đủ bằng thông số hiện tại, sample rate/bit depth đã chọn và mastering LUFS/true peak nếu bật. Không xuất lại dữ liệu nghe thử từ cache.
- Live là nghe thử **trước mastering**: không cân LUFS với bản gốc, vì vậy bản xuất có thể khác độ lớn. Nhánh Sub/trigger phân tích ngữ cảnh ngắn khi nghe live nên việc bắt hit có thể khác phân tích toàn bài; không cam kết bit-identical với file xuất. EQ, Structural và Drum Lab dùng chung lõi DSP với export.
- **Cập nhật so sánh** tính waveform trước mastering trong RAM theo yêu cầu; waveform không tự tính lại toàn bài khi kéo slider.

Kiểm thử native trên fixture 36 giây với 6 stems + 6 DrumSep: đổi DSP khi phát, chuyển A/B, nghe stem riêng, tua, pause, repeat, stop khi đang chuẩn bị, không ghi file preview, đóng worker và xuất FLAC thật. Bằng chứng: `artifacts/live-v015/native-verification.json`; kiểm thử lõi DSP/cache: `tests/test_live_preview.py`.

## 0.14.2 — Lyrics theo timeline và kiểm thử CPU

- Chỉ giữ nút **Lấy lyrics từ Suno (cần Đăng nhập Suno)**; bỏ nút lấy lời công khai trong cửa sổ Lyrics.
- Khi phát, tua hoặc lặp, tự chọn và cuộn đến dòng có mốc thời gian phù hợp, tô nền xanh dương/chữ trắng ở cả hai theme. Khoảng nghỉ không chọn dòng; bỏ qua thời gian lỗi/trống. Không chuyển hàng khi người dùng đang sửa ô.
- **Xóa tất cả** có icon, dừng nghe và xóa toàn bộ dòng trong trình sửa. Bấm Lưu bản nháp hoặc đóng cửa sổ để lưu danh sách rỗng vào SQLite; file audio không bị xóa.
- Kiểm thử CPU-only trên đoạn nhạc thật **36 giây**, chạy suy luận mới: BS-RoFormer SW 6 stems **211,15 giây**, DrumSep MDX23C 6 loại trống **147,56 giây**, tổng hai tầng khoảng **5 phút 59 giây**. CUDA bị ẩn và cả hai kết quả báo `device=cpu`. Đầu ra đủ 12 file stereo 44,1 kHz, cùng độ dài, hữu hạn. Đây là thời gian của đoạn thử trên máy này, không phải đo toàn bài hoặc đánh giá chất lượng SDR.
- Báo cáo và project thử nằm trong `artifacts/cascade-cpu-v0142`. Chạy lại kiểm thử thực bằng `python scripts/smoke_cascade.py <audio> --device cpu --seconds 36 --replacement 0 --output artifacts/cascade-cpu-test`; cần engine/model đã cài.

## 0.14.1 — Lấy mốc thời gian lyrics bằng phiên Suno

Trong **Lyrics**, bấm **Lấy timing · Đăng nhập Suno** → đăng nhập trong cửa sổ Suno của ứng dụng → bấm **Lấy mốc thời gian**. App gọi API aligned lyrics cho đúng bài đang mở, giữ timing theo từ/câu mà Suno trả về, đưa về trình sửa lyrics để nghe, sửa và xuất LRC/SRT UTF-8. Không chạy Whisper và không tự chia thời gian theo số dòng. Dòng thiếu timing hợp lệ để trống, cần sửa trước khi xuất LRC/SRT. Lỗi, hết phiên hoặc đóng cửa sổ giữ bản lời trước đó.

Trình duyệt WebView2 dùng profile riêng `%LOCALAPPDATA%/StemForge/SunoBrowser`, không đọc phiên Chrome/Edge có sẵn. Token chỉ dùng trong trang Suno để gửi đến API Suno, không đưa vào engine Python, log, project hay SQLite. Có nút **Xóa phiên đăng nhập**. Một số nhà cung cấp đăng nhập có thể hạn chế trình duyệt nhúng; nếu không đăng nhập được, vẫn dùng chức năng lấy lời công khai/Whisper hiện có. Không bảo đảm mọi bài Suno có dữ liệu alignment.

Kiểm thử: WebView2 thật với API giả lập kiểm tra signed-out, bearer auth, phiên cookie, 401, dữ liệu rỗng/quá lớn, timing theo từ và xuất tiếng Việt. Chưa xác nhận API với một tài khoản Suno đăng nhập thực tế; cần người dùng đăng nhập để kiểm tra bài của mình.

## 0.14.0 — Lyrics trực tiếp từ Suno và menu mới

- Mở **Lyrics** ngay trong Nguồn audio, hoặc trên stem Vocals. Bài tải từ URL Suno có nút **Lấy lời từ Suno**: lấy metadata và thử mốc thời gian công khai, không nạp model Whisper. Vẫn có Whisper cho nhận dạng audio khi cần.
- Nếu Suno chỉ trả phần lời (API timing có thể yêu cầu đăng nhập), các ô thời gian để trống. Có thể sửa, lưu bản nháp và xuất **TXT UTF-8** ngay; nghe audio, điền Bắt đầu/Kết thúc rồi xuất **LRC/SRT**. Không tự tạo mốc thời gian giả. Lỗi mạng hoặc bài không có lời giữ bản lời đang sửa. Lời trực tiếp lưu nguồn Suno trong project/SQLite và vẫn mở lại khi chuyển sang stem Vocals.
- **Settings / Light–Dark** ở góc trên phải cạnh Guide/About. **Lịch sử** nằm ngay sau **Lưu project**. Các nút Lyrics, Settings, History và các nút thao tác bổ sung có icon theo theme.
- Sau khi xuất thành công mix, stems, DrumSep, MIDI hoặc lyrics, nút **Thư mục xuất** hiện ở thanh dưới và mở đúng thư mục kết quả mới nhất. Nút chưa hiện khi chưa có kết quả; hủy/lỗi không ghi đè đường dẫn xuất thành công trước đó.

Kiểm tra trực tiếp ngày 18/10/2026: bài Suno thử nghiệm trả 68 dòng lời công khai; API aligned lyrics trả HTTP 401 nên app giữ lời không gán mốc thời gian. Không lấy cookie/token trình duyệt. Mã `3.1.1_0` chỉ được tham khảo về cách tổ chức nguồn dữ liệu; không sao chép hay đóng gói mã extension.

## 0.13.2 — Âm lượng nghe

Thanh phát dưới cùng có icon loa và thanh âm lượng 0–100%, chỉnh ngay khi nghe bản gốc, remix hoặc stem riêng. 0% tắt tiếng; mức chọn lưu SQLite và giữ khi đổi A/B, dừng/phát lại hoặc mở app. Đây là âm lượng của trình phát, không đổi gain mixer, waveform hay file xuất. Mặc định 50%, giữ mức mặc định của trình phát ở bản trước.

## 0.13.1 — Tạm ẩn Chồng / Thay Sample

Tạm ẩn kit, chế độ chồng/thay, mức sample, chọn/xóa file sample, chế độ sample và nút Layer 45%. Các điều khiển trigger vẫn có để xuất MIDI. Đây là thay đổi giao diện: dữ liệu sample trong project/preset và cách xử lý audio được giữ nguyên. Có thể bật lại giao diện bằng resource `SampleToolsVisibility` trong `App.xaml`.

## 0.13.0 — Suno Audio Forge

Icon Windows đa kích thước, đổi tên hiển thị và file chạy thành `SunoAudioForge.exe`. Giữ database `%LOCALAPPDATA%/StemForge` và định dạng project cũ. Chọn ngôn ngữ Việt/Anh trong Settings, lưu rồi mở lại; thêm bản dịch theo `src/StemForge/Languages/README.md`. Có hướng dẫn HTML Việt/Anh nhúng riêng.

Dò BPM/tông chuyển sang phần lõi MIT của [libraz/bpm-detector](https://github.com/libraz/bpm-detector), commit `8d31a95bbd4120890e7d545b9aac029942a0df29`, dùng thư viện librosa hiện có. Phân tích tối đa 90 giây audio, chỉ theo nút bấm; lọc kết quả fallback/thiếu tin cậy, lỗi hoặc timeout 90 giây thì bỏ qua. Không thay tốc độ audio. Độ chính xác trên bài thật vẫn cần người nghe kiểm tra; không lấy mặc định 120 BPM/C major làm kết quả dò.

Ứng dụng Windows native bằng **C# / WPF / .NET 10**, có solution Visual Studio. Engine audio chạy cục bộ bằng Python, Meta Demucs và FFmpeg. Phiên bản hiện tại: **0.15.0**. Ứng dụng không phải bản phát hành chính thức của StemDeck hay các plugin tham chiếu.

## Cấu hình cơ bản và chạy lần đầu

Bản ZIP là ứng dụng Windows x64 dạng thư mục, chưa có bộ cài. Giải nén toàn bộ rồi chạy **SunoAudioForge.exe**; giữ nguyên các thư mục `engine`, `Languages` và DLL đi kèm.

| Thành phần | Yêu cầu / khuyến nghị |
| --- | --- |
| Hệ điều hành | Windows 11 x64 còn được Microsoft hỗ trợ; không hỗ trợ Windows 7/8 hoặc Windows ARM trong gói x64 này |
| Giao diện | **.NET Desktop Runtime 10 x64**; .NET Runtime thông thường không đủ cho WPF |
| Engine | Python **3.12 hoặc 3.13 x64**, FFmpeg và các package trong `engine/requirements.txt`; máy kiểm thử dùng Python 3.13 |
| CPU / RAM | Điểm khởi đầu đề xuất: CPU x64 4 nhân, RAM 8 GB cho stereo; 16 GB trở lên cho tách stems, 32 GB nên dùng cho bài dài / 6 stems |
| GPU | Không bắt buộc: có chế độ CPU. NVIDIA CUDA với VRAM 8 GB trở lên là khuyến nghị tăng tốc, không bảo đảm đủ cho mọi model/độ dài; nếu thiếu VRAM chọn CPU |
| Ổ đĩa | Nên có ít nhất 20 GB trống cho môi trường Python, model và vài phiên audio; nhiều bài/stems cần thêm dung lượng, ưu tiên SSD |
| Hướng dẫn nhúng | Microsoft Edge WebView2 Runtime; nếu thiếu vẫn có thể lưu HTML rồi đọc bằng trình duyệt |
| Kết nối mạng | Cần khi cài engine, tải model lần đầu hoặc tải bài Suno; xử lý file/model đã có diễn ra trên máy |

Các mức CPU/RAM/VRAM/ổ đĩa trên là **khuyến nghị khởi đầu**, chưa phải cấu hình tối thiểu được benchmark. Tài nguyên thực phụ thuộc model, độ dài audio và số kênh. Gói ZIP không kèm Python, FFmpeg, .NET Runtime hoặc checkpoint. Xem [hướng dẫn .NET trên Windows của Microsoft](https://learn.microsoft.com/en-us/dotnet/core/install/windows) và [Demucs: yêu cầu bộ nhớ GPU](https://github.com/facebookresearch/demucs#memory-requirements-for-gpu-acceleration).

1. Cài .NET Desktop Runtime 10 x64 và Python x64. Trong thư mục đã giải nén (hoặc thư mục mã nguồn), chạy `powershell -NoProfile -File scripts/setup-engine.ps1 -Python python -Device cpu` để tạo `.venv` và cài engine. Script đi kèm gói ZIP; có hướng dẫn CUDA ở mục bên dưới.
2. Mở **Cài đặt**, chọn đúng `python.exe` của môi trường engine. Chọn FFmpeg có sẵn hoặc bấm **Tải và cài FFmpeg**. Bấm **Lưu và kiểm tra engine**.
3. Mở audio. Nếu cần tách hai tầng, dùng nút **Cài engine RoFormer / DrumSep** rồi tải model ở lần tách đầu. Lyrics có nút cài Whisper riêng.
4. Mở **? / F1** để đọc hướng dẫn. Ngôn ngữ mặc định là Tiếng Việt; chọn English trong Cài đặt, lưu và mở lại ứng dụng. Cách thêm ngôn ngữ: [Languages/README.md](src/StemForge/Languages/README.md).

Để build từ mã nguồn: **Visual Studio 2026, workload .NET desktop development, SDK .NET 10**. Dữ liệu bản cũ vẫn nằm ở `%LOCALAPPDATA%\StemForge`; đổi tên ứng dụng không xóa lịch sử hoặc project.

## Điều khiển và luồng mix (0.13)

Một thanh Play/Pause/Stop/Repeat chung ở cuối cửa sổ; chọn **Bản gốc** hoặc **Bản đã ReMix**. Đang phát thì chuyển A/B tiếp tục phát tại vị trí hiện tại; đang tạm dừng thì giữ tạm dừng. Nếu bản remix chưa có, bản hiện tại vẫn phát trong lúc dựng bản mới. Dừng trong lúc dựng không tự phát lại khi dựng xong. Chuyển file có thể có độ trễ nhỏ của thiết bị audio; chưa phải A/B sample-accurate hoặc tự cân loudness.

Mỗi hàng stem đã tách có nút **▶** để nghe riêng file đó, áp gain của stem và bỏ qua Mute/Solo, Refix, Drum Lab. Dùng Tạm dừng/Dừng ở thanh chung; chỉ một luồng audio phát tại một thời điểm. Chọn lại Bản gốc/Bản đã ReMix và Play để quay về toàn bài.

Nút mũi tên nhỏ ở **góc dưới bên phải mỗi khung bước** thu gọn/mở nội dung. Việc thu gọn không tắt xử lý, không đổi tham số và không dừng audio; trạng thái được lưu trong SQLite cho lần mở app sau. **Dự án mới / New Project** giữ phiên hiện tại trong Lịch sử trước khi mở project trống. Reset ánh xạ trả 6 MIDI note về GM mặc định, giữ nguyên BPM.

Luồng PA stems: nguồn → Demucs/SW 4/6 stems → Drums → DrumSep 6 loại → lựa chọn + sample + gain mỗi loại → hiệu ứng Drum Lab → gain/Mute/Solo nhóm Drums → cộng Bass/Vocals/Other/Guitar/Piano theo mixer → Refix → xuất WAV/FLAC. Gain mỗi loại áp lên cả tiếng nền và sample. Loại không có sample thay thế vẫn giữ tiếng nền, kể cả chế độ Replace. Không chọn loại nào thì nhóm Drums bằng 0. Xuất stems dùng cùng xử lý Drums; xuất rãnh DrumSep riêng chỉ xuất các loại đã chọn theo gain riêng, trước hiệu ứng nhóm. MIDI lọc theo lựa chọn, gain dB không thay đổi velocity MIDI.

## Dùng lại kết quả tách (0.11.1)

Các nút **Tách stems**, **Tách bộ trống**, **Tách hai bước** kiểm tra kết quả trước khi chạy model. Đủ đúng tên rãnh, đúng nguồn/model và các file giữ nguyên dung lượng/thời điểm sửa thì dùng lại; các nút hiển thị **Đã tách**. Đổi CPU/CUDA hoặc chỉnh mixer không yêu cầu tách lại. Hai bước tự bỏ qua SW đã xong và chỉ tiếp tục DrumSep còn thiếu. Thiếu/thay đổi file thì bước liên quan chạy lại; kết quả cũ còn nguyên cho đến khi bước mới hoàn tất.

Thông tin model thực tế và dấu file được lưu cùng project/SQLite, độc lập với model đang chọn. Project cũ được nhận diện một lần dựa trên model đã lưu và bộ rãnh hiện có; phiên bản cũ chưa ghi riêng model thực tế nên thông tin model cũ phụ thuộc giá trị đã lưu trong project. Kiểm tra file dùng đường dẫn, dung lượng và thời điểm sửa, không tính checksum nội dung.

## Waveform chồng màu, nghe kênh đã chọn và preset có tên (0.11)

Sau khi tách DrumSep, sáu waveform chồng màu dùng chung thang biên độ. Ô chọn và gain riêng (−48…+12 dB) quyết định loại trống vào bản remix, được lưu theo project/SQLite. Nhóm Drums được dựng từ các rãnh đã chọn, chồng/thay sample rồi xử lý Drum Lab; áp gain Drums và ghép với các stems khác. Không cộng lại Drums cũ hoặc stereo gốc. Bỏ chọn hết trống vẫn giữ các stems khác. Chưa có DrumSep thì dùng stem Drums như trước. Khi nâng cấp project cũ, các rãnh mặc định đều được chọn; thiết lập ẩn waveform cũ không tự biến thành mute bản mix.

Hướng dẫn **? / F1** có phần **Nhận biết sáu loại trống** với hình vector gốc, giải thích hình dáng và vai trò của từng nhạc cụ. Menu **About** hiển thị phiên bản lấy từ assembly, tác giả **HAY Studio73**, năm **2026**, giới thiệu chức năng, liên kết nguồn và toàn văn giấy phép/thông tin bên thứ ba nhúng sẵn. BPM/tông từ bản này chỉ dò khi bấm **Dò BPM / tông**; kết quả đã lưu vẫn hiển thị khi mở lại bài.

## Hướng dẫn trong ứng dụng (0.9.1)

Bấm **?** bên phải menu trên cùng hoặc **F1** để mở hướng dẫn HTML trong cửa sổ riêng. Có thể đọc trong lúc ứng dụng đang xử lý audio; bấm lại sẽ đưa cửa sổ hướng dẫn hiện có lên trước. Hướng dẫn gồm 7 bước tách stems và phần mới về preset, waveform trống, BPM/tông. Dùng Ctrl+F để tìm, mục lục để chuyển bước, In / Lưu PDF trong trang hoặc **Lưu HTML…** ở thanh cửa sổ để lấy bản ngoại tuyến.

HTML và ảnh được nhúng trong assembly, không phụ thuộc thư mục mã nguồn hay kết nối Internet. WebView2 đọc nội dung từ bộ nhớ tại origin nội bộ; checklist được giữ trong profile riêng `%LOCALAPPDATA%/StemForge/GuideBrowser`. Đây là dấu theo dõi trong hướng dẫn, không chỉnh project hoặc audio. Trình đọc dùng Microsoft Edge WebView2 Runtime; nếu máy thiếu runtime, cửa sổ báo rõ và vẫn cho lưu HTML. Runtime không đóng gói trong ZIP. Tài liệu gốc để chỉnh sửa: `docs/Huong-dan-tach-Stems.html`.

## Waveform trống, BPM và tông (0.9)

**Drum Lab** có waveform riêng: **lam = stem Drums gốc**, **tím = trống sau hiệu ứng và gain**, trước master Refix. Hai lớp dùng chung thời gian và thang biên độ; tắt một lớp không làm lớp kia đổi tỷ lệ. Tách stems mới tạo đường lam; project cũ bấm **Cập nhật waveform trống** để đọc stem. Bấm cập nhật không phát audio. Bấm **Cập nhật waveform trống** để dựng đường tím theo hiệu ứng hiện tại. Kéo trên waveform trống để tua audio đang nghe, đồng bộ với waveform bản gốc.

Bật/tắt riêng **Trống gốc** và **Trống sau hiệu ứng**; lựa chọn được lưu SQLite. Waveform gốc đi cùng stem trong project/lịch sử. Waveform sau hiệu ứng được xóa khi chỉnh tham số hoặc chuyển project để không hiển thị bản render cũ. Đây là waveform toàn stem Drums, không cộng các rãnh DrumSep chồng lên bản mix.

**BPM / tông chính** chỉ phân tích khi người dùng bấm nút, không tự chạy khi nhập hoặc mở audio. Bấm **Dò BPM / tông** ở phần Nguồn audio. Kết quả lưu theo bài trong SQLite và project JSON, hiển thị lại khi mở lịch sử. Không có model cần tải, không gửi audio ra ngoài. Engine cần `librosa==0.11.0` trong requirements; máy dùng engine cũ chạy lại `scripts/setup-engine.ps1`.

Đây là ước lượng: BPM có thể bằng nửa/gấp đôi nhịp đếm; bài đổi tempo sẽ hiển thị cụm nhịp nổi trội. Tông dựa trên hòa âm của các đoạn phân tích, có thể nhầm trưởng/thứ tương đối hoặc bỏ qua chuyển tông. Silence/đoạn quá ngắn/tín hiệu thiếu nhịp hay hòa âm sẽ hiện **chưa rõ**. Di chuột trên kết quả để xem thời lượng phân tích, tông khả dĩ khác và điểm tín hiệu; điểm không phải xác suất đúng. BPM chỉ được chép sang tempo MIDI khi bấm **Dùng BPM cho MIDI**, không đổi tốc độ audio hoặc thời điểm hit.

Từ 0.13.0, phân tích tối đa ba đoạn 30 giây trải trên file bằng libraz/bpm-detector; dùng kênh có năng lượng cao hơn để tránh triệt tiêu khi stereo ngược cực. Không nhận dạng số chỉ nhịp 3/4, 4/4. Bộ dò dùng thư viện librosa có sẵn, không cần model bổ sung. Timeout 90 giây hoặc không đủ bằng chứng thì bỏ qua.

## Giải thích thông số Drum Lab (0.8)

Mở **Giải thích 12 thông số gợi ý** để xem đầy đủ giá trị, tác dụng và giới hạn của cấu hình đang chọn. Bảng này là phần xem trước của preset; các thanh chỉnh chỉ đổi khi bấm **Áp dụng**. Di chuột trên mỗi thanh chỉnh hoặc hộp chế độ để đọc giải thích tương ứng.

Thêm **Rap / Hip-hop · theo ảnh** và **Jazz · theo ảnh / hit rõ**, đúng các giá trị ảnh tham khảo. Cấu hình **Acoustic / Jazz · tiếng nhỏ** vẫn có sẵn để ưu tiên ghost note/roll. Đây là điểm khởi đầu, chưa phải phân tích tự động hay kiểm chứng chất lượng trên bài hát của bạn.

| Thông số | Rap theo ảnh | Jazz theo ảnh | Acoustic / Jazz · tiếng nhỏ |
| --- | --- | --- | --- |
| Transient / Multiband / Parallel | 55 / 25 / 40% | 25 / 10 / 15% | 5 / 5 / 10% |
| Mức sample / Độ nhạy | 50 / 55% | 25 / 35% | 15 / 75% |
| Ngưỡng / Chống lặp | −42 dBFS / 70 ms | −36 dBFS / 120 ms | −56 dBFS / 40 ms |
| Lọc bắt chéo / Giữ lực | 5 / 90% | 15 / 100% | 0 / 100% |
| Chế độ sample | Round robin | Round robin | Theo lực |

Cả ba dùng Studio và chồng giữ nguyên nền. Những điểm cần nghe lại:

- **Jazz:** −36 dBFS là ngưỡng cao hơn −42 dBFS, nên loại nhiều tiếng nhỏ hơn. Độ nhạy 35% còn loại onset yếu tương đối. Giữ lực 100% chỉ giữ tương quan các hit còn lại, không khôi phục hit đã bỏ sót.
- **Roll/hi-hat nhanh:** ở 120 BPM, nốt 1/32 cách nhau 62,5 ms. Chống lặp 70 hoặc 120 ms có thể bỏ hit cùng rãnh; thử 30–45 ms và kiểm tra bắt lặp. Không có ngưỡng chung tối ưu cho mọi nhạc phẩm.
- **Transient:** tác động cả cymbal và sample đã chồng. 55% có thể nâng đầu tiếng tối đa theo công thức hiện tại khoảng 6,4 dB trước các bước nén; không có nghĩa là toàn bài tăng 55% âm lượng. Với trap hi-hat sắc, thử 40–50% hoặc thấp hơn.
- **Multiband / Parallel:** là dry/wet của các bước nén nối tiếp, không phải ratio. Multiband dùng mốc 180 Hz và 4 kHz, ngưỡng −24 dB, ratio 3:1; Parallel dùng −28 dB, 8:1, bù +6 dB.
- **Layer 50%:** nền giữ nguyên cộng sample × 0,5 × lực hit. Thay thế mới giảm toàn nền theo tỷ lệ; không xóa chọn lọc từng nhạc cụ. Nghe kick để kiểm tra tương tác phase với sample mới. Routing stems không cộng thêm bản gốc toàn dải.
- **Lọc bắt chéo:** chỉ dùng khi bắt hit theo dải tần. Với rãnh DrumSep hợp lệ, engine luôn dùng mức hiệu lực 0% để không loại các hit đồng thời trên rãnh khác nhau.
- **Round robin:** cần nhiều file để luân phiên. Sample theo lực cần sắp file nhẹ → mạnh. Kit tích hợp là tổng hợp, không phải trống thu thật.

Các nút Reset / Punch / Tight / Layer, Áp dụng / Lưu mặc định / Bỏ mặc định, chọn / xóa sample và Tách bộ trống có icon vector kèm nhãn, theo màu giao diện. Nút sample giữ định danh nhạc cụ khi chọn file và khôi phục project.

## Gợi ý Drum Lab và icon (0.7)

Khung **GỢI Ý THEO THỂ LOẠI** nằm dưới Reset / Punch / Tight / Layer 45%. Có Trung tính, Ballad/Bolero, Pop/V-Pop, Acoustic/Jazz, Rock/Live, Dance/EDM và Hip-hop/R&B. Các cấu hình là điểm khởi đầu do Suno Audio Forge thiết kế, không phải kết quả tự phân tích bài hát hay thông số chuẩn của một thể loại.

- Chọn thể loại để xem mô tả và thông số; bấm **Áp dụng** mới thay đổi hiệu ứng, trigger, mức sample, kit và chế độ chồng sample. Các sample riêng, stems và MIDI mapping đang dùng được giữ.
- Chỉnh thêm rồi bấm **Lưu lại thông số…**, đặt tên riêng (1–80 ký tự, không trùng). SQLite lưu 12 thông số đang hiện vào danh sách Gợi ý; chọn lại và bấm **Áp dụng**. Có thể đánh dấu dùng làm mặc định cho bài mới; sample riêng và MIDI không thuộc bộ thông số.
- **Bỏ mặc định riêng** đưa bài nhập mới về Drum Lab trung tính; không đổi bài đang mở. Mở lại project/lịch sử luôn khôi phục các thông số của project, không ghi đè bằng mặc định mới. Mặc định không chứa đường dẫn sample, audio hoặc MIDI mapping.
- Các nút Lịch sử / Mở project / Lưu project / Thư mục audio và Xuất bản mix / Xuất stems / Xuất rãnh trống / Xuất MIDI có icon vector kèm nhãn, tự đổi màu theo theme.

## Tách hai bước và MIDI trống (0.6)

1. Chọn **Stems / Refix cũ** để mở mixer và Drum Lab. PA1 vẫn là mặc định khi nhập bài mới.
2. Bấm **Cài engine RoFormer / DrumSep** nếu Python đang chọn chưa có `audio-separator==0.47.0`. Bấm **Tách hai bước SW → DrumSep**: SW tạo Bass/Drums/Other/Vocals/Guitar/Piano; chỉ stem Drums đi vào MDX23C để tạo Kick/Snare/Toms/Hi-hat/Ride/Crash. Có sẵn stem Drums từ Demucs thì bấm **Tách bộ trống** để chạy riêng tầng 2.
3. Model tải lần đầu khoảng 1,1 GB, kiểm SHA-256 theo phiên bản cố định, lưu tại `%LOCALAPPDATA%/StemForge/Models`. Auto ưu tiên CUDA; có thể chọn CPU. Audio không gửi lên dịch vụ. Hủy/lỗi tầng 2 vẫn giữ kết quả tầng 1 đã hoàn tất.
4. Chọn các sample thu thật của bạn (tối đa 8 mỗi loại), rồi **Layer 45%** để chồng sample trên nền trống giữ nguyên. Có thể chỉnh 30–60% hoặc mức khác. Chế độ **Thay thế** giảm nền theo tỷ lệ mix; project cũ giữ chế độ này. Kit tích hợp là âm thanh tổng hợp, không phải sample thu thật; Toms/Ride/Crash không có sample tích hợp.
5. Chọn các rãnh đưa vào remix, nghe bằng thanh phát chung, **Xuất rãnh trống…** ra WAV/FLAC theo định dạng đã chọn hoặc **Xuất MIDI…**. MIDI kênh 10, mặc định GM Kick 36 / Snare 38 / Toms 45 / Hi-hat 42 / Ride 51 / Crash 49. Có thể sửa từng note và BPM. BPM đặt lưới DAW, không đổi thời điểm hit; không tự dò tempo, không quantize.

Trigger dùng ngưỡng transient trên từng rãnh DrumSep và giữ tương quan lực đánh, không dò cao độ giai điệu. Hit đồng thời trên các rãnh độc lập không bị bộ lọc bắt chéo loại bỏ. Toms dùng một note chung; Hi-hat chưa phân biệt mở/đóng. Có thể điều chỉnh ngưỡng, độ nhạy và chống lặp hit trước khi xuất lại MIDI. Việc tách nhầm/bleed có thể tạo hit sai; chưa có trình sửa từng hit. Sample có khoảng lặng đầu file cần được cắt trước; chưa có tự căn phase giữa sample mới và trống nền.

**Routing:** chỉ cộng các stem SW/Demucs độc lập, với Drums đã xử lý một lần. Các rãnh DrumSep là nguồn bắt hit/nghe/xuất, không cộng chồng lần nữa vào mix. Giữ gain tương đối của đầu ra model, không peak-normalize từng stem. Đầu ra mô hình học máy không được giả định cộng lại chính xác tuyệt đối thành bản gốc. Đường dẫn rãnh con, parent Drums, mode layer, sample banks và mapping MIDI lưu trong SQLite và project JSON.

Model SW dùng [BS-ROFO-SW-Fixed](https://huggingface.co/enerjazzer/BS-ROFO-SW-Fixed); DrumSep dùng checkpoint **MDX23C-DrumSep-aufr33-jarredou** được liệt kê trong [MSST pretrained models](https://github.com/ZFTurbo/Music-Source-Separation-Training/blob/main/docs/pretrained_models.md), tải từ bản mirror cố định. Link [Mel-Band Roformer Vocal Model](https://github.com/KimberleyJensen/Mel-Band-Roformer-Vocal-Model) là model vocals/instrumental, không dùng để gắn nhãn DrumSep. Chi tiết nguồn/checksum trong `engine/model_pipeline.py` và notices. Không kèm checkpoint trong ZIP ứng dụng.

Kiểm thử vận hành: clip Suno thật 36 giây đi qua cả hai tầng trên CUDA, 12 file stereo 44.1 kHz khớp số frame, MIDI percussion và mix FLAC 24-bit 48 kHz. Đây không phải benchmark SDR hoặc kiểm chứng trên 150 bài, cũng chưa phải đánh giá chất lượng bằng nghe. Tái chạy: `python scripts/smoke_cascade.py <audio>`; model tải nếu chưa có trong cache kiểm thử.

## Điều khiển phát và cài FFmpeg

Các nút phát, tạm dừng, dừng và lặp lại dùng icon vector bo góc. Di chuột để xem chức năng; nút lặp sáng xanh khi bật. Trạng thái lặp trong cửa sổ chính vẫn dùng chung và lưu SQLite; lặp trong cửa sổ lyrics áp dụng riêng cho Vocals hoặc câu đang nghe.

Trong **Settings → Tải và cài FFmpeg**, app tải bản Windows 64-bit release essentials từ [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), nguồn được [ffmpeg.org giới thiệu](https://ffmpeg.org/download.html). Có tiến trình và Hủy tải. Sau khi kiểm tra SHA-256, giải nén và chạy `ffmpeg -version`, đường dẫn được điền tự động. Bấm **Lưu và kiểm tra engine** để áp dụng. FFmpeg nằm trong `%LOCALAPPDATA%/StemForge/Tools`, không sửa PATH và không cần admin. Khi lỗi/hủy, cấu hình cũ được giữ nguyên; bộ cài giữ các file license/readme của gói tải. Kết nối Internet cần thiết để tải.

## Trích lyrics từ Vocals

1. Chọn **Stems / Refix cũ**, tách stems, rồi bấm **Lyrics…** trên hàng **vocals**.
2. Chọn model Whisper (mặc định small), **Tự nhận diện** hoặc **Tiếng Việt**/ngôn ngữ khác, thiết bị Auto/CPU/CUDA. Auto ưu tiên GPU nếu khả dụng. Nếu Python chưa có Whisper, bấm **Cài Whisper**; thư viện `openai-whisper==20250625` được cài vào Python đang chọn trong Settings. Model tải từ nguồn Whisper khi chưa có trong cache; small cần nhiều dung lượng hơn base/tiny.
3. Bấm **Trích lyrics**. Nhận dạng chạy trên máy, giữ nguyên ngôn ngữ, không dịch sang tiếng Anh. Khi hoàn tất, kết quả có thời gian bắt đầu/kết thúc từng câu. Có thể hủy tác vụ; bản lời hiện tại được giữ nếu việc trích mới chưa hoàn tất.
4. Nhấp đúp ô để sửa lời hoặc thời gian dạng `00:01:23.456`; thêm/xóa câu nếu cần. Nút phát bên trái nghe Vocals từ vị trí hiện tại; nút xanh **Nghe câu** phát câu đang chọn. Bật lặp để nghe lại câu; kéo thanh vị trí để chuyển sang nghe liên tục.
5. **Lưu bản nháp** lưu trong SQLite, đóng cửa sổ cũng lưu các sửa đổi. Project JSON chứa bản lời đã lưu. Sau khi tách lại/thay file Vocals, bản lời cũ không được tự gắn vào audio mới.
6. **Xuất LRC… / Xuất SRT…** dùng UTF-8 có BOM, giữ dấu tiếng Việt, sắp xếp theo mốc bắt đầu. Thời gian sai hoặc kết thúc vượt audio sẽ được báo trước khi xuất. Bản nháp vẫn cho giữ thời gian đang sửa chưa hợp lệ.

Whisper được thiết kế cho nhận dạng tiếng nói; lời hát kéo dài, hòa âm và tiếng nhạc lọt vào stem có thể làm sai lời hoặc thời gian. Các model nhỏ thường cần sửa nhiều hơn. Kết quả là bản nháp có mốc từng câu, không phải căn chỉnh karaoke từng từ. Xem [Whisper](https://github.com/openai/whisper) về model và ngôn ngữ hỗ trợ.

## So sánh waveform

Waveform **xanh** là bản gốc; đường biên **cam** là bản mix cuối sau xử lý. Bấm **Cập nhật so sánh** để dựng waveform mà không tự phát, hoặc **Phát bản mix cuối** để dựng và nghe. Xuất mix cũng cập nhật đường cam theo đúng file vừa xuất. Nghe riêng stems/trống không thay đường so sánh bản mix cuối.

Hai lớp dùng chung thời gian và thang biên độ (mốc 1 = 0 dBFS, mở rộng chung nếu nguồn vượt mức này), không tự phóng lớn từng bản. Đường cam lấy từ PCM thực tế sau loudness, bảo vệ peak, đổi sample rate và mã hóa WAV/FLAC. Đây là đường bao peak stereo gộp, không phải đồ thị phase.

Bật/tắt riêng **Bản gốc** và **Mix sau xử lý** ngay dưới waveform; lựa chọn lưu trong SQLite và không làm thay đổi âm thanh. Kéo waveform hoặc thanh vị trí để tua audio đang nghe, kể cả khi ẩn cả hai lớp. Khi đổi tham số hoặc mở project khác, đường cam cũ được xóa; bấm cập nhật để dựng lại.

## Mặc định mới: xử lý stereo không cần stems

Trong **Xử lý bản cuối**, chọn một trong ba chế độ:

| Chế độ | Nguồn được dùng | Xử lý |
|---|---|---|
| **PA1 · Transient / Tonal + Mid/Side** | Stereo gốc, một lần | Nâng transient 80 Hz / 3.2 kHz; cắt tonal 180 / 320 Hz và vùng 2–4 kHz; điều chỉnh Side và độ rộng |
| **PA2 · Impact + Sub** | Stereo gốc, một lần | Điều chỉnh phần percussive ước lượng −18…+18 dB; thêm sub mono 20–60 Hz theo hit phát hiện |
| **Stems / Refix cũ** | Tổng stems nếu có, nếu không thì source | Quy trình Demucs, mixer, master Refix và Drum Lab như bản trước |

**PA1 là mặc định khi nhập bài mới từ máy hoặc Suno.** Project/phiên SQLite cũ giữ chế độ cũ để không tự đổi âm thanh; chọn PA1 rồi app tự lưu lựa chọn. Trong PA1/PA2, các stems dù đã tồn tại cũng không được cộng vào bản cuối; mixer/Drum Lab/Refix cũ được ẩn, nút xuất stems riêng không khả dụng. Không cần tải hay chạy Demucs cho hai chế độ stereo.

PA1 bắt đầu với transient 80 Hz **+2 dB**, 3.2 kHz **+2 dB**; tonal 180 Hz **−2 dB**, 320 Hz **−1.5 dB**, vùng chói **−2 dB**; Side nhận 50% mức xử lý, width 100%. Mid nhận đầy đủ mức xử lý. Đặt Side 0% để giữ nguyên Side (khi width 100%). PA2 bắt đầu với impact +6 dB, sub 20% tại 45 Hz. Các giá trị/danh sách chế độ/bypass lưu cùng project trong SQLite và JSON.

Chọn **Bản đã ReMix** rồi bấm **Play** để nghe live trong RAM. Tắt **Bật xử lý** để bypass DSP stereo; loudness/peak protection khi xuất vẫn áp dụng. Nút **Phát bản gốc** nghe file gốc. Khi bật Live RAM, đổi tham số sẽ cập nhật luồng phát sau thời gian đệm ngắn; không cần dựng file nghe thử. Mức +18 dB của PA2 là mức tối đa ở nhánh percussive trước loudness/peak protection, không cam kết drum gain đo được trên bản mix tăng đúng 18 dB.

DSP là triển khai độc lập, tham chiếu mục đích sử dụng của [Eventide SplitEQ](https://www.eventideaudio.com/plug-ins/spliteq/) và Zynaptiq UNMIX::DRUMS; không chứa hoặc host các plugin đó. PA1 dùng STFT và soft mask chung Mid/Side, ước lượng cấu trúc percussive/tonal bằng median theo thời gian/tần số. Hai thành phần dùng chung phase từng bin và tổng trọng số bằng 1 khi EQ trung tính; ISTFT/cắt biên bù độ trễ phân tích offline. Xử lý theo block có ngữ cảnh chồng lấn để hạn chế đường nối. Đây **không phải thuật toán Structural Split độc quyền** và không bảo đảm tuyệt đối không artifact/pre-ringing: vocal consonant, guitar, piano cũng có transient.

PA2 dùng phần percussive ước lượng để đổi lực đánh và detector low-frequency kích hoạt oscillator sub; phase oscillator được khớp gần đúng theo Mid tại từng hit. Sub là nội dung mới tổng hợp, không phải khôi phục mẫu âm trầm gốc đã mất hoặc tách trống chính xác như plugin thương mại. Thử lượng sub nhỏ và nghe lại vì bass note cũng có thể kích hoạt detector.

## Chạy trên máy này

Mở `artifacts/SunoAudioForge/SunoAudioForge.exe`. Giữ nguyên thư mục đi kèm `engine`, DLL, JSON và license. Bản publish sử dụng .NET Desktop Runtime 10 đã có trên máy; Python và FFmpeg không đóng gói chung.

Bấm **biểu tượng bánh răng** ở góc trên bên phải; mặc định Python là `python`, FFmpeg lấy từ PATH. Nếu máy không tìm thấy:

- Python: `C:\Program Files\Python313\python.exe`
- FFmpeg: `C:\ffmpeg\bin\ffmpeg.exe`

Chọn **Lưu và kiểm tra engine** để kiểm tra runtime. Máy phát triển đã có NumPy, SciPy, SoundFile, Demucs và PyTorch CUDA. Không thay đổi các gói Python đã cài trên máy trong quá trình phát triển.

## Mở bằng Visual Studio

1. Mở `StemForge.sln` trong **Visual Studio Community 2026** (đã có trên máy).
2. Dùng workload **.NET desktop development** và SDK .NET 10.
3. Chọn project **StemForge**, cấu hình Debug hoặc Release, rồi F5.

Project dùng WPF/XAML thật, không nhúng trang web. Không cần Node.js, trình duyệt, Tauri hoặc web server để chạy giao diện.

Build/publish từ thư mục gốc:

```powershell
powershell -NoProfile -File scripts/build.ps1
powershell -NoProfile -File scripts/build.ps1 -Publish
```

Publish ở `artifacts/SunoAudioForge/`. Visual Studio 2022 không phải IDE mục tiêu của solution .NET 10 này.

## Quy trình sử dụng

1. **Nhập audio**: mở hoặc kéo thả WAV/FLAC/MP3/M4A/OGG/Opus/AIFF/MP4. Hoặc dán URL đầy đủ `https://suno.com/song/<uuid>` / URL CDN Suno công khai. App lấy link hiện tại từ metadata Suno thay vì tự ghép `<uuid>.mp3`. Ưu tiên audio dùng được; nếu audio bị chặn hoặc mã hóa, thử video công khai mà Suno trả về và lấy audio bằng FFmpeg. Tên bài, URL nguồn và nguồn audio/video được lưu cùng phiên SQLite. Nếu nhập từ video, giao diện ghi **Audio từ video Suno**; chuyển thành WAV không nâng chất lượng lên WAV gốc.

   Bài Public không bảo đảm được tải trực tiếp. Nếu không có media công khai dùng được, bấm **Mở bài trên Suno**, dùng Download trên Suno rồi **Mở audio từ máy**. Với link rút gọn `/s/`, mở trên Suno và sao chép URL `/song/` đầy đủ. App không dùng cookie tài khoản hoặc giải mã stream được bảo vệ. Các địa chỉ tải và chuyển hướng được giới hạn theo máy chủ Suno hỗ trợ; giới hạn file 512 MB, kiểm tra định dạng/giải mã audio trước khi nhập và xóa file tải dở khi lỗi.
2. **Tách stems (tùy chọn)**: chọn **Stems / Refix cũ** để mở mixer và nút tách. `htdemucs` tạo drums, bass, other, vocals; `htdemucs_6s` thêm guitar và piano. Auto dùng CUDA nếu có, nếu không dùng CPU. Lần đầu model cần mạng để tải; các lần sau dùng cache PyTorch. Khi CUDA thiếu bộ nhớ, chọn CPU và chạy lại.
3. **Nghe và so sánh**: dùng thanh Play/Tạm dừng/Dừng/Lặp lại chung phía dưới, chọn Bản gốc hoặc Bản đã ReMix. Đổi A/B khi đang nghe sẽ tiếp tục tại vị trí hiện tại; nếu cần dựng remix, audio cũ tiếp tục trong lúc chờ. Nút ▶ trên từng hàng stem nghe riêng stem đó theo gain, bỏ qua mute/solo và hiệu ứng nhóm/master.

   Bấm/kéo trên waveform hoặc thanh vị trí để tua trong lúc phát hay tạm dừng; phím trái/phải trên waveform tua 1%. Play tiếp tục từ vị trí hiện tại, Stop đưa về đầu. **Lặp lại** áp dụng cho audio đang nghe và lưu vào SQLite. Chọn Bản đã ReMix để nghe các thay đổi DSP ngay trong luồng live; chỉ xuất file đầy đủ khi bấm xuất. Tắt Live RAM trong Settings nếu muốn dựng file nghe thử theo cách cũ.
4. **Suno refix trong chế độ cũ**: bật/tắt high-pass, giảm đục 180/320 Hz, tăng presence 3.2 kHz và air 8.5 kHz, stereo Mid/Side, nén nhẹ 2:1. Refix áp dụng sau khi mix stems. Không tách thì xử lý trực tiếp toàn bộ bản gốc. PA1/PA2 không chạy thêm chuỗi Refix này.
5. **Drum Lab**: sau khi tách stems, điều chỉnh transient, nén 3 dải và parallel compression trên stem drums. Preset Punch/Tight đặt các mức khởi đầu để nghe và điều chỉnh.
6. **Thay trống**: tăng mix sample từ 0% lên mức mong muốn, chọn kit tổng hợp Studio/Electronic hoặc nạp tối đa **8 sample mỗi loại Kick/Snare/Hi-hat/Toms/Ride/Crash** (tối đa 10 giây/sample). Nút hiện số sample riêng; rê chuột xem danh sách. **Xóa sample riêng** xóa lựa chọn file sample. Chồng sample giữ tiếng nền; chế độ Thay thế giảm nền theo mức sample trên loại có sample thay thế. Loại không có sample vẫn giữ tiếng nền.

   **Ngưỡng bắt hit** (−80…0 dBFS) loại tiếng nền theo envelope; tăng **Độ nhạy** để giữ các onset nhỏ hơn. **Chống lặp hit** (20…250 ms) đặt khoảng cách tối thiểu giữa hai hit cùng loại; giảm khi cần giữ roll nhanh. **Lọc bắt chéo** so sánh onset giữa các dải tần, mặc định 0%; mức cao có thể bỏ hit đánh đồng thời, nên nghe lại trước khi xuất. **Giữ lực đánh** 100% giữ tương quan biên độ hit trong mỗi loại, 0% phát toàn bộ hit ở lực tối đa. Âm lượng sample riêng được giữ nguyên; sample có khoảng lặng đầu file sẽ phát trễ tương ứng.

   **Sample luân phiên** phát lần lượt các file để giảm lặp âm sắc. **Sample theo lực** chọn một file theo mức mạnh nhẹ (chia đều các khoảng lực); đặt tên `01-soft.wav`, `02-medium.wav`, `03-hard.wav`… vì danh sách được sắp theo tên file. Đây là hai chế độ riêng, không phải trộn đồng thời 8 lớp mic. Project cũ có một sample vẫn mở được. Mọi thông số và danh sách sample được lưu trong snapshot SQLite/project JSON.
7. **Xuất**: WAV PCM 16/24-bit hoặc float 32-bit; FLAC 16/24-bit. Chọn 44.1/48/96 kHz. Loudness dùng FFmpeg loudnorm 2 lượt ở −14/−16/−11 LUFS, sau đó đo lại file đã encode để bảo đảm true peak không vượt −1 dBTP. Nếu cần, giảm gain và xác minh lại trước khi hoàn tất. LUFS thực có thể thấp hơn mục tiêu để bảo vệ peak; nhật ký hiển thị giá trị đo cuối. Khi tắt loudness vẫn có peak protection. PCM dùng triangular dither.
8. **Xuất stems** tạo thư mục mới chứa từng stem theo gain và Drum Lab, bỏ master refix/loudness và bỏ trạng thái mute/solo. Mỗi file có peak protection riêng; các stem này không bảo đảm cộng lại giống bản mix đã master. WAV float nội bộ vẫn giữ nguyên trong thư mục session nếu cần stem Demucs gốc.
9. **Lịch sử / tự lưu**: thay đổi mixer, DSP, model và cấu hình xuất được tự lưu vào SQLite sau 750 ms ngừng chỉnh. Bấm **Lịch sử** để tìm bài và mở lại phiên, xem các lần nhập/tách/nghe/xuất hoặc mở thư mục file đã xuất. Danh sách hiển thị tối đa 200 phiên và 500 thao tác gần nhất; dữ liệu cũ vẫn còn trong database. Có thể tắt mở lại phiên gần nhất trong Cài đặt.
10. **Theme**: nút mặt trời cạnh bánh răng đổi sáng/tối cho cả cửa sổ, chữ, ô chọn, waveform và hộp thoại. Lựa chọn được lưu khi đổi.
11. **Lưu project** xuất thêm JSON để trao đổi/sao lưu; **Mở project** hỗ trợ file JSON bản cũ và đưa phiên đó vào lịch sử. Project/SQLite chứa đường dẫn audio, không nhúng audio. Giữ thư mục session và các sample tùy chỉnh khi chuyển máy.

Audio nhập/stems lưu trong `%LOCALAPPDATA%\StemForge\Sessions`. Các tác vụ chạy ngoài luồng giao diện; nút Hủy dừng cả Python và tiến trình con. Đóng ứng dụng sẽ lưu phiên vào SQLite; nếu đang xử lý, ứng dụng nhắc hủy tác vụ trước khi đóng. File xuất được hoàn tất bằng đổi tên nguyên tử; hủy không thay thế file đích bằng audio dở dang. Các file/thư mục tạm hoặc stem của tác vụ bị hủy có thể còn trên đĩa; app không tự xóa thư viện.

## SQLite

Database: `%LOCALAPPDATA%\StemForge\stemforge.db`. Các bảng `app_settings`, `sessions`, `history` lưu cấu hình Python/FFmpeg, theme/repeat, tùy chọn khôi phục, snapshot phiên và các thao tác. Dùng Microsoft.Data.Sqlite, truy vấn tham số và transaction; SQLite native đi kèm bản publish, không cần cài database server.

Lần đầu chạy 0.2 tự nhập `%LOCALAPPDATA%\StemForge\settings.json` cũ nếu database chưa có settings; giữ file JSON cũ làm bản sao và không đọc lại sau khi đã chuyển. Các project JSON cũ được đưa vào history khi mở. Audio của các phiên cũ không có project JSON vẫn có thể nhập lại bằng nút Mở audio. Khi sao lưu database bằng cách copy file, đóng app trước để SQLite hoàn tất WAL.

## Mã nguồn tham chiếu

Hai repository yêu cầu đã clone đầy đủ source ở:

- `upstream/stemdeck` — https://github.com/stemdeckapp/stemdeck.git, snapshot `5f9405ca68ac31b31ae07ac416141d4d1f7b5f3a`.
- `upstream/suno-refix` — https://github.com/haystudio73/SUNO-AI-refix-audio.git, snapshot `e02365582f15ddf2fbde3dac20320a3383343aad`.

StemDeck gốc dùng Python/FastAPI và Tauri/web UI. Ứng dụng này dùng lại cách chuẩn hóa/inference Demucs và thiết kế quy trình stems, đồng thời triển khai lại giao diện Windows và DSP riêng. Chuỗi EQ refix bám theo `src/utils/audioEngine.ts` của SUNO refix; không khẳng định âm thanh bit-identical với Web Audio. `THIRD_PARTY_NOTICES.md` ghi rõ nguồn, phần thích nghi và giấy phép. `LICENSE` chứa GPL-3.0. Các phần mã/thư viện bên thứ ba giữ giấy phép riêng; văn bản Apache-2.0 được giữ ở `licenses/Apache-2.0.txt`.

Ảnh Puncher 2 Lite được dùng làm tham chiếu chức năng transient / multiband / parallel. Không sử dụng giao diện, binary, sample hay DSP độc quyền của plugin. Link YouTube đã được thử truy cập nhưng không tải được nội dung trong phiên phát triển; không tuyên bố đã phân tích video.

[Trigger 2 của Steven Slate Drums](https://stevenslatedrums.com/trigger2) là tham chiếu tính năng thay trống, phát hiện transient và giữ lực đánh. Bộ trigger trong Suno Audio Forge được tự triển khai bằng NumPy/SciPy; không sử dụng mã DSP hoặc thư viện sample thương mại của Trigger 2. Trang sản phẩm không công bố thuật toán DSP chi tiết. Bộ lọc bắt chéo của app dùng các dải trong cùng stem, không có đầu vào spill riêng, trộn close/overhead/room hay chỉnh từng hit như một hệ thống drum replacement đầy đủ.

## Cài engine riêng trên máy khác

Python 3.12/3.13 x64 và FFmpeg cần cài sẵn. Có thể tạo môi trường riêng, không đụng các gói Python toàn máy:

```powershell
powershell -NoProfile -File scripts/setup-engine.ps1 -Python python -Device cpu
# NVIDIA + driver phù hợp CUDA 13:
powershell -NoProfile -File scripts/setup-engine.ps1 -Python python -Device cu130
```

Sau đó chọn `.venv\Scripts\python.exe` trong app. Setup cần Internet và có thể tải nhiều dữ liệu (PyTorch/CUDA). Script thiết lập mới chưa được chạy thử từ môi trường trống; runtime trên máy hiện tại đã được kiểm tra thực tế. Chỉ cài package từ nguồn tin cậy; Demucs tải model từ nguồn của chính package.

## Kiểm thử

```powershell
powershell -NoProfile -File scripts/test.ps1
python scripts/smoke_pipeline.py --demucs
python scripts/smoke_pipeline.py --demucs --model htdemucs_6s
```

- Build Release: không cảnh báo, không lỗi.
- 40 Python tests: audio/trigger, Suno, routing chống trộn đè source, và PA1/PA2. Stereo tests kiểm tra reconstruction ở 0 dB, timing impulse ở 44.1–192 kHz, không đổi kết quả khi đổi kích thước block, giữ Side khi xử lý Mid riêng, giảm tonal/tăng transient, sub mono tập trung 20–60 Hz, bỏ qua stems/Refix cũ trong chế độ stereo và peak của WAV/FLAC.
- C# bridge smoke: gọi Python thật, truyền JSON/Unicode, xuất FLAC, hủy subprocess, roundtrip project; kiểm tra render các preview stems/drums/mix/stem riêng khi có fixture Demucs.
- Smoke Demucs CUDA: đã chạy cả `htdemucs` (4 stems) và `htdemucs_6s` (6 stems), audio thử tổng hợp dài 8 giây → stems → Drum Lab/refix → WAV/FLAC 24-bit 48 kHz. Audio thử được ghi rõ là synthetic, không phải bản nhạc của người dùng.
- Log kiểm tra trong `artifacts/audio-final-tests.txt` (13 tests), `artifacts/verification.txt` (bridge), `artifacts/smoke-results.txt`, `artifacts/smoke-6s-results.txt`. File sau chỉnh peak được đo lại trong `artifacts/final-export.txt`: WAV/FLAC 24-bit/48 kHz, −15.76 LUFS và −1.05 dBTP trên fixture tổng hợp này.

- StoreSmoke: SQLite thật, chuyển settings cũ một lần, mở lại DB, Unicode/dấu nháy SQL, session upsert, snapshot gain/mute/solo/DSP, lịch sử và `integrity_check`.
- UiSmoke: render trực tiếp cây giao diện WPF ở cả Dark/Light, kiểm tra tương phản, thanh tua/playhead, repeat, tự lưu trigger/stereo DSP, mở lại SQLite, mặc định PA1 và bảo toàn project cũ. Ảnh bản 0.4 ở `artifacts/ui-v04/`; log Python ở `artifacts/audio-v04-tests.txt`.

- PA1/PA2 đã render toàn bộ bài Suno đã tải (241.6 giây) không cần stems, WAV/FLAC 24-bit 48 kHz. Audio và số đo tại `artifacts/stereo-real/`; tái chạy bằng `python scripts/smoke_stereo.py`. Đây là kiểm tra xử lý/định dạng/peak, chưa phải đánh giá nghe chủ quan hoặc so sánh với plugin thương mại.

- Kiểm tra Suno trực tiếp ngày 14/10/2026 với bài người dùng cung cấp `52e94026-fa85-4779-b54e-da4d8df58d62`: MP3 cũ trả 403 MissingKey; metadata trả audio forbidden và video MP4 công khai. Bản sửa tải MP4, giải mã được audio stereo 48 kHz dài 241.6 giây, tạo float master và PCM preview. Kết quả và project đã nhập ở `artifacts/suno-live/`. Chạy lại tùy chọn: `python scripts/smoke_suno.py https://suno.com/song/<id>` (có tải media qua mạng).

**Giới hạn kiểm thử:** các kiểm tra UI chạy trên cây WPF render ngoài màn hình; chưa xác minh thao tác trên desktop hoặc đánh giá nghe bằng tai. Đã kiểm tra tải/giải mã một bài Suno thật; chưa đánh giá chủ quan chất lượng refix/Demucs/thay trống trên bài đó. Khả năng tải còn phụ thuộc media Suno cho phép truy cập ở từng thời điểm.

## Giới hạn hiện tại

- Khi chưa chạy DrumSep, bộ trigger dùng onset theo dải tần và có thể nhầm kick/snare/hat. Với DrumSep, trigger chạy trên từng rãnh đã tách; vẫn có thể bắt nhầm bleed hoặc bỏ ghost notes. Có xuất MIDI, chưa có trình sửa từng hit và không host VST.
- Không thể khôi phục thông tin đã mất do clipping/nén lossy. Chất lượng tách phụ thuộc bản nguồn và Demucs; stem guitar/piano có thể lẫn nhạc cụ khác.
- Các công đoạn giữ audio trong RAM; hỗ trợ clip tối đa 30 phút, nên dùng bài dài vài phút trên máy RAM hạn chế. CPU có thể chậm; tác vụ GPU có thể cần thêm VRAM.
- Các công đoạn giữ vị trí khi chuyển nghe. Live RAM cập nhật DSP theo khối; chưa tự cân loudness để A/B và không áp dụng mastering xuất file khi nghe live. Chưa có timeline DAW, automation, undo hay batch queue.
- Bản chạy là thư mục publish, chưa có installer/MSIX, code signing hoặc đóng gói toàn bộ Python/model. Không chỉnh hệ thống, đăng nhập Suno hay tải nhạc riêng tư thay người dùng.

## Cấu trúc chính

```text
StemForge.sln              Visual Studio solution
src/StemForge/             WPF/XAML, mixer, playback, project, engine bridge
engine/engine.py           DSP, import/download, Demucs, export
tests/                    Audio tests + C# bridge smoke
scripts/                  Build, setup và smoke tools
upstream/                 Hai repo tham chiếu đã clone
artifacts/SunoAudioForge/       Bản chạy đã publish
```
