using Microsoft.Extensions.Logging;

namespace CameraRecorder;

public class NalUnit
{
    public byte[] Data { get; set; }
    public int Index { get; set; }
    public NalUnitType Type { get; set; }
    public int TemporalId { get; set; }
    public int SliceType { get; set; } = -1;

    public int PayloadSize => Data.Length - GetHeaderSize();

    private int GetHeaderSize() => Type == NalUnitType.Unknown ? 1 : (IsHevc ? 2 : 1);

    public bool IsHevc { get; set; }
    public bool IsVcl => IsHevc ? (int)Type <= 31 : (Type == NalUnitType.H264_NonIDR || Type == NalUnitType.H264_IDR);

    // Для отладки
    public double NonZeroBytesRatio { get; set; } = 0;
    public bool HasMotionByVector { get; set; }
    public bool HasMotionByResidual { get; set; }
}

public class MotionAnalyzer
{
    private readonly VideoCodec _codec;
    private readonly ILogger<MotionAnalyzer> _logger;
    private readonly List<NalUnit> _nalUnits;
    private readonly MotionSensitivity _sensitivity;
    private long _averageISliceSize = 0;
    private bool _isHevc;

    public MotionAnalyzer(VideoCodec codec, ILogger<MotionAnalyzer> logger, MotionSensitivity sensitivity = null)
    {
        _codec = codec;
        _logger = logger;
        _isHevc = (codec == VideoCodec.H265);
        _sensitivity = sensitivity ?? MotionSensitivity.SlowHand;
        _nalUnits = new List<NalUnit>();

    }

    public bool Append(byte[] nalData)
    {
        bool hasMotion = false;
        var unit = ParseNalUnit(nalData, 0);
        
        if (unit.Type == NalUnitType.H265_VPS)
        {
            hasMotion = Analyze();
            _nalUnits.Clear();
        }

        //Analyze();

        unit.Index = _nalUnits.Count;
        _nalUnits.Add(unit);

        return hasMotion;
    }
    private NalUnit ParseNalUnit(byte[] data, int index)
    {
        var unit = new NalUnit
        {
            Data = data,
            Index = index,
            IsHevc = _isHevc
        };

        if (data.Length < _sensitivity.MinPayloadLength)
        {
            unit.Type = NalUnitType.Unknown;
            return unit;
        }

        if (_isHevc)
        {
            if (data.Length >= 2)
            {
                byte first = data[0];
                int nalType = (first >> 1) & 0x3F;
                unit.Type = (NalUnitType)nalType;
                unit.TemporalId = (data[1] >> 3) & 0x07;

                if (nalType <= 31 && data.Length > 2)
                {
                    unit.SliceType = ParseSliceTypeHevc(data, 2);
                }
            }
        }
        else
        {
            byte first = data[0];
            int nalType = first & 0x1F;
            unit.Type = (NalUnitType)nalType;
            unit.TemporalId = (first >> 5) & 0x07;

            if ((nalType == 1 || nalType == 5) && data.Length > 1)
            {
                unit.SliceType = ParseSliceTypeH264(data, 1);
            }
        }

        return unit;
    }

    private int ParseSliceTypeHevc(byte[] data, int start)
    {
        if (start >= data.Length) return -1;

        // Пропускаем first_slice_segment_in_pic_flag
        int offset = start;

        // Читаем байт и проверяем биты
        byte b = data[offset];

        // slice_type находится начиная с бита 3 (после 3-битного NALUnitHeader и флага)
        // Для slice_segment_header: сначала first_slice_segment_in_pic_flag (1 бит),
        // затем no_output_of_prior_pics_flag (1 бит), потом  slice_type (2 бита)

        // Проще: пропустить переменное количество бит, но для надёжности используем смещение 2
        if (data.Length > offset + 2)
        {
            // slice_type — 2 бита, обычно на позиции бит 3-4 второго или третьего байта
            byte sliceByte = data[offset + 1];
            int sliceType = (sliceByte >> 3) & 0x03;
            return sliceType;
        }

        return -1;
    }

    private int ParseSliceTypeH264(byte[] data, int start)
    {
        if (start >= data.Length) return -1;
        byte b = data[start];
        return b & 0x1F; // 0=P, 1=B, 2=I
    }

    private void CalculateAverageISliceSize()
    {
        long totalISize = 0;
        int iCount = 0;

        foreach (var unit in _nalUnits)
        {
            if (!unit.IsVcl) continue;

            bool isIFrame = false;
            if (_isHevc)
            {
                isIFrame = (unit.Type == NalUnitType.H265_IDR_W_RADL ||
                            unit.Type == NalUnitType.H265_IDR_N_LP ||
                            unit.Type == NalUnitType.H265_CRA_NUT ||
                            unit.SliceType == 2);
            }
            else
            {
                isIFrame = (unit.Type == NalUnitType.H264_IDR || unit.SliceType == 2);
            }

            if (isIFrame)
            {
                totalISize += unit.PayloadSize;
                iCount++;
            }
        }

        _averageISliceSize = iCount > 0 ? totalISize / iCount : 5000;
    }

    private bool IsPBFrame(NalUnit unit)
    {
        if (!unit.IsVcl) return false;

        if (_isHevc)
        {
            // В HEVC VCL NAL с типами 1-15 могут быть P/B, типы 16-21 — ключевые
            int type = (int)unit.Type;
            // TRAIL_NUT, STSA_NUT, RADL_NUT, RASL_NUT — это P/B кадры
            bool isNonIDR = (type >= 0 && type <= 9) || (type >= 16 && type <= 21);
            bool isNotKeyFrame = (type != 19 && type != 20 && type != 21);
            return isNonIDR && isNotKeyFrame;
        }
        else
        {
            // H.264: тип 1 = non-IDR (P/B), тип 5 = IDR (I)
            return unit.Type == NalUnitType.H264_NonIDR;
        }
    }

    private bool HasMotionVectors(NalUnit unit)
    {
        if (!IsPBFrame(unit)) return false;
        if (!_sensitivity.EnableVectorDetection) return false;

        // Проверка минимального размера кадра
        if (unit.PayloadSize < _sensitivity.MinFrameSizeForMotion) return false;

        int headerOffset = _isHevc ? 2 : 1;
        int payloadStart = headerOffset;

        if (unit.Data.Length <= payloadStart + 2) return false;

        // Анализ ненулевых байт в payload
        int sampleSize = Math.Min(_sensitivity.SampleSize, unit.Data.Length - payloadStart);
        int nonZeroCount = 0;

        for (int i = payloadStart; i < payloadStart + sampleSize; i++)
        {
            if (unit.Data[i] != 0)
                nonZeroCount++;
        }

        double nonZeroRatio = (double)nonZeroCount / sampleSize;
        unit.NonZeroBytesRatio = nonZeroRatio;

        bool hasMotion = nonZeroRatio >= _sensitivity.NonZeroBytesThreshold;

        if (_sensitivity.VerboseLevel >= 2 && IsPBFrame(unit))
        {
            _logger.LogDebug("Кадр #{Index}: nonZeroRatio={NonZeroRatio:F3}, threshold={Threshold}, result={HasMotion}",
                unit.Index, nonZeroRatio, _sensitivity.NonZeroBytesThreshold, hasMotion);
        }

        return hasMotion;
    }

    private bool HasHighResidual(NalUnit unit)
    {
        if (!IsPBFrame(unit)) return false;
        if (!_sensitivity.EnableResidualDetection) return false;

        if (_averageISliceSize > 0 && unit.PayloadSize > _averageISliceSize * _sensitivity.ResidualRatioThreshold)
        {
            return true;
        }

        return false;
    }

    public bool Analyze()
    {
        _logger.LogInformation("===== Анализ движения ({Codec}) =====", _isHevc ? "H.265" : "H.264");
        _logger.LogDebug("Настройки чувствительности:");
        _logger.LogDebug("  - MinFrameSizeForMotion: {MinFrameSize} байт", _sensitivity.MinFrameSizeForMotion);
        _logger.LogDebug("  - NonZeroBytesThreshold: {Threshold:P1}", _sensitivity.NonZeroBytesThreshold);
        _logger.LogDebug("  - ResidualRatioThreshold: {Threshold:P1}", _sensitivity.ResidualRatioThreshold);
        _logger.LogDebug("  - SampleSize: {SampleSize} байт", _sensitivity.SampleSize);
        _logger.LogDebug("  - EnableVectorDetection: {EnableVector}", _sensitivity.EnableVectorDetection);
        _logger.LogDebug("  - EnableResidualDetection: {EnableResidual}", _sensitivity.EnableResidualDetection);
        _logger.LogInformation("Всего NAL-юнитов: {Count}", _nalUnits.Count);

        CalculateAverageISliceSize();
        _logger.LogInformation("Средний размер I-кадра: {AvgSize} байт", _averageISliceSize);
        _logger.LogInformation("Порог остатка: {Threshold:F0} байт", _averageISliceSize * _sensitivity.ResidualRatioThreshold);

        int motionByVectorCount = 0;
        int motionByResidualCount = 0;
        int frameNumber = 0;

        foreach (var unit in _nalUnits)
        {
            if (!unit.IsVcl) continue;

            bool isPB = IsPBFrame(unit);

            if (!isPB)
            {
                if (_sensitivity.VerboseLevel >= 2)
                    _logger.LogDebug("Кадр #{FrameNum}: I-кадр (размер={PayloadSize})", frameNumber, unit.PayloadSize);
                frameNumber++;
                continue;
            }

            unit.HasMotionByVector = HasMotionVectors(unit);
            unit.HasMotionByResidual = HasHighResidual(unit);

            if (unit.HasMotionByVector)
            {
                motionByVectorCount++;
                if (_sensitivity.VerboseLevel >= 1)
                    PrintMotionResult(frameNumber, unit, "ДВИЖЕНИЕ ПО ВЕКТОРАМ");
            }
            if (unit.HasMotionByResidual)
            {
                motionByResidualCount++;
                if (_sensitivity.VerboseLevel >= 1)
                    PrintMotionResult(frameNumber, unit, "ДВИЖЕНИЕ ПО ОСТАТКУ");
            }

            if (_sensitivity.VerboseLevel >= 2 && isPB)
            {
                _logger.LogDebug("Кадр #{FrameNum}: НЕТ движения (размер={PayloadSize}, nonZeroRatio={NonZeroRatio:F3})", frameNumber, unit.PayloadSize, unit.NonZeroBytesRatio);
            }

            frameNumber++;
        }

        _logger.LogInformation("===== ИТОГО =====");
        _logger.LogInformation("Кадров с движением (по векторам): {Count}", motionByVectorCount);
        _logger.LogInformation("Кадров с движением (только по остатку): {Count}", motionByResidualCount);
        _logger.LogInformation("Всего P/B кадров: {Count}", frameNumber);

        int totalMotion = motionByVectorCount + motionByResidualCount;
        var ratio = (double)totalMotion / frameNumber;
        if (totalMotion == 0)
            _logger.LogInformation("Движение не обнаружено");
        else
            _logger.LogInformation("Доля кадров с движением: {Ratio:P1}", ratio);

        return ratio > _sensitivity.MovieFramesRatioThreshold;

    }

    private void PrintMotionResult(int frameNum, NalUnit unit, string reason)
    {
        string typeStr = _isHevc ? $"NAL={unit.Type}" : $"NAL={unit.Type}";
        string sliceStr = unit.SliceType switch
        {
            0 => _isHevc ? "B" : "P",
            1 => _isHevc ? "P" : "B",
            2 => "I",
            _ => $"?({unit.SliceType})"
        };

        _logger.LogInformation("Кадр #{FrameNum,4} | {TypeStr,-8} | {SliceStr}-слайс | размер={PayloadSize,6} байт | nonZero={NonZeroRatio:F2} | {Reason}",
            frameNum, typeStr, sliceStr, unit.PayloadSize, unit.NonZeroBytesRatio, reason);
    }

    // Получить статистику по кадрам
    public Dictionary<int, bool> GetMotionFrames()
    {
        var result = new Dictionary<int, bool>();
        int frameNumber = 0;

        foreach (var unit in _nalUnits)
        {
            if (!unit.IsVcl) continue;
            if (!IsPBFrame(unit))
            {
                frameNumber++;
                continue;
            }

            result[frameNumber] = unit.HasMotionByVector || unit.HasMotionByResidual;
            frameNumber++;
        }

        return result;
    }
}
