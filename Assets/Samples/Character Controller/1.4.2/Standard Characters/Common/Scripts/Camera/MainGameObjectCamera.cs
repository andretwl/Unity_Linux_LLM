using UnityEngine;

public class MainGameObjectCamera : MonoBehaviour
{
    public static Camera Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic()
    {
        Instance = null;
    }

    void Awake()
    {
        Instance = GetComponent<Camera>();
    }
}
