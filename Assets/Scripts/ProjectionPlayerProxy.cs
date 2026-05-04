using UnityEngine;

public class ProjectionPlayerProxy : MonoBehaviour
{
    public PlayerController owner;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (owner != null)
            owner.HandleProjectionTriggerEnter(other);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (owner != null)
            owner.HandleProjectionTriggerExit(other);
    }
}
