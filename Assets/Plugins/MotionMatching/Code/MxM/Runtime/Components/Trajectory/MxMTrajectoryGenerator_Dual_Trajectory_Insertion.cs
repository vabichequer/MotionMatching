// ================================================================================================
// File: MxMTrajectoryGenerator.cs
// 
// Authors:  Kenneth Claassen
// Date:     07-07-2019: Created this file.
// 
//     Contains a part of the 'MxM' namespace for 'Unity Engine'. 
// ================================================================================================
using UnityEngine;
using UnityEngine.Events;
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
    public class MxMTrajectoryGenerator_Dual_Trajectory_Insertion : MxMTrajectoryGeneratorBase
    {
        [Header("Motion Settings")]
        [Range(0f, 5f)]
        [SerializeField] private float m_simulationSpeedScale = 1f;
        //[SerializeField] private float m_maxSpeed = 4f; //The maximum speed of the trajectory (can be modified at runtime)
        [SerializeField] private float m_posBias = 15f; //The positional responsivity of the trajectory (can be modified at runtime)
        [SerializeField] private float m_dirBias = 10f; //The rotational responsivity of the trajectory (can be modified at runtime)
        [SerializeField] private ETrajectoryMoveMode m_trajectoryMode = ETrajectoryMoveMode.Normal; //If the trajectory should behave like strafing or not.
        [SerializeField] private EAnimationDataset m_animationDataset = EAnimationDataset.Mixamo;

        [Header("Input")]
        [SerializeField] private bool m_customInput = false; //Whether to use custom input or not (InputVector must be set every frame if false)
        [SerializeField] private bool m_resetDirectionOnOnInput = true;

        [Header("Other")]
        [SerializeField] private Transform m_camTransform = null; //A reference to the camera transform
        [SerializeField] private MxMInputProfile m_mxmInputProfile = null; //A reference to the Input profile asset used to shape the trajectory
        [SerializeField] private TrajectoryGeneratorModule m_trajectoryGeneratorModule = null; //The trajectory generator module to use
        

        [Header("Mods")]
        //[SerializeField] private float TangentAngle = 0f;
        [SerializeField] private List<float> Radius = new List<float>(2);
        [SerializeField] private List<float> LinearSpeed = new List<float>(2);
        [SerializeField] private float time_limit;
        [SerializeField] private ETurnOrientation turnOrientation = ETurnOrientation.Left;
        [SerializeField] private float Turns = 1;
        [SerializeField] public bool RecordData = false;
        [SerializeField] public bool Dual = false;
        [SerializeField] public string OutputFolder = "";

        private bool m_hasInputThisFrame; //A bool to cache whether there has been movement input in the current frame.

        private float m_lastDesiredOrientation = 0f;

        private float m_maxSpeed;

        public Vector3 StrafeDirection { get; set; }

        float acctime = 0, time_switcher = 0;
        bool has_inertia = true, first_second = false;
        int idx = 0;
        MxMAnimator debugger;
        StreamWriter writer_final, writer_stats, writer_info, writer_planned, writer_switching;
        List<string> pos_player = new List<string>();
        List<string> planned_pos = new List<string>();
        List<string> pose_stats = new List<string>();
        List<float> totalTime = new List<float>();
        List<int> switchingStatus = new List<int>();
        float completionTime = 0, settlingTime = 0;
        List<float> w = new List<float>();
        Vector3 center = new Vector3(0, 0, 0);

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
        public EAnimationDataset AnimationDataset { get { return m_animationDataset; } set { m_animationDataset = value; } }
        public ETurnOrientation DesiredTurnOrientation { get { return turnOrientation; } set { turnOrientation = value; } }
        public List<float> DesiredRadiuses { get { return Radius; } set { Radius = value; } }
        public float DesiredTimeSwitcherLimit { get { return time_limit; } set { time_limit = value; } }
        public List<float> DesiredLinearSpeed { get { return LinearSpeed; } set { LinearSpeed = value; } }
        public float DesiredOrientation { get { return m_lastDesiredOrientation; } }
        public Vector3 InputVector { get; set; } //The raw input vector
        public Vector2 InputVector2D { get { return new Vector2(InputVector.x, InputVector.z); } set { InputVector = new Vector3(value.x, 0f, value.y); } }
        public Vector3 LinearInputVector { get; private set; } //The transformed input vector relative to camera
        public float MaxSpeed { get { return m_maxSpeed; } set { m_maxSpeed = value; } } //The maximum speed of the trajectory generator
        public float PositionBias { get { return m_posBias; } set { m_posBias = value; } } //The positional responsiveness of the trajectory generator
        public float DirectionBias { get { return m_dirBias; } set { m_dirBias = value; } } //The rotational responsiveness of the trajectory generator
        public MxMInputProfile InputProfile { get { return m_mxmInputProfile; } set { m_mxmInputProfile = value; } } //The input profile used to shape the trajectory generator 
        public Transform RelativeCameraTransform { get { return m_camTransform; } set { m_camTransform = value; } } //The camera transform used to make input relative to the camera.
        public float SimulationSpeedScale { get { return m_simulationSpeedScale; } set { m_simulationSpeedScale = value; } }

        private List<Vector3> m_newTrajectoryPositions = new List<Vector3>();

        protected override void Setup(float[] a_predictionTimes) { }

        private Vector3 plannedTrajectory(float t, float r, float v)
        {
            float x = r * Mathf.Sin(t);
            float y = r * Mathf.Cos(t);

            return new Vector3(x, 0, y);
        }

        private string SortAnimationDataset(string path)
        {
            switch (m_animationDataset)
            {
                case (EAnimationDataset.Mixamo):
                    {
                        path += "/Mixamo/" + LinearSpeed[0].ToString() + '/';
                        break;
                    }
                case (EAnimationDataset.Vicenzo):
                    {
                        path += "/Vicenzo/" + LinearSpeed[0].ToString() + '/';
                        break;
                    }
                case (EAnimationDataset.DemoMocap):
                    {
                        path += "/DemoMocap/" + LinearSpeed[0].ToString() + '/';
                        break;
                    }
                default:
                    break;
            }


            return path;
        }

        private string SortTurnOrientation(string path)
        {
            switch (turnOrientation)
            {
                case (ETurnOrientation.Left):
                    {
                        path += "/Left/";
                        break;
                    }
                case (ETurnOrientation.Right):
                    {
                        path += "/Right/";
                        break;
                    }
                case (ETurnOrientation.Straight):
                    {
                        path += "/Straight/";
                        break;
                    }
                default:
                    break;
            }


            return path;
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
            OutputFolder = SortAnimationDataset(OutputFolder);
            OutputFolder = SortTurnOrientation(OutputFolder);

            debugger = gameObject.GetComponent<MxMAnimator>();
            debugger.StartRecordAnalytics();
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            w = new List<float>();

            StrafeDirection = Vector3.forward;
            m_lastDesiredOrientation = transform.rotation.eulerAngles.y;


            gameObject.transform.position = new Vector3((float)turnOrientation * Radius[0], 0, -10);

            if (Dual)
            {
                for (int i = 0; i < 2; i++)
                {               
                    w.Add((float)turnOrientation * LinearSpeed[i] / Radius[i]);
                }
            }
            else
            {
                w.Add((float)turnOrientation * LinearSpeed[0] / Radius[0]);
            }

            if (turnOrientation == ETurnOrientation.Straight)
            {
                settlingTime = 0;
                completionTime = 5;
            }
            else
            {
                settlingTime = 5;
                completionTime = 30; // turnDuration * Turns;
            }

            if (RecordData)
            {
                Directory.CreateDirectory(OutputFolder);
                writer_stats = new StreamWriter(new FileStream(OutputFolder + Radius[0].ToString() + "_stats.csv", FileMode.Create, FileAccess.Write));
                writer_info = new StreamWriter(new FileStream(OutputFolder + Radius[0].ToString() + "_info.csv", FileMode.Create, FileAccess.Write));
                writer_final = new StreamWriter(new FileStream(OutputFolder + Radius[0].ToString() + "_final.csv", FileMode.Create, FileAccess.Write));
                writer_planned = new StreamWriter(new FileStream(OutputFolder + Radius[0].ToString() + "_planned.csv", FileMode.Create, FileAccess.Write));
                writer_switching = new StreamWriter(new FileStream(OutputFolder + Radius[0].ToString() + "_switching_status.csv", FileMode.Create, FileAccess.Write));
            }

            for (int i = 0; i < p_trajectoryIterations; i++)
                m_newTrajectoryPositions.Add(new Vector3());
            
            switch (m_animationDataset)
            {
                case (EAnimationDataset.Mixamo):
                    {
                        m_maxSpeed = 5.4f;
                        break;
                    }
                case (EAnimationDataset.Vicenzo):
                    {
                        m_maxSpeed = 2.5f;
                        break;
                    }
                case (EAnimationDataset.DemoMocap):
                    {
                        m_maxSpeed = 5.6f;
                        break;
                    }
                default:
                    break;
            }

        }

        protected override void OnDestroy()
        {
            if (RecordData)
            {
                foreach (string str in pos_player)
                    writer_final.WriteLine(str);


                foreach (string str in planned_pos)
                    writer_planned.WriteLine(str);

                foreach (string str in pose_stats)
                    writer_stats.WriteLine(str);

                foreach (int status in switchingStatus)
                    writer_switching.WriteLine(status);

                List<float> degAngularSpeed = new List<float>();

                for (int i = 0; i < w.Count; i++)
                {
                    if (turnOrientation != ETurnOrientation.Straight)
                        degAngularSpeed.Add(w[i] * Mathf.Rad2Deg);
                    else
                        degAngularSpeed.Add(0);
                }

                Debug.Log("First time: " + totalTime.First() + ", Last time: " + totalTime.Last());
                Debug.Log("Actual elapsed time: " + (totalTime.Last() - totalTime.First()));
                writer_info.WriteLine("Total time," + (totalTime.Last() - totalTime.First()));
                writer_info.WriteLine("Radius," + Radius[0]);
                writer_info.WriteLine("Angle speed," + degAngularSpeed[0]);
                if(w.Count > 1)
                    writer_info.WriteLine("Angle speed 2," + degAngularSpeed[1]);
                writer_info.WriteLine("Desired linear speed," + LinearSpeed[0]);
                if (LinearSpeed.Count > 1)
                    writer_info.WriteLine("Switching linear speed," + LinearSpeed[1]);
                if (Radius.Count > 1)
                    writer_info.WriteLine("Switching radius," + Radius[1]);
                writer_final.Close();
                writer_planned.Close();
                writer_stats.Close();
                writer_info.Close();
                writer_switching.Close();
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
            Vector3 charPos = transform.position;
            acctime += Time.deltaTime;
            p_trajPositions[0] = Vector3.zero;
            p_trajFacingAngles[0] = 0f;

            for (int i = 1; i < p_trajPositions.Length; i++)
            {
                float lerp = (float)i / (float)p_trajPositions.Length;
                float delta = acctime + Time.deltaTime * i;
                Vector3 newCoord = Vector3.zero;

                // The trajectory is centered in the character, so I need to translate the prediction matrix to the center

                if (has_inertia && (transform.position.z < 0))
                {
                    p_trajPositions[i] = new Vector3(0, 0, Mathf.Lerp(0, LinearSpeed[0], lerp));
                }
                else
                {
                    if (has_inertia)
                    {
                        has_inertia = false;
                        acctime = 0;
                        time_switcher = 0;
                        delta = 0;
                    }

                    if (acctime < completionTime)
                    {
                        // This will only execute in dual speed mode. Otherwise, it will use only a single linear speed
                        if (time_switcher > time_limit && Dual)
                        {
                            time_switcher = 0;
                            first_second = !first_second;
                            Vector3 p = gameObject.transform.position;
                            double previous_angle = Math.Atan2(-p.z, -p.x);
                            float x1 = (float)Math.Round(p.x + Radius[Convert.ToInt32(first_second)] * Math.Cos(previous_angle), 3);
                            float z1 = (float)Math.Round(p.z + Radius[Convert.ToInt32(first_second)] * Math.Sin(previous_angle), 3);
                            center = new Vector3(x1, 0, z1);
                        }

                        int dual_index = Convert.ToInt32(first_second);

                        if (turnOrientation == ETurnOrientation.Straight)
                        {
                            newCoord = new Vector3(0, 0, Mathf.Lerp(0, LinearSpeed[dual_index], lerp));
                            p_trajPositions[i] = newCoord;
                        }
                        else
                        {
                            if (turnOrientation == ETurnOrientation.Left)
                                newCoord = new Vector3(Radius[dual_index] * Mathf.Cos(delta * w[dual_index]), 0, Radius[dual_index] * Mathf.Sin(delta * w[dual_index])) + center;
                            else
                                newCoord = new Vector3(Radius[dual_index] * Mathf.Cos((delta * w[dual_index]) + Mathf.PI), 0, Radius[dual_index] * Mathf.Sin((delta * w[dual_index]) + Mathf.PI)) + center;

                            p_trajPositions[i] = newCoord - charPos;
                        }
                    }
                    else
                    {
                        //Debug.Log("Acctime: " + acctime.ToString());
                        p_trajPositions[i] = Vector3.zero;
                        EventManager.TriggerEvent("CharacterDone");
                    }
                }

                if (i == 1 && RecordData && !has_inertia && acctime > settlingTime)
                {
                    time_switcher += Time.deltaTime;
                    planned_pos.Add(newCoord.x.ToString() + ",0," + newCoord.z.ToString());
                    Vector3 p = gameObject.transform.position;
                    Vector3 r = gameObject.transform.rotation.eulerAngles;
                    pos_player.Add(acctime.ToString() + ',' + p.x.ToString() + ',' + p.y.ToString() + ',' + p.z.ToString() + ',' + r.x.ToString() + ',' + r.y.ToString() + ',' + r.z.ToString());
                    switchingStatus.Add(Convert.ToInt32(first_second));

                    for (int k = 0; k < 8; k++)
                    {
                        pose_stats.Add("channel," + k);
                        MxMDebugFrame.PlayableStateDebugData states = debugger.DebugData.FrameData[debugger.DebugData.LastRecordedFrame].playableStates[k];
                        GetPoseStatistics(states, states.StartPoseId);
                    }
                    
                    float lastChosenCost = this.GetComponent<MxMAnimator>().LastChosenCost;
                    pose_stats.Add("float,lastChosenCost," + lastChosenCost);
                    pose_stats.Add("frame," + debugger.DebugData.LastRecordedFrame.ToString() + ",idx," + idx.ToString());

                    idx++;

                    totalTime.Add(Time.realtimeSinceStartup);
                }

                /*GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cylinder.transform.position = p_trajPositions[i];
                cylinder.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);*/

                Vector3 dir;
                dir = (Vector3)p_trajPositions[i] - (Vector3)p_trajPositions[i - 1];
                p_trajFacingAngles[i] = Quaternion.LookRotation(dir).eulerAngles.y;
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
            float clipTime = debugger.CurrentAnimData.Poses[startPoseId].Time;
            AnimationClip chosenClip = debugger.CurrentAnimData.Clips[chosenClipId];
            pose_stats.Add("AnimationClip,Primary clip," + chosenClip.name);
            pose_stats.Add("float,Time," + clipTime.ToString());

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