using UnityEngine;

public class ChaseCamera : MonoBehaviour
{
    public Transform target;
    public CarController car;
    public Vector3 offset = new Vector3(0f, 2.5f, -9f);
    public float posLag = 8f, rotLag = 6f;
    public float fovBase = 60f, fovMax = 78f;

    void LateUpdate()
    {
        if (!target) return;
        float dt = Time.deltaTime;
        Vector3 desired = target.TransformPoint(offset);
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-posLag * dt));

        Vector3 look = target.position + target.forward * 4f + Vector3.up * 0.8f;
        Quaternion want = Quaternion.LookRotation(look - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-rotLag * dt));

        if (car) GetComponent<Camera>().fieldOfView =
            Mathf.Lerp(fovBase, fovMax, Mathf.Clamp01(car.SpeedKmh / 350f));
    }
}