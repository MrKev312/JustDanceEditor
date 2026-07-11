using System;
using System.Diagnostics;

namespace JustDanceEditor.Editor.Services;

internal static class EditorLog
{
    public static void Unexpected(Exception exception, string operation)
        => Trace.TraceError("{0} failed: {1}", operation, exception);

    public static void Fallback(Exception exception, string operation)
        => Trace.TraceWarning("{0} fell back: {1}", operation, exception);
}
