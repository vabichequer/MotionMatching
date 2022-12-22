using UnityEngine;
using System;

namespace MxM
{
    [System.Serializable]
    public struct BlendClipData : IComplexAnimData
    {
        public int StartPoseId;
        public int EndPoseId;

        public bool NormalizeTime;

        public int[] ClipIds;
        public float[] Weightings;

        public EComplexAnimType ComplexAnimType { get { return EComplexAnimType.BlendClip; } }

        public BlendClipData(int a_startPoseId, int a_endPoseId, bool a_normalizeTime, int[] a_clipIds, float[] a_weightings)
        {
            StartPoseId = a_startPoseId;
            EndPoseId = a_endPoseId;
            NormalizeTime = a_normalizeTime;

            int actualWeightCount = 0;
            foreach (float weight in a_weightings)
            {
                if (weight > Mathf.Epsilon)
                {
                    ++actualWeightCount;
                }
            }

            ClipIds = new int[actualWeightCount];
            Weightings = new float[actualWeightCount];

            int index = 0;
            for (int i = 0; i < a_weightings.Length; ++i)
            {
                if(a_weightings[i] > Mathf.Epsilon)
                {
                    ClipIds[index] = a_clipIds[i];
                    Weightings[index] = a_weightings[i];
                    ++index;
                }

            }
        }
    }
}