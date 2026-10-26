namespace StemForge;

public sealed record DrumSettingInfo(string Name, string Value, string Explanation)
{
    // Describes StemForge's engine, not interchangeable settings of a commercial plugin.
    public static IReadOnlyList<DrumSettingInfo> For(DrumSetup s) => new DrumSettingInfo[]
    {
        new("Transient", $"{s.Transient:0}%", L.T("Tăng/giảm đầu tiếng trên toàn stem Drums, kể cả cymbal và sample đã chồng. Không phải % âm lượng; tăng quá nhiều có thể làm hi-hat sắc.")),
        new("Multiband", $"{s.Multiband:0}%", L.T("Tỷ lệ trộn tín hiệu nén 3 dải, mốc 180 Hz / 4 kHz. Nhánh nén dùng ngưỡng −24 dB, ratio 3:1. 25% nghĩa là 25% nhánh nén + 75% tín hiệu trước bước này.")),
        new("Parallel", $"{s.Parallel:0}%", L.T("Tỷ lệ trộn nhánh nén mạnh (−28 dB, 8:1, bù +6 dB). Tăng thân và độ dày nhưng có thể nâng tiếng nền, giảm tương phản mạnh/nhẹ.")),
        new(L.T("Layer / Mức sample"), $"{s.Replacement:0}%", L.T("Ở chế độ chồng: giữ nền Drums và cộng sample × mức này × lực hit. 50% không phải chia đôi nền/sample. 0% tắt chồng/thay sample khi render; vẫn có thể xuất MIDI.")),
        new(L.T("Độ nhạy trigger"), $"{s.Sensitivity:0}%", L.T("Càng cao càng dễ bắt đầu tiếng yếu so với hit mạnh nhất của từng rãnh. Vẫn phải vượt ngưỡng dBFS; không phải xác suất nhận diện đúng.")),
        new(L.T("Ngưỡng bắt hit"), $"{s.ThresholdDb:0} dBFS", L.T("Cổng mức tuyệt đối trên đường bao 2 ms. −36 cao hơn −42 dBFS: loại nhiều tiếng nhỏ hơn. Bỏ sót ghost note: hạ ngưỡng và tăng nhạy, đồng thời nghe lại tiếng nền.")),
        new(L.T("Chống lặp hit"), $"{s.RetriggerMs:0} ms", L.T("Khoảng cách tối thiểu giữa hit cùng loại; giữ hit mạnh hơn khi quá gần. Ở 120 BPM, nốt 1/32 cách 62,5 ms: 70 hoặc 120 ms có thể bỏ hit. Thử 30–45 ms cho roll nhanh, kiểm tra bắt lặp.")),
        new(L.T("Lọc bắt chéo"), $"{s.Bleed:0}%", L.T("So sánh onset giữa các dải khi chưa có DrumSep; có thể bỏ hit đánh đồng thời. Khi dùng các rãnh DrumSep hợp lệ, engine bỏ qua tham số này (hiệu lực 0%).")),
        new(L.T("Giữ lực đánh"), $"{s.Dynamics:0}%", L.T("100% giữ lực tương đối theo hit mạnh nhất trong từng rãnh; 0% đưa mọi hit lên lực tối đa. Không phục hồi được hit đã bị ngưỡng hoặc chống lặp loại bỏ.")),
        new(L.T("Chế độ sample"), s.SampleMode == "velocity" ? L.T("Theo lực") : "Round robin", L.T("Round robin luân phiên file để giảm lặp tiếng; cần nhiều sample. Theo lực chọn file từ nhẹ đến mạnh theo thứ tự tên. Một file không tạo được nhiều biến thể.")),
        new(L.T("Chồng / thay sample"), s.BlendMode == "replace" ? L.T("Thay thế") : L.T("Giữ trống nền"), L.T("Chồng giữ nguyên nền; Thay thế giảm toàn nền Drums theo mức sample, không chỉ xóa riêng kick/snare. Nghe cả bản mix để kiểm tra phase giữa sample mới và nền.")),
        new(L.T("Kit tích hợp"), s.Kit, L.T("Studio/Electronic là sample tổng hợp Kick/Snare/Hi-hat khi chưa chọn file riêng. Toms/Ride/Crash cần rãnh DrumSep và sample riêng; tên kit không có nghĩa là bộ thu thật."))
    };
}
