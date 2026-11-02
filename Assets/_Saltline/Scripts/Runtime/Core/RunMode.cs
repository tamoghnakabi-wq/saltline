using UnityEngine;

namespace Saltline.Core
{
    /// <summary>
    /// Whether this process draws anything. Headless test players skip all presentation work;
    /// <c>-capture</c> keeps presentation on in batch mode so frames can be rendered to images.
    /// </summary>
    public static class RunMode
    {
        static int s_Presentation = -1;

        public static bool Presentation
        {
            get
            {
                if (s_Presentation < 0)
                    s_Presentation = !Application.isBatchMode || (CommandLine.Has("capture") && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) ? 1 : 0;
                return s_Presentation == 1;
            }
        }
    }
}
