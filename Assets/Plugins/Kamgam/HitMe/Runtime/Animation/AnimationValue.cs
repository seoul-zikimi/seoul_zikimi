using UnityEngine;

namespace Kamgam.HitMe
{
    [System.Serializable]
    public class AnimationValue
    {
        public enum ValueMode
        {
            Constant = 0,
            RandomBetweenTwoConstants = 1
        }

        public ValueMode Mode = ValueMode.Constant;

        [ShowIf("Mode", ValueMode.Constant, ShowIfAttribute.DisablingType.DontDraw)]
        public float Constant;

        [ShowIf("Mode", ValueMode.RandomBetweenTwoConstants, ShowIfAttribute.DisablingType.DontDraw)]
        public Vector2 ConstantRandom = new Vector2(0, 90f);

        public AnimationValue(float constantValue)
        {
            Constant = constantValue;
        }

        public AnimationValue(AnimationValue copyFrom)
        {
            if (copyFrom != null)
                copyFrom.CopyValuesTo(this);
        }

        public AnimationValue Copy()
        {
            return new AnimationValue(this);
        }

        public void CopyValuesTo(AnimationValue target)
        {
            target.Mode = Mode;
            target.Constant = Constant;
            target.ConstantRandom = ConstantRandom;
        }

        public float Evaluate()
        {
            switch (Mode)
            {
                case ValueMode.Constant:
                    return Constant;
                    
                case ValueMode.RandomBetweenTwoConstants:
                    return Random.Range(ConstantRandom.x, ConstantRandom.y);

                default:
                    return Constant;
            }
        }
    }
}