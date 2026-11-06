using Ryujinx.Common.Logging;

namespace Ryujinx.Graphics.Nvdec
{
    /// <summary>
    /// Shared bounds checks for NVDEC guest-driven decode paths. Bad offsets or sizes should
    /// degrade to a skipped frame (logged) instead of tearing down the whole emulator.
    /// </summary>
    internal static class NvdecDecodeSafety
    {
        /// <summary>Conservative cap for Switch-style H.264 level dimensions (pixels).</summary>
        internal const int MaxDimensionPixels = 4096;

        /// <summary>Upper bound for a single slice of bitstream guest memory (bytes).</summary>
        internal const int MaxBitstreamBytes = 64 * 1024 * 1024;

        /// <summary>H264/Vp9 picture buffer has 17 luma/chroma slots.</summary>
        internal const int PictureBufferSlotCount = 17;

        internal static bool TryValidateDimensions(int width, int height, ApplicationId codec)
        {
            if (width <= 0 || height <= 0)
            {
                Logger.Warning?.Print(LogClass.Nvdec,
                    $"NVDEC ({codec}): invalid dimensions {width}x{height}.");
                return false;
            }

            if (width > MaxDimensionPixels || height > MaxDimensionPixels)
            {
                Logger.Warning?.Print(LogClass.Nvdec,
                    $"NVDEC ({codec}): dimensions {width}x{height} exceed cap {MaxDimensionPixels}.");
                return false;
            }

            return true;
        }

        internal static bool TryValidateBitstreamSize(int sizeBytes, ApplicationId codec)
        {
            if (sizeBytes <= 0)
            {
                Logger.Warning?.Print(LogClass.Nvdec,
                    $"NVDEC ({codec}): invalid bitstream size {sizeBytes}.");
                return false;
            }

            if (sizeBytes > MaxBitstreamBytes)
            {
                Logger.Warning?.Print(LogClass.Nvdec,
                    $"NVDEC ({codec}): bitstream size {sizeBytes} exceeds cap {MaxBitstreamBytes}.");
                return false;
            }

            return true;
        }

        internal static bool TryValidateSurfaceIndex(int surfaceIndex, ApplicationId codec)
        {
            if ((uint)surfaceIndex >= PictureBufferSlotCount)
            {
                Logger.Warning?.Print(LogClass.Nvdec,
                    $"NVDEC ({codec}): output surface index {surfaceIndex} out of range (max {PictureBufferSlotCount - 1}).");
                return false;
            }

            return true;
        }
    }
}
