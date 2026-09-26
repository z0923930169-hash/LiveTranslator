using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LiveTranslator;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        NativeRuntimeBootstrap.Configure();

        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
            return SmokeTests.RunNativeSmoke();

        if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
            return SmokeTests.RunUiSmoke();

        if (args.Contains("--engine-smoke-test", StringComparer.OrdinalIgnoreCase))
            return SmokeTests.RunEngineSmokeAsync().GetAwaiter().GetResult();

        Application.Run(new MainForm());
        return 0;
    }
}
