using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Globalization;

public class FlowField : MonoBehaviour
{
    // Publics

    [Header("Field configurations")]
    public GameObject ArrowObject;
    public Gradient gradient;
    public bool ShowField = true;
    public bool ShowTrajectory = true;
    public float PowerParameter = 1.9f;
    float MaxSpeed = -9999999999;
    float MinimumSpeed = 9999999999;

    [Header("CSV file")]
    public string CSVFileFolder;

    // Privates
    private bool _showField = true, _showTrajectory = true;

    // When using local positions, a plane will always be 10x10
    private readonly float LENGTH_PLANE = 10.0f, WIDTH_PLANE = 10.0f, OFFSET = -5f, STEP = 0.5f;

    // General

    List<float> arrow_angles = new List<float>();
    List<float> arrow_speeds = new List<float>();
    List<GameObject> arrows = new List<GameObject>();
    List<List<GameObject>> trajectories = new List<List<GameObject>>();
    GameObject trajectories_parent;


    void CheckBools()
    {
        if (_showField != ShowField)
        {
            _showField = ShowField;
            foreach (GameObject arrow in arrows)
            {
                arrow.SetActive(_showField);
            }
        }

        if (_showTrajectory != ShowTrajectory)
        {
            _showTrajectory = ShowTrajectory;
            foreach (List<GameObject> trajectory in trajectories)
            {
                foreach (GameObject point in trajectory)
                {
                    point.SetActive(_showTrajectory);
                }
            }
        }
    }

    Color ColorFromGradient(float value)  // float between 0-1
    {
        return gradient.Evaluate(value);
    }

    public float GetArrowSpeed(int idx)
    {
        return arrow_speeds[idx];
    }

    public float GetArrowAngle(int idx)
    {
        return arrow_angles[idx];
    }

    public GameObject SearchNearestArrow(Vector3 pos)
    {
        GameObject nearest = arrows[0];
        double distance = 999999999;

        foreach(GameObject arrow in arrows)
        {
            double temp = Vector3.Distance(pos, arrow.transform.position);
            if (temp < distance)
            {
                distance = temp;
                nearest = arrow;
            }
        }

        return nearest;
    }

    // For some reason I have to shift the angles when rotating. I still don't understand why this works
    // and I think I should do it differently, but for the moment I just want to test one thing
    void ApplyRotationToArrow(float angle, GameObject arrow)
    {
        arrow.transform.rotation = Quaternion.Euler(180, 90 - angle, 0);
    }

    double ConvertToAngle(double x, double y)
    {
        return Math.Atan2(y, x) * Mathf.Rad2Deg;
    }


    void SetMinMaxSpeed(List<Vector3> speed)
    {
        int max = speed.Count;

        for (int i = 0; i < max; i++)
        {
            float mag = speed[i].magnitude;
            if (mag > MaxSpeed)
                MaxSpeed = mag;

            if (mag < MinimumSpeed)
                MinimumSpeed = mag;
        }
    }

    void Propagate(float k, List<Vector3> speed, List<Vector3> trajectory)
    {
        float max_speed_dif = MaxSpeed - MinimumSpeed;
        foreach (GameObject arrow_outer in arrows)
        {
            Vector3 pos = arrow_outer.transform.position;
            Vector3 num = new Vector3(0, 0, 0);
            float denom = 0;

            for (int i = 0; i < speed.Count; i++)
            {
                float distance = Vector3.Distance(pos, trajectory[i]);

                float w = Mathf.Pow(1 / distance, k);

                denom += w;

                // VecPos are the points in the trajectory
                // ControlVectors are the velocity

                num += w * speed[i];
            }

            float speed_dif = num.magnitude - MinimumSpeed;

            arrow_speeds.Add(num.magnitude);

            float speed_proportion = speed_dif / max_speed_dif;

            Vector3 result = num / denom;

            float angle = (float)ConvertToAngle(result.x, result.z);
            ApplyRotationToArrow(angle, arrow_outer);

            arrow_angles.Add(angle);

            arrow_outer.GetComponent<Renderer>().material.color = ColorFromGradient(speed_proportion);
        }
    }

    void Start()
    {
        if (string.IsNullOrEmpty(CSVFileFolder))
        {
            throw new Exception("There are no CSV files to use. (Set the CSV File Folder variable in the FlowField component)");
        }

        trajectories_parent = GameObject.Find("Trajectories");

        // Generation of the flow field objects

        int counter = 0;

        for (float i = STEP; i < LENGTH_PLANE; i = i + STEP)
        {
            for (float j = STEP; j < WIDTH_PLANE; j = j + STEP)
            {
                GameObject arrow = Instantiate(ArrowObject, gameObject.transform);
                arrow.transform.localPosition = new Vector3(OFFSET + i, STEP / 2.0f, OFFSET + j);
                arrow.name = counter.ToString();
                arrow.tag = "Arrow";
                arrows.Add(arrow);
                counter++;
            }
        }

        // Configuration of the flow field through a CSV file from UMANS
        StreamReader reader;
        string line;
        string[] items;
        double Vx = 0, Vy = 0, old_time, new_time;
        double[] coords = new double[2], old_coords = new double[2];
        List<Vector3> mainline_speed = new List<Vector3>(), trajectory_points = new List<Vector3>();

        string[] filePaths = Directory.GetFiles(CSVFileFolder, "*.csv", SearchOption.TopDirectoryOnly);

        foreach (string file in filePaths)
        {
            reader = new StreamReader(file);
            line = reader.ReadLine();
            items = line.Split(',');

            List<GameObject> trajectory = new List<GameObject>();

            float k = 0;

            while (!reader.EndOfStream)
            {
                // Get trajectory points

                old_coords[0] = Convert.ToDouble(items[1], CultureInfo.InvariantCulture);
                old_coords[1] = Convert.ToDouble(items[2], CultureInfo.InvariantCulture);
                old_time = Convert.ToDouble(items[0], CultureInfo.InvariantCulture);

                line = reader.ReadLine();
                items = line.Split(',');

                coords[0] = Convert.ToDouble(items[1], CultureInfo.InvariantCulture);
                coords[1] = Convert.ToDouble(items[2], CultureInfo.InvariantCulture);
                new_time = Convert.ToDouble(items[0], CultureInfo.InvariantCulture);

                double dist_x = coords[0] - old_coords[0];
                double dist_y = coords[1] - old_coords[1];
                double time = new_time - old_time;
                double angle = ConvertToAngle(dist_x, dist_y);

                Vector3 pos = new Vector3((float)coords[0], 0.25f, (float)coords[1]);
                GameObject arrow = SearchNearestArrow(pos);

                ApplyRotationToArrow((float)angle, arrow);
                arrow.GetComponent<Renderer>().material.color = ColorFromGradient(k);

                GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cylinder.transform.position = pos;
                cylinder.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
                cylinder.transform.SetParent(trajectories_parent.transform);
                trajectory.Add(cylinder);

                // Calculate velocity

                Vx = (dist_x) / time;
                Vy = (dist_y) / time;
                k += 0.005f;

                // Update input vector
                mainline_speed.Add(new Vector3((float)Vx, 0, (float)Vy));
                trajectory_points.Add(pos);
            }
            reader.Close();
            trajectory[0].GetComponent<Renderer>().material.color = Color.green;
            trajectory[trajectory.Count - 1].GetComponent<Renderer>().material.color = Color.red;

            SetMinMaxSpeed(mainline_speed);

            Propagate(PowerParameter, mainline_speed, trajectory_points);
        }        
    }

    // Update is called once per frame
    void Update()
    {
        CheckBools();
    }
}
