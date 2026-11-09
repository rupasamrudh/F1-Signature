using UnityEngine;

public class MarkerField : MonoBehaviour
{
    public int count = 400;
    public float range = 1500f;

    void Start()
    {
        Random.InitState(1);
        for (int i = 0; i < count; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(c.GetComponent<Collider>());
            c.transform.SetParent(transform);
            c.transform.position = new Vector3(Random.Range(-range, range), 0.5f, Random.Range(-range, range));
        }
    }
}