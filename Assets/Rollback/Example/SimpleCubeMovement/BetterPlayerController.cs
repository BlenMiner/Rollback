using UnityEngine;

[System.Serializable]
public struct BPlayerInput
{
    public int MoveInputX;

    public int MoveInputY;

    public Vector2Int MoveInput => new  (MoveInputX, MoveInputY);
}

[System.Serializable]
public struct BPlayerState
{
    public float PosX, PosY, PosZ;

    public Vector3 Position
    {
        get => new Vector3(PosX, PosY, PosZ);
        set
        {
            PosX = value.x;
            PosY = value.y;
            PosZ = value.z;
        }
    }
}

public class BetterPlayerController : AuthoritativeController<BPlayerInput, BPlayerState>
{
    public override BPlayerInput GatherCurrentInput()
    {
        int horizontal = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
        int vertical = (Input.GetKey(KeyCode.Z) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);

        return new BPlayerInput
        {
            MoveInputX = horizontal,
            MoveInputY = vertical
        };
    }

    public override BPlayerState GatherCurrentState()
    {
        return new BPlayerState
        {
            Position = transform.position
        };
    }

    public override bool HasError(BPlayerState stateA, BPlayerState stateB)
    {
        return Vector3.Distance(stateA.Position, stateB.Position) > 0.001f;
    }

    public override void ApplyState(BPlayerState state)
    {
        transform.position = state.Position;
    }

    public override void Simulate(BPlayerInput input, double delta, bool replay)
    {
        const float SPEED = 10f;

        Vector2 moveDir = input.MoveInput;

        moveDir.Normalize();

        var move = moveDir * (float)delta * SPEED;

        // Stupid solution
        TranslateWithCollision(new Vector3(move.x, 0, 0));
        TranslateWithCollision(new Vector3(0, move.y, 0));
    }

    static RaycastHit[] CACHE = new RaycastHit[512];
 
    void TranslateWithCollision(Vector3 move)
    {
        const float SKIN_SIZE = 0.1f;
        const float BOX_SIZE = 1f - SKIN_SIZE;

        int count = Physics.BoxCastNonAlloc(transform.position, new Vector3(BOX_SIZE, BOX_SIZE, BOX_SIZE) * 0.5f, move.normalized, CACHE, transform.rotation, move.magnitude);

        count = Mathf.Min(count, CACHE.Length);

        float moveDistance = move.magnitude;

        for (int i = 0; i < count; ++i)
        {
            var hit = CACHE[i];

            if (hit.distance < moveDistance && hit.collider.gameObject != gameObject)
            {
                moveDistance = hit.distance;
            }
        }

        transform.position += move.normalized * (moveDistance - SKIN_SIZE * 0.5f);
    }
}
