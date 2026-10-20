using System.Windows;

namespace PCLPlus
{
    public partial class App : Application
    {
        static App()
        {
            // Must run before any type that references an embedded dependency is loaded.
            EmbeddedAssemblyLoader.Register();
        }
    }
}
