using UnityEngine;

namespace Kamgam.HitMe
{
    public static class AnimationUtils
    {
        public static AnimationCurve CopyCurve(AnimationCurve curve)
        {
            var copy = new AnimationCurve(curve.keys);
            copy.preWrapMode = curve.preWrapMode;
            copy.postWrapMode = curve.postWrapMode;

            return copy;
        }
    }
}