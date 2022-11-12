using UnityEngine;

namespace Riten.Serialized
{
    [System.Serializable]
    public struct SQuaternion
    {
        public float x, y, z, w;

        public SQuaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static implicit operator Quaternion(SQuaternion q)
        {
            return new Quaternion(q.x, q.y, q.z, q.w);
        }

        public static implicit operator SQuaternion(Quaternion q)
        {
            return new SQuaternion(q.x, q.y, q.z, q.w);
        }
    }
}
