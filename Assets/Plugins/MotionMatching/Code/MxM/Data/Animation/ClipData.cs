using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MxM
{
    [System.Serializable]
    public struct ClipData : IComplexAnimData
    {
        public int StartPoseId;
        public int EndPoseId;

        public int ClipId;
        public bool IsLooping;

        public EComplexAnimType ComplexAnimType { get { return EComplexAnimType.Clip; } }
        
        public ClipData(int a_startPoseId, int a_endPoseId, int a_clipId, bool a_isLooping)

        {
            StartPoseId = a_startPoseId;
            EndPoseId = a_endPoseId;
            ClipId = a_clipId;
            IsLooping = a_isLooping;
        }

    }//End of struct: ClipData
}//End of namespace: MxM