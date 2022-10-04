using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    void Update()
    {
        Camera cam = Camera.main;

        float speed = 20;

        Vector3 moveDelta = cam.transform.forward * Input.GetAxis("Vertical") + cam.transform.right * Input.GetAxis("Horizontal");

        cam.transform.position += moveDelta * speed * Time.deltaTime;

        if (Input.GetMouseButton(1)) {
            float sensitivity = 7;

            cam.transform.Rotate(new Vector3(-Input.GetAxis("Mouse Y"), 0, 0) * sensitivity, Space.Self);
            cam.transform.Rotate(new Vector3(0, Input.GetAxis("Mouse X"), 0) * sensitivity, Space.World);
        }
    }
}
