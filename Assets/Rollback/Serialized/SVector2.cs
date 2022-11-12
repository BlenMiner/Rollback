using UnityEngine;

namespace Riten.Serialized
{
    [System.Serializable]
    public struct SVector2
    {
        public float x, y;

        public SVector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static implicit operator Vector2(SVector2 q)
        {
            return new Vector2(q.x, q.y);
        }

        public static implicit operator SVector2(Vector2 q)
        {
            return new SVector2(q.x, q.y);
        }
    }
}