using System.Collections.Generic;
using UnityEngine;
using MxM;
using System;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using System.IO;
#endif

namespace MxMEditor
{
    //============================================================================================
    /**
    *  @brief 
    *         
    *********************************************************************************************/
    [CreateAssetMenu(fileName = "MxMExtractMocapCoverageStats", menuName = "MxM/Core/Extract coverage stats", order = 1)]
    [CustomEditor(typeof(MxMExtractMocapCoverageStats))]
    public class MxMExtractMocapCoverageStats : Editor
    {
        [HideInInspector] public List<AnimationClip> Animations = new List<AnimationClip>();
        private string OutputFolder;

        private ReorderableList anim_list;

        private void OnEnable()
        {
            anim_list = new ReorderableList(Animations, typeof(List<AnimationClip>), true, true, true, true);
        }

        public void EvaluateCurve(ref List<float> values, AnimationCurve curve, float total_time, float dT)
        {
            float acc = 0;

            while (acc <= total_time)
            {
                values.Add(curve.Evaluate(acc));
                acc += dT;
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            DropAreaGUI();

            // will enable the default inpector UI

            // implement your UI code here

            OutputFolder = EditorGUILayout.TextField("Output folder", OutputFolder);

            if (GUILayout.Button("Extract coverage stats"))
            {
                // Process animations logic
                if (Animations.Count > 0)
                {
                    List<List<Vector3>> all_trajectories = new List<List<Vector3>>(), all_rotations = new List<List<Vector3>>();
                    List<string> animation_names = new List<string>();
                    List<int> animation_total_frames = new List<int>();
                    List<float> deltas = new List<float>();
                    foreach (AnimationClip animation in Animations)
                    {
                        List<Vector3> trajectory = new List<Vector3>(), rotation = new List<Vector3>();
                        List<float> x = new List<float>(), y = new List<float>(), z = new List<float>();
                        List<float> rx = new List<float>(), ry = new List<float>(), rz = new List<float>(), rw = new List<float>();
                        float total_frames = animation.length * animation.frameRate;
                        Debug.Log("Total frames: " + total_frames);
                        EditorCurveBinding[] all_bindings = AnimationUtility.GetCurveBindings(animation);
                        foreach (EditorCurveBinding binding in all_bindings)
                        {
                            switch (binding.propertyName)
                            {
                                case "RootT.x":
                                    {
                                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animation, binding);
                                        EvaluateCurve(ref x, curve, animation.length, animation.length / total_frames);
                                    }
                                    break;
                                case "RootT.y":
                                    {
                                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animation, binding);
                                        EvaluateCurve(ref y, curve, animation.length, animation.length / total_frames);
                                    }
                                    break;
                                case "RootT.z":
                                    {
                                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animation, binding);
                                        EvaluateCurve(ref z, curve, animation.length, animation.length / total_frames);
                                    }
                                    break;
                                case "RootQ.x":
                                    {
                                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animation, binding);
                                        EvaluateCurve(ref rx, curve, animation.length, animation.length / total_frames);
                                    }
                                    break;
                                case "RootQ.y":
                                    {
                                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animation, binding);
                                        EvaluateCurve(ref ry, curve, animation.length, animation.length / total_frames);
                                    }
                                    break;
                                case "RootQ.z":
                                    {
                                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animation, binding);
                                        EvaluateCurve(ref rz, curve, animation.length, animation.length / total_frames);
                                    }
                                    break;
                                case "RootQ.w":
                                    {
                                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animation, binding);
                                        EvaluateCurve(ref rw, curve, animation.length, animation.length / total_frames);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }

                        // I need to get the positions to calculate speed and turning angle.                        
                        for (int i = 0; i < x.Count; i++)
                        {
                            trajectory.Add(new Vector3(x[i], y[i], z[i]));
                            rotation.Add(new Quaternion(rx[i], ry[i], rz[i], rw[i]).eulerAngles);
                        }
                        animation_names.Add(animation.name);
                        animation_total_frames.Add((int)Math.Round(total_frames, 0));
                        all_trajectories.Add(trajectory);
                        all_rotations.Add(rotation);
                        deltas.Add(animation.length / total_frames);
                    }
                    SaveData(animation_names, all_trajectories, all_rotations, deltas, animation_total_frames);
                }
                else
                {
                    Debug.LogError("Animations vector is empty!");
                }

            }

            if (GUILayout.Button("Clear animation vector"))
            {
                Animations.Clear();
            }
        }

        private void SaveData(List<string> names, List<List<Vector3>> all_trajectories, List<List<Vector3>> all_rotations, List<float> deltas, List<int> total_frames)
        {
            if (OutputFolder == "")
                Debug.LogError("Null path in the Output folder field.");
            else
            {
                StreamWriter writer_anim = new StreamWriter(new FileStream(OutputFolder + '/' + "animation_dataset.csv", FileMode.Create, FileAccess.Write));
                StreamWriter writer_names = new StreamWriter(new FileStream(OutputFolder + '/' + "animation_clips.csv", FileMode.Create, FileAccess.Write));

                for (int i = 0; i < all_trajectories.Count; i++)
                {
                    writer_names.WriteLine(names[i] + ',' + total_frames[i]);
                    float time = 0;
                    for (int j = 0; j < all_trajectories[i].Count; j++)
                    {
                        Vector3 p = all_trajectories[i][j];
                        Vector3 r = all_rotations[i][j];

                        writer_anim.WriteLine(time.ToString() + ',' + p.x.ToString() + ',' + p.y.ToString() + ',' + p.z.ToString() + ',' + r.x.ToString() + ',' + r.y.ToString() + ',' + r.z.ToString());

                        time += deltas[i];
                    }
                }

                writer_anim.Close();
                writer_names.Close();

                Debug.Log("Animation dataset successfully created at " + OutputFolder);
            }
        }


        public void DropAreaGUI()
        {
            anim_list.DoLayoutList();

            Event evt = Event.current;
            Rect drop_area = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(drop_area, "Drag and drop animations here");

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!drop_area.Contains(evt.mousePosition))
                        return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (string path in DragAndDrop.paths)
                        {
                            AnimationClip anim = AssetDatabase.LoadAssetAtPath(path, typeof(AnimationClip)) as AnimationClip;
                            if (anim != null)
                            {
                                Debug.Log(anim.name);
                                Animations.Add(anim);
                                Debug.Log(Animations.Count);
                            }
                            else
                            {
                                Debug.LogError("Problem loading asset at: " + path);
                            }
                        }
                    }
                    break;
            }
        }
    }
}