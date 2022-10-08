using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PrefixSumTest : MonoBehaviour
{
    public ComputeShader computeShader;

    void Start()
    {
        // Prepare data.
        int[] arr = new int[1024*1024];

        for (int i = 0; i < arr.Length; i++) {
            arr[i] = Random.Range(0, 1024);
        }

        ComputeBuffer buffer = new ComputeBuffer(arr.Length, 4);
        buffer.SetData(arr);

        ComputeBuffer groupBuffer = new ComputeBuffer(arr.Length / 1024, 4);
        groupBuffer.SetData(new int[arr.Length / 1024]);

        for (int i = 0; i < 3; i++) {
            computeShader.SetBuffer(i, "arr", buffer);
            computeShader.SetBuffer(i, "groupArr", groupBuffer);
        }

        int[] result = new int[arr.Length];
        int[] gpuResult = new int[arr.Length];

        double startTime;

        Debug.Log("Prefix sum benchmark:");

        // CPU
        startTime = Time.realtimeSinceStartupAsDouble;

        result[0] = arr[0];
        for (int i = 1; i < arr.Length; i++) {
            result[i] = arr[i] + result[i-1];
        }

        Debug.Log("CPU: " + Mathf.RoundToInt((float)((Time.realtimeSinceStartupAsDouble - startTime) * 1000)) + "ms.");

        // GPU
        startTime = Time.realtimeSinceStartup;

        computeShader.Dispatch(0, 1024, 1, 1);
        computeShader.Dispatch(1, 1, 1, 1);
        computeShader.Dispatch(2, 1024, 1, 1);

        buffer.GetData(gpuResult);

        Debug.Log("GPU: " + Mathf.RoundToInt((float)((Time.realtimeSinceStartupAsDouble - startTime) * 1000)) + "ms.");

        // Compare results.
        bool pass = true;
        for (int i = 0; i < arr.Length; i++) {
            if (result[i] != gpuResult[i]) {
                pass = false;
                // Debug.Log($"Index {i} : {result[i]} != {gpuResult[i]}");
            }
        }

        Debug.Log("Equal: " + pass);

        buffer.Dispose();
        groupBuffer.Dispose();
    }
}
