using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System.Linq;
using MxM;
using System;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using System.IO;
#endif

using MyBox;

public class DualTrialGenerator : MonoBehaviour
{
    [Separator("Operation mode options")]
    public bool Dual = true;
    public bool RecordData = true;
    public bool RecordVideo = true;
    [Separator("Linear speed interval")]
    [MinMaxRange(1, 5)] public RangedInt LinearSpeedInterval = new RangedInt(1, 5); 
    public float LinearSpeedStep;

    [Separator("Angular speed interval")]
    [MinMaxRange(-200, 200)] public RangedInt AngularSpeedInterval = new RangedInt(-100, 100);
    public int AngularSpeedStep;

    [Separator("Speed switch configuration")]
    [ConditionalField(nameof(Dual))] public float SecondLinearSpeedInterval = 4;
    [ConditionalField(nameof(Dual))] public float TimeSwitcherLimit = 5;

    [Separator("Video recorder configuration")]
    [ConditionalField(nameof(Dual))] public GameObject VideoRecorder;

    [Separator("Prefab configurations")]
    public GameObject testSubject;
    public EAnimationDataset m_animationDataset = EAnimationDataset.Mixamo;
    public string OutputFolder = ""; //C:\Users\vabicheq\Documents\MotionMatching\Assets\output\new

    private bool start = false;
    private List<float> LinearSpeed, AngularSpeed;
    List<List<float>> radius = new List<List<float>>();
    List<ETurnOrientation> turnOrientation = new List<ETurnOrientation>();
    int aspd_idx = 0, lspd_idx = 0, radius_idx = 0;
    private GameObject currentTestSubject;
    GameObject currentRecorder;
    StreamWriter writer_radius, writer_lspd;
    string orientationFolder = "";
    string output_folder_root;

    string returnStringArray(List<float> temp)
    {
        string result = "";

        foreach(float x in temp)
        {
            result += x + ", ";
        }

        result += "Total: " + temp.Count();

        return result;
    }

    private string SortAnimationDataset(string path)
    {
        switch (m_animationDataset)
        {
            case (EAnimationDataset.Mixamo):
                {
                    path += "/Mixamo/";
                    break;
                }
            case (EAnimationDataset.Vicenzo):
                {
                    path += "/Vicenzo/";
                    break;
                }
            case (EAnimationDataset.DemoMocap):
                {
                    path += "/DemoMocap/";
                    break;
                }
            default:
                break;
        }


        return path;
    }

    public void StartSimulation()
    {
        start = true;
        radius = new List<List<float>>();
        AngularSpeed = genSpeed(AngularSpeedInterval.Min, AngularSpeedInterval.Max, AngularSpeedStep);
        LinearSpeed = genSpeed(LinearSpeedInterval.Min, LinearSpeedInterval.Max, LinearSpeedStep);

        output_folder_root = OutputFolder;

        OutputFolder = SortAnimationDataset(OutputFolder);

        List<List<float>> meterPerDegree = new List<List<float>>();
        List<List<float>> circumference = new List<List<float>>();

        writer_lspd = new StreamWriter(new FileStream(OutputFolder + "/linearspeeds.csv", FileMode.Create, FileAccess.Write));

        foreach (float lspd in LinearSpeed)
        {
            foreach (float aspd in AngularSpeed)
            {
                List<float> temp = new List<float>();
                if (aspd < 0)
                {
                    temp.Add(lspd / aspd);
                    if (Dual) temp.Add(SecondLinearSpeedInterval / aspd);
                    meterPerDegree.Add(temp);
                    turnOrientation.Add(ETurnOrientation.Right);
                    orientationFolder = "Right";
                }
                else if (aspd > 0)
                {
                    temp.Add(lspd / aspd);
                    if (Dual) temp.Add(SecondLinearSpeedInterval / aspd);
                    meterPerDegree.Add(temp);
                    turnOrientation.Add(ETurnOrientation.Left);
                    orientationFolder = "Left";
                }
                else
                {
                    temp.Add(0);
                    if (Dual) temp.Add(0);
                    meterPerDegree.Add(temp);
                    turnOrientation.Add(ETurnOrientation.Straight);
                    orientationFolder = "Straight";
                }
            }
            writer_lspd.WriteLine(lspd.ToString());
        }

        writer_lspd.Close();

        foreach (List<float> mpds in meterPerDegree)
        {
            List<float> temp = new List<float>();
            foreach (float mpd in mpds)
                temp.Add(mpd * 360);
            circumference.Add(temp);
        }

        writer_radius = new StreamWriter(new FileStream(OutputFolder + "/radiuses.csv", FileMode.Create, FileAccess.Write));

        foreach (List<float> circs in circumference)
        {
            List<float> temp = new List<float>();
            foreach (float circ in circs)
            {
                float r = circ / (2 * Mathf.PI);
                temp.Add(r);
            }
            writer_radius.WriteLine(temp[0].ToString());
            for (int i = 0; i < temp.Count; i++)
                temp[i] = Math.Abs(temp[i]);                
            radius.Add(temp);
        }

        writer_radius.Close();

        Debug.Log("Angular speeds: " + returnStringArray(AngularSpeed));
        Debug.Log("Linear speeds: " + returnStringArray(LinearSpeed));

        radius_idx = lspd_idx * AngularSpeed.Count() + aspd_idx;
        List<float> linear_speed_pair = new List<float> { LinearSpeed[lspd_idx], SecondLinearSpeedInterval };

        Simulate(linear_speed_pair, radius[radius_idx], turnOrientation[radius_idx], TimeSwitcherLimit, output_folder_root);
        EventManager.StartListening("CharacterDone", DetectCharacterFinish);
    }

    private void OnDestroy()
    {
        EventManager.StopListening("CharacterDone", DetectCharacterFinish);
    }

    private void DetectCharacterFinish()
    {
        EventManager.Destroy(currentTestSubject);
    }

    private static List<float> linspace(float startval, float endval, int amount)
    {
        float interval = (endval / Mathf.Abs(endval)) * Mathf.Abs(endval - startval) / (amount - 1);
        return (from val in Enumerable.Range(0, amount) select startval + (val * interval)).ToList();
    }

    List<float> genSpeed(int start, int last, float step)
    {
        float sum = Mathf.Abs(start - last);
        int amount = (int)((sum / step) + 1);
        return linspace(start, last, amount);
    }

    // Update is called once per frame
    void Simulate(List<float> lspd, List<float> r, ETurnOrientation orientation, float timeSwitcherLimit, string of_root)
    {
        if (Application.isPlaying)
        {
            if (start)
            {
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().AnimationDataset = m_animationDataset;
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().DesiredRadiuses = r;
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().DesiredLinearSpeed = lspd;
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().Dual = Dual;
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().DesiredTurnOrientation = orientation;
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().DesiredTimeSwitcherLimit = timeSwitcherLimit;
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().RecordData = RecordData;
                testSubject.GetComponent<MxMTrajectoryGenerator_Dual_Trajectory_Insertion>().OutputFolder = of_root;

                if (orientation == ETurnOrientation.Left)
                    orientationFolder = "Left";
                else if (orientation == ETurnOrientation.Right)
                    orientationFolder = "Right";
                else
                    orientationFolder = "Straight";

                if (RecordVideo)
                {
                    VideoRecorder.GetComponent<RecordScene>().OutputFolder = OutputFolder + '/' + lspd[0].ToString() + '/' + orientationFolder + '/';
                    VideoRecorder.GetComponent<RecordScene>().FileName = r[0].ToString() + "_recording";

                    currentRecorder = Instantiate(VideoRecorder);
                }
                else
                    currentRecorder = null;          
                
                currentTestSubject = Instantiate(testSubject);

                if (currentTestSubject != null)
                    Debug.Log("Simulating a character with parameters: Linear Speed = " + lspd[0].ToString() + " Angular Speed = " + AngularSpeed[aspd_idx] + " and Radius = " + r[0].ToString());
            }
        }
        else
        {
            Debug.LogError("The simulation won't start. Make sure to start the simulation with the scene playing!");
        }
    }

    private void Update()
    {
        if(start)
        {
            if(currentTestSubject == null)
            {
                if (currentRecorder != null)
                {
                    currentRecorder.GetComponent<RecordScene>().FinishRecording();
                    Destroy(currentRecorder);
                }
                if (lspd_idx < (LinearSpeed.Count - 1))
                {
                    lspd_idx += 1;
                    radius_idx = lspd_idx * AngularSpeed.Count() + aspd_idx;
                    List<float> linear_speed_pair = new List<float> { LinearSpeed[lspd_idx], SecondLinearSpeedInterval };

                    Simulate(linear_speed_pair, radius[radius_idx], turnOrientation[radius_idx], TimeSwitcherLimit, output_folder_root);
                }
                else
                {
                    if (aspd_idx < (AngularSpeed.Count - 1))
                    {
                        lspd_idx = 0;
                        aspd_idx += 1;
                        radius_idx = lspd_idx * AngularSpeed.Count() + aspd_idx;
                        List<float> linear_speed_pair = new List<float> { LinearSpeed[lspd_idx], SecondLinearSpeedInterval };

                        Simulate(linear_speed_pair, radius[radius_idx], turnOrientation[radius_idx], TimeSwitcherLimit, output_folder_root);
                    }
#if UNITY_EDITOR
                    else
                    {
                        EditorApplication.isPlaying = false;
                    }
#endif
                }
            }
        }
    }
}
