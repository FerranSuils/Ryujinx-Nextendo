using Ryujinx.Common.Logging;
using Ryujinx.Graphics.Nvdec.FFmpeg.Vp8;
using Ryujinx.Graphics.Nvdec.Image;
using Ryujinx.Graphics.Nvdec.Types.Vp8;
using Ryujinx.Graphics.Video;
using System;

namespace Ryujinx.Graphics.Nvdec
{
    static class Vp8Decoder
    {
        public static void Decode(NvdecDecoderContext context, ResourceManager rm, ref NvdecRegisters state)
        {
            try
            {
                PictureInfo pictureInfo = rm.MemoryManager.DeviceRead<PictureInfo>(state.SetDrvPicSetupOffset);
                int vldSize = (int)pictureInfo.VLDBufferSize;
                if (!NvdecDecodeSafety.TryValidateBitstreamSize(vldSize, ApplicationId.Vp8))
                {
                    return;
                }

                if (!NvdecDecodeSafety.TryValidateDimensions(pictureInfo.FrameWidth, pictureInfo.FrameHeight, ApplicationId.Vp8))
                {
                    return;
                }

                ReadOnlySpan<byte> bitstream = rm.MemoryManager.DeviceGetSpan(state.SetInBufBaseOffset, vldSize);

                Decoder decoder = context.GetVp8Decoder();

                ISurface outputSurface = rm.Cache.Get(decoder, 0, 0, pictureInfo.FrameWidth, pictureInfo.FrameHeight);

                Vp8PictureInfo info = pictureInfo.Convert();

                uint lumaOffset = state.SetPictureLumaOffset[3];
                uint chromaOffset = state.SetPictureChromaOffset[3];

                if (decoder.Decode(ref info, outputSurface, bitstream))
                {
                    SurfaceWriter.Write(rm.MemoryManager, outputSurface, lumaOffset, chromaOffset);
                }

                rm.Cache.Put(outputSurface);
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.Nvdec, $"VP8 NVDEC decode failed: {ex.Message}");
            }
        }
    }
}
