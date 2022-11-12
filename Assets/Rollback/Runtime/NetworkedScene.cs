using System.Collections.Generic;

namespace Riten.Authorative
{
    public static class NetworkedScene
    {
        static List<IRollback> ActiveControllers = new ();

        public static void RegisterController(IRollback controller)
        {
            ActiveControllers.Add(controller);
        }

        public static void UnregisterController(IRollback controller)
        {
            ActiveControllers.Remove(controller);
        }

        public static void RollbackEveryoneExcept(IRollback except, ulong tick)
        {
            foreach(var c in ActiveControllers)
            {
                if (c == except) 
                    continue;

                c.RollbackTo(tick);
            }
        }

        public static void RollbackEveryone(ulong tick)
        {
            foreach(var c in ActiveControllers)
            {
                c.RollbackTo(tick);
            }
        }

        public static void ResetEveryone()
        {
            foreach(var c in ActiveControllers)
            {
                c.ResetState();
            }
        }
    }
}
