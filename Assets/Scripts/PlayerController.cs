using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public FezCameraController cameraController;

    public float moveSpeed = 5f;
    public float gravity = -20f;
    public float jumpHeight = 1.5f;

    public float jumpBufferTime = 0.12f;
    public float coyoteTime = 0.12f;

    private CharacterController controller;
    private Vector3 velocity;

    private float jumpBufferCounter;
    private float coyoteCounter;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        Move();
    }

    void Move()
    {
        if (cameraController == null ||
            cameraController.IsRotating ||
            cameraController.IsSwitchingView ||
            cameraController.IsFirstPerson)
            return;

        Vector3 right = cameraController.GetRight();

        float inputX = Input.GetAxisRaw("Horizontal");
        Vector3 move = right * inputX;

        controller.Move(move * moveSpeed * Time.deltaTime);

        if (controller.isGrounded)
        {
            coyoteCounter = coyoteTime;

            if (velocity.y < 0)
                velocity.y = -2f;
        }
        else
        {
            coyoteCounter -= Time.deltaTime;
        }

        if (Input.GetButtonDown("Jump"))
        {
            jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            jumpBufferCounter -= Time.deltaTime;
        }

        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
        }

        velocity.y += gravity * Time.deltaTime;

        controller.Move(velocity * Time.deltaTime);
    }
}
