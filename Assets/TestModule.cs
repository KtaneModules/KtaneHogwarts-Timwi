using UnityEngine;

public class TestModule : MonoBehaviour
{
    public KMSelectable Button;
    public KMBombModule Module;

    void Start()
    {
        Button.OnInteract += delegate
        {
            Module.HandlePass();
            return false;
        };
    }
}
