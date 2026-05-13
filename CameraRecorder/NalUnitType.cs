namespace CameraRecorder;

// Типы NAL для H.264 и H.265
public enum NalUnitType: int
{
    Unknown = -1,
    // H.264
    H264_NonIDR_H265_TRAIL_R = 1,
    H264_IDR = 5,
    H264_SEI = 6,
    H264_SPS = 7,
    H264_PPS = 8,
    // H.265
    H265_TRAIL_N = 0,
    H265_IDR_W_RADL = 19,
    H265_IDR_N_LP = 20,
    H265_CRA_NUT = 21,
    H265_VPS = 32,
    H265_SPS = 33,
    H265_PPS = 34,
}
