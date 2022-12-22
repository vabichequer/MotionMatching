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

public class TrialGenerator : MonoBehaviour
{
    [Header("Angular speed interval")]
    public int AngularSpeedStart;
    public int AngularSpeedEnd;
    public float AngularSpeedStep;

    [Header("Linear speed interval")]
    public int LinearSpeedStart;
    public int LinearSpeedEnd;
    public float LinearSpeedStep;

    [Header("Video recorder configuration")]
    public GameObject VideoRecorder;

    [Header("Prefab configurations")]
    public GameObject testSubject;
    public EAnimationDataset m_animationDataset = EAnimationDataset.Mixamo;
    public string OutputFolder = ""; //C:\Users\vabicheq\Documents\MotionMatching\Assets\output\new

    private bool start = false;
    private List<float> LinearSpeed, AngularSpeed;
    List<float> radius = new List<float>();
    List<ETurnOrientation> turnOrientation = new List<ETurnOrientation>();
    int aspd_idx = 0, lspd_idx = 0, radius_idx = 0;
    private GameObject currentTestSubject;
    GameObject currentRecorder;
    StreamWriter writer_radius, writer_lspd;
    string orientationFolder = "";

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
        radius = new List<float>();
        AngularSpeed = genSpeed(AngularSpeedStart, AngularSpeedEnd, AngularSpeedStep);
        LinearSpeed = genSpeed(LinearSpeedStart, LinearSpeedEnd, LinearSpeedStep);

        OutputFolder = SortAnimationDataset(OutputFolder);

        List<float> meterPerDegree = new List<float>();
        List<float> circumference = new List<float>();

        writer_lspd = new StreamWriter(new FileStream(OutputFolder + "/linearspeeds.csv", FileMode.Create, FileAccess.Write));

        foreach (float lspd in LinearSpeed)
        {
            foreach (float aspd in AngularSpeed)
            {
                if (aspd < 0)
                {
                    meterPerDegree.Add(lspd / aspd);
                    turnOrientation.Add(ETurnOrientation.Right);
                    orientationFolder = "Right";
                }
                else if (aspd > 0)
                {
                    meterPerDegree.Add(lspd / aspd);
                    turnOrientation.Add(ETurnOrientation.Left);
                    orientationFolder = "Left";
                }
                else
                {
                    meterPerDegree.Add(0);
                    turnOrientation.Add(ETurnOrientation.Straight);
                    orientationFolder = "Straight";
                }
            }
            writer_lspd.WriteLine(lspd.ToString());
        }

        writer_lspd.Close();

        foreach (float mpd in meterPerDegree)
            circumference.Add(mpd * 360);

        writer_radius = new StreamWriter(new FileStream(OutputFolder + "/radiuses.csv", FileMode.Create, FileAccess.Write));

        foreach (float circ in circumference)
        {
            float r = circ / (2 * Mathf.PI);
            writer_radius.WriteLine(r.ToString());
            radius.Add(Mathf.Abs(r));
        }

        writer_radius.Close();

        Debug.Log("Angular speeds: " + returnStringArray(AngularSpeed));
        Debug.Log("Linear speeds: " + returnStringArray(LinearSpeed));

        radius_idx = lspd_idx * AngularSpeed.Count() + aspd_idx;
        Simulate(LinearSpeed[lspd_idx], radius[radius_idx], turnOrientation[radius_idx]);
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
    void Simulate(float lspd, float r, ETurnOrientation orientation)
    {
        if (Application.isPlaying)
        {
            if (start)
            {
                testSubject.GetComponent<MxMTrajectoryGenerator_Trajectory_Insertion>().AnimationDataset = m_animationDataset;
                testSubject.GetComponent<MxMTrajectoryGenerator_Trajectory_Insertion>().DesiredRadius = Mathf.Abs(r);
                testSubject.GetComponent<MxMTrajectoryGenerator_Trajectory_Insertion>().DesiredLinearSpeed = lspd;
                testSubject.GetComponent<MxMTrajectoryGenerator_Trajectory_Insertion>().DesiredTurnOrientation = orientation;

                if (orientation == ETurnOrientation.Left)
                    orientationFolder = "Left";
                else if (orientation == ETurnOrientation.Right)
                    orientationFolder = "Right";
                else
                    orientationFolder = "Straight";


                VideoRecorder.GetComponent<RecordScene>().OutputFolder = OutputFolder + '/' + lspd.ToString() + '/' + orientationFolder + '/';
                VideoRecorder.GetComponent<RecordScene>().FileName = r.ToString() + "_recording";

                currentTestSubject = Instantiate(testSubject);
                currentRecorder = Instantiate(VideoRecorder);

                if (currentTestSubject != null)
                    Debug.Log("Simulating a character with parameters: Linear Speed = " + lspd.ToString() + " Angular Speed = " + AngularSpeed[aspd_idx] + " and Radius = " + r.ToString());
            }
        }
        else
        {
            Debug.LogError("The simulation won't start. Make sure to start the simulaiton with the scene playing!");
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
                    Simulate(LinearSpeed[lspd_idx], radius[radius_idx], turnOrientation[radius_idx]);
                }
                else
                {
                    if (aspd_idx < (AngularSpeed.Count - 1))
                    {
                        lspd_idx = 0;
                        aspd_idx += 1;
                        radius_idx = lspd_idx * AngularSpeed.Count() + aspd_idx;
                        Simulate(LinearSpeed[lspd_idx], radius[radius_idx], turnOrientation[radius_idx]);
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
