// ================================================================================================
// File: MxMTrajectoryGenerator.cs
// 
// Authors:  Kenneth Claassen
// Date:     07-07-2019: Created this file.
// 
//     Contains a part of the 'MxM' namespace for 'Unity Engine'. 
// ================================================================================================
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using System.IO;
using System;
using System.Linq;
using System.Globalization;
using System.Reflection;

namespace MxM
{
    //===========================================================================================
    /**
    *  @brief The class / Unity component (Monobehaviour) is used to generate a trajectory in 
    *  3D space for an MxMAnimator (Motion Matching animation system). 
    *  
    *  The component has two roles. Firstly to continuously record the past trajectory positions. 
    *  This is done via the inherited MxMtrajectoryGeneratorBase class which handles past trajectory
    *  and extracting of trajectory data. 
    *  
    *  The second role of this component is to predict a future trajectory based on user input. The
    *  future trajectory is also transformed by the camera rotation so the trajectory input is always
    *  relative to the view of the player. Additionally it allows for an InputProfile to shape the
    *  trajectory so that it is Digital in nature (rather than Analog). This is important to match
    *  the viable trajectory speeds to that of viable movement speeds in the animation.
    *  
    *  This class interfaces with the MxManimator through an implementation of IMxMTrajectory. 
    *  This is mostly implemented in MxMtrajectoryGeneratorBase. Every time The MxMAnimator wants 
    *  to perform a search for best fit animation, it requests a trajectory from this component. 
    *  
    *  Note that while the Trajectory Generator may generate a trajectory of X number granular 
    *  samples, the MxMAnimator will extract and interpolate only the samples it needs based on the
    *  trajectory configuration setup by the user.
    *         
    *********************************************************************************************/
    [RequireComponent(typeof(MxMAnimator))]
    public class MxMTrajectoryGenerator_AI_Modified : MxMTrajectoryGeneratorBase
    {
        [Header("Motion Settings")]
        [Range(0f, 5f)]
        [SerializeField] private float m_simulationSpeedScale = 1f;
        [SerializeField] private float m_maxSpeed = 4f; //The maximum speed of the trajectory (can be modified at runtime)
        [SerializeField] private float m_posBias = 15f; //The positional responsivity of the trajectory (can be modified at runtime)
        [SerializeField] private float m_dirBias = 10f; //The rotational responsivity of the trajectory (can be modified at runtime)
        [SerializeField] private ETrajectoryMoveMode m_trajectoryMode = ETrajectoryMoveMode.Normal; //If the trajectory should behave like strafing or not.
        
        [Header("Input")]
        [SerializeField] private bool m_customInput = false; //Whether to use custom input or not (InputVector must be set every frame if false)
        [SerializeField] private bool m_resetDirectionOnOnInput = true;

        [Header("Other")]
        [SerializeField] private Transform m_camTransform = null; //A reference to the camera transform
        [SerializeField] private MxMInputProfile m_mxmInputProfile = null; //A reference to the Input profile asset used to shape the trajectory
        [SerializeField] private TrajectoryGeneratorModule m_trajectoryGeneratorModule = null; //The trajectory generator module to use

        [Header("Mods")]
        //[SerializeField] private float TangentAngle = 0f;
        [SerializeField] private float Radius = 0f;
        [SerializeField] private float LinearSpeed = 1f;
        [SerializeField] private bool Record = false;
        [SerializeField] private string OutputFolder = "";

        private bool m_hasInputThisFrame; //A bool to cache whether there has been movement input in the current frame.
        private NativeArray<float3> m_newTrajPositions; //A native array buffer for storing the new trajectory points calculated for the current frame.

        private float m_lastDesiredOrientation = 0f;

        public Vector3 StrafeDirection { get; set; }

        float acctime = 0;
        MxMAnimator debugger;
        StreamWriter writer_final, writer_stats, writer_info, writer_planned;
        List<string> pos_player = new List<string>();
        List<string> planned_pos = new List<string>();
        List<string> pose_stats = new List<string>();
        List<float> totalTime = new List<float>();

        public bool Strafing
        {
            get { return m_trajectoryMode == ETrajectoryMoveMode.Strafe ? true : false; }
            set
            {
                if(value)
                    m_trajectoryMode = ETrajectoryMoveMode.Strafe;
                else
                    m_trajectoryMode = ETrajectoryMoveMode.Normal;
            }
        }  

        public bool Climbing
        {
            get { return m_trajectoryMode == ETrajectoryMoveMode.Climb ? true : false; }
            set
            {
                if (value)
                    m_trajectoryMode = ETrajectoryMoveMode.Climb;
                else
                    m_trajectoryMode = ETrajectoryMoveMode.Normal;
            }
        }

        public ETrajectoryMoveMode TrajectoryMode { get { return m_trajectoryMode; } set { m_trajectoryMode = value; } }
        public Vector3 InputVector { get; set; } //The raw input vector
        public Vector2 InputVector2D { get { return new Vector2(InputVector.x, InputVector.z); } set { InputVector = new Vector3(value.x, 0f, value.y); } }
        public Vector3 LinearInputVector { get; private set; } //The transformed input vector relative to camera
        public float MaxSpeed { get { return m_maxSpeed; } set { m_maxSpeed = value; } } //The maximum speed of the trajectory generator
        public float PositionBias { get { return m_posBias; } set { m_posBias = value; } } //The positional responsiveness of the trajectory generator
        public float DirectionBias { get { return m_dirBias; } set { m_dirBias = value; } } //The rotational responsiveness of the trajectory generator
        public MxMInputProfile InputProfile { get { return m_mxmInputProfile; } set { m_mxmInputProfile = value; } } //The input profile used to shape the trajectory generator 
        public Transform RelativeCameraTransform { get { return m_camTransform; } set { m_camTransform = value; } } //The camera transform used to make input relative to the camera.
        public float DesiredOrientation { get { return m_lastDesiredOrientation; } }
        public float SimulationSpeedScale { get { return m_simulationSpeedScale; } set { m_simulationSpeedScale = value; } }

        private List<Vector3> m_newTrajectoryPositions = new List<Vector3>();

        protected override void Setup(float[] a_predictionTimes) { }

        private Vector3 plannedTrajectory(float t, float r, float v)
        {
            float x = r * Mathf.Sin(t);
            float y = r * Mathf.Cos(t);

            return new Vector3(x, 0, y);
        }

        private void finalTrajectory(float t, GameObject c)
        {
            Vector3 p = c.transform.position;
            Vector3 r = c.transform.rotation.eulerAngles;

            if (Record)
                pos_player.Add(t.ToString() + ',' + p.x.ToString() + ',' + p.y.ToString() + ',' + p.z.ToString() + ',' + r.x.ToString() + ',' + r.y.ToString() + ',' + r.z.ToString());
        }

        //===========================================================================================
        /**
        *  @brief Monobehaviour Start function called once before the gameobject is updated.
        *  
        *  Ensures that the trajectory generator has a handle to the camera so that it can make
        *  inputs relative
        *         
        *********************************************************************************************/
        protected virtual void Start()
        {
            debugger = gameObject.GetComponent<MxMAnimator>();
            debugger.StartRecordAnalytics();
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Debug.Log("The current culture is now " + CultureInfo.CurrentCulture.Name);

            StrafeDirection = Vector3.forward;
            m_lastDesiredOrientation = transform.rotation.eulerAngles.y;

            gameObject.transform.position = new Vector3(0, 0, Radius);
            gameObject.transform.Rotate(0, 90, 0);

            if(Record)
            {
                writer_stats = new StreamWriter(new FileStream(OutputFolder + '/' + Radius.ToString() + "_stats.csv", FileMode.Create, FileAccess.Write));
                writer_info = new StreamWriter(new FileStream(OutputFolder + '/' + Radius.ToString() + "_info.csv", FileMode.Create, FileAccess.Write));
                writer_final = new StreamWriter(new FileStream(OutputFolder + '/' + Radius.ToString() + "_final.csv", FileMode.Create, FileAccess.Write));
                writer_planned = new StreamWriter(new FileStream(OutputFolder + '/' + Radius.ToString() + "_planned.csv", FileMode.Create, FileAccess.Write));
            }

            for (int i = 0; i < p_trajectoryIterations; i++)
                m_newTrajectoryPositions.Add(new Vector3());
        }

        protected override void OnDestroy()
        {
            if (Record)
            {
                foreach (string str in pos_player)
                    writer_final.WriteLine(str);


                foreach (string str in planned_pos)
                    writer_planned.WriteLine(str);

                foreach (string str in pose_stats)
                    writer_stats.WriteLine(str);

                float circumference = 2 * Mathf.PI * Radius;
                float meterPerDegree = circumference / 360;
                float angularSpeed = LinearSpeed / meterPerDegree;

                Debug.Log("First time: " + totalTime.First() + ", Last time: " + totalTime.Last());
                Debug.Log("Actual elapsed time: " + (totalTime.Last() - totalTime.First()));
                writer_info.WriteLine("Total time," + (totalTime.Last() - totalTime.First()));
                writer_info.WriteLine("Radius," + Radius);
                writer_info.WriteLine("Angle speed," + angularSpeed);
                writer_info.WriteLine("Desired linear speed," + LinearSpeed);

                writer_final.Close();
                writer_planned.Close();
                writer_stats.Close();
                writer_info.Close();
            }
            base.OnDestroy();
        }

        //===========================================================================================
        /**
        *  @brief Monobehaviour FixedUpdate which updates / records the past trajectory every physics
        *  update if the Animator component is set to AnimatePhysics update mode.
        *         
        *********************************************************************************************/
        public void FixedUpdate()
        {
            if(p_animator.updateMode == AnimatorUpdateMode.AnimatePhysics)
            {
                UpdatePastTrajectory();
            }
        }

        //===========================================================================================
        /**
        *  @brief Monobehaviour Update function which is called every frame that the object is active. 
        *  
        *  This updates / records the past trajectory, provided that the Animator component isn't 
        *  running in 'Animate Physics'
        *         
        *********************************************************************************************/
        public void Update()
        {
            if (p_animator.updateMode != AnimatorUpdateMode.AnimatePhysics)
            {
                UpdatePastTrajectory();
            }
        }

        //===========================================================================================
        /**
        *  @brief This is the core function of a motion matching controller. It is responsible for 
        *  updating the trajectory prediction for movement. This movement model can differ between 
        *  controllers but this controller takes a deceivingly simplistic approach. 
        *  
        *  The predicted trajectory output from this function is in evenly spaced points and it is not
        *  necessarily the trajectory passed to the MxMAnimator. Rather, the high resolution trajectory
        *  calculated here will be sampled using the ExtractMotion function before passing data to the
        *  MxMAnimator.
        *         
        *********************************************************************************************/
        protected override void UpdatePrediction()
        {
            acctime += Time.deltaTime;
         
            Vector3 charPosition = transform.position;

            m_newTrajectoryPositions[0] = float3.zero;
            p_trajFacingAngles[0] = 0f;
            int iterations = m_newTrajectoryPositions.Count;

            float pathCumDisplacement = 0f;

            Vector3 from = Vector3.zero;
            Vector3 to;
            //float largestFacingAngle = 0f;
            float desiredOrientation = 0f;
            for (int i = 1; i < iterations; ++i)
            {
                int nbrWayPoints = 1;
                float percentage = (float)i / (float)(iterations - 1);
                float desiredDisplacement = LinearSpeed * percentage; // Speed

                //find the desired point along the path.
                Vector3 lastPoint = Vector3.zero;
                Vector3 desiredPos = Vector3.zero;
                desiredOrientation = 0f;
                float displacement = 0;
                to = plannedTrajectory(acctime, Radius, LinearSpeed) - charPosition;
                while (pathCumDisplacement + displacement < desiredDisplacement)
                {
                    from = to;
                    to = plannedTrajectory(acctime + Time.deltaTime * nbrWayPoints, Radius, LinearSpeed) - charPosition;

                    displacement = math.length(to - from);

                    pathCumDisplacement += displacement;
                    nbrWayPoints++;
                }

                float lerp = (desiredDisplacement - pathCumDisplacement) / displacement;
                desiredPos = math.lerp(from, to, lerp);

                Vector3 lastPosition = p_trajPositions[i - 1];

                Vector3 adjustedTrajectoryDisplacement = math.lerp((Vector3)p_trajPositions[i] - lastPosition, desiredPos - lastPosition,
                    1f - math.exp(-m_posBias * percentage * Time.deltaTime));

                m_newTrajectoryPositions[i] = m_newTrajectoryPositions[i - 1] + adjustedTrajectoryDisplacement;
            }

            //Rotation iteration
            to = m_newTrajectoryPositions[0];
            for (int i = 1; i < m_newTrajectoryPositions.Count; ++i)
            {
                float percentage = (float)i / (float)(iterations - 1);

                from = to;
                to = m_newTrajectoryPositions[i];
                Vector3 next = to + (to - from);

                if (i < m_newTrajectoryPositions.Count - 1)
                    next = m_newTrajectoryPositions[i + 1];

                var displacementVector = next - to;
                desiredOrientation = Vector3.SignedAngle(Vector3.forward, displacementVector, Vector3.up);

                if (Vector3.SqrMagnitude(to - from) > 0.1f)
                {
                    float facingAngle = Mathf.LerpAngle(p_trajFacingAngles[i], desiredOrientation,
                        1f - math.exp(-m_dirBias * percentage * Time.deltaTime));

                    p_trajFacingAngles[i] = facingAngle;
                }
            }

            m_hasInputThisFrame = true;

            for (int i = 0; i < iterations; ++i)
            {
                p_trajPositions[i] = m_newTrajectoryPositions[i];
            }
        }

        public void GetPoseStatistics(object state, int startPoseId)
        {
            FieldInfo[] members = state.GetType().GetFields();

            foreach (FieldInfo fi in members)
            {
                // perform update of FieldInfo fi
                pose_stats.Add(fi.ToString().Replace(' ', ',') + "," +  fi.GetValue(state));
            }

            int chosenClipId = debugger.CurrentAnimData.Poses[startPoseId].PrimaryClipId;
            AnimationClip chosenClip = debugger.CurrentAnimData.Clips[chosenClipId];
            pose_stats.Add("AnimationClip,Primary clip," + chosenClip.name);

        }

        //===========================================================================================
        /**
        *  @brief This function calculates the relative input vector transfromed both by the camera
        *  and then the character (in that order). This allows movement (or trajectory) input to be 
        *  dependent on camera angle. Since the trajectory is going to be transformed into the character
        *  space by the MxMAnimator, it needs to be inversely transformed by the character transform as well
        *  
        *  @return Vector3 - the relative input vector that will be used to generate a trajectory.
        *         
        *********************************************************************************************/
        public Vector3 GetRelativeInputVector()
        {
            if (m_camTransform == null)
            {
                return InputVector;
            }
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(m_camTransform.forward, Vector3.up);
                Vector3 linearInput = Quaternion.FromToRotation(Vector3.forward, forward) * InputVector;

                return transform.InverseTransformVector(linearInput);
            }
        }

        //===========================================================================================
        /**
        *  @brief Allocates and initializes any native arrays required for the trajectory generator
        *  to function.
        *         
        *********************************************************************************************/
        protected override void InitializeNativeData()
        {
            base.InitializeNativeData();
        }

        //===========================================================================================
        /**
        *  @brief Disposes any native data that has been created for jobs to avoid memory leaks.
        *         
        *********************************************************************************************/
        protected override void DisposeNativeData()
        {
            base.DisposeNativeData();
        }

        //===========================================================================================
        /**
        *  @brief Checks if the trajectory generator has movement input or not. The movement input is
        *  usually cached because it is processed during the update and may need to be fetched at a 
        *  later point.
        *  
        *  @return bool - true if there is movement input, false if there is not.
        *         
        *********************************************************************************************/
        public override bool HasMovementInput()
        {
            return m_hasInputThisFrame;
        }

        //===========================================================================================
        /**
        *  @brief This function resets the 'Motion' on the trajectory generator. Essentially it sets
        *  all past and future predicted points to zero.  It is also an implementation of IMxMTrajectory
        *  
        *  This function is normally called automatically from the MxMAnimator after an event dependent
        *  on the method used 'PostEventTrajectoryHandling' (See MxMAnimator.cs). However, it can also
        *  be used to stomp all trajectory for whatever reason (e.g. when teleporting the character, 
        *  it may be useful to stomp trajectory so they don't do a running stop after teleportation)
        *         
        *********************************************************************************************/
        public override void ResetMotion(float a_rotation = 0f)
        {
            base.ResetMotion(a_rotation);

            m_hasInputThisFrame = false;
        }

        //===========================================================================================
        /**
        *  @brief Allows developers to manually set the input vector on the Trajectory Generator for
        *  custom input.
        *         
        *********************************************************************************************/
        public void SetInput(Vector2 a_input)
        {
            InputVector = new Vector3(a_input.x, 0f, a_input.y);
        }

        //===========================================================================================
        /**
        *  @brief 
        *         
        *********************************************************************************************/
        public void SetTrajectoryModule(TrajectoryGeneratorModule a_trajGenModule)
        {
            if (a_trajGenModule == null)
                return;

            m_maxSpeed = a_trajGenModule.MaxSpeed;
            m_posBias = a_trajGenModule.PosBias;
            m_dirBias = a_trajGenModule.DirBias;
            m_trajectoryMode = a_trajGenModule.TrajectoryMode;
            m_customInput = a_trajGenModule.CustomInput;
            m_resetDirectionOnOnInput = a_trajGenModule.ResetDirectionOnNoInput;
            m_camTransform = a_trajGenModule.CamTransform;
            m_mxmInputProfile = a_trajGenModule.InputProfile;
            p_flattenTrajectory = a_trajGenModule.FlattenTrajectory;
        }


    }//End of class: MxMTrajectoryGenerator
}//End of namespace: MxM