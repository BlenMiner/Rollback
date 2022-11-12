using UnityEngine;

namespace Riten.Serialized
{
    [System.Serializable]
    public struct SVector3
    {
        public float x, y, z;

        public SVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static implicit operator Vector3(SVector3 q)
        {
            return new Vector3(q.x, q.y);
        }

        public static implicit operator SVector3(Vector3 q)
        {
            return new SVector3(q.x, q.y, q.z);
        }
    }
}
