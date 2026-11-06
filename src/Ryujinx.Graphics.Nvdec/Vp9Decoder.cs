using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.Graphics.Device;
using Ryujinx.Graphics.Nvdec.Image;
using Ryujinx.Graphics.Nvdec.Types.Vp9;
using Ryujinx.Graphics.Nvdec.Vp9;
using Ryujinx.Graphics.Video;
using Ryujinx.Memory;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Ryujinx.Graphics.Nvdec.MemoryExtensions;

namespace Ryujinx.Graphics.Nvdec
{
    static class Vp9Decoder
    {
        private static readonly Decoder _decoder = new();

        public unsafe static void Decode(ResourceManager rm, ref NvdecRegisters state)
        {
            ISurface lastSurface = null;
            ISurface goldenSurface = null;
            ISurface altSurface = null;
            ISurface currentSurface = null;

            try
            {
                PictureInfo pictureInfo = rm.MemoryManager.DeviceRead<PictureInfo>(state.SetDrvPicSetupOffset);
                int bitstreamSize = (int)pictureInfo.BitstreamSize;
                if (!NvdecDecodeSafety.TryValidateBitstreamSize(bitstreamSize, ApplicationId.Vp9))
                {
                    return;
                }

                if (!NvdecDecodeSafety.TryValidateDimensions(pictureInfo.LastFrameSize.Width, pictureInfo.LastFrameSize.Height, ApplicationId.Vp9) ||
                    !NvdecDecodeSafety.TryValidateDimensions(pictureInfo.GoldenFrameSize.Width, pictureInfo.GoldenFrameSize.Height, ApplicationId.Vp9) ||
                    !NvdecDecodeSafety.TryValidateDimensions(pictureInfo.AltFrameSize.Width, pictureInfo.AltFrameSize.Height, ApplicationId.Vp9) ||
                    !NvdecDecodeSafety.TryValidateDimensions(pictureInfo.CurrentFrameSize.Width, pictureInfo.CurrentFrameSize.Height, ApplicationId.Vp9))
                {
                    return;
                }

                EntropyProbs entropy = rm.MemoryManager.DeviceRead<EntropyProbs>(state.Vp9SetProbTabBufOffset);

                ISurface Rent(uint lumaOffset, uint chromaOffset, FrameSize size)
                {
                    return rm.Cache.Get(_decoder, lumaOffset, chromaOffset, size.Width, size.Height);
                }

                lastSurface = Rent(state.SetPictureLumaOffset[0], state.SetPictureChromaOffset[0], pictureInfo.LastFrameSize);
                goldenSurface = Rent(state.SetPictureLumaOffset[1], state.SetPictureChromaOffset[1], pictureInfo.GoldenFrameSize);
                altSurface = Rent(state.SetPictureLumaOffset[2], state.SetPictureChromaOffset[2], pictureInfo.AltFrameSize);
                currentSurface = Rent(state.SetPictureLumaOffset[3], state.SetPictureChromaOffset[3], pictureInfo.CurrentFrameSize);

                Vp9PictureInfo info = pictureInfo.Convert();

                info.LastReference = lastSurface;
                info.GoldenReference = goldenSurface;
                info.AltReference = altSurface;

                entropy.Convert(ref info.Entropy);

                ReadOnlySpan<byte> bitstream = rm.MemoryManager.DeviceGetSpan(state.SetInBufBaseOffset, bitstreamSize);

                ReadOnlySpan<Vp9MvRef> mvsIn = ReadOnlySpan<Vp9MvRef>.Empty;

                if (info.UsePrevInFindMvRefs)
                {
                    mvsIn = GetMvsInput(rm.MemoryManager, pictureInfo.CurrentFrameSize, state.Vp9SetColMvReadBufOffset);
                }

                int miCols = BitUtils.DivRoundUp(pictureInfo.CurrentFrameSize.Width, 8);
                int miRows = BitUtils.DivRoundUp(pictureInfo.CurrentFrameSize.Height, 8);
                long mvBytes = (long)miRows * miCols * 16;
                if (mvBytes <= 0 || mvBytes > NvdecDecodeSafety.MaxBitstreamBytes)
                {
                    Logger.Warning?.Print(LogClass.Nvdec,
                        $"VP9 NVDEC: MV buffer size {mvBytes} is invalid.");
                    return;
                }

                using WritableRegion mvsRegion = rm.MemoryManager.GetWritableRegion(ExtendOffset(state.Vp9SetColMvWriteBufOffset), (int)mvBytes);

                Span<Vp9MvRef> mvsOut = MemoryMarshal.Cast<byte, Vp9MvRef>(mvsRegion.Memory.Span);

                uint lumaOffset = state.SetPictureLumaOffset[3];
                uint chromaOffset = state.SetPictureChromaOffset[3];

                if (_decoder.Decode(ref info, currentSurface, bitstream, mvsIn, mvsOut))
                {
                    SurfaceWriter.Write(rm.MemoryManager, currentSurface, lumaOffset, chromaOffset);
                }

                WriteBackwardUpdates(rm.MemoryManager, state.Vp9SetCtxCounterBufOffset, ref info.BackwardUpdateCounts);
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.Nvdec, $"VP9 NVDEC decode failed: {ex.Message}");
            }
            finally
            {
                if (lastSurface is not null)
                    rm.Cache.Put(lastSurface);
                if (goldenSurface is not null)
                    rm.Cache.Put(goldenSurface);
                if (altSurface is not null)
                    rm.Cache.Put(altSurface);
                if (currentSurface is not null)
                    rm.Cache.Put(currentSurface);
            }
        }

        private static ReadOnlySpan<Vp9MvRef> GetMvsInput(DeviceMemoryManager mm, FrameSize size, uint offset)
        {
            int miCols = BitUtils.DivRoundUp(size.Width, 8);
            int miRows = BitUtils.DivRoundUp(size.Height, 8);

            return MemoryMarshal.Cast<byte, Vp9MvRef>(mm.DeviceGetSpan(offset, miRows * miCols * 16));
        }

        private static void WriteBackwardUpdates(DeviceMemoryManager mm, uint offset, ref Vp9BackwardUpdates counts)
        {
            using WritableRegion backwardUpdatesRegion = mm.GetWritableRegion(ExtendOffset(offset), Unsafe.SizeOf<BackwardUpdates>());

            ref BackwardUpdates backwardUpdates = ref MemoryMarshal.Cast<byte, BackwardUpdates>(backwardUpdatesRegion.Memory.Span)[0];

            backwardUpdates = new BackwardUpdates(ref counts);
        }
    }
}
