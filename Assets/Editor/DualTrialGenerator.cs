using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(DualTrialGenerator))]
class DualTrialGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("Start"))
        {
            Debug.Log("Starting simulation...");
            ((MonoBehaviour)target).GetComponent<DualTrialGenerator>().StartSimulation();
        }
    }
}