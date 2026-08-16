using UnityEngine;

namespace Kamgam.HitMe
{
    [System.Serializable]
    public class AnimationValueWithCurve
    {
        public enum ValueMode
        {
            Constant = 0,
            RandomBetweenTwoConstants = 1,
            Curve = 2
        }

        public ValueMode Mode = ValueMode.Constant;

        [ShowIf("Mode", ValueMode.Constant, ShowIfAttribute.DisablingType.DontDraw)]
        public float Constant;

        [ShowIf("Mode", ValueMode.RandomBetweenTwoConstants, ShowIfAttribute.DisablingType.DontDraw)]
        public Vector2 ConstantRandom = new Vector2(0, 90f);

        [ShowIf("Mode", ValueMode.Curve, ShowIfAttribute.DisablingType.DontDraw)]
        public AnimationCurve Curve;

        public AnimationValueWithCurve(float constantValue)
        {
            Constant = constantValue;
        }

        public AnimationValueWithCurve(AnimationValueWithCurve copyFrom)
        {
            if (copyFrom != null)
                copyFrom.CopyValuesTo(this);
        }

        public AnimationValueWithCurve Copy()
        {
            return new AnimationValueWithCurve(this);
        }

        public void CopyValuesTo(AnimationValueWithCurve target)
        {
            target.Mode = Mode;
            target.Constant = Constant;
            target.ConstantRandom = ConstantRandom;
            target.Curve = AnimationUtils.CopyCurve(Curve);
        }

        public float Evaluate()
        {
            return Evaluate(0f);
        }

        public float Evaluate(float t)
        {
            switch (Mode)
            {
                case ValueMode.Constant:
                    return Constant;
                    
                case ValueMode.Curve:
                    return Curve.Evaluate(t);

                case ValueMode.RandomBetweenTwoConstants:
                    return Random.Range(ConstantRandom.x, ConstantRandom.y);

                default:
                    return Constant;
            }
        }
    }
}