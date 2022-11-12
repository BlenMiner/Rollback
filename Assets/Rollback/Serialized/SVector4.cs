using UnityEngine;

namespace Riten.Serialized
{
    [System.Serializable]
    public struct SVector4
    {
        public float x, y, z, w;

        public SVector4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static implicit operator Vector4(SVector4 q)
        {
            return new Vector4(q.x, q.y, q.z, q.w);
        }

        public static implicit operator Quaternion(SVector4 q)
        {
            return new Quaternion(q.x, q.y, q.z, q.w);
        }

        public static implicit operator SVector4(Quaternion q)
        {
            return new SVector4(q.x, q.y, q.z, q.w);
        }

        public static implicit operator SVector4(Vector4 q)
        {
            return new SVector4(q.x, q.y, q.z, q.w);
        }
    }
}