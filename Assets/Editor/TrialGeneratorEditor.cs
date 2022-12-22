using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TrialGenerator))]
class TrialGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("Start"))
        {
            Debug.Log("Starting simulation...");
            ((MonoBehaviour)target).GetComponent<TrialGenerator>().StartSimulation();
        }
    }
}