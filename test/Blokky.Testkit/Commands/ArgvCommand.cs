using Blokky.Testkit.Interop;

namespace Blokky.Testkit.Commands;

/// <summary><c>argv</c> (TK-12): the received arguments as a JSON array; no options are interpreted.</summary>
internal static class ArgvCommand
{
    public static unsafe int Run(IReadOnlyList<Arg> args)
    {
        if (args.Count > 0 && args[0].Text == "--raw")
        {
            if (!OperatingSystem.IsWindows())
            {
                throw TestkitException.NotSupported("argv --raw");
            }

            var raw = new string(Kernel32.GetCommandLineW());
            StdStream.Out.Write(Json.Line(w => w.WriteStringValue(raw)));
            return 0;
        }

        StdStream.Out.Write(Json.Line(w => Json.StringArray(w, args.Select(a => a.Text))));
        return 0;
    }
}
