namespace Core.Math
{
    // consider if this is a good place to use platform intrinsics?

    public static class MathHelpers
    {
        public static decimal GetDifference(decimal v1, decimal v2)
        {

            /*   Formula
             *   
             *   |V1 - V2|
             *   ---------
             * [(V1 + V2) / 2] 
             * 
             */


            var delta = v1 - v2;
            var sum = v1 + v2;
            sum /= 2;
            var diff = delta / sum;
            return diff;
        }
    }
}
