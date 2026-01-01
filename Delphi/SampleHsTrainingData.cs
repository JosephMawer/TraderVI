using Core.ML.Engine.Training.Classifiers;
using System;
using System.Collections.Generic;

public static class SampleHsTrainingData
{
    private static readonly Random _rng = new(123);

    public static List<PatternWindow> BuildSampleData(int lookback = 30)
    {
        var data = new List<PatternWindow>();

        // A few “positive” examples
        for (int i = 0; i < 10; i++)
            data.Add(CreateHeadAndShouldersLikeWindow(lookback, hasPattern: true));

        // A few “negative” examples
        for (int i = 0; i < 10; i++)
            data.Add(CreateNonPatternWindow(lookback, hasPattern: false));

        return data;
    }

    private static PatternWindow CreateHeadAndShouldersLikeWindow(int L, bool hasPattern)
    {
        var prices = new float[L];
        var volume = new float[L];

        // Simple 3-peak structure: left shoulder, head, right shoulder
        int leftIdx = L / 4;
        int headIdx = L / 2;
        int rightIdx = 3 * L / 4;

        float baseLevel = NextFloat(0.9f, 1.1f);
        float shoulderHeight = baseLevel * NextFloat(1.02f, 1.06f);
        float headHeight = baseLevel * NextFloat(1.08f, 1.15f);
        float endLevel = baseLevel * NextFloat(0.9f, 0.98f); // slightly below base

        // Helper: linear interpolation between two points
        void FillSeg(int start, int end, float a, float b)
        {
            int len = end - start;
            for (int i = 0; i <= len; i++)
            {
                float t = len == 0 ? 0f : (float)i / len;
                prices[start + i] = Lerp(a, b, t);
            }
        }

        FillSeg(0, leftIdx, baseLevel, shoulderHeight);
        FillSeg(leftIdx, headIdx, shoulderHeight, headHeight);
        FillSeg(headIdx, rightIdx, headHeight, shoulderHeight);
        FillSeg(rightIdx, L - 1, shoulderHeight, endLevel);

        // Add small noise
        for (int i = 0; i < L; i++)
        {
            prices[i] += NextFloat(-0.01f, 0.01f);
        }

        // Normalize by first price
        float first = prices[0];
        if (first == 0) first = 1f;
        for (int i = 0; i < L; i++)
            prices[i] /= first;

        // Volume: base + spikes near the “pattern” points
        float baseVol = NextFloat(0.8f, 1.2f);
        for (int i = 0; i < L; i++)
        {
            float v = baseVol * NextFloat(0.7f, 1.3f);

            if (Math.Abs(i - leftIdx) <= 1 ||
                Math.Abs(i - headIdx) <= 1 ||
                Math.Abs(i - rightIdx) <= 1 ||
                i == L - 1)
            {
                v *= NextFloat(1.3f, 2.0f);
            }

            volume[i] = v;
        }

        // Normalize volume by average
        float avgVol = volume.Average();
        if (avgVol == 0) avgVol = 1f;
        for (int i = 0; i < L; i++)
            volume[i] /= avgVol;

        return new PatternWindow
        {
            PriceNorm = prices,
            VolumeNorm = volume,
            HasHeadAndShoulders = hasPattern
        };
    }

    private static PatternWindow CreateNonPatternWindow(int L, bool hasPattern)
    {
        var prices = new float[L];
        var volume = new float[L];

        float level = NextFloat(0.9f, 1.1f);
        float drift = NextFloat(-0.003f, 0.003f);

        for (int i = 0; i < L; i++)
        {
            level += drift + NextFloat(-0.01f, 0.01f);
            if (level <= 0.1f) level = 0.1f;
            prices[i] = level;
        }

        // Normalize by first price
        float first = prices[0];
        if (first == 0) first = 1f;
        for (int i = 0; i < L; i++)
            prices[i] /= first;

        // Volume: random around a base
        float baseVol = NextFloat(0.8f, 1.2f);
        for (int i = 0; i < L; i++)
        {
            volume[i] = baseVol * NextFloat(0.7f, 1.3f);
        }

        float avgVol = volume.Average();
        if (avgVol == 0) avgVol = 1f;
        for (int i = 0; i < L; i++)
            volume[i] /= avgVol;

        return new PatternWindow
        {
            PriceNorm = prices,
            VolumeNorm = volume,
            HasHeadAndShoulders = hasPattern
        };
    }

    private static float NextFloat(float min, float max)
        => (float)(_rng.NextDouble() * (max - min) + min);

    private static float Lerp(float a, float b, float t)
        => a + (b - a) * t;
}
