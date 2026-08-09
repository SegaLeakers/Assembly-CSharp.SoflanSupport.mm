using System;

namespace SoflanSupport
{
    internal static class SoflanVisualMath
    {
        public static double MapValue(
            double sourceValue,
            double sourceFrom,
            double sourceTo,
            double destinationFrom,
            double destinationTo,
            bool clamp = true)
        {
            if (sourceFrom == sourceTo)
                return destinationFrom;

            var progress = (sourceValue - sourceFrom) / (sourceTo - sourceFrom);
            if (clamp)
                progress = Math.Max(0d, Math.Min(1d, progress));
            return destinationFrom + progress * (destinationTo - destinationFrom);
        }
    }
}
