using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

public class RacingLine : MonoBehaviour
{
    GameObject agent;
    GameObject[] cylinders;
    Vector3 previous_position, new_position;
    public Gradient gradient;

    float[] Range(float start, float end, float step)
    {
        int n_elem = (int)(end / step);
        float[] values = new float[n_elem];

        values[0] = start;

        for (int i = 1; i < n_elem; i++)
            values[i] = values[i - 1] + step;

        return values;
    }

    // Start is called before the first frame update
    void Start()
    {
        float resolution = 0.05f;
        float pi_arc = (float)(2 * Math.PI);// 4.0);
        int n_elem = (int)(pi_arc / resolution);
        float[] x = Range(0, pi_arc, resolution);
        float[] z = new float[n_elem];
        cylinders = new GameObject[n_elem];

        agent = GameObject.Find("Robot Kyle");
        previous_position = agent.transform.position;

        for (int i = 0; i < n_elem; i++)
        {
            z[i] = (float)Math.Sin(x[i]);
            x[i] = x[i] * 4.0f;
            cylinders[i] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinders[i].transform.position = new Vector3(previous_position.x + z[i], 0.0f, previous_position.z + x[i]);
            cylinders[i].transform.localScale = new Vector3(0.25f, 1.0f, 0.25f);
        }
      
    }

    Color ColorFromGradient(float value)  // float between 0-1
    {
        return gradient.Evaluate(value);
    }

    void updateColors(float speed)
    {
        for (int i = 0; i < cylinders.Length; i++)
        {
            cylinders[i].GetComponent<Renderer>().material.color = ColorFromGradient(speed/6.2f);
        }
    }

    // Update is called once per frame
    void Update()
    {
        new_position = agent.transform.position;
        Vector3 dP = new_position - previous_position;
        Vector3 speed_vector = dP / Time.deltaTime;
        float vx = speed_vector.x, vz = speed_vector.z;
        previous_position = new_position;

        float speed = (float)Math.Sqrt(Math.Pow(vx, 2) + Math.Pow(vz, 2));

        updateColors(speed);
    }
}
