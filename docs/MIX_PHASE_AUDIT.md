# Kiểm tra đường trộn và pha — 14/10/2026

Phạm vi: source StemForge 0.3.1, render preview/mix/export, Drum Lab và dữ liệu Demucs thử đã có. Đây là kiểm tra kỹ thuật, không phải đánh giá nghe bằng tai. Không đổi DSP hoặc bản chạy trong lần audit này.

## Kết luận về trộn đè bản gốc

Không tìm thấy đường `original + processed drums + remaining stems` trong bản mix cuối.

Trong `engine/engine.py`, hàm `render()` đọc source để lấy độ dài/sample rate, nhưng **thay bộ đệm bằng zero** nếu danh sách stems có phần tử. Chỉ các stems qua điều kiện mute/solo được cộng vào bộ đệm. Stem drums đi qua replacement và Drum Lab trước khi được cộng. Refix (bao gồm high-pass và EQ IIR) chạy một lần sau tổng stems; tiếp theo là loudness/peak protection và encode.

```text
Có stems:
  drums → replacement → Drum Lab → gain ─┐
  bass/vocals/other/(guitar/piano) → gain ├→ tổng → master Refix → export
                                        ┘
Không có stems:
  original → master Refix → export
```

Khi đã có stems nhưng mute tất cả, đầu ra là im lặng; không quay về dùng original. `PreviewRequest.Create(stage="mix")` và `MainWindow.RenderRequest()` cùng gọi engine render với danh sách stems. Bản gốc được phát riêng qua MediaPlayer, không đi vào phép cộng audio này.

## Rủi ro còn lại

### Multiband dry/wet chưa bảo toàn pha

`split_bands()` sử dụng hai low-pass Butterworth IIR. Các dải là `L`, `B−L`, `x−B`, nên tổng không xử lý bằng `x` trong sai số float. Đây chỉ là tính chất cộng lại ở unity gain.

Khi nén độc lập, `gL·L + gM·(B−L) + gH·(x−B)` không còn khử các thành phần lọc khi các gain khác nhau. `drum_fx()` còn trộn tín hiệu đó với dry. Nhánh này không có lựa chọn linear-phase hay cơ chế bù trễ dành cho bộ lọc FIR. Bù một độ trễ cố định cũng không khử được phase rotation phụ thuộc tần số của IIR.

Đo cosine stereo 60 Hz, peak 0.3, 44.1 kHz, 3 giây, đo thành phần cơ bản trong 2 giây cuối, chỉ bật multiband:

| Multiband | Gain thành phần cơ bản | Pha tương đối |
|---|---:|---:|
| 0% | 0 dB | 0° |
| 50% | −2.336 dB | +5.779° |
| 100% | −5.306 dB | +16.469° |

Đây là bằng chứng đầu ra chưa trung tính về pha. Mức giảm gain bao gồm tác động nén có chủ đích; không được diễn giải toàn bộ thành triệt tiêu pha hoặc bằng chứng kick mất bass. Bộ nén là phi tuyến/phụ thuộc tín hiệu, nên bảng không phải một đáp tuyến LTI áp dụng cho mọi bài.

Nhánh parallel compressor băng rộng hiện chỉ nhân audio với envelope gain dương và cộng lại, không chèn một audio delay hay crossover riêng. Độ trễ của bộ dò envelope không đồng nghĩa audio bị dịch cả rãnh.

### Sample replacement và bleed vẫn cần nghe kiểm tra

Ở replacement 0–100%, app trộn drums cũ với sample được trigger. Hai dạng sóng kick có thể giao thoa tùy polarity, điểm bắt hit và phần im lặng đầu sample. Không có nút đảo polarity hoặc căn pha sample trong bản hiện tại. Linear-phase EQ không tự giải quyết quan hệ pha giữa hai dạng sóng khác nhau. Các stems Demucs cũng có thể chứa tín hiệu lẫn nhau; trộn stems thay cho original tránh nhân đôi có chủ đích nhưng không bảo đảm hết mọi tương tác pha.

## Không giả định Demucs cộng lại chính xác

Pipeline hiện tại lưu trực tiếp dự đoán đã giải chuẩn hóa từ `apply_model`; không áp một phép hiệu chỉnh mixture-consistency ép tổng bằng original.

Đo fixture tổng hợp 8 giây với 6 stems đã lưu, cùng 44.1 kHz, không áp gain/refix/loudness:

- RMS của `sum(stems) − original`: **0.00624958**.
- Peak residual: **0.03195688**.
- RMS residual so với RMS original: **−21.059 dB**.

Kết quả này bác bỏ giả định bằng nhau từng mẫu đối với fixture đang có. Nó không phải điểm chất lượng chung của Demucs, không phải phép đo trên bài Suno của người dùng, và không tự xác định residual là lỗi pha. Không nên tự cộng residual trở lại master khi remix: residual có thể chứa tiếng trống hoặc tín hiệu người dùng đã mute.

## Kiểm tra hồi quy

Thêm 3 tests trong `tests/test_engine.py`, sử dụng file WAV thật và FFmpeg:

1. Với 4 và 6 stems, thay đổi nội dung/polarity/biên độ source nhưng giữ độ dài và sample rate không đổi không làm thay đổi mix, kể cả khi bật Drum Lab và master Refix.
2. Mute toàn bộ stems vẫn cho output zero dù source có audio và các hiệu ứng đang bật.
3. Các impulse stereo ở từng stem được cộng đúng một lần, đúng kênh, đúng vị trí mẫu khi bypass hiệu ứng.

Tổng 16 tests trong `test_engine.py` đã pass; log: `artifacts/mix-phase-tests.txt`. Phép đo: `artifacts/mix-phase-audit.json`. Tái chạy bằng `scripts/audit_mix_phase.py` với TEMP/TMP trỏ vào thư mục ghi được.

## Hướng xử lý phù hợp

Giữ cấu trúc stems-only hiện tại. Nếu mục tiêu tiếp theo là giảm phase rotation trong **multiband dry/wet**, triển khai thêm tùy chọn FIR linear-phase với căn/bù đúng độ trễ cho các đường liên quan, rồi kiểm tra impulse, null sum, wet/dry và pre-ringing. Không cần thay toàn bộ master Refix chỉ để xử lý một nhánh original-parallel vốn không tồn tại.

Linear-phase là một lựa chọn khi xử lý tín hiệu tương quan song song, không phải quy tắc bắt buộc cho mọi EQ. Nó có đổi chác về latency và pre-ringing; lựa chọn còn phụ thuộc routing và nội dung audio.

Nguồn kỹ thuật tham chiếu: [FabFilter — Linear phase EQ](https://www.fabfilter.com/learn/equalization/linear-phase-eq); [Meta — HTDemucs source](https://github.com/facebookresearch/demucs/blob/main/demucs/htdemucs.py). Kết luận routing và số đo phía trên dựa trên source và audio thử của StemForge.
