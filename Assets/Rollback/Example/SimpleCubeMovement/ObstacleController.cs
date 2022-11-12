using System.Collections.Generic;
using FishNet.Connection;
using Riten.Authorative;
using Riten.Rollback;
using Riten.Serialized;
using UnityEngine;

[System.Serializable]
public struct ObstacleState
{
    public SVector3 Position;
    
    public SQuaternion Rotation;

    public double Time;

    public SVector3 StartingPos;
}

public class ObstacleController : NetworkedController, IAuthoritative<ObstacleState>
{
    public Dictionary<NetworkConnection, History<ObstacleState>> StateHistories { get; set; }

    public History<ObstacleState> StateHistory { get; set; }

    [SerializeField] float m_rotationSpeed = 45f;

    [SerializeField] float m_movementRange = 1f;

    protected void Awake()
    {
        Initialize(this);

        m_startingPos = transform.position;
    }

    public void ApplyState(ObstacleState state)
    {
        transform.position = state.Position;
        transform.rotation = state.Rotation;
        m_timer = state.Time;
        m_startingPos = state.StartingPos;
    }

    public ObstacleState GatherCurrentState()
    {
        return new ObstacleState{
            Position = transform.position,
            Rotation = transform.rotation,
            Time = m_timer,
            StartingPos = m_startingPos
        };
    }

    public bool HasError(ObstacleState stateA, ObstacleState stateB)
    {
        float posErrorInUnits = Vector3.Distance(stateA.Position, stateB.Position);

        if (posErrorInUnits > 0.02f) 
        {
            return true;
        }

        float rotErrorInDegrees = Quaternion.Angle(stateA.Rotation, stateB.Rotation);

        if (rotErrorInDegrees > 0.5f)
        {
            return true;
        }

        return false;
    }

    double m_timer = 0;

    Vector3 m_startingPos;

    public void Simulate(double delta, bool replay)
    {
        transform.rotation *= Quaternion.Euler(0, 0, m_rotationSpeed * (float)delta);

        m_timer += delta;

        transform.position = m_startingPos + Vector3.up * (float)System.Math.Sin(m_timer) * m_movementRange;
    }
}
