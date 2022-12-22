using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Recorder;
using UnityEditor;

public class RecordScene : MonoBehaviour
{
    public string OutputFolder;
    public string FileName;
    RecorderWindow recorderWindow;
    private RecorderWindow GetRecorderWindow()
    {
        return (RecorderWindow)EditorWindow.GetWindow(typeof(RecorderWindow));
    }
    public void FinishRecording()
    {
        if (recorderWindow.IsRecording())
            recorderWindow.StopRecording();

        recorderWindow.Close();
    }

    // Start is called before the first frame update
    void Start()
    {
        recorderWindow = GetRecorderWindow();

        RecorderControllerSettings a = new RecorderControllerSettings();
        a.SetRecordModeToManual();
        
        MovieRecorderSettings mr = new MovieRecorderSettings();
        mr.OutputFile = OutputFolder + FileName;
        mr.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        mr.VideoBitRateMode = VideoBitrateMode.High;

        ImageInputSelector c = new ImageInputSelector();
        c.cameraInputSettings.Source = ImageSource.TaggedCamera;
        c.cameraInputSettings.CameraTag = "Recording";
        c.cameraInputSettings.OutputHeight = 1080;
        c.cameraInputSettings.OutputWidth = 1920;

        mr.ImageInputSettings = c.cameraInputSettings;
        mr.RecordMode = RecordMode.Manual;
        a.AddRecorderSettings(mr);

        recorderWindow.SetRecorderControllerSettings(a);

        recorderWindow.StartRecording();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
