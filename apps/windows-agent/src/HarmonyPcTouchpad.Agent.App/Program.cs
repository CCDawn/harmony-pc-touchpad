using HarmonyPcTouchpad.Agent.Windows;

namespace HarmonyPcTouchpad.Agent.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var context = new AgentApplicationContext(
            new WindowsInputSink(new NativeWindowsInputApi()));
        Application.Run(context);
    }
}
