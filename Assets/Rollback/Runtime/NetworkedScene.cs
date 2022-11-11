using System.Collections.Generic;

namespace Riten.Authorative
{
    public static class NetworkedScene
    {
        static List<NetworkedController> ActiveControllers = new ();

        public static void RegisterController(NetworkedController controller)
        {
            ActiveControllers.Add(controller);
        }

        public static void UnregisterController(NetworkedController controller)
        {
            ActiveControllers.Remove(controller);
        }
    }
}
