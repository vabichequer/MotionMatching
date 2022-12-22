using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MxM
{
    [System.Serializable]
    public struct CompositeData : IComplexAnimData
    {
        public int StartPoseId;
        public int EndPoseId;

        public float Length;
        public float ClipALength;
        public float ClipBLength;

        public int ClipIdA;
        public int ClipIdB;

        public EComplexAnimType ComplexAnimType { get { return EComplexAnimType.Composite; } }

        public CompositeData(int a_startPoseId, int a_endPoseId, int a_clipIdA, int a_clipIdB, float a_clipALength, float a_clipBLength)
        {
            StartPoseId = a_startPoseId;
            EndPoseId = a_endPoseId;
            ClipIdA = a_clipIdA;
            ClipIdB = a_clipIdB;
            ClipALength = a_clipALength;
            ClipBLength = a_clipBLength;
            Length = ClipALength + ClipBLength;
        }

    }//End of struct: CompositeData
}//End of namespace: MxM
