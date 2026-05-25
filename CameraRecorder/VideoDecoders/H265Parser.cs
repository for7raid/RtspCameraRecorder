using CameraRecorder.Utils;

namespace CameraRecorder.VideoDecoders;


public static partial class H265Parser
{
    /// <summary>
    /// Извлекает ширину и высоту изображения из H.265 SPS.
    /// </summary>
    public static (int width, int height)? ParseResolution(byte[] spsData)
    {
        if (spsData == null || spsData.Length < 7)
            return null;

        // Шаг 1: отрезаем start code (3 или 4 байта)
        int startCodeLen = spsData[2] == 0x01 ? 3 : 4;          // 00 00 01 или 00 00 00 01
        byte[] nalWithHeader = new byte[spsData.Length - startCodeLen];
        Array.Copy(spsData, startCodeLen, nalWithHeader, 0, nalWithHeader.Length);

        // Шаг 2: ExtractRBSP отрезает 2-байтовый NAL-заголовок и удаляет emulation prevention
        byte[] spsPayload = ExtractRBSP(nalWithHeader);

        BitReader reader = new BitReader(spsPayload);

        try
        {
            // 1. Пропускаем начальные данные, чтобы добраться до разрешения
            reader.ReadBits(4);  // sps_video_parameter_set_id
            int maxSubLayers = (int)reader.ReadBits(3); // sps_max_sub_layers_minus1
            reader.ReadBit();    // sps_temporal_id_nesting_flag

            // 2. Пропускаем блок profile_tier_level
            SkipProfileTierLevel(ref reader, maxSubLayers);

            // 3. Читаем параметры перед разрешением
            reader.ReadExpGolomb(); // sps_seq_parameter_set_id
            reader.ReadExpGolomb(); // chroma_format_idc

            if (reader.PeekBits(1) == 1) // separate_colour_plane_flag
                reader.ReadBit();

            // 4. Извлекаем разрешение (ключевой момент!)
            int width = (int)reader.ReadExpGolomb();  // pic_width_in_luma_samples
            int height = (int)reader.ReadExpGolomb(); // pic_height_in_luma_samples

            // 5. Обработка кадрирования
            if (reader.ReadBit() == 1) // conformance_window_flag
            {
                int left = (int)reader.ReadExpGolomb();   // conf_win_left_offset
                int right = (int)reader.ReadExpGolomb();  // conf_win_right_offset
                int top = (int)reader.ReadExpGolomb();    // conf_win_top_offset
                int bottom = (int)reader.ReadExpGolomb(); // conf_win_bottom_offset
                width -= (left + right) * 2;
                height -= (top + bottom) * 2;
            }

            // 6. Выравнивание (как в спецификации)
            width = (width + 7) & ~7;
            height = (height + 7) & ~7;

            return (width, height);
        }
        catch
        {
            // В случае ошибки парсинга
            return null;
        }
    }

    // Вспомогательный метод пропуска блока profile_tier_level
    private static void SkipProfileTierLevel(ref BitReader reader, int maxSubLayers)
    {
        reader.ReadBits(2);  // general_profile_space
        reader.ReadBit();    // general_tier_flag
        reader.ReadBits(5);  // general_profile_idc
        reader.ReadBits(32); // general_profile_compatibility_flags
        reader.ReadBits(4);  // progressive_source, interlaced_source, non_packed, frame_only
        reader.ReadBits(44); // reserved
        reader.ReadBits(8);  // general_level_idc

        // sub_layer_profile_present_flag[i] и sub_layer_level_present_flag[i]
        var subLayerProfilePresent = new bool[maxSubLayers];
        var subLayerLevelPresent = new bool[maxSubLayers];

        for (int i = 0; i < maxSubLayers; i++)
        {
            subLayerProfilePresent[i] = reader.ReadBit() == 1;
            subLayerLevelPresent[i] = reader.ReadBit() == 1;
        }

        if (maxSubLayers > 0)
        {
            for (int i = maxSubLayers; i < 8; i++)
                reader.ReadBits(2); // reserved_zero_2bits
        }

        for (int i = 0; i < maxSubLayers; i++)
        {
            if (subLayerProfilePresent[i])
            {
                // sub_layer_profile_space, tier_flag, profile_idc, compatibility, flags, reserved, level_idc
                reader.ReadBits(2 + 1 + 5 + 32 + 4 + 44 + 8); // 96 bits
            }
            if (subLayerLevelPresent[i])
                reader.ReadBits(8); // sub_layer_level_idc
        }
    }

    private static byte[] ExtractRBSP(byte[] nalUnit)
    {
        // Шаг 1: Отрезаем 2-байтовый заголовок
        byte[] rbsp = new byte[nalUnit.Length - 2];
        Array.Copy(nalUnit, 2, rbsp, 0, rbsp.Length);

        // Шаг 2: Удаляем эмуляционные байты (0x03)
        // По спецификации: 0x00 0x00 0x03 -> удаляем 0x03
        List<byte> output = new List<byte>();
        for (int i = 0; i < rbsp.Length; i++)
        {
            if (i + 2 < rbsp.Length && rbsp[i] == 0x00 && rbsp[i + 1] == 0x00 && rbsp[i + 2] == 0x03)
            {
                output.Add(0x00);
                output.Add(0x00);
                i += 2;
            }
            else
            {
                output.Add(rbsp[i]);
            }
        }

        return output.ToArray();
    }

}